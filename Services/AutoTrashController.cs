using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Automatically destroys items dropped on the ground inside active mode zones.
/// Queries for ItemPickup entities each tick and removes those within zone boundaries.
/// </summary>
public sealed class AutoTrashController
{
    readonly Dictionary<int, ZoneDefinition> _activeZones = new();
    EntityQuery _itemPickupQuery;
    bool _initialized;
    bool _enabled = true;

    DateTime _lastSweep = DateTime.UtcNow;
    const int SweepIntervalMs = 1000; // check every 1 second
    int _totalTrashed;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public int TotalTrashed => _totalTrashed;

    public void Initialize()
    {
        var em = VRisingCore.EntityManager;
        _itemPickupQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<ItemPickup>(),
            ComponentType.ReadOnly<Translation>()
        );
        _initialized = true;
        BattleLuckPlugin.LogInfo("[AutoTrash] Initialized.");
    }

    /// <summary>
    /// Register a zone as active — items dropped here will be destroyed.
    /// </summary>
    public void RegisterZone(int zoneHash, ZoneDefinition zone)
    {
        _activeZones[zoneHash] = zone;
    }

    /// <summary>
    /// Unregister a zone — items will no longer be auto-trashed there.
    /// </summary>
    public void UnregisterZone(int zoneHash)
    {
        _activeZones.Remove(zoneHash);
    }

    public void ClearZones() => _activeZones.Clear();

    /// <summary>
    /// Called each server tick. Sweeps for dropped items inside active zones and destroys them.
    /// </summary>
    public void Tick()
    {
        if (!_enabled || !_initialized || _activeZones.Count == 0) return;

        var now = DateTime.UtcNow;
        if ((now - _lastSweep).TotalMilliseconds < SweepIntervalMs) return;
        _lastSweep = now;

        var em = VRisingCore.EntityManager;
        var entities = _itemPickupQuery.ToEntityArray(Allocator.Temp);

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!em.Exists(entity)) continue;

                float3 pos;
                if (em.HasComponent<Translation>(entity))
                    pos = em.GetComponentData<Translation>(entity).Value;
                else
                    continue;

                foreach (var kv in _activeZones)
                {
                    var zone = kv.Value;
                    var zoneCenter = new float3(zone.Position.X, zone.Position.Y, zone.Position.Z);
                    float dist = math.distance(new float2(pos.x, pos.z), new float2(zoneCenter.x, zoneCenter.z));

                    if (dist <= zone.Radius)
                    {
                        em.DestroyEntity(entity);
                        _totalTrashed++;
                        break;
                    }
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
