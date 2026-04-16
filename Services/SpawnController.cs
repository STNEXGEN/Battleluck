using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Spawns enemies, bosses, and VBloods for PvE game modes.
/// Uses UnitSpawnerUpdateSystem.SpawnUnit with callback pattern from VAMP SpawnService.
/// Also supports direct InstantiateEntityImmediate for immediate spawns.
/// </summary>
public sealed class SpawnController
{
    readonly Dictionary<Entity, SpawnedUnit> _tracked = new();

    // ── Common enemy / boss prefab GUIDs ────────────────────────────────

    // Regular enemies (varying difficulty)
    public static readonly PrefabGUID Skeleton_Warrior = new(-1584807109);
    public static readonly PrefabGUID Skeleton_Mage = new(-539289064);
    public static readonly PrefabGUID Skeleton_Archer = new(-1340402506);
    public static readonly PrefabGUID Ghoul = new(-1508186605);
    public static readonly PrefabGUID Bandit_Thug = new(1458281806);
    public static readonly PrefabGUID Bandit_Hunter = new(-1000550829);
    public static readonly PrefabGUID Militia_Guard = new(-1101895538);
    public static readonly PrefabGUID Militia_Devoted = new(1820387430);
    public static readonly PrefabGUID Church_Paladin = new(-1791316508);
    public static readonly PrefabGUID Vampire_Cultist = new(-707081968);

    // Elite / mini-boss enemies
    public static readonly PrefabGUID Bandit_Bomber = new(-1090756563);
    public static readonly PrefabGUID Church_Captain = new(1090737596);
    public static readonly PrefabGUID Bear_Dire = new(-1391546585);
    public static readonly PrefabGUID Werewolf = new(1885959949);
    public static readonly PrefabGUID Golem_Stone = new(543834575);

    // VBlood bosses (world bosses)
    public static readonly PrefabGUID VBlood_Errol = new(-484556888);
    public static readonly PrefabGUID VBlood_Grayson = new(1106149033);
    public static readonly PrefabGUID VBlood_Putrid = new(-1905691330);
    public static readonly PrefabGUID VBlood_Keely = new(-1065970933);
    public static readonly PrefabGUID VBlood_Nicholaus = new(153390636);
    public static readonly PrefabGUID VBlood_Quincey = new(-680831417);
    public static readonly PrefabGUID VBlood_Jade = new(-1968372384);
    public static readonly PrefabGUID VBlood_Octavian = new(1688478381);
    public static readonly PrefabGUID VBlood_Dracula = new(-327335305);

    // Wave difficulty tiers (low → high)
    public static readonly List<PrefabGUID> Tier1Enemies = new() { Skeleton_Warrior, Skeleton_Archer, Skeleton_Mage };
    public static readonly List<PrefabGUID> Tier2Enemies = new() { Ghoul, Bandit_Thug, Bandit_Hunter };
    public static readonly List<PrefabGUID> Tier3Enemies = new() { Militia_Guard, Militia_Devoted, Church_Paladin };
    public static readonly List<PrefabGUID> Tier4Enemies = new() { Bandit_Bomber, Church_Captain, Vampire_Cultist };
    public static readonly List<PrefabGUID> EliteEnemies = new() { Bear_Dire, Werewolf, Golem_Stone };

    /// <summary>
    /// Spawn a unit using UnitSpawnerUpdateSystem (proper game spawn with AI/pathfinding).
    /// The callback fires once the entity is fully initialized by the game engine.
    /// </summary>
    public void SpawnWithCallback(PrefabGUID prefab, float3 position, float duration = 0f, Action<Entity>? postActions = null)
    {
        var usus = VRisingCore.Server.GetExistingSystemManaged<UnitSpawnerUpdateSystem>();
        var durationKey = UnitSpawnerPatch.NextKey();

        usus.SpawnUnit(Entity.Null, prefab, position, 1, 1, 1, durationKey);

        UnitSpawnerPatch.PostActions[durationKey] = (duration, entity =>
        {
            // Apply standard post-spawn fixes
            ApplyPostSpawnFixes(entity);

            // Track the entity
            _tracked[entity] = new SpawnedUnit
            {
                Entity = entity,
                Prefab = prefab,
                SpawnPosition = position,
                SpawnedAtUtc = DateTime.UtcNow
            };

            BattleLuckPlugin.LogInfo($"[SpawnController] Unit spawned via callback: {prefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0})");

            // Execute additional post-spawn actions
            postActions?.Invoke(entity);
        });
    }

    /// <summary>
    /// Spawn a unit immediately using InstantiateEntityImmediate (no AI initialization).
    /// Use SpawnWithCallback for full NPC spawns with AI/pathfinding.
    /// </summary>
    public Entity SpawnImmediate(PrefabGUID prefab, float3 position, Entity? owner = null)
    {
        var ownerEntity = owner ?? Entity.Null;
        var entity = EntityExtensions.SpawnUnit(prefab, ownerEntity, position);

        if (entity.Exists())
        {
            _tracked[entity] = new SpawnedUnit
            {
                Entity = entity,
                Prefab = prefab,
                SpawnPosition = position,
                SpawnedAtUtc = DateTime.UtcNow
            };
        }

        return entity;
    }

