using System.Text.Json.Serialization;
using System.Reflection;
using BattleLuck.Models;

/// <summary>
/// Loads and caches per-mode configuration from JSON files.
/// Config root: {BepInEx}/config/BattleLuck/{modeId}/
/// </summary>
public static class ConfigLoader
{
    static readonly Dictionary<string, ModeConfig> _cache = new();
    static AIConfig? _aiConfig;
    static string? _configRoot;
    static bool _defaultsEnsured;

    public static string ConfigRoot
    {
        get
        {
            if (_configRoot == null)
            {
                _configRoot = Path.Combine(BepInEx.Paths.ConfigPath, "BattleLuck");
            }
            return _configRoot;
        }
        set
        {
            _configRoot = value;
            _defaultsEnsured = false;
            _aiConfig = null; // Reset cached AI config
        }
    }

    public static AIConfig LoadAIConfig()
    {
        if (_aiConfig != null)
            return _aiConfig;

        EnsureDefaultsDeployed();

        var aiConfigPath = Path.Combine(ConfigRoot, "ai_config.json");
        _aiConfig = LoadJson<AIConfig>(aiConfigPath) ?? new AIConfig();

        return _aiConfig;
    }

    public static DiscordBridgeConfig? LoadDiscordBridgeConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "discord_bridge.json");
        return LoadJson<DiscordBridgeConfig>(path);
    }

    public static WebhookConfig? LoadWebhookConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "webhook.json");
        return LoadJson<WebhookConfig>(path);
    }

    public static AiLoggerConfig? LoadAiLoggerConfig()
    {
        EnsureDefaultsDeployed();
        var path = Path.Combine(ConfigRoot, "ai_logger.json");
        return LoadJson<AiLoggerConfig>(path);
    }

    public static void ReloadAIConfig()
    {
        _aiConfig = null;
        LoadAIConfig();
    }

    public static ModeConfig Load(string modeId)
    {
        EnsureDefaultsDeployed();

        if (_cache.TryGetValue(modeId, out var cached))
            return cached;

        var dir = Path.Combine(ConfigRoot, modeId);
        var config = new ModeConfig { ModeId = modeId };

        config.Session = LoadJson<SessionConfig>(Path.Combine(dir, "session.json")) ?? new SessionConfig();
        config.Zones = LoadJson<ZonesConfig>(Path.Combine(dir, "zones.json")) ?? new ZonesConfig();
        config.FlowEnter = LoadJson<FlowConfig>(Path.Combine(dir, "flow_enter.json")) ?? new FlowConfig();
        config.FlowExit = LoadJson<FlowConfig>(Path.Combine(dir, "flow_exit.json")) ?? new FlowConfig();
        config.Kit = LoadJson<KitConfig>(Path.Combine(dir, "kit.json"));

        _cache[modeId] = config;
        return config;
    }

    public static void Reload(string modeId)
    {
        _cache.Remove(modeId);
        Load(modeId);
    }

    public static void ReloadAll()
    {
        var ids = _cache.Keys.ToList();
        _cache.Clear();
        foreach (var id in ids) Load(id);
    }

    public static void EnsureDefaultsDeployed()
    {
        if (_defaultsEnsured)
            return;

        _defaultsEnsured = true;

        try
        {
            Directory.CreateDirectory(ConfigRoot);

            var assembly = typeof(BattleLuckPlugin).Assembly;
            var assemblyName = assembly.GetName().Name ?? nameof(BattleLuckPlugin);
            var resourcePrefix = $"{assemblyName}.config.BattleLuck.";
            var deployed = 0;

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal))
                    continue;

                var relativePath = ToRelativeConfigPath(resourceName, resourcePrefix);
                if (string.IsNullOrEmpty(relativePath))
                    continue;

                var targetPath = Path.Combine(ConfigRoot, relativePath);
                if (File.Exists(targetPath))
                    continue;

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    continue;

                using var file = File.Create(targetPath);
                stream.CopyTo(file);
                deployed++;
            }

            if (deployed > 0)
                BattleLuckPlugin.LogInfo($"[ConfigLoader] Deployed {deployed} default config file(s) to {ConfigRoot}");
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Failed to deploy default configs: {ex.Message}");
        }
    }

    static T? LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Missing config: {path}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[ConfigLoader] Error loading {path}: {ex.Message}");
            return null;
        }
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static string? ToRelativeConfigPath(string resourceName, string resourcePrefix)
    {
        var remainder = resourceName[resourcePrefix.Length..];
        var parts = remainder.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var fileName = $"{parts[^2]}.{parts[^1]}";
        if (parts.Length == 2)
            return fileName;

        var relativeDir = Path.Combine(parts.Take(parts.Length - 2).ToArray());
        return Path.Combine(relativeDir, fileName);
    }
}

// ── Data models ─────────────────────────────────────────────────────────

public sealed class ModeConfig
{
    public string ModeId { get; set; } = "";
    public SessionConfig Session { get; set; } = new();
    public ZonesConfig Zones { get; set; } = new();
    public FlowConfig FlowEnter { get; set; } = new();
    public FlowConfig FlowExit { get; set; } = new();
    public KitConfig? Kit { get; set; }
}

// ── session.json ────────────────────────────────────────────────────────

public sealed class SessionConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("useEcs")]
    public bool UseEcs { get; set; } = true;

    [JsonPropertyName("startDelay")]
    public DelayConfig StartDelay { get; set; } = new();

    [JsonPropertyName("rules")]
    public SessionRules Rules { get; set; } = new();
}

