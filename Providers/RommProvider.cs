using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using JellyEmu.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public abstract class BaseRommProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected static bool IsEnabled => Plugin.Instance?.Configuration.RommEnabled == true;
        protected static string InstanceUrl => (Plugin.Instance?.Configuration.RommInstanceUrl ?? string.Empty).TrimEnd('/');
        protected static string Username => Plugin.Instance?.Configuration.RommUsername ?? string.Empty;
        protected static string Password => Plugin.Instance?.Configuration.RommPassword ?? string.Empty;

        protected BaseRommProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", JellyEmuVersion.UserAgent);
            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
            }
            return client;
        }

        /// <summary>
        /// Resolves a ROM from the Romm instance by name, with accent-normalized fallback.
        /// </summary>
        protected async Task<JsonElement?> ResolveRomAsync(string name, CancellationToken cancellationToken)
        {
            if (!IsEnabled || string.IsNullOrEmpty(InstanceUrl)) return null;
            var cleanName = RomExtensions.CleanName(name);
            if (string.IsNullOrEmpty(cleanName)) return null;

            var candidates = new[] { cleanName, RomExtensions.NormalizeForSearch(cleanName) }
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var query in candidates)
            {
                try
                {
                    var url = $"{InstanceUrl}/api/roms?search_term={Uri.EscapeDataString(query)}&limit=1";
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                            return items[0].Clone();
                    }
                }
                catch { }
            }
            return null;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            return await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }

    public class RommMetadataProvider : BaseRommProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "Romm Metadata Provider";
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
            if (!string.IsNullOrEmpty(searchInfo.Path) && (!RomExtensions.IsRomPath(searchInfo.Path) || RomExtensions.IsWindowsRom(searchInfo.Path))) return results;

            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            if (string.IsNullOrEmpty(cleanName) || string.IsNullOrEmpty(InstanceUrl)) return results;

            var normalizedName = RomExtensions.NormalizeForSearch(cleanName);

            foreach (var query in new[] { cleanName, normalizedName }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var url = $"{InstanceUrl}/api/roms?search_term={Uri.EscapeDataString(query)}&limit=5";
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                        if (document.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                        {
                            foreach (var rom in items.EnumerateArray())
                            {
                                var romId = rom.TryGetProperty("id", out var id) ? id.GetInt32().ToString() : string.Empty;
                                var romName = rom.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                                if (string.IsNullOrEmpty(romId)) continue;

                                var sr = new RemoteSearchResult
                                {
                                    Name = romName,
                                    ProviderIds = new Dictionary<string, string> { { "Romm", romId } },
                                    SearchProviderName = Name
                                };

                                if (rom.TryGetProperty("url_cover", out var coverEl) && coverEl.ValueKind != JsonValueKind.Null)
                                {
                                    var coverUrl = coverEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(coverUrl))
                                    {
                                        if (!Uri.IsWellFormedUriString(coverUrl, UriKind.Absolute))
                                            coverUrl = $"{InstanceUrl}{(coverUrl.StartsWith("/") ? "" : "/")}{coverUrl}";
                                        sr.ImageUrl = coverUrl;
                                    }
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
            if (!IsEnabled) return result;
            if (!string.IsNullOrEmpty(info.Path) && (!RomExtensions.IsRomPath(info.Path) || RomExtensions.IsWindowsRom(info.Path))) return result;
            if (string.IsNullOrEmpty(InstanceUrl)) return result;

            info.ProviderIds.TryGetValue("Romm", out var romId);
            JsonElement? rom = null;

            if (!string.IsNullOrEmpty(romId))
            {
                try
                {
                    var response = await GetHttpClient().GetAsync($"{InstanceUrl}/api/roms/{romId}", cancellationToken).ConfigureAwait(false);
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
                    Name = rom.Value.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                    Overview = rom.Value.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() ?? string.Empty : string.Empty,
                    Tags = tags.ToArray()
                };

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

                if (rom.Value.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
                    foreach (var g in genresEl.EnumerateArray())
                    {
                        var gName = g.TryGetProperty("name", out var gn) ? gn.GetString() : g.GetString();
                        if (!string.IsNullOrWhiteSpace(gName)) item.AddGenre(gName);
                    }

                if (rom.Value.TryGetProperty("developers", out var devsEl) && devsEl.ValueKind == JsonValueKind.Array)
                    foreach (var d in devsEl.EnumerateArray())
                    {
                        var dName = d.TryGetProperty("name", out var dn) ? dn.GetString() : d.GetString();
                        if (!string.IsNullOrWhiteSpace(dName)) item.AddStudio(dName);
                    }

                if (rom.Value.TryGetProperty("publishers", out var pubsEl) && pubsEl.ValueKind == JsonValueKind.Array)
                    foreach (var p in pubsEl.EnumerateArray())
                    {
                        var pName = p.TryGetProperty("name", out var pn) ? pn.GetString() : p.GetString();
                        if (!string.IsNullOrWhiteSpace(pName)) item.AddStudio(pName);
                    }

                if (rom.Value.TryGetProperty("creators", out var creatorsEl) && creatorsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var creator in creatorsEl.EnumerateArray())
                    {
                        var creatorName = creator.TryGetProperty("name", out var cName) ? cName.GetString() : creator.GetString();
                        if (!string.IsNullOrWhiteSpace(creatorName))
                        {
                            var pInfo = new PersonInfo
                            {
                                Name = GamingPersonHelper.ToGamingPersonName(creatorName),
                                Type = PersonKind.Creator,
                                Role = "Creator"
                            };
                            if (creator.ValueKind == JsonValueKind.Object && creator.TryGetProperty("id", out var cId))
                                pInfo.ProviderIds = new Dictionary<string, string> { { "Romm", cId.ToString() } };
                            result.AddPerson(pInfo);
                        }
                    }
                }

                if (rom.Value.TryGetProperty("platform_name", out var platformEl))
                {
                    var pName = platformEl.GetString();
                    if (!string.IsNullOrWhiteSpace(pName)) item.AddStudio(pName);
                }

                if (!string.IsNullOrEmpty(resolvedId))
                    item.SetProviderId("Romm", resolvedId);

                if (rom.Value.TryGetProperty("igdb_metadata", out var igdbMeta) && igdbMeta.ValueKind == JsonValueKind.Object)
                {
                    if (igdbMeta.TryGetProperty("total_rating", out var ratingEl) &&
                        ratingEl.ValueKind == JsonValueKind.Number &&
                        igdbMeta.TryGetProperty("total_rating_count", out var countEl) &&
                        countEl.ValueKind == JsonValueKind.Number &&
                        countEl.GetInt32() > 0)
                    {
                        item.CommunityRating = (float)Math.Round(ratingEl.GetDouble() / 10.0, 1);
                    }
                }

                result.HasMetadata = true;
                result.Item = item;
            }
            catch { }
            return result;
        }
    }

    public class RommImageProvider : BaseRommProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "Romm Image Provider";
        public int Order => 4;

        public RommImageProvider(IHttpClientFactory httpClientFactory, ILogger<RommImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book && RomExtensions.IsRomPath(item.Path) && !RomExtensions.IsWindowsRom(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!IsEnabled || string.IsNullOrEmpty(InstanceUrl)) return list;
            if (!string.IsNullOrEmpty(item.Path) && (!RomExtensions.IsRomPath(item.Path) || RomExtensions.IsWindowsRom(item.Path))) return list;

            var romId = item.GetProviderId("Romm");
            JsonElement? rom = null;

            if (!string.IsNullOrEmpty(romId))
            {
                try
                {
                    var response = await GetHttpClient().GetAsync($"{InstanceUrl}/api/roms/{romId}", cancellationToken).ConfigureAwait(false);
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
                if (rom.Value.TryGetProperty("url_cover", out var coverEl) && coverEl.ValueKind != JsonValueKind.Null)
                {
                    var coverUrl = coverEl.GetString();
                    if (!string.IsNullOrWhiteSpace(coverUrl))
                    {
                        if (!Uri.IsWellFormedUriString(coverUrl, UriKind.Absolute))
                            coverUrl = $"{InstanceUrl}{(coverUrl.StartsWith("/") ? "" : "/")}{coverUrl}";
                        list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = coverUrl });
                    }
                }

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