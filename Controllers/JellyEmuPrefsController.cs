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
    /// Handles user preferences and playtime tracking.
    /// Routes: /jellyemu/prefs/*, /jellyemu/playtime/*
    /// </summary>
    public class JellyEmuPrefsController : JellyEmuBaseController
    {
        public JellyEmuPrefsController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPrefsController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Returns all stored emulator preferences for a user.
        /// Path: GET /jellyemu/prefs/{userId}
        /// </summary>
        [HttpGet("/jellyemu/prefs/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetPrefs(string userId)
        {
            var prefs = ReadFullPrefs(userId);
            return Ok(new
            {
                userId,
                scale         = prefs.Scale,
                mute          = prefs.Mute,
                controller    = prefs.Controller,
                haptics       = prefs.Haptics,
                autosave      = prefs.Autosave,
                shader        = prefs.Shader,
                videoRotation = prefs.VideoRotation,
                controls      = prefs.Controls,
            });
        }

        /// <summary>
        /// Patches emulator preferences for a user. Omitted fields keep their current value.
        /// Path: POST /jellyemu/prefs/{userId}
        /// Body: JSON object with any subset of preference fields.
        /// </summary>
        [HttpPost("/jellyemu/prefs/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostPrefs(string userId)
        {
            UserFullPrefs current = ReadFullPrefs(userId);
            try
            {
                var body = await new System.IO.StreamReader(Request.Body).ReadToEndAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var r = doc.RootElement;
                string Str(string key, string def) =>
                    r.TryGetProperty(key, out var v) ? (v.GetString() ?? def) : def;
                int Int(string key, int def) =>
                    r.TryGetProperty(key, out var v) ? v.GetInt32() : def;

                current = new UserFullPrefs(
                    Scale:              Str("scale",              current.Scale),
                    Mute:               Str("mute",               current.Mute),
                    Controller:         Str("controller",         current.Controller),
                    Haptics:            Str("haptics",            current.Haptics),
                    Autosave:           Str("autosave",           current.Autosave),
                    Shader:             Str("shader",             current.Shader),
                    VideoRotation:      Int("videoRotation",      current.VideoRotation),
                    Controls:           Str("controls",           current.Controls),
                    ControllerControls: Str("controllerControls", current.ControllerControls));
            }
            catch { return BadRequest("Body must be a JSON object."); }

            WriteFullPrefs(userId, current);
            Logger.LogInformation("[JellyEmu] Prefs saved for user {UserId}", userId);
            return Ok(new
            {
                userId,
                scale              = current.Scale,
                mute               = current.Mute,
                controller         = current.Controller,
                haptics            = current.Haptics,
                autosave           = current.Autosave,
                shader             = current.Shader,
                videoRotation      = current.VideoRotation,
                controls           = current.Controls,
                controllerControls = current.ControllerControls,
            });
        }

        /// <summary>
        /// Returns the total playtime in seconds for a given user and item.
        /// Path: GET /jellyemu/playtime/{itemId}/{userId}
        /// </summary>
        [HttpGet("/jellyemu/playtime/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetPlaytime(string itemId, string userId)
        {
            var seconds = ReadPlaytimeSeconds(userId, itemId);
            return Ok(new { userId, itemId, seconds });
        }

        /// <summary>
        /// Adds played seconds to the running total for a given user and item.
        /// Path: POST /jellyemu/playtime/{itemId}/{userId}
        /// Body: Plain integer OR JSON { "seconds": N }
        /// </summary>
        [HttpPost("/jellyemu/playtime/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostPlaytime(string itemId, string userId)
        {
            long seconds = 0;
            try
            {
                var body = await new System.IO.StreamReader(Request.Body).ReadToEndAsync();
                body = body.Trim();
                if (body.StartsWith("{"))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    seconds = doc.RootElement.TryGetProperty("seconds", out var v) ? v.GetInt64() : 0;
                }
                else
                {
                    seconds = long.Parse(body);
                }
            }
            catch { return BadRequest("Body must be an integer number of seconds or JSON { \"seconds\": N }."); }

            if (seconds < 0) return BadRequest("seconds must be non-negative.");

            AddPlaytimeSeconds(userId, itemId, seconds);
            var total = ReadPlaytimeSeconds(userId, itemId);
            Logger.LogInformation("[JellyEmu] Playtime +{Seconds}s for item {ItemId} user {UserId} (total {Total}s)",
                seconds, itemId, userId, total);
            return Ok(new { userId, itemId, added = seconds, total });
        }
    }
}