public sealed class SessionRules
{
    [JsonPropertyName("minPlayers")]
    public int MinPlayers { get; set; } = 1;

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; } = 4;

    [JsonPropertyName("enablePvP")]
    public bool EnablePvP { get; set; }

    [JsonPropertyName("enableVBloods")]
    public bool EnableVBloods { get; set; }

    [JsonPropertyName("enableEliteMobs")]
    public bool EnableEliteMobs { get; set; }

    [JsonPropertyName("matchDurationMinutes")]
    public int MatchDurationMinutes { get; set; } = 10;

    [JsonPropertyName("allowLateJoin")]
    public bool AllowLateJoin { get; set; }

    [JsonPropertyName("requireReadyCheck")]
    public bool RequireReadyCheck { get; set; }

    [JsonPropertyName("restrictGear")]
    public bool RestrictGear { get; set; }

    [JsonPropertyName("shareLoot")]
    public bool ShareLoot { get; set; }

    [JsonPropertyName("resetOnExit")]
    public bool ResetOnExit { get; set; } = true;

    [JsonPropertyName("eliminationMode")]
    public bool EliminationMode { get; set; }

    [JsonPropertyName("livesPerPlayer")]
    public int LivesPerPlayer { get; set; } = 3;
}

public sealed class DelayConfig
{
    [JsonPropertyName("seconds")]
    public int Seconds { get; set; }
}

// ── zones.json ──────────────────────────────────────────────────────────

public sealed class ZonesConfig
{
    [JsonPropertyName("detection")]
    public DetectionConfig Detection { get; set; } = new();

    [JsonPropertyName("autoEnter")]
    public AutoEnterConfig AutoEnter { get; set; } = new();

    [JsonPropertyName("zones")]
    public List<ZoneDefinition> Zones { get; set; } = new();
}

public sealed class DetectionConfig
{
    [JsonPropertyName("checkIntervalMs")]
    public int CheckIntervalMs { get; set; } = 500;

    [JsonPropertyName("positionThreshold")]
    public float PositionThreshold { get; set; } = 1.0f;
}

public sealed class AutoEnterConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("enterOnConnect")]
    public bool EnterOnConnect { get; set; }

    [JsonPropertyName("enterOnSpawn")]
    public bool EnterOnSpawn { get; set; } = true;

    [JsonPropertyName("exitOnDisconnect")]
    public bool ExitOnDisconnect { get; set; } = true;

    [JsonPropertyName("tickIntervalMs")]
    public int TickIntervalMs { get; set; } = 250;

    [JsonPropertyName("spawnResolveTimeoutMs")]
    public int SpawnResolveTimeoutMs { get; set; } = 15000;
}

public sealed class ZoneDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("hash")]
    public int Hash { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;

    [JsonPropertyName("kitId")]
    public string KitId { get; set; } = "";

    [JsonPropertyName("position")]
    public Vec3Config Position { get; set; } = new();

    [JsonPropertyName("teleportSpawn")]
    public Vec3Config TeleportSpawn { get; set; } = new();

    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 60f;

    [JsonPropertyName("exitRadius")]
    public float ExitRadius { get; set; } = 65f;

    [JsonPropertyName("isSafe")]
    public bool IsSafe { get; set; }

    [JsonPropertyName("blockedActions")]
    public List<string> BlockedActions { get; set; } = new();

    [JsonPropertyName("boundary")]
    public BoundaryConfig? Boundary { get; set; }

    [JsonPropertyName("waypoints")]
    public WaypointConfig? Waypoints { get; set; }

    [JsonPropertyName("glowBorder")]
    public GlowBorderConfig? GlowBorder { get; set; }

    [JsonPropertyName("movingPlatform")]
    public MovingPlatformConfig? MovingPlatform { get; set; }

    [JsonPropertyName("lootCrates")]
    public LootCrateConfig? LootCrates { get; set; }

    [JsonPropertyName("glow")]
    public GlowConfig? Glow { get; set; }

    [JsonPropertyName("bosses")]
    public BossesConfig? Bosses { get; set; }
}

/// <summary>Boundary enforcement config per zone.</summary>
public sealed class BoundaryConfig
{
    /// <summary>Policy: dot_only, teleport_only, walls_only, dot_and_walls, none</summary>
    [JsonPropertyName("policy")]
    public string Policy { get; set; } = "none";

    [JsonPropertyName("dot")]
    public DotBoundaryConfig? Dot { get; set; }

    [JsonPropertyName("walls")]
    public WallBoundaryConfig? Walls { get; set; }
}

public sealed class DotBoundaryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("warningRadiusPercent")]
    public float WarningRadiusPercent { get; set; } = 0.80f;

    [JsonPropertyName("dangerRadiusPercent")]
    public float DangerRadiusPercent { get; set; } = 0.95f;

    [JsonPropertyName("teleportOnExit")]
    public bool TeleportOnExit { get; set; } = true;
}

public sealed class WallBoundaryConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("height")]
    public float Height { get; set; } = 15f;

    [JsonPropertyName("spacing")]
    public float Spacing { get; set; } = 5f;

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;
}

public sealed class Vec3Config
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    public Unity.Mathematics.float3 ToFloat3() => new(X, Y, Z);
}

// ── flow_enter.json / flow_exit.json ────────────────────────────────────

public sealed class FlowConfig
{
    [JsonPropertyName("delayBefore")]
    public DelayConfig DelayBefore { get; set; } = new();

    [JsonPropertyName("delayBetweenFlows")]
    public DelayConfig DelayBetweenFlows { get; set; } = new();

    [JsonPropertyName("delayBetweenActions")]
    public DelayConfig DelayBetweenActions { get; set; } = new();

    [JsonPropertyName("executionOrder")]
    public List<string> ExecutionOrder { get; set; } = new();

    [JsonPropertyName("flows")]
    public Dictionary<string, FlowDefinition> Flows { get; set; } = new();
}

public sealed class FlowDefinition
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("delayBetweenActions")]
    public DelayConfig DelayBetweenActions { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();
}
