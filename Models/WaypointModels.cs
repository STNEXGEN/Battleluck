using System.Text.Json.Serialization;

/// <summary>
/// Config models for dynamic waypoint movement and glow border scanning.
/// </summary>
public sealed class WaypointConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("points")]
    public List<Vec3Config> Points { get; set; } = new();

    [JsonPropertyName("moveIntervalSec")]
    public float MoveIntervalSec { get; set; } = 5f;

    [JsonPropertyName("moveSpeed")]
    public float MoveSpeed { get; set; } = 8f;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; } = true;

    [JsonPropertyName("radiusShrinkPerWaypoint")]
    public float RadiusShrinkPerWaypoint { get; set; } = 3f;

    [JsonPropertyName("minimumRadius")]
    public float MinimumRadius { get; set; } = 15f;
}

public sealed class GlowBorderConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("scanWidth")]
    public float ScanWidth { get; set; } = 10f;

    [JsonPropertyName("maxGlowEntities")]
    public int MaxGlowEntities { get; set; } = 50;

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;
}
