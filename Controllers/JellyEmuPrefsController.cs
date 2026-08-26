using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles user preferences (global, system, game scopes) stored in SQLite.
    /// Routes: /jellyemu/prefs/*
    /// </summary>
    [ApiController]
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
        /// Returns effective runtime preferences hierarchically merged (Defaults -> Global -> System).
        /// Path: GET /jellyemu/prefs/{userId}/effective?platform={platform}
        /// </summary>
        [HttpGet("/jellyemu/prefs/{userId}/effective")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetEffectivePrefs(string userId, [FromQuery] string? platform)
        {
            var prefs = await PreferenceService.GetEffectivePreferencesAsync(userId, platform);
            return Ok(new
            {
                userId,
                platform,
                scale               = prefs.Scale,
                mute                = prefs.Mute,
                volume              = prefs.Volume,
                controller          = prefs.Controller,
                haptics             = prefs.Haptics,
                autosave            = prefs.Autosave,
                shader              = prefs.Shader,
                videoRotation       = prefs.VideoRotation,
                controls            = prefs.Controls,
                controllerControls  = prefs.ControllerControls,
                jeBindings          = prefs.Controls,
                virtualGamepad      = prefs.VirtualGamepad,
                virtualGamepadLefty = prefs.VirtualGamepadLefty,
                core                = prefs.Core,
                vsync               = prefs.Vsync,
                ffrate              = prefs.FfRate,
                smrate              = prefs.SmRate,
                showFps             = prefs.ShowFps
            });
        }

        /// <summary>
        /// Returns explicit raw preferences configured at a specific scope (global, system, game).
        /// Path: GET /jellyemu/prefs/{userId}?scope={global|system|game}&targetId={targetId}
        /// </summary>
        [HttpGet("/jellyemu/prefs/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetScopedPrefs(string userId, [FromQuery] string? scope, [FromQuery] string? targetId)
        {
            var targetScope = string.IsNullOrWhiteSpace(scope) ? "global" : scope;
            var target = targetId ?? string.Empty;
            var prefs = await PreferenceService.GetScopedPreferencesAsync(userId, targetScope, target);

            return Ok(new
            {
                userId,
                scope = targetScope,
                targetId = target,
                preferences = prefs
            });
        }

        /// <summary>
        /// Returns a summary list of all platforms/systems and games that have custom overrides configured.
        /// Path: GET /jellyemu/prefs/{userId}/summary
        /// </summary>
        [HttpGet("/jellyemu/prefs/{userId}/summary")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOverridesSummary(string userId)
        {
            var list = await PreferenceService.GetAllOverridesSummaryAsync(userId);
            return Ok(new
            {
                userId,
                overrides = list
            });
        }

        /// <summary>
        /// Upserts preferences at the specified scope (global, system, or game).
        /// Path: POST /jellyemu/prefs/{userId}
        /// Body can be either:
        ///   1. { "scope": "system", "targetId": "SNES", "preferences": { "shader": "crt-easymode", ... } }
        ///   2. Direct dictionary { "shader": "crt-easymode", ... } (defaults to global scope)
        /// </summary>
        [HttpPost("/jellyemu/prefs/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostPrefs(string userId)
        {
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body)) return BadRequest("Request body cannot be empty.");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string scope = "global";
                string targetId = string.Empty;
                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                if (root.TryGetProperty("preferences", out var prefsProp) && prefsProp.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("scope", out var sProp)) scope = sProp.GetString() ?? "global";
                    if (root.TryGetProperty("targetId", out var tProp)) targetId = tProp.GetString() ?? string.Empty;

                    foreach (var prop in prefsProp.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
                    }
                }
                else
                {
                    // Direct property bag format
                    if (root.TryGetProperty("scope", out var sProp)) scope = sProp.GetString() ?? "global";
                    if (root.TryGetProperty("targetId", out var tProp)) targetId = tProp.GetString() ?? string.Empty;

                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.NameEquals("scope") || prop.NameEquals("targetId")) continue;
                        dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
                    }
                }

                // Handle aliases like jeBindings -> controls
                if (dict.TryGetValue("jeBindings", out var jeVal) && !dict.ContainsKey("controls"))
                {
                    dict["controls"] = jeVal;
                }

                await PreferenceService.SetPreferencesAsync(userId, scope, targetId, dict);
                Logger.LogInformation("[JellyEmu] Preferences saved for user {UserId}, scope={Scope}, target={TargetId}", userId, scope, targetId);

                var updated = await PreferenceService.GetScopedPreferencesAsync(userId, scope, targetId);
                return Ok(new
                {
                    userId,
                    scope,
                    targetId,
                    preferences = updated
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to save preferences for user {UserId}", userId);
                return BadRequest("Invalid JSON payload.");
            }
        }

        /// <summary>
        /// Deletes a specific preference key or all preferences at a scope.
        /// Path: DELETE /jellyemu/prefs/{userId}?scope={system|game}&targetId={targetId}&key={key}
        /// </summary>
        [HttpDelete("/jellyemu/prefs/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> DeletePref(string userId, [FromQuery] string scope, [FromQuery] string targetId, [FromQuery] string? key)
        {
            var success = await PreferenceService.DeletePreferenceAsync(userId, scope, targetId, key);
            return Ok(new
            {
                success,
                userId,
                scope,
                targetId,
                key
            });
        }

        /// <summary>
        /// Completely resets all user preferences and custom overrides back to factory defaults.
        /// Path: DELETE /jellyemu/prefs/{userId}/reset
        /// </summary>
        [HttpDelete("/jellyemu/prefs/{userId}/reset")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPrefs(string userId)
        {
            var success = await PreferenceService.ResetUserPreferencesAsync(userId);
            return Ok(new
            {
                success,
                userId,
                message = "All user preferences and overrides have been reset to factory defaults."
            });
        }
    }
}
