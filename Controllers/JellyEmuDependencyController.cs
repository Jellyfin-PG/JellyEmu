using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves cached/proxied third-party JS dependencies used by JellyEmu.
    /// Routes: /jellyemu/pico8/runtime.js, /jellyemu/threejs/three.min.js
    /// </summary>
    public class JellyEmuDependencyController : JellyEmuBaseController
    {
        private readonly JellyEmuPico8Manager _pico8Manager;
        private readonly JellyEmuThreeJsManager _threeJsManager;
        private readonly JellyEmuEjsManager _ejsManager;

        public JellyEmuDependencyController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuDependencyController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            JellyEmuPico8Manager pico8Manager,
            JellyEmuThreeJsManager threeJsManager)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _pico8Manager = pico8Manager;
            _threeJsManager = threeJsManager;
            _ejsManager = ejsManager;
        }

        /// <summary>
        /// Serves the PICO-8 web runtime JS.
        /// Tries local cache first; falls back to live proxy from Lexaloffle.
        ///
        /// Path: GET /jellyemu/pico8/runtime.js
        /// </summary>
        [HttpGet("/jellyemu/pico8/runtime.js")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Pico8Runtime()
        {
            const string contentType = "application/javascript";
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            if (_pico8Manager.IsReady)
            {
                var localPath = Path.Combine(_pico8Manager.LocalRoot, JellyEmuPico8Manager.RuntimeFilename);
                if (System.IO.File.Exists(localPath))
                {
                    Logger.LogDebug("[JellyEmu] Serving PICO-8 runtime from local cache");
                    return File(System.IO.File.OpenRead(localPath), contentType);
                }
            }

            Logger.LogWarning("[JellyEmu] PICO-8 runtime not cached yet — proxying from Lexaloffle");
            try
            {
                var client = HttpClientFactory.CreateClient("JellyEmuPico8");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; JellyEmu/1.0)");

                using var upstream = await client.GetAsync(
                    JellyEmuPico8Manager.RuntimeUrl,
                    HttpCompletionOption.ResponseHeadersRead);

                if (!upstream.IsSuccessStatusCode)
                {
                    Logger.LogError("[JellyEmu] Lexaloffle returned {Status} for runtime", (int)upstream.StatusCode);
                    return StatusCode(502);
                }

                return File(await upstream.Content.ReadAsByteArrayAsync(), contentType);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to proxy PICO-8 runtime from Lexaloffle");
                return StatusCode(502);
            }
        }

        /// <summary>
        /// Serves the Three.js r128 runtime JS.
        /// Tries local cache first; falls back to live proxy from cdnjs.
        ///
        /// Path: GET /jellyemu/threejs/three.min.js
        /// </summary>
        [HttpGet("/jellyemu/threejs/three.min.js")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> ThreeJs()
        {
            const string contentType = "application/javascript";
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            if (_threeJsManager.IsReady)
            {
                var localPath = Path.Combine(_threeJsManager.LocalRoot, JellyEmuThreeJsManager.RuntimeFilename);
                if (System.IO.File.Exists(localPath))
                {
                    Logger.LogDebug("[JellyEmu] Serving Three.js runtime from local cache");
                    return File(System.IO.File.OpenRead(localPath), contentType);
                }
            }

            Logger.LogWarning("[JellyEmu] Three.js runtime not cached yet — proxying from cdnjs");
            try
            {
                var client = HttpClientFactory.CreateClient("JellyEmuThreeJs");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; JellyEmu/1.0)");

                using var upstream = await client.GetAsync(
                    JellyEmuThreeJsManager.RuntimeUrl,
                    HttpCompletionOption.ResponseHeadersRead);

                if (!upstream.IsSuccessStatusCode)
                {
                    Logger.LogError("[JellyEmu] cdnjs returned {Status} for Three.js", (int)upstream.StatusCode);
                    return StatusCode(502);
                }

                return File(await upstream.Content.ReadAsByteArrayAsync(), contentType);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to proxy Three.js runtime from cdnjs");
                return StatusCode(502);
            }
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

            if (_ejsManager.IsReady)
            {
                var localPath = Path.Combine(_ejsManager.LocalRoot, path.Replace('/', Path.DirectorySeparatorChar));
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