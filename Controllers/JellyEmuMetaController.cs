using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
using System.Text.Json;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles external metadata fetching e.g. RetroAchievements.
    /// Routes: /jellyemu/meta/*
    /// </summary>
    public class JellyEmuMetaController : JellyEmuBaseController
    {
        public JellyEmuMetaController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuMetaController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Fetches user progress for a specific game from RetroAchievements.
        /// Path: GET /jellyemu/meta/achievements/{itemId}/{userId}
        /// </summary>
        [HttpGet("/jellyemu/meta/achievements/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetAchievements(string itemId, string userId)
        {
            var prefs = ReadFullPrefs(userId);
            if (string.IsNullOrEmpty(prefs.RaUsername) || string.IsNullOrEmpty(prefs.RaApiKey))
            {
                return Unauthorized(new { error = "RetroAchievements credentials not configured." });
            }

            var item = LibraryManager.GetItemById(itemId);
            if (item == null) return NotFound();

            var raGameId = item.GetProviderId("RetroAchievements");
            
            if (string.IsNullOrEmpty(raGameId))
            {
                var md5 = item.GetProviderId("MD5");
                if (!string.IsNullOrEmpty(md5))
                {
                    raGameId = await ResolveRaGameIdFromHash(md5, prefs.RaUsername, prefs.RaApiKey);
                    if (!string.IsNullOrEmpty(raGameId))
                    {
                        item.SetProviderId("RetroAchievements", raGameId);
                    }
                }
            }

            if (string.IsNullOrEmpty(raGameId))
            {
                return NotFound(new { error = "Game not linked to RetroAchievements." });
            }

            try
            {
                var url = $"https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?z={prefs.RaUsername}&y={prefs.RaApiKey}&g={raGameId}&u={prefs.RaUsername}";
                var client = HttpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");

                var response = await client.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode);

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.String)
                {
                    return BadRequest(new { error = root.GetString() });
                }

                var numUnlocked = root.TryGetProperty("NumAwarded", out var na) ? na.GetInt32() : 0;
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
                Logger.LogError(ex, "[JellyEmu] Error fetching RetroAchievements progress for item {ItemId}", itemId);
                return StatusCode(500);
            }
        }

        private async Task<string?> ResolveRaGameIdFromHash(string md5, string user, string key)
        {
            try
            {
                var url = $"https://retroachievements.org/API/API_GetGameID.php?z={user}&y={key}&h={md5}";
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
            }
            catch { }
            return null;
        }
    }
}
