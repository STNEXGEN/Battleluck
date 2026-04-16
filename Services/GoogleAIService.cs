using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BattleLuck.Services
{
    public class GoogleAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly SemaphoreSlim _rateLimiter;
        private readonly int _maxRetries;
        private readonly int _retryDelayMs;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

        public GoogleAIService(string apiKey, string model = "gemini-pro", int maxRequestsPerSecond = 10)
        {
            _apiKey = apiKey;
            _model = model;
            _maxRetries = 3;
            _retryDelayMs = 1000;
            _rateLimiter = new SemaphoreSlim(maxRequestsPerSecond, maxRequestsPerSecond);

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string?> GetChatCompletionAsync(List<ChatMessage> messages, float temperature = 0.7f, int maxTokens = 500)
        {
            await _rateLimiter.WaitAsync();

            try
            {
                // Convert chat messages to Google AI Studio format
                var contents = new List<object>();
                
                foreach (var msg in messages)
                {
                    var role = msg.Role == "assistant" ? "model" : "user";
                    contents.Add(new { role = role, parts = new[] { new { text = msg.Content } } });
                }

                var requestBody = new
                {
                    contents = contents.ToArray(),
                    generationConfig = new
                    {
                        temperature = temperature,
                        maxOutputTokens = maxTokens,
                        topP = 1.0f
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                for (int attempt = 0; attempt < _maxRetries; attempt++)
                {
                    try
                    {
                        var url = $"{BaseUrl}/{_model}:generateContent?key={_apiKey}";
                        var response = await _httpClient.PostAsync(url, content);

                        if (response.IsSuccessStatusCode)
                        {
                            var responseJson = await response.Content.ReadAsStringAsync();
                            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);

                            if (responseObj.TryGetProperty("candidates", out var candidates) && 
                                candidates.GetArrayLength() > 0 &&
                                candidates[0].TryGetProperty("content", out var contentObj) &&
                                contentObj.TryGetProperty("parts", out var parts) &&
                                parts.GetArrayLength() > 0 &&
                                parts[0].TryGetProperty("text", out var textContent))
                            {
                                return textContent.GetString();
                            }
                        }
                        else
                        {
                            BattleLuckLogger.Warning($"Google AI Studio API error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                            
                            // Don't retry on client errors (400-499)
                            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                            {
                                break;
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        BattleLuckLogger.Warning($"Google AI Studio request failed (attempt {attempt + 1}): {ex.Message}");
                    }
                    catch (TaskCanceledException ex)
                    {
                        BattleLuckLogger.Warning($"Google AI Studio request timeout (attempt {attempt + 1}): {ex.Message}");
                    }

                    if (attempt < _maxRetries - 1)
                    {
                        await Task.Delay(_retryDelayMs * (attempt + 1)); // Exponential backoff
                    }
                }

                return null;
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _rateLimiter?.Dispose();
        }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        public static ChatMessage System(string content) => new("system", content);
        public static ChatMessage User(string content) => new("user", content);
        public static ChatMessage Assistant(string content) => new("assistant", content);
    }
}