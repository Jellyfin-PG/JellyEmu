using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public class HasheousProvider : IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HasheousProvider> _logger;
        private readonly PlatformResolver _platformResolver;

        public HasheousProvider(
            IHttpClientFactory httpClientFactory, 
            ILogger<HasheousProvider> logger,
            PlatformResolver platformResolver)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _platformResolver = platformResolver;
        }

        private static string? TryExtractEmbeddedHasheousId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(path, @"\[hash-(\d+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public string Name => "Hasheous";
        public int Order => 1;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsRomPath(searchInfo.Path)) return results;

            searchInfo.ProviderIds.TryGetValue("Hasheous", out var hasheousId);
            if (string.IsNullOrEmpty(hasheousId))
                hasheousId = TryExtractEmbeddedHasheousId(searchInfo.Path);

            searchInfo.ProviderIds.TryGetValue("MD5", out var md5);

            if (!string.IsNullOrEmpty(hasheousId) || !string.IsNullOrEmpty(md5))
            {
                try
                {
                    var url = !string.IsNullOrEmpty(hasheousId) 
                        ? $"https://hasheous.org/api/v1/Lookup/ById/{hasheousId}"
                        : $"https://hasheous.org/api/v1/Lookup/ByHash/md5/{md5}";

                    var client = _httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");

                    var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = await JsonDocument.ParseAsync(
                            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        var root = doc.RootElement;

                        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32().ToString() : null;
                        var actualMd5 = md5;

                        if (string.IsNullOrEmpty(name) && root.TryGetProperty("signatures", out var signatures) && signatures.GetArrayLength() > 0)
                        {
                            var match = signatures[0];
                            name = match.TryGetProperty("name", out var mn) ? mn.GetString() : null;
                            id = match.TryGetProperty("id", out var mi) ? mi.GetInt64().ToString() : null;
                            actualMd5 = match.TryGetProperty("signature", out var sig) && sig.TryGetProperty("rom", out var rom) && rom.TryGetProperty("md5", out var rmd5) ? rmd5.GetString() : md5;
                        }

                        if (!string.IsNullOrEmpty(name))
                        {
                            var sr = new RemoteSearchResult
                            {
                                Name = name,
                                SearchProviderName = Name
                            };
                            if (!string.IsNullOrEmpty(id)) sr.SetProviderId("Hasheous", id);
                            if (!string.IsNullOrEmpty(actualMd5)) sr.SetProviderId("MD5", actualMd5);
                            results.Add(sr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyEmu] Error searching Hasheous for ID {Id} or MD5 {MD5}", hasheousId, md5);
                }
            }

            return results;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            
            info.ProviderIds.TryGetValue("Hasheous", out var hasheousId);
            if (string.IsNullOrEmpty(hasheousId))
                hasheousId = TryExtractEmbeddedHasheousId(info.Path);

            info.ProviderIds.TryGetValue("MD5", out var md5);

            if (string.IsNullOrEmpty(hasheousId) && string.IsNullOrEmpty(md5))
                return result;

            try
            {
                var url = !string.IsNullOrEmpty(hasheousId) 
                    ? $"https://hasheous.org/api/v1/Lookup/ById/{hasheousId}"
                    : $"https://hasheous.org/api/v1/Lookup/ByHash/md5/{md5}";
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
                
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(
                        await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), 
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    var root = doc.RootElement;

                    var gameId = root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : null;
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var matchRoot = root;

                    if (string.IsNullOrEmpty(name) && root.TryGetProperty("signatures", out var signatures) && signatures.GetArrayLength() > 0)
                    {
                        matchRoot = signatures[0];
                        gameId = matchRoot.TryGetProperty("id", out var mIdProp) ? mIdProp.GetInt64().ToString() : null;
                        name = matchRoot.TryGetProperty("name", out var mNameProp) ? mNameProp.GetString() : null;
                    }

                    if (string.IsNullOrEmpty(name)) return result;

                    var consoleTag = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path));
                    var regionTag = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                    var discTag = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));

                    var tags = new List<string> { "JellyEmu", "Game", consoleTag };
                    if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);
                    if (!string.IsNullOrEmpty(discTag)) tags.Add(discTag);

                    result.Item = new Book
                    {
                        Name = name,
                        Tags = tags.ToArray()
                    };

                    if (!string.IsNullOrEmpty(gameId))
                        result.Item.SetProviderId("Hasheous", gameId);
                    
                    if (!string.IsNullOrEmpty(md5))
                        result.Item.SetProviderId("MD5", md5);

                    if (matchRoot.TryGetProperty("signature", out var sig) && sig.TryGetProperty("game", out var gameSig))
                    {
                        if (gameSig.TryGetProperty("year", out var yearProp))
                        {
                            var yearStr = yearProp.GetString();
                            if (!string.IsNullOrEmpty(yearStr) && int.TryParse(yearStr, out var year))
                            {
                                result.Item.ProductionYear = year;
                                result.Item.PremiereDate = new DateTime(year, 1, 1);
                            }
                        }
                        if (gameSig.TryGetProperty("publisher", out var pubProp))
                        {
                            var pub = pubProp.GetString();
                            if (!string.IsNullOrEmpty(pub)) result.Item.AddStudio(pub);
                        }
                    }

                    if (matchRoot.TryGetProperty("attributes", out var attrs))
                    {
                        foreach (var attr in attrs.EnumerateArray())
                        {
                            if (attr.TryGetProperty("attributeName", out var attrName) && attrName.GetString() == "AIDescription")
                            {
                                result.Item.Overview = attr.TryGetProperty("value", out var attrVal) ? attrVal.GetString() : result.Item.Overview;
                            }
                        }
                    }

                    if (matchRoot.TryGetProperty("metadata", out var metadataArray))
                    {
                        foreach (var meta in metadataArray.EnumerateArray())
                        {
                            var source = meta.TryGetProperty("source", out var s) ? s.GetString() : null;
                            var val = meta.TryGetProperty("id", out var v) ? v.GetString() : null;

                            if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(val))
                            {
                                if (source.Equals("IGDB", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("IGDB", val);
                                else if (source.Equals("TheGamesDb", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("TheGamesDb", val);
                                else if (source.Equals("RetroAchievements", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("RetroAchievements", val);
                                else if (source.Equals("GiantBomb", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("GiantBomb", val);
                                else if (source.Equals("SteamGridDb", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("SteamGridDb", val);
                                else if (source.Equals("ScreenScraper", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("ScreenScraper", val);
                                else if (source.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("Steam", val);
                                else if (source.Equals("GOG", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("GOG", val);
                                else if (source.Equals("EpicGameStore", StringComparison.OrdinalIgnoreCase))
                                    result.Item.SetProviderId("EpicGameStore", val);
                            }
                        }
                    }

                    result.HasMetadata = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Error fetching metadata from Hasheous for MD5 {MD5}", md5);
            }

            return result;
        }
    }

    public class HasheousImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HasheousImageProvider> _logger;

        public HasheousImageProvider(IHttpClientFactory httpClientFactory, ILogger<HasheousImageProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string Name => "Hasheous Image Provider";
        public int Order => 1;

        public bool Supports(BaseItem item) => item is Book && RomExtensions.IsRomPath(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Logo };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && !RomExtensions.IsRomPath(item.Path)) return list;

            var id = item.GetProviderId("Hasheous");
            var md5 = item.GetProviderId("MD5");
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(md5)) return list;

            try
            {
                var url = !string.IsNullOrEmpty(id) 
                    ? $"https://hasheous.org/api/v1/Lookup/ById/{id}"
                    : $"https://hasheous.org/api/v1/Lookup/ByHash/md5/{md5}";

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
                
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
                    var root = doc.RootElement;
                    
                    var matchRoot = root;
                    if (!root.TryGetProperty("attributes", out _) && root.TryGetProperty("signatures", out var signatures) && signatures.GetArrayLength() > 0)
                        matchRoot = signatures[0];

                    if (matchRoot.TryGetProperty("attributes", out var attrs))
                    {
                        foreach (var attr in attrs.EnumerateArray())
                        {
                            if (attr.TryGetProperty("attributeType", out var typeProp) && typeProp.GetString() == "ImageId")
                            {
                                var attrName = attr.TryGetProperty("attributeName", out var nameProp) ? nameProp.GetString() : null;
                                var imageId = attr.TryGetProperty("value", out var valProp) ? valProp.GetString() : null;
                                if (string.IsNullOrEmpty(imageId)) continue;

                                var imgUrl = $"https://hasheous.org/api/v1/images/{imageId}";
                                if (attrName == "BoxArt" || attrName == "Front")
                                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = imgUrl });
                                else if (attrName == "Logo")
                                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Logo, Url = imgUrl });
                                else if (attrName != null && attrName.StartsWith("Screenshot"))
                                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = imgUrl });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Error fetching images from Hasheous for item {Name}", item.Name);
            }
            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        }
    }

    public class HasheousGameExternalId : IExternalId
    {
        public string ProviderName => "Hasheous";
        public string Key => "Hasheous";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://hasheous.org/index.html?page=dataobjectdetail&type=game&id={0}";
        public bool Supports(IHasProviderIds item) => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);
    }

    public class HasheousExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "Hasheous";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.TryGetProviderId("Hasheous", out var id))
                yield return $"https://hasheous.org/index.html?page=dataobjectdetail&type=game&id={id}";
        }
    }
}
