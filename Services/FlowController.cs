using ProjectM;
using Stunlock.Core;
using Unity.Entities;

/// <summary>
/// Executes flow_enter / flow_exit action sequences from config.
/// Fail-fast: if kit.apply fails, auto-rollback via snapshot.restore.
/// 
/// Core actions (Step 3):
///   1. snapshot.save   — save full player state
///   2. kit.apply       — apply kit with onFail: rollback
///   3. snapshot.restore — restore full player state
///   4. notify          — send chat message
///   5. teleport        — move player to zone spawn
///
/// Legacy actions preserved for backward compatibility.
/// </summary>
public sealed class FlowController
{
    readonly PlayerStateController _playerState;
    readonly GameModeRegistry _registry;

    public FlowController(PlayerStateController playerState, GameModeRegistry registry)
    {
        _playerState = playerState;
        _registry = registry;
    }

    /// <summary>
    /// Deterministic zone-entry sequence owned by FlowController.
    /// Critical order: snapshot -> kit -> pvp/heal/teleport -> reactive event.
    /// </summary>
    public OperationResult ExecuteEnter(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        ulong steamId = playerCharacter.GetSteamId();
        string modeId = config.ModeId;
        string kitId = string.IsNullOrWhiteSpace(zone.KitId) ? modeId : zone.KitId;

        try
        {
            _playerState.SaveSnapshot(playerCharacter, zone.Hash);

            var kitResult = KitController.ApplyKit(playerCharacter, kitId);
            if (!kitResult.Success)
            {
                _playerState.RestoreSnapshot(playerCharacter, zone.Hash);
                NotificationHelper.NotifyAdmins($"[BattleLuck] Kit apply failed for {steamId} in {modeId}: {kitResult.Error}");
                return OperationResult.Fail(kitResult.Error ?? "kit.apply failed");
            }

            if (config.Session.Rules.EnablePvP)
                playerCharacter.SetTeam(zone.Hash + (int)(steamId % 1000));

            playerCharacter.HealToFull();
            playerCharacter.SetPosition(zone.TeleportSpawn.ToFloat3());

            GameEvents.OnZoneEnter?.Invoke(new ZoneEnterEvent
            {
                PlayerEntity = playerCharacter,
                SteamId = steamId,
                ZoneId = zone.Name,
                SessionId = ctx?.SessionId ?? ""
            });

            if (TryGetUser(playerCharacter, out var user))
                NotificationHelper.NotifyPlayer(user, $"Entered {zone.Name}. Loadout applied.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[FlowController] ExecuteEnter failed for {steamId} in zone {zone.Hash}: {ex.Message}");
            try
            {
                _playerState.RestoreSnapshot(playerCharacter, zone.Hash);
            }
            catch (Exception restoreEx)
            {
                BattleLuckPlugin.LogError($"[FlowController] ExecuteEnter rollback failed for {steamId}: {restoreEx.Message}");
            }

            NotificationHelper.NotifyAdmins($"[BattleLuck] ExecuteEnter failed for {steamId} in {modeId}: {ex.Message}");
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Deterministic zone-exit sequence owned by FlowController.
    /// Critical order: restore -> disable pvp -> reactive event.
    /// </summary>
    public OperationResult ExecuteExit(ModeConfig config, Entity playerCharacter, ZoneDefinition zone, GameModeContext? ctx = null)
    {
        ulong steamId = playerCharacter.GetSteamId();

        try
        {
            if (!_playerState.RestoreSnapshot(playerCharacter, zone.Hash))
                return OperationResult.Fail($"No snapshot found for {steamId}.");

            if (config.Session.Rules.EnablePvP)
                playerCharacter.SetTeam(0);

            GameEvents.OnZoneExit?.Invoke(new ZoneExitEvent
            {
                PlayerEntity = playerCharacter,
                SteamId = steamId,
                ZoneId = zone.Name,
                SessionId = ctx?.SessionId ?? ""
            });

            if (TryGetUser(playerCharacter, out var user))
                NotificationHelper.NotifyPlayer(user, $"Exited {zone.Name}. Original state restored.");

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[FlowController] ExecuteExit failed for {steamId} in zone {zone.Hash}: {ex.Message}");
            NotificationHelper.NotifyAdmins($"[BattleLuck] ExecuteExit failed for {steamId} in {config.ModeId}: {ex.Message}");
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>Execute a flow config for a player (synchronous — must run on main thread).</summary>
    public void ExecuteFlow(FlowConfig flowConfig, Entity playerCharacter, int zoneHash, GameModeContext? ctx = null)
    {
        foreach (var flowName in flowConfig.ExecutionOrder)
        {
            if (!flowConfig.Flows.TryGetValue(flowName, out var flow))
            {
                BattleLuckPlugin.LogWarning($"[FlowController] Flow '{flowName}' not found in config.");
                continue;
            }

            foreach (var actionStr in flow.Actions)
            {
                var success = ExecuteAction(actionStr, playerCharacter, zoneHash, ctx);
                if (!success)
                {
                    BattleLuckPlugin.LogError($"[FlowController] FAIL-FAST: Action '{actionStr}' failed. Aborting flow '{flowName}'.");
                    // Auto-rollback: restore snapshot if one was saved
                    try { _playerState.RestoreSnapshot(playerCharacter, zoneHash); }
                    catch (Exception ex) { BattleLuckPlugin.LogError($"[FlowController] Rollback also failed: {ex.Message}"); }
                    return;
                }
            }
        }
    }

    /// <summary>Parse and execute a single action. Returns false on critical failure (triggers rollback).</summary>
    bool ExecuteAction(string actionStr, Entity playerCharacter, int zoneHash, GameModeContext? ctx)
    {
        var parts = actionStr.Split(':', 2);
        var actionName = parts[0].Trim();
        var parameters = new Dictionary<string, string>();

        if (parts.Length > 1)
        {
            foreach (var param in parts[1].Split('|'))
            {
                var kv = param.Split('=', 2);
                if (kv.Length == 2)
                    parameters[kv[0].Trim()] = kv[1].Trim();
            }
        }

        try
        {
            switch (actionName)
            {
                // ── Core 5 actions ──────────────────────────────────────

                case "snapshot.save":
                case "snapshot.save_old":
                    _playerState.SaveSnapshot(playerCharacter, zoneHash);
                    break;

                case "kit.apply":
                    {
                        parameters.TryGetValue("kitId", out var kitId);
                        parameters.TryGetValue("modeId", out var modeId);
                        var id = modeId ?? kitId ?? "bloodbath";
                        var result = KitController.ApplyKit(playerCharacter, id);
                        if (!result.Success)
                        {
                            BattleLuckPlugin.LogError($"[FlowController] kit.apply FAILED: {result.Error}");
                            return false; // triggers fail-fast rollback
                        }
                    }
                    break;

                case "snapshot.restore":
                case "snapshot.restore_old":
                    _playerState.RestoreSnapshot(playerCharacter, zoneHash);
                    break;

                case "notify":
                case "send_message":
                    if (parameters.TryGetValue("message", out var msg))
                        ctx?.Broadcast?.Invoke(msg);
                    break;

                case "teleport":
                case "player.teleport":
                    if (parameters.TryGetValue("targetZoneHash", out var hashStr) && int.TryParse(hashStr, out int targetHash))
                        TeleportToZone(playerCharacter, targetHash);
                    break;

                // ── Legacy actions (backward compat) ────────────────────

                case "snapshot.mark_active":
                    ctx?.Players.Add(playerCharacter.GetSteamId());
                    break;

                case "snapshot.clear_active":
                    ctx?.Players.Remove(playerCharacter.GetSteamId());
                    break;

                case "state.clear_old":
                case "state.clear_zone":
                    _playerState.ClearSnapshot(playerCharacter.GetSteamId());
                    break;

                case "buff.clear_all":
                    BattleLuckPlugin.LogInfo("[FlowController] buff.clear_all — skipped (kit/heal handles state).");
                    break;

                case "ability.reset_slots":
                    AbilityController.ResetCooldowns(playerCharacter);
                    break;

                case "heal":
                    playerCharacter.HealToFull();
                    break;

                case "kit.apply_weapons":
                    KitController.ApplyWeaponsKit(playerCharacter);
                    break;

                case "kit.apply_armor":
                    KitController.ApplyArmorKit(playerCharacter);
                    break;

                case "level.set_max":
                    KitController.SetMaxLevel(playerCharacter);
                    break;

                case "ability.unlock_all":
                    AbilityController.UnlockAllAbilities(playerCharacter);
                    break;

                case "ability.unlock_school":
                    if (parameters.TryGetValue("school", out var school))
                        AbilityController.UnlockSchool(playerCharacter, school);
                    break;

                case "inventory.clear_kit":
                    {
                        parameters.TryGetValue("kitId", out var clearKitId);
                        ClearKitItems(playerCharacter, clearKitId ?? "bloodbath");
                    }
                    break;

                case "visual_enable":
                case "visual_disable":
                    break; // stub

                case "enable_pvp":
                    playerCharacter.SetTeam(zoneHash + (int)(playerCharacter.GetSteamId() % 1000));
                    break;

                case "disable_pvp":
                    playerCharacter.SetTeam(0);
                    break;

                case "set_blood":
                    SetPlayerBlood(playerCharacter, parameters, ctx);
                    break;

                case "mode.start":
                    if (parameters.TryGetValue("modeId", out var startModeId))
                        BattleLuckPlugin.LogInfo($"[FlowController] mode.start:{startModeId} — deferred to SessionController.");
                    break;

                case "mode.end":
                    if (parameters.TryGetValue("modeId", out var endModeId))
                    {
                        var mode = _registry.Resolve(endModeId);
                        if (mode != null && ctx != null) mode.OnEnd(ctx);
                    }
                    break;

                case "player.stun":
                    if (parameters.TryGetValue("durationSeconds", out var stunDurStr) && float.TryParse(stunDurStr, out var stunDur))
                    {
                        playerCharacter.TryApplyBuff(Prefabs.Buff_General_Freeze);
                        var capturedPlayer = playerCharacter;
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay((int)(stunDur * 1000));
                            try { capturedPlayer.TryRemoveBuff(Prefabs.Buff_General_Freeze); }
                            catch { }
                        });
                        BattleLuckPlugin.LogInfo($"[FlowController] Stunned player for {stunDur}s");
                    }
                    break;

                default:
                    BattleLuckPlugin.LogWarning($"[FlowController] Unknown action: {actionName}");
                    break;
            }
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogError($"[FlowController] Error executing '{actionName}': {ex.Message}");
            return false; // critical failure → rollback
        }

        return true;
    }

    void ClearKitItems(Entity playerCharacter, string kitId)
    {
        var prefabs = KitController.GetKitPrefabs(kitId);
        int removed = 0;
        foreach (var prefab in prefabs)
        {
            try
            {
                if (playerCharacter.TryRemoveItem(prefab, 99))
                    removed++;
            }
            catch { }
        }
        BattleLuckPlugin.LogInfo($"[FlowController] Cleared {removed} kit items (kit={kitId}) from player.");
    }

    void TeleportToZone(Entity playerCharacter, int targetZoneHash)
    {
        foreach (var modeId in new[] { "gauntlet", "bloodbath", "siege", "trials", "colosseum" })
        {
            var config = ConfigLoader.Load(modeId);
            foreach (var zone in config.Zones.Zones)
            {
                if (zone.Hash == targetZoneHash)
                {
                    playerCharacter.SetPosition(zone.TeleportSpawn.ToFloat3());
                    BattleLuckPlugin.LogInfo($"[FlowController] Teleported player to zone {zone.Name} ({targetZoneHash}).");
                    return;
                }
            }
        }
        BattleLuckPlugin.LogWarning($"[FlowController] Zone hash {targetZoneHash} not found.");
    }

    static readonly Dictionary<string, PrefabGUID> BloodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Scholar"]   = new PrefabGUID(-700632469),
        ["Warrior"]   = new PrefabGUID(-1094467405),
        ["Rogue"]     = new PrefabGUID(793735874),
        ["Brute"]     = new PrefabGUID(842225604),
        ["Worker"]    = new PrefabGUID(-1389286985),
        ["Creature"]  = new PrefabGUID(1897056612),
        ["Draculin"]  = new PrefabGUID(-1820256602),
    };

    void SetPlayerBlood(Entity playerCharacter, Dictionary<string, string> parameters, GameModeContext? ctx = null)
    {
        if (!playerCharacter.Has<Blood>()) return;

        parameters.TryGetValue("bloodType", out var bloodTypeName);
        parameters.TryGetValue("quality", out var qualityStr);

        float quality = 100f;
        if (!string.IsNullOrEmpty(qualityStr) && float.TryParse(qualityStr, out float q))
            quality = Math.Clamp(q, 0f, 100f);

        PrefabGUID bloodType = BloodTypes.GetValueOrDefault(bloodTypeName ?? "Scholar", new PrefabGUID(-700632469));
        string eventName = ctx?.ModeId ?? "unknown";

        playerCharacter.With((ref Blood blood) =>
        {
            blood.BloodType = bloodType;
            blood.Quality = quality;
            blood.Value = blood.MaxBlood;
        });

        BattleLuckPlugin.LogInfo($"[FlowController] Set blood to {bloodTypeName ?? "Scholar"} @ {quality}% for player ({eventName}).");
    }

    public static bool TryGetUser(Entity playerCharacter, out ProjectM.Network.User user)
    {
        user = default;
        if (!playerCharacter.Has<PlayerCharacter>())
            return false;

        var userEntity = playerCharacter.Read<PlayerCharacter>().UserEntity;
        if (!userEntity.Exists() || !userEntity.Has<ProjectM.Network.User>())
            return false;

        user = userEntity.Read<ProjectM.Network.User>();
        return true;
    }
}
