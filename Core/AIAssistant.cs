using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BattleLuck.Models;
using BattleLuck.Services;

namespace BattleLuck.Core
{
    public class AIAssistant
    {
        private GoogleAIService? _aiService;
        private readonly List<GoogleAIService> _aiFallbackServices = new();
        private BattleAiSidecarService? _sidecarService;
        private AIConfig? _config;
        private readonly ConcurrentDictionary<ulong, PlayerContext> _playerContexts = new();
        private readonly ConcurrentDictionary<ulong, DateTime> _lastMessageTimes = new();
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
        
        private TimeSpan _messageCooldown = TimeSpan.FromSeconds(30);
        private TimeSpan _contextRetention = TimeSpan.FromMinutes(30);
        private TimeSpan _tipCooldown = TimeSpan.FromMinutes(5);
        private float _queryTemperature = 0.8f;
        private int _queryMaxTokens = 300;
        private int _maxConversationHistory = 20;
        
        public bool IsEnabled { get; private set; }
        public bool IsSidecarConfigured => _sidecarService?.IsEnabled == true;
        public string SidecarBaseUrl => _sidecarService?.BaseUrl ?? "";
        public string? SidecarLastError => _sidecarService?.LastError;
        public DateTime? SidecarLastSuccessfulCallUtc => _sidecarService?.LastSuccessfulCallUtc;
        
        private readonly HttpClient _webhookHttp = new();

        public void Initialize(AIConfig config)
        {
            try
            {
                _config = config;
                _messageCooldown = TimeSpan.FromSeconds(Math.Max(5, config.Messaging.MessageCooldownSeconds));
                _contextRetention = TimeSpan.FromMinutes(Math.Max(5, config.Messaging.ContextRetentionMinutes));
                _tipCooldown = TimeSpan.FromMinutes(Math.Max(1, config.Messaging.TipCooldownMinutes));
                _queryTemperature = config.GoogleAIStudio.Temperature;
                _queryMaxTokens = Math.Max(100, config.GoogleAIStudio.MaxTokens);
                _maxConversationHistory = Math.Max(5, config.Privacy.MaxConversationHistorySize);

                _aiService = new GoogleAIService(
                    config.GoogleAIStudio.ApiKey,
                    config.GoogleAIStudio.Model,
                    Math.Max(1, config.GoogleAIStudio.MaxRequestsPerSecond)
                );

                _aiFallbackServices.Clear();
                foreach (var fallbackModel in config.GoogleAIStudio.FallbackModels)
                {
                    if (string.IsNullOrWhiteSpace(fallbackModel))
                        continue;

                    if (string.Equals(fallbackModel, config.GoogleAIStudio.Model, StringComparison.OrdinalIgnoreCase))
                        continue;

                    _aiFallbackServices.Add(new GoogleAIService(
                        config.GoogleAIStudio.ApiKey,
                        fallbackModel,
                        Math.Max(1, config.GoogleAIStudio.MaxRequestsPerSecond)
                    ));
                }

                _sidecarService?.Dispose();
                _sidecarService = null;
                if (config.Sidecar.Enabled && !string.IsNullOrWhiteSpace(config.Sidecar.BaseUrl))
                {
                    _sidecarService = new BattleAiSidecarService(config.Sidecar);
                    BattleLuckLogger.Info($"Battle AI sidecar configured at {_sidecarService.BaseUrl}");
                }

                SubscribeToEvents();
                IsEnabled = true;
                BattleLuckLogger.Info($"AI Assistant initialized successfully with Google AI Studio (providers: {1 + _aiFallbackServices.Count})");
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Critical($"Failed to initialize AI Assistant: {ex.Message}");
                IsEnabled = false;
            }
        }

        public void Initialize(string apiKey, string model = "gemini-pro")
        {
            var config = new AIConfig();
            config.GoogleAIStudio.ApiKey = apiKey;
            config.GoogleAIStudio.Model = model;
            Initialize(config);
        }

