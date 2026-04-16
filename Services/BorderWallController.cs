using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Math = System.Math;

/// <summary>
/// Spawns a ring of wall entities around a zone center using proper V Rising entity instantiation.
/// Pattern: Instantiate from prefab entity → set Translation/Rotation → PhysicsCustomTags.
/// Staggered spawning: N walls per tick to avoid server spikes.
/// Hard cap: MAX_WALLS = 100.
/// </summary>
public sealed class BorderWallController
{
    const int MAX_WALLS = 100;
    const int MAX_FLOORS = 500;
    const int FLOOR_BATCH_SIZE = 20;

    struct PendingWall
    {
        public float3 Position;
        public float Angle;
    }

    struct PendingFloor
    {
        public float3 Position;
    }

    readonly List<Entity> _spawnedWalls = new();
    readonly Queue<PendingWall> _pendingWalls = new();
    readonly HashSet<long> _queuedWallTiles = new();
    readonly HashSet<long> _processedWallTiles = new();
    readonly List<Entity> _spawnedFloors = new();
    readonly Queue<PendingFloor> _pendingFloors = new();
    readonly HashSet<long> _queuedFloorTiles = new();
    readonly HashSet<long> _processedFloorTiles = new();
    readonly object _scanSync = new();
    float3 _center;
    float _radius;
    float _height;
    int _batchSize;
    bool _spawning;
    bool _spawningFloors;
    PrefabGUID _wallPrefab;
    PrefabGUID _floorPrefab;
    float _floorSpacing = 3f;
    WallBoundaryConfig? _lastConfig;

