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
    /// Manages Jellyfin playback sessions so games appear in the Active Sessions dashboard.
    /// Routes: /jellyemu/session/*
    /// </summary>
    public class JellyEmuSessionController : JellyEmuBaseController
    {
        public JellyEmuSessionController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuSessionController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Opens a Jellyfin playback session for the game.
        /// Path: POST /jellyemu/session/start/{itemId}/{userId}
        /// Headers (optional): X-JellyEmu-DeviceId, X-JellyEmu-DeviceName
        /// </summary>
        [HttpPost("/jellyemu/session/start/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SessionStart(string itemId, string userId)
        {
            if (LibraryManager.GetItemById(itemId) == null) return NotFound();

            var remoteIp  = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var deviceId  = Request.Headers["X-JellyEmu-DeviceId"].FirstOrDefault()   ?? $"jellyemu-{userId}";
            var deviceName = Request.Headers["X-JellyEmu-DeviceName"].FirstOrDefault() ?? "JellyEmu Browser";

            await SessionService.StartSessionAsync(userId, itemId, "JellyEmu", deviceId, deviceName, remoteIp)
                .ConfigureAwait(false);

            return Ok(new { started = true, itemId, userId });
        }

        /// <summary>
        /// Keeps the session alive and advances the elapsed-time ticker.
        /// Path: POST /jellyemu/session/ping/{itemId}/{userId}
        /// </summary>
        [HttpPost("/jellyemu/session/ping/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SessionPing(string itemId, string userId)
        {
            await SessionService.PingSessionAsync(userId, itemId).ConfigureAwait(false);
            return Ok(new { alive = true });
        }

        /// <summary>
        /// Closes the Jellyfin playback session for the game.
        /// Path: POST /jellyemu/session/stop/{itemId}/{userId}
        /// </summary>
        [HttpPost("/jellyemu/session/stop/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SessionStop(string itemId, string userId)
        {
            await SessionService.StopSessionAsync(userId, itemId).ConfigureAwait(false);
            return Ok(new { stopped = true });
        }
    }
}
