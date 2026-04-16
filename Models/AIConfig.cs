using System.Text.Json.Serialization;

namespace BattleLuck.Models
{
    public class AIConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("google_ai")]
        public GoogleAIStudioSettings GoogleAIStudio { get; set; } = new();

        [JsonPropertyName("ai_sidecar")]
        public BattleAiSidecarSettings Sidecar { get; set; } = new();

        [JsonPropertyName("messaging")]
        public MessagingSettings Messaging { get; set; } = new();

        [JsonPropertyName("privacy")]
        public PrivacySettings Privacy { get; set; } = new();
    }

    public class GoogleAIStudioSettings
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gemini-pro";

        [JsonPropertyName("max_requests_per_second")]
        public int MaxRequestsPerSecond { get; set; } = 10;

        [JsonPropertyName("temperature")]
        public float Temperature { get; set; } = 0.8f;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 300;

        [JsonPropertyName("fallback_models")]
        public List<string> FallbackModels { get; set; } = new()
        {
            "gemini-2.0-flash",
            "gemini-1.5-flash"
        };
    }

    public class MessagingSettings
    {
        [JsonPropertyName("message_cooldown_seconds")]
        public int MessageCooldownSeconds { get; set; } = 30;

        [JsonPropertyName("tip_cooldown_minutes")]
        public int TipCooldownMinutes { get; set; } = 5;

        [JsonPropertyName("context_retention_minutes")]
        public int ContextRetentionMinutes { get; set; } = 30;

        [JsonPropertyName("auto_tips_enabled")]
        public bool AutoTipsEnabled { get; set; } = true;

        [JsonPropertyName("welcome_messages_enabled")]
        public bool WelcomeMessagesEnabled { get; set; } = true;

        [JsonPropertyName("match_summaries_enabled")]
        public bool MatchSummariesEnabled { get; set; } = true;

        [JsonPropertyName("discord_webhook_url")]
        public string DiscordWebhookUrl { get; set; } = "";
    }

    public class PrivacySettings
    {
        [JsonPropertyName("opt_out_by_default")]
        public bool OptOutByDefault { get; set; } = false;

        [JsonPropertyName("allow_player_toggle")]
        public bool AllowPlayerToggle { get; set; } = true;

        [JsonPropertyName("store_conversation_history")]
        public bool StoreConversationHistory { get; set; } = true;

        [JsonPropertyName("max_conversation_history_size")]
        public int MaxConversationHistorySize { get; set; } = 20;
    }
}