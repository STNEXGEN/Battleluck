using HarmonyLib;
using ProjectM;
using Unity.Entities;
using BattleLuck.Core;
using BattleLuck.Models;
using BattleLuck.Utilities;

[BepInPlugin("gg.battleluck", "BattleLuck", "1.0.0")]
public class BattleLuckPlugin : BasePlugin
{
    public new static ManualLogSource? Log { get; private set; }
    public static GameModeRegistry? GameModes { get; private set; }
    public static SessionController? Session { get; private set; }
    public static AIAssistant? AIAssistant { get; private set; }
    static DiscordBridgeController? _discordBridge;
    static AiLoggerController? _aiLogger;

    /// <summary>Broadcast a message to all online players (stub — wire to server API).</summary>
    public static Action<string, string>? BroadcastToSession { get; set; }

    static Harmony? _harmony;
    static bool _initialized;
    static EntityQuery? _playerQuery;

    public static bool IsInitialized => _initialized;
    public static bool IsDiscordBridgeEnabled => _discordBridge != null;

    public static void SetAIAssistant(AIAssistant? assistant)
    {
        AIAssistant = assistant;
    }

    public static void PostToDiscordLogs(string message)
    {
        _discordBridge?.PostToLogs(message);
    }

    public static void PostToDiscordChatVip(string message)
    {
        _discordBridge?.PostToChatVip(message);
    }

    public static bool TryNotifyPlayerBySteamId(ulong steamId, string message)
    {
        if (!VRisingCore.IsReady)
            return false;

        foreach (var player in VRisingCore.GetOnlinePlayers())
        {
            if (!player.Exists() || !player.IsPlayer() || player.GetSteamId() != steamId)
                continue;

            if (FlowController.TryGetUser(player, out var user))
            {
                NotificationHelper.NotifyPlayer(user, message);
                return true;
            }
        }

        return false;
    }

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo("[BattleLuck] Loading...");
        ConfigLoader.EnsureDefaultsDeployed();

        GameModes = new GameModeRegistry();
        GameModes.Register(new GauntletMode());
        GameModes.Register(new BloodbathMode());
        GameModes.Register(new SiegeMode());
        GameModes.Register(new TrialsMode());
        GameModes.Register(new ColosseumMode());
        GameModes.Register(new AiEventMode());

        // Apply Harmony patches for death detection
        _harmony = new Harmony("gg.battleluck.patches");
        _harmony.PatchAll(typeof(DeathHook).Assembly);

        CommandRegistry.RegisterAll();

