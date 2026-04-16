using System.Linq;
using VampireCommandFramework;
using System.Collections.Generic;
using Unity.Entities;

public static class AdminCommands
{
    [Command("debugabilities", description: "Print all discovered AbilityGroup prefabs from the server", adminOnly: true)]
    public static void DebugAbilities(ChatCommandContext ctx)
    {
        try
        {
            PrefabHelper.ScanLivePrefabs();
            var abilityGroups = PrefabHelper.FindLive("AbilityGroup").ToList();

            ctx.Reply($"Found {abilityGroups.Count} AbilityGroup prefabs in PrefabCollectionSystem:");

            // Print in batches to avoid chat overflow
            int shown = 0;
            foreach (var kvp in abilityGroups.OrderBy(k => k.Key))
            {
                ctx.Reply($"  {kvp.Key} → {kvp.Value.GuidHash}");
                shown++;
                if (shown >= 50)
                {
                    ctx.Reply($"  ... and {abilityGroups.Count - shown} more. Use .exportprefabs for full list.");
                    break;
                }
            }

            // Also show combat key status
            var schools = AbilityController.AbilitiesBySchool;
            ctx.Reply($"Discovered schools: {string.Join(", ", schools.Where(s => s.Value.Count > 0).Select(s => $"{s.Key}({s.Value.Count})"))}");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Debug failed: {ex.Message}");
        }
    }

    [Command("debugslots", description: "Print combat key slot resolution status", adminOnly: true)]
    public static void DebugSlots(ChatCommandContext ctx)
    {
        try
        {
            PrefabHelper.ScanLivePrefabs();

            var checks = new (string Name, string PrefabName)[]
            {
                ("PrimaryAttack", "AB_Vampire_PrimaryAttack_AbilityGroup"),
                ("Dash",          "AB_Vampire_VampireDash_AbilityGroup"),
                ("VeilOfBlood",   "AB_Vampire_VeilOfBlood_AbilityGroup"),
            };

            foreach (var (name, prefabName) in checks)
            {
                var exact = PrefabHelper.GetPrefabGuidDeep(prefabName);
                var status = exact.HasValue ? $"OK → {exact.Value.GuidHash}" : "MISSING";
                ctx.Reply($"  {name}: {status} ({prefabName})");
            }

            // Show partial matches for Veil if exact failed
            var veilMatches = PrefabHelper.FindLive("Veil").Take(10).ToList();
            if (veilMatches.Count > 0)
            {
                ctx.Reply($"Veil partial matches ({veilMatches.Count}):");
                foreach (var m in veilMatches)
                    ctx.Reply($"  {m.Key} → {m.Value.GuidHash}");
            }
        }
        catch (Exception ex)
        {
            ctx.Reply($"Debug failed: {ex.Message}");
        }
    }

    [Command("reload", description: "Reload configs from disk", adminOnly: true)]
    public static void ReloadConfigs(ChatCommandContext ctx)
    {
        try
        {
            ConfigLoader.ReloadAll();
            ctx.Reply("Configs reloaded.");
        }
        catch (System.Exception ex)
        {
            ctx.Reply($"Failed to reload: {ex.Message}");
        }
    }

    [Command("pause", description: "Pause all active sessions", adminOnly: true)]
    public static void PauseSessions(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        session.PauseAll();
        ctx.Reply($"Paused {session.ActiveSessions.Count} active session(s).");
    }

    [Command("resume", description: "Resume paused sessions", adminOnly: true)]
    public static void ResumeSessions(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var count = session.ResumeAll();
        ctx.Reply($"Resumed {count} session(s).");
    }

    [Command("kick", description: "Kick player from session", adminOnly: true)]
    public static void KickPlayer(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            ctx.Reply("Invalid Steam ID.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        if (!session.TryKickPlayer(steamId, out var error))
        {
            ctx.Reply($"Kick failed: {error}");
            return;
        }

        ctx.Reply($"Kicked player {steamId}.");
    }

    [Command("setwinner", description: "Set winner and end session", adminOnly: true)]
    public static void SetWinner(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            ctx.Reply("Invalid Steam ID.");
            return;
        }

        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        if (!session.TrySetWinner(steamId, out var error))
        {
            ctx.Reply($"Set winner failed: {error}");
            return;
        }

        ctx.Reply($"Winner set to {steamId}. Session ended.");
    }

