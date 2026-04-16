using ProjectM;
using ProjectM.Scripting;
using Unity.Entities;

/// <summary>
/// Central static accessor for V Rising ECS singletons.
/// Mirrors Bloodcraft Core.cs pattern — lazy initialization from the server world.
/// SystemService is inlined (Bloodcraft's is a custom class, not a V Rising API).
/// </summary>
public static class VRisingCore
{
    static World? _server;
    static EntityManager? _entityManager;
    static ServerScriptMapper? _serverScriptMapper;
    static DebugEventsSystem? _debugEventsSystem;
    static PrefabCollectionSystem? _prefabCollectionSystem;
    static EntityQuery? _onlinePlayersQuery;

    public static bool IsReady => _server != null && _server.IsCreated;

    public static World Server
    {
        get => _server ?? throw new InvalidOperationException("VRisingCore not initialized.");
        private set => _server = value;
    }

    public static EntityManager EntityManager
    {
        get
        {
            _entityManager ??= Server.EntityManager;
            return _entityManager.Value;
        }
    }

    public static ServerScriptMapper ServerScriptMapper
    {
        get
        {
            _serverScriptMapper ??= GetSystem<ServerScriptMapper>();
            return _serverScriptMapper;
        }
    }

    /// <summary>
    /// Fresh reference every call — ServerGameManager is a struct.
    /// Caching a copy causes stale native pointers → AccessViolationException.
    /// </summary>
    public static ServerGameManager ServerGameManager
        => ServerScriptMapper.GetServerGameManager();

    public static DebugEventsSystem DebugEventsSystem
    {
        get
        {
            _debugEventsSystem ??= GetSystem<DebugEventsSystem>();
            return _debugEventsSystem;
        }
    }

    public static PrefabCollectionSystem PrefabCollectionSystem
    {
        get
        {
            _prefabCollectionSystem ??= GetSystem<PrefabCollectionSystem>();
            return _prefabCollectionSystem;
        }
    }

    /// <summary>
    /// Initialize from the V Rising dedicated server world.
    /// Call once during plugin Load after the server world is available.
    /// </summary>
    public static void Initialize()
    {
        _server = GetServerWorld();
        if (_server == null)
        {
            BattleLuckPlugin.LogWarning("[VRisingCore] Server world not found — will retry on first access.");
            return;
        }

        // Force eager init so downstream callers don't race.
        _ = EntityManager;
        BattleLuckPlugin.LogInfo("[VRisingCore] Initialized successfully.");
    }

    /// <summary>Reset cached references (call on server restart / world teardown).</summary>
    public static void Reset()
    {
        _server = null;
        _entityManager = null;
        _serverScriptMapper = null;
        _debugEventsSystem = null;
        _prefabCollectionSystem = null;
        _onlinePlayersQuery = null;
    }

    static T GetSystem<T>() where T : ComponentSystemBase
    {
        return Server.GetExistingSystemManaged<T>()
            ?? throw new InvalidOperationException($"System {typeof(T).Name} not found in server world.");
    }

    static World? GetServerWorld()
    {
        foreach (var world in World.All)
        {
            if (world.Name == "Server")
                return world;
        }
        return null;
    }

    /// <summary>
    /// Get all online player character entities.
    /// </summary>
    public static List<Entity> GetOnlinePlayers()
    {
        _onlinePlayersQuery ??= EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerCharacter>());
        var entities = _onlinePlayersQuery.Value.ToEntityArray(Allocator.Temp);
        var players = new List<Entity>(entities.Length);
        for (int i = 0; i < entities.Length; i++)
            players.Add(entities[i]);
        entities.Dispose();
        return players;
    }
}
