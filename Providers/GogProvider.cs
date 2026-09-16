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
            client.DefaultRequestHeaders.Add("User-Agent", JellyEmuVersion.BrowserUserAgent);
            return client;
        }

        public static string? TryExtractEmbeddedGogId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(path, @"\[gog-(\d+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string? NormalizeImageUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var trimmed = url.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                trimmed = "https:" + trimmed;
            }
            return trimmed;
        }

        public static void AddImage(List<RemoteImageInfo> list, string providerName, ImageType type, string? url)
        {
            var normalized = NormalizeImageUrl(url);
            if (string.IsNullOrEmpty(normalized)) return;
            if (!list.Any(i => i.Type == type && string.Equals(i.Url, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new RemoteImageInfo { ProviderName = providerName, Type = type, Url = normalized });
            }
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
                // Try v2/games/{id} first
                try
                {
                    var v2Url = $"https://api.gog.com/v2/games/{gogId}";
                    var response = await GetHttpClient().GetAsync(v2Url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        var root = document.RootElement;
                        var title = string.Empty;
                        if (root.TryGetProperty("_embedded", out var embedded) &&
                            embedded.TryGetProperty("product", out var prod) &&
                            prod.TryGetProperty("title", out var titleProp))
                        {
                            title = titleProp.GetString() ?? string.Empty;
                        }

                        if (!string.IsNullOrEmpty(title))
                        {
                            var sr = new RemoteSearchResult
                            {
                                Name = title,
                                ProviderIds = new Dictionary<string, string> { { "GOG", gogId } },
                                SearchProviderName = Name
                            };

                            if (root.TryGetProperty("_links", out var links))
                            {
                                if (links.TryGetProperty("store", out var storeProp) && storeProp.TryGetProperty("href", out var storeHref))
                                {
                                    var storeUrl = storeHref.GetString();
                                    if (!string.IsNullOrEmpty(storeUrl))
                                    {
                                        var match = System.Text.RegularExpressions.Regex.Match(storeUrl, @"/game/([^/?]+)");
                                        if (match.Success)
                                        {
                                            sr.ProviderIds.Add("GOGSlug", match.Groups[1].Value);
                                        }
                                    }
                                }

                                if (links.TryGetProperty("boxArtImage", out var boxArt) && boxArt.TryGetProperty("href", out var boxHref))
                                {
                                    sr.ImageUrl = NormalizeImageUrl(boxHref.GetString());
                                }
                            }

                            if (embedded.TryGetProperty("product", out var productEl))
                            {
                                if (productEl.TryGetProperty("globalReleaseDate", out var relDateProp) &&
                                    DateTime.TryParse(relDateProp.GetString(), out var relDate))
                                {
                                    sr.PremiereDate = relDate;
                                    sr.ProductionYear = relDate.Year;
                                }
                                else if (productEl.TryGetProperty("gogReleaseDate", out var gogDateProp) &&
                                         DateTime.TryParse(gogDateProp.GetString(), out var gogDate))
                                {
                                    sr.PremiereDate = gogDate;
                                    sr.ProductionYear = gogDate.Year;
                                }
                            }

                            return new[] { sr };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[JellyEmu] GOG v2 search failed for ID {Id}", gogId);
                }

                // Fallback to legacy products endpoint
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
                                sr.ImageUrl = NormalizeImageUrl(imgUrl);
                            }

                            if (prod.TryGetProperty("releaseDate", out var rdProp) &&
                                rdProp.ValueKind == JsonValueKind.String &&
                                DateTime.TryParse(rdProp.GetString()?.Replace(".", "-"), out var relDate))
                            {
                                sr.PremiereDate = relDate;
                                sr.ProductionYear = relDate.Year;
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

            // Try v2/games/{id}
            try
            {
                var v2Url = $"https://api.gog.com/v2/games/{gogId}";
                var response = await GetHttpClient().GetAsync(v2Url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    var root = document.RootElement;
                    
                    var title = string.Empty;
                    var globalReleaseDate = string.Empty;
                    var gogReleaseDate = string.Empty;
                    var developers = new List<string>();
                    var publishers = new List<string>();
                    var genres = new List<string>();

                    if (root.TryGetProperty("_embedded", out var embedded))
                    {
                        if (embedded.TryGetProperty("product", out var prod))
                        {
                            if (prod.TryGetProperty("title", out var titleProp))
                                title = titleProp.GetString() ?? string.Empty;
                            if (prod.TryGetProperty("globalReleaseDate", out var grProp))
                                globalReleaseDate = grProp.GetString() ?? string.Empty;
                            if (prod.TryGetProperty("gogReleaseDate", out var gorProp))
                                gogReleaseDate = gorProp.GetString() ?? string.Empty;
                        }

                        if (embedded.TryGetProperty("developers", out var devs) && devs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var dev in devs.EnumerateArray())
                            {
                                if (dev.TryGetProperty("name", out var dn) && dn.ValueKind == JsonValueKind.String)
                                {
                                    var name = dn.GetString();
                                    if (!string.IsNullOrWhiteSpace(name)) developers.Add(name);
                                }
                            }
                        }

                        if (embedded.TryGetProperty("publishers", out var pubs) && pubs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var pub in pubs.EnumerateArray())
                            {
                                if (pub.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String)
                                {
                                    var name = pn.GetString();
                                    if (!string.IsNullOrWhiteSpace(name)) publishers.Add(name);
                                }
                            }
                        }

                        if (embedded.TryGetProperty("tags", out var tagList) && tagList.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var t in tagList.EnumerateArray())
                            {
                                if (t.TryGetProperty("name", out var tn) && tn.ValueKind == JsonValueKind.String)
                                {
                                    var name = tn.GetString();
                                    if (!string.IsNullOrWhiteSpace(name)) genres.Add(name);
                                }
                            }
                        }

                        if (embedded.TryGetProperty("properties", out var propList) && propList.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var p in propList.EnumerateArray())
                            {
                                if (p.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String)
                                {
                                    var name = pn.GetString();
                                    if (!string.IsNullOrWhiteSpace(name)) genres.Add(name);
                                }
                            }
                        }
                    }

                    var slug = string.Empty;
                    if (root.TryGetProperty("_links", out var links) && links.TryGetProperty("store", out var store) && store.TryGetProperty("href", out var storeHref))
                    {
                        var storeUrl = storeHref.GetString();
                        if (!string.IsNullOrEmpty(storeUrl))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(storeUrl, @"/game/([^/?]+)");
                            if (match.Success)
                            {
                                slug = match.Groups[1].Value;
                            }
                        }
                    }

                    string overview = string.Empty;
                    if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                    {
                        var html = descProp.GetString() ?? string.Empty;
                        overview = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
                        overview = System.Net.WebUtility.HtmlDecode(overview).Trim();
                    }
                    else if (root.TryGetProperty("overview", out var overProp) && overProp.ValueKind == JsonValueKind.String)
                    {
                        overview = overProp.GetString()?.Trim() ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(title))
                    {
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

                        if (DateTime.TryParse(globalReleaseDate, out var relDate))
                        {
                            item.PremiereDate = relDate;
                            item.ProductionYear = relDate.Year;
                        }
                        else if (DateTime.TryParse(gogReleaseDate, out var gogRelDate))
                        {
                            item.PremiereDate = gogRelDate;
                            item.ProductionYear = gogRelDate.Year;
                        }

                        foreach (var dev in developers.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            item.AddStudio(dev);
                        }
                        foreach (var pub in publishers.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            item.AddStudio(pub);
                        }

                        if (genres.Count > 0)
                        {
                            item.Genres = genres.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        }

                        result.Item = item;
                        result.HasMetadata = true;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Failed to fetch GOG v2 metadata for ID {Id}", gogId);
            }

            // Fallback to legacy products endpoint
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

                    if (root.TryGetProperty("release_date", out var rdProp) &&
                        rdProp.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(rdProp.GetString(), out var relDate))
                    {
                        item.PremiereDate = relDate;
                        item.ProductionYear = relDate.Year;
                    }

                    result.Item = item;
                    result.HasMetadata = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to fetch legacy GOG metadata for ID {Id}", gogId);
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

            // Primary source: v2/games/{id}
            try
            {
                var v2Url = $"https://api.gog.com/v2/games/{gogId}";
                var response = await GetHttpClient().GetAsync(v2Url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    ParseV2GameImages(document.RootElement, Name, list);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Failed to fetch GOG v2 images for ID {Id}", gogId);
            }

            // Fallback for box art: catalog.gog.com/v1/catalog
            if (!list.Any(x => x.Type == ImageType.Primary))
            {
                try
                {
                    var catalogUrl = $"https://catalog.gog.com/v1/catalog?ids={gogId}";
                    var response = await GetHttpClient().GetAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("products", out var products) &&
                            products.ValueKind == JsonValueKind.Array &&
                            products.GetArrayLength() > 0)
                        {
                            ParseCatalogImages(products[0], Name, list);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[JellyEmu] Failed to fetch GOG catalog images for ID {Id}", gogId);
                }
            }

            // Fallback for backdrop or logo: legacy products endpoint
            if (!list.Any(x => x.Type == ImageType.Backdrop) || !list.Any(x => x.Type == ImageType.Logo))
            {
                try
                {
                    var prodUrl = $"https://api.gog.com/products/{gogId}";
                    var response = await GetHttpClient().GetAsync(prodUrl, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        ParseLegacyProductImages(document.RootElement, Name, list);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[JellyEmu] Failed to fetch legacy GOG images for ID {Id}", gogId);
                }
            }

            return list;
        }

        public static void ParseV2GameImages(JsonElement root, string providerName, List<RemoteImageInfo> list)
        {
            if (root.TryGetProperty("_links", out var links) && links.ValueKind == JsonValueKind.Object)
            {
                if (links.TryGetProperty("boxArtImage", out var boxArt) && boxArt.TryGetProperty("href", out var boxHref))
                {
                    AddImage(list, providerName, ImageType.Primary, boxHref.GetString());
                }

                if (links.TryGetProperty("galaxyBackgroundImage", out var galBg) && galBg.TryGetProperty("href", out var galBgHref))
                {
                    AddImage(list, providerName, ImageType.Backdrop, galBgHref.GetString());
                }

                if (links.TryGetProperty("backgroundImage", out var bg) && bg.TryGetProperty("href", out var bgHref))
                {
                    AddImage(list, providerName, ImageType.Backdrop, bgHref.GetString());
                }

                if (links.TryGetProperty("logo", out var logo) && logo.TryGetProperty("href", out var logoHref))
                {
                    AddImage(list, providerName, ImageType.Logo, logoHref.GetString());
                }

                if (!list.Any(x => x.Type == ImageType.Primary) && links.TryGetProperty("icon", out var icon) && icon.TryGetProperty("href", out var iconHref))
                {
                    AddImage(list, providerName, ImageType.Primary, iconHref.GetString());
                }
            }

            if (root.TryGetProperty("_embedded", out var embedded) &&
                embedded.TryGetProperty("screenshots", out var screenshots) &&
                screenshots.ValueKind == JsonValueKind.Array)
            {
                foreach (var shot in screenshots.EnumerateArray())
                {
                    if (shot.TryGetProperty("_links", out var shotLinks) &&
                        shotLinks.TryGetProperty("self", out var selfProp) &&
                        selfProp.TryGetProperty("href", out var selfHref) &&
                        selfHref.ValueKind == JsonValueKind.String)
                    {
                        var rawHref = selfHref.GetString();
                        if (!string.IsNullOrEmpty(rawHref))
                        {
                            var formatted = rawHref.Replace("_{formatter}", "_1600").Replace("{formatter}", "1600");
                            AddImage(list, providerName, ImageType.Backdrop, formatted);
                        }
                    }
                }
            }
        }

        public static void ParseCatalogImages(JsonElement product, string providerName, List<RemoteImageInfo> list)
        {
            if (product.TryGetProperty("coverVertical", out var cv) && cv.ValueKind == JsonValueKind.String)
            {
                AddImage(list, providerName, ImageType.Primary, cv.GetString());
            }
            else if (product.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.String)
            {
                AddImage(list, providerName, ImageType.Primary, img.GetString());
            }

            if (product.TryGetProperty("galaxyBackgroundImage", out var galBg) && galBg.ValueKind == JsonValueKind.String)
            {
                AddImage(list, providerName, ImageType.Backdrop, galBg.GetString());
            }

            if (product.TryGetProperty("coverHorizontal", out var ch) && ch.ValueKind == JsonValueKind.String)
            {
                AddImage(list, providerName, ImageType.Backdrop, ch.GetString());
            }

            if (product.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.String)
            {
                AddImage(list, providerName, ImageType.Logo, logo.GetString());
            }

            if (product.TryGetProperty("screenshots", out var screenshots) && screenshots.ValueKind == JsonValueKind.Array)
            {
                foreach (var shot in screenshots.EnumerateArray())
                {
                    if (shot.ValueKind == JsonValueKind.String)
                    {
                        var rawHref = shot.GetString();
                        if (!string.IsNullOrEmpty(rawHref))
                        {
                            var formatted = rawHref.Replace("_{formatter}", "_1600").Replace("{formatter}", "1600");
                            AddImage(list, providerName, ImageType.Backdrop, formatted);
                        }
                    }
                }
            }
        }

        public static void ParseLegacyProductImages(JsonElement root, string providerName, List<RemoteImageInfo> list)
        {
            if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
            {
                if (images.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.String)
                {
                    AddImage(list, providerName, ImageType.Backdrop, bg.GetString());
                }

                if (images.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.String)
                {
                    AddImage(list, providerName, ImageType.Logo, logo.GetString());
                }
            }
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
