using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves Roms and core information
    /// </summary>
    public class JellyEmuRomController : JellyEmuBaseController
    {

        public JellyEmuRomController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPico8Controller> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
        }

        [HttpGet("/jellyemu/rom/{itemId}/{filename?}")]
        [HttpHead("/jellyemu/rom/{itemId}/{filename?}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Rom(string itemId, string? filename = null)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path) || !System.IO.File.Exists(item.Path))
            {
                Logger.LogWarning("[JellyEmu] Rom: item {ItemId} not found or path missing", itemId);
                return NotFound();
            }

            Logger.LogInformation("[JellyEmu] Serving ROM: {Path}", item.Path);

            var stream = System.IO.File.OpenRead(item.Path);
            var fileName = Path.GetFileName(item.Path);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        /// <summary>
        /// Returns the resolved core name, whether it requires threads (SharedArrayBuffer),
        /// and which launcher to use for the given item.
        /// Used by the UI to decide iframe vs new tab launch, and which play page to load.
        /// 
        /// Path: GET /jellyemu/core/{itemId}
        /// Parameters:
        ///   - itemId (string, path): The unique ID of the item.
        /// Returns Example: { "core": "gba", "needsThreads": false, "launcher": "ejs" }
        ///          Example: { "core": "pico8", "needsThreads": false, "launcher": "pico8" }
        /// </summary>
        [HttpGet("/jellyemu/core/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetCore(string itemId)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
                return NotFound();

            var info = ResolveCoreInfo(item);
            return Ok(new { core = info.Core, needsThreads = info.NeedsThreads, launcher = info.Launcher });
        }
    }
}