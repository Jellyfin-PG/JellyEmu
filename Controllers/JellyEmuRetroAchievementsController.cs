using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Mime;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Manages RetroAchievements credentials separately from general preferences.
    /// Routes: /jellyemu/retroachievements/*
    /// </summary>
    [Authorize]
    [ApiController]
    public class JellyEmuRetroAchievementsController : JellyEmuBaseController
    {
        public JellyEmuRetroAchievementsController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuRetroAchievementsController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Returns the RetroAchievements credentials for a user.
        /// Path: GET /jellyemu/retroachievements/{userId}
        /// </summary>
        [HttpGet("/jellyemu/retroachievements/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetCredentials(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();
            var prefs = ReadFullPrefs(userId);
            return Ok(new
            {
                userId,
                raUsername = prefs.RaUsername,
                raApiKey = prefs.RaApiKey
            });
        }

        /// <summary>
        /// Updates the RetroAchievements credentials for a user.
        /// Path: POST /jellyemu/retroachievements/{userId}
        /// Body: { "raUsername": "...", "raApiKey": "..." }
        /// </summary>
        [HttpPost("/jellyemu/retroachievements/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostCredentials(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();
            var current = ReadFullPrefs(userId);
            try
            {
                var body = await new System.IO.StreamReader(Request.Body).ReadToEndAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var r = doc.RootElement;

                var newUsername = r.TryGetProperty("raUsername", out var u) ? (u.GetString() ?? current.RaUsername) : current.RaUsername;
                var newApiKey = r.TryGetProperty("raApiKey", out var k) ? (k.GetString() ?? current.RaApiKey) : current.RaApiKey;

                var updated = new UserFullPrefs(
                    Scale: current.Scale,
                    Mute: current.Mute,
                    Controller: current.Controller,
                    Haptics: current.Haptics,
                    Autosave: current.Autosave,
                    Shader: current.Shader,
                    VideoRotation: current.VideoRotation,
                    Controls: current.Controls,
                    ControllerControls: current.ControllerControls,
                    RaUsername: newUsername,
                    RaApiKey: newApiKey);

                WriteFullPrefs(userId, updated);
                Logger.LogInformation("[JellyEmu] RetroAchievements credentials updated for user {UserId}", userId);
                return Ok(new { success = true, raUsername = newUsername });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to update RetroAchievements credentials for user {UserId}", userId);
                return BadRequest("Invalid JSON body.");
            }
        }
    }
}
