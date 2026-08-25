using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
        private static readonly string[] InjectionJsModules =
        {
            "core.js",
            "cards.js",
            "details.js",
            "settings.js",
            "saves.js"
        };

        private static readonly string[] InjectionCssModules =
        {
            "core.css",
            "cards.css",
            "details.css",
            "settings.css",
            "saves.css"
        };

        private static byte[]? _cachedJsBundle;
        private static byte[]? _cachedCssBundle;

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
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult InputJs()
        {
            return ServeEmbeddedJs("ejs.input.js");
        }

        /// <summary>
        /// Serves the XR embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.xr.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.xr.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult XrJs()
        {
            return ServeEmbeddedJs("ejs.xr.js");
        }

        /// <summary>
        /// Serves the save embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.save.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.save.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult SaveJs()
        {
            return ServeEmbeddedJs("ejs.save.js");
        }

        /// <summary>
        /// Serves the stylesheet embedded CSS resource.
        /// Path: GET /jellyemu/assets/ejs.style.css
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.style.css")]
        [Produces("text/css")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult StyleCss()
        {
            const string contentType = "text/css; charset=utf-8";
            Response.ContentType = contentType;

            var assembly = typeof(JellyEmuResourceController).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("ejs.style.css", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Logger.LogError("[JellyEmu] Embedded stylesheet ejs.style.css not found.");
                return NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return NotFound();

            return File(stream, contentType);
        }

        /// <summary>
        /// Serves the combined injection CSS bundle generated in-memory from distinct module files.
        /// Path: GET /jellyemu/assets/injection/bundle.css
        /// </summary>
        [HttpGet("/jellyemu/assets/injection/bundle.css")]
        [Produces("text/css")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult InjectionBundleCss()
        {
            if (_cachedCssBundle != null)
            {
                Response.ContentType = "text/css; charset=utf-8";
                Response.Headers["Cache-Control"] = "public, max-age=3600";
                return File(_cachedCssBundle, "text/css; charset=utf-8");
            }

            return CombineAndServe("Web.Injection.", InjectionCssModules, "text/css; charset=utf-8", ref _cachedCssBundle);
        }

        /// <summary>
        /// Serves the combined injection JS bundle generated in-memory from distinct module files.
        /// Path: GET /jellyemu/assets/injection/bundle.js
        /// </summary>
        [HttpGet("/jellyemu/assets/injection/bundle.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult InjectionBundleJs()
        {
            if (_cachedJsBundle != null)
            {
                Response.ContentType = "application/javascript; charset=utf-8";
                Response.Headers["Cache-Control"] = "public, max-age=3600";
                return File(_cachedJsBundle, "application/javascript; charset=utf-8");
            }

            return CombineAndServe("Web.Injection.", InjectionJsModules, "application/javascript; charset=utf-8", ref _cachedJsBundle);
        }

        /// <summary>
        /// Serves individual embedded injection assets (e.g. core.js, cards.js, saves.css, etc.)
        /// Path: GET /jellyemu/assets/injection/{filename}
        /// </summary>
        [HttpGet("/jellyemu/assets/injection/{filename}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult InjectionAsset(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || filename.Contains(".."))
            {
                return NotFound();
            }

            string contentType = filename.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                ? "text/css; charset=utf-8"
                : "application/javascript; charset=utf-8";

            return ServeEmbeddedFile($"Web.Injection.{filename}", contentType);
        }

        /// <summary>
        /// Helper to dynamically combine embedded files into a single in-memory bundle.
        /// </summary>
        private IActionResult CombineAndServe(string prefix, string[] modules, string contentType, ref byte[]? cache)
        {
            var assembly = typeof(JellyEmuResourceController).Assembly;
            var allNames = assembly.GetManifestResourceNames();

            using var ms = new MemoryStream();
            foreach (var file in modules)
            {
                var targetSuffix = prefix + file;
                var resourceName = allNames.FirstOrDefault(n => n.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase));
                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        stream.CopyTo(ms);
                        ms.Write(Encoding.UTF8.GetBytes("\n"));
                    }
                }
                else
                {
                    Logger.LogWarning("[JellyEmu] Embedded bundle module {Module} not found.", file);
                }
            }

            if (ms.Length == 0)
            {
                return NotFound();
            }

            cache = ms.ToArray();
            Response.ContentType = contentType;
            Response.Headers["Cache-Control"] = "public, max-age=3600";
            return File(cache, contentType);
        }

        /// <summary>
        /// Shared helper: finds and streams an embedded .js resource by filename.
        /// </summary>
        private IActionResult ServeEmbeddedJs(string filename)
        {
            return ServeEmbeddedFile(filename, "application/javascript; charset=utf-8");
        }

        /// <summary>
        /// Shared helper: finds and streams an embedded resource with proper caching headers.
        /// </summary>
        private IActionResult ServeEmbeddedFile(string resourceSuffix, string contentType)
        {
            Response.ContentType = contentType;
            Response.Headers["Cache-Control"] = "public, max-age=3600";

            var assembly = typeof(JellyEmuResourceController).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Logger.LogError("[JellyEmu] Embedded resource {Suffix} not found. Available: {All}",
                    resourceSuffix,
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