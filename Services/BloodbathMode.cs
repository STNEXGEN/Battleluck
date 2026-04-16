using System.Linq;
using BattleLuck.Models;
using Stunlock.Core;
using Unity.Mathematics;

/// <summary>
/// Bloodbath — Free-for-all PvP deathmatch with shrinking zone.
/// Full flows: PvP kills, optional PvE waves (enableEliteMobs/enableVBloods),
/// loot crate drops, boss spawns, lives system, late-join, shrink zone, platform sync.
/// Last player standing or highest kills when timer expires wins.
/// </summary>
public sealed class BloodbathMode : GameModeBase
{
    public override string ModeId => "bloodbath";
    public override string DisplayName => "Bloodbath";

    private readonly ShrinkZoneController _shrink = new();
    private readonly TimerController _timer = new();
    private readonly LootCrateController _lootCrates = new();
    private readonly SpawnController _spawner = new();

    private int _initialPlayerCount;
    private readonly Dictionary<ulong, int> _lives = new();
    private int _livesPerPlayer;
    private bool _eliminationMode;
    private bool _allowLateJoin;
    private bool _enableVBloods;
    private bool _enableEliteMobs;
    private float _bossSpawnTimer;
    private bool _bossSpawned;
    private BossesConfig? _bossConfig;
    private float3 _center;
    private float _gridHalfExtent;
    private ModeConfig? _config;

    public override void OnStart(GameModeContext ctx)
    {
        _config = ctx.State.TryGetValue("config", out var cfg) && cfg is ModeConfig mc ? mc : ConfigLoader.Load(ModeId);
        var rules = _config.Session.Rules;

        int timeLimitSec = rules.MatchDurationMinutes * 60;
        if (timeLimitSec <= 0) timeLimitSec = 300;

        _livesPerPlayer = rules.LivesPerPlayer > 0 ? rules.LivesPerPlayer : 1;
        _eliminationMode = rules.EliminationMode;
        _allowLateJoin = rules.AllowLateJoin;
        _enableVBloods = rules.EnableVBloods;
        _enableEliteMobs = rules.EnableEliteMobs;
        _bossSpawned = true; // Default to true to disable boss spawn if not configured
        _bossSpawnTimer = 0f;

        var zone = _config.Zones.Zones.FirstOrDefault();
        float startRadius = zone?.Radius > 0 ? zone.Radius : 100f;
        float minRadius = zone?.ExitRadius > 0 ? zone.ExitRadius : 15f;
        float shrinkDuration = timeLimitSec * 0.8f;

        _center = zone?.TeleportSpawn?.ToFloat3() ?? new float3(0, 0, 0);
        _gridHalfExtent = startRadius * 0.5f;

        _timer.Start(timeLimitSec);
        ctx.TimeLimitSeconds = timeLimitSec;

        _shrink.Configure(startRadius, minRadius, shrinkDuration);
        _shrink.ConfigureWaypoints(zone?.Waypoints, _center);
        _shrink.Start();

        // Initialize lives for all starting players
        _lives.Clear();
        foreach (var steamId in ctx.Players)
            _lives[steamId] = _livesPerPlayer;
        _initialPlayerCount = ctx.Players.Count;

        // Loot crates from zone config
        if (zone?.LootCrates != null)
            _lootCrates.Configure(zone.LootCrates);

        // Boss config from zone
        _bossConfig = zone?.Bosses;
        if (_bossConfig?.Enabled == true && _bossConfig.SpawnTrigger == "timed")
            _bossSpawnTimer = _bossConfig.TimedSpawnAfterSeconds;

        // Store spawner in ctx.State so SessionController can clean up
        ctx.State["spawner"] = _spawner;

        // Optional: spawn initial PvE threat wave if elites enabled
        if (_enableEliteMobs)
        {
            var prefabs = SpawnController.EliteEnemies;
            int count = Math.Max(1, ctx.Players.Count / 2);
            _spawner.SpawnWave(prefabs, count, _center, startRadius * 0.6f);
        }

        ctx.Broadcast?.Invoke($"BLOODBATH — Free-for-all! {(_livesPerPlayer > 1 ? $"{_livesPerPlayer} lives each. " : "")}Last vampire standing wins! Time: {_timer.FormatRemaining()}");
        GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
        });
    }

    public override void OnTick(GameModeContext ctx, float deltaSeconds)
    {
        // Shrink radius
        _shrink.Tick(ctx, deltaSeconds);

        // Move center along waypoints + sync platform and DOT boundary
        bool centerMoved = _shrink.TickMovement(ctx.SessionId, deltaSeconds);
        if (centerMoved)
        {
            _center = _shrink.GetCurrentCenter();

            if (ctx.State.TryGetValue("platform", out var p) && p is PlatformController platform)
                platform.SyncPlatformWithCenter(_center);

            // Sync loot crate positions with center movement
            _lootCrates.SyncCratePositions(_shrink.GetCurrentCenter() - _center);
        }

        // Loot crate tick — spawn and collect
        TickLootCrates(ctx, deltaSeconds);

        // Boss spawn timer
        TickBossSpawn(ctx, deltaSeconds);

        // Timer expired — end by score
        if (_timer.IsExpired)
        {
            ctx.State["result"] = "time_up";
            var leaderboard = ctx.Scores.GetLeaderboard();
            if (leaderboard.Count > 0)
                ctx.State["winner"] = leaderboard[0];
            return;
        }

        // Last standing wins (only active players with lives remaining)
        int alivePlayers = _eliminationMode
            ? _lives.Count(kv => kv.Value > 0 && ctx.Players.Contains(kv.Key))
            : ctx.Players.Count;

        if (alivePlayers <= 1 && _initialPlayerCount > 1)
        {
            ctx.State["result"] = "last_standing";
            ulong? winner = _eliminationMode
                ? _lives.Where(kv => kv.Value > 0 && ctx.Players.Contains(kv.Key)).Select(kv => kv.Key).FirstOrDefault()
                : ctx.Players.FirstOrDefault();
            if (winner.HasValue && winner.Value != 0)
                ctx.State["winner"] = winner.Value;
            return;
        }

        // Escalation: broadcast pressure warnings
        if (_initialPlayerCount > 2 && alivePlayers == 2)
        {
            if (!ctx.State.ContainsKey("_final2"))
            {
                ctx.State["_final2"] = true;
                ctx.Broadcast?.Invoke("⚔️ FINAL TWO! Fight to the death!");
            }
        }

        // Mid-match VBlood spawn (once at 50% time)
        if (_enableVBloods && !_bossSpawned && _timer.ElapsedPercent >= 0.5f)
        {
            SpawnVBloodBoss(ctx);
        }
    }

    public override void OnPlayerJoin(GameModeContext ctx, ulong steamId)
    {
        if (!_allowLateJoin && ctx.State.ContainsKey("_started"))
        {
            BattleLuckPlugin.LogInfo($"[Bloodbath] Late join denied for {steamId}");
            return;
        }

        if (!_lives.ContainsKey(steamId))
            _lives[steamId] = _livesPerPlayer;

        ctx.Players.Add(steamId);

        if (_initialPlayerCount == 0)
            _initialPlayerCount = ctx.Players.Count;

        ctx.Broadcast?.Invoke($"A new challenger approaches! ({ctx.Players.Count} players)");
    }

    public override void OnPlayerDowned(GameModeContext ctx, ulong victimSteamId, ulong? killerSteamId)
    {
        // Score the kill
        if (killerSteamId.HasValue && killerSteamId.Value != victimSteamId)
        {
            ScoreAction(ctx, killerSteamId.Value, ActionType.Kill, 1);
            ctx.Broadcast?.Invoke($"Kill! ({ctx.Scores.GetPlayerScore(killerSteamId.Value)} total)");
        }

        // Lives system
        if (_lives.TryGetValue(victimSteamId, out int remaining))
        {
            remaining--;
            _lives[victimSteamId] = remaining;

            if (remaining > 0)
            {
                ctx.Broadcast?.Invoke($"💀 Down! {remaining} {(remaining == 1 ? "life" : "lives")} remaining.");
                // Player stays in — SessionController handles respawn
                return;
            }
        }

        // Out of lives or single-life mode — eliminate
        ctx.Players.Remove(victimSteamId);
        ScoreAction(ctx, victimSteamId, ActionType.Elimination, 0);
        GameEvents.OnPlayerEliminated?.Invoke(new PlayerEliminatedEvent
        {
            SessionId = ctx.SessionId,
            SteamId = victimSteamId,
            EliminatedBy = killerSteamId
        });
        ctx.Broadcast?.Invoke($"☠️ ELIMINATED! ({ctx.Players.Count} remain)");
    }

    public override void OnPlayerLeave(GameModeContext ctx, ulong steamId)
    {
        ctx.Players.Remove(steamId);
        _lives.Remove(steamId);

        GameEvents.OnPlayerLeft?.Invoke(new PlayerLeftEvent
        {
            SessionId = ctx.SessionId,
            SteamId = steamId,
            ModeId = ModeId
        });
    }

    public override void OnEnd(GameModeContext ctx)
    {
        _timer.Stop();
        _shrink.Reset();
        _lootCrates.ClearAllCrates();
        _spawner.DespawnAll();

        // Resolve winner: prefer already resolved by OnTick
        ulong winner = 0UL;
        if (ctx.State.TryGetValue("winner", out var w) && w is ulong winnerId)
        {
            winner = winnerId;
        }
        else
        {
            var leaderboard = ctx.Scores.GetLeaderboard();
            winner = leaderboard.Count > 0 ? leaderboard[0] : 0UL;
        }

        ctx.Broadcast?.Invoke($"Bloodbath over! Winner: {winner}");
        GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
        {
            SessionId = ctx.SessionId,
            ModeId = ModeId,
            WinnerSteamId = winner != 0 ? winner : null
        });
    }

    public override void OnReset(GameModeContext ctx)
    {
        _timer.Reset();
        _shrink.Reset();
        _lootCrates.ClearAllCrates();
        _spawner.Reset();
        ctx.Scores.Reset();
        _lives.Clear();
        _initialPlayerCount = 0;
        _bossSpawned = false;
        _bossSpawnTimer = 0f;
        _config = null;
    }

    // ── Private helpers ──────────────────────────────────────────────────

    void TickLootCrates(GameModeContext ctx, float deltaSeconds)
    {
        var playerEntities = GetPlayerEntities(ctx);
        var collected = _lootCrates.Tick(_center, _gridHalfExtent, deltaSeconds, playerEntities, ctx.SessionId);

        foreach (var (player, crate) in collected)
        {
            ulong steamId = player.GetSteamId();
            ScoreAction(ctx, steamId, ActionType.LootCrate, 5);
            ctx.Broadcast?.Invoke($"🎁 {crate.Type} collected! +5 pts");
        }
    }

    void TickBossSpawn(GameModeContext ctx, float deltaSeconds)
    {
        if (_bossSpawned || _bossConfig == null || !_bossConfig.Enabled) return;

        if (_bossConfig.SpawnTrigger == "timed")
        {
            _bossSpawnTimer -= deltaSeconds;
            if (_bossSpawnTimer <= 0)
            {
                SpawnConfiguredBoss(ctx);
            }
        }
        else if (_bossConfig.SpawnTrigger == "player_count")
        {
            // Spawn boss when players drop to half
            if (ctx.Players.Count <= _initialPlayerCount / 2 && _initialPlayerCount > 2)
            {
                SpawnConfiguredBoss(ctx);
            }
        }
    }

    void SpawnConfiguredBoss(GameModeContext ctx)
    {
        if (_bossConfig == null || _bossConfig.Bosses.Count == 0) return;

        var rng = new System.Random();
        var bossDef = _bossConfig.Bosses[rng.Next(_bossConfig.Bosses.Count)];
        var prefab = PrefabHelper.GetPrefabGuidDeep(bossDef.Prefab);
        if (prefab == null) return;

        var offset = bossDef.SpawnOffset?.ToFloat3() ?? float3.zero;
        _spawner.SpawnBoss(prefab.Value, _center + offset, entity =>
        {
            ctx.Broadcast?.Invoke($"⚠️ BOSS SPAWNED: {bossDef.Name}! Kill it for bonus points!");
        });
        _bossSpawned = true;
    }

    void SpawnVBloodBoss(GameModeContext ctx)
    {
        if (_bossSpawned) return;
        _bossSpawned = true;

        // Pick a random VBlood from the full pool
        var vbloods = new[] {
            SpawnController.VBlood_Errol, SpawnController.VBlood_Grayson,
            SpawnController.VBlood_Putrid, SpawnController.VBlood_Keely,
            SpawnController.VBlood_Nicholaus, SpawnController.VBlood_Quincey,
            SpawnController.VBlood_Jade, SpawnController.VBlood_Octavian,
            SpawnController.VBlood_Dracula
        };
        var rng = new System.Random();
        var boss = vbloods[rng.Next(vbloods.Length)];

        _spawner.SpawnBoss(boss, _center, entity =>
        {
            ctx.Broadcast?.Invoke("⚠️ A VBlood has entered the arena! Kill it for bonus points!");
        });
    }

    /// <summary>Get active player entities from the context.</summary>
    static List<Unity.Entities.Entity> GetPlayerEntities(GameModeContext ctx)
    {
        var entities = new List<Unity.Entities.Entity>();
        foreach (var steamId in ctx.Players)
        {
            var onlinePlayers = VRisingCore.GetOnlinePlayers();
            foreach (var player in onlinePlayers)
            {
                if (player.GetSteamId() == steamId)
                {
                    entities.Add(player);
                    break;
                }
            }
        }
        return entities;
    }

    public ShrinkZoneController Shrink => _shrink;
    public TimerController Timer => _timer;
    public LootCrateController LootCrates => _lootCrates;
    public SpawnController Spawner => _spawner;
    public IReadOnlyDictionary<ulong, int> Lives => _lives;
}
