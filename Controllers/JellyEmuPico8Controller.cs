using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves the PICO-8 play page and proxies/caches the Lexaloffle web runtime.
    /// Routes: /jellyemu/pico8/play/*, /jellyemu/pico8/runtime.js
    /// </summary>
    public class JellyEmuPico8Controller : JellyEmuBaseController
    {
        private readonly JellyEmuPico8Manager _pico8Manager;

        public JellyEmuPico8Controller(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPico8Controller> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            JellyEmuPico8Manager pico8Manager)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _pico8Manager = pico8Manager;
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
        public async Task<IActionResult> Runtime()
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
    }
}