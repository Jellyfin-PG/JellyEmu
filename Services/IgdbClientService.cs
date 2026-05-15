using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    public class IgdbClientService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IgdbClientService> _logger;
        private string _accessToken = string.Empty;
        private DateTime _tokenExpiration = DateTime.MinValue;

        public IgdbClientService(IHttpClientFactory httpClientFactory, ILogger<IgdbClientService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private string ClientId => Plugin.Instance?.Configuration.IgdbClientId ?? string.Empty;
        private string ClientSecret => Plugin.Instance?.Configuration.IgdbClientSecret ?? string.Empty;

        public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
                return string.Empty;

            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration)
                return _accessToken;

            try
            {
                var url = $"https://id.twitch.tv/oauth2/token?client_id={ClientId}&client_secret={ClientSecret}&grant_type=client_credentials";
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsync(url, null, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    _accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
                    _tokenExpiration = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 60);
                    return _accessToken;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to get IGDB access token");
            }

            return string.Empty;
        }

        public async Task<HttpClient> GetIgdbClientAsync(CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            
            if (string.IsNullOrEmpty(token)) return client;

            client.DefaultRequestHeaders.Add("Client-ID", ClientId);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }
    }
}
