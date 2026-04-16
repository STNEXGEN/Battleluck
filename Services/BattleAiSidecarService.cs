using BattleLuck.Models;

namespace BattleLuck.Services
{
    public sealed class BattleAiSidecarService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly BattleAiSidecarSettings _settings;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string BaseUrl => _httpClient.BaseAddress?.ToString()?.TrimEnd('/') ?? "";
        public bool IsEnabled => _settings.Enabled && _httpClient.BaseAddress != null;
        public string? LastError { get; private set; }
        public DateTime? LastSuccessfulCallUtc { get; private set; }

        public BattleAiSidecarService(BattleAiSidecarSettings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                var baseUrl = settings.BaseUrl.Trim();
                if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
                {
                    baseUrl += "/";
                }

                _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }

            _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
        }

        public Task<BattleAiHealthResponse?> GetHealthAsync()
        {
            return SendAsync<BattleAiHealthResponse>(HttpMethod.Get, "health");
        }

        public Task<BattleAiQueryEnrichmentResult?> EnrichDirectQueryAsync(BattleAiQueryEnrichmentRequest request)
        {
            return SendAsync<BattleAiQueryEnrichmentResult>(HttpMethod.Post, "api/query/enrich", request);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? payload = null)
        {
            if (!IsEnabled)
            {
                return default;
            }

            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrWhiteSpace(_settings.AuthKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.AuthKey);
            }

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            try
            {
                using var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"{(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}";
                    BattleLuckLogger.Warning($"Battle AI sidecar request failed: {LastError}");
                    return default;
                }

                LastSuccessfulCallUtc = DateTime.UtcNow;
                LastError = null;

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    return default;
                }

                var trimmed = responseBody.TrimStart();
                if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
                {
                    LastError = "Sidecar returned non-JSON response (likely HTML). Verify ai_sidecar.base_url points to an API endpoint, not a web page.";
                    BattleLuckLogger.Warning($"Battle AI sidecar response parse skipped: {LastError}");
                    return default;
                }

                return JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                BattleLuckLogger.Warning($"Battle AI sidecar request error: {ex.Message}");
                return default;
            }
        }
    }
}