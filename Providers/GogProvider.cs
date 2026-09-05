using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public abstract class BaseGogProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected BaseGogProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 {JellyEmuVersion.UserAgent}");
            return client;
        }

        protected static string? TryExtractEmbeddedGogId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(path, @"\[gog-(\d+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        protected async Task<string?> ResolveGameIdAsync(string name, CancellationToken cancellationToken)
        {
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;

            try
            {
                var url = $"https://catalog.gog.com/v1/catalog?limit=5&query={Uri.EscapeDataString(cleanName)}";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("products", out var products) &&
                        products.ValueKind == JsonValueKind.Array &&
                        products.GetArrayLength() > 0)
                    {
                        var first = products[0];
                        if (first.TryGetProperty("id", out var idProp))
                        {
                            return idProp.ValueKind == JsonValueKind.Number
                                ? idProp.GetInt32().ToString()
                                : idProp.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to resolve GOG game ID for {Name}", name);
            }
            return null;
        }
    }

    public class GogMetadataProvider : BaseGogProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "GOG Metadata Provider";
        public int Order => 1;

        private readonly PlatformResolver _platformResolver;

        public GogMetadataProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<GogMetadataProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsWindowsRom(searchInfo.Path)) return results;

            searchInfo.ProviderIds.TryGetValue("GOG", out var gogId);
            if (string.IsNullOrEmpty(gogId))
                gogId = TryExtractEmbeddedGogId(searchInfo.Path);

            if (!string.IsNullOrEmpty(gogId))
            {
                try
                {
                    var url = $"https://api.gog.com/products/{gogId}";
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        var root = document.RootElement;
                        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                        var slug = root.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;

                        var sr = new RemoteSearchResult
                        {
                            Name = title,
                            ProviderIds = new Dictionary<string, string> { { "GOG", gogId } },
                            SearchProviderName = Name
                        };
                        if (!string.IsNullOrEmpty(slug))
                        {
                            sr.ProviderIds.Add("GOGSlug", slug);
                        }
                        return new[] { sr };
                    }
                }
                catch { }
            }

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            if (string.IsNullOrEmpty(cleanName)) return results;

            try
            {
                var url = $"https://catalog.gog.com/v1/catalog?limit=20&query={Uri.EscapeDataString(cleanName)}";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("products", out var products) &&
                        products.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var prod in products.EnumerateArray())
                        {
                            var title = prod.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                            
                            var id = string.Empty;
                            if (prod.TryGetProperty("id", out var idProp))
                            {
                                id = idProp.ValueKind == JsonValueKind.Number
                                    ? idProp.GetInt32().ToString()
                                    : idProp.GetString() ?? string.Empty;
                            }

                            var slug = prod.TryGetProperty("slug", out var sProp) ? sProp.GetString() ?? string.Empty : string.Empty;

                            if (string.IsNullOrEmpty(id)) continue;

                            var sr = new RemoteSearchResult
                            {
                                Name = title,
                                ProviderIds = new Dictionary<string, string> { { "GOG", id } },
                                SearchProviderName = Name
                            };
                            if (!string.IsNullOrEmpty(slug))
                            {
                                sr.ProviderIds.Add("GOGSlug", slug);
                            }

                            var imgUrl = string.Empty;
                            if (prod.TryGetProperty("coverVertical", out var cvProp) && cvProp.ValueKind == JsonValueKind.String)
                            {
                                imgUrl = cvProp.GetString() ?? string.Empty;
                            }
                            else if (prod.TryGetProperty("image", out var imgProp) && imgProp.ValueKind == JsonValueKind.String)
                            {
                                imgUrl = imgProp.GetString() ?? string.Empty;
                            }

                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                if (imgUrl.StartsWith("//")) imgUrl = "https:" + imgUrl;
                                sr.ImageUrl = imgUrl;
                            }

                            results.Add(sr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] GOG metadata search failed for {Name}", cleanName);
            }

            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsWindowsRom(info.Path)) return result;

            info.ProviderIds.TryGetValue("GOG", out var gogId);
            if (string.IsNullOrEmpty(gogId))
                gogId = TryExtractEmbeddedGogId(info.Path);
            if (string.IsNullOrEmpty(gogId))
                gogId = (await GetSearchResults(info, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ProviderIds["GOG"];
            if (string.IsNullOrEmpty(gogId)) return result;

            try
            {
                var url = $"https://api.gog.com/products/{gogId}?expand=description";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var root = document.RootElement;
                    var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var slug = root.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;

                    string overview = string.Empty;
                    if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.Object)
                    {
                        if (descProp.TryGetProperty("full", out var fullDesc))
                        {
                            var html = fullDesc.GetString() ?? string.Empty;
                            overview = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
                            overview = System.Net.WebUtility.HtmlDecode(overview).Trim();
                        }
                    }

                    var isJ3u = string.Equals(Path.GetExtension(info.Path), ".j3u", StringComparison.OrdinalIgnoreCase);
                    var tags = new List<string> { "JellyEmu", "Game", "Windows", "Unsupported" };
                    if (isJ3u)
                    {
                        tags.Add("MultiDisc");
                    }

                    var item = new Book
                    {
                        Name = title,
                        Overview = overview,
                        Tags = tags.ToArray()
                    };

                    item.SetProviderId("GOG", gogId);
                    if (!string.IsNullOrEmpty(slug))
                    {
                        item.SetProviderId("GOGSlug", slug);
                    }

                    result.Item = item;
                    result.HasMetadata = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to fetch GOG metadata for ID {Id}", gogId);
            }

            return result;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class GogImageProvider : BaseGogProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "GOG Image Provider";
        public int Order => 1;

        public GogImageProvider(IHttpClientFactory httpClientFactory, ILogger<GogImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book && RomExtensions.IsWindowsRom(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Logo };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsWindowsRom(item.Path)) return list;

            var gogId = item.GetProviderId("GOG");
            if (string.IsNullOrEmpty(gogId) && !string.IsNullOrEmpty(item.Path))
            {
                gogId = TryExtractEmbeddedGogId(item.Path);
            }
            if (string.IsNullOrEmpty(gogId))
            {
                gogId = await ResolveGameIdAsync(
                    item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty), cancellationToken).ConfigureAwait(false);
            }
            if (string.IsNullOrEmpty(gogId)) return list;

            try
            {
                var url = $"https://api.gog.com/products/{gogId}";
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var root = document.RootElement;
                    if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
                    {
                        if (images.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.String)
                        {
                            var imgUrl = bg.GetString();
                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                if (imgUrl.StartsWith("//")) imgUrl = "https:" + imgUrl;
                                list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = imgUrl });
                            }
                        }

                        if (images.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.String)
                        {
                            var imgUrl = logo.GetString();
                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                if (imgUrl.StartsWith("//")) imgUrl = "https:" + imgUrl;
                                list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Logo, Url = imgUrl });
                            }
                        }

                        if (images.TryGetProperty("sidebarIcon", out var sidebarIcon) && sidebarIcon.ValueKind == JsonValueKind.String)
                        {
                            var imgUrl = sidebarIcon.GetString();
                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                if (imgUrl.StartsWith("//")) imgUrl = "https:" + imgUrl;
                                list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = imgUrl });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to fetch GOG images for ID {Id}", gogId);
            }

            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class GogGameExternalId : IExternalId
    {
        public string ProviderName => "GOG";
        public string Key => "GOG";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://www.gog.com/game/{0}";
        public bool Supports(IHasProviderIds item) => item is Book && RomExtensions.IsWindowsRom((item as BaseItem)?.Path);
    }

    public class GogExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "GOG";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item is Book && RomExtensions.IsWindowsRom(item.Path))
            {
                if (item.TryGetProviderId("GOGSlug", out var slug))
                    yield return $"https://www.gog.com/game/{slug}";
                else if (item.TryGetProviderId("GOG", out var gogId))
                    yield return $"https://www.gog.com/game/{gogId}";
            }
        }
    }
}
