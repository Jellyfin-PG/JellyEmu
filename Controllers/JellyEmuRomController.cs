using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves Roms and core information
    /// </summary>
    public class JellyEmuRomController : JellyEmuBaseController
    {
        private readonly PlatformResolver _platformResolver;

        public JellyEmuRomController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuRomController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            PlatformResolver platformResolver)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _platformResolver = platformResolver;
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

            var fileInfo = new FileInfo(item.Path);
            Response.Headers["X-Rom-Hash"] = GetFileHash(item.Path);
            Response.Headers["X-Rom-Size"] = fileInfo.Length.ToString();
            Response.Headers["X-Rom-Extension"] = fileInfo.Extension;
            Response.Headers["X-Rom-Name"] = Path.GetFileNameWithoutExtension(item.Path);

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

        /// <summary>
        /// Returns the total number of scanned ROMs in the library (items with the tag JellyEmu).
        /// Path: GET /jellyemu/roms/count/{userId}
        /// </summary>
        [HttpGet("/jellyemu/roms/count/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetRomCount(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            var count = LibraryManager.GetItemList(query)
                .Count(i => i.Tags != null && i.Tags.Contains("JellyEmu", StringComparer.OrdinalIgnoreCase));

            return Ok(new { total = count, count = count, total_roms = count });
        }

        /// <summary>
        /// Returns the list of system tags of all scanned ROMs, merged, with 1 entry per system.
        /// Dynamically returns the total number of systems currently uploaded.
        /// Path: GET /jellyemu/roms/systems/{userId}
        /// </summary>
        [HttpGet("/jellyemu/roms/systems/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetRomSystems(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            var items = LibraryManager.GetItemList(query)
                .Where(i => i.Tags != null && i.Tags.Contains("JellyEmu", StringComparer.OrdinalIgnoreCase))
                .ToList();

            var knownSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var val in PlatformResolver.Aliases.Values)
                knownSystems.Add(val);
            foreach (var val in PlatformResolver.LibraryOnlyAliases.Values)
                knownSystems.Add(val);
            foreach (var key in CoreMap.Keys)
                knownSystems.Add(key);

            var systemsList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var resolved = _platformResolver.Resolve(item.Path);
                if (!string.IsNullOrEmpty(resolved) && !string.Equals(resolved, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    systemsList.Add(resolved);
                }

                if (item.Tags != null)
                {
                    foreach (var tag in item.Tags)
                    {
                        if (knownSystems.Contains(tag))
                        {
                            systemsList.Add(tag);
                        }
                    }
                }
            }

            var sortedSystems = systemsList.OrderBy(s => s).ToList();

            return Ok(new
            {
                systems = sortedSystems,
                totalSystems = sortedSystems.Count,
                count = sortedSystems.Count
            });
        }
    }
}