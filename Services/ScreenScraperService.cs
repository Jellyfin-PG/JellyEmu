using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JellyEmu.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    public class GameGuideResponse
    {
        [JsonPropertyName("itemId")]
        public string? ItemId { get; set; }

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "ScreenScraper";

        [JsonPropertyName("gameId")]
        public string? GameId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("guideUrl")]
        public string? GuideUrl { get; set; }

        [JsonPropertyName("manualUrl")]
        public string? ManualUrl { get; set; }

        [JsonPropertyName("mapUrl")]
        public string? MapUrl { get; set; }

        [JsonPropertyName("videoUrl")]
        public string? VideoUrl { get; set; }

        [JsonPropertyName("boxArtUrl")]
        public string? BoxArtUrl { get; set; }

        [JsonPropertyName("wheelUrl")]
        public string? WheelUrl { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("developer")]
        public string? Developer { get; set; }

        [JsonPropertyName("publisher")]
        public string? Publisher { get; set; }

        [JsonPropertyName("releaseDate")]
        public DateTime? ReleaseDate { get; set; }

        [JsonPropertyName("rating")]
        public float? Rating { get; set; }

        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; } = new();
    }

    public class ScreenScraperGuideDetails
    {
        public string GameId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Overview { get; set; }
        public string? GuideUrl { get; set; }
        public string? ManualUrl { get; set; }
        public string? MapUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? BoxArtUrl { get; set; }
        public string? WheelUrl { get; set; }
        public string? Developer { get; set; }
        public string? Publisher { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public float? Rating { get; set; }
        public List<string> Genres { get; set; } = new();
    }

    public class ScreenScraperService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly JellyEmuFileService _fileService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JellyEmuCacheService _cacheService;
        private readonly ILogger<ScreenScraperService> _logger;

        private const string CachePrefix = "ss_guide_";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public ScreenScraperService(
            ILibraryManager libraryManager,
            JellyEmuFileService fileService,
            IHttpClientFactory httpClientFactory,
            JellyEmuCacheService cacheService,
            ILogger<ScreenScraperService> logger)
        {
            _libraryManager = libraryManager;
            _fileService = fileService;
            _httpClientFactory = httpClientFactory;
            _cacheService = cacheService;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the unified guide response for a game item, combining ScreenScraper metadata with local manuals.
        /// </summary>
        public async Task<GameGuideResponse?> GetUnifiedGuideAsync(
            string? itemId,
            string? provider,
            string? gameId,
            CancellationToken cancellationToken = default)
        {
            BaseItem? item = !string.IsNullOrWhiteSpace(itemId) ? _libraryManager.GetItemById(itemId) : null;
            string? localManualUrl = null;

            if (item != null && !string.IsNullOrWhiteSpace(item.Path))
            {
                var localManualPath = _fileService.GetLocalManualPath(item.Id.ToString());
                if (!string.IsNullOrEmpty(localManualPath) && System.IO.File.Exists(localManualPath))
                {
                    localManualUrl = $"/jellyemu/meta/manual/{item.Id}.pdf";
                }
            }

            ScreenScraperGuideDetails? details = null;

            if (string.IsNullOrEmpty(provider) || string.Equals(provider, "screenscraper", StringComparison.OrdinalIgnoreCase))
            {
                string? targetRegion = null;
                string? preferredLang = null;

                if (item != null)
                {
                    if (string.IsNullOrEmpty(gameId))
                    {
                        gameId = item.GetProviderId("ScreenScraper");
                    }
                    if (string.IsNullOrEmpty(gameId))
                    {
                        gameId = BaseScreenScraperProvider.TryExtractEmbeddedScreenScraperId(item.Path);
                    }

                    targetRegion = BaseScreenScraperProvider.ResolveEffectiveRegion(item.Path, Plugin.Instance?.Configuration.ScreenScraperRegionPreference ?? "auto");
                    preferredLang = Plugin.Instance?.Configuration.ScreenScraperLanguagePreference ?? "en";
                }

                if (!string.IsNullOrEmpty(gameId))
                {
                    details = await FetchGuideDetailsAsync(gameId, targetRegion, preferredLang, cancellationToken).ConfigureAwait(false);
                }
            }

            var effectiveManualUrl = localManualUrl ?? details?.ManualUrl;
            var effectiveGuideUrl = details?.GuideUrl;
            var effectiveMapUrl = details?.MapUrl;

            if (details != null || !string.IsNullOrEmpty(effectiveManualUrl))
            {
                var resolvedTitle = details != null && !string.IsNullOrWhiteSpace(details.Title) && details.Title != details.GameId
                    ? details.Title
                    : (!string.IsNullOrWhiteSpace(item?.Name) ? item.Name : (details?.GameId ?? gameId ?? itemId ?? "Game"));

                return new GameGuideResponse
                {
                    ItemId = itemId,
                    Provider = "ScreenScraper",
                    GameId = details?.GameId ?? gameId,
                    Title = resolvedTitle,
                    GuideUrl = effectiveGuideUrl,
                    ManualUrl = effectiveManualUrl,
                    MapUrl = effectiveMapUrl,
                    VideoUrl = details?.VideoUrl,
                    BoxArtUrl = details?.BoxArtUrl,
                    WheelUrl = details?.WheelUrl,
                    Overview = details?.Overview,
                    Developer = details?.Developer,
                    Publisher = details?.Publisher,
                    ReleaseDate = details?.ReleaseDate,
                    Rating = details?.Rating,
                    Genres = details?.Genres ?? new()
                };
            }

            return null;
        }

        public async Task<ScreenScraperGuideDetails?> FetchGuideDetailsAsync(
            string gameId,
            string? targetRegion = null,
            string? preferredLang = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;

            targetRegion ??= "us";
            preferredLang ??= "en";

            var cacheKey = $"{CachePrefix}{gameId}_{targetRegion}_{preferredLang}";
            if (_cacheService.TryGetValue<ScreenScraperGuideDetails>(cacheKey, out var cached))
            {
                return cached;
            }

            var fallbackGuide = new ScreenScraperGuideDetails
            {
                GameId = gameId,
                Title = gameId,
                GuideUrl = $"https://www.screenscraper.fr/gameinfos.php?gameid={gameId}&action=onglet&zone=gameinfostips"
            };

            if (!BaseScreenScraperProvider.IsConfigured)
            {
                _logger.LogDebug("[JellyEmu] ScreenScraper dev credentials not configured; using direct guide URL fallback for GameId {GameId}", gameId);
                _cacheService.Set(cacheKey, fallbackGuide, CacheDuration);
                return fallbackGuide;
            }

            try
            {
                var queryParams = new Dictionary<string, string?> { { "gameid", gameId } };
                var url = BaseScreenScraperProvider.BuildApiUrl("jeuInfos.php", queryParams);

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var softName = !string.IsNullOrEmpty(Plugin.Instance?.Configuration.ScreenScraperSoftName)
                    ? Plugin.Instance.Configuration.ScreenScraperSoftName
                    : "JellyEmu";

                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"{softName}/1.0");
                }

                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[JellyEmu] ScreenScraper guide request returned {StatusCode} for GameId {GameId}; using direct guide URL fallback", response.StatusCode, gameId);
                    _cacheService.Set(cacheKey, fallbackGuide, TimeSpan.FromMinutes(30));
                    return fallbackGuide;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("response", out var resp) ||
                    !resp.TryGetProperty("jeu", out var jeu))
                {
                    _cacheService.Set(cacheKey, fallbackGuide, TimeSpan.FromMinutes(30));
                    return fallbackGuide;
                }

                var details = ParseGuideDetails(jeu, gameId, targetRegion, preferredLang);
                if (details != null)
                {
                    _cacheService.Set(cacheKey, details, CacheDuration);
                    return details;
                }

                return fallbackGuide;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Failed to fetch ScreenScraper guide details for GameId {GameId}; using direct guide URL fallback", gameId);
                _cacheService.Set(cacheKey, fallbackGuide, TimeSpan.FromMinutes(30));
                return fallbackGuide;
            }
        }

        public static ScreenScraperGuideDetails? ParseGuideDetails(
            JsonElement jeu,
            string gameId,
            string targetRegion,
            string preferredLang)
        {
            var title = BaseScreenScraperProvider.ExtractLocalizedTitle(jeu, targetRegion);
            var synopsis = BaseScreenScraperProvider.ExtractSynopsis(jeu, preferredLang);
            var manualUrl = BaseScreenScraperProvider.ExtractMediaUrl(jeu, "manuel", targetRegion);
            var mapUrl = BaseScreenScraperProvider.ExtractMediaUrl(jeu, "map", targetRegion);
            var videoUrl = BaseScreenScraperProvider.ExtractMediaUrl(jeu, "video-normalized", targetRegion)
                        ?? BaseScreenScraperProvider.ExtractMediaUrl(jeu, "video", targetRegion);
            var boxArtUrl = BaseScreenScraperProvider.ExtractMediaUrl(jeu, "box-2d", targetRegion)
                         ?? BaseScreenScraperProvider.ExtractMediaUrl(jeu, "box-3d", targetRegion);
            var wheelUrl = BaseScreenScraperProvider.ExtractMediaUrl(jeu, "wheel", targetRegion);

            string? developer = null;
            if (jeu.TryGetProperty("developpeur", out var devEl) && devEl.TryGetProperty("text", out var devText))
            {
                developer = devText.GetString();
            }

            string? publisher = null;
            if (jeu.TryGetProperty("editeur", out var pubEl) && pubEl.TryGetProperty("text", out var pubText))
            {
                publisher = pubText.GetString();
            }

            var releaseDate = BaseScreenScraperProvider.ExtractReleaseDate(jeu, targetRegion);

            float? rating = null;
            if (jeu.TryGetProperty("note", out var noteEl) && noteEl.TryGetProperty("text", out var noteText))
            {
                if (float.TryParse(noteText.GetString(), out var noteVal) && noteVal > 0)
                {
                    rating = (float)Math.Round(noteVal / 2.0f, 1);
                }
            }

            var genres = new List<string>();
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
                            if (string.Equals(lang, preferredLang, StringComparison.OrdinalIgnoreCase) || string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    genres.Add(name);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return new ScreenScraperGuideDetails
            {
                GameId = gameId,
                Title = !string.IsNullOrWhiteSpace(title) ? title : gameId,
                Overview = synopsis,
                GuideUrl = $"https://www.screenscraper.fr/gameinfos.php?gameid={gameId}&action=onglet&zone=gameinfostips",
                ManualUrl = manualUrl,
                MapUrl = mapUrl,
                VideoUrl = videoUrl,
                BoxArtUrl = boxArtUrl,
                WheelUrl = wheelUrl,
                Developer = developer,
                Publisher = publisher,
                ReleaseDate = releaseDate,
                Rating = rating,
                Genres = genres
            };
        }
    }
}
