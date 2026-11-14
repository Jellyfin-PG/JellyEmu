using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    public record MarketplaceGameResult(
        string Title,
        string System,
        string ProviderName,
        string DetailUrl,
        string ThumbnailUrl,
        string? Region = null,
        string? Version = null,
        string? Languages = null,
        string? ExtraFlags = null,
        bool HasManual = false
    );

    public class ScraperProvider
    {
        public string Name { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string SearchUrl { get; set; } = string.Empty;
        public string SearchRegex { get; set; } = string.Empty;
        public string BrowseUrl { get; set; } = string.Empty;
        public string BrowseRegex { get; set; } = string.Empty;
        public string DownloadActionRegex { get; set; } = string.Empty;
        public List<string> DownloadMediaIdRegexes { get; set; } = new();
        public string DownloadMethod { get; set; } = "GET";
        public string DownloadParamName { get; set; } = "mediaId";
        public string? ThumbnailUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public Dictionary<string, string> SystemMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class MarketplaceService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<MarketplaceService> _logger;

        public static readonly Dictionary<string, string> PlatformTags = new(StringComparer.OrdinalIgnoreCase)
        {
            { "NES", "NES" },
            { "SNES", "SNES" },
            { "N64", "N64" },
            { "Game Boy", "GB" },
            { "Game Boy Color", "GBC" },
            { "Game Boy Advance", "GBA" },
            { "Nintendo DS", "NDS" },
            { "Virtual Boy", "VB" },
            { "Master System", "SMS" },
            { "Sega Genesis", "Genesis" },
            { "Game Gear", "GG" },
            { "Sega Saturn", "Saturn" },
            { "Sega CD", "Sega CD" },
            { "Sega 32X", "32X" },
            { "PlayStation", "PS1" },
            { "Atari 2600", "Atari 2600" },
            { "Atari 7800", "Atari 7800" },
            { "Atari Lynx", "Lynx" },
            { "Atari Jaguar", "Jaguar" },
            { "WonderSwan", "WonderSwan" },
            { "TurboGrafx-16", "TurboGrafx-16" },
            { "ColecoVision", "ColecoVision" },
            { "NeoGeo Pocket", "NGP" },
            { "PICO-8", "Pico-8" }
        };

        public MarketplaceService(
            IHttpClientFactory httpClientFactory,
            ILibraryManager libraryManager,
            ILogger<MarketplaceService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _libraryManager = libraryManager;
            _logger = logger;
        }

        private HttpClient GetClient(string? referer = null)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8"
            );
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            
            if (!string.IsNullOrEmpty(referer))
            {
                client.DefaultRequestHeaders.Referrer = new Uri(referer);
            }

            return client;
        }

        public static readonly List<ScraperProvider> DefaultProviders = new();

        public async Task<List<ScraperProvider>> LoadProvidersAsync()
        {
            var config = Plugin.Instance?.Configuration;
            var feedUrl = config?.MarketplaceFeedUrl;

            List<ScraperProvider>? providers = null;

            if (!string.IsNullOrWhiteSpace(feedUrl))
            {
                try
                {
                    if (feedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        feedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        var client = GetClient();
                        var json = await client.GetStringAsync(feedUrl);
                        providers = System.Text.Json.JsonSerializer.Deserialize<List<ScraperProvider>>(json, new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    else
                    {
                        var localPath = feedUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                            ? new Uri(feedUrl).LocalPath
                            : feedUrl;
                        if (File.Exists(localPath))
                        {
                            var json = await File.ReadAllTextAsync(localPath);
                            providers = System.Text.Json.JsonSerializer.Deserialize<List<ScraperProvider>>(json, new System.Text.Json.JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyEmu] Failed to load external scraper providers from {Url}.", feedUrl);
                }
            }

            if (providers == null || providers.Count == 0)
            {
                // Fallback to local providers.json file if present on disk
                var candidatePaths = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "providers.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "providers.json"),
                    Path.Combine(Path.GetDirectoryName(typeof(MarketplaceService).Assembly.Location) ?? "", "providers.json")
                };

                foreach (var candidate in candidatePaths)
                {
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(candidate);
                            var loaded = System.Text.Json.JsonSerializer.Deserialize<List<ScraperProvider>>(json, new System.Text.Json.JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (loaded != null && loaded.Count > 0)
                            {
                                providers = loaded;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[JellyEmu] Failed reading candidate providers file at {Path}", candidate);
                        }
                    }
                }
            }

            return providers ?? new List<ScraperProvider>();
        }

        private string ResolveThumbnailUrl(ScraperProvider provider, Match m, string rawSys, string id, string title)
        {
            if (m.Groups["thumbnailUrl"].Success && !string.IsNullOrWhiteSpace(m.Groups["thumbnailUrl"].Value))
            {
                var t = m.Groups["thumbnailUrl"].Value.Trim();
                return t.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? t : $"https://{provider.Domain}{t}";
            }
            if (m.Groups["thumb"].Success && !string.IsNullOrWhiteSpace(m.Groups["thumb"].Value))
            {
                var t = m.Groups["thumb"].Value.Trim();
                return t.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? t : $"https://{provider.Domain}{t}";
            }

            if (!string.IsNullOrEmpty(provider.ThumbnailUrl))
            {
                var sysSlug = m.Groups["systemSlug"].Success ? m.Groups["systemSlug"].Value : rawSys.ToLowerInvariant();
                return provider.ThumbnailUrl
                    .Replace("{domain}", provider.Domain)
                    .Replace("{system}", sysSlug)
                    .Replace("{id}", id)
                    .Replace("{title}", Uri.EscapeDataString(title));
            }

            return string.Empty;
        }

        public async Task<List<MarketplaceGameResult>> SearchAsync(string query, string systemFilter = "", string letterFilter = "")
        {
            var results = new List<MarketplaceGameResult>();
            var allProviders = await LoadProvidersAsync();
            if (allProviders.Count == 0)
            {
                return results;
            }

            var activeProviders = GetActiveProviders();
            var targetProviders = activeProviders.Count > 0
                ? allProviders.Where(p => activeProviders.Any(ap => ap.Contains(p.Domain, StringComparison.OrdinalIgnoreCase))).ToList()
                : allProviders;

            foreach (var provider in targetProviders)
            {

                try
                {
                    _logger.LogInformation("[JellyEmu] Searching provider {ProviderName} for: {Query}", provider.Name, query);
                    var providerResults = await ScrapeSearch(provider, query);
                    results.AddRange(providerResults);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyEmu] Failed searching provider {ProviderName}", provider.Name);
                }
            }

            // Apply in-memory filters
            if (!string.IsNullOrEmpty(systemFilter))
            {
                results = results.Where(r => string.Equals(r.System, systemFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(letterFilter))
            {
                if (letterFilter == "#")
                {
                    results = results.Where(r => r.Title.Length > 0 && char.IsDigit(r.Title[0])).ToList();
                }
                else
                {
                    results = results.Where(r => r.Title.StartsWith(letterFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            return results;
        }

        private async Task<List<MarketplaceGameResult>> ScrapeSearch(ScraperProvider provider, string query)
        {
            var list = new List<MarketplaceGameResult>();
            var client = GetClient();
            var searchUrl = provider.SearchUrl.Replace("{query}", Uri.EscapeDataString(query));

            var html = await client.GetStringAsync(searchUrl);
            var matches = Regex.Matches(html, provider.SearchRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var detailPath = m.Groups["url"].Value;
                var id = m.Groups["id"].Value;
                var title = System.Net.WebUtility.HtmlDecode(m.Groups["title"].Value.Trim());
                var rawSys = m.Groups["system"].Value.Trim();
                if (string.IsNullOrEmpty(rawSys) && m.Groups["systemSlug"].Success)
                {
                    rawSys = m.Groups["systemSlug"].Value.Trim();
                }

                var normalizedSystem = NormalizeSystem(rawSys);
                if (provider.SystemMap.TryGetValue(rawSys, out var mappedSys))
                {
                    normalizedSystem = NormalizeSystem(mappedSys);
                }

                var fullUrl = detailPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? detailPath
                    : $"https://{provider.Domain}{detailPath}";
                
                var thumbnailUrl = ResolveThumbnailUrl(provider, m, rawSys, id, title);

                var regionsHtml = m.Groups["regions"].Value;
                var version = m.Groups["version"].Value.Trim();
                var languages = m.Groups["languages"].Value.Trim();
                var parsedRegions = ParseRegions(regionsHtml);

                var extraHtml = m.Groups["extra"].Value;
                var hasManual = extraHtml.Contains("manual", StringComparison.OrdinalIgnoreCase);
                var extraFlags = ParseExtraFlags(extraHtml);

                list.Add(new MarketplaceGameResult(
                    title,
                    normalizedSystem,
                    provider.Name,
                    fullUrl,
                    thumbnailUrl,
                    string.IsNullOrEmpty(parsedRegions) ? null : parsedRegions,
                    version == "-" || string.IsNullOrEmpty(version) ? null : version,
                    languages == "-" || string.IsNullOrEmpty(languages) ? null : languages,
                    extraFlags,
                    hasManual
                ));
            }

            return list;
        }

        private string ParseRegions(string regionsHtml)
        {
            if (string.IsNullOrWhiteSpace(regionsHtml)) return string.Empty;
            var matches = Regex.Matches(regionsHtml, @"title=""(?<name>[^""]+)""", RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                var names = matches.Cast<Match>().Select(m => m.Groups["name"].Value.Trim()).Where(n => !string.IsNullOrEmpty(n));
                return string.Join(", ", names);
            }
            var clean = Regex.Replace(regionsHtml, "<.*?>", "").Trim();
            return clean;
        }

        private string? ParseExtraFlags(string extraHtml)
        {
            if (string.IsNullOrWhiteSpace(extraHtml)) return null;
            var matches = Regex.Matches(extraHtml, @"title=""(?<title>[^""]+)""", RegexOptions.IgnoreCase);
            var flags = new List<string>();
            foreach (Match match in matches)
            {
                var val = match.Groups["title"].Value.Trim();
                if (string.IsNullOrEmpty(val)) continue;
                if (val.Equals("Read the manual", StringComparison.OrdinalIgnoreCase)) continue;
                
                flags.Add(val);
            }
            return flags.Count > 0 ? string.Join(", ", flags) : null;
        }

        public async Task<List<MarketplaceGameResult>> BrowseAsync(string provider, string system, string letter)
        {
            try
            {
                _logger.LogInformation("[JellyEmu] Browsing provider {Provider} for System: {System}, Letter: {Letter}", provider, system, letter);
                var allProviders = await LoadProvidersAsync();
                var p = allProviders.FirstOrDefault(x => provider.Contains(x.Domain));
                if (p == null)
                {
                    _logger.LogWarning("[JellyEmu] Provider {Domain} not found in scraper sources feed.", provider);
                    return new List<MarketplaceGameResult>();
                }

                return await ScrapeBrowse(p, system, letter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed browsing provider {Provider}", provider);
                return new List<MarketplaceGameResult>();
            }
        }

        private async Task<List<MarketplaceGameResult>> ScrapeBrowse(ScraperProvider provider, string system, string letter)
        {
            var list = new List<MarketplaceGameResult>();
            var sysSlug = system;
            if (provider.SystemMap.TryGetValue(system, out var mappedSlug))
            {
                sysSlug = mappedSlug;
            }

            var letterPath = string.IsNullOrEmpty(letter) ? "A" : letter.ToUpperInvariant();
            var url = provider.BrowseUrl.Replace("{system}", sysSlug).Replace("{letter}", letterPath);

            var client = GetClient();
            var html = await client.GetStringAsync(url);
            var matches = Regex.Matches(html, provider.BrowseRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var detailPath = m.Groups["url"].Value;
                var id = m.Groups["id"].Value;
                var title = System.Net.WebUtility.HtmlDecode(m.Groups["title"].Value.Trim());
                
                var fullUrl = detailPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? detailPath
                    : $"https://{provider.Domain}{detailPath}";
                
                var thumbnailUrl = ResolveThumbnailUrl(provider, m, sysSlug, id, title);

                var regionsHtml = m.Groups["regions"].Value;
                var version = m.Groups["version"].Value.Trim();
                var languages = m.Groups["languages"].Value.Trim();
                var parsedRegions = ParseRegions(regionsHtml);

                var extraHtml = m.Groups["extra"].Value;
                var hasManual = extraHtml.Contains("manual", StringComparison.OrdinalIgnoreCase);
                var extraFlags = ParseExtraFlags(extraHtml);

                list.Add(new MarketplaceGameResult(
                    title,
                    system,
                    provider.Name,
                    fullUrl,
                    thumbnailUrl,
                    string.IsNullOrEmpty(parsedRegions) ? null : parsedRegions,
                    version == "-" || string.IsNullOrEmpty(version) ? null : version,
                    languages == "-" || string.IsNullOrEmpty(languages) ? null : languages,
                    extraFlags,
                    hasManual
                ));
            }

            return list;
        }

        private List<string> GetActiveProviders()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || config.MarketplaceProviders == null)
            {
                return new List<string>();
            }
            return config.MarketplaceProviders;
        }

        public string NormalizeSystem(string rawSystem)
        {
            if (string.IsNullOrWhiteSpace(rawSystem)) return "Unknown";
            var trimmed = rawSystem.Trim();

            if (PlatformResolver.Aliases.TryGetValue(trimmed, out var aliasPlatform))
            {
                return aliasPlatform;
            }

            var clean = trimmed.ToLowerInvariant().Replace("-", " ");
            
            if (clean.Contains("gba") || clean.Contains("game boy advance") || clean.Contains("gameboy advance"))
                return "Game Boy Advance";
            if (clean.Contains("gbc") || clean.Contains("game boy color") || clean.Contains("gameboy color"))
                return "Game Boy Color";
            if (clean.Contains("game boy") || clean.Contains("gameboy") || clean == "gb")
                return "Game Boy";
                
            if (clean.Contains("snes") || clean.Contains("super nintendo") || clean.Contains("super famicom") || clean.Contains("supernintendo"))
                return "SNES";
            if (clean.Contains("n64") || clean.Contains("nintendo 64") || clean.Contains("nintendo64"))
                return "N64";
            if (clean.Contains("nds") || clean.Contains("nintendo ds") || clean.Contains("nintendods") || clean.Contains(" ds") || clean == "ds")
                return "Nintendo DS";
            if (clean.Contains("virtual boy") || clean.Contains("virtualboy") || clean == "vb")
                return "Virtual Boy";
                
            if (clean.Contains("sega cd") || clean.Contains("segacd") || clean.Contains("mega cd") || clean.Contains("megacd"))
                return "Sega CD";
            if (clean.Contains("32x") || clean.Contains("sega 32x"))
                return "Sega 32X";
            if (clean.Contains("genesis") || clean.Contains("mega drive") || clean.Contains("megadrive") || clean == "md")
                return "Sega Genesis";
            if (clean.Contains("game gear") || clean.Contains("gamegear") || clean == "gg")
                return "Game Gear";
            if (clean.Contains("saturn") || clean == "ss")
                return "Sega Saturn";
                
            if (clean.Contains("playstation 2") || clean.Contains("playstation2") || clean.Contains("ps2"))
                return "PlayStation 2";
            if (clean.Contains("playstation 1") || clean.Contains("playstation1") || clean.Contains("ps1") || clean.Contains("psx") || clean.Contains("playstation") || clean.Contains("ps one"))
                return "PlayStation";
            if (clean.Contains("psp") || clean.Contains("playstation portable"))
                return "PSP";

            if (clean == "nes" || clean.Contains(" nes ") || clean.StartsWith("nes ") || clean.EndsWith(" nes") || clean.Contains("famicom") || clean.Contains("nintendo entertainment system"))
                return "NES";

            if (clean.Contains("2600") || clean.Contains("atari 2600") || clean.Contains("atari2600"))
                return "Atari 2600";
            if (clean.Contains("7800") || clean.Contains("atari 7800") || clean.Contains("atari7800"))
                return "Atari 7800";
            if (clean.Contains("lynx") || clean.Contains("atari lynx"))
                return "Atari Lynx";
            if (clean.Contains("jaguar") || clean.Contains("atari jaguar"))
                return "Atari Jaguar";
                
            if (clean.Contains("wonderswan") || clean == "ws" || clean.Contains("wonder swan"))
                return "WonderSwan";
            if (clean.Contains("pce") || clean.Contains("turbografx") || clean.Contains("pc engine") || clean.Contains("pcengine") || clean.Contains("tg16") || clean.Contains("tg 16"))
                return "TurboGrafx-16";
            if (clean.Contains("coleco"))
                return "ColecoVision";
            if (clean.Contains("ngp") || clean.Contains("neogeo pocket") || clean.Contains("neogeopocket") || clean.Contains("ngpc") || clean.Contains("neo geo pocket"))
                return "NeoGeo Pocket";
            if (clean.Contains("pico"))
                return "PICO-8";

            return "Unknown";
        }

        public string GetPlatformFilenameTag(string systemName)
        {
            if (PlatformTags.TryGetValue(systemName, out var tag))
            {
                return tag;
            }
            return systemName;
        }

        public async Task<string> DownloadRomAsync(string detailUrl, string system, string gamesLibraryPath)
        {
            var allProviders = await LoadProvidersAsync();
            var provider = allProviders.FirstOrDefault(p => detailUrl.Contains(p.Domain));
            if (provider == null)
            {
                throw new NotSupportedException($"No provider configuration found in feed for URL: {detailUrl}");
            }

            var client = GetClient(detailUrl);
            byte[] fileBytes;

            var html = await client.GetStringAsync(detailUrl);

            string action = "";
            if (!string.IsNullOrEmpty(provider.DownloadActionRegex))
            {
                var formMatch = Regex.Match(html, provider.DownloadActionRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (formMatch.Success)
                {
                    var actionAttrMatch = Regex.Match(formMatch.Value, @"action=""(?<action>[^""\s]+)""", RegexOptions.IgnoreCase);
                    if (actionAttrMatch.Success)
                    {
                        action = actionAttrMatch.Groups["action"].Value;
                    }
                }
            }

            if (string.IsNullOrEmpty(action))
            {
                if (!string.IsNullOrEmpty(provider.DownloadUrl))
                {
                    action = provider.DownloadUrl.Replace("{domain}", provider.Domain);
                }
                else
                {
                    action = $"https://{provider.Domain}/";
                }
            }
            else if (action.StartsWith("//"))
            {
                action = "https:" + action;
            }
            else if (action.StartsWith("/"))
            {
                action = $"https://{provider.Domain}" + action;
            }

            string mediaId = "";
            foreach (var regex in provider.DownloadMediaIdRegexes)
            {
                var m = Regex.Match(html, regex, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    mediaId = m.Groups[provider.DownloadParamName].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(mediaId))
            {
                var fallbackMatch = Regex.Match(detailUrl, @"(?<id>\d+)(?:\.php|\.html|/|$|\?)");
                if (fallbackMatch.Success)
                {
                    mediaId = fallbackMatch.Groups["id"].Value;
                }
                else
                {
                    fallbackMatch = Regex.Match(detailUrl, @"(?<id>\d+)");
                    if (fallbackMatch.Success)
                    {
                        mediaId = fallbackMatch.Groups["id"].Value;
                    }
                    else
                    {
                        throw new Exception("Could not resolve media/game ID from the detail page or URL.");
                    }
                }
            }

            _logger.LogInformation("[JellyEmu] Decoupled downloader parsed action: {Action}, parameter value: {ParamValue}", action, mediaId);

            HttpResponseMessage response;
            var dlUrl = action;
            if (!dlUrl.Contains("?"))
            {
                dlUrl += $"?{provider.DownloadParamName}={mediaId}";
            }
            else
            {
                dlUrl += $"&{provider.DownloadParamName}={mediaId}";
            }

            if (provider.DownloadMethod == "GET" || provider.DownloadMethod == "GET_THEN_POST")
            {
                _logger.LogInformation("[JellyEmu] Downloading via GET: {Url}", dlUrl);
                response = await client.GetAsync(dlUrl);

                var isHtml = response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;
                if ((!response.IsSuccessStatusCode || isHtml) && provider.DownloadMethod == "GET_THEN_POST")
                {
                    _logger.LogWarning("[JellyEmu] GET download returned HTML or failed. Retrying via POST...");
                    var postContent = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>(provider.DownloadParamName, mediaId)
                    });
                    response = await client.PostAsync(action, postContent);
                }
            }
            else
            {
                _logger.LogInformation("[JellyEmu] Downloading via POST to: {Action}", action);
                var postContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>(provider.DownloadParamName, mediaId)
                });
                response = await client.PostAsync(action, postContent);
            }

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            {
                var responseHtml = await response.Content.ReadAsStringAsync();
                _logger.LogError("[JellyEmu] Download failed. Server returned HTML: {Content}", responseHtml.Substring(0, Math.Min(500, responseHtml.Length)));
                throw new Exception($"The download server returned an HTML page instead of the ROM binary. {provider.Name} might be blocking the request or experiencing high traffic.");
            }

            fileBytes = await response.Content.ReadAsByteArrayAsync();

            var contentDisposition = response.Content.Headers.ContentDisposition;
            var filename = contentDisposition?.FileName?.Trim('"') ?? contentDisposition?.FileNameStar?.Trim('"');
            if (string.IsNullOrEmpty(filename))
            {
                filename = $"Game_{mediaId}.zip";
            }

            return SaveFileToDisk(fileBytes, filename, system, gamesLibraryPath);
        }

        public string GetSystemFolderName(string system)
        {
            var normalized = NormalizeSystem(system);
            if (normalized != "Unknown") return normalized;
            return string.IsNullOrWhiteSpace(system) ? "Unknown" : system.Trim();
        }

        private string SaveFileToDisk(byte[] fileBytes, string originalFilename, string system, string libraryPath)
        {
            var ext = Path.GetExtension(originalFilename);
            var title = Path.GetFileNameWithoutExtension(originalFilename);
            
            title = PlatformResolver.CleanDisplayName(title);

            var systemFolder = GetSystemFolderName(system);
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                systemFolder = systemFolder.Replace(c.ToString(), "");
            }

            var targetFilename = $"{title}{ext}";
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                targetFilename = targetFilename.Replace(c.ToString(), "");
            }

            var systemDirPath = Path.Combine(libraryPath, systemFolder);
            if (!Directory.Exists(systemDirPath))
            {
                Directory.CreateDirectory(systemDirPath);
            }

            var fullPath = Path.Combine(systemDirPath, targetFilename);
            
            _logger.LogInformation("[JellyEmu] Writing ROM to: {Path}", fullPath);
            File.WriteAllBytes(fullPath, fileBytes);

            try
            {
                _logger.LogInformation("[JellyEmu] Triggering library scan for catalog update.");
                _libraryManager.QueueLibraryScan();
            }
            catch (Exception scanEx)
            {
                _logger.LogWarning(scanEx, "[JellyEmu] Failed to queue Jellyfin library scan automatically.");
            }

            return Path.Combine(systemFolder, targetFilename);
        }
    }
}
