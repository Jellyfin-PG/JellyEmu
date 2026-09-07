using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
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
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"{JellyEmuVersion.UserAgent} (https://github.com/Jellyfin-PG/JellyEmu)");
            }
            return client;
        }

        protected static string? TryExtractEmbeddedWikiId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = Regex.Match(path, @"\[wiki-(\d+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Resolves a Wikipedia page ID by game name, retrying with accent-normalized fallback.
        /// </summary>
        protected async Task<string?> ResolvePageIdAsync(string name, CancellationToken cancellationToken)
        {
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;

            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);
            var queries = new List<string> { cleanName + " video game", cleanName };
            if (!string.Equals(normalizedName, cleanName, StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(normalizedName + " video game");
                queries.Add(normalizedName);
            }

            foreach (var query in queries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&utf8=&format=json";
                    var response = await GetHttpClient().GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("query", out var q) &&
                            q.TryGetProperty("search", out var searchArray) &&
                            searchArray.GetArrayLength() > 0)
                        {
                            return searchArray[0].GetProperty("pageid").GetInt32().ToString();
                        }
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

    public class WikipediaMetadataProvider : BaseWikipediaProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "Wikipedia Metadata Provider";
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

        // Identify
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && (!RomExtensions.IsRomPath(searchInfo.Path) || RomExtensions.IsWindowsRom(searchInfo.Path))) return results;

            searchInfo.ProviderIds.TryGetValue("Wikipedia", out var directId);
            if (string.IsNullOrEmpty(directId))
                directId = TryExtractEmbeddedWikiId(searchInfo.Path);

            if (!string.IsNullOrEmpty(directId))
            {
                try
                {
                    var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=pageimages&pithumbsize=600&pilicense=any&pageids={directId}&format=json";
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("query", out var q) &&
                            q.TryGetProperty("pages", out var pages) &&
                            pages.TryGetProperty(directId, out var page))
                        {
                            var title = page.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                            var sr = new RemoteSearchResult
                            {
                                Name = title,
                                ProviderIds = new Dictionary<string, string> { { "Wikipedia", directId } },
                                SearchProviderName = Name
                            };

                            if (page.TryGetProperty("thumbnail", out var thumb) &&
                                thumb.TryGetProperty("source", out var src))
                            {
                                var imgUrl = src.GetString();
                                if (!string.IsNullOrWhiteSpace(imgUrl))
                                    sr.ImageUrl = imgUrl;
                            }

                            return new[] { sr };
                        }
                    }
                }
                catch { }
                return results;
            }

            var rawName = !string.IsNullOrWhiteSpace(searchInfo.Name)
                ? searchInfo.Name
                : Path.GetFileNameWithoutExtension(searchInfo.Path ?? string.Empty);
            var cleanName = RomExtensions.CleanName(rawName);
            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);

            var queries = new List<string>();
            if (!string.IsNullOrWhiteSpace(cleanName))
            {
                queries.Add(cleanName + " video game");
                queries.Add(cleanName);
            }
            if (!string.IsNullOrWhiteSpace(normalizedName) && !string.Equals(normalizedName, cleanName, StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(normalizedName + " video game");
                queries.Add(normalizedName);
            }

            foreach (var query in queries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(query)}&gsrlimit=5&prop=pageimages&pithumbsize=600&pilicense=any&format=json";
                    var response = await GetHttpClient().GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("query", out var q) &&
                            q.TryGetProperty("pages", out var pages))
                        {
                            var pageList = new List<(int index, RemoteSearchResult result)>();
                            foreach (var page in pages.EnumerateObject())
                            {
                                var pageEl = page.Value;
                                var pageId = pageEl.TryGetProperty("pageid", out var pid) ? pid.GetInt32().ToString() : string.Empty;
                                var title = pageEl.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                                var index = pageEl.TryGetProperty("index", out var idx) ? idx.GetInt32() : 999;

                                if (string.IsNullOrEmpty(pageId)) continue;

                                var sr = new RemoteSearchResult
                                {
                                    Name = title,
                                    ProviderIds = new Dictionary<string, string> { { "Wikipedia", pageId } },
                                    SearchProviderName = Name
                                };

                                if (pageEl.TryGetProperty("thumbnail", out var thumb) &&
                                    thumb.TryGetProperty("source", out var src))
                                {
                                    var imgUrl = src.GetString();
                                    if (!string.IsNullOrWhiteSpace(imgUrl))
                                        sr.ImageUrl = imgUrl;
                                }

                                pageList.Add((index, sr));
                            }

                            foreach (var item in pageList.OrderBy(p => p.index))
                            {
                                results.Add(item.result);
                            }

                            if (results.Count > 0) break;
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
            if (!string.IsNullOrEmpty(info.Path) && (!RomExtensions.IsRomPath(info.Path) || RomExtensions.IsWindowsRom(info.Path))) return result;

            info.ProviderIds.TryGetValue("Wikipedia", out var pageId);
            if (string.IsNullOrEmpty(pageId))
                pageId = TryExtractEmbeddedWikiId(info.Path);
            if (string.IsNullOrEmpty(pageId))
                pageId = (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ProviderIds["Wikipedia"];
            if (string.IsNullOrEmpty(pageId)) return result;

            try
            {
                var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=extracts&exintro&explaintext&pageids={pageId}&format=json";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var q) &&
                        q.TryGetProperty("pages", out var pages) &&
                        pages.TryGetProperty(pageId, out var page))
                    {
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
        public string Name => "Wikipedia Image Provider";
        public int Order => 3;

        public WikipediaImageProvider(IHttpClientFactory httpClientFactory, ILogger<WikipediaImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book && !RomExtensions.IsWindowsRom(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && (!RomExtensions.IsRomPath(item.Path) || RomExtensions.IsWindowsRom(item.Path))) return list;

            var rawName = !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : Path.GetFileNameWithoutExtension(item.Path ?? string.Empty);

            var pageId = item.GetProviderId("Wikipedia");
            if (string.IsNullOrEmpty(pageId))
                pageId = TryExtractEmbeddedWikiId(item.Path);
            if (string.IsNullOrEmpty(pageId))
                pageId = await ResolvePageIdAsync(rawName, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(pageId)) return list;

            try
            {
                // 1. Primary: Use pageimages with pilicense=any to fetch the lead infobox cover art
                var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=pageimages&pithumbsize=1000&pilicense=any&pageids={pageId}&format=json";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var q) &&
                        q.TryGetProperty("pages", out var pages) &&
                        pages.TryGetProperty(pageId, out var page) &&
                        page.TryGetProperty("thumbnail", out var thumbnail) &&
                        thumbnail.TryGetProperty("source", out var source))
                    {
                        var imgUrl = source.GetString();
                        if (!string.IsNullOrWhiteSpace(imgUrl))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = imgUrl });
                            return list;
                        }
                    }
                }

                // 2. Strict Fallback: Only accept images that explicitly indicate game cover art (never arbitrary jpg/png!)
                var imagesUrl = $"https://en.wikipedia.org/w/api.php?action=query&prop=images&pageids={pageId}&format=json&imlimit=20";
                var imagesResponse = await GetHttpClient().GetAsync(imagesUrl, cancellationToken).ConfigureAwait(false);

                if (imagesResponse.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await imagesResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("query", out var q) &&
                        q.TryGetProperty("pages", out var pages) &&
                        pages.TryGetProperty(pageId, out var page) &&
                        page.TryGetProperty("images", out var images))
                    {
                        foreach (var img in images.EnumerateArray())
                        {
                            if (!img.TryGetProperty("title", out var titleEl)) continue;
                            var title = titleEl.GetString() ?? string.Empty;

                            // Skip non-game files, logos, icons, hardware, etc.
                            if (title.Contains("Flag", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Commons-logo", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Wikidata", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("iPod", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Controller", StringComparison.OrdinalIgnoreCase)) continue;

                            // ONLY match if filename explicitly contains cover / box / packshot / poster
                            if (title.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("boxart", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("box_art", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("box-art", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("packshot", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("poster", StringComparison.OrdinalIgnoreCase))
                            {
                                var fileName = Uri.EscapeDataString(title.Replace("File:", "").Replace(" ", "_"));
                                var thumbUrl = $"https://en.wikipedia.org/w/index.php?title=Special:Redirect/file/{fileName}&width=600";
                                list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = thumbUrl });
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                Logger.LogDebug(ex, "[JellyEmu] Failed fetching image from Wikipedia for page {PageId}", pageId);
            }
            return list;
        }
    }

    public class WikipediaGameExternalId : IExternalId
    {
        public string ProviderName => "Wikipedia";
        public string Key => "Wikipedia";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://en.wikipedia.org/?curid={0}";
        public bool Supports(IHasProviderIds item) => item is Book || item is BookInfo;
    }

    public class WikipediaExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "Wikipedia";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (RomExtensions.IsWindowsRom(item.Path)) yield break;
            if (item is Book && item.TryGetProviderId("Wikipedia", out var wikiId))
                yield return $"https://en.wikipedia.org/?curid={wikiId}";
        }
    }
}