    [Command("zoneinfo", description: "Show zone stats and player counts", adminOnly: true)]
    public static void ZoneInfo(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null)
        {
            ctx.Reply("Session controller not initialized.");
            return;
        }

        var zones = session.ActiveSessions;
        if (zones.Count == 0)
        {
            ctx.Reply("No active zones.");
            return;
        }

        ctx.Reply($"Active zones ({zones.Count}):");
        foreach (var kv in zones)
        {
            var s = kv.Value;
            var state = s.IsStarted ? "running" : "waiting";
            ctx.Reply($"  Zone {kv.Key} — {s.Context.ModeId} ({s.Context.Players.Count} players, {state})");
        }
    }

    [Command("freebuild", description: "Toggle building restrictions off/on", adminOnly: true)]
    public static void ToggleFreeBuild(ChatCommandContext ctx)
    {
        if (BuildingRestrictionController.RestrictionsDisabled)
        {
            BuildingRestrictionController.EnableRestrictions();
            ctx.Reply("Building restrictions ENABLED (normal mode).");
        }
        else
        {
            BuildingRestrictionController.DisableRestrictions();
            ctx.Reply("Building restrictions DISABLED (free build).");
        }
    }

    // ── Event Control Commands ──────────────────────────────────────────

    [Command("event.start", description: "Start an event mode (teleports you in)", adminOnly: true)]
    public static void EventStart(ChatCommandContext ctx, string modeId)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var registry = BattleLuckPlugin.GameModes;
        if (registry?.Resolve(modeId) == null) { ctx.Reply($"Unknown mode: {modeId}"); return; }

        var entity = ctx.Event.SenderCharacterEntity;
        session.ForceStart(modeId, entity);
        ctx.Reply($"Event '{modeId}' started. You have been teleported in.");
    }

    [Command("event.end", description: "End all sessions for a mode and clear burning", adminOnly: true)]
    public static void EventEnd(ChatCommandContext ctx, string modeId)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        session.ForceEndByModeId(modeId);
        ctx.Reply($"Event '{modeId}' ended. All sessions closed and burning cleared.");
    }

    [Command("event.endall", description: "End ALL active sessions and clear all burning", adminOnly: true)]
    public static void EventEndAll(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var modeIds = session.ActiveSessions.Values
            .Select(s => s.Context?.ModeId)
            .Where(m => m != null)
            .Distinct()
            .ToList();

        foreach (var modeId in modeIds)
            session.ForceEndByModeId(modeId!);

        ctx.Reply($"All events ended ({modeIds.Count} mode(s) stopped).");
    }

    [Command("event.status", description: "Show all active events and player counts", adminOnly: true)]
    public static void EventStatus(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var zones = session.ActiveSessions;
        if (zones.Count == 0)
        {
            ctx.Reply($"No active events. Burning: {session.BurningPlayerCount} player(s).");
            return;
        }

        ctx.Reply($"Active events ({zones.Count}):");
        foreach (var kv in zones)
        {
            var s = kv.Value;
            var state = s.IsStarted ? (s.IsPaused ? "paused" : "running") : "waiting";
            var elapsed = s.Context.ElapsedSeconds;
            var limit = s.Context.TimeLimitSeconds;
            ctx.Reply($"  {s.Context.ModeId} (zone {kv.Key}) — {s.Context.Players.Count} players, {state}, {elapsed:F0}/{limit}s");
        }
        ctx.Reply($"Entered: {session.EnteredPlayerCount} | Burning: {session.BurningPlayerCount}");
    }

    [Command("event.clearburning", description: "Remove burning penalty from all players", adminOnly: true)]
    public static void EventClearBurning(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var onlinePlayers = VRisingCore.GetOnlinePlayers();
        var cleared = session.ClearAllBurning(onlinePlayers);
        ctx.Reply($"Cleared burning from {cleared} player(s).");
    }

    [Command("event.forceenter", description: "Force a player into a mode", adminOnly: true)]
    public static void EventForceEnter(ChatCommandContext ctx, string modeId, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId)) { ctx.Reply("Invalid Steam ID."); return; }

        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        // Find the player entity
        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(e => e.IsPlayer() && e.GetSteamId() == steamId);
        if (player == Entity.Null) { ctx.Reply($"Player {steamId} not found online."); return; }

        session.ForceStart(modeId, player);
        ctx.Reply($"Force-entered player {steamId} into '{modeId}'.");
    }

    [Command("event.forceexit", description: "Force a player out of their current event", adminOnly: true)]
    public static void EventForceExit(ChatCommandContext ctx, string steamIdStr)
    {
        if (!ulong.TryParse(steamIdStr, out var steamId)) { ctx.Reply("Invalid Steam ID."); return; }

        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var player = VRisingCore.GetOnlinePlayers().FirstOrDefault(e => e.IsPlayer() && e.GetSteamId() == steamId);
        if (player == Entity.Null) { ctx.Reply($"Player {steamId} not found online."); return; }

        if (!session.ForceExitPlayer(steamId, player))
        {
            ctx.Reply($"Player {steamId} is not in any active event.");
            return;
        }
        ctx.Reply($"Force-exited player {steamId}.");
    }

    // ── Auto-Trash Commands ─────────────────────────────────────────────

    [Command("autotrash", description: "Toggle auto-trash for dropped items in mode zones", adminOnly: true)]
    public static void AutoTrashToggle(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var trash = session.AutoTrash;
        trash.Enabled = !trash.Enabled;
        ctx.Reply($"Auto-trash is now {(trash.Enabled ? "ENABLED" : "DISABLED")}. Total items trashed: {trash.TotalTrashed}");
    }

    [Command("autotrash.status", description: "Show auto-trash stats", adminOnly: true)]
    public static void AutoTrashStatus(ChatCommandContext ctx)
    {
        var session = BattleLuckPlugin.Session;
        if (session == null) { ctx.Reply("Session controller not initialized."); return; }

        var trash = session.AutoTrash;
        ctx.Reply($"Auto-trash: {(trash.Enabled ? "ON" : "OFF")} | Items destroyed: {trash.TotalTrashed}");
    }

    // ── Debug Spawn Commands ────────────────────────────────────────────

    [Command("spawntest", description: "Test-spawn a unit at your position. Usage: .spawntest <prefabGUID>", adminOnly: true)]
    public static void SpawnTest(ChatCommandContext ctx, int prefabHash)
    {
        var charEntity = ctx.Event.SenderCharacterEntity;
        var pos = charEntity.GetPosition();
        var prefab = new PrefabGUID(prefabHash);
        var spawnPos = pos + new Unity.Mathematics.float3(2f, 0f, 2f);

        var spawner = new SpawnController();
        spawner.SpawnWithCallback(prefab, spawnPos, duration: 0f, entity =>
        {
            var name = PrefabHelper.GetLivePrefabName(prefab) ?? prefab.GuidHash.ToString();
            BattleLuckPlugin.LogInfo($"[SpawnTest] Spawned {name} at ({pos.x:F0}, {pos.y:F0}, {pos.z:F0}). Entity: {entity.Index}");
        });

        var name2 = PrefabHelper.GetLivePrefabName(prefab) ?? prefabHash.ToString();
        ctx.Reply($"Spawn request sent for <color=green>{name2}</color> at ({spawnPos.x:F0}, {spawnPos.y:F0}, {spawnPos.z:F0}). Check logs for result.");
    }

    [Command("spawnwave", description: "Test-spawn a wave of enemies. Usage: .spawnwave <tier> <count>", adminOnly: true)]
    public static void SpawnWaveTest(ChatCommandContext ctx, int tier, int count)
    {
        var charEntity = ctx.Event.SenderCharacterEntity;
        var pos = charEntity.GetPosition();

        var enemies = SpawnController.GetEnemiesForWave(tier);
        var spawner = new SpawnController();

        spawner.SpawnWave(enemies, count, pos, spread: 5f, entities =>
        {
            BattleLuckPlugin.LogInfo($"[SpawnWaveTest] Wave complete: {entities.Count}/{count} tier-{tier} enemies.");
        });

        ctx.Reply($"Spawn wave request sent: {count} tier-{tier} enemies. Check logs for result.");
    }

    // ── Live Prefab Scan Commands ───────────────────────────────────────

    [Command("scanprefabs", description: "Scan live prefabs matching a filter. Usage: .scanprefabs <filter> [maxResults]", adminOnly: true)]
    public static void ScanPrefabs(ChatCommandContext ctx, string filter, int maxResults = 20)
    {
        PrefabHelper.ScanLivePrefabs();
        var results = PrefabHelper.FindLive(filter).Take(maxResults).ToList();

        if (results.Count == 0)
        {
            ctx.Reply($"No live prefabs matching '{filter}'.");
            return;
        }

        ctx.Reply($"Found {results.Count} prefab(s) matching '{filter}':");
        foreach (var kv in results)
        {
            ctx.Reply($"  <color=yellow>{kv.Key}</color> = {kv.Value.GuidHash}");
        }
    }

    [Command("scanbufs", description: "Scan live prefabs for buffs. Usage: .scanbufs [filter]", adminOnly: true)]
    public static void ScanBuffs(ChatCommandContext ctx, string filter = "Buff_General")
    {
        PrefabHelper.ScanLivePrefabs();
        var results = PrefabHelper.FindLive(filter).Take(30).ToList();

        if (results.Count == 0)
        {
            ctx.Reply($"No live prefabs matching '{filter}'.");
            return;
        }

        ctx.Reply($"Found {results.Count} buff-related prefab(s):");
        foreach (var kv in results)
        {
            ctx.Reply($"  <color=cyan>{kv.Key}</color> = {kv.Value.GuidHash}");
        }
    }

    [Command("scanitems", description: "Scan live prefabs for items. Usage: .scanitems <filter>", adminOnly: true)]
    public static void ScanItems(ChatCommandContext ctx, string filter = "Item_Weapon_Sword")
    {
        PrefabHelper.ScanLivePrefabs();
        var results = PrefabHelper.FindLive(filter).Take(30).ToList();

        if (results.Count == 0)
        {
            ctx.Reply($"No live prefabs matching '{filter}'.");
            return;
        }

        ctx.Reply($"Found {results.Count} item prefab(s):");
        foreach (var kv in results)
        {
            ctx.Reply($"  <color=green>{kv.Key}</color> = {kv.Value.GuidHash}");
        }
    }

    // ── AI Assistant Admin Commands ─────────────────────────────────────

    [Command("ai.status", description: "Show detailed AI assistant status", adminOnly: true)]
    public static async void AIAdminStatus(ChatCommandContext ctx)
    {
        try
        {
            var aiAssistant = BattleLuckPlugin.AIAssistant;
            if (aiAssistant == null)
            {
                ctx.Reply("AI Assistant is not initialized.");
                return;
            }

            var config = ConfigLoader.LoadAIConfig();
            var status = aiAssistant.IsEnabled ? "ENABLED" : "DISABLED";
            
            ctx.Reply($"🤖 AI Assistant Status: {status}");
            ctx.Reply($"Configuration: {(config.Enabled ? "Enabled" : "Disabled")}");
            ctx.Reply($"Google AI Studio API Key: {(!string.IsNullOrEmpty(config.GoogleAIStudio.ApiKey) ? "Configured" : "Not set")}");
            ctx.Reply($"Model: {config.GoogleAIStudio.Model}");
            ctx.Reply($"Auto Tips: {(config.Messaging.AutoTipsEnabled ? "ON" : "OFF")}");
            ctx.Reply($"Message Cooldown: {config.Messaging.MessageCooldownSeconds}s");
            ctx.Reply($"Battle Sidecar: {(config.Sidecar.Enabled ? "ON" : "OFF")}");

            if (config.Sidecar.Enabled)
            {
                ctx.Reply($"Sidecar URL: {config.Sidecar.BaseUrl}");
                var health = await aiAssistant.GetSidecarHealthAsync();
                if (health != null)
                {
                    ctx.Reply($"Sidecar Health: {health.Status} ({health.Version})");
                }
                else if (!string.IsNullOrWhiteSpace(aiAssistant.SidecarLastError))
                {
                    ctx.Reply($"Sidecar Health: UNAVAILABLE ({aiAssistant.SidecarLastError})");
                }
                else
                {
                    ctx.Reply("Sidecar Health: UNAVAILABLE");
                }
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"AI status command failed: {ex.Message}");
            ctx.Reply($"AI status failed: {ex.Message}");
        }
    }

    [Command("ai.reload", description: "Reload AI configuration and restart service", adminOnly: true)]
    public static void AIReload(ChatCommandContext ctx)
    {
        try
        {
            // Shutdown existing AI
            var currentAI = BattleLuckPlugin.AIAssistant;
            currentAI?.Shutdown();

            // Reload config
            ConfigLoader.ReloadAIConfig();
            var aiConfig = ConfigLoader.LoadAIConfig();

            if (aiConfig.Enabled && !string.IsNullOrEmpty(aiConfig.GoogleAIStudio.ApiKey))
            {
                // Reinitialize AI
                var newAI = new BattleLuck.Core.AIAssistant();
                newAI.Initialize(aiConfig);
                
                BattleLuckPlugin.SetAIAssistant(newAI);

                ctx.Reply("AI Assistant reloaded and reinitialized successfully.");
            }
            else
            {
                BattleLuckPlugin.SetAIAssistant(null);
                    
                ctx.Reply("AI Assistant disabled (check configuration).");
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"AI reload failed: {ex.Message}");
            ctx.Reply($"AI reload failed: {ex.Message}");
        }
    }

    [Command("ai.test", description: "Test AI assistant with a sample query", adminOnly: true)]
    public static async void AITest(ChatCommandContext ctx, string query = "Hello, can you help players?")
    {
        var aiAssistant = BattleLuckPlugin.AIAssistant;
        if (aiAssistant == null || !aiAssistant.IsEnabled)
        {
            ctx.Reply("AI Assistant is not available for testing.");
            return;
        }

        try
        {
            var steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
            var response = await aiAssistant.HandleDirectQuery(steamId, query);
            
            ctx.Reply($"🤖 Test Query: {query}");
            ctx.Reply($"🤖 AI Response: {response}");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"AI test failed: {ex.Message}");
            ctx.Reply($"AI test failed: {ex.Message}");
        }
    }

    [Command("ai.event", description: "Replay AI event flow (start, score, elimination, end) without entering a zone", adminOnly: true)]
    public static void AIEventReplay(ChatCommandContext ctx, string modeId = "aievent")
    {
        try
        {
            GameModeContext? replayContext = null;
            var sessionController = BattleLuckPlugin.Session;
            if (sessionController != null)
            {
                replayContext = sessionController.ActiveSessions.Values
                    .Select(s => s.Context)
                    .FirstOrDefault(c => c != null && c.ModeId.Equals(modeId, StringComparison.OrdinalIgnoreCase));
            }

            if (replayContext == null)
            {
                var senderSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
                replayContext = new GameModeContext
                {
                    SessionId = $"admin-{modeId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                    ZoneHash = -1,
                    ModeId = modeId,
                    Broadcast = msg => ctx.Reply(msg)
                };

                replayContext.Players.Add(senderSteamId);

                var secondPlayer = VRisingCore.GetOnlinePlayers()
                    .Where(e => e.IsPlayer())
                    .Select(e => e.GetSteamId())
                    .FirstOrDefault(id => id != senderSteamId);

                if (secondPlayer != 0)
                    replayContext.Players.Add(secondPlayer);

                ctx.Reply($"No active '{modeId}' session found. Replaying with synthetic context ({replayContext.Players.Count} player(s)).");
            }
            else
            {
                ctx.Reply($"Replaying AI flow in active session {replayContext.SessionId} ({replayContext.ModeId}).");
            }

            GameEvents.OnModeStarted?.Invoke(new ModeStartedEvent
            {
                SessionId = replayContext.SessionId,
                ModeId = replayContext.ModeId,
            });

            AiEventMode.EmitCoreAiTestSequence(replayContext);

            var leaderboard = replayContext.Scores.GetLeaderboard();
            var topPlayer = leaderboard.Count > 0 ? leaderboard[0] : 0UL;
            GameEvents.OnModeEnded?.Invoke(new ModeEndedEvent
            {
                SessionId = replayContext.SessionId,
                ModeId = replayContext.ModeId,
                WinnerSteamId = topPlayer != 0 ? topPlayer : null
            });

            ctx.Reply("AI event flow emitted: mode.start -> player.scored -> player.eliminated -> mode.end");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"ai.event failed: {ex.Message}");
            ctx.Reply($"ai.event failed: {ex.Message}");
        }
    }
}