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
        private static readonly Dictionary<string, string> _extensionMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            // NES - fceumm/nestopia
            { ".nes", "NES" }, { ".fds", "NES" }, { ".unf", "NES" }, { ".unif", "NES" },
            // SNES - snes9x/bsnes
            { ".smc", "SNES" }, { ".sfc", "SNES" }, { ".swc", "SNES" }, { ".fig", "SNES" },
            // N64 - mupen64plus_next/parallel-n64
            { ".z64", "N64" }, { ".n64", "N64" }, { ".v64", "N64" },
            // Game Boy / Game Boy Color - both use gambatte
            { ".gb", "Game Boy" }, { ".gbc", "Game Boy" },
            // Game Boy Advance - mgba
            { ".gba", "Game Boy Advance" },
            // Nintendo DS - melonds/desmume
            { ".nds", "Nintendo DS" },
            // Virtual Boy - beetle_vb
            { ".vb", "Virtual Boy" },
            // Sega - genesis_plus_gx
            { ".sms", "Master System" },
            { ".gg", "Game Gear" },
            { ".md", "Sega Genesis" }, { ".smd", "Sega Genesis" }, { ".gen", "Sega Genesis" },
            { ".68k", "Sega Genesis" }, { ".sgd", "Sega Genesis" },
            // Sega 32X - picodrive
            { ".32x", "Sega 32X" },
            // PlayStation - pcsx_rearmed
            { ".pbp", "PlayStation" }, { ".cue", "PlayStation" }, { ".chd", "PlayStation" }, { ".iso", "PlayStation" },
            // Atari - stella2014/prosystem/handy/virtualjaguar
            { ".a26", "Atari 2600" },
            { ".a78", "Atari 7800" },
            { ".lnx", "Atari Lynx" },
            { ".jag", "Atari Jaguar" }, { ".j64", "Atari Jaguar" },
            // WonderSwan - mednafen_wswan
            { ".ws", "WonderSwan" }, { ".wsc", "WonderSwan" },
            // TurboGrafx-16 - mednafen_pce
            { ".pce", "TurboGrafx-16" },
            // ColecoVision - gearcoleco
            { ".col", "ColecoVision" }, { ".cv", "ColecoVision" },
            // NeoGeo Pocket - mednafen_ngp
            { ".ngp", "NeoGeo Pocket" }, { ".ngc", "NeoGeo Pocket" },
        };

        public static bool IsRomPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && _extensionMappings.ContainsKey(ext);
        }

        public static string GetConsoleTag(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && _extensionMappings.TryGetValue(ext, out var tag)) return tag;
            return ext?.TrimStart('.') ?? "Unknown";
        }

        public static string CleanName(string name)
        {
            var cleaned = Regex.Replace(name ?? string.Empty, @"(\(.*?\)|\[.*?\])", "").Trim();
            cleaned = Regex.Replace(cleaned.Replace("_", " ").Replace("-", " "), @"\s+", " ").Trim();
            return cleaned;
        }
    }

    public class RomLocalProvider : ILocalMetadataProvider<Book>, IRemoteImageProvider
    {
        private readonly ILogger<RomLocalProvider> _logger;

        public RomLocalProvider(ILogger<RomLocalProvider> logger)
        {
            _logger = logger;
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
                    result.HasMetadata = true;
                    result.Item = new Book
                    {
                        Overview = "Parsed successfully from local .nfo file!",
                        PremiereDate = new DateTime(1990, 1, 1),
                        Tags = new[] { "Game", RomExtensions.GetConsoleTag(info.Path) }
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
                var dir = Path.GetDirectoryName(item.Path);
                var baseName = Path.GetFileNameWithoutExtension(item.Path);
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

        public IgdbMetadataProvider(IHttpClientFactory httpClientFactory, ILogger<IgdbMetadataProvider> logger) : base(httpClientFactory, logger) { }

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
                var content = new StringContent($"where id = {gameId}; fields name,summary,first_release_date,genres.name;", Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.GetArrayLength() > 0)
                    {
                        var root = document.RootElement[0];
                        var item = new Book
                        {
                            Name = root.GetProperty("name").GetString() ?? string.Empty,
                            Overview = root.TryGetProperty("summary", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty,
                            Tags = new[] { "Game", RomExtensions.GetConsoleTag(info.Path) }
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

        public RawgMetadataProvider(IHttpClientFactory httpClientFactory, ILogger<RawgMetadataProvider> logger) : base(httpClientFactory, logger) { }

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
                    var item = new Book
                    {
                        Name = root.GetProperty("name").GetString() ?? string.Empty,
                        Overview = root.TryGetProperty("description_raw", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty,
                        Tags = new[] { "Game", RomExtensions.GetConsoleTag(info.Path) }
                    };

                    if (root.TryGetProperty("genres", out var genresArray) && genresArray.ValueKind == JsonValueKind.Array)
                        foreach (var genre in genresArray.EnumerateArray())
                            if (genre.TryGetProperty("name", out var genreName)) item.AddGenre(genreName.GetString());

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
}