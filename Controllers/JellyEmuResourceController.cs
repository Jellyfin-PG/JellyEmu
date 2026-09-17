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
            "saves.js",
            "people.js"
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
        private readonly JellyEmuPlayManager _playManager;

        public JellyEmuResourceController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuResourceController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuPlayManager playManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _playManager = playManager;
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
        /// Serves the core options embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.core.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.core.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CoreJs()
        {
            return ServeEmbeddedJs("ejs.core.js");
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
        /// Serves the settings embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.setting.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.setting.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult SettingJs()
        {
            return ServeEmbeddedJs("ejs.setting.js");
        }

        /// <summary>
        /// Serves the netplay embedded JS resource.
        /// Path: GET /jellyemu/assets/ejs.netplay.js
        /// </summary>
        [HttpGet("/jellyemu/assets/ejs.netplay.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult NetplayJs()
        {
            return ServeEmbeddedJs("ejs.netplay.js");
        }

        /// <summary>
        /// Serves the COI Service Worker resource with Service-Worker-Allowed header.
        /// Path: GET /jellyemu/assets/coi-serviceworker.js
        /// </summary>
        [HttpGet("/jellyemu/assets/coi-serviceworker.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CoiServiceWorkerJs()
        {
            Response.Headers["Service-Worker-Allowed"] = "/";
            return ServeEmbeddedFile("Web.Injection.coi-serviceworker.js", "application/javascript; charset=utf-8");
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
        /// Serves the Play! stylesheet embedded CSS resource.
        /// Path: GET /jellyemu/assets/play.style.css
        /// </summary>
        [HttpGet("/jellyemu/assets/play.style.css")]
        [Produces("text/css")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlayStyleCss()
        {
            const string contentType = "text/css; charset=utf-8";
            Response.ContentType = contentType;

            var assembly = typeof(JellyEmuResourceController).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("play.style.css", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Logger.LogError("[JellyEmu] Embedded stylesheet play.style.css not found.");
                return NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return NotFound();

            return File(stream, contentType);
        }

        /// <summary>
        /// Serves the Play! disc I/O worker embedded JS resource.
        /// Path: GET /jellyemu/assets/play.disc.js
        /// </summary>
        [HttpGet("/jellyemu/assets/play.disc.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlayDiscJs()
        {
            return ServeEmbeddedJs("play.disc.js");
        }

        /// <summary>
        /// Serves the Play! input manager embedded JS resource.
        /// Path: GET /jellyemu/assets/play.input.js
        /// </summary>
        [HttpGet("/jellyemu/assets/play.input.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlayInputJs()
        {
            return ServeEmbeddedJs("play.input.js");
        }

        /// <summary>
        /// Serves the Play! save manager embedded JS resource.
        /// Path: GET /jellyemu/assets/play.save.js
        /// </summary>
        [HttpGet("/jellyemu/assets/play.save.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlaySaveJs()
        {
            return ServeEmbeddedJs("play.save.js");
        }

        /// <summary>
        /// Serves the Play! settings manager embedded JS resource.
        /// Path: GET /jellyemu/assets/play.setting.js
        /// </summary>
        [HttpGet("/jellyemu/assets/play.setting.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlaySettingJs()
        {
            return ServeEmbeddedJs("play.setting.js");
        }

        /// <summary>
        /// Serves downloaded Play! wasm core runtime files (Play.js, Play.wasm).
        /// Path: GET /jellyemu/play/runtime/{*filename}
        /// </summary>
        [HttpGet("/jellyemu/play/runtime/{*filename}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlayRuntimeFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || filename.Contains(".."))
                return NotFound();

            var filePath = _playManager.GetRuntimeFilePath(filename);
            if (filePath == null || !System.IO.File.Exists(filePath))
                return NotFound();

            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cache-Control"] = "no-cache, must-revalidate";

            string contentType = filename.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)
                ? "application/wasm"
                : filename.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                    ? "text/javascript; charset=utf-8"
                    : "application/octet-stream";

            return PhysicalFile(filePath, contentType);
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
            Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
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
            Response.Headers["Cache-Control"] = "no-cache, must-revalidate";

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