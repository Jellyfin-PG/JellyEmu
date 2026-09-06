using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public abstract class BaseTheGamesDbProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected const string BaseUrl = "https://api.thegamesdb.net/v1/";
        protected const string CdnFallback = "https://cdn.thegamesdb.net/images/original/";

        protected static string ApiKey => Plugin.Instance?.Configuration.TheGamesDbApiKey ?? string.Empty;

        protected BaseTheGamesDbProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", JellyEmuVersion.UserAgent);
            }
            return client;
        }

        protected static string CombineUrl(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(baseUrl)) return path;
            if (string.IsNullOrEmpty(path)) return baseUrl;
            return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        public static string? TryExtractEmbeddedTheGamesDbId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = Regex.Match(path, @"\[(?:tgdb|thegamesdb)-(\d+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static readonly Dictionary<string, int> PlatformToTheGamesDbId = new(StringComparer.OrdinalIgnoreCase)
        {
            { "NES", 7 },
            { "SNES", 6 },
            { "N64", 3 },
            { "Game Boy", 4 },
            { "Game Boy Color", 41 },
            { "Game Boy Advance", 5 },
            { "Nintendo DS", 8 },
            { "Virtual Boy", 4918 },
            { "Master System", 35 },
            { "Game Gear", 20 },
            { "Sega Genesis", 18 },
            { "Sega CD", 21 },
            { "Sega 32X", 22 },
            { "Sega Saturn", 17 },
            { "PlayStation", 10 },
            { "PSP", 13 },
            { "Atari 2600", 23 },
            { "Atari 7800", 25 },
            { "Atari Lynx", 27 },
            { "Atari Jaguar", 28 },
            { "WonderSwan", 4925 },
            { "WonderSwan Color", 4926 },
            { "TurboGrafx-16", 33 },
            { "ColecoVision", 31 },
            { "Neo Geo Pocket", 29 },
            { "Neo Geo Pocket Color", 30 },
            { "3DO", 26 },
            { "PC-FX", 4930 },
            { "Arcade", 24 }
        };

        public static int? ResolvePlatformId(string? platform)
        {
            if (string.IsNullOrWhiteSpace(platform)) return null;
            if (PlatformToTheGamesDbId.TryGetValue(platform.Trim(), out var id))
                return id;
            return null;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return Task.FromException<HttpResponseMessage>(new ArgumentException("URL must be a well-formed absolute URI.", nameof(url)));
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class TheGamesDbMetadataProvider : BaseTheGamesDbProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "TheGamesDB Metadata Provider";
        public int Order => 1;

        private readonly PlatformResolver _platformResolver;

        public TheGamesDbMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TheGamesDbMetadataProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(ApiKey)) return results;
            if (!string.IsNullOrEmpty(searchInfo.Path) && (!RomExtensions.IsRomPath(searchInfo.Path) || RomExtensions.IsWindowsRom(searchInfo.Path))) return results;

            searchInfo.ProviderIds.TryGetValue("TheGamesDB", out var directId);
            if (string.IsNullOrEmpty(directId))
                directId = TryExtractEmbeddedTheGamesDbId(searchInfo.Path);

            // 1. Direct ID lookup
            if (!string.IsNullOrEmpty(directId))
            {
                try
                {
                    var url = $"{BaseUrl}Games/ByGameID?apikey={ApiKey}&id={directId}&fields=players,publishers,genres,overview,last_updated,rating,platform&include=boxart,platform";
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        var sr = ParseSingleGameSearchResult(doc.RootElement, directId);
                        if (sr != null) return new[] { sr };
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
                {
                    Logger.LogWarning(ex, "[JellyEmu] TheGamesDB direct lookup failed for ID {Id}", directId);
                }
                return results;
            }

            // 2. Fuzzy name search
            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);
            var candidates = new[] { cleanName, normalizedName }.Distinct(StringComparer.OrdinalIgnoreCase);

            var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(searchInfo.Path));
            var platformId = ResolvePlatformId(consoleTag);

            foreach (var query in candidates.Where(q => !string.IsNullOrWhiteSpace(q)))
            {

                // First attempt: with platform filter if resolved
                if (platformId.HasValue)
                {
                    var filteredUrl = $"{BaseUrl}Games/ByGameName?apikey={ApiKey}&name={Uri.EscapeDataString(query)}&filter%5Bplatform%5D={platformId.Value}&include=boxart,platform";
                    var res = await QueryGamesByNameAsync(filteredUrl, cancellationToken).ConfigureAwait(false);
                    if (res.Count > 0)
                        return res;
                }

                // Fallback attempt: without platform filter
                var fallbackUrl = $"{BaseUrl}Games/ByGameName?apikey={ApiKey}&name={Uri.EscapeDataString(query)}&include=boxart,platform";
                var fallbackRes = await QueryGamesByNameAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);
                if (fallbackRes.Count > 0)
                    return fallbackRes;
            }

            return results;
        }

        private async Task<List<RemoteSearchResult>> QueryGamesByNameAsync(string url, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            try
            {
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array)
                    {
                        var boxartMap = ExtractBoxartMap(root);

                        foreach (var game in games.EnumerateArray().Take(5).Where(g => g.TryGetProperty("id", out _)))
                        {
                            var idEl = game.GetProperty("id");
                            var idStr = idEl.GetInt32().ToString();
                            var title = game.TryGetProperty("game_title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;

                            var sr = new RemoteSearchResult
                            {
                                Name = title,
                                ProviderIds = new Dictionary<string, string> { { "TheGamesDB", idStr } },
                                SearchProviderName = Name
                            };

                            if (game.TryGetProperty("release_date", out var relEl) &&
                                relEl.ValueKind == JsonValueKind.String &&
                                DateTime.TryParse(relEl.GetString(), out var relDate))
                            {
                                sr.ProductionYear = relDate.Year;
                            }

                            if (boxartMap.TryGetValue(idStr, out var imgUrl))
                            {
                                sr.ImageUrl = imgUrl;
                            }

                            results.Add(sr);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                Logger.LogDebug(ex, "[JellyEmu] TheGamesDB query failed for URL {Url}", url);
            }
            return results;
        }

        private RemoteSearchResult? ParseSingleGameSearchResult(JsonElement root, string directId)
        {
            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array && games.GetArrayLength() > 0)
            {
                var game = games[0];
                var title = game.TryGetProperty("game_title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var sr = new RemoteSearchResult
                {
                    Name = title,
                    ProviderIds = new Dictionary<string, string> { { "TheGamesDB", directId } },
                    SearchProviderName = Name
                };

                if (game.TryGetProperty("release_date", out var relEl) &&
                    relEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(relEl.GetString(), out var relDate))
                {
                    sr.ProductionYear = relDate.Year;
                }

                var boxartMap = ExtractBoxartMap(root);
                if (boxartMap.TryGetValue(directId, out var imgUrl))
                {
                    sr.ImageUrl = imgUrl;
                }

                return sr;
            }
            return null;
        }

        internal static Dictionary<string, string> ExtractBoxartMap(JsonElement root, bool preferOriginal = false)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetProperty("include", out var include)) return map;
            if (!include.TryGetProperty("boxart", out var boxart)) return map;

            var baseUrl = CdnFallback;
            if (boxart.TryGetProperty("base_url", out var bUrl))
            {
                if (preferOriginal)
                {
                    if (bUrl.TryGetProperty("original", out var origUrl) && origUrl.ValueKind == JsonValueKind.String)
                        baseUrl = origUrl.GetString() ?? baseUrl;
                    else if (bUrl.TryGetProperty("large", out var lrgUrl) && lrgUrl.ValueKind == JsonValueKind.String)
                        baseUrl = lrgUrl.GetString() ?? baseUrl;
                    else if (bUrl.TryGetProperty("thumb", out var thumbUrl) && thumbUrl.ValueKind == JsonValueKind.String)
                        baseUrl = thumbUrl.GetString() ?? baseUrl;
                }
                else
                {
                    if (bUrl.TryGetProperty("thumb", out var thumbUrl) && thumbUrl.ValueKind == JsonValueKind.String)
                        baseUrl = thumbUrl.GetString() ?? baseUrl;
                    else if (bUrl.TryGetProperty("original", out var origUrl) && origUrl.ValueKind == JsonValueKind.String)
                        baseUrl = origUrl.GetString() ?? baseUrl;
                }
            }

            if (boxart.TryGetProperty("data", out var bData) && bData.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in bData.EnumerateObject())
                {
                    var gameId = prop.Name;
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            var side = item.TryGetProperty("side", out var s) ? s.GetString() : null;
                            var fn = item.TryGetProperty("filename", out var f) ? f.GetString() : null;
                            if (string.Equals(side, "front", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(fn))
                            {
                                map[gameId] = CombineUrl(baseUrl, fn);
                                break;
                            }
                        }
                    }
                }
            }
            return map;
        }

        internal static (string? frontUrl, string? backUrl) ExtractBoxartUrls(JsonElement root, string gameId, bool preferOriginal = true)
        {
            if (!root.TryGetProperty("include", out var include)) return (null, null);
            if (!include.TryGetProperty("boxart", out var boxart)) return (null, null);

            var baseUrl = CdnFallback;
            if (boxart.TryGetProperty("base_url", out var bUrl))
            {
                if (preferOriginal)
                {
                    if (bUrl.TryGetProperty("original", out var origUrl) && origUrl.ValueKind == JsonValueKind.String)
                        baseUrl = origUrl.GetString() ?? baseUrl;
                    else if (bUrl.TryGetProperty("large", out var lrgUrl) && lrgUrl.ValueKind == JsonValueKind.String)
                        baseUrl = lrgUrl.GetString() ?? baseUrl;
                    else if (bUrl.TryGetProperty("thumb", out var thumbUrl) && thumbUrl.ValueKind == JsonValueKind.String)
                        baseUrl = thumbUrl.GetString() ?? baseUrl;
                }
                else
                {
                    if (bUrl.TryGetProperty("thumb", out var thumbUrl) && thumbUrl.ValueKind == JsonValueKind.String)
                        baseUrl = thumbUrl.GetString() ?? baseUrl;
                    else if (bUrl.TryGetProperty("original", out var origUrl) && origUrl.ValueKind == JsonValueKind.String)
                        baseUrl = origUrl.GetString() ?? baseUrl;
                }
            }

            string? front = null;
            string? back = null;

            if (boxart.TryGetProperty("data", out var bData) && bData.ValueKind == JsonValueKind.Object)
            {
                JsonElement gameItems = default;
                if (!bData.TryGetProperty(gameId.Trim(), out gameItems))
                {
                    var prop = bData.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, gameId.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (!prop.Equals(default(JsonProperty)))
                    {
                        gameItems = prop.Value;
                    }
                }

                if (gameItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in gameItems.EnumerateArray())
                    {
                        var side = item.TryGetProperty("side", out var s) ? s.GetString() : null;
                        var fn = item.TryGetProperty("filename", out var f) ? f.GetString() : null;
                        if (string.IsNullOrWhiteSpace(fn)) continue;

                        if (string.Equals(side, "front", StringComparison.OrdinalIgnoreCase) && front == null)
                        {
                            front = CombineUrl(baseUrl, fn);
                        }
                        else if (string.Equals(side, "back", StringComparison.OrdinalIgnoreCase) && back == null)
                        {
                            back = CombineUrl(baseUrl, fn);
                        }
                    }
                }
            }

            return (front, back);
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (string.IsNullOrEmpty(ApiKey)) return result;
            if (!string.IsNullOrEmpty(info.Path) && (!RomExtensions.IsRomPath(info.Path) || RomExtensions.IsWindowsRom(info.Path))) return result;

            info.ProviderIds.TryGetValue("TheGamesDB", out var gameId);
            if (string.IsNullOrEmpty(gameId))
                gameId = TryExtractEmbeddedTheGamesDbId(info.Path);
            if (string.IsNullOrEmpty(gameId))
            {
                var searchRes = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                gameId = searchRes.FirstOrDefault()?.ProviderIds["TheGamesDB"];
            }

            if (string.IsNullOrEmpty(gameId)) return result;

            try
            {
                var url = $"{BaseUrl}Games/ByGameID?apikey={ApiKey}&id={gameId}&fields=players,publishers,genres,overview,last_updated,rating,platform&include=boxart,platform";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array && games.GetArrayLength() > 0)
                    {
                        var game = games[0];

                        var isJ3u = string.Equals(Path.GetExtension(info.Path), ".j3u", StringComparison.OrdinalIgnoreCase);
                        var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));

                        var tags = new List<string> { "JellyEmu", "Game", consoleTag };
                        if (string.Equals(consoleTag, "Windows", StringComparison.OrdinalIgnoreCase))
                        {
                            tags.Add("Unsupported");
                        }
                        tags.AddRange(PlatformResolver.ResolveRegions(RomExtensions.EffectiveRomPath(info.Path)));
                        if (isJ3u)
                        {
                            tags.Add("MultiDisc");
                        }
                        else
                        {
                            var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));
                            if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);
                        }

                        var title = game.TryGetProperty("game_title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
                        var overview = game.TryGetProperty("overview", out var overEl) ? overEl.GetString() ?? string.Empty : string.Empty;

                        var item = new Book
                        {
                            Name = title,
                            Overview = overview,
                            Tags = tags.ToArray()
                        };

                        if (game.TryGetProperty("release_date", out var relEl) &&
                            relEl.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(relEl.GetString(), out var relDate))
                        {
                            item.PremiereDate = relDate;
                            item.ProductionYear = relDate.Year;
                        }

                        if (game.TryGetProperty("rating", out var ratingEl) && ratingEl.ValueKind == JsonValueKind.String)
                        {
                            var r = ratingEl.GetString();
                            if (!string.IsNullOrWhiteSpace(r)) item.OfficialRating = r;
                        }

                        item.SetProviderId("TheGamesDB", gameId);

                        result.Item = item;
                        result.HasMetadata = true;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                Logger.LogError(ex, "[JellyEmu] TheGamesDB metadata retrieval failed for game ID {GameId}", gameId);
            }

            return result;
        }

    }

    public class TheGamesDbImageProvider : BaseTheGamesDbProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "TheGamesDB Image Provider";
        public int Order => 1;

        private readonly PlatformResolver? _platformResolver;

        public TheGamesDbImageProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<TheGamesDbImageProvider> logger,
            PlatformResolver? platformResolver = null)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public bool Supports(BaseItem item) => item is Book && !RomExtensions.IsWindowsRom(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
            new[] { ImageType.Primary, ImageType.Backdrop, ImageType.BoxRear, ImageType.Banner, ImageType.Logo };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (string.IsNullOrEmpty(ApiKey)) return list;
            if (!string.IsNullOrEmpty(item.Path) && (!RomExtensions.IsRomPath(item.Path) || RomExtensions.IsWindowsRom(item.Path))) return list;

            var gameId = item.GetProviderId("TheGamesDB");
            if (string.IsNullOrEmpty(gameId))
                gameId = TryExtractEmbeddedTheGamesDbId(item.Path);

            if (string.IsNullOrEmpty(gameId))
            {
                gameId = await ResolveGameIdAsync(
                    item.Name ?? RomExtensions.CleanName(item.Path) ?? string.Empty,
                    item.Path,
                    cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(gameId)) return list;

            var primaryImages = new List<RemoteImageInfo>();
            var boxRearImages = new List<RemoteImageInfo>();
            var backdropImages = new List<RemoteImageInfo>();
            var bannerImages = new List<RemoteImageInfo>();
            var logoImages = new List<RemoteImageInfo>();

            // 1. Fast path: Fetch front and back box art via ByGameID?include=boxart (responds in ~0.5s with the exact cover art)
            try
            {
                var byGameUrl = $"{BaseUrl}Games/ByGameID?apikey={ApiKey}&id={Uri.EscapeDataString(gameId.Trim())}&include=boxart";
                var bgResponse = await GetHttpClient().GetAsync(byGameUrl, cancellationToken).ConfigureAwait(false);
                if (bgResponse.IsSuccessStatusCode)
                {
                    var json = await bgResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var bgDoc = JsonDocument.Parse(json);
                    var (frontUrl, backUrl) = TheGamesDbMetadataProvider.ExtractBoxartUrls(bgDoc.RootElement, gameId.Trim(), preferOriginal: true);
                    if (!string.IsNullOrWhiteSpace(frontUrl))
                    {
                        primaryImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = frontUrl });
                    }
                    if (!string.IsNullOrWhiteSpace(backUrl))
                    {
                        boxRearImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.BoxRear, Url = backUrl });
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                Logger.LogDebug(ex, "[JellyEmu] ByGameID boxart lookup failed for TheGamesDB game ID {GameId}", gameId);
            }

            // 2. Fetch backdrops, banners, clearlogos from Games/Images with a 3s timeout so slow CDN queries never block
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var url = $"{BaseUrl}Games/Images?apikey={ApiKey}&games_id={Uri.EscapeDataString(gameId.Trim())}";
                var response = await GetHttpClient().GetAsync(url, cts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("data", out var data))
                    {
                        var baseUrl = CdnFallback;
                        if (data.TryGetProperty("base_url", out var bUrl))
                        {
                            if (bUrl.ValueKind == JsonValueKind.String)
                            {
                                baseUrl = bUrl.GetString() ?? baseUrl;
                            }
                            else if (bUrl.ValueKind == JsonValueKind.Object)
                            {
                                if (bUrl.TryGetProperty("original", out var origUrl) && origUrl.ValueKind == JsonValueKind.String)
                                    baseUrl = origUrl.GetString() ?? baseUrl;
                                else if (bUrl.TryGetProperty("large", out var lrgUrl) && lrgUrl.ValueKind == JsonValueKind.String)
                                    baseUrl = lrgUrl.GetString() ?? baseUrl;
                            }
                        }

                        if (data.TryGetProperty("images", out var imagesElement))
                        {
                            JsonElement gameImages = default;
                            if (imagesElement.ValueKind == JsonValueKind.Object)
                            {
                                if (!imagesElement.TryGetProperty(gameId.Trim(), out gameImages))
                                {
                                    var prop = imagesElement.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, gameId.Trim(), StringComparison.OrdinalIgnoreCase));
                                    if (!prop.Equals(default(JsonProperty)))
                                    {
                                        gameImages = prop.Value;
                                    }
                                }
                            }
                            else if (imagesElement.ValueKind == JsonValueKind.Array)
                            {
                                gameImages = imagesElement;
                            }

                            if (gameImages.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var img in gameImages.EnumerateArray())
                                {
                                    var type = img.TryGetProperty("type", out var t) ? t.GetString() : null;
                                    var side = img.TryGetProperty("side", out var s) ? s.GetString() : null;
                                    var fn = img.TryGetProperty("filename", out var f) ? f.GetString() : null;

                                    if (string.IsNullOrWhiteSpace(fn)) continue;
                                    var fullUrl = CombineUrl(baseUrl, fn);

                                    if (string.Equals(type, "boxart", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.Equals(side, "back", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!boxRearImages.Any(i => i.Url == fullUrl))
                                                boxRearImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.BoxRear, Url = fullUrl });
                                        }
                                        else
                                        {
                                            if (!primaryImages.Any(i => i.Url == fullUrl))
                                                primaryImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = fullUrl });
                                        }
                                    }
                                    else if ((string.Equals(type, "fanart", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(type, "screenshot", StringComparison.OrdinalIgnoreCase)) &&
                                             backdropImages.Count < 5)
                                    {
                                        backdropImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = fullUrl });
                                    }
                                    else if (string.Equals(type, "banner", StringComparison.OrdinalIgnoreCase) && bannerImages.Count < 2)
                                    {
                                        bannerImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Banner, Url = fullUrl });
                                    }
                                    else if (string.Equals(type, "clearlogo", StringComparison.OrdinalIgnoreCase) && logoImages.Count < 2)
                                    {
                                        logoImages.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Logo, Url = fullUrl });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                Logger.LogDebug(ex, "[JellyEmu] Extra image fetching from TheGamesDB skipped for game ID {GameId}", gameId);
            }

            // Order: Primary first so Jellyfin picks the primary cover art
            list.AddRange(primaryImages);
            list.AddRange(backdropImages);
            list.AddRange(boxRearImages);
            list.AddRange(bannerImages);
            list.AddRange(logoImages);

            return list;
        }

        private async Task<string?> ResolveGameIdAsync(string name, string? path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var cleanName = RomExtensions.CleanName(name);
            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);
            var candidates = new[] { cleanName, normalizedName }.Distinct(StringComparer.OrdinalIgnoreCase);

            var consoleTag = !string.IsNullOrEmpty(path)
                ? (_platformResolver ?? new PlatformResolver(null!)).Resolve(RomExtensions.EffectiveRomPath(path))
                : null;
            var platformId = ResolvePlatformId(consoleTag);

            foreach (var query in candidates.Where(q => !string.IsNullOrWhiteSpace(q)))
            {
                try
                {
                    var url = $"{BaseUrl}Games/ByGameName?apikey={ApiKey}&name={Uri.EscapeDataString(query)}&fields=platform";
                    if (platformId.HasValue)
                    {
                        url += $"&filter%5Bplatform%5D={platformId.Value}";
                    }

                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("games", out var games) &&
                            games.ValueKind == JsonValueKind.Array &&
                            games.GetArrayLength() > 0)
                        {
                            var firstGame = games[0];
                            if (firstGame.TryGetProperty("id", out var idEl))
                            {
                                return idEl.ValueKind == JsonValueKind.Number
                                    ? idEl.GetInt32().ToString()
                                    : idEl.GetString();
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
                {
                    Logger.LogDebug(ex, "[JellyEmu] Failed resolving TheGamesDB game ID for {Name}", query);
                }
            }
            return null;
        }
    }

    public class TheGamesDbGameExternalId : IExternalId
    {
        public string ProviderName => "TheGamesDB";
        public string Key => "TheGamesDB";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://thegamesdb.net/game.php?id={0}";
        public bool Supports(IHasProviderIds item) => item is Book || item is BookInfo;
    }

    public class TheGamesDbExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "TheGamesDB";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (RomExtensions.IsWindowsRom(item.Path)) yield break;
            if (item.TryGetProviderId("TheGamesDB", out var id))
                yield return $"https://thegamesdb.net/game.php?id={id}";
        }
    }
}
