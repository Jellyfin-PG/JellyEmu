using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    [Authorize]
    [ApiController]
    public class JellyEmuMarketplaceController : JellyEmuBaseController
    {
        private readonly MarketplaceService _marketplaceService;

        public JellyEmuMarketplaceController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuMarketplaceController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            MarketplaceService marketplaceService)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _marketplaceService = marketplaceService;
        }

        /// <summary>
        /// Gets the configured or default download providers list.
        /// </summary>
        [HttpGet("/jellyemu/marketplace/providers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProviders()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return Ok(new List<string>());

            var isConfigured = config.MarketplaceConfigured || (config.MarketplaceProviders != null && config.MarketplaceProviders.Count > 0);
            if (!isConfigured)
            {
                try
                {
                    var allProviders = await _marketplaceService.LoadProvidersAsync();
                    if (allProviders.Any())
                    {
                        return Ok(allProviders.Select(p => $"https://{p.Domain}").ToList());
                    }
                }
                catch
                {
                    // Fallback
                }
                return Ok(new List<string> { "https://vimm.net" });
            }

            return Ok(config.MarketplaceProviders ?? new List<string>());
        }

        /// <summary>
        /// Gets all available scraper providers from the external feed.
        /// </summary>
        [HttpGet("/jellyemu/marketplace/feed-providers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFeedProviders()
        {
            try
            {
                var allProviders = await _marketplaceService.LoadProvidersAsync();
                var config = Plugin.Instance?.Configuration;
                var active = config?.MarketplaceProviders ?? new List<string>();
                var isConfigured = config?.MarketplaceConfigured ?? false;

                if (!isConfigured && active.Count == 0)
                {
                    try
                    {
                        if (allProviders.Any())
                        {
                            active = allProviders.Select(p => $"https://{p.Domain}").ToList();
                        }
                    }
                    catch
                    {
                        active = new List<string> { "https://vimm.net" };
                    }
                }

                var result = allProviders.Select(p => new
                {
                    name = p.Name,
                    domain = p.Domain,
                    url = $"https://{p.Domain}",
                    isEnabled = active.Any(a => a.Contains(p.Domain, StringComparison.OrdinalIgnoreCase))
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to load feed providers.");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// Adds a new provider URL to the configuration.
        /// </summary>
        [HttpPost("/jellyemu/marketplace/providers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AddProvider([FromBody] AddProviderRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest(new { message = "URL cannot be empty." });
            }

            var url = request.Url.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return BadRequest(new { message = "Invalid absolute URL." });
            }

            var allProviders = await _marketplaceService.LoadProvidersAsync();
            var isSupported = allProviders.Any(p => url.Contains(p.Domain, StringComparison.OrdinalIgnoreCase));
            if (!isSupported)
            {
                return BadRequest(new { message = "Domain not supported by the external feed configuration." });
            }

            var plugin = Plugin.Instance;
            if (plugin == null) return BadRequest(new { message = "Plugin instance is null." });

            var config = plugin.Configuration;
            if (config == null) return BadRequest(new { message = "Plugin configuration not loaded." });

            var isConfigured = config.MarketplaceConfigured || (config.MarketplaceProviders != null && config.MarketplaceProviders.Count > 0);
            if (!isConfigured)
            {
                config.MarketplaceProviders = allProviders.Select(p => $"https://{p.Domain}").ToList();
                config.MarketplaceConfigured = true;
            }
            else if (config.MarketplaceProviders == null)
            {
                config.MarketplaceProviders = new List<string>();
            }

            if (!config.MarketplaceProviders.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                config.MarketplaceProviders.Add(url);
                plugin.UpdateConfiguration(config);
            }

            return Ok(config.MarketplaceProviders);
        }

        /// <summary>
        /// Removes a provider URL from the configuration.
        /// </summary>
        [HttpDelete("/jellyemu/marketplace/providers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RemoveProvider([FromQuery] string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return BadRequest(new { message = "URL query parameter is required." });
            }

            var plugin = Plugin.Instance;
            if (plugin == null) return BadRequest(new { message = "Plugin instance is null." });

            var config = plugin.Configuration;
            if (config == null) return BadRequest(new { message = "Plugin configuration not loaded." });

            var isConfigured = config.MarketplaceConfigured || (config.MarketplaceProviders != null && config.MarketplaceProviders.Count > 0);
            if (!isConfigured)
            {
                var allProviders = await _marketplaceService.LoadProvidersAsync();
                config.MarketplaceProviders = allProviders.Select(p => $"https://{p.Domain}").ToList();
                config.MarketplaceConfigured = true;
            }

            if (config.MarketplaceProviders != null)
            {
                config.MarketplaceProviders.RemoveAll(p => string.Equals(p, url, StringComparison.OrdinalIgnoreCase));
                plugin.UpdateConfiguration(config);
            }

            return Ok(config.MarketplaceProviders ?? new List<string>());
        }

        /// <summary>
        /// Returns all virtual folders in Jellyfin (games, libraries) with paths.
        /// Used by settings dropdown so users can choose the ROM target folder.
        /// </summary>
        [HttpGet("/jellyemu/marketplace/library-folders")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetLibraryFolders()
        {
            try
            {
                var folders = LibraryManager.GetVirtualFolders()
                    .Select(f => new
                    {
                        name = f.Name,
                        paths = f.Locations.ToList(),
                        type = f.CollectionType
                    })
                    .ToList();

                return Ok(folders);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to fetch virtual folders.");
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// Searches ROMs across active providers.
        /// </summary>
        [HttpGet("/jellyemu/marketplace/search")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Search([FromQuery(Name = "query")] string? query = null, [FromQuery(Name = "q")] string? q = null, [FromQuery] string? system = null, [FromQuery] string? letter = null)
        {
            var searchTerm = !string.IsNullOrWhiteSpace(query) ? query : q;
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return BadRequest(new { message = "Search query parameter 'query' or 'q' is required." });
            }

            try
            {
                var results = await _marketplaceService.SearchAsync(searchTerm, system ?? "", letter ?? "");
                return Ok(results);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Marketplace search failed for query: {Query}", q);
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// Browses ROMs on a specific provider system list.
        /// </summary>
        [HttpGet("/jellyemu/marketplace/browse")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Browse([FromQuery] string provider, [FromQuery] string system, [FromQuery] string? letter = null)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(system))
            {
                return BadRequest(new { message = "Parameters 'provider' and 'system' are required." });
            }

            try
            {
                var results = await _marketplaceService.BrowseAsync(provider, system, letter ?? "");
                return Ok(results);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Marketplace browse failed for system {System} on {Provider}", system, provider);
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        /// <summary>
        /// Downloads the ROM, saves it to the library folder, and triggers a library scan.
        /// </summary>
        [HttpPost("/jellyemu/marketplace/download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Download([FromBody] DownloadRequest request)
        {
            if (request == null)
            {
                Logger.LogWarning("[JellyEmu] Download request body is null.");
                return BadRequest(new { message = "Request body is empty." });
            }

            if (string.IsNullOrWhiteSpace(request.DetailUrl) || string.IsNullOrWhiteSpace(request.System))
            {
                Logger.LogWarning("[JellyEmu] Download request missing parameters. DetailUrl: '{DetailUrl}', System: '{System}'", request.DetailUrl, request.System);
                return BadRequest(new { message = "Parameters 'detailUrl' and 'system' are required." });
            }

            var config = Plugin.Instance?.Configuration;
            var targetPath = config?.GamesLibraryPath ?? string.Empty;

            if (string.IsNullOrEmpty(targetPath))
            {
                Logger.LogWarning("[JellyEmu] ROM download attempted but games library folder is not configured.");
                return BadRequest(new { message = "Games library folder has not been configured in JellyEmu settings." });
            }

            try
            {
                var targetFile = await _marketplaceService.DownloadRomAsync(request.DetailUrl, request.System, targetPath);
                return Ok(new { success = true, filename = targetFile, message = $"Successfully downloaded and added to {targetPath}" });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] ROM download failed for URL: {Url}", request.DetailUrl);
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = ex.Message });
            }
        }
    }

    public class AddProviderRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public class DownloadRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("detailUrl")]
        public string DetailUrl { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("system")]
        public string System { get; set; } = string.Empty;
    }
}
