using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using JellyEmu.Services;

namespace JellyEmu.Providers
{
    public abstract class BaseIgdbProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;
        protected readonly IgdbClientService IgdbClientService;

        protected BaseIgdbProvider(IHttpClientFactory httpClientFactory, ILogger logger, IgdbClientService igdbClientService)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
            IgdbClientService = igdbClientService;
        }

        protected static string? TryExtractEmbeddedIgdbId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = Regex.Match(path, @"\[igdb-(\d+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Searches IGDB for a game by name, trying exact name first then normalized (accent-stripped) fallback.
        /// Returns the numeric id and slug of the best match.
        /// </summary>
        protected async Task<(string? id, string? slug)> ResolveGameAsync(string name, CancellationToken cancellationToken)
        {
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return (null, null);

            var candidates = new[] { cleanName, RomExtensions.NormalizeForSearch(cleanName) }
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var query in candidates)
            {
                try
                {
                    var client = await IgdbClientService.GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                    var content = new StringContent($"search \"{query}\"; fields id,slug; limit 1;", Encoding.UTF8, "text/plain");
                    var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.GetArrayLength() > 0)
                        {
                            var first = doc.RootElement[0];
                            var id = first.GetProperty("id").GetInt32().ToString();
                            var slug = first.TryGetProperty("slug", out var s) ? s.GetString() : null;
                            return (id, slug);
                        }
                    }
                }
                catch { }
            }
            return (null, null);
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            return HttpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }

    public class IgdbMetadataProvider : BaseIgdbProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "IGDB Metadata Provider";
        public int Order => 1;

        private readonly PlatformResolver _platformResolver;
        private readonly ILogger<IgdbMetadataProvider> _logger;

        public IgdbMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<IgdbMetadataProvider> logger,
            PlatformResolver platformResolver,
            IgdbClientService igdbClientService)
            : base(httpClientFactory, logger, igdbClientService)
        {
            _platformResolver = platformResolver;
            _logger = logger;
        }

        // Identify
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            searchInfo.ProviderIds.TryGetValue("IGDB", out var directId);
            if (string.IsNullOrEmpty(directId))
                directId = TryExtractEmbeddedIgdbId(searchInfo.Path);

            if (!string.IsNullOrEmpty(directId))
            {
                try
                {
                    var client = await IgdbClientService.GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                    var content = new StringContent(
                        $"where id = {directId}; fields id,name,slug,first_release_date,cover.image_id; limit 1;",
                        Encoding.UTF8, "text/plain");
                    var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.GetArrayLength() > 0)
                        {
                            var game = document.RootElement[0];
                            var gameId = game.GetProperty("id").GetInt32().ToString();
                            var slug = game.TryGetProperty("slug", out var s) ? s.GetString() ?? gameId : gameId;

                            var searchResult = new RemoteSearchResult
                            {
                                Name = game.GetProperty("name").GetString() ?? string.Empty,
                                ProviderIds = new Dictionary<string, string> { { "IGDB", gameId }, { "IGDBSlug", slug } },
                                SearchProviderName = Name
                            };

                            if (game.TryGetProperty("first_release_date", out var releaseUnix))
                                searchResult.ProductionYear = DateTimeOffset.FromUnixTimeSeconds(releaseUnix.GetInt64()).UtcDateTime.Year;

                            if (game.TryGetProperty("cover", out var cover) &&
                                cover.TryGetProperty("image_id", out var cId) &&
                                cId.ValueKind != JsonValueKind.Null)
                            {
                                var cIdStr = cId.GetString();
                                if (!string.IsNullOrWhiteSpace(cIdStr))
                                    searchResult.ImageUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{cIdStr}.jpg";
                            }

                            return new[] { searchResult };
                        }
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
                    var client = await IgdbClientService.GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                    var content = new StringContent(
                        $"search \"{query}\"; fields id,name,slug,first_release_date,cover.image_id; limit 5;",
                        Encoding.UTF8, "text/plain");
                    var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.GetArrayLength() == 0) continue;

                        foreach (var game in document.RootElement.EnumerateArray())
                        {
                            var gameId = game.GetProperty("id").GetInt32().ToString();
                            var slug = game.TryGetProperty("slug", out var s) ? s.GetString() ?? gameId : gameId;

                            var searchResult = new RemoteSearchResult
                            {
                                Name = game.GetProperty("name").GetString() ?? string.Empty,
                                ProviderIds = new Dictionary<string, string> { { "IGDB", gameId }, { "IGDBSlug", slug } },
                                SearchProviderName = Name
                            };

                            if (game.TryGetProperty("first_release_date", out var releaseUnix))
                                searchResult.ProductionYear = DateTimeOffset.FromUnixTimeSeconds(releaseUnix.GetInt64()).UtcDateTime.Year;

                            if (game.TryGetProperty("cover", out var cover) &&
                                cover.TryGetProperty("image_id", out var cId) &&
                                cId.ValueKind != JsonValueKind.Null)
                            {
                                var cIdStr = cId.GetString();
                                if (!string.IsNullOrWhiteSpace(cIdStr))
                                    searchResult.ImageUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{cIdStr}.jpg";
                            }

                            results.Add(searchResult);
                        }
                        break;
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

            info.ProviderIds.TryGetValue("IGDB", out var gameId);
            string? slug = null;
            info.ProviderIds.TryGetValue("IGDBSlug", out slug);

            if (string.IsNullOrEmpty(gameId))
                gameId = TryExtractEmbeddedIgdbId(info.Path);

            if (string.IsNullOrEmpty(gameId))
            {
                var resolved = await ResolveGameAsync(info.Name, cancellationToken).ConfigureAwait(false);
                gameId = resolved.id;
                slug = resolved.slug;
            }
            if (string.IsNullOrEmpty(gameId)) return result;

            try
            {
                var client = await IgdbClientService.GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                var content = new StringContent(
                    $"where id = {gameId}; fields name,slug,summary,first_release_date,genres.name,involved_companies.company.name,involved_companies.developer,involved_companies.publisher,total_rating,total_rating_count,collection.name,franchises.name;",
                    Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.GetArrayLength() > 0)
                    {
                        var root = document.RootElement[0];

                        if (root.TryGetProperty("slug", out var slugEl) && slugEl.ValueKind != JsonValueKind.Null)
                            slug = slugEl.GetString() ?? slug;

                        var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));
                        var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));

                        var tags = new List<string> { "JellyEmu", "Game", consoleTag };
                        tags.AddRange(PlatformResolver.ResolveRegions(RomExtensions.EffectiveRomPath(info.Path)));
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

                        if (root.TryGetProperty("total_rating", out var totalRating) &&
                            totalRating.ValueKind == JsonValueKind.Number &&
                            root.TryGetProperty("total_rating_count", out var ratingCount) &&
                            ratingCount.ValueKind == JsonValueKind.Number &&
                            ratingCount.GetInt32() > 0)
                        {
                            item.CommunityRating = (float)Math.Round(totalRating.GetDouble() / 10.0, 1);
                        }

                        if (root.TryGetProperty("collection", out var collection) && collection.ValueKind == JsonValueKind.Object && collection.TryGetProperty("name", out var collectionName))
                            item.SeriesName = collectionName.GetString();
                        else if (root.TryGetProperty("franchises", out var franchises) && franchises.ValueKind == JsonValueKind.Array && franchises.GetArrayLength() > 0)
                            if (franchises[0].TryGetProperty("name", out var franchiseName))
                                item.SeriesName = franchiseName.GetString();

                        try
                        {
                            var ttbContent = new StringContent($"where game_id = {gameId}; fields normally,hastily,completely; limit 1;", Encoding.UTF8, "text/plain");
                            var ttbResponse = await client.PostAsync("https://api.igdb.com/v4/game_time_to_beats", ttbContent, cancellationToken).ConfigureAwait(false);
                            if (ttbResponse.IsSuccessStatusCode)
                            {
                                using var ttbDoc = JsonDocument.Parse(await ttbResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                                if (ttbDoc.RootElement.GetArrayLength() > 0)
                                {
                                    var ttb = ttbDoc.RootElement[0];
                                    var codes = new List<string>();
                                    
                                    if (ttb.TryGetProperty("normally", out var norm) && norm.ValueKind == JsonValueKind.Number && norm.GetInt32() > 0)
                                        codes.Add($"M{norm.GetInt32() / 3600}");
                                    
                                    if (ttb.TryGetProperty("hastily", out var haste) && haste.ValueKind == JsonValueKind.Number && haste.GetInt32() > 0)
                                        codes.Add($"H{haste.GetInt32() / 3600}");
                                    
                                    if (ttb.TryGetProperty("completely", out var comp) && comp.ValueKind == JsonValueKind.Number && comp.GetInt32() > 0)
                                        codes.Add($"C{comp.GetInt32() / 3600}");

                                    if (codes.Count > 0)
                                    {
                                        item.SetProviderId("IgdbTTB", string.Join(",", codes));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[JellyEmu] Failed to fetch Time to Beat for game {Id}", gameId);
                        }

                        item.SetProviderId("IGDB", gameId);
                        if (!string.IsNullOrEmpty(slug))
                            item.SetProviderId("IGDBSlug", slug);

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
        public string Name => "IGDB Image Provider";
        public int Order => 1;

        public IgdbImageProvider(IHttpClientFactory httpClientFactory, ILogger<IgdbImageProvider> logger, IgdbClientService igdbClientService)
            : base(httpClientFactory, logger, igdbClientService) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var gameId = item.GetProviderId("IGDB");
            if (string.IsNullOrEmpty(gameId))
            {
                var resolved = await ResolveGameAsync(item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
                gameId = resolved.id;
                if (!string.IsNullOrEmpty(resolved.slug))
                item.SetProviderId("IGDBSlug", resolved.slug);
            }
            if (string.IsNullOrEmpty(gameId)) return list;

            try
            {
                var client = await IgdbClientService.GetIgdbClientAsync(cancellationToken).ConfigureAwait(false);
                var content = new StringContent($"where id = {gameId}; fields cover.image_id,screenshots.image_id;", Encoding.UTF8, "text/plain");
                var response = await client.PostAsync("https://api.igdb.com/v4/games", content, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.GetArrayLength() > 0)
                    {
                        var root = document.RootElement[0];

                        if (root.TryGetProperty("cover", out var cover) &&
                            cover.TryGetProperty("image_id", out var cId) &&
                            cId.ValueKind != JsonValueKind.Null)
                        {
                            var cIdStr = cId.GetString();
                            if (!string.IsNullOrWhiteSpace(cIdStr))
                                list.Add(new RemoteImageInfo
                                {
                                    ProviderName = Name,
                                    Type = ImageType.Primary,
                                    Url = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{cIdStr}.jpg"
                                });
                        }

                        if (root.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var shot in shots.EnumerateArray())
                            {
                                if (shot.TryGetProperty("image_id", out var sId) && sId.ValueKind != JsonValueKind.Null)
                                {
                                    var sIdStr = sId.GetString();
                                    if (!string.IsNullOrWhiteSpace(sIdStr))
                                        list.Add(new RemoteImageInfo
                                        {
                                            ProviderName = Name,
                                            Type = ImageType.Backdrop,
                                            Url = $"https://images.igdb.com/igdb/image/upload/t_1080p/{sIdStr}.jpg"
                                        });
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

    public class IgdbGameExternalId : IExternalId
    {
        public string ProviderName => "IGDB";
        public string Key => "IGDB";
        public ExternalIdMediaType? Type => null;
        public bool Supports(IHasProviderIds item) => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);
    }

    public class IgdbGameExternalSlug : IExternalId
    {
        public string ProviderName => "IGDBSlug";
        public string Key => "IGDBSlug";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://www.igdb.com/games/{0}";
        public bool Supports(IHasProviderIds item) => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);
    }

    public class IgdbExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "IGDB";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.TryGetProviderId("IGDBSlug", out var slug))
                yield return $"https://www.igdb.com/games/{slug}";
            else if (item.TryGetProviderId("IGDB", out var gameId))
                yield return $"https://www.igdb.com/games/{gameId}";
        }
    }
}