using System.Net.Mime;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Card metadata batch endpoint and EJS asset proxy.
    /// Routes: /jellyemu/cardmeta, /jellyemu/ejs/*
    /// </summary>
    public class JellyEmuMetaController : JellyEmuBaseController
    {
        public JellyEmuMetaController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuMetaController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Returns lightweight card metadata for a batch of item IDs (tags, rating, providerIds).
        /// Replaces N individual getItem calls with a single request per batch (max 100 IDs).
        /// Path: GET /jellyemu/cardmeta?ids=id1,id2,...
        /// </summary>
        [HttpGet("/jellyemu/cardmeta")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult CardMeta([FromQuery] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return Ok(new { });

            var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Take(100)
                            .ToList();

            var result = new Dictionary<string, object>(idList.Count);
            foreach (var id in idList)
            {
                var item = LibraryManager.GetItemById(id);
                if (item == null) continue;
                result[id] = new
                {
                    tags            = item.Tags ?? Array.Empty<string>(),
                    communityRating = item.CommunityRating,
                    providerIds     = item.ProviderIds ?? new Dictionary<string, string>(),
                };
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Serves EmulatorJS assets from local cache (if downloaded) or proxies from CDN.
        /// Path: GET /jellyemu/ejs/{*path}
        /// </summary>
        [HttpGet("/jellyemu/ejs/{*path}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> EjsAsset(string path,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            if (string.IsNullOrEmpty(path)) return NotFound();

            path = path.Replace('\\', '/').TrimStart('/');
            if (path.Contains("..")) return BadRequest();

            var contentType = path switch
            {
                var p when p.EndsWith(".mjs",  StringComparison.OrdinalIgnoreCase) => "application/javascript",
                var p when p.EndsWith(".cjs",  StringComparison.OrdinalIgnoreCase) => "application/javascript",
                var p when p.EndsWith(".jsx",  StringComparison.OrdinalIgnoreCase) => "text/javascript",
                var p when p.EndsWith(".js",   StringComparison.OrdinalIgnoreCase) => "application/javascript",
                var p when p.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) => "application/wasm",
                var p when p.EndsWith(".css",  StringComparison.OrdinalIgnoreCase) => "text/css",
                var p when p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) => "application/json",
                var p when p.EndsWith(".png",  StringComparison.OrdinalIgnoreCase) => "image/png",
                var p when p.EndsWith(".svg",  StringComparison.OrdinalIgnoreCase) => "image/svg+xml",
                var p when p.EndsWith(".txt",  StringComparison.OrdinalIgnoreCase) => "text/plain",
                var p when p.EndsWith(".csv",  StringComparison.OrdinalIgnoreCase) => "text/csv",
                var p when p.EndsWith(".xml",  StringComparison.OrdinalIgnoreCase) => "application/xml",
                _ => "application/octet-stream"
            };

            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            // Try local cache first
            if (EjsManager.IsReady)
            {
                var localPath = Path.Combine(EjsManager.LocalRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(localPath))
                {
                    Logger.LogDebug("[JellyEmu] Serving EJS asset locally: {Path}", path);
                    return File(System.IO.File.OpenRead(localPath), contentType);
                }
                Logger.LogWarning("[JellyEmu] EJS asset missing from local cache, proxying: {Path}", path);
            }

            // Fall back to CDN proxy
            var cdnUrl = $"{JellyEmuEjsManager.CdnBase}/{path}";
            Logger.LogDebug("[JellyEmu] Proxying EJS asset from CDN: {Url}", cdnUrl);

            try
            {
                var client = httpClientFactory.CreateClient("JellyEmuEjs");
                using var cdnResponse = await client.GetAsync(cdnUrl, HttpCompletionOption.ResponseHeadersRead);

                if (!cdnResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[JellyEmu] CDN returned {Status} for {Url}", (int)cdnResponse.StatusCode, cdnUrl);
                    return NotFound();
                }

                return File(await cdnResponse.Content.ReadAsByteArrayAsync(), contentType);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to proxy EJS asset from CDN: {Url}", cdnUrl);
                return StatusCode(502);
            }
        }
    }
}