        Log.LogInfo("[BattleLuck] Loaded — 6 game modes registered. Waiting for server world...");
    }

    /// <summary>
    /// Called once each server tick. Initializes VRising core on first ready tick,
    /// then drives the session controller.
    /// Must be called from a BepInEx update hook or coroutine.
    /// </summary>
    public static void ServerTick(float deltaSeconds)
    {
        if (!_initialized) return;

        try
        {
            // Cache the query — creating a new EntityQuery every frame leaks native memory in IL2CPP.
            // Query PlayerCharacter (character entities) not User (separate entity).
            // Character entities have Translation for position, PlayerCharacter for identity.
            if (_playerQuery == null)
                _playerQuery = VRisingCore.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerCharacter>());

            EntityQuery pq = (EntityQuery)_playerQuery;
            var entities = pq.ToEntityArray(Unity.Collections.Allocator.Temp);

            var players = new List<Entity>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
                players.Add(entities[i]);
            entities.Dispose();

            Session?.Tick(players, deltaSeconds);
            MainThreadDispatcher.ProcessQueue();
            AIAssistant?.ProcessMainThreadQueue();
            AIAssistant?.CleanupOldContexts(); // Cleanup old contexts periodically
            _discordBridge?.DrainMainThreadQueue();
            _aiLogger?.Tick();
            FloorLockService.Tick(players);
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[BattleLuck] Tick error: {ex.Message}");
        }
    }

    public static bool TryInitializeCore()
    {
        try
        {
            VRisingCore.Initialize();
            if (!VRisingCore.IsReady)
                return false;

            var playerState = new PlayerStateController();
            var flow = new FlowController(playerState, GameModes!);
            var zoneDetection = new ZoneDetectionSystem();
            zoneDetection.Initialize();

            Session = new SessionController(GameModes!, playerState, flow, zoneDetection);
            Session.Initialize();

            // Initialize Discord bridge companion endpoint
            try
            {
                var discordConfig = ConfigLoader.LoadDiscordBridgeConfig();
                if (discordConfig?.Enabled == true)
                {
                    _discordBridge = new DiscordBridgeController();
                    _discordBridge.Configure(discordConfig);
                    _discordBridge.Start();
                    Log?.LogInfo($"[BattleLuck] Discord bridge enabled on port {discordConfig.Port}");
                }
                else
                {
                    Log?.LogInfo("[BattleLuck] Discord bridge disabled in configuration");
                }
            }
            catch (Exception discordEx)
            {
                Log?.LogWarning($"[BattleLuck] Failed to initialize Discord bridge: {discordEx.Message}");
            }

            // Initialize AI Assistant
            try
            {
                var aiConfig = ConfigLoader.LoadAIConfig();
                if (aiConfig.Enabled && !string.IsNullOrEmpty(aiConfig.GoogleAIStudio.ApiKey))
                {
                    AIAssistant = new AIAssistant();
                    AIAssistant.Initialize(aiConfig);

                    var providerSummary = AIAssistant.IsSidecarConfigured
                        ? $"Google AI Studio + battle sidecar ({AIAssistant.SidecarBaseUrl})"
                        : "Google AI Studio";

                    Log?.LogInfo($"[BattleLuck] AI Assistant initialized successfully with {providerSummary}");
                }
                else
                {
                    Log?.LogInfo("[BattleLuck] AI Assistant disabled in configuration");
                }
            }
            catch (Exception aiEx)
            {
                Log?.LogWarning($"[BattleLuck] Failed to initialize AI Assistant: {aiEx.Message}");
            }

            // Initialize AI Logger (game event → AI summary → Discord webhook)
            try
            {
                var aiLoggerConfig = ConfigLoader.LoadAiLoggerConfig();
                if (aiLoggerConfig?.Enabled == true)
                {
                    _aiLogger = new AiLoggerController();
                    _aiLogger.Configure(aiLoggerConfig);
                    Log?.LogInfo("[BattleLuck] AI Logger initialized");
                }
            }
            catch (Exception loggerEx)
            {
                Log?.LogWarning($"[BattleLuck] Failed to initialize AI Logger: {loggerEx.Message}");
            }

            // Resolve live buff GUIDs (must run after VRisingCore.Initialize)
            PrefabHelper.ScanLivePrefabs();
            Prefabs.ResolveLiveBuffGuids();

            // Toggle building restrictions with event lifecycle
            GameEvents.OnModeStarted += _ => BuildingRestrictionController.DisableRestrictions();
            GameEvents.OnModeEnded += _ => BuildingRestrictionController.EnableRestrictions();

            // Wire VFX sequences to action events
            GameEvents.OnActionPerformed += HandleActionVFX;

            _initialized = true;
            Log?.LogInfo("[BattleLuck] V Rising core initialized. Session controller active.");
            return true;
        }
        catch (Exception ex)
        {
            Log?.LogError($"[BattleLuck] TryInitializeCore failed: {ex}");
            return false;
        }
    }

    public override bool Unload()
    {
        BuildingRestrictionController.Reset();
        FloorLockService.Reset();
        AIAssistant?.Shutdown();
        AIAssistant = null;
        _discordBridge?.Dispose();
        _discordBridge = null;
        _aiLogger?.Dispose();
        _aiLogger = null;
        Session?.Shutdown();
        _harmony?.UnpatchSelf();
        GameEvents.Shutdown();
        VRisingCore.Reset();
        _playerQuery = null;
        _initialized = false;
        Log?.LogInfo("[BattleLuck] Unloaded.");
        return true;
    }

    public static void LogInfo(string msg) => Log?.LogInfo(msg);
    public static void LogWarning(string msg) => Log?.LogWarning(msg);
    public static void LogError(string msg) => Log?.LogError(msg);

    static void HandleActionVFX(ActionPerformedEvent e)
    {
        try
        {
            if (e.PlayerEntity.HasValue && e.PlayerEntity.Value.Exists())
            {
                var seq = ActionSequences.GetVFX(e.Action);
                e.PlayerEntity.Value.PlaySequence(seq, label: $"action_{e.Action}");
            }
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"[BattleLuck] VFX error for {e.Action}: {ex.Message}");
        }
    }
}
