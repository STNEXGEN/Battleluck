using HarmonyLib;
using ProjectM;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Harmony patch on UnitSpawnerReactSystem.OnUpdate to catch spawned units
/// and execute post-spawn callbacks. Pattern from VAMP UnitSpawnerPatch.
///
/// SpawnService assigns a unique durationKey as the LifeTime.Duration,
/// which this patch matches to execute the correct callback and set the real duration.
/// </summary>
[HarmonyPatch]
internal static class UnitSpawnerPatch
{
    internal static readonly Dictionary<long, (float actualDuration, Action<Entity> Actions)> PostActions = new();

    [HarmonyPatch(typeof(UnitSpawnerReactSystem), nameof(UnitSpawnerReactSystem.OnUpdate))]
    [HarmonyPrefix]
    static void Prefix(UnitSpawnerReactSystem __instance)
    {
        if (PostActions.Count == 0) return;

        try
        {
            var entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    if (!entity.Has<LifeTime>()) continue;

                    var lifetimeComp = entity.Read<LifeTime>();
                    var durationKey = (long)Mathf.Round(lifetimeComp.Duration);

                    if (PostActions.TryGetValue(durationKey, out var unitData))
                    {
                        var (actualDuration, actions) = unitData;
                        PostActions.Remove(durationKey);

                        var endAction = actualDuration <= 0 ? LifeTimeEndAction.None : LifeTimeEndAction.Destroy;
                        entity.Write(new LifeTime
                        {
                            Duration = actualDuration,
                            EndAction = endAction
                        });

                        try
                        {
                            actions(entity);
                        }
                        catch (Exception ex)
                        {
                            BattleLuckPlugin.LogWarning($"[UnitSpawnerPatch] Post-spawn callback error: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception e)
        {
            BattleLuckPlugin.LogWarning($"[UnitSpawnerPatch] Error: {e.Message}");
        }
    }

    /// <summary>Generate a unique key that won't collide with existing entries.</summary>
    internal static long NextKey()
    {
        var rng = new System.Random();
        long key;
        int attempts = 10;
        do
        {
            key = rng.Next(10000) * 3;
            attempts--;
            if (attempts < 0)
                throw new Exception("Failed to generate unique UnitSpawner key");
        } while (PostActions.ContainsKey(key));
        return key;
    }
}