        public void Shutdown()
        {
            IsEnabled = false;
            UnsubscribeFromEvents();
            _aiService?.Dispose();
            foreach (var service in _aiFallbackServices)
            {
                service.Dispose();
            }
            _aiFallbackServices.Clear();
            _sidecarService?.Dispose();
            _sidecarService = null;
            _playerContexts.Clear();
            _lastMessageTimes.Clear();
            BattleLuckLogger.Info("AI Assistant shutdown");
        }

        public async Task<string?> HandleDirectQuery(ulong steamId, string query, string source = "game", bool broadcastToInGameChat = false)
        {
            if (!IsEnabled || _aiService == null)
                return "AI Assistant is currently unavailable.";

            try
            {
                var context = GetOrCreatePlayerContext(steamId);
                var enrichment = await GetDirectQueryEnrichmentAsync(context, query);
                var messages = BuildQueryMessages(context, query, enrichment);
                
                var response = await GetChatCompletionWithFailoverAsync(messages, _queryTemperature, _queryMaxTokens);
                
                context.AddMessage(ChatMessage.User(query));
                if (!string.IsNullOrEmpty(response))
                {
                    context.AddMessage(ChatMessage.Assistant(response));
                    _lastMessageTimes[steamId] = DateTime.UtcNow;

                    PublishAssistantOutput(steamId, query, response, source, broadcastToInGameChat);

                    // Forward to Discord and Superuser sidecar (fire-and-forget)
                    _ = Task.Run(() => ForwardToDiscordAsync(steamId, query, response));
                    _ = Task.Run(() => ForwardToSidecarAsync(steamId, query, response));
                }
                
                return response ?? "I'm having trouble processing your request right now.";
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"AI query error for player {steamId}: {ex.Message}");
                return "Sorry, I encountered an error processing your request.";
            }
        }

