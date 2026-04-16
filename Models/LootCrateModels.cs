using System.Text.Json.Serialization;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Config for loot crate spawning on the battle platform.</summary>
public sealed class LootCrateConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("spawnIntervalSec")]
    public float SpawnIntervalSec { get; set; } = 15f;

    [JsonPropertyName("maxActiveCrates")]
    public int MaxActiveCrates { get; set; } = 3;

    [JsonPropertyName("despawnAfterSec")]
    public float DespawnAfterSec { get; set; } = 10f;

    [JsonPropertyName("crateTypes")]
    public List<CrateTypeConfig> CrateTypes { get; set; } = new();
}

public sealed class CrateTypeConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("prefab")]
    public string Prefab { get; set; } = "";
}

public sealed class CrateInstance
{
    public Entity Entity { get; set; }
    public Entity GlowEntity { get; set; }
    public CrateTypeConfig Type { get; set; } = new();
    public DateTime SpawnedAt { get; set; }
    public float3 Position { get; set; }
}
