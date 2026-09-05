using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles fetching and swapping discs for multi-disc .j3u playlist items.
    /// Routes: /jellyemu/playlist/*
    /// </summary>
    public class JellyEmuPlaylistController : JellyEmuBaseController
    {
        private readonly JellyEmuFileService _fileService;

        public JellyEmuPlaylistController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPlaylistController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            JellyEmuFileService fileService)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// Gets the disc list and the current active disc index for a user and .j3u playlist.
        /// Path: GET /jellyemu/playlist/{itemId}/discs/{userId}
        /// </summary>
        [HttpGet("/jellyemu/playlist/{itemId}/discs/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetDiscs(string itemId, string userId)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
            {
                return BadRequest("Invalid item ID or user ID format.");
            }

            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path) || !item.Path.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound("Item not found or is not a .j3u playlist.");
            }

            var discFiles = J3uParser.GetReferencedFiles(item.Path);
            var discs = discFiles.Select((file, index) => new
            {
                index = index + 1,
                name = $"Disc {index + 1}",
                filename = Path.GetFileName(file)
            }).ToList();

            var activeDiscIndex = _fileService.GetActiveDiscIndex(userId, itemId, discs.Count);

            return Ok(new
            {
                activeDiscIndex,
                discs
            });
        }

        /// <summary>
        /// Swaps the active disc for a .j3u playlist to either a specific index or the next disc.
        /// Path: POST /jellyemu/playlist/{itemId}/swap/{userId}?disc={index}
        /// </summary>
        [HttpPost("/jellyemu/playlist/{itemId}/swap/{userId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult SwapDisc(string itemId, string userId, [FromQuery] string disc)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
            {
                return BadRequest("Invalid item ID or user ID format.");
            }

            if (!VerifyUser(userId)) return Forbid();

            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path) || !item.Path.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound("Item not found or is not a .j3u playlist.");
            }

            var discFiles = J3uParser.GetReferencedFiles(item.Path);
            if (discFiles.Count == 0)
            {
                return BadRequest("The .j3u playlist is empty.");
            }

            int activeDiscIndex = _fileService.GetActiveDiscIndex(userId, itemId, discFiles.Count);

            int targetDisc = 1;
            if (string.Equals(disc, "next", StringComparison.OrdinalIgnoreCase))
            {
                targetDisc = (activeDiscIndex % discFiles.Count) + 1;
            }
            else if (int.TryParse(disc, out var parsedIndex))
            {
                targetDisc = parsedIndex;
            }
            else
            {
                return BadRequest("Invalid disc parameter. Must be 'next' or a valid integer index.");
            }

            if (targetDisc < 1 || targetDisc > discFiles.Count)
            {
                targetDisc = 1;
            }

            _fileService.SetActiveDiscIndex(userId, itemId, targetDisc);

            Logger.LogInformation("[JellyEmu] Item {ItemId} user {UserId} active disc swapped to {DiscIndex} ({DiscFile})",
                itemId, userId, targetDisc, Path.GetFileName(discFiles[targetDisc - 1]));

            return Ok(new
            {
                activeDiscIndex = targetDisc
            });
        }
    }
}
