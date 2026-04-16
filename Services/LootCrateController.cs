using Unity.Entities;
using Unity.Mathematics;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
/// <summary>
/// Weighted random loot crate spawner. Crates spawn on the platform, auto-collected
/// by proximity (XZ distance < 1.5m). Crates move with center delta.
/// </summary>
public sealed class LootCrateController
{
    readonly List<CrateInstance> _activeCrates = new();
    readonly System.Random _rng = new();
    LootCrateConfig? _config;
    float _spawnTimer;
    bool _configured;

    public int ActiveCount => _activeCrates.Count;

    public void Configure(LootCrateConfig? config)
    {
        if (config == null || !config.Enabled || config.CrateTypes.Count == 0)
        {
            _configured = false;
            return;
        }
        _config = config;
        _spawnTimer = config.SpawnIntervalSec;
        _configured = true;
    }

    /// <summary>
    /// Tick crate spawning, despawning, and proximity collection.
    /// centerPos = current arena center. gridHalfExtent = platform half-size for spawn bounds.
    /// Returns list of collected crate types for the caller to apply effects.
    /// </summary>
    public List<(Entity player, CrateTypeConfig crate)> Tick(
        float3 centerPos, float gridHalfExtent, float deltaSeconds,
        IEnumerable<Entity> players, string sessionId)
    {
        var collected = new List<(Entity, CrateTypeConfig)>();
        if (!_configured || _config == null) return collected;

        // Despawn expired crates
        DespawnExpired();

        // Spawn timer
        _spawnTimer -= deltaSeconds;
        if (_spawnTimer <= 0 && _activeCrates.Count < _config.MaxActiveCrates)
        {
            _spawnTimer = _config.SpawnIntervalSec;
            SpawnCrate(centerPos, gridHalfExtent);
        }

        // Proximity collection
        var em = VRisingCore.EntityManager;
        foreach (var player in players)
        {
            try
            {
                if (!em.Exists(player)) continue;
                var playerPos = player.GetPosition();

                for (int i = _activeCrates.Count - 1; i >= 0; i--)
                {
                    var crate = _activeCrates[i];
                    float dist = math.distance(playerPos.xz, crate.Position.xz);
                    if (dist < 1.5f)
                    {
                        collected.Add((player, crate.Type));
                        DestroyCrate(crate);
                        _activeCrates.RemoveAt(i);

                        GameEvents.OnCrateCollected?.Invoke(new CrateCollectedEvent
                        {
                            SessionId = sessionId,
                            SteamId = player.GetSteamId(),
                            CrateId = crate.Type.Type,
                            LootTable = crate.Type.Prefab
                        });
                    }
                }
            }
            catch { /* skip unreadable players */ }
        }

        return collected;
    }

    /// <summary>Sync crate positions after platform moves.</summary>
    public void SyncCratePositions(float3 centerDelta)
    {
        if (math.lengthsq(centerDelta.xz) < 0.0001f) return;
        var em = VRisingCore.EntityManager;
        foreach (var crate in _activeCrates)
        {
            crate.Position += centerDelta;
            try
            {
                if (em.Exists(crate.Entity))
                    crate.Entity.SetPosition(crate.Position);
            }
            catch { /* best-effort */ }
        }
    }

    public void ClearAllCrates()
    {
        foreach (var crate in _activeCrates)
            DestroyCrate(crate);
        _activeCrates.Clear();
        _spawnTimer = _config?.SpawnIntervalSec ?? 15f;
    }

    void SpawnCrate(float3 center, float halfExtent)
    {
        if (_config == null) return;

        var type = SelectWeightedRandom();
        if (type == null) return;

        // Random position on platform
        float rx = (float)(_rng.NextDouble() * 2 - 1) * halfExtent * 0.8f;
        float rz = (float)(_rng.NextDouble() * 2 - 1) * halfExtent * 0.8f;
        var pos = new float3(center.x + rx, center.y + 0.5f, center.z + rz);

        try
        {
            var em = VRisingCore.EntityManager;
            var entity = em.CreateEntity();
            entity.SetPosition(pos);

            _activeCrates.Add(new CrateInstance
            {
                Entity = entity,
                Type = type,
                SpawnedAt = DateTime.UtcNow,
                Position = pos
            });
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[LootCrate] Spawn failed: {ex.Message}");
        }
    }

    CrateTypeConfig? SelectWeightedRandom()
    {
        if (_config == null || _config.CrateTypes.Count == 0) return null;

        int totalWeight = 0;
        foreach (var ct in _config.CrateTypes) totalWeight += ct.Weight;
        if (totalWeight <= 0) return null;

        int roll = _rng.Next(totalWeight);
        int cumulative = 0;
        foreach (var ct in _config.CrateTypes)
        {
            cumulative += ct.Weight;
            if (roll < cumulative) return ct;
        }
        return _config.CrateTypes[^1];
    }

    void DespawnExpired()
    {
        if (_config == null) return;
        var now = DateTime.UtcNow;
        for (int i = _activeCrates.Count - 1; i >= 0; i--)
        {
            if ((now - _activeCrates[i].SpawnedAt).TotalSeconds >= _config.DespawnAfterSec)
            {
                DestroyCrate(_activeCrates[i]);
                _activeCrates.RemoveAt(i);
            }
        }

        // FIFO cleanup if over max
        while (_config != null && _activeCrates.Count > _config.MaxActiveCrates)
        {
            DestroyCrate(_activeCrates[0]);
            _activeCrates.RemoveAt(0);
        }
    }

    void DestroyCrate(CrateInstance crate)
    {
        try
        {
            var em = VRisingCore.EntityManager;
            if (em.Exists(crate.Entity))
                em.DestroyEntity(crate.Entity);
            if (em.Exists(crate.GlowEntity))
                em.DestroyEntity(crate.GlowEntity);
        }
        catch { /* best-effort */ }
    }
}