    // ── Event → wall prefab mapping ─────────────────────────────────────
    static readonly Dictionary<string, string> _eventWallPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bloodbath"]  = "TM_Castle_Wall_Tier02_Stone",
        ["colosseum"]  = "TM_Castle_Wall_Tier03_Marble",
        ["gauntlet"]   = "TM_Castle_Wall_Tier01_Wood",
        ["siege"]      = "TM_Castle_Wall_Tier02_Stone",
        ["trials"]     = "TM_Castle_Wall_Tier03_Marble",
    };

    // ── Event → floor prefab mapping ────────────────────────────────────
    static readonly Dictionary<string, string> _eventFloorPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bloodbath"]  = "TM_Castle_Floor_Tier02_Stone",
        ["colosseum"]  = "TM_Castle_Floor_Tier03_Marble",
        ["gauntlet"]   = "TM_Castle_Floor_Tier01_Wood",
        ["siege"]      = "TM_Castle_Floor_Tier02_Stone",
        ["trials"]     = "TM_Castle_Floor_Tier03_Marble",
    };

    const string DefaultWallPrefabName = "TM_Castle_Wall_Tier02_Stone";
    const string DefaultFloorPrefabName = "TM_Castle_Floor_Tier02_Stone";

    /// <summary>
    /// Begin staggered wall ring spawn. Call once, then call TickSpawn() each frame.
    /// </summary>
    public void StartWallRing(float3 center, float radius, WallBoundaryConfig config)
    {
        lock (_scanSync)
        {
            DespawnWalls();

            _center = center;
            _radius = radius;
            _height = config.Height;
            _batchSize = Math.Max(1, config.BatchSize);
            _lastConfig = config;
            _spawning = true;
            // _wallPrefab is set by caller (SpawnEventBorder) or defaults
            if (_wallPrefab == default)
                _wallPrefab = ResolveBoundaryPrefab(DefaultWallPrefabName, "wall");

            float spacing = Math.Max(1f, config.Spacing);
            int wallCount = Math.Min(MAX_WALLS, (int)(2f * math.PI * radius / spacing));
            int queued = 0;
            int deduped = 0;

            for (int i = 0; i < wallCount; i++)
            {
                float angle = 2f * math.PI * i / wallCount;
                float rawX = center.x + radius * math.cos(angle);
                float rawZ = center.z + radius * math.sin(angle);

                // Snap to tile grid (V Rising tiles are 2.5 world units = 5 tile units)
                float3 pos = new(
                    math.round(rawX / 2.5f) * 2.5f,
                    center.y + _height,
                    math.round(rawZ / 2.5f) * 2.5f
                );

                long tileKey = ToTileKey(pos);
                if (!_queuedWallTiles.Add(tileKey))
                {
                    deduped++;
                    continue;
                }

                _pendingWalls.Enqueue(new PendingWall { Position = pos, Angle = angle });
                queued++;
            }

            BattleLuckPlugin.LogInfo($"[BorderWallController] Queued {queued} unique walls (deduped={deduped}, radius={radius:F0}, spacing={spacing:F0}).");
        }
    }

    /// <summary>Spawn next batch of walls. Returns true while still spawning.</summary>
    public bool TickSpawn()
    {
        lock (_scanSync)
        {
            if (!_spawning || _pendingWalls.Count == 0)
            {
                _spawning = false;
                return false;
            }

            var em = VRisingCore.EntityManager;
            var pcs = VRisingCore.PrefabCollectionSystem;

            if (!pcs._PrefabGuidToEntityMap.TryGetValue(_wallPrefab, out var prefabEntity))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Wall prefab {_wallPrefab.GuidHash} not found in PrefabGuidToEntityMap.");
                _spawning = false;
                _pendingWalls.Clear();
                _queuedWallTiles.Clear();
                return false;
            }

            int toSpawn = Math.Min(_batchSize, _pendingWalls.Count);
            for (int i = 0; i < toSpawn; i++)
            {
                var pending = _pendingWalls.Dequeue();
                long tileKey = ToTileKey(pending.Position);
                _queuedWallTiles.Remove(tileKey);
                if (!_processedWallTiles.Add(tileKey))
                {
                    BattleLuckPlugin.LogWarning($"[BorderWallController] Duplicate wall processing prevented at {pending.Position}.");
                    continue;
                }

                try
                {
                    var entity = em.Instantiate(prefabEntity);

                    // Set world position
                    if (!entity.Has<Translation>()) em.AddComponent<Translation>(entity);
                    entity.Write(new Translation { Value = pending.Position });
                    if (entity.Has<LastTranslation>())
                        entity.Write(new LastTranslation { Value = pending.Position });

                    // Set rotation
                    if (!entity.Has<Rotation>()) em.AddComponent<Rotation>(entity);
                    entity.Write(new Rotation { Value = quaternion.RotateY(pending.Angle) });

                    // Set tile grid position (world coords × 2, floored to int)
                    var tilePos = new int2(
                        (int)math.floor(pending.Position.x * 2f),
                        (int)math.floor(pending.Position.z * 2f));
                    if (!entity.Has<TilePosition>()) em.AddComponent<TilePosition>(entity);
                    entity.Write(new TilePosition { Tile = tilePos });

                    // Set tile AABB bounds (single tile)
                    if (!entity.Has<TileBounds>()) em.AddComponent<TileBounds>(entity);
                    entity.Write(new TileBounds
                    {
                        Value = new BoundsMinMax { Min = tilePos, Max = tilePos }
                    });

                    // Mark as schematic-spawned entity
                    if (!entity.Has<PhysicsCustomTags>()) em.AddComponent<PhysicsCustomTags>(entity);

                    // Make immortal so players can't dismantle
                    if (entity.Has<EditableTileModel>())
                    {
                        var etm = entity.Read<EditableTileModel>();
                        etm.CanDismantle = false;
                        entity.Write(etm);
                    }

                    _spawnedWalls.Add(entity);
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[BorderWallController] Failed to spawn wall at {pending.Position}: {ex.Message}");
                }
            }

            if (_pendingWalls.Count == 0)
            {
                _spawning = false;
                BattleLuckPlugin.LogInfo($"[BorderWallController] All {_spawnedWalls.Count} walls spawned (processed={_processedWallTiles.Count}).");
            }

            return _pendingWalls.Count > 0;
        }
    }

    /// <summary>Destroy all tracked wall entities.</summary>
    public void DespawnWalls()
    {
        lock (_scanSync)
        {
            var em = VRisingCore.EntityManager;
            int destroyed = 0;
            foreach (var wall in _spawnedWalls)
            {
                try
                {
                    if (em.Exists(wall))
                    {
                        wall.Destroy();
                        destroyed++;
                    }
                }
                catch { }
            }
            _spawnedWalls.Clear();
            _pendingWalls.Clear();
            _queuedWallTiles.Clear();
            _processedWallTiles.Clear();
            _spawning = false;

            if (destroyed > 0)
                BattleLuckPlugin.LogInfo($"[BorderWallController] Despawned {destroyed} walls.");
        }
    }

    /// <summary>Move all walls to new center (same ring shape, new origin).</summary>
    public void UpdateCenter(float3 newCenter, float radius)
    {
        lock (_scanSync)
        {
            if (_spawnedWalls.Count == 0) return;

            int wallCount = _spawnedWalls.Count;
            for (int i = 0; i < wallCount; i++)
            {
                float angle = 2f * math.PI * i / wallCount;
                float3 pos = new(
                    newCenter.x + radius * math.cos(angle),
                    newCenter.y + _height,
                    newCenter.z + radius * math.sin(angle)
                );
                try
                {
                    if (_spawnedWalls[i].Exists())
                    {
                        _spawnedWalls[i].SetPosition(pos);
                    }
                }
                catch { }
            }
            _center = newCenter;
        }
    }

    public bool IsSpawning => _spawning;
    public bool IsSpawningFloors => _spawningFloors;
    public int WallCount => _spawnedWalls.Count;
    public int FloorCount => _spawnedFloors.Count;

    // ── Floor ring spawning ─────────────────────────────────────────────

    /// <summary>
    /// Queue a box floor fill centered on the zone. Call TickSpawnFloors() each frame.
    /// </summary>
    public void StartFloorRing(float3 center, float radius, float spacing = 2.5f)
    {
        lock (_scanSync)
        {
            DespawnFloors();

            _floorSpacing = Math.Max(1f, spacing);
            _spawningFloors = true;

            if (_floorPrefab == default)
                _floorPrefab = ResolveBoundaryPrefab(DefaultFloorPrefabName, "floor");

            // Fill a square grid instead of a circular area.
            float targetHalfExtent = Math.Max(0f, radius - 1f);
            int tilesPerSide = (int)(2f * targetHalfExtent / _floorSpacing) + 1;
            float halfExtent = (tilesPerSide - 1) * _floorSpacing / 2f;
            int queued = 0;
            int deduped = 0;

            for (int row = 0; row < tilesPerSide && queued < MAX_FLOORS; row++)
            {
                for (int col = 0; col < tilesPerSide && queued < MAX_FLOORS; col++)
                {
                    float localX = col * _floorSpacing - halfExtent;
                    float localZ = row * _floorSpacing - halfExtent;

                    // Snap to tile grid
                    float worldX = math.round((center.x + localX) / 2.5f) * 2.5f;
                    float worldZ = math.round((center.z + localZ) / 2.5f) * 2.5f;
                    float3 tilePos = new float3(worldX, center.y, worldZ);

                    long tileKey = ToTileKey(tilePos);
                    if (!_queuedFloorTiles.Add(tileKey))
                    {
                        deduped++;
                        continue;
                    }

                    _pendingFloors.Enqueue(new PendingFloor { Position = tilePos });
                    queued++;
                }
            }

            BattleLuckPlugin.LogInfo($"[BorderWallController] Queued {queued} unique floor tiles (reason=box-fill, deduped={deduped}, halfExtent={halfExtent:F0}, spacing={_floorSpacing:F1}).");
        }
    }

    /// <summary>Spawn next batch of floor tiles. Returns true while still spawning.</summary>
    public bool TickSpawnFloors()
    {
        lock (_scanSync)
        {
            if (!_spawningFloors || _pendingFloors.Count == 0)
            {
                _spawningFloors = false;
                return false;
            }

            var em = VRisingCore.EntityManager;
            var pcs = VRisingCore.PrefabCollectionSystem;

            if (!pcs._PrefabGuidToEntityMap.TryGetValue(_floorPrefab, out var prefabEntity))
            {
                BattleLuckPlugin.LogWarning($"[BorderWallController] Floor prefab {_floorPrefab.GuidHash} not found.");
                _spawningFloors = false;
                _pendingFloors.Clear();
                _queuedFloorTiles.Clear();
                return false;
            }

            int toSpawn = Math.Min(FLOOR_BATCH_SIZE, _pendingFloors.Count);
            int newlySearched = 0;
            int duplicateProcessSkipped = 0;
            for (int i = 0; i < toSpawn; i++)
            {
                var pending = _pendingFloors.Dequeue();
                long tileKey = ToTileKey(pending.Position);
                _queuedFloorTiles.Remove(tileKey);
                if (!_processedFloorTiles.Add(tileKey))
                {
                    duplicateProcessSkipped++;
                    continue;
                }

                newlySearched++;

                try
                {
                    var entity = em.Instantiate(prefabEntity);

                    // Set world position
                    if (!entity.Has<Translation>()) em.AddComponent<Translation>(entity);
                    entity.Write(new Translation { Value = pending.Position });
                    if (entity.Has<LastTranslation>())
                        entity.Write(new LastTranslation { Value = pending.Position });

                    // Set rotation
                    if (!entity.Has<Rotation>()) em.AddComponent<Rotation>(entity);
                    entity.Write(new Rotation { Value = quaternion.identity });

                    // Set tile grid position (world coords × 2, floored to int)
                    var tilePos = new int2(
                        (int)math.floor(pending.Position.x * 2f),
                        (int)math.floor(pending.Position.z * 2f));
                    if (!entity.Has<TilePosition>()) em.AddComponent<TilePosition>(entity);
                    entity.Write(new TilePosition { Tile = tilePos });

                    // Set tile AABB bounds (single tile)
                    if (!entity.Has<TileBounds>()) em.AddComponent<TileBounds>(entity);
                    entity.Write(new TileBounds
                    {
                        Value = new BoundsMinMax { Min = tilePos, Max = tilePos }
                    });

                    // Mark as schematic-spawned entity
                    if (!entity.Has<PhysicsCustomTags>()) em.AddComponent<PhysicsCustomTags>(entity);

                    // Make immortal so players can't dismantle
                    if (entity.Has<EditableTileModel>())
                    {
                        var etm = entity.Read<EditableTileModel>();
                        etm.CanDismantle = false;
                        entity.Write(etm);
                    }

                    _spawnedFloors.Add(entity);
                }
                catch (Exception ex)
                {
                    BattleLuckPlugin.LogWarning($"[BorderWallController] Failed to spawn floor at {pending.Position}: {ex.Message}");
                }
            }

            if (newlySearched > 0 || duplicateProcessSkipped > 0)
            {
                BattleLuckPlugin.LogInfo($"[BorderWallController] Floor search update: newlySearched={newlySearched}, duplicateProcessSkipped={duplicateProcessSkipped} (reason=first-process).");
            }

            if (_pendingFloors.Count == 0)
            {
                _spawningFloors = false;
                BattleLuckPlugin.LogInfo($"[BorderWallController] All {_spawnedFloors.Count} floor tiles spawned (searched={_processedFloorTiles.Count}).");
            }

            return _pendingFloors.Count > 0;
        }
    }

    /// <summary>Destroy all tracked floor entities.</summary>
    public void DespawnFloors()
    {
        lock (_scanSync)
        {
            int destroyed = 0;
            foreach (var floor in _spawnedFloors)
            {
                try { if (floor.Exists()) { floor.Destroy(); destroyed++; } }
                catch { }
            }
            _spawnedFloors.Clear();
            _pendingFloors.Clear();
            _queuedFloorTiles.Clear();
            _processedFloorTiles.Clear();
            _spawningFloors = false;

            if (destroyed > 0)
                BattleLuckPlugin.LogInfo($"[BorderWallController] Despawned {destroyed} floor tiles.");
        }
    }

    // ── Unified zone boundary ───────────────────────────────────────────

    /// <summary>
    /// Spawn floor boundary tiles for an event in one call.
    /// Usage: StartZoneBoundary("bloodbath", zoneCenter, 25f, wallConfig)
    /// </summary>
    public void StartZoneBoundary(string eventName, float3 center, float radius, WallBoundaryConfig config)
    {
        // Pick event-specific floor prefab
        var floorPrefabName = _eventFloorPrefabs.TryGetValue(eventName, out var fp)
            ? fp : DefaultFloorPrefabName;

        _floorPrefab = ResolveBoundaryPrefab(floorPrefabName, "floor");

        BattleLuckPlugin.LogInfo($"[BorderWallController] StartZoneBoundary '{eventName}' → floors={_floorPrefab.GuidHash} (floor-only mode)");

        // Floor-only: do not queue wall ring from session boundary start.
        StartFloorRing(center, radius);
    }

    /// <summary>Tick floor spawning. Returns true while floors are still spawning.</summary>
    public bool TickSpawnAll()
    {
        bool f = TickSpawnFloors();
        return f;
    }

    // ── Event-triggered spawning ────────────────────────────────────────

    /// <summary>
    /// Spawn an event-specific wall border. Picks the right prefab for the event.
    /// Usage: SpawnEventBorder("bloodbath", zoneCenter, 25f, config)
    /// </summary>
    public void SpawnEventBorder(string eventName, float3 center, float radius, WallBoundaryConfig config)
    {
        var wallPrefabName = _eventWallPrefabs.TryGetValue(eventName, out var prefab)
            ? prefab
            : DefaultWallPrefabName;

        _wallPrefab = ResolveBoundaryPrefab(wallPrefabName, "wall");

        BattleLuckPlugin.LogInfo($"[BorderWallController] Event '{eventName}' → wall prefab {_wallPrefab.GuidHash}");
        StartWallRing(center, radius, config);
    }

    /// <summary>Clean up walls and floors at event end.</summary>
    public void OnEventEnd()
    {
        DespawnWalls();
        DespawnFloors();
        BattleLuckPlugin.LogInfo("[BorderWallController] Event border cleared.");
    }

    // ── Zone heartbeat: re-check and repair destroyed walls ─────────────

    /// <summary>
    /// Call periodically (e.g. every 5s) to detect and respawn any walls destroyed mid-event.
    /// Returns the number of walls repaired.
    /// </summary>
    public int ZoneHeartbeat()
    {
        lock (_scanSync)
        {
            if (_spawnedWalls.Count == 0 || _lastConfig == null) return 0;

            var em = VRisingCore.EntityManager;
            var pcs = VRisingCore.PrefabCollectionSystem;

            if (!pcs._PrefabGuidToEntityMap.TryGetValue(_wallPrefab, out var prefabEntity))
                return 0;

            int wallCount = _spawnedWalls.Count;
            int repaired = 0;

            for (int i = 0; i < wallCount; i++)
            {
                if (_spawnedWalls[i].Exists()) continue;

                // Wall was destroyed — respawn at original position
                float angle = 2f * math.PI * i / wallCount;
                float3 pos = new(
                    _center.x + _radius * math.cos(angle),
                    _center.y + _height,
                    _center.z + _radius * math.sin(angle)
                );

                try
                {
                    var entity = em.Instantiate(prefabEntity);
                    entity.SetPosition(pos);
                    entity.Write(new Rotation { Value = quaternion.RotateY(angle) });
                    _spawnedWalls[i] = entity;
                    repaired++;
                }
                catch { }
            }

            if (repaired > 0)
                BattleLuckPlugin.LogInfo($"[BorderWallController] Heartbeat repaired {repaired} walls.");

            return repaired;
        }
    }

    public void Reset()
    {
        DespawnWalls();
        DespawnFloors();
    }

    static PrefabGUID ResolveBoundaryPrefab(string prefabName, string context)
    {
        if (PrefabHelper.TryGetValidPrefabGuidDeep(prefabName, out var guid))
            return guid;

        BattleLuckPlugin.LogWarning($"[BorderWallController] Could not resolve {context} prefab '{prefabName}' from live prefab map.");
        return PrefabGUID.Empty;
    }

    static long ToTileKey(float3 position)
    {
        int x = (int)math.round(position.x * 2f);
        int z = (int)math.round(position.z * 2f);
        return ((long)x << 32) ^ (uint)z;
    }
}
