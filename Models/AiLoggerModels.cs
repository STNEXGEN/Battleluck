using System.Text.Json.Serialization;

public sealed class AiLoggerConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    // ── Primary: Azure OpenAI ──────────────────────────────────────────────────
    [JsonPropertyName("azureOpenAiEndpoint")]
    public string AzureOpenAiEndpoint { get; set; } = "";

    [JsonPropertyName("azureOpenAiKey")]
    public string AzureOpenAiKey { get; set; } = "";

    [JsonPropertyName("azureOpenAiDeployment")]
    public string AzureOpenAiDeployment { get; set; } = "gpt-4o-mini";

    // ── Secondary: Google Gemini ───────────────────────────────────────────────
    [JsonPropertyName("geminiApiKey")]
    public string GeminiApiKey { get; set; } = "";

    [JsonPropertyName("geminiModel")]
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    // ── Tertiary: Superuser AI Sidecar ─────────────────────────────────────────
    [JsonPropertyName("superuserSidecarUrl")]
    public string SuperuserSidecarUrl { get; set; } = "http://localhost:3000";

    [JsonPropertyName("superuserSidecarKey")]
    public string SuperuserSidecarKey { get; set; } = "";

    // ── Shared ─────────────────────────────────────────────────────────────────
    [JsonPropertyName("discordWebhookUrl")]
    public string DiscordWebhookUrl { get; set; } = "";

    [JsonPropertyName("discordServerId")]
    public string DiscordServerId { get; set; } = "";

    [JsonPropertyName("discordChannelId")]
    public string DiscordChannelId { get; set; } = "";

    [JsonPropertyName("bufferFlushIntervalSec")]
    public int BufferFlushIntervalSec { get; set; } = 60;

    [JsonPropertyName("maxBufferSize")]
    public int MaxBufferSize { get; set; } = 100;

    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; set; } = "You are a game event summarizer for a V Rising PvP arena mod called BattleLuck. Summarize the following game events into a brief, engaging narrative suitable for a Discord channel. Use 1-3 sentences.";

    /// <summary>True if at least one AI provider is configured.</summary>
    public bool HasAnyProvider =>
        (!string.IsNullOrWhiteSpace(AzureOpenAiEndpoint) && !string.IsNullOrWhiteSpace(AzureOpenAiKey)) ||
        !string.IsNullOrWhiteSpace(GeminiApiKey) ||
        !string.IsNullOrWhiteSpace(SuperuserSidecarUrl);
}

public sealed class GameEventEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("details")]
    public string Details { get; set; } = "";
}

public sealed class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("color")]
    public int Color { get; set; } = 0x5865F2;

    [JsonPropertyName("fields")]
    public List<EmbedField>? Fields { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public sealed class EmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}

public sealed class DiscordWebhookPayload
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed>? Embeds { get; set; }
}
