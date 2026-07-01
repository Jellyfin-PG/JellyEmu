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
    /// Routes: /jellyemu/prefs/*
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
                videoRotation      = prefs.VideoRotation,
                controls           = prefs.Controls,
                jeBindings         = prefs.Controls,
                virtualGamepad     = prefs.VirtualGamepad,
                virtualGamepadLefty= prefs.VirtualGamepadLefty
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
                    Controls:           Str("jeBindings",         Str("controls",           current.Controls)),
                    ControllerControls: Str("controllerControls", current.ControllerControls),
                    RaUsername:         current.RaUsername,
                    RaApiKey:           current.RaApiKey,
                    VirtualGamepad:     Str("virtualGamepad",     current.VirtualGamepad),
                    VirtualGamepadLefty:Str("virtualGamepadLefty",current.VirtualGamepadLefty));
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
                jeBindings         = current.Controls,
                virtualGamepad     = current.VirtualGamepad,
                virtualGamepadLefty= current.VirtualGamepadLefty
            });
        }
    }
}
