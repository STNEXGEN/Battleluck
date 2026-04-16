using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Central session manager with toggle-based enter/exit flow.
///
/// Enter flow:
///   1. Player uses .toggleenter OR walks into Zone One
///   2. Ready check passes → snapshot saved → kit applied → teleported → 2-min timer starts
///
/// Exit flow:
///   - .toggleleave: clean exit (kit cleared, exit preset applied, snapshot restored)
///   - Walking out without .toggleleave: burning effect until death
///   - Penalty death: restore old kit/loadout, teleport to penalty spawn (-1000, 0, -500)
/// </summary>
public sealed class SessionController
{
    readonly GameModeRegistry _registry;
    readonly PlayerStateController _playerState;
    readonly FlowController _flow;
    readonly ZoneDetectionSystem _zoneDetection;
    readonly SpawnController _spawner = new();
    readonly AutoTrashController _autoTrash = new();

    public AutoTrashController AutoTrash => _autoTrash;

    // Active sessions: zoneHash → ActiveSession
    readonly Dictionary<int, ActiveSession> _activeSessions = new();
    readonly List<int> _pendingEnd = new();

    // Zone hash → mode ID mapping
    readonly Dictionary<int, string> _zoneModeMap = new();

    // Toggle-enter state tracking
    readonly HashSet<ulong> _readyPlayers = new();           // Players who used .toggleenter (ready to enter)
    readonly HashSet<ulong> _enteredPlayers = new();          // Players inside zone via proper enter flow
    readonly HashSet<ulong> _penaltyBurning = new();          // Players burning from unauthorized exit
    readonly Dictionary<ulong, int> _playerZoneMap = new();   // steamId → zoneHash they entered properly
    readonly HashSet<ulong> _recentlyDied = new();            // Players who recently died — suppress zone-exit detection

    // Penalty spawn point
    static readonly float3 PenaltySpawn = new(-1000f, 0f, -500f);

    // Penalty HP drain per tick (percentage of max HP)
    const float PenaltyDrainPercent = 0.08f;

    // Mode duration (seconds)
    const int ModeDurationSeconds = 120; // 2 minutes

    public SessionController(GameModeRegistry registry, PlayerStateController playerState, FlowController flow, ZoneDetectionSystem zoneDetection)
    {
        _registry = registry;
        _playerState = playerState;
        _flow = flow;
        _zoneDetection = zoneDetection;
    }

    public void Initialize()
    {
        foreach (var modeId in new[] { "gauntlet", "bloodbath", "siege", "trials", "colosseum", "aievent" })
        {
            var config = ConfigLoader.Load(modeId);
            foreach (var zone in config.Zones.Zones)
            {
                _zoneModeMap[zone.Hash] = modeId;
            }
        }

        _zoneDetection.OnPlayerEnterZone += HandlePlayerWalkIntoZone;
        _zoneDetection.OnPlayerExitZone += HandlePlayerWalkOutOfZone;
        DeathHook.OnDeath += HandleDeath;

        _autoTrash.Initialize();
        BattleLuckPlugin.LogInfo($"[SessionController] Initialized with {_zoneModeMap.Count} zone-mode mappings.");
    }

    public void Shutdown()
    {
        _zoneDetection.OnPlayerEnterZone -= HandlePlayerWalkIntoZone;
        _zoneDetection.OnPlayerExitZone -= HandlePlayerWalkOutOfZone;
        DeathHook.OnDeath -= HandleDeath;

        foreach (var session in _activeSessions.Values)
            session.Spawner.DespawnAll();
        _activeSessions.Clear();
        _readyPlayers.Clear();
        _enteredPlayers.Clear();
        _penaltyBurning.Clear();
        _playerZoneMap.Clear();
    }

    // ── Public API for commands ─────────────────────────────────────────

