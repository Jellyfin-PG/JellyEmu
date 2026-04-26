using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu
{

    internal static class RomExtensions
    {
        public static bool IsRomPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Regular file path
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext)) return PlatformResolver.AllRomExtensions.Contains(ext);
            // Directory path — valid only if it contains exactly one .cue that references
            // at least one existing .bin (i.e. it's a CUE+BIN game folder, not a platform folder).
            if (Directory.Exists(path))
            {
                try
                {
                    var cues = Directory.GetFiles(path, "*.cue");
                    return cues.Length == 1 && CueParser.HasResolvedBin(cues[0]);
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Returns the effective ROM file path for use by metadata providers.
        /// When <paramref name="path"/> is a directory (folder-as-game), returns the
        /// single .cue file inside it. Otherwise returns <paramref name="path"/> as-is.
        /// </summary>
        public static string EffectiveRomPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (Directory.Exists(path))
            {
                try
                {
                    var cues = Directory.GetFiles(path, "*.cue");
                    if (cues.Length == 1 && CueParser.HasResolvedBin(cues[0]))
                        return cues[0];
                }
                catch { }
            }
            return path;
        }

        public static string CleanName(string name)
        {
            var stripped = PlatformResolver.CleanDisplayName(name ?? string.Empty);
            var cleaned = Regex.Replace(stripped.Replace("_", " ").Replace("-", " "), @"\s+", " ").Trim();
            return cleaned;
        }
    }

    public class RomLocalProvider : ILocalMetadataProvider<Book>, IRemoteImageProvider
    {
        private readonly ILogger<RomLocalProvider> _logger;
        private readonly PlatformResolver _platformResolver;

        public RomLocalProvider(ILogger<RomLocalProvider> logger, PlatformResolver platformResolver)
        {
            _logger = logger;
            _platformResolver = platformResolver;
        }

        public string Name => "Retro Games Local Assets";

        public Task<MetadataResult<Book>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();

            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path))
                return Task.FromResult(result);

            if (!string.IsNullOrEmpty(info.Path))
            {
                var nfoPath = Path.ChangeExtension(info.Path, ".nfo");
                if (File.Exists(nfoPath))
                {
                    var nfoTags = new List<string> { "Game", _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path)) };
                    var nfoRegion = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                    var nfoDisc = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));
                    if (!string.IsNullOrEmpty(nfoRegion)) nfoTags.Add(nfoRegion);
                    if (!string.IsNullOrEmpty(nfoDisc)) nfoTags.Add(nfoDisc);

                    result.HasMetadata = true;
                    result.Item = new Book
                    {
                        Overview = "Parsed successfully from local .nfo file!",
                        PremiereDate = new DateTime(1990, 1, 1),
                        Tags = nfoTags.ToArray()
                    };
                }
            }

            return Task.FromResult(result);
        }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && RomExtensions.IsRomPath(item.Path))
            {
                var effectivePath = RomExtensions.EffectiveRomPath(item.Path);
                var dir = Path.GetDirectoryName(effectivePath);
                var baseName = Path.GetFileNameWithoutExtension(effectivePath);
                var possible = Path.Combine(dir ?? string.Empty, baseName + ".jpg");
                if (File.Exists(possible))
                {
                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = new Uri(possible).AbsoluteUri });
                }
                var possiblePng = Path.ChangeExtension(possible, ".png");
                if (File.Exists(possiblePng))
                {
                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = new Uri(possiblePng).AbsoluteUri });
                }
            }
            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(url) && url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = new Uri(url).LocalPath;
                if (File.Exists(localPath))
                {
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                    var stream = File.OpenRead(localPath);
                    response.Content = new StreamContent(stream);
                    var ext = Path.GetExtension(localPath).ToLowerInvariant();
                    var mime = ext == ".png" ? "image/png" : "image/jpeg";
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                    return Task.FromResult(response);
                }
            }
            return Task.FromResult<HttpResponseMessage>(null!);
        }
    }


    public abstract class BaseIgdbProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected static string ClientId => Plugin.Instance?.Configuration.IgdbClientId ?? string.Empty;
        protected static string ClientSecret => Plugin.Instance?.Configuration.IgdbClientSecret ?? string.Empty;

        private static string _accessToken = string.Empty;
        private static DateTime _tokenExpiration = DateTime.MinValue;

        protected BaseIgdbProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
                return string.Empty;

            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration) return _accessToken;
            var url = $"https://id.twitch.tv/oauth2/token?client_id={ClientId}&client_secret={ClientSecret}&grant_type=client_credentials";
            var client = HttpClientFactory.CreateClient();
            var response = await client.PostAsync(url, null, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                _accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
                _tokenExpiration = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 60);
                return _accessToken;
            }
            return string.Empty;
        }

        protected async Task<HttpClient> GetIgdbClientAsync(CancellationToken cancellationToken)
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Client-ID", ClientId);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {await GetTokenAsync(cancellationToken)}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }

        protected async Task<string?> ResolveGameIdAsync(string name, CancellationToken cancellationToken)
        {
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;
            try
            {
                var client = await GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                var content = new StringContent($"search \"{cleanName}\"; fields id; limit 1;", Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.GetArrayLength() > 0) return doc.RootElement[0].GetProperty("id").GetInt32().ToString();
                }
            }
            catch { }
            return null;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            }
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }

    public abstract class BaseRawgProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected static string ApiKey => Plugin.Instance?.Configuration.RawgApiKey ?? string.Empty;

        protected BaseRawgProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
            return client;
        }

        protected async Task<string?> ResolveGameIdAsync(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;
            try
            {
                var url = $"https://api.rawg.io/api/games?search={Uri.EscapeDataString(cleanName)}&key={ApiKey}&page_size=1";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var arr) && arr.GetArrayLength() > 0)
                        return arr[0].GetProperty("id").GetInt32().ToString();
                }
            }
            catch { }
            return null;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            }
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class IgdbMetadataProvider : BaseIgdbProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "IGDB Video Game Database";
        public int Order => 1;

        private readonly PlatformResolver _platformResolver;

        public IgdbMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<IgdbMetadataProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            try
            {
                var client = await GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                var content = new StringContent($"search \"{cleanName}\"; fields name,first_release_date; limit 5;", Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    foreach (var game in document.RootElement.EnumerateArray())
                    {
                        var searchResult = new RemoteSearchResult
                        {
                            Name = game.GetProperty("name").GetString() ?? string.Empty,
                            ProviderIds = new Dictionary<string, string> { { "IGDB", game.GetProperty("id").GetInt32().ToString() } },
                            SearchProviderName = Name
                        };
                        if (game.TryGetProperty("first_release_date", out var releaseUnix))
                            searchResult.ProductionYear = DateTimeOffset.FromUnixTimeSeconds(releaseUnix.GetInt64()).UtcDateTime.Year;
                        results.Add(searchResult);
                    }
                }
            }
            catch { }
            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path)) return result;

            info.ProviderIds.TryGetValue("IGDB", out var gameId);
            if (string.IsNullOrEmpty(gameId)) gameId = (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ProviderIds["IGDB"];
            if (string.IsNullOrEmpty(gameId)) return result;

            try
            {
                var client = await GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                var content = new StringContent($"where id = {gameId}; fields name,summary,first_release_date,genres.name,involved_companies.company.name,involved_companies.developer,involved_companies.publisher;", Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.GetArrayLength() > 0)
                    {
                        var root = document.RootElement[0];

                        var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));
                        var regionTag = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                        var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));

                        var tags = new List<string> { "Game", consoleTag };
                        if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);
                        if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);

                        var item = new Book
                        {
                            Name = root.GetProperty("name").GetString() ?? string.Empty,
                            Overview = root.TryGetProperty("summary", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty,
                            Tags = tags.ToArray()
                        };

                        if (root.TryGetProperty("first_release_date", out var releaseUnix))
                        {
                            var releaseDate = DateTimeOffset.FromUnixTimeSeconds(releaseUnix.GetInt64()).UtcDateTime;
                            item.PremiereDate = releaseDate;
                            item.ProductionYear = releaseDate.Year;
                        }

                        if (root.TryGetProperty("genres", out var genresArray) && genresArray.ValueKind == JsonValueKind.Array)
                            foreach (var genre in genresArray.EnumerateArray())
                                if (genre.TryGetProperty("name", out var genreName)) item.AddGenre(genreName.GetString());

                        if (root.TryGetProperty("involved_companies", out var companies) && companies.ValueKind == JsonValueKind.Array)
                            foreach (var entry in companies.EnumerateArray())
                            {
                                var isDev = entry.TryGetProperty("developer", out var devProp) && devProp.GetBoolean();
                                var isPub = entry.TryGetProperty("publisher", out var pubProp) && pubProp.GetBoolean();
                                if ((isDev || isPub) && entry.TryGetProperty("company", out var co) && co.TryGetProperty("name", out var coName))
                                {
                                    var name = coName.GetString();
                                    if (!string.IsNullOrWhiteSpace(name)) item.AddStudio(name);
                                }
                            }

                        item.SetProviderId("IGDB", gameId);
                        result.HasMetadata = true;
                        result.Item = item;
                    }
                }
            }
            catch { }
            return result;
        }
    }

    public class IgdbImageProvider : BaseIgdbProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "IGDB Video Game Database";
        public int Order => 1;

        public IgdbImageProvider(IHttpClientFactory httpClientFactory, ILogger<IgdbImageProvider> logger) : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var gameId = item.GetProviderId("IGDB") ?? await ResolveGameIdAsync(item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(gameId)) return list;

            try
            {
                var client = await GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                var content = new StringContent($"where id = {gameId}; fields cover.image_id,screenshots.image_id;", Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.GetArrayLength() > 0)
                    {
                        var root = document.RootElement[0];

                        if (root.TryGetProperty("cover", out var cover) && cover.TryGetProperty("image_id", out var cId) && cId.ValueKind != JsonValueKind.Null)
                        {
                            var cIdStr = cId.GetString();
                            if (!string.IsNullOrWhiteSpace(cIdStr))
                                list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{cIdStr}.jpg" });
                        }

                        if (root.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var shot in shots.EnumerateArray())
                            {
                                if (shot.TryGetProperty("image_id", out var sId) && sId.ValueKind != JsonValueKind.Null)
                                {
                                    var sIdStr = sId.GetString();
                                    if (!string.IsNullOrWhiteSpace(sIdStr))
                                        list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = $"https://images.igdb.com/igdb/image/upload/t_1080p/{sIdStr}.jpg" });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }
    }

    public class RawgMetadataProvider : BaseRawgProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "RAWG Video Game Database";
        public int Order => 2;

        private readonly PlatformResolver _platformResolver;

        public RawgMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<RawgMetadataProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            try
            {
                var response = await GetHttpClient().GetAsync($"https://api.rawg.io/api/games?search={Uri.EscapeDataString(cleanName)}&key={ApiKey}", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("results", out var resultsArray))
                    {
                        foreach (var game in resultsArray.EnumerateArray().Take(5))
                        {
                            results.Add(new RemoteSearchResult
                            {
                                Name = game.GetProperty("name").GetString() ?? string.Empty,
                                ProviderIds = new Dictionary<string, string> { { "RAWG", game.GetProperty("id").GetInt32().ToString() } },
                                SearchProviderName = Name
                            });
                        }
                    }
                }
            }
            catch { }
            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path)) return result;

            info.ProviderIds.TryGetValue("RAWG", out var gameId);
            if (string.IsNullOrEmpty(gameId)) gameId = (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ProviderIds["RAWG"];
            if (string.IsNullOrEmpty(gameId)) return result;

            try
            {
                var response = await GetHttpClient().GetAsync($"https://api.rawg.io/api/games/{gameId}?key={ApiKey}", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var root = document.RootElement;

                    var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));
                    var regionTag = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                    var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));

                    var tags = new List<string> { "Game", consoleTag };
                    if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);
                    if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);

                    var item = new Book
                    {
                        Name = root.GetProperty("name").GetString() ?? string.Empty,
                        Overview = root.TryGetProperty("description_raw", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty,
                        Tags = tags.ToArray()
                    };

                    if (root.TryGetProperty("genres", out var genresArray) && genresArray.ValueKind == JsonValueKind.Array)
                        foreach (var genre in genresArray.EnumerateArray())
                            if (genre.TryGetProperty("name", out var genreName)) item.AddGenre(genreName.GetString());

                    if (root.TryGetProperty("developers", out var devsArray) && devsArray.ValueKind == JsonValueKind.Array)
                        foreach (var dev in devsArray.EnumerateArray())
                            if (dev.TryGetProperty("name", out var devName) && !string.IsNullOrWhiteSpace(devName.GetString()))
                                item.AddStudio(devName.GetString());

                    if (root.TryGetProperty("publishers", out var pubsArray) && pubsArray.ValueKind == JsonValueKind.Array)
                        foreach (var pub in pubsArray.EnumerateArray())
                            if (pub.TryGetProperty("name", out var pubName) && !string.IsNullOrWhiteSpace(pubName.GetString()))
                                item.AddStudio(pubName.GetString());

                    item.SetProviderId("RAWG", gameId);
                    result.HasMetadata = true;
                    result.Item = item;
                }
            }
            catch { }
            return result;
        }

    }

    public class RawgImageProvider : BaseRawgProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "RAWG Video Game Database";
        public int Order => 2;

        public RawgImageProvider(IHttpClientFactory httpClientFactory, ILogger<RawgImageProvider> logger) : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var gameId = item.GetProviderId("RAWG") ?? await ResolveGameIdAsync(item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(gameId)) return list;

            try
            {
                var response = await GetHttpClient().GetAsync($"https://api.rawg.io/api/games/{gameId}?key={ApiKey}", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var root = document.RootElement;

                    if (root.TryGetProperty("background_image", out var bg) && bg.ValueKind != JsonValueKind.Null)
                    {
                        var url = bg.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = url });
                    }

                    if (root.TryGetProperty("background_image_additional", out var bgAdd) && bgAdd.ValueKind != JsonValueKind.Null)
                    {
                        var url = bgAdd.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = url });
                    }
                }
            }
            catch { }
            return list;
        }
    }

    public abstract class BaseWikipediaProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected BaseWikipediaProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0 (https://github.com/grimmdev/JellyEmu)");
            return client;
        }

        protected async Task<string?> ResolvePageIdAsync(string name, CancellationToken cancellationToken)
        {
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;

            try
            {
                var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(cleanName + " video game")}&utf8=&format=json";
                var response = await GetHttpClient().GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var query) &&
                        query.TryGetProperty("search", out var searchArray) &&
                        searchArray.GetArrayLength() > 0)
                    {
                        return searchArray[0].GetProperty("pageid").GetInt32().ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            }
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class WikipediaMetadataProvider : BaseWikipediaProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "Wikipedia";
        public int Order => 3;

        private readonly PlatformResolver _platformResolver;

        public WikipediaMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<WikipediaMetadataProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            try
            {
                var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(cleanName + " video game")}&utf8=&format=json";
                var response = await GetHttpClient().GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var query) &&
                        query.TryGetProperty("search", out var searchArray))
                    {
                        foreach (var page in searchArray.EnumerateArray().Take(5))
                        {
                            results.Add(new RemoteSearchResult
                            {
                                Name = page.GetProperty("title").GetString() ?? string.Empty,
                                ProviderIds = new Dictionary<string, string> { { "Wikipedia", page.GetProperty("pageid").GetInt32().ToString() } },
                                SearchProviderName = Name
                            });
                        }
                    }
                }
            }
            catch { }
            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path)) return result;

            info.ProviderIds.TryGetValue("Wikipedia", out var pageId);
            if (string.IsNullOrEmpty(pageId)) pageId = (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ProviderIds["Wikipedia"];
            if (string.IsNullOrEmpty(pageId)) return result;

            try
            {
                var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=extracts&exintro&explaintext&pageids={pageId}&format=json";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var query) &&
                        query.TryGetProperty("pages", out var pages) &&
                        pages.TryGetProperty(pageId, out var page))
                    {
                        var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));
                        var regionTag = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                        var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));

                        var tags = new List<string> { "Game", consoleTag };
                        if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);
                        if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);

                        var item = new Book
                        {
                            Name = page.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                            Overview = page.TryGetProperty("extract", out var ext) ? ext.GetString() ?? string.Empty : string.Empty,
                            Tags = tags.ToArray()
                        };

                        item.SetProviderId("Wikipedia", pageId);
                        result.HasMetadata = true;
                        result.Item = item;
                    }
                }
            }
            catch { }
            return result;
        }
    }

    public class WikipediaImageProvider : BaseWikipediaProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "Wikipedia";
        public int Order => 3;

        public WikipediaImageProvider(IHttpClientFactory httpClientFactory, ILogger<WikipediaImageProvider> logger) : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var pageId = item.GetProviderId("Wikipedia") ?? await ResolvePageIdAsync(item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(pageId)) return list;

            try
            {
                var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=pageimages&pithumbsize=1000&pageids={pageId}&format=json";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var query) &&
                        query.TryGetProperty("pages", out var pages) &&
                        pages.TryGetProperty(pageId, out var page) &&
                        page.TryGetProperty("thumbnail", out var thumbnail) &&
                        thumbnail.TryGetProperty("source", out var source))
                    {
                        var imgUrl = source.GetString();
                        if (!string.IsNullOrWhiteSpace(imgUrl))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = imgUrl });
                        }
                    }
                }
            }
            catch { }
            return list;
        }
    }

    public abstract class BaseRommProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected static bool IsEnabled => Plugin.Instance?.Configuration.RommEnabled == true;
        protected static string InstanceUrl => (Plugin.Instance?.Configuration.RommInstanceUrl ?? string.Empty).TrimEnd('/');
        protected static string Username => Plugin.Instance?.Configuration.RommUsername ?? string.Empty;
        protected static string Password => Plugin.Instance?.Configuration.RommPassword ?? string.Empty;

        // Simple in-process token cache — keyed by InstanceUrl+Username so it auto-invalidates on credential changes.

        protected BaseRommProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
            }
            return client;
        }

        /// <summary>
        /// Searches the Romm API for a ROM by name and returns the first matching ROM object,
        /// or null if nothing is found.
        /// </summary>
        protected async Task<JsonElement?> ResolveRomAsync(string name, CancellationToken cancellationToken)
        {
            if (!IsEnabled || string.IsNullOrEmpty(InstanceUrl)) return null;
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;

            try
            {
                var url = $"{InstanceUrl}/api/roms?search_term={Uri.EscapeDataString(cleanName)}&limit=1";
                var client = GetHttpClient();
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    // Romm returns { "items": [...], "total": N }
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                        return items[0].Clone();
                }
            }
            catch { }
            return null;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            var client = GetHttpClient();
            return await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }

    public class RommMetadataProvider : BaseRommProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "Romm";
        public int Order => 4;

        private readonly PlatformResolver _platformResolver;

        public RommMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<RommMetadataProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!IsEnabled) return results;
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            if (string.IsNullOrEmpty(cleanName) || string.IsNullOrEmpty(InstanceUrl)) return results;

            try
            {
                var url = $"{InstanceUrl}/api/roms?search_term={Uri.EscapeDataString(cleanName)}&limit=5";
                var client = GetHttpClient();
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("items", out var items))
                    {
                        foreach (var rom in items.EnumerateArray())
                        {
                            var romId = rom.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : string.Empty;
                            var romName = rom.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                            if (string.IsNullOrEmpty(romId)) continue;

                            var searchResult = new RemoteSearchResult
                            {
                                Name = romName,
                                ProviderIds = new Dictionary<string, string> { { "Romm", romId } },
                                SearchProviderName = Name
                            };
                            results.Add(searchResult);
                        }
                    }
                }
            }
            catch { }
            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!IsEnabled) return result;
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path)) return result;
            if (string.IsNullOrEmpty(InstanceUrl)) return result;

            // Try to get cached ROM id first, otherwise search
            info.ProviderIds.TryGetValue("Romm", out var romId);
            JsonElement? rom = null;

            if (!string.IsNullOrEmpty(romId))
            {
                try
                {
                    var client = GetHttpClient();
                    var response = await client.GetAsync($"{InstanceUrl}/api/roms/{romId}", cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        rom = doc.RootElement.Clone();
                    }
                }
                catch { }
            }

            if (rom == null)
                rom = await ResolveRomAsync(info.Name, cancellationToken).ConfigureAwait(false);

            if (rom == null) return result;

            try
            {
                var resolvedId = rom.Value.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : romId ?? string.Empty;

                var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));
                var regionTag = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));

                var tags = new List<string> { "Game", consoleTag };
                if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);
                if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);

                var item = new Book
                {
                    Name = rom.Value.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                    Overview = rom.Value.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? string.Empty : string.Empty,
                    Tags = tags.ToArray()
                };

                // Release year from first_release_date (unix timestamp) or year_released (int)
                if (rom.Value.TryGetProperty("first_release_date", out var frdEl) && frdEl.ValueKind == JsonValueKind.Number)
                {
                    var releaseDate = DateTimeOffset.FromUnixTimeSeconds(frdEl.GetInt64()).UtcDateTime;
                    item.PremiereDate = releaseDate;
                    item.ProductionYear = releaseDate.Year;
                }
                else if (rom.Value.TryGetProperty("year_released", out var yrEl) && yrEl.ValueKind == JsonValueKind.Number)
                {
                    item.ProductionYear = yrEl.GetInt32();
                    item.PremiereDate = new DateTime(yrEl.GetInt32(), 1, 1);
                }

                // Genres
                if (rom.Value.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
                    foreach (var g in genresEl.EnumerateArray())
                    {
                        var gName = g.TryGetProperty("name", out var gn) ? gn.GetString() : g.GetString();
                        if (!string.IsNullOrWhiteSpace(gName)) item.AddGenre(gName);
                    }

                // Platforms / studios from the rom's platform name
                if (rom.Value.TryGetProperty("platform_name", out var platformEl))
                {
                    var pName = platformEl.GetString();
                    if (!string.IsNullOrWhiteSpace(pName)) item.AddStudio(pName);
                }

                if (!string.IsNullOrEmpty(resolvedId))
                    item.SetProviderId("Romm", resolvedId);

                result.HasMetadata = true;
                result.Item = item;
            }
            catch { }
            return result;
        }
    }

    public class RommImageProvider : BaseRommProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "Romm";
        public int Order => 4;

        public RommImageProvider(IHttpClientFactory httpClientFactory, ILogger<RommImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!IsEnabled || string.IsNullOrEmpty(InstanceUrl)) return list;
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var romId = item.GetProviderId("Romm");
            JsonElement? rom = null;

            if (!string.IsNullOrEmpty(romId))
            {
                try
                {
                    var client = GetHttpClient();
                    var response = await client.GetAsync($"{InstanceUrl}/api/roms/{romId}", cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        rom = doc.RootElement.Clone();
                    }
                }
                catch { }
            }

            if (rom == null)
                rom = await ResolveRomAsync(item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);

            if (rom == null) return list;

            try
            {
                // cover_url is the primary artwork Romm exposes
                if (rom.Value.TryGetProperty("url_cover", out var coverEl) && coverEl.ValueKind != JsonValueKind.Null)
                {
                    var coverUrl = coverEl.GetString();
                    if (!string.IsNullOrWhiteSpace(coverUrl))
                    {
                        // Romm may return a relative path
                        if (!Uri.IsWellFormedUriString(coverUrl, UriKind.Absolute))
                            coverUrl = $"{InstanceUrl}{(coverUrl.StartsWith("/") ? "" : "/")}{coverUrl}";
                        list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = coverUrl });
                    }
                }

                // screenshots as backdrop images
                if (rom.Value.TryGetProperty("url_screenshots", out var screenshotsEl) && screenshotsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var shot in screenshotsEl.EnumerateArray())
                    {
                        var shotUrl = shot.GetString();
                        if (string.IsNullOrWhiteSpace(shotUrl)) continue;
                        if (!Uri.IsWellFormedUriString(shotUrl, UriKind.Absolute))
                            shotUrl = $"{InstanceUrl}{(shotUrl.StartsWith("/") ? "" : "/")}{shotUrl}";
                        list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = shotUrl });
                    }
                }
            }
            catch { }
            return list;
        }
    }

}