    /// <summary>Spawn a wave of enemies with proper AI using UnitSpawnerUpdateSystem.</summary>
    public void SpawnWave(List<PrefabGUID> prefabs, int count, float3 center, float spread = 5f, Action<List<Entity>>? onWaveComplete = null)
    {
        var rng = new System.Random();
        int spawned = 0;
        var entities = new List<Entity>();

        for (int i = 0; i < count; i++)
        {
            var prefab = prefabs[rng.Next(prefabs.Count)];
            var offset = new float3(
                (float)(rng.NextDouble() * 2 - 1) * spread,
                0,
                (float)(rng.NextDouble() * 2 - 1) * spread
            );
            var pos = center + offset;
            int capturedIndex = i;

            SpawnWithCallback(prefab, pos, duration: 0f, entity =>
            {
                entities.Add(entity);
                spawned++;
                if (spawned >= count)
                {
                    BattleLuckPlugin.LogInfo($"[SpawnController] Wave complete: {entities.Count}/{count} units.");
                    onWaveComplete?.Invoke(entities);
                }
            });
        }
    }

    /// <summary>Spawn a boss with proper AI using UnitSpawnerUpdateSystem.</summary>
    public void SpawnBoss(PrefabGUID bossPrefab, float3 position, Action<Entity>? onSpawned = null)
    {
        SpawnWithCallback(bossPrefab, position, duration: 0f, entity =>
        {
            BattleLuckPlugin.LogInfo($"[SpawnController] Boss spawned: {bossPrefab.GuidHash} at ({position.x:F0}, {position.y:F0}, {position.z:F0})");
            onSpawned?.Invoke(entity);
        });
    }

    /// <summary>Apply standard post-spawn fixes (prevent disable, remove drops, remove convertable).</summary>
    static void ApplyPostSpawnFixes(Entity entity)
    {
        var em = VRisingCore.EntityManager;

        // Prevent entity from being disabled when no players nearby
        if (!em.HasComponent<CanPreventDisableWhenNoPlayersInRange>(entity))
            em.AddComponent<CanPreventDisableWhenNoPlayersInRange>(entity);
        entity.With((ref CanPreventDisableWhenNoPlayersInRange c) => c.CanDisable = new ModifiableBool(false));

        // Remove drop tables
        if (em.HasComponent<DropTableBuffer>(entity))
        {
            var sgm = VRisingCore.ServerGameManager;
            if (sgm.TryGetBuffer<DropTableBuffer>(entity, out var dropBuffer))
            {
                for (int i = 0; i < dropBuffer.Length; i++)
                {
                    var item = dropBuffer[i];
                    item.DropTableGuid = PrefabGUID.Empty;
                    item.DropTrigger = DropTriggerType.OnSalvageDestroy;
                    dropBuffer[i] = item;
                }
            }
        }

        // Remove convertability
        if (entity.Has<ServantConvertable>())
            em.RemoveComponent<ServantConvertable>(entity);
        if (entity.Has<CharmSource>())
            em.RemoveComponent<CharmSource>(entity);
    }

    /// <summary>Get the appropriate enemy tier for a wave number.</summary>
    public static List<PrefabGUID> GetEnemiesForWave(int waveNumber)
    {
        return waveNumber switch
        {
            <= 2 => Tier1Enemies,
            <= 4 => Tier2Enemies,
            <= 6 => Tier3Enemies,
            <= 8 => Tier4Enemies,
            _ => EliteEnemies
        };
    }

    /// <summary>Record that a tracked entity was killed. Returns true if it was tracked.</summary>
    public bool RecordKill(Entity entity)
    {
        return _tracked.Remove(entity);
    }

    /// <summary>Destroy all tracked spawned entities.</summary>
    public void DespawnAll()
    {
        foreach (var kv in _tracked)
        {
            if (kv.Key.Exists())
                kv.Key.DestroyWithReason();
        }
        _tracked.Clear();
        BattleLuckPlugin.LogInfo("[SpawnController] Despawned all tracked units.");
    }

    /// <summary>Number of currently tracked (alive) spawned units.</summary>
    public int AliveCount => _tracked.Count(kv => kv.Key.Exists());

    /// <summary>Get all tracked entities.</summary>
    public IReadOnlyDictionary<Entity, SpawnedUnit> TrackedUnits => _tracked;

    public void Reset()
    {
        DespawnAll();
    }
}

public sealed class SpawnedUnit
{
    public Entity Entity { get; set; }
    public PrefabGUID Prefab { get; set; }
    public float3 SpawnPosition { get; set; }
    public DateTime SpawnedAtUtc { get; set; }
}