    /// <summary>
    /// Toggle-enter: mark player as ready and execute the enter flow.
    /// If modeId is provided and player is outside, teleport them to the zone first.
    /// </summary>
    public OperationResult ToggleEnter(ulong steamId, Entity playerCharacter, string? modeId = null)
    {
        if (_enteredPlayers.Contains(steamId))
            return OperationResult.Fail("You are already in an active session.");

        int zoneHash = _zoneDetection.GetPlayerZone(steamId);

        // Player is outside any zone — need a mode name to know where to go
        if (zoneHash == 0)
        {
            if (string.IsNullOrEmpty(modeId))
                return OperationResult.Fail("You are not in a zone. Use: .toggleenter <modeName>");

            // Find zone for the requested mode
            var zoneEntry = _zoneModeMap.FirstOrDefault(kv => kv.Value.Equals(modeId, StringComparison.OrdinalIgnoreCase));
            if (zoneEntry.Value == null)
                return OperationResult.Fail($"Unknown mode '{modeId}'. Use .modelist to see available modes.");

            zoneHash = zoneEntry.Key;
            var config = ConfigLoader.Load(modeId);
            var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
            if (zone == null)
                return OperationResult.Fail($"Zone definition not found for mode '{modeId}'.");

            // Teleport player into the zone
            playerCharacter.SetPosition(zone.TeleportSpawn.ToFloat3());
            _zoneDetection.SetPlayerZone(steamId, zoneHash);
        }

        if (!_zoneModeMap.TryGetValue(zoneHash, out var resolvedModeId))
            return OperationResult.Fail("This zone is not mapped to any game mode.");

        return ExecuteEnterFlow(steamId, playerCharacter, zoneHash, resolvedModeId);
    }

    /// <summary>
    /// Toggle-leave: clean exit with kit restore.
    /// Called from .toggleleave command.
    /// </summary>
    public OperationResult ToggleLeave(ulong steamId, Entity playerCharacter)
    {
        if (!_enteredPlayers.Contains(steamId))
            return OperationResult.Fail("You are not in an active session. Use .toggleenter to join.");

        if (!_playerZoneMap.TryGetValue(steamId, out var zoneHash))
            return OperationResult.Fail("Cannot determine your zone.");

        return ExecuteLeaveFlow(steamId, playerCharacter, zoneHash);
    }

    /// <summary>Check if a player is properly entered (for command validation).</summary>
    public bool IsPlayerEntered(ulong steamId) => _enteredPlayers.Contains(steamId);

    /// <summary>Check if a player is burning from penalty.</summary>
    public bool IsPlayerBurning(ulong steamId) => _penaltyBurning.Contains(steamId);

    // ── Core enter/exit flows ───────────────────────────────────────────

    OperationResult ExecuteEnterFlow(ulong steamId, Entity playerCharacter, int zoneHash, string modeId)
    {
        var config = ConfigLoader.Load(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null)
            return OperationResult.Fail($"Zone definition not found for hash {zoneHash}.");

        var rules = config.Session.Rules;
        var session = GetOrCreateSession(zoneHash, modeId);

        // Check player limits
        if (session.Context.Players.Count >= rules.MaxPlayers)
            return OperationResult.Fail("Zone is full.");

        // Execute enter flow: save snapshot → clear kit → apply enter kit → heal → teleport
        var enterResult = _flow.ExecuteEnter(config, playerCharacter, zone, session.Context);
        if (!enterResult.Success)
            return OperationResult.Fail($"Enter failed: {enterResult.Error}");

        // Mark as properly entered
        _enteredPlayers.Add(steamId);
        _playerZoneMap[steamId] = zoneHash;
        _readyPlayers.Remove(steamId);
        _penaltyBurning.Remove(steamId);
        session.Context.Players.Add(steamId);

        // Spawn arena assets exactly once, on first successful player entry.
        if (!session.ArenaInitialized)
        {
            session.ArenaInitialized = true;
            SpawnArenaTiles(session, zone, modeId, config);
            BattleLuckPlugin.LogInfo($"[Session] Arena initialized for {modeId} zone {zone.Hash} on first entry ({steamId}).");
        }

        // Auto-start mode if enough players and not already started
        if (!session.IsStarted && session.Context.Players.Count >= rules.MinPlayers)
        {
            session.IsStarted = true;

            // Set mode duration to 2 minutes
            session.Context.TimeLimitSeconds = ModeDurationSeconds;
            session.Context.StartTimeUtc = DateTime.UtcNow;
            session.Mode?.OnStart(session.Context);

            BattleLuckPlugin.LogInfo($"[Session] Mode {modeId} started with {session.Context.Players.Count} players (2-min timer).");
        }
        else if (session.IsStarted)
        {
            session.Mode?.OnPlayerJoin(session.Context, steamId);
        }

        BattleLuckPlugin.LogInfo($"[Session] Player {steamId} entered zone {zone.Name} ({modeId}) via toggle-enter.");
        return OperationResult.Ok();
    }

