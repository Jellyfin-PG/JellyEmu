using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves embedded JavaScript and other static resources for JellyEmu.
    /// Route prefix: /jellyemu/assets/
    /// </summary>
    public class JellyEmuResourceController : JellyEmuBaseController
    {
        public JellyEmuResourceController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuResourceController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
        }

        /// <summary>
        /// Serves the input mapping embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.input.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.input.js")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult InputJs()
        {
            return ServeEmbeddedJs("ejs.input.js");
        }

        /// <summary>
        /// Serves the input mapping embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.xr.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.xr.js")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult XrJs()
        {
            return ServeEmbeddedJs("ejs.xr.js");
        }

        /// <summary>
        /// Serves the input mapping embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.xr.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.save.js")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult SaveJs()
        {
            return ServeEmbeddedJs("ejs.save.js");
        }

        /// <summary>
        /// Shared helper: finds and streams an embedded .js resource by filename.
        /// </summary>
        private IActionResult ServeEmbeddedJs(string filename)
        {
            const string contentType = "application/javascript";
            //Response.Headers["Cache-Control"] = "public, max-age=3600";

            var assembly = typeof(JellyEmuResourceController).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(filename, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Logger.LogError("[JellyEmu] Embedded resource {Filename} not found. Available: {All}",
                    filename,
                    string.Join(", ", assembly.GetManifestResourceNames()));
                return NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogError("[JellyEmu] Could not open stream for embedded resource {Name}", resourceName);
                return NotFound();
            }

            Logger.LogDebug("[JellyEmu] Serving embedded resource {Name}", resourceName);
            return File(stream, contentType);
        }
    }
}