using System.Net.Mime;
using System.Security.Claims;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using LiteDB;
using System.IO;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles user-level queries and cross-user statistics such as playtime.
    /// Routes: /jellyemu/playtime/*
    /// </summary>
    [ApiController]
    public class JellyEmuPlaytimeController : JellyEmuBaseController
    {
        private readonly IUserManager _userManager;

        public JellyEmuPlaytimeController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPlaytimeController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            IUserManager userManager)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _userManager = userManager;
        }

        /// <summary>
        /// Returns total playtime in seconds and detailed per-game playtime for a single user.
        /// Path: GET /jellyemu/playtime/{userId}
        /// </summary>
        [HttpGet("/jellyemu/playtime/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetUserPlaytime(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var cacheKey = JellyEmuCacheKeys.UserPlaytime(userId);
            if (CacheService.TryGetValue<object>(cacheKey, out var cachedUserPlaytime) && cachedUserPlaytime != null)
            {
                return Ok(cachedUserPlaytime);
            }

            var games = new List<object>();
            long totalSeconds = 0;

            EnsureDatabaseCreated();

            try
            {
                using var db = new LiteDatabase(GetPlaytimeConnectionString());
                var col = db.GetCollection<PlaytimeEntry>("Playtime");
                foreach (var entry in col.Find(x => x.UserId == userId))
                {
                    totalSeconds += entry.Seconds;
                    var item = LibraryManager.GetItemById(entry.ItemId);
                    var itemName = item?.Name ?? "Unknown Game";

                    games.Add(new
                    {
                        itemId = entry.ItemId,
                        itemName,
                        seconds = entry.Seconds
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to query user playtime from LiteDB for user {UserId}", userId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to query database.");
            }

            var result = new
            {
                userId,
                totalSeconds,
                games
            };
            CacheService.Set(cacheKey, (object)result, slidingExpiration: TimeSpan.FromMinutes(30));
            return Ok(result);
        }

        /// <summary>
        /// Returns aggregated total playtime statistics for all users.
        /// Requires admin privileges.
        /// Path: GET /jellyemu/playtime/all/{userId}
        /// </summary>
        [HttpGet("/jellyemu/playtime/all/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetAllUsersPlaytime(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var usersPlaytime = new List<object>();
            long grandTotalSeconds = 0;

            EnsureDatabaseCreated();

            try
            {
                using var db = new LiteDatabase(GetPlaytimeConnectionString());
                var col = db.GetCollection<PlaytimeEntry>("Playtime");
                var allEntries = col.FindAll().ToList();

                foreach (var group in allEntries.GroupBy(x => x.UserId))
                {
                    var userIdStr = group.Key;
                    var totalSeconds = group.Sum(x => x.Seconds);
                    grandTotalSeconds += totalSeconds;

                    string username = "Unknown User";
                    if (Guid.TryParse(userIdStr, out var userGuid))
                    {
                        var user = _userManager.GetUserById(userGuid);
                        if (user != null)
                        {
                            username = user.Username;
                        }
                    }

                    usersPlaytime.Add(new
                    {
                        userId = userIdStr,
                        username,
                        totalSeconds
                    });
                }

                var topGames = allEntries
                    .GroupBy(x => x.ItemId)
                    .Select(g => new { ItemId = g.Key, Seconds = g.Sum(x => x.Seconds) })
                    .OrderByDescending(g => g.Seconds)
                    .Take(10)
                    .Select(g =>
                    {
                        var item = LibraryManager.GetItemById(g.ItemId);
                        return (object)new
                        {
                            itemId = g.ItemId,
                            itemName = item?.Name ?? "Unknown Game",
                            seconds = g.Seconds
                        };
                    })
                    .ToList();

                return Ok(new
                {
                    totalSeconds = grandTotalSeconds,
                    users = usersPlaytime,
                    topGames
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to query all users playtime from LiteDB");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to query database.");
            }
        }

        /// <summary>
        /// Returns the total playtime in seconds for a given user and item.
        /// Path: GET /jellyemu/playtime/{itemId}/{userId}
        /// </summary>
        [HttpGet("/jellyemu/playtime/{itemId}/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetPlaytime(string itemId, string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var cacheKey = JellyEmuCacheKeys.Playtime(itemId, userId);
            if (CacheService.TryGetValue<long>(cacheKey, out var cachedSecs))
            {
                return Ok(new { userId, itemId, seconds = cachedSecs });
            }

            var seconds = ReadPlaytimeSeconds(userId, itemId);
            CacheService.Set(cacheKey, seconds, slidingExpiration: TimeSpan.FromHours(2));
            return Ok(new { userId, itemId, seconds });
        }

        /// <summary>
        /// Adds played seconds to the running total for a given user and item.
        /// Path: POST /jellyemu/playtime/{itemId}/{userId}
        /// Body: Plain integer OR JSON { "seconds": N }
        /// </summary>
        [HttpPost("/jellyemu/playtime/{itemId}/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> PostPlaytime(string itemId, string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

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

            // Write-through update item cache & evict user aggregate cache
            CacheService.Set(JellyEmuCacheKeys.Playtime(itemId, userId), total, slidingExpiration: TimeSpan.FromHours(2));
            CacheService.Evict(JellyEmuCacheKeys.UserPlaytime(userId));

            Logger.LogInformation("[JellyEmu] Playtime +{Seconds}s for item {ItemId} user {UserId} (total {Total}s)",
                seconds, itemId, userId, total);
            return Ok(new { userId, itemId, added = seconds, total });
        }
    }
}
