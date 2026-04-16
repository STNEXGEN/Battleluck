using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;

/// <summary>
/// Subscribes to GameEvents, buffers them, and periodically sends an AI-summarized
/// narrative to a Discord webhook. Tries providers in order:
///   1. Azure OpenAI (primary)
///   2. Google Gemini (secondary)
///   3. Superuser AI Sidecar (tertiary)
/// </summary>
public sealed class AiLoggerController : IDisposable
{
    AiLoggerConfig? _config;
    readonly ConcurrentQueue<GameEventEntry> _buffer = new();
    readonly HttpClient _http = new();
    DateTime _lastFlush = DateTime.UtcNow;
    bool _configured;
    int _bufferCount;

    public void Configure(AiLoggerConfig? config)
    {
        if (config == null || !config.Enabled ||
            string.IsNullOrWhiteSpace(config.DiscordWebhookUrl) ||
            !config.HasAnyProvider)
        {
            _configured = false;
            return;
        }
        _config = config;
        _configured = true;

        var providers = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.AzureOpenAiEndpoint) && !string.IsNullOrWhiteSpace(config.AzureOpenAiKey))
            providers.Add("Azure OpenAI (primary)");
        if (!string.IsNullOrWhiteSpace(config.GeminiApiKey))
            providers.Add("Gemini (secondary)");
        if (!string.IsNullOrWhiteSpace(config.SuperuserSidecarUrl))
            providers.Add("Superuser Sidecar (tertiary)");

        SubscribeToEvents();
        BattleLuckPlugin.LogInfo($"[AiLogger] Configured with providers: {string.Join(", ", providers)}");
    }

    void SubscribeToEvents()
    {
        GameEvents.OnZoneEnter += e => BufferEvent("PlayerJoin", $"Player {e.SteamId} entered zone {e.ZoneId}");
        GameEvents.OnZoneExit += e => BufferEvent("PlayerLeave", $"Player {e.SteamId} exited zone {e.ZoneId}");
        GameEvents.OnPlayerScored += e => BufferEvent("PlayerKill", $"Player {e.SteamId} scored {e.Points} pts — {e.Reason}");
        GameEvents.OnRoundEnded += e => BufferEvent("RoundEnd", $"Round {e.RoundNumber} ended in {e.ModeId}");
        GameEvents.OnModeStarted += e => BufferEvent("SessionStart", $"Session started: {e.ModeId}");
        GameEvents.OnModeEnded += e => BufferEvent("SessionEnd", $"Session ended: {e.ModeId}");
        GameEvents.OnZoneShrink += e => BufferEvent("ZoneShrink", $"Zone shrank to radius {e.NewRadius:F1}");
        GameEvents.OnBossSpawned += e => BufferEvent("BossSpawn", $"Boss spawned (prefab {e.PrefabGuid})");
        GameEvents.OnCrateCollected += e => BufferEvent("CrateCollected", $"Player {e.SteamId} collected crate {e.CrateId}");
    }

    void BufferEvent(string type, string details)
    {
        if (!_configured || _config == null) return;
        if (_bufferCount >= _config.MaxBufferSize)
        {
            _buffer.TryDequeue(out _);
            _bufferCount--;
        }
        _buffer.Enqueue(new GameEventEntry
        {
            Timestamp = DateTime.UtcNow,
            Type = type,
            Details = details
        });
        _bufferCount++;
    }

    /// <summary>Call from main tick. Flushes buffer when interval has elapsed.</summary>
    public void Tick()
    {
        if (!_configured || _config == null) return;
        if ((DateTime.UtcNow - _lastFlush).TotalSeconds < _config.BufferFlushIntervalSec) return;
        if (_buffer.IsEmpty) return;

        _lastFlush = DateTime.UtcNow;
        var events = new List<GameEventEntry>();
        while (_buffer.TryDequeue(out var entry))
            events.Add(entry);
        _bufferCount = 0;

        _ = Task.Run(() => FlushAsync(events));
    }

    async Task FlushAsync(List<GameEventEntry> events)
    {
        try
        {
            var eventsText = string.Join("\n", events.Select(e =>
                $"[{e.Timestamp:HH:mm:ss}] {e.Type}: {e.Details}"));

            var summary = await GetAiSummaryAsync(eventsText);
            if (string.IsNullOrWhiteSpace(summary)) return;

            await PostToDiscordAsync(summary, events.Count);
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AiLogger] Flush failed: {ex.Message}");
        }
    }

    async Task<string?> GetAiSummaryAsync(string eventsText)
    {
        if (_config == null) return null;

        // ── 1. Azure OpenAI (primary) ──────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(_config.AzureOpenAiEndpoint) &&
            !string.IsNullOrWhiteSpace(_config.AzureOpenAiKey))
        {
            var result = await TryAzureOpenAIAsync(eventsText);
            if (result != null) return result;
            BattleLuckPlugin.LogWarning("[AiLogger] Azure OpenAI failed, falling back to Gemini");
        }

        // ── 2. Google Gemini (secondary) ───────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(_config.GeminiApiKey))
        {
            var result = await TryGeminiAsync(eventsText);
            if (result != null) return result;
            BattleLuckPlugin.LogWarning("[AiLogger] Gemini failed, falling back to Superuser sidecar");
        }

        // ── 3. Superuser AI Sidecar (tertiary) ─────────────────────────────────
        if (!string.IsNullOrWhiteSpace(_config.SuperuserSidecarUrl))
            return await TrySidecarAsync(eventsText);

        return null;
    }

    async Task<string?> TryAzureOpenAIAsync(string eventsText)
    {
        if (_config == null) return null;
        try
        {
            var url = $"{_config.AzureOpenAiEndpoint.TrimEnd('/')}/openai/deployments/{_config.AzureOpenAiDeployment}/chat/completions?api-version=2024-02-01";
            var payload = new
            {
                messages = new[]
                {
                    new { role = "system", content = _config.SystemPrompt },
                    new { role = "user", content = eventsText }
                },
                max_tokens = 300,
                temperature = 0.7
            };
            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("api-key", _config.AzureOpenAiKey);
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                BattleLuckPlugin.LogWarning($"[AiLogger] Azure OpenAI returned {response.StatusCode}");
                return null;
            }
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AiLogger] Azure OpenAI exception: {ex.Message}");
            return null;
        }
    }

    async Task<string?> TryGeminiAsync(string eventsText)
    {
        if (_config == null) return null;
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.GeminiModel}:generateContent?key={_config.GeminiApiKey}";
            var payload = new
            {
                system_instruction = new { parts = new[] { new { text = _config.SystemPrompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = eventsText } } } },
                generationConfig = new { maxOutputTokens = 300, temperature = 0.7 }
            };
            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                BattleLuckPlugin.LogWarning($"[AiLogger] Gemini returned {response.StatusCode}");
                return null;
            }
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AiLogger] Gemini exception: {ex.Message}");
            return null;
        }
    }

    async Task<string?> TrySidecarAsync(string eventsText)
    {
        if (_config == null) return null;
        try
        {
            var url = $"{_config.SuperuserSidecarUrl.TrimEnd('/')}/api/query/enrich";
            var payload = new
            {
                query = eventsText,
                player = new { steamId = "ailogger", recentEvents = Array.Empty<string>() }
            };
            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(_config.SuperuserSidecarKey))
                request.Headers.Add("Authorization", $"Bearer {_config.SuperuserSidecarKey}");
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                BattleLuckPlugin.LogWarning($"[AiLogger] Superuser sidecar returned {response.StatusCode}");
                return null;
            }
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("summary", out var s) ? s.GetString() : null;
        }
        catch (Exception ex)
        {
            BattleLuckPlugin.LogWarning($"[AiLogger] Superuser sidecar exception: {ex.Message}");
            return null;
        }
    }

    async Task PostToDiscordAsync(string summary, int eventCount)
    {
        if (_config == null) return;

        var embed = new DiscordEmbed
        {
            Title = "⚔️ BattleLuck Arena Update",
            Description = summary,
            Color = 0xFF6600,
            Fields = new List<EmbedField>
            {
                new() { Name = "Events Processed", Value = eventCount.ToString(), Inline = true },
                new() { Name = "Period", Value = $"{_config.BufferFlushIntervalSec}s", Inline = true }
            },
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        var payload = new DiscordWebhookPayload { Embeds = new List<DiscordEmbed> { embed } };
        var json = JsonSerializer.Serialize(payload);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(_config.DiscordWebhookUrl, content);
        if (!response.IsSuccessStatusCode)
            BattleLuckPlugin.LogWarning($"[AiLogger] Discord webhook returned {response.StatusCode}");
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
