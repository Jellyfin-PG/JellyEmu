using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;

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
                    RaApiKey: newApiKey,
                    VirtualGamepad: current.VirtualGamepad,
                    VirtualGamepadLefty: current.VirtualGamepadLefty);

                WriteFullPrefs(userId, updated);
                return Ok(new { success = true, raUsername = newUsername });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to update RetroAchievements credentials for user {UserId}", userId);
                return BadRequest("Invalid JSON body.");
            }
        }

        /// <summary>
        /// Fetches user progress for a specific game from RetroAchievements.
        /// Path: GET /jellyemu/retroachievements/progress/{itemId}/{userId}
        /// </summary>
        [HttpGet("/jellyemu/retroachievements/progress/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetAchievementsProgress(string itemId, string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] RA: Item with ID {ItemId} not found in LibraryManager.", itemId);
                return NotFound();
            }

            string? raGameId = item.GetProviderId("RetroAchievements");

            if (string.IsNullOrEmpty(raGameId))
            {
                var md5 = item.GetProviderId("MD5");
                if (!string.IsNullOrEmpty(md5))
                {
                    raGameId = await ResolveRaGameIdFromHash(md5);
                    if (!string.IsNullOrEmpty(raGameId))
                    {
                        item.SetProviderId("RetroAchievements", raGameId);
                        await LibraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None);
                    }
                    else
                    {
                        Logger.LogWarning("[JellyEmu] RA: Failed to resolve Game ID from MD5 hash: {Md5}", md5);
                    }
                }
            }

            if (string.IsNullOrEmpty(raGameId))
            {
                Logger.LogWarning("[JellyEmu] RA: No RetroAchievements Game ID could be resolved. Returning 404.");
                return NotFound(new { error = "Game does not support RetroAchievements." });
            }

            var prefs = ReadFullPrefs(userId);

            if (string.IsNullOrEmpty(prefs.RaUsername) || string.IsNullOrEmpty(prefs.RaApiKey))
            {
                Logger.LogWarning("[JellyEmu] RA: Missing credentials for user {UserId}. Returning 401.", userId);
                return Unauthorized(new { error = "RetroAchievements credentials not configured." });
            }

            // Safe fallback response helper
            IActionResult SafeFallback()
            {
                Logger.LogInformation("[JellyEmu] RA: Returning safe fallback response with gameId={GameId}", raGameId);
                return Ok(new
                {
                    gameId = raGameId,
                    gameName = item.Name,
                    numUnlocked = 0,
                    numTotal = 0,
                    progressPercent = 0,
                    lastAwarded = (string?)null,
                    raGameUrl = $"https://retroachievements.org/game/{raGameId}"
                });
            }

            try
            {
                var url = $"https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?z={prefs.RaUsername}&y={prefs.RaApiKey}&g={raGameId}&u={prefs.RaUsername}&a=1";
                var client = HttpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");

                var response = await client.GetAsync(url).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Logger.LogWarning("[JellyEmu] RA: RetroAchievements API returned 401 Unauthorized. User's configured credentials might be invalid.");
                    return Unauthorized(new { error = "RetroAchievements credentials invalid or expired." });
                }

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[JellyEmu] RA: HTTP fetch failed with status {StatusCode}", response.StatusCode);
                    return SafeFallback();
                }

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.String)
                {
                    return SafeFallback();
                }

                var numUnlocked = 0;
                if (root.TryGetProperty("NumAwardedToUser", out var na))
                {
                    numUnlocked = na.GetInt32();
                }
                if (root.TryGetProperty("NumAwardedToUserHardcore", out var nah))
                {
                    numUnlocked = Math.Max(numUnlocked, nah.GetInt32());
                }
                var numTotal = root.TryGetProperty("NumAchievements", out var nt) ? nt.GetInt32() : 0;
                var gameName = root.TryGetProperty("Title", out var t) ? t.GetString() : item.Name;
                var lastAwarded = root.TryGetProperty("LastAwarded", out var la) ? la.GetString() : null;

                return Ok(new
                {
                    gameId = raGameId,
                    gameName,
                    numUnlocked,
                    numTotal,
                    progressPercent = numTotal > 0 ? (int)((double)numUnlocked / numTotal * 100) : 0,
                    lastAwarded,
                    raGameUrl = $"https://retroachievements.org/game/{raGameId}"
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] RA: Error fetching progress for item {ItemId}", itemId);
                return SafeFallback();
            }
        }

        private async Task<string?> ResolveRaGameIdFromHash(string md5)
        {
            try
            {
                var url = $"https://retroachievements.org/dorequest.php?r=gameid&m={md5}";
                var client = HttpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");

                var response = await client.GetAsync(url).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("GameID", out var gid))
                {
                    var id = gid.ValueKind == JsonValueKind.Number ? gid.GetInt32().ToString() : gid.GetString();
                    return id == "0" ? null : id;
                }
                else
                {
                    Logger.LogWarning("[JellyEmu] RA: GameID property not found in hash resolution response.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] RA: Exception in ResolveRaGameIdFromHash for md5 {Md5}", md5);
            }
            return null;
        }
    }
}
