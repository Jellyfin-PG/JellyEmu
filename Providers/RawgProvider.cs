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

        protected static string? TryExtractEmbeddedRawgId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = Regex.Match(path, @"\[rawg-(\d+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Resolves a RAWG game ID by name, retrying with accent-normalized name as fallback.
        /// </summary>
        protected async Task<string?> ResolveGameIdAsync(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;

            var candidates = new[] { cleanName, RomExtensions.NormalizeForSearch(cleanName) }
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var query in candidates)
            {
                try
                {
                    var url = $"https://api.rawg.io/api/games?search={Uri.EscapeDataString(query)}&key={ApiKey}&page_size=1";
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
            }
            return null;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class RawgMetadataProvider : BaseRawgProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "RAWG Metadata Provider";
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

        // Identify
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            searchInfo.ProviderIds.TryGetValue("RAWG", out var directId);
            if (string.IsNullOrEmpty(directId))
                directId = TryExtractEmbeddedRawgId(searchInfo.Path);

            if (!string.IsNullOrEmpty(directId))
            {
                try
                {
                    var response = await GetHttpClient().GetAsync(
                        $"https://api.rawg.io/api/games/{directId}?key={ApiKey}",
                        cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        var root = document.RootElement;

                        var sr = new RemoteSearchResult
                        {
                            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                            ProviderIds = new Dictionary<string, string> { { "RAWG", directId } },
                            SearchProviderName = Name
                        };

                        if (root.TryGetProperty("background_image", out var bg) && bg.ValueKind != JsonValueKind.Null)
                        {
                            var imgUrl = bg.GetString();
                            if (!string.IsNullOrWhiteSpace(imgUrl)) sr.ImageUrl = imgUrl;
                        }

                        if (root.TryGetProperty("released", out var rel) && rel.ValueKind == JsonValueKind.String &&
                            DateTime.TryParse(rel.GetString(), out var releaseDate))
                            sr.ProductionYear = releaseDate.Year;

                        return new[] { sr };
                    }
                }
                catch { }
                return results;
            }

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);

            foreach (var query in new[] { cleanName, normalizedName }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var response = await GetHttpClient().GetAsync(
                        $"https://api.rawg.io/api/games?search={Uri.EscapeDataString(query)}&key={ApiKey}&page_size=5",
                        cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("results", out var resultsArray) && resultsArray.GetArrayLength() > 0)
                        {
                            foreach (var game in resultsArray.EnumerateArray().Take(5))
                            {
                                var sr = new RemoteSearchResult
                                {
                                    Name = game.GetProperty("name").GetString() ?? string.Empty,
                                    ProviderIds = new Dictionary<string, string> { { "RAWG", game.GetProperty("id").GetInt32().ToString() } },
                                    SearchProviderName = Name
                                };

                                if (game.TryGetProperty("background_image", out var bg) && bg.ValueKind != JsonValueKind.Null)
                                {
                                    var imgUrl = bg.GetString();
                                    if (!string.IsNullOrWhiteSpace(imgUrl))
                                        sr.ImageUrl = imgUrl;
                                }

                                results.Add(sr);
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path)) return result;

            info.ProviderIds.TryGetValue("RAWG", out var gameId);
            if (string.IsNullOrEmpty(gameId))
                gameId = TryExtractEmbeddedRawgId(info.Path);
            if (string.IsNullOrEmpty(gameId))
                gameId = (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ProviderIds["RAWG"];
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

                    var tags = new List<string> { "JellyEmu", consoleTag };
                    if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);
                    if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);

                    var item = new Book
                    {
                        Name = root.GetProperty("name").GetString() ?? string.Empty,
                        Overview = root.TryGetProperty("description_raw", out var desc) ? (desc.GetString() ?? string.Empty) : string.Empty,
                        Tags = tags.ToArray()
                    };

                    if (root.TryGetProperty("metacritic", out var metacritic) && metacritic.ValueKind == JsonValueKind.Number)
                        item.CriticRating = metacritic.GetSingle();

                    if (root.TryGetProperty("rating", out var rawgRating) && rawgRating.ValueKind == JsonValueKind.Number)
                        item.CommunityRating = (float)Math.Round(rawgRating.GetDouble() * 2, 1);

                    if (root.TryGetProperty("esrb_rating", out var esrb) && esrb.ValueKind == JsonValueKind.Object && esrb.TryGetProperty("name", out var esrbName))
                        item.OfficialRating = esrbName.GetString();

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

                    try
                    {
                        var teamResponse = await GetHttpClient().GetAsync(
                            $"https://api.rawg.io/api/games/{gameId}/development-team?key={ApiKey}", cancellationToken).ConfigureAwait(false);
                        if (teamResponse.IsSuccessStatusCode)
                        {
                            using var teamDoc = JsonDocument.Parse(await teamResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                            if (teamDoc.RootElement.TryGetProperty("results", out var teamArray) && teamArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var member in teamArray.EnumerateArray())
                                {
                                    if (!member.TryGetProperty("name", out var memberName) || string.IsNullOrWhiteSpace(memberName.GetString())) continue;

                                    string role = "Developer";
                                    if (member.TryGetProperty("positions", out var positions) && positions.ValueKind == JsonValueKind.Array && positions.GetArrayLength() > 0)
                                        role = positions[0].TryGetProperty("name", out var posName) ? (posName.GetString() ?? "Developer") : "Developer";

                                    var pInfo = new PersonInfo { Name = memberName.GetString(), Type = PersonKind.Author, Role = role };

                                    if (member.TryGetProperty("image", out var imgEl) && imgEl.ValueKind == JsonValueKind.String)
                                    {
                                        var imgUrl = imgEl.GetString();
                                        if (!string.IsNullOrWhiteSpace(imgUrl)) pInfo.ImageUrl = imgUrl;
                                    }

                                    result.AddPerson(pInfo);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }
    }

    public class RawgImageProvider : BaseRawgProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "RAWG Image Provider";
        public int Order => 2;

        public RawgImageProvider(IHttpClientFactory httpClientFactory, ILogger<RawgImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var gameId = item.GetProviderId("RAWG") ?? await ResolveGameIdAsync(
                item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
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

    public class RawgPersonMetadataProvider : BaseRawgProvider, IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
    {
        public string Name => "RAWG Creator Metadata Provider";
        public int Order => 1;

        public RawgPersonMetadataProvider(IHttpClientFactory httpClientFactory, ILogger<RawgPersonMetadataProvider> logger)
            : base(httpClientFactory, logger) { }

        // Identify
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(ApiKey)) return results;

            searchInfo.ProviderIds.TryGetValue("RAWG", out var directId);

            if (!string.IsNullOrEmpty(directId))
            {
                try
                {
                    var response = await GetHttpClient().GetAsync(
                        $"https://api.rawg.io/api/creators/{directId}?key={ApiKey}",
                        cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        var root = doc.RootElement;

                        var sr = new RemoteSearchResult
                        {
                            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? searchInfo.Name : searchInfo.Name,
                            ProviderIds = new Dictionary<string, string> { { "RAWG", directId } },
                            SearchProviderName = Name
                        };

                        if (root.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
                        {
                            var imgUrl = img.GetString();
                            if (!string.IsNullOrWhiteSpace(imgUrl)) sr.ImageUrl = imgUrl;
                        }

                        return new[] { sr };
                    }
                }
                catch { }
                return results;
            }

            try
            {
                var url = $"https://api.rawg.io/api/creators?search={Uri.EscapeDataString(searchInfo.Name)}&key={ApiKey}";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var creator in arr.EnumerateArray().Take(5))
                        {
                            if (!creator.TryGetProperty("id", out var idEl) || !creator.TryGetProperty("name", out var nameEl)) continue;

                            var sr = new RemoteSearchResult
                            {
                                Name = nameEl.GetString() ?? string.Empty,
                                ProviderIds = new Dictionary<string, string> { { "RAWG", idEl.GetInt32().ToString() } },
                                SearchProviderName = Name
                            };

                            if (creator.TryGetProperty("image", out var imgEl) && imgEl.ValueKind == JsonValueKind.String)
                            {
                                var imgUrl = imgEl.GetString();
                                if (!string.IsNullOrWhiteSpace(imgUrl)) sr.ImageUrl = imgUrl;
                            }

                            results.Add(sr);
                        }
                    }
                }
            }
            catch { }
            return results;
        }

        public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Person> { HasMetadata = false };
            if (string.IsNullOrEmpty(ApiKey)) return result;

            info.ProviderIds.TryGetValue("RAWG", out var rawgId);
            if (string.IsNullOrEmpty(rawgId))
            {
                var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                rawgId = searchResults.FirstOrDefault()?.ProviderIds["RAWG"];
            }
            if (string.IsNullOrEmpty(rawgId)) return result;

            try
            {
                var url = $"https://api.rawg.io/api/creators/{rawgId}?key={ApiKey}";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var root = doc.RootElement;

                    var person = new Person
                    {
                        Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? info.Name : info.Name,
                        Overview = root.TryGetProperty("description", out var d)
                            ? (d.GetString() ?? string.Empty)
                                .Replace("<p>", "").Replace("</p>", "")
                                .Replace("<br />", "\n").Replace("&#39;", "'")
                            : string.Empty
                    };

                    person.SetProviderId("RAWG", rawgId);
                    result.Item = person;
                    result.HasMetadata = true;
                }
            }
            catch { }
            return result;
        }
    }

    public class RawgPersonImageProvider : BaseRawgProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "RAWG Creator Image Provider";
        public int Order => 1;

        public RawgPersonImageProvider(IHttpClientFactory httpClientFactory, ILogger<RawgPersonImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Person;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (string.IsNullOrEmpty(ApiKey)) return list;

            var rawgId = item.GetProviderId("RAWG");
            if (string.IsNullOrEmpty(rawgId)) return list;

            try
            {
                var url = $"https://api.rawg.io/api/creators/{rawgId}?key={ApiKey}";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("image", out var img) && img.ValueKind != JsonValueKind.Null)
                    {
                        var imgUrl = img.GetString();
                        if (!string.IsNullOrWhiteSpace(imgUrl))
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = imgUrl });
                    }
                }
            }
            catch { }
            return list;
        }
    }

    public class RawgPersonExternalId : IExternalId
    {
        public string ProviderName => "RAWG";
        public string Key => "RAWG";
        public ExternalIdMediaType? Type => ExternalIdMediaType.Person;
        public string UrlFormatString => "https://rawg.io/creators/{0}";
        public bool Supports(IHasProviderIds item) => item is Person;
    }

    public class RawgGameExternalId : IExternalId
    {
        public string ProviderName => "RAWG";
        public string Key => "RAWG";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://rawg.io/games/{0}";
        public bool Supports(IHasProviderIds item) 
            => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);
    }

    public class RawgExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "RAWG";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item is Person && item.TryGetProviderId("RAWG", out var personId))
                yield return $"https://rawg.io/creators/{personId}";
            else if (item is Book && item.TryGetProviderId("RAWG", out var gameId))
                yield return $"https://rawg.io/games/{gameId}";
        }
    }
}