    OperationResult ExecuteLeaveFlow(ulong steamId, Entity playerCharacter, int zoneHash)
    {
        if (!_zoneModeMap.TryGetValue(zoneHash, out var modeId))
            return OperationResult.Fail("Zone not mapped.");

        var config = ConfigLoader.Load(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null)
            return OperationResult.Fail("Zone definition not found.");

        if (!_activeSessions.TryGetValue(zoneHash, out var session))
            return OperationResult.Fail("No active session.");

        // Execute exit flow: clear kit → restore snapshot
        var exitResult = _flow.ExecuteExit(config, playerCharacter, zone, session.Context);
        if (!exitResult.Success)
            BattleLuckPlugin.LogError($"[Session] Exit flow failed for {steamId}: {exitResult.Error}");

        // Clean up player tracking
        CleanupPlayerState(steamId, session);

        BattleLuckPlugin.LogInfo($"[Session] Player {steamId} exited zone {zoneHash} ({modeId}) via toggle-leave.");
        return OperationResult.Ok();
    }

    void CleanupPlayerState(ulong steamId, ActiveSession session)
    {
        _enteredPlayers.Remove(steamId);
        _playerZoneMap.Remove(steamId);
        _readyPlayers.Remove(steamId);
        _penaltyBurning.Remove(steamId);
        session.Context.Players.Remove(steamId);
        session.Mode?.OnPlayerLeave(session.Context, steamId);
        FloorLockService.UnlockPlayer(steamId);

        if (session.Context.Players.Count == 0)
            EndSession(session.Context.ZoneHash);
    }

    // ── Zone walk-in/walk-out handlers ──────────────────────────────────

