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
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0 (https://github.com/grimmdev/JellyEmu)");
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

            var candidates = new[] { cleanName, RomExtensions.NormalizeForSearch(cleanName) }
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var query in candidates)
            {
                try
                {
                    var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query + " video game")}&utf8=&format=json";
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

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);

            foreach (var query in new[] { cleanName, normalizedName }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(query + " video game")}&gsrlimit=5&prop=pageimages&pithumbsize=300&format=json";
                    var response = await GetHttpClient().GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("query", out var q) &&
                            q.TryGetProperty("pages", out var pages))
                        {
                            foreach (var page in pages.EnumerateObject())
                            {
                                var pageEl = page.Value;
                                var pageId = pageEl.TryGetProperty("pageid", out var pid) ? pid.GetInt32().ToString() : string.Empty;
                                var title = pageEl.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;

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

                                results.Add(sr);
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
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path)) return result;

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
        public string Name => "Wikipedia Image Provider";
        public int Order => 3;

        public WikipediaImageProvider(IHttpClientFactory httpClientFactory, ILogger<WikipediaImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var pageId = item.GetProviderId("Wikipedia") ?? await ResolvePageIdAsync(
                item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(pageId)) return list;

            try
            {
                var url = $"https://en.wikipedia.org/w/api.php?action=query&prop=pageimages&pithumbsize=1000&pageids={pageId}&format=json";
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

                            if (title.Contains("Flag", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Commons-logo", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Wikidata", StringComparison.OrdinalIgnoreCase)) continue;

                            if (title.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("box", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("art", StringComparison.OrdinalIgnoreCase) ||
                                title.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                title.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
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
            catch { }
            return list;
        }
    }

    public class WikipediaGameExternalId : IExternalId
    {
        public string ProviderName => "Wikipedia";
        public string Key => "Wikipedia";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://en.wikipedia.org/?curid={0}";
        public bool Supports(IHasProviderIds item) 
            => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);
    }

    public class WikipediaExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "Wikipedia";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item is Book && item.TryGetProviderId("Wikipedia", out var wikiId))
                yield return $"https://en.wikipedia.org/?curid={wikiId}";
        }
    }
}