        private async Task ForwardToDiscordAsync(ulong steamId, string query, string response)
        {
            try
            {
                var webhookUrl = _config?.Messaging?.DiscordWebhookUrl;
                if (string.IsNullOrWhiteSpace(webhookUrl)) return;

                var payload = new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = "🤖 BattleLuck AI Chat",
                            color = 0x5865F2,
                            fields = new[]
                            {
                                new { name = "Player", value = steamId.ToString(), inline = true },
                                new { name = "Question", value = query.Length > 1024 ? query.Substring(0, 1021) + "..." : query, inline = false },
                                new { name = "Answer", value = response.Length > 1024 ? response.Substring(0, 1021) + "..." : response, inline = false }
                            },
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var result = await _webhookHttp.PostAsync(webhookUrl, content);
                if (!result.IsSuccessStatusCode)
                    BattleLuckLogger.Warning($"[AI→Discord] Webhook returned {result.StatusCode}");
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"[AI→Discord] Forward failed: {ex.Message}");
            }
        }

        private async Task ForwardToSidecarAsync(ulong steamId, string query, string response)
        {
            try
            {
                if (_sidecarService == null || !_sidecarService.IsEnabled || _config?.Sidecar == null) return;

                var url = $"{_config.Sidecar.BaseUrl.TrimEnd('/')}/api/chat/log";
                var payload = new
                {
                    steamId = steamId.ToString(),
                    query,
                    response,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(_config.Sidecar.AuthKey))
                    request.Headers.Add("Authorization", $"Bearer {_config.Sidecar.AuthKey}");

                var result = await _webhookHttp.SendAsync(request);
                if (!result.IsSuccessStatusCode)
                    BattleLuckLogger.Warning($"[AI→Sidecar] Log returned {result.StatusCode}");
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"[AI→Sidecar] Forward failed: {ex.Message}");
            }
        }

        private void PublishAssistantOutput(ulong steamId, string query, string response, string source, bool broadcastToInGameChat)
        {
            try
            {
                var safeSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim().ToLowerInvariant();
                var compactQuery = TrimForOutput(query, 140);
                var compactResponse = TrimForOutput(response, 240);

                BattleLuckPlugin.LogInfo($"[AI][{safeSource}] {steamId} Q: {compactQuery} | A: {compactResponse}");

                var discordMessage = $"🤖 [{safeSource}] {steamId}\nQ: {compactQuery}\nA: {compactResponse}";
                BattleLuckPlugin.PostToDiscordLogs(discordMessage);
                BattleLuckPlugin.PostToDiscordChatVip(discordMessage);

                if (broadcastToInGameChat)
                {
                    BattleLuckPlugin.TryNotifyPlayerBySteamId(steamId, $"🤖 AI Assistant: {TrimForOutput(response, 400)}");
                }
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"Failed to publish AI output: {ex.Message}");
            }
        }

        private static string TrimForOutput(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "(empty)";

            var normalized = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (normalized.Length <= maxLength)
                return normalized;

            return normalized[..Math.Max(1, maxLength - 3)] + "...";
        }

        public async Task<BattleAiHealthResponse?> GetSidecarHealthAsync()
        {
            if (_sidecarService == null || !_sidecarService.IsEnabled)
            {
                return null;
            }

            return await _sidecarService.GetHealthAsync();
        }

        private void SubscribeToEvents()
        {
            GameEvents.OnPlayerScored += OnPlayerScored;
            GameEvents.OnPlayerEliminated += OnPlayerEliminated;
            GameEvents.OnModeStarted += OnModeStarted;
            GameEvents.OnModeEnded += OnModeEnded;
            GameEvents.OnRoundEnded += OnRoundEnded;
            GameEvents.OnWaveStarted += OnWaveStarted;
            GameEvents.OnWaveCleared += OnWaveCleared;
            GameEvents.OnZoneEnter += OnZoneEnter;
        }

        private void UnsubscribeFromEvents()
        {
            GameEvents.OnPlayerScored -= OnPlayerScored;
            GameEvents.OnPlayerEliminated -= OnPlayerEliminated;
            GameEvents.OnModeStarted -= OnModeStarted;
            GameEvents.OnModeEnded -= OnModeEnded;
            GameEvents.OnRoundEnded -= OnRoundEnded;
            GameEvents.OnWaveStarted -= OnWaveStarted;
            GameEvents.OnWaveCleared -= OnWaveCleared;
            GameEvents.OnZoneEnter -= OnZoneEnter;
        }

        private void OnPlayerScored(PlayerScoredEvent e)
        {
            if (!ShouldSendMessage(e.SteamId)) return;

            _mainThreadQueue.Enqueue(async () =>
            {
                var context = GetOrCreatePlayerContext(e.SteamId);
                context.RecordEvent($"Scored {e.Points} points for {e.Reason}");

                // Provide encouragement for good performance
                if (e.Points >= 100 && context.ShouldReceiveTip("scoring"))
                {
                    var message = await GenerateContextualMessage(context, "scoring_encouragement", e);
                    if (!string.IsNullOrEmpty(message))
                    {
                        BroadcastToPlayer(e.SteamId, $"🤖 AI Assistant: {message}");
                    }
                }
            });
        }

        private void OnPlayerEliminated(PlayerEliminatedEvent e)
        {
            if (!ShouldSendMessage(e.SteamId)) return;

            _mainThreadQueue.Enqueue(async () =>
            {
                var context = GetOrCreatePlayerContext(e.SteamId);
                context.RecordEvent($"Eliminated by {e.EliminatedBy}");

                // Provide strategy tip after elimination
                if (context.ShouldReceiveTip("elimination"))
                {
                    var message = await GenerateContextualMessage(context, "elimination_advice", e);
                    if (!string.IsNullOrEmpty(message))
                    {
                        BroadcastToPlayer(e.SteamId, $"🤖 AI Assistant: {message}");
                    }
                }
            });
        }

        private void OnModeStarted(ModeStartedEvent e)
        {
            // Provide mode-specific tips to players
            _mainThreadQueue.Enqueue(async () =>
            {
                var session = BattleLuckPlugin.Session?.ActiveSessions?.Values
                    .FirstOrDefault(s => s.Context?.SessionId == e.SessionId);
                
                if (session?.Context?.Players != null)
                {
                    foreach (var playerId in session.Context.Players)
                    {
                        if (!ShouldSendMessage(playerId)) continue;

                        var context = GetOrCreatePlayerContext(playerId);
                        context.RecordEvent($"Started {e.ModeId} mode");

                        if (context.ShouldReceiveTip("mode_start"))
                        {
                            var message = await GenerateContextualMessage(context, "mode_start_tips", e);
                            if (!string.IsNullOrEmpty(message))
                            {
                                BroadcastToPlayer(playerId, $"🤖 AI Assistant: {message}");
                            }
                        }
                    }
                }
            });
        }

        private void OnModeEnded(ModeEndedEvent e)
        {
            // Provide performance summary and improvement suggestions
            _mainThreadQueue.Enqueue(async () =>
            {
                var session = BattleLuckPlugin.Session?.ActiveSessions?.Values
                    .FirstOrDefault(s => s.Context?.SessionId == e.SessionId);
                
                if (session?.Context?.Players != null)
                {
                    foreach (var playerId in session.Context.Players)
                    {
                        if (!ShouldSendMessage(playerId)) continue;

                        var context = GetOrCreatePlayerContext(playerId);
                        context.RecordEvent($"Completed {e.ModeId} mode");

                        var message = await GenerateContextualMessage(context, "match_summary", e);
                        if (!string.IsNullOrEmpty(message))
                        {
                            BroadcastToPlayer(playerId, $"🤖 AI Assistant: {message}");
                        }
                    }
                }
            });
        }

        private void OnRoundEnded(RoundEndedEvent e) { /* Similar pattern */ }
        private void OnWaveStarted(WaveStartedEvent e) { /* Similar pattern */ }
        private void OnWaveCleared(WaveClearedEvent e) { /* Similar pattern */ }
        private void OnZoneEnter(ZoneEnterEvent e) { /* Welcome message for first-time players */ }

        private async Task<string?> GenerateContextualMessage(PlayerContext context, string messageType, object eventData)
        {
            if (_aiService == null) return null;

            try
            {
                var messages = BuildContextualMessages(context, messageType, eventData);
                var response = await GetChatCompletionWithFailoverAsync(
                    messages,
                    Math.Min(1.0f, _queryTemperature + 0.1f),
                    Math.Min(250, _queryMaxTokens)
                );
                
                if (!string.IsNullOrEmpty(response))
                {
                    context.AddMessage(ChatMessage.Assistant(response));
                }
                
                return response;
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"Failed to generate AI message for {context.SteamId}: {ex.Message}");
                return null;
            }
        }

        private List<ChatMessage> BuildContextualMessages(PlayerContext context, string messageType, object eventData)
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System(GetSystemPrompt()),
                ChatMessage.System($"Player context: {context.GetContextSummary()}"),
                ChatMessage.System($"Message type: {messageType}"),
                ChatMessage.User(GetMessagePrompt(messageType, eventData))
            };

            // Add recent conversation history for context
            messages.AddRange(context.GetRecentMessages(3));
            
            return messages;
        }

        private async Task<BattleAiQueryEnrichmentResult?> GetDirectQueryEnrichmentAsync(PlayerContext context, string query)
        {
            if (_sidecarService == null || !_sidecarService.IsEnabled)
            {
                return null;
            }

            var request = new BattleAiQueryEnrichmentRequest
            {
                Query = query,
                Player = new BattleAiPlayerContextDto
                {
                    SteamId = context.SteamId.ToString(),
                    RecentEvents = context.RecentEvents.TakeLast(5).ToList(),
                    ConversationSummary = context.GetContextSummary(),
                    LastActivityUtc = context.LastActivity.ToString("O")
                },
                Session = CreateSessionContext(context.SteamId)
            };

            return await _sidecarService.EnrichDirectQueryAsync(request);
        }

        private BattleAiSessionContextDto? CreateSessionContext(ulong steamId)
        {
            var session = BattleLuckPlugin.Session?.ActiveSessions?.Values
                .FirstOrDefault(activeSession => activeSession.Context?.Players?.Contains(steamId) == true);

            if (session?.Context == null)
            {
                return null;
            }

            var sortedPlayers = session.Context.Players
                .Select(playerId => new BattleAiSessionPlayerDto
                {
                    SteamId = playerId.ToString(),
                    Score = session.Context.Scores.GetPlayerScore(playerId),
                    TeamId = session.Context.Teams.TryGetValue(playerId, out var teamId) ? teamId : null,
                    IsRequester = playerId == steamId,
                })
                .OrderByDescending(player => player.Score)
                .ThenBy(player => player.SteamId, StringComparer.Ordinal)
                .ToList();

            return new BattleAiSessionContextDto
            {
                SessionId = session.Context.SessionId,
                ModeId = session.Context.ModeId,
                ZoneHash = session.Context.ZoneHash,
                ElapsedSeconds = Math.Round(session.Context.ElapsedSeconds, 2),
                TimeLimitSeconds = session.Context.TimeLimitSeconds,
                IsTimeUp = session.Context.IsTimeUp,
                Players = sortedPlayers,
                Leaderboard = sortedPlayers.Take(5)
                    .Select(player => new BattleAiSessionPlayerDto
                    {
                        SteamId = player.SteamId,
                        Score = player.Score,
                        TeamId = player.TeamId,
                        IsRequester = player.IsRequester,
                    })
                    .ToList(),
                TeamScores = session.Context.Scores.GetAllTeamScores()
                    .ToDictionary(entry => entry.Key.ToString(), entry => entry.Value),
            };
        }

        private List<ChatMessage> BuildQueryMessages(PlayerContext context, string query, BattleAiQueryEnrichmentResult? enrichment)
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.System(GetSystemPrompt()),
                ChatMessage.System($"Player context: {context.GetContextSummary()}")
            };

            if (enrichment != null)
            {
                messages.Add(ChatMessage.System($"Battle intelligence summary: {enrichment.Summary}"));

                if (enrichment.TacticalFocus.Count > 0)
                {
                    messages.Add(ChatMessage.System($"Tactical focus: {string.Join(" | ", enrichment.TacticalFocus)}"));
                }

                if (enrichment.AnswerHints.Count > 0)
                {
                    messages.Add(ChatMessage.System($"Session facts: {string.Join(" | ", enrichment.AnswerHints)}"));
                }
            }

            messages.AddRange(context.GetRecentMessages(5));
            messages.Add(ChatMessage.User(query));
            
            return messages;
        }

        private async Task<string?> GetChatCompletionWithFailoverAsync(List<ChatMessage> messages, float temperature, int maxTokens)
        {
            if (_aiService == null)
                return null;

            var allProviders = new List<GoogleAIService>(1 + _aiFallbackServices.Count) { _aiService };
            allProviders.AddRange(_aiFallbackServices);

            string? lastError = null;
            for (int index = 0; index < allProviders.Count; index++)
            {
                try
                {
                    var provider = allProviders[index];
                    var response = await provider.GetChatCompletionAsync(messages, temperature: temperature, maxTokens: maxTokens);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        if (index > 0)
                            BattleLuckLogger.Warning($"AI failover succeeded with fallback provider #{index + 1}.");
                        return response;
                    }

                    lastError = $"Provider #{index + 1} returned empty response.";
                    BattleLuckLogger.Warning(lastError);
                }
                catch (Exception ex)
                {
                    lastError = $"Provider #{index + 1} failed: {ex.Message}";
                    BattleLuckLogger.Warning(lastError);
                }
            }

            if (!string.IsNullOrWhiteSpace(lastError))
                BattleLuckLogger.Warning($"All AI providers failed. Last error: {lastError}");

            return null;
        }

        private string GetSystemPrompt()
        {
            return @"You are an AI assistant for BattleLuck, a competitive V Rising mod with various PvP game modes. You are powered by Google AI Studio.

Game Modes:
- Bloodbath: Free-for-all deathmatch
- Colosseum: 1v1 arena with ELO rating  
- Gauntlet: Wave-based PvE survival
- Siege: Team-based objective mode
- Trials: Timed challenge mode

Your role:
- Provide helpful gameplay tips and strategies
- Explain game mechanics and commands
- Offer encouragement and constructive advice
- Keep responses brief (1-2 sentences max)
- Be supportive but not overly chatty
- Focus on actionable advice
- Treat any supplied BattleLuck battle-intelligence summary as authoritative live session context

Always be helpful, concise, and focused on improving the player's experience.";
        }

        private string GetMessagePrompt(string messageType, object eventData)
        {
            return messageType switch
            {
                "scoring_encouragement" => "Player just scored points. Give brief encouragement and a quick tip to maintain momentum.",
                "elimination_advice" => "Player was eliminated. Provide a constructive tip to improve their strategy without being negative.",
                "mode_start_tips" => "Game mode just started. Give a brief strategy tip specific to this mode.",
                "match_summary" => "Game mode ended. Provide brief performance feedback and an improvement suggestion.",
                _ => "Provide helpful guidance based on the current situation."
            };
        }

        private PlayerContext GetOrCreatePlayerContext(ulong steamId)
        {
            return _playerContexts.GetOrAdd(steamId, _ => new PlayerContext(steamId, _tipCooldown, _maxConversationHistory));
        }

        private bool ShouldSendMessage(ulong steamId)
        {
            if (!IsEnabled) return false;
            
            if (_lastMessageTimes.TryGetValue(steamId, out var lastTime))
            {
                return DateTime.UtcNow - lastTime > _messageCooldown;
            }
            
            return true;
        }

        private void BroadcastToPlayer(ulong steamId, string message)
        {
            // Find the player's active session and broadcast to them
            var session = BattleLuckPlugin.Session?.ActiveSessions?.Values
                .FirstOrDefault(s => s.Context?.Players?.Contains(steamId) == true);
                
            session?.Context?.Broadcast?.Invoke(message);
        }

        public void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    BattleLuckLogger.Warning($"AI Assistant main thread action error: {ex.Message}");
                }
            }
        }

        public void CleanupOldContexts()
        {
            var cutoff = DateTime.UtcNow - _contextRetention;
            var playersToRemove = _playerContexts
                .Where(kvp => kvp.Value.LastActivity < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var steamId in playersToRemove)
            {
                _playerContexts.TryRemove(steamId, out _);
                _lastMessageTimes.TryRemove(steamId, out _);
            }
        }
    }

    public class PlayerContext
    {
        public ulong SteamId { get; }
        public DateTime LastActivity { get; private set; }
        public List<string> RecentEvents { get; } = new();
        public Dictionary<string, int> TipCounts { get; } = new();
        public List<ChatMessage> ConversationHistory { get; } = new();
        
        private readonly Dictionary<string, DateTime> _lastTipTimes = new();
        private readonly TimeSpan _tipCooldown;
        private readonly int _maxConversationHistory;

        public PlayerContext(ulong steamId, TimeSpan tipCooldown, int maxConversationHistory)
        {
            SteamId = steamId;
            _tipCooldown = tipCooldown;
            _maxConversationHistory = maxConversationHistory;
            LastActivity = DateTime.UtcNow;
        }

        public void RecordEvent(string eventDescription)
        {
            LastActivity = DateTime.UtcNow;
            RecentEvents.Add($"{DateTime.UtcNow:HH:mm:ss}: {eventDescription}");
            
            if (RecentEvents.Count > 10)
            {
                RecentEvents.RemoveAt(0);
            }
        }

        public bool ShouldReceiveTip(string tipType)
        {
            if (_lastTipTimes.TryGetValue(tipType, out var lastTime))
            {
                if (DateTime.UtcNow - lastTime <= _tipCooldown)
                {
                    return false;
                }
            }

            _lastTipTimes[tipType] = DateTime.UtcNow;
            return true;
        }

        public void AddMessage(ChatMessage message)
        {
            ConversationHistory.Add(message);
            LastActivity = DateTime.UtcNow;
            
            if (ConversationHistory.Count > _maxConversationHistory)
            {
                ConversationHistory.RemoveAt(0);
            }
        }

        public List<ChatMessage> GetRecentMessages(int count)
        {
            return ConversationHistory.TakeLast(count).ToList();
        }

        public string GetContextSummary()
        {
            var recentEventsStr = string.Join("; ", RecentEvents.TakeLast(5));
            return $"Recent activity: {recentEventsStr}";
        }
    }
}