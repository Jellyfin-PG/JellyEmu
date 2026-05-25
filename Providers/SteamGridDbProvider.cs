using System.Net.Http.Headers;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public class SteamGridDbImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SteamGridDbImageProvider> _logger;

        public SteamGridDbImageProvider(IHttpClientFactory httpClientFactory, ILogger<SteamGridDbImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string Name => "SteamGridDB Image Provider";
        public int Order => 2;

        private static string? TryExtractEmbeddedSteamGridDbId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(path, @"\[sgdb-(\d+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public bool Supports(BaseItem item) => item is Book && !RomExtensions.IsWindowsRom(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Logo };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && (!RomExtensions.IsRomPath(item.Path) || RomExtensions.IsWindowsRom(item.Path))) return list;
            var apiKey = Plugin.Instance?.Configuration.SteamGridDbApiKey;
            if (string.IsNullOrEmpty(apiKey)) return list;

            var gameId = item.GetProviderId("SteamGridDb");
            if (string.IsNullOrEmpty(gameId))
                gameId = TryExtractEmbeddedSteamGridDbId(item.Path);

            if (string.IsNullOrEmpty(gameId))
            {
                gameId = await ResolveGameIdAsync(item.Name ?? RomExtensions.CleanName(item.Path) ?? string.Empty, apiKey, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(gameId)) return list;

            try
            {
                await Task.WhenAll(
                    FetchImagesAsync(list, "grids", gameId, ImageType.Primary, apiKey, cancellationToken),
                    FetchImagesAsync(list, "heroes", gameId, ImageType.Backdrop, apiKey, cancellationToken),
                    FetchImagesAsync(list, "logos", gameId, ImageType.Logo, apiKey, cancellationToken)
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Error fetching images from SteamGridDB for game {GameId}", gameId);
            }

            return list;
        }

        private async Task FetchImagesAsync(List<RemoteImageInfo> list, string type, string gameId, ImageType imageType, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await client.GetAsync($"https://www.steamgriddb.com/api/v2/{type}/game/{gameId}", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean() &&
                        doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var img in data.EnumerateArray())
                        {
                            var url = img.TryGetProperty("url", out var u) ? u.GetString() : null;
                            if (!string.IsNullOrEmpty(url))
                            {
                                lock (list)
                                {
                                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = imageType, Url = url });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Error fetching {Type} from SteamGridDB for game {GameId}", type, gameId);
            }
        }

        private async Task<string?> ResolveGameIdAsync(string name, string apiKey, CancellationToken cancellationToken)
        {
            try
            {
                var cleanName = RomExtensions.CleanName(name);
                if (string.IsNullOrEmpty(cleanName)) return null;

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await client.GetAsync($"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(cleanName)}", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean() &&
                        doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                    {
                        return data[0].GetProperty("id").GetInt32().ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }

    public class SteamGridDbExternalId : IExternalId
    {
        public string ProviderName => "SteamGridDB";
        public string Key => "SteamGridDb";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://www.steamgriddb.com/game/{0}";
        public bool Supports(IHasProviderIds item) => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path) && !RomExtensions.IsWindowsRom((item as BaseItem)?.Path);
    }

    public class SteamGridDbExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "SteamGridDB";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (RomExtensions.IsWindowsRom(item.Path)) yield break;
            if (item.TryGetProviderId("SteamGridDb", out var id))
                yield return $"https://www.steamgriddb.com/game/{id}";
        }
    }
}
