using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using JellyEmu.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public abstract class BaseScreenScraperProvider
    {
        protected readonly IHttpClientFactory HttpClientFactory;
        protected readonly ILogger Logger;

        protected const string BaseUrl = "https://api.screenscraper.fr/api2/";

        protected static string DevId => Plugin.Instance?.Configuration.ScreenScraperDevId ?? string.Empty;
        protected static string DevPassword => Plugin.Instance?.Configuration.ScreenScraperDevPassword ?? string.Empty;
        protected static string SoftName => !string.IsNullOrEmpty(Plugin.Instance?.Configuration.ScreenScraperSoftName)
            ? Plugin.Instance.Configuration.ScreenScraperSoftName
            : "JellyEmu";

        protected static string User => Plugin.Instance?.Configuration.ScreenScraperUser ?? string.Empty;
        protected static string Password => Plugin.Instance?.Configuration.ScreenScraperPassword ?? string.Empty;

        protected static string RegionPreference => !string.IsNullOrEmpty(Plugin.Instance?.Configuration.ScreenScraperRegionPreference)
            ? Plugin.Instance.Configuration.ScreenScraperRegionPreference
            : "auto";

        protected static string LanguagePreference => !string.IsNullOrEmpty(Plugin.Instance?.Configuration.ScreenScraperLanguagePreference)
            ? Plugin.Instance.Configuration.ScreenScraperLanguagePreference
            : "en";

        public static bool IsConfigured => !string.IsNullOrEmpty(DevId) && !string.IsNullOrEmpty(DevPassword);

        protected BaseScreenScraperProvider(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            HttpClientFactory = httpClientFactory;
            Logger = logger;
        }

        protected HttpClient GetHttpClient()
        {
            var client = HttpClientFactory.CreateClient();
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"{SoftName}/1.0");
            }
            return client;
        }

        public static string BuildApiUrl(string endpoint, IDictionary<string, string?> parameters)
        {
            var query = new List<string>
            {
                $"devid={Uri.EscapeDataString(DevId)}",
                $"devpassword={Uri.EscapeDataString(DevPassword)}",
                $"softname={Uri.EscapeDataString(SoftName)}",
                "output=json"
            };

            if (!string.IsNullOrEmpty(User))
            {
                query.Add($"ssid={Uri.EscapeDataString(User)}");
            }
            if (!string.IsNullOrEmpty(Password))
            {
                query.Add($"sspassword={Uri.EscapeDataString(Password)}");
            }

            foreach (var kvp in parameters)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    query.Add($"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}");
                }
            }

            return $"{BaseUrl}{endpoint}?{string.Join("&", query)}";
        }

        public static string? TryExtractEmbeddedScreenScraperId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = Regex.Match(path, @"\[(?:ss|screenscraper)-(\d+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string MapRegionToCode(string? region)
        {
            if (string.IsNullOrWhiteSpace(region)) return "us";
            var r = region.Trim().ToLowerInvariant();

            return r switch
            {
                "japan" or "jpn" or "jp" => "jp",
                "europe" or "eu" or "eur" => "eu",
                "france" or "fra" or "fr" => "fr",
                "germany" or "ger" or "de" or "deu" => "de",
                "spain" or "spa" or "es" => "es",
                "italy" or "ita" or "it" => "it",
                "brazil" or "bra" or "br" => "br",
                "korea" or "kor" or "kr" => "kr",
                "china" or "chn" or "cn" => "cn",
                "australia" or "aus" or "au" => "au",
                "world" or "wor" => "wor",
                _ => "us"
            };
        }

        public static string ResolveEffectiveRegion(string? romPath, string preference)
        {
            if (!string.Equals(preference, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return MapRegionToCode(preference);
            }

            var detectedRegions = PlatformResolver.ResolveRegions(romPath);
            var first = detectedRegions.FirstOrDefault();
            return !string.IsNullOrEmpty(first) ? MapRegionToCode(first) : "us";
        }

        public static string ExtractLocalizedTitle(JsonElement jeu, string targetRegion)
        {
            var titlesByRegion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (jeu.TryGetProperty("noms", out var nomsEl) && nomsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var nom in nomsEl.EnumerateArray())
                {
                    var reg = nom.TryGetProperty("region", out var r) ? r.GetString() ?? "wor" : "wor";
                    var text = nom.TryGetProperty("text", out var t) ? t.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text) && !titlesByRegion.ContainsKey(reg))
                    {
                        titlesByRegion[reg] = text;
                    }
                }
            }

            if (titlesByRegion.TryGetValue(targetRegion, out var matchTitle))
            {
                return matchTitle;
            }

            string[] fallbackOrder = targetRegion switch
            {
                "jp" => new[] { "jp", "us", "wor", "eu" },
                "eu" => new[] { "eu", "wor", "us", "jp" },
                _ => new[] { "us", "wor", "eu", "jp" }
            };

            foreach (var reg in fallbackOrder)
            {
                if (titlesByRegion.TryGetValue(reg, out var fbTitle))
                {
                    return fbTitle;
                }
            }

            if (titlesByRegion.Count > 0)
            {
                return titlesByRegion.Values.First();
            }

            if (jeu.TryGetProperty("nom", out var directNom) && directNom.ValueKind == JsonValueKind.String)
            {
                return directNom.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        public static string ExtractSynopsis(JsonElement jeu, string preferredLang)
        {
            var synopses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (jeu.TryGetProperty("synopsis", out var synEl) && synEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var syn in synEl.EnumerateArray())
                {
                    var lang = syn.TryGetProperty("langue", out var l) ? l.GetString() ?? "en" : "en";
                    var text = syn.TryGetProperty("text", out var t) ? t.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text) && !synopses.ContainsKey(lang))
                    {
                        synopses[lang] = text;
                    }
                }
            }

            if (synopses.TryGetValue(preferredLang, out var matchSyn))
            {
                return matchSyn;
            }

            if (synopses.TryGetValue("en", out var enSyn))
            {
                return enSyn;
            }

            return synopses.Count > 0 ? synopses.Values.First() : string.Empty;
        }

        public static DateTime? ExtractReleaseDate(JsonElement jeu, string targetRegion)
        {
            var datesByRegion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (jeu.TryGetProperty("dates", out var datesEl) && datesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in datesEl.EnumerateArray())
                {
                    var reg = d.TryGetProperty("region", out var r) ? r.GetString() ?? "wor" : "wor";
                    var text = d.TryGetProperty("text", out var t) ? t.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text) && !datesByRegion.ContainsKey(reg))
                    {
                        datesByRegion[reg] = text;
                    }
                }
            }

            string[] searchRegions = { targetRegion, "us", "wor", "eu", "jp" };
            foreach (var reg in searchRegions)
            {
                if (datesByRegion.TryGetValue(reg, out var rawDate) && DateTime.TryParse(rawDate, out var dt))
                {
                    return dt;
                }
            }

            foreach (var val in datesByRegion.Values)
            {
                if (DateTime.TryParse(val, out var dt))
                {
                    return dt;
                }
            }

            return null;
        }

        public static string? ExtractMediaUrl(JsonElement jeu, string mediaType, string targetRegion)
        {
            if (!jeu.TryGetProperty("medias", out var mediasEl) || mediasEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var candidates = new List<(string Region, string Url)>();

            foreach (var media in mediasEl.EnumerateArray())
            {
                var type = media.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (!string.Equals(type, mediaType, StringComparison.OrdinalIgnoreCase) &&
                    !(type != null && type.StartsWith(mediaType, StringComparison.OrdinalIgnoreCase))) continue;

                var reg = media.TryGetProperty("region", out var r) ? r.GetString() ?? "wor" : "wor";
                var url = media.TryGetProperty("url", out var u) ? u.GetString() : null;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    candidates.Add((reg, url));
                }
            }

            if (candidates.Count == 0) return null;

            var directMatch = candidates.FirstOrDefault(c => string.Equals(c.Region, targetRegion, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(directMatch.Url)) return directMatch.Url;

            string[] fallbackOrder = targetRegion switch
            {
                "jp" => new[] { "jp", "us", "wor", "eu" },
                "eu" => new[] { "eu", "wor", "us", "jp" },
                _ => new[] { "us", "wor", "eu", "jp" }
            };

            foreach (var reg in fallbackOrder)
            {
                var match = candidates.FirstOrDefault(c => string.Equals(c.Region, reg, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Url)) return match.Url;
            }

            return candidates.First().Url;
        }

        public static (string? Md5, string? Crc, long FileSize) ComputeFastChecksums(string path)
        {
            if (!File.Exists(path)) return (null, null, 0);

            try
            {
                var fi = new FileInfo(path);
                long size = fi.Length;

                // For files < 100MB, compute MD5 and CRC32
                if (size < 100 * 1024 * 1024)
                {
                    using var stream = File.OpenRead(path);
                    using var md5 = MD5.Create();
                    var hashBytes = md5.ComputeHash(stream);
                    var md5Hex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    stream.Position = 0;
                    uint crc = ComputeCrc32(stream);
                    var crcHex = crc.ToString("x8");

                    return (md5Hex, crcHex, size);
                }

                return (null, null, size);
            }
            catch
            {
                return (null, null, 0);
            }
        }

        private static uint ComputeCrc32(Stream stream)
        {
            uint crc = 0xFFFFFFFF;
            byte[] buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    crc = Crc32Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                }
            }

            return ~crc;
        }

        private static readonly uint[] Crc32Table = GenerateCrc32Table();

        private static uint[] GenerateCrc32Table()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ 0xEDB88320;
                    else
                        entry >>= 1;
                }
                table[i] = entry;
            }
            return table;
        }
    }

    public class ScreenScraperProvider : BaseScreenScraperProvider, IRemoteMetadataProvider<Book, BookInfo>, IHasOrder
    {
        public string Name => "ScreenScraper Metadata Provider";
        public int Order => 1;

        private readonly PlatformResolver _platformResolver;

        public ScreenScraperProvider(
            IHttpClientFactory httpClientFactory,
            ILogger<ScreenScraperProvider> logger,
            PlatformResolver platformResolver)
            : base(httpClientFactory, logger)
        {
            _platformResolver = platformResolver;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            if (!IsConfigured) return results;
            if (!string.IsNullOrEmpty(searchInfo.Path) && (!RomExtensions.IsRomPath(searchInfo.Path) || RomExtensions.IsWindowsRom(searchInfo.Path))) return results;

            searchInfo.ProviderIds.TryGetValue("ScreenScraper", out var directId);
            if (string.IsNullOrEmpty(directId))
                directId = TryExtractEmbeddedScreenScraperId(searchInfo.Path);

            var platform = _platformResolver.Resolve(RomExtensions.EffectiveRomPath(searchInfo.Path));
            var systemId = ScreenScraperSystemMap.GetSystemId(platform);
            var targetRegion = ResolveEffectiveRegion(searchInfo.Path, RegionPreference);

            // 1. Direct Game ID lookup
            if (!string.IsNullOrEmpty(directId))
            {
                try
                {
                    var url = BuildApiUrl("jeuInfos.php", new Dictionary<string, string?> { { "gameid", directId } });
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        var sr = ParseJeuToSearchResult(doc.RootElement, directId, targetRegion);
                        if (sr != null) return new[] { sr };
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[JellyEmu] ScreenScraper lookup failed for Game ID {Id}", directId);
                }
                return results;
            }

            // 2. ROM file checksum / filename lookup via jeuInfos.php
            if (!string.IsNullOrEmpty(searchInfo.Path) && File.Exists(searchInfo.Path) && systemId.HasValue)
            {
                try
                {
                    var (md5, crc, size) = ComputeFastChecksums(searchInfo.Path);
                    var filename = Path.GetFileName(searchInfo.Path);

                    var queryParams = new Dictionary<string, string?>
                    {
                        { "systemeid", systemId.Value.ToString() },
                        { "romnom", filename },
                        { "romtaille", size > 0 ? size.ToString() : null },
                        { "md5", md5 },
                        { "crc", crc }
                    };

                    var url = BuildApiUrl("jeuInfos.php", queryParams);
                    var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        var sr = ParseJeuToSearchResult(doc.RootElement, null, targetRegion);
                        if (sr != null)
                        {
                            results.Add(sr);
                            return results;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "[JellyEmu] ScreenScraper ROM hash lookup failed for {Path}", searchInfo.Path);
                }
            }

            // 3. Name search fallback via jeuRecherche.php
            var cleanName = RomExtensions.CleanName(searchInfo.Name);
            if (string.IsNullOrWhiteSpace(cleanName)) return results;

            try
            {
                var queryParams = new Dictionary<string, string?>
                {
                    { "recherche", cleanName },
                    { "systemeid", systemId?.ToString() }
                };

                var url = BuildApiUrl("jeuRecherche.php", queryParams);
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var searchResults = ParseRechercheResults(doc.RootElement, targetRegion);
                    results.AddRange(searchResults);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] ScreenScraper name search failed for {Name}", cleanName);
            }

            return results;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book> { HasMetadata = false };
            if (!IsConfigured) return result;
            if (!string.IsNullOrEmpty(info.Path) && (!RomExtensions.IsRomPath(info.Path) || RomExtensions.IsWindowsRom(info.Path))) return result;

            info.ProviderIds.TryGetValue("ScreenScraper", out var screenScraperId);
            if (string.IsNullOrEmpty(screenScraperId))
                screenScraperId = TryExtractEmbeddedScreenScraperId(info.Path);

            var targetRegion = ResolveEffectiveRegion(info.Path, RegionPreference);

            if (string.IsNullOrEmpty(screenScraperId))
            {
                var searchResults = await GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                var best = searchResults.FirstOrDefault();
                if (best != null && best.ProviderIds.TryGetValue("ScreenScraper", out var foundId))
                {
                    screenScraperId = foundId;
                }
            }

            if (string.IsNullOrEmpty(screenScraperId)) return result;

            try
            {
                var url = BuildApiUrl("jeuInfos.php", new Dictionary<string, string?> { { "gameid", screenScraperId } });
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("response", out var resp) &&
                        resp.TryGetProperty("jeu", out var jeu))
                    {
                        var book = new Book();
                        book.SetProviderId("ScreenScraper", screenScraperId);

                        // 1. Localized Title
                        var localizedTitle = ExtractLocalizedTitle(jeu, targetRegion);
                        if (!string.IsNullOrWhiteSpace(localizedTitle))
                        {
                            book.Name = localizedTitle;
                        }

                        // 2. Localized Synopsis
                        var synopsis = ExtractSynopsis(jeu, LanguagePreference);
                        if (!string.IsNullOrWhiteSpace(synopsis))
                        {
                            book.Overview = synopsis;
                        }

                        // 3. Release Date
                        var releaseDate = ExtractReleaseDate(jeu, targetRegion);
                        if (releaseDate.HasValue)
                        {
                            book.PremiereDate = releaseDate.Value;
                            book.ProductionYear = releaseDate.Value.Year;
                        }

                        // 4. Developer / Publisher (Studios)
                        if (jeu.TryGetProperty("developpeur", out var devEl) && devEl.TryGetProperty("text", out var devText))
                        {
                            var dev = devText.GetString();
                            if (!string.IsNullOrWhiteSpace(dev)) book.AddStudio(dev);
                        }

                        if (jeu.TryGetProperty("editeur", out var pubEl) && pubEl.TryGetProperty("text", out var pubText))
                        {
                            var pub = pubText.GetString();
                            if (!string.IsNullOrWhiteSpace(pub)) book.AddStudio(pub);
                        }

                        // 5. Genres
                        if (jeu.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var g in genresEl.EnumerateArray())
                            {
                                if (g.TryGetProperty("noms", out var gNoms) && gNoms.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var gn in gNoms.EnumerateArray())
                                    {
                                        var lang = gn.TryGetProperty("langue", out var l) ? l.GetString() : "en";
                                        var name = gn.TryGetProperty("text", out var t) ? t.GetString() : null;
                                        if (string.Equals(lang, LanguagePreference, StringComparison.OrdinalIgnoreCase) || string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!string.IsNullOrWhiteSpace(name))
                                            {
                                                book.AddGenre(name);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // 6. Community Rating (note out of 20 -> convert to 10)
                        if (jeu.TryGetProperty("note", out var noteEl) && noteEl.TryGetProperty("text", out var noteText))
                        {
                            if (float.TryParse(noteText.GetString(), out var noteVal) && noteVal > 0)
                            {
                                book.CommunityRating = (float)Math.Round(noteVal / 2.0f, 1);
                            }
                        }

                        result.Item = book;
                        result.HasMetadata = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] ScreenScraper metadata retrieval failed for ID {Id}", screenScraperId);
            }

            return result;
        }

        private RemoteSearchResult? ParseJeuToSearchResult(JsonElement root, string? fallbackId, string targetRegion)
        {
            if (!root.TryGetProperty("response", out var resp) || !resp.TryGetProperty("jeu", out var jeu))
            {
                return null;
            }

            var gameId = jeu.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? fallbackId : fallbackId;
            if (string.IsNullOrEmpty(gameId)) return null;

            var title = ExtractLocalizedTitle(jeu, targetRegion);
            if (string.IsNullOrEmpty(title)) return null;

            var sr = new RemoteSearchResult
            {
                Name = title,
                SearchProviderName = Name,
                ProviderIds = new Dictionary<string, string> { { "ScreenScraper", gameId } }
            };

            var date = ExtractReleaseDate(jeu, targetRegion);
            if (date.HasValue)
            {
                sr.PremiereDate = date.Value;
                sr.ProductionYear = date.Value.Year;
            }

            var boxUrl = ExtractMediaUrl(jeu, "box-2d", targetRegion) ?? ExtractMediaUrl(jeu, "box-3d", targetRegion);
            if (!string.IsNullOrEmpty(boxUrl))
            {
                sr.ImageUrl = boxUrl;
            }

            return sr;
        }

        private IEnumerable<RemoteSearchResult> ParseRechercheResults(JsonElement root, string targetRegion)
        {
            var results = new List<RemoteSearchResult>();
            if (!root.TryGetProperty("response", out var resp)) return results;

            JsonElement jeuxArray;
            if (resp.TryGetProperty("jeux", out var jx) && jx.ValueKind == JsonValueKind.Array)
            {
                jeuxArray = jx;
            }
            else if (resp.TryGetProperty("jeu", out var jSingle))
            {
                var sr = ParseSingleJeuElement(jSingle, targetRegion);
                if (sr != null) results.Add(sr);
                return results;
            }
            else
            {
                return results;
            }

            foreach (var jeu in jeuxArray.EnumerateArray().Take(10))
            {
                var sr = ParseSingleJeuElement(jeu, targetRegion);
                if (sr != null) results.Add(sr);
            }

            return results;
        }

        private RemoteSearchResult? ParseSingleJeuElement(JsonElement jeu, string targetRegion)
        {
            var gameId = jeu.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(gameId)) return null;

            var title = ExtractLocalizedTitle(jeu, targetRegion);
            if (string.IsNullOrEmpty(title)) return null;

            var sr = new RemoteSearchResult
            {
                Name = title,
                SearchProviderName = Name,
                ProviderIds = new Dictionary<string, string> { { "ScreenScraper", gameId } }
            };

            var date = ExtractReleaseDate(jeu, targetRegion);
            if (date.HasValue)
            {
                sr.PremiereDate = date.Value;
                sr.ProductionYear = date.Value.Year;
            }

            var boxUrl = ExtractMediaUrl(jeu, "box-2d", targetRegion) ?? ExtractMediaUrl(jeu, "box-3d", targetRegion);
            if (!string.IsNullOrEmpty(boxUrl))
            {
                sr.ImageUrl = boxUrl;
            }

            return sr;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }


    public class ScreenScraperImageProvider : BaseScreenScraperProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "ScreenScraper Image Provider";
        public int Order => 1;

        public ScreenScraperImageProvider(IHttpClientFactory httpClientFactory, ILogger<ScreenScraperImageProvider> logger)
            : base(httpClientFactory, logger) { }

        public bool Supports(BaseItem item) => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[]
        {
            ImageType.Primary,
            ImageType.Backdrop,
            ImageType.Menu // ClearLogo (wheel)
        };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!IsConfigured) return list;

            var screenScraperId = item.GetProviderId("ScreenScraper");
            if (string.IsNullOrEmpty(screenScraperId))
                screenScraperId = TryExtractEmbeddedScreenScraperId(item.Path);

            if (string.IsNullOrEmpty(screenScraperId)) return list;

            var targetRegion = ResolveEffectiveRegion(item.Path, RegionPreference);

            try
            {
                var url = BuildApiUrl("jeuInfos.php", new Dictionary<string, string?> { { "gameid", screenScraperId } });
                var response = await GetHttpClient().GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("response", out var resp) &&
                        resp.TryGetProperty("jeu", out var jeu))
                    {
                        // 1. Primary Image (2D Box, 3D Box, or Mix)
                        var box2d = ExtractMediaUrl(jeu, "box-2d", targetRegion);
                        var box3d = ExtractMediaUrl(jeu, "box-3d", targetRegion);
                        if (!string.IsNullOrEmpty(box2d))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = box2d });
                        }
                        if (!string.IsNullOrEmpty(box3d) && box3d != box2d)
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = box3d });
                        }

                        // 2. Backdrop Image (Fanart, Screenshot, or Titlescreen)
                        var fanart = ExtractMediaUrl(jeu, "fanart", targetRegion);
                        var screenshot = ExtractMediaUrl(jeu, "screen", targetRegion);
                        var titlescreen = ExtractMediaUrl(jeu, "titlescreen", targetRegion);

                        if (!string.IsNullOrEmpty(fanart))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = fanart });
                        }
                        if (!string.IsNullOrEmpty(screenshot))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = screenshot });
                        }
                        if (!string.IsNullOrEmpty(titlescreen))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Backdrop, Url = titlescreen });
                        }

                        // 3. ClearLogo / Menu Image (Wheel)
                        var wheel = ExtractMediaUrl(jeu, "wheel", targetRegion);
                        if (!string.IsNullOrEmpty(wheel))
                        {
                            list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Menu, Url = wheel });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "[JellyEmu] ScreenScraper image lookup failed for ID {Id}", screenScraperId);
            }

            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return GetHttpClient().GetAsync(url, cancellationToken);
        }
    }

    public class ScreenScraperExternalId : IExternalId
    {
        public string ProviderName => "ScreenScraper";
        public string Key => "ScreenScraper";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://www.screenscraper.fr/gameinfos.php?gameid={0}";

        public bool Supports(IHasProviderIds item) =>
            item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path) && !RomExtensions.IsWindowsRom((item as BaseItem)?.Path);
    }

    public class ScreenScraperExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "ScreenScraper";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (RomExtensions.IsWindowsRom(item.Path)) yield break;
            if (item.TryGetProviderId("ScreenScraper", out var id))
            {
                yield return $"https://www.screenscraper.fr/gameinfos.php?gameid={id}";
            }
        }
    }
}