    /// <summary>
    /// Fired when ZoneDetection detects player walked into a zone.
    /// Auto-enters if player is ready, otherwise just tracks position.
    /// </summary>
    void HandlePlayerWalkIntoZone(ulong steamId, Entity playerEntity, ZoneDefinition zone)
    {
        if (!_zoneModeMap.TryGetValue(zone.Hash, out var modeId)) return;

        // If already properly entered, ignore (re-entering after penalty teleport, etc.)
        if (_enteredPlayers.Contains(steamId)) return;

        // Auto-enter: treat walking into zone as ready + enter
        var result = ExecuteEnterFlow(steamId, playerEntity, zone.Hash, modeId);
        if (result.Success)
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {steamId} auto-entered zone {zone.Name} by walking in.");
        }
        else
        {
            BattleLuckPlugin.LogWarning($"[Session] Auto-enter failed for {steamId}: {result.Error}");
        }
    }

    /// <summary>
    /// Fired when ZoneDetection detects player walked out of a zone.
    /// If they didn't use .toggleleave, apply burning penalty.
    /// </summary>
    void HandlePlayerWalkOutOfZone(ulong steamId, Entity playerEntity, int previousZoneHash)
    {
        if (!_zoneModeMap.TryGetValue(previousZoneHash, out var modeId)) return;

        // If not properly entered, nothing to penalize
        if (!_enteredPlayers.Contains(steamId))
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {steamId} exited zone {previousZoneHash} but was not entered — skipping penalty.");
            return;
        }

        // CRITICAL: Skip penalty if player just died (respawn teleports them out of zone)
        if (_recentlyDied.Contains(steamId))
        {
            BattleLuckPlugin.LogInfo($"[Session] Player {steamId} zone-exit suppressed — recently died, not a walk-out.");
            return;
        }

        // Player walked out without .toggleleave → BURNING PENALTY (HP drain per tick)
        _penaltyBurning.Add(steamId);

        BattleLuckPlugin.LogInfo($"[Session] Player {steamId} left zone {previousZoneHash} without .toggleleave — HP drain penalty applied.");

        if (FlowController.TryGetUser(playerEntity, out var user))
            NotificationHelper.NotifyPlayer(user, "⚠ You left the zone without .toggleleave! Taking damage until death. Use .toggleleave to exit properly.");
    }

    // ── Tick ────────────────────────────────────────────────────────────

    public void Tick(IEnumerable<Entity> onlinePlayers, float deltaSeconds)
    {
        _zoneDetection.Tick(onlinePlayers);

        // Process pending buff re-applications (RemoveAndAddBuff queue)
        EntityExtensions.TickPendingBuffs();

        // Apply HP drain to penalty players each tick (replaces broken Ignite buff)
        foreach (var player in onlinePlayers)
        {
            if (!player.Exists() || !player.IsPlayer()) continue;
            ulong steamId = player.GetSteamId();
            if (_penaltyBurning.Contains(steamId))
            {
                player.DealDamagePercent(PenaltyDrainPercent);
            }
        }

        // Clear recently-died players who have respawned and are back online
        if (_recentlyDied.Count > 0)
        {
            var toClear = new List<ulong>();
            foreach (var steamId in _recentlyDied)
            {
                // Keep in recently-died for a few ticks to let zone detection stabilize after respawn
                // The HandleDeath sets the flag; we clear after 1 tick cycle
                toClear.Add(steamId);
            }
            foreach (var steamId in toClear)
                _recentlyDied.Remove(steamId);
        }

        foreach (var kv in _activeSessions)
        {
            var session = kv.Value;
            if (session.Mode == null || session.Context == null) continue;

            // Tick arena tile spawning
            if (session.ArenaSpawning && session.Border != null)
                session.ArenaSpawning = session.Border.TickSpawnAll();

            session.Mode.OnTick(session.Context, deltaSeconds);

            // Tick DOT boundary enforcement
            if (session.BorderDot != null && session.Mode is BloodbathMode bbMode && bbMode.Shrink.IsActive)
            {
                var zone = session.Config?.Zones?.Zones?.FirstOrDefault(z => z.Hash == kv.Key);
                var dotCfg = zone?.Boundary?.Dot;
                if (dotCfg != null)
                {
                    var zoneEntities = onlinePlayers.Where(e => session.Context.Players.Contains(e.GetSteamId()));
                    session.BorderDot.Tick(session.Context, zoneEntities,
                        bbMode.Shrink.GetCurrentCenter(), bbMode.Shrink.CurrentRadius,
                        zone.ExitRadius, dotCfg);
                }
            }

            // Auto-end session when mode signals completion
            if (session.Context.State.ContainsKey("result"))
                _pendingEnd.Add(kv.Key);
        }

        // Auto-trash dropped items in active zones
        _autoTrash.Tick();

        foreach (var zoneHash in _pendingEnd)
            EndSession(zoneHash);
        _pendingEnd.Clear();
    }

    // ── Death handler ───────────────────────────────────────────────────

    void HandleDeath(Entity died, Entity killer)
    {
        if (!died.IsPlayer()) return;
        ulong victimId = died.GetSteamId();

        BattleLuckPlugin.LogInfo($"[Session] HandleDeath: victim={victimId}, killer={killer.Index}, isPlayer={killer.IsPlayer()}");

        // Mark as recently died to suppress zone-exit detection from respawn teleport
        _recentlyDied.Add(victimId);

        // PENALTY DEATH: player was under HP drain from unauthorized zone exit
        if (_penaltyBurning.Contains(victimId))
        {
            HandlePenaltyDeath(victimId, died);
            return;
        }

        // Route to session death handling
        foreach (var kv in _activeSessions)
        {
            var session = kv.Value;
            if (session.Mode == null || session.Context == null) continue;

            if (!session.Context.Players.Contains(victimId)) continue;

            // Boundary DOT death
            if ((!killer.IsPlayer() || killer == died) &&
                session.BorderDot != null && session.BorderDot.HandleBoundaryDeath(died, victimId))
            {
                BattleLuckPlugin.LogInfo($"[Session] Player {victimId} died from boundary DOT — returned to safe position.");
                return;
            }

            // PvP kill
            if (killer.IsPlayer() && killer != died)
            {
                ulong killerId = killer.GetSteamId();
                session.Mode.OnPlayerDowned(session.Context, victimId, killerId);
                return;
            }

            // PvE / self death
            session.Mode.OnPlayerDowned(session.Context, victimId, 0);
            return;
        }

        // PvE kill tracking (player killed NPC)
        if (killer.IsPlayer() && !died.IsPlayer())
        {
            ulong killerId = killer.GetSteamId();
            foreach (var kv in _activeSessions)
            {
                if (kv.Value.Context.Players.Contains(killerId))
                {
                    kv.Value.Spawner.RecordKill(died);
                    kv.Value.Mode?.OnPlayerDowned(kv.Value.Context, 0, killerId);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Handle death from burning penalty: restore old kit, teleport to penalty spawn.
    /// </summary>
    void HandlePenaltyDeath(ulong steamId, Entity playerEntity)
    {
        _penaltyBurning.Remove(steamId);

        BattleLuckPlugin.LogInfo($"[Session] HandlePenaltyDeath: {steamId} — restoring snapshot.");

        // Restore original snapshot (saved on enter)
        if (_playerZoneMap.TryGetValue(steamId, out var zoneHash))
        {
            _playerState.RestoreSnapshot(playerEntity, zoneHash);
        }

        // Clean up from active session
        if (_playerZoneMap.TryGetValue(steamId, out var zh) && _activeSessions.TryGetValue(zh, out var session))
        {
            CleanupPlayerState(steamId, session);
        }
        else
        {
            _enteredPlayers.Remove(steamId);
            _playerZoneMap.Remove(steamId);
        }

        // Teleport to penalty spawn
        playerEntity.SetPosition(PenaltySpawn);

        // Heal to full after restore
        playerEntity.HealToFull();

        BattleLuckPlugin.LogInfo($"[Session] Player {steamId} died from penalty burn — kit restored, teleported to {PenaltySpawn}.");
    }

    // ── Arena tile spawning ────────────────────────────────────────────

    void SpawnArenaTiles(ActiveSession session, ZoneDefinition zone, string modeId, ModeConfig config)
    {
        try
        {
            float radius = zone.Radius > 0 ? zone.Radius : 50f;
            var center = zone.TeleportSpawn?.ToFloat3() ?? new float3(0, 0, 0);

            if (session.Border != null && zone.Boundary?.Walls != null)
            {
                session.Border.StartZoneBoundary(modeId, center, radius, zone.Boundary.Walls);
                session.ArenaSpawning = true;
                BattleLuckPlugin.LogInfo($"[Session] Arena tiles queued for {modeId} zone {zone.Hash}");
            }

            if (session.Platform != null)
            {
                var platformCfg = zone.MovingPlatform;
                if (platformCfg != null)
                {
                    session.Platform.Configure(platformCfg);
                    session.Platform.SpawnPlatform(center);
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[Session] Arena tile spawn failed for {modeId}: {ex.Message}");
        }
    }

    // ── Session management ──────────────────────────────────────────────

    ActiveSession GetOrCreateSession(int zoneHash, string modeId)
    {
        if (_activeSessions.TryGetValue(zoneHash, out var existing))
            return existing;

        var mode = _registry.Resolve(modeId);
        var config = ConfigLoader.Load(modeId);

        var ctx = new GameModeContext
        {
            SessionId = $"{modeId}_{zoneHash}_{DateTime.UtcNow.Ticks}",
            ZoneHash = zoneHash,
            ModeId = modeId,
            TimeLimitSeconds = ModeDurationSeconds,
            Broadcast = msg => BattleLuckPlugin.BroadcastToSession?.Invoke(modeId, msg)
        };

        var spawner = new SpawnController();
        var border = new BorderWallController();
        var borderDot = new BorderController();
        var platform = new PlatformController();
        ctx.State["spawner"] = spawner;
        ctx.State["config"] = config;
        ctx.State["border"] = border;
        ctx.State["borderDot"] = borderDot;
        ctx.State["platform"] = platform;

        var session = new ActiveSession
        {
            Mode = mode,
            Context = ctx,
            Spawner = spawner,
            Border = border,
            BorderDot = borderDot,
            Platform = platform,
            Config = config
        };

        _activeSessions[zoneHash] = session;

        // Register zone for auto-trash
        var trashZone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (trashZone != null)
            _autoTrash.RegisterZone(zoneHash, trashZone);

        return session;
    }

    void EndSession(int zoneHash)
    {
        if (!_activeSessions.TryGetValue(zoneHash, out var session)) return;

        if (session.Mode != null && session.Context != null)
        {
            session.Mode.OnEnd(session.Context);
            session.Mode.OnReset(session.Context);
        }

        // Clean up all players in this session
        if (session.Context != null)
        {
            foreach (var steamId in session.Context.Players.ToList())
            {
                _enteredPlayers.Remove(steamId);
                _playerZoneMap.Remove(steamId);
                FloorLockService.UnlockPlayer(steamId);
            }
        }

        // Remove burning penalty from ALL players who left this zone without .toggleleave
        ClearAllBurningForZone(zoneHash);

        session.Spawner.DespawnAll();
        session.BorderDot?.Reset();
        if (session.Border != null)
        {
            session.Border.DespawnWalls();
            session.Border.DespawnFloors();
        }
        session.Platform?.DespawnPlatform();

        _autoTrash.UnregisterZone(zoneHash);
        _activeSessions.Remove(zoneHash);
        BattleLuckPlugin.LogInfo($"[Session] Session ended for zone {zoneHash}.");
    }

    /// <summary>Clear burning from all penalty players (used on event end).</summary>
    void ClearAllBurningForZone(int zoneHash)
    {
        // Remove burning from players who were penalized from this zone
        var toClear = _playerZoneMap.Where(kv => kv.Value == zoneHash && _penaltyBurning.Contains(kv.Key))
                                     .Select(kv => kv.Key).ToList();

        // Also clear any burning players no longer tracked in zone map
        // (they may have already been partially cleaned up)
        foreach (var steamId in toClear)
        {
            _penaltyBurning.Remove(steamId);
            _playerZoneMap.Remove(steamId);
        }

        if (toClear.Count > 0)
            BattleLuckPlugin.LogInfo($"[Session] Cleared burning penalty from {toClear.Count} player(s) on event end.");
    }

    /// <summary>Clear ALL burning penalties globally (admin command).</summary>
    public int ClearAllBurning(IEnumerable<Entity> onlinePlayers)
    {
        if (_penaltyBurning.Count == 0) return 0;

        int cleared = 0;
        foreach (var player in onlinePlayers)
        {
            if (!player.Exists() || !player.IsPlayer()) continue;
            ulong steamId = player.GetSteamId();
            if (_penaltyBurning.Remove(steamId))
            {
                cleared++;
            }
        }
        _penaltyBurning.Clear();
        return cleared;
    }

    /// <summary>Get count of currently burning players.</summary>
    public int BurningPlayerCount => _penaltyBurning.Count;

    /// <summary>Get count of entered players.</summary>
    public int EnteredPlayerCount => _enteredPlayers.Count;

    // ── Admin commands ──────────────────────────────────────────────────

    public void ForceEndByModeId(string modeId)
    {
        var toEnd = _activeSessions.Where(kv => kv.Value.Context?.ModeId == modeId).Select(kv => kv.Key).ToList();
        foreach (var hash in toEnd)
            EndSession(hash);
    }

    /// <summary>Force-start: teleport player to zone and auto-enter.</summary>
    public void ForceStart(string modeId, Entity playerCharacter)
    {
        var zoneEntry = _zoneModeMap.FirstOrDefault(kv => kv.Value == modeId);
        if (zoneEntry.Value == null)
        {
            BattleLuckPlugin.LogWarning($"[Session] No zone mapped for mode '{modeId}'.");
            return;
        }

        int zoneHash = zoneEntry.Key;
        var config = ConfigLoader.Load(modeId);
        var zone = config.Zones.Zones.FirstOrDefault(z => z.Hash == zoneHash);
        if (zone == null) return;

        ulong steamId = playerCharacter.GetSteamId();

        // Teleport into zone so detection picks them up
        playerCharacter.SetPosition(zone.TeleportSpawn.ToFloat3());
        _zoneDetection.SetPlayerZone(steamId, zoneHash);

        // Execute enter flow directly
        ExecuteEnterFlow(steamId, playerCharacter, zoneHash, modeId);
    }

    /// <summary>Force a player to exit their current zone session.</summary>
    public bool ForceExitPlayer(ulong steamId, Entity playerCharacter)
    {
        if (!_playerZoneMap.TryGetValue(steamId, out var zoneHash))
            return false;

        // Stop any burning
        _penaltyBurning.Remove(steamId);

        ExecuteLeaveFlow(steamId, playerCharacter, zoneHash);
        _zoneDetection.SetPlayerZone(steamId, 0);
        return true;
    }

    public ActiveSession? GetSession(int zoneHash) => _activeSessions.GetValueOrDefault(zoneHash);
    public IReadOnlyDictionary<int, ActiveSession> ActiveSessions => _activeSessions;
    public SpawnController GetSpawner(int zoneHash) => _activeSessions.TryGetValue(zoneHash, out var s) ? s.Spawner : _spawner;

    public void PauseAll()
    {
        foreach (var session in _activeSessions.Values)
            session.IsPaused = true;
    }

    public int ResumeAll()
    {
        int count = 0;
        foreach (var session in _activeSessions.Values)
        {
            if (session.IsPaused) { session.IsPaused = false; count++; }
        }
        return count;
    }

    public bool TryKickPlayer(ulong steamId, out string? error)
    {
        error = null;
        foreach (var session in _activeSessions.Values)
        {
            if (session.Context.Players.Contains(steamId))
            {
                CleanupPlayerState(steamId, session);
                return true;
            }
        }
        error = "Player not in any active session";
        return false;
    }

    public bool TrySetWinner(ulong steamId, out string? error)
    {
        error = null;
        foreach (var session in _activeSessions.Values)
        {
            if (session.Context.Players.Contains(steamId))
            {
                session.Context.State["winner"] = steamId;
                session.Context.State["result"] = "admin_forced";
                EndSession(session.Context.ZoneHash);
                return true;
            }
        }
        error = "Player not in any active session";
        return false;
    }
}

public sealed class ActiveSession
{
    public GameModeBase? Mode { get; set; }
    public GameModeContext Context { get; set; } = new();
    public SpawnController Spawner { get; set; } = new();
    public BorderWallController? Border { get; set; }
    public BorderController? BorderDot { get; set; }
    public PlatformController? Platform { get; set; }
    public ModeConfig Config { get; set; } = new();
    public bool IsStarted { get; set; }
    public bool IsPaused { get; set; }
    public bool ArenaInitialized { get; set; }
    public bool ArenaSpawning { get; set; }
}
