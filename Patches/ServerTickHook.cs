using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.WarEvents;

/// <summary>
/// One-shot initialization: hooks WarEventRegistrySystem.RegisterWarEventEntities
/// which fires after the server world is fully ready (same pattern as Bloodcraft).
/// </summary>
[HarmonyPatch]
internal static class InitializationHook
{
    [HarmonyPatch(typeof(WarEventRegistrySystem), nameof(WarEventRegistrySystem.RegisterWarEventEntities))]
    [HarmonyPostfix]
    static void RegisterWarEventEntitiesPostfix()
    {
        try
        {
            BattleLuckPlugin.TryInitializeCore();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BattleLuck] Init failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Periodic tick: hooks BuffSystem_Spawn_Server.OnUpdate which fires every server frame.
/// Drives zone detection, session ticks, and mode logic.
/// </summary>
[HarmonyPatch]
internal static class ServerTickHook
{
    static DateTime _lastTick = DateTime.UtcNow;

    [HarmonyPatch(typeof(BuffSystem_Spawn_Server), nameof(BuffSystem_Spawn_Server.OnUpdate))]
    [HarmonyPostfix]
    static void OnUpdatePostfix()
    {
        // Fallback init: if the WarEvent hook didn't fire, try here
        if (!BattleLuckPlugin.IsInitialized)
        {
            try { BattleLuckPlugin.TryInitializeCore(); }
            catch { }
            if (!BattleLuckPlugin.IsInitialized) return;
        }

        try
        {
            var now = DateTime.UtcNow;
            float delta = (float)(now - _lastTick).TotalSeconds;
            _lastTick = now;

            // Clamp to avoid huge deltas on first tick or lag spikes
            if (delta > 2f) delta = 0.016f;

            BattleLuckPlugin.ServerTick(delta);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[BattleLuck] Tick error: {ex.Message}");
        }
    }
}
