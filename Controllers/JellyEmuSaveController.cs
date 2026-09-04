using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles save-state CRUD, SRAM CRUD, slot management, save listing, and save screenshots.
    /// Routes: /jellyemu/save/*, /jellyemu/sram/*, /jellyemu/slot/*, /jellyemu/saves/*,
    ///         /jellyemu/save-screenshot/*
    /// </summary>
    public class JellyEmuSaveController : JellyEmuBaseController
    {
        public JellyEmuSaveController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuSaveController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Returns 200 if a save state exists for the given user/item/slot, 404 otherwise.
        /// If slot is omitted, returns 200 if a save state exists in any slot for this item.
        /// Path: HEAD /jellyemu/save/{itemId}/{userId}
        /// Path: HEAD /jellyemu/save/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpHead("/jellyemu/save/{itemId}/{userId}")]
        [HttpHead("/jellyemu/save/{itemId}/{userId}/{slotRoute:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult HeadSave(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            var targetSlot = slot ?? slotRoute;
            if (targetSlot.HasValue)
            {
                var path = GetSavePath(userId, itemId, Math.Max(1, targetSlot.Value));
                if (System.IO.File.Exists(path))
                {
                    var lastModified = System.IO.File.GetLastWriteTimeUtc(path);
                    Response.Headers["last-modified"] = lastModified.ToString("R");
                    return Ok();
                }

                return NotFound();
            }

            var userDir = GetSafeUserSavesDir(userId);
            if (Directory.Exists(userDir))
            {
                var slotDirs = Directory.GetDirectories(userDir, "slot*")
                    .Where(d => int.TryParse(Path.GetFileName(d).AsSpan(4), out _))
                    .Select(d => int.Parse(Path.GetFileName(d).AsSpan(4)));

                foreach (var slotNumber in slotDirs)
                {
                    var stateFile = GetSafeSaveFilePath(userId, itemId, slotNumber, "state");
                    if (System.IO.File.Exists(stateFile))
                    {
                        var lastModified = System.IO.File.GetLastWriteTimeUtc(stateFile);
                        Response.Headers["last-modified"] = lastModified.ToString("R");
                        return Ok();
                    }
                }
            }

            return NotFound();
        }

        /// <summary>
        /// Downloads the binary save state for a given user/item/slot.
        /// Path: GET /jellyemu/save/{itemId}/{userId}
        /// Path: GET /jellyemu/save/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpGet("/jellyemu/save/{itemId}/{userId}")]
        [HttpGet("/jellyemu/save/{itemId}/{userId}/{slotRoute:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetSave(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var cacheKey = JellyEmuCacheKeys.Save(itemId, userId, slotNum);
            if (CacheService.TryGetValue<byte[]>(cacheKey, out var cachedBytes) && cachedBytes != null)
            {
                Logger.LogInformation("[JellyEmu] Pipeline STAGE 3 (Server Send): Serving save from cache for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum, cachedBytes.Length);
                return File(cachedBytes, "application/octet-stream", $"{itemId}.state");
            }

            var path = GetSavePath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(path))
            {
                Logger.LogInformation("[JellyEmu] No save found for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NotFound();
            }

            var fileInfo = new System.IO.FileInfo(path);
            Logger.LogInformation("[JellyEmu] Pipeline STAGE 3 (Server Send): Serving save for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                SanitizeForLog(itemId), SanitizeForLog(userId), slotNum, fileInfo.Length);

            if (CacheService.ShouldCacheBinary(fileInfo.Length))
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                CacheService.Set(cacheKey, bytes, slidingExpiration: TimeSpan.FromMinutes(60));
                return File(bytes, "application/octet-stream", $"{itemId}.state");
            }

            var stream = System.IO.File.OpenRead(path);
            return File(stream, "application/octet-stream", $"{itemId}.state");
        }

        /// <summary>
        /// Uploads and stores a binary save state into the specified (or default slot 1).
        /// Path: POST /jellyemu/save/{itemId}/{userId}
        /// Path: POST /jellyemu/save/{itemId}/{userId}/{slot}
        /// Body: Raw binary save data.
        /// </summary>
        [HttpPost("/jellyemu/save/{itemId}/{userId}")]
        [HttpPost("/jellyemu/save/{itemId}/{userId}/{slotRoute:int}")]
        [Authorize]
        [DisableRequestSizeLimit]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> PostSave(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!VerifyUser(userId)) return Forbid();
            if (Request.ContentLength is 0)
                return BadRequest("Empty save body.");

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var path = GetSavePath(userId, itemId, slotNum);
            var tempPath = path + ".tmp";

            try
            {
                using (var fs = System.IO.File.Create(tempPath))
                    await Request.Body.CopyToAsync(fs, HttpContext.RequestAborted);

                var writtenFile = new System.IO.FileInfo(tempPath);
                if (writtenFile.Length < 50)
                {
                    Logger.LogWarning("Save State too small.");
                    System.IO.File.Delete(tempPath);
                    return BadRequest("Save state was empty or corrupt.");
                }

                Logger.LogInformation("[JellyEmu] Pipeline STAGE 2 (Server Receive): Saved state for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum, writtenFile.Length);
                System.IO.File.Move(tempPath, path, overwrite: true);

                // Evict cache entries
                CacheService.Evict(JellyEmuCacheKeys.Save(itemId, userId, slotNum));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(itemId, userId));
                CacheService.Evict(JellyEmuCacheKeys.UserSaves(userId));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to write save state for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
                throw;
            }
            return Ok();
        }

        /// <summary>
        /// Deletes a save state. Only the authenticated owner may delete their own saves.
        /// Path: DELETE /jellyemu/save/{itemId}/{userId}
        /// Path: DELETE /jellyemu/save/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpDelete("/jellyemu/save/{itemId}/{userId}")]
        [HttpDelete("/jellyemu/save/{itemId}/{userId}/{slotRoute:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult DeleteSave(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!VerifyUser(userId))
            {
                Logger.LogWarning("[JellyEmu] Unauthorized delete attempt.");
                return Forbid();
            }

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var path = GetSavePath(userId, itemId, slotNum);

            if (!System.IO.File.Exists(path))
            {
                Logger.LogInformation("[JellyEmu] Cannot delete: No save found for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NotFound();
            }

            try
            {
                System.IO.File.Delete(path);
                CacheService.Evict(JellyEmuCacheKeys.Save(itemId, userId, slotNum));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(itemId, userId));
                CacheService.Evict(JellyEmuCacheKeys.UserSaves(userId));

                Logger.LogInformation("[JellyEmu] Successfully deleted save for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NoContent();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to delete save file for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Returns all save slots that exist for a given item and user.
        /// Path: GET /jellyemu/save-slots/{itemId}/{userId}
        /// Path: GET /jellyemu/save/slots/{itemId}/{userId}
        /// Path: GET /jellyemu/saves/{itemId}/{userId}
        /// </summary>
        [HttpGet("/jellyemu/save-slots/{itemId}/{userId}")]
        [HttpGet("/jellyemu/save/slots/{itemId}/{userId}")]
        [HttpGet("/jellyemu/saves/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult GetItemSaveSlots(string itemId, string userId)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            var cacheKey = JellyEmuCacheKeys.SaveSlots(itemId, userId);
            if (CacheService.TryGetValue<List<object>>(cacheKey, out var cachedSlots) && cachedSlots != null)
            {
                return Ok(cachedSlots);
            }

            var userDir = GetSafeUserSavesDir(userId);
            if (!Directory.Exists(userDir))
            {
                var empty = new List<object>();
                CacheService.Set(cacheKey, empty, slidingExpiration: TimeSpan.FromMinutes(60));
                return Ok(empty);
            }

            var results = new List<object>();
            var slotNumbers = Directory.GetDirectories(userDir, "slot*")
                .Where(d => int.TryParse(Path.GetFileName(d).AsSpan(4), out _))
                .Select(d => int.Parse(Path.GetFileName(d).AsSpan(4)));

            foreach (var slotNumber in slotNumbers)
            {
                var stateFile = GetSafeSaveFilePath(userId, itemId, slotNumber, "state");
                if (System.IO.File.Exists(stateFile))
                {
                    var fi = new System.IO.FileInfo(stateFile);
                    results.Add(new
                    {
                        slot = slotNumber,
                        sizeBytes = fi.Length,
                        lastModified = fi.LastWriteTimeUtc.ToString("o"),
                        hasScreenshot = System.IO.File.Exists(GetSaveScreenshotPath(userId, itemId, slotNumber)),
                        downloadUrl = $"/jellyemu/save/{itemId}/{userId}?slot={slotNumber}"
                    });
                }
            }

            results.Sort((a, b) =>
            {
                var aSlot = (int)a.GetType().GetProperty("slot")!.GetValue(a)!;
                var bSlot = (int)b.GetType().GetProperty("slot")!.GetValue(b)!;
                return aSlot.CompareTo(bSlot);
            });

            CacheService.Set(cacheKey, results, slidingExpiration: TimeSpan.FromMinutes(60));
            return Ok(results);
        }

        /// <summary>
        /// Returns all save states for a user, enriched with library metadata.
        /// Path: GET /jellyemu/saves/{userId}
        /// </summary>
        [HttpGet("/jellyemu/saves/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult ListSaves(string userId)
        {
            if (!IsValidId(userId))
                return BadRequest("Invalid user ID.");

            if (!VerifyUser(userId)) return Forbid();

            var cacheKey = JellyEmuCacheKeys.UserSaves(userId);
            if (CacheService.TryGetValue<List<object>>(cacheKey, out var cachedList) && cachedList != null)
            {
                return Ok(cachedList);
            }

            var userDir = GetSafeUserSavesDir(userId);
            if (!Directory.Exists(userDir))
            {
                var empty = new List<object>();
                CacheService.Set(cacheKey, empty, slidingExpiration: TimeSpan.FromMinutes(30));
                return Ok(empty);
            }

            var knownRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
                "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
                "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
            };

            var results = new List<object>();
            var slotDirs = Directory.GetDirectories(userDir, "slot*")
                .Where(d => int.TryParse(Path.GetFileName(d).AsSpan(4), out _));

            foreach (var slotDir in slotDirs)
            {
                var slotNumber = int.Parse(Path.GetFileName(slotDir).AsSpan(4));

                foreach (var stateFile in Directory.GetFiles(slotDir, "*.state"))
                {
                    var itemId = Path.GetFileNameWithoutExtension(stateFile);
                    if (!IsValidId(itemId)) continue;

                    var fi = new System.IO.FileInfo(stateFile);

                    string gameName = itemId, platform = string.Empty, region = string.Empty;
                    bool hasArt = false;

                    try
                    {
                        var item = LibraryManager.GetItemById(itemId);
                        if (item != null)
                        {
                            gameName = item.Name;
                            hasArt   = item.HasImage(MediaBrowser.Model.Entities.ImageType.Primary);
                            if (item.Tags != null)
                            {
                                foreach (var tag in item.Tags)
                                {
                                    if (tag == "JellyEmu") continue;
                                    if (knownRegions.Contains(tag)) { if (string.IsNullOrEmpty(region)) region = tag; }
                                    else                             { if (string.IsNullOrEmpty(platform)) platform = tag; }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug(ex, "Failed to resolve item metadata for item {ItemId}", SanitizeForLog(itemId));
                    }

                    results.Add(new
                    {
                        itemId,
                        gameName,
                        platform,
                        region,
                        slot         = slotNumber,
                        sizeBytes    = fi.Length,
                        lastModified = fi.LastWriteTimeUtc.ToString("o"),
                        hasArt,
                        hasScreenshot = System.IO.File.Exists(GetSaveScreenshotPath(userId, itemId, slotNumber)),
                        downloadUrl   = $"/jellyemu/save/{itemId}/{userId}?slot={slotNumber}",
                    });
                }
            }

            results.Sort((a, b) =>
            {
                var aDate = (string)a.GetType().GetProperty("lastModified")!.GetValue(a)!;
                var bDate = (string)b.GetType().GetProperty("lastModified")!.GetValue(b)!;
                return string.Compare(bDate, aDate, StringComparison.Ordinal);
            });

            CacheService.Set(cacheKey, results, slidingExpiration: TimeSpan.FromMinutes(30));
            return Ok(results);
        }

        /// <summary>
        /// Returns the save-state screenshot as JSON { dataUrl: "data:image/png;base64,..." }.
        /// Path: GET /jellyemu/save-screenshot/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpGet("/jellyemu/save-screenshot/{itemId}/{userId}/{slot}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSaveScreenshot(string itemId, string userId, int slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            var slotNum = Math.Max(1, slot);
            var cacheKey = JellyEmuCacheKeys.SaveScreenshot(itemId, userId, slotNum);
            if (CacheService.TryGetValue<string>(cacheKey, out var cachedJson) && cachedJson != null)
            {
                Response.Headers["Cache-Control"] = "no-cache";
                return Content(cachedJson, MediaTypeNames.Application.Json);
            }

            var path = GetSaveScreenshotPath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(path)) return NotFound();
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(false);
                CacheService.Set(cacheKey, json, slidingExpiration: TimeSpan.FromMinutes(60));
                Response.Headers["Cache-Control"] = "no-cache";
                return Content(json, MediaTypeNames.Application.Json);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Logger.LogDebug(ex, "Could not read screenshot data for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NotFound();
            }
        }

        /// <summary>
        /// Stores a save-state screenshot.
        /// Body: { "dataUrl": "data:image/png;base64,..." }
        /// Path: POST /jellyemu/save-screenshot/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpPost("/jellyemu/save-screenshot/{itemId}/{userId}/{slot}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> PostSaveScreenshot(string itemId, string userId, int slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!VerifyUser(userId)) return Forbid();
            var slotNum = Math.Max(1, slot);
            try
            {
                using var reader = new System.IO.StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var dataUrl = doc.RootElement.TryGetProperty("dataUrl", out var d)
                    ? d.GetString() ?? string.Empty : string.Empty;
                if (!dataUrl.StartsWith("data:image"))
                    return BadRequest("Body must contain a valid dataUrl.");

                var path = GetSaveScreenshotPath(userId, itemId, slotNum);
                await System.IO.File.WriteAllTextAsync(path,
                    System.Text.Json.JsonSerializer.Serialize(new { dataUrl }),
                    System.Text.Encoding.UTF8).ConfigureAwait(false);

                // Evict cache entries
                CacheService.Evict(JellyEmuCacheKeys.SaveScreenshot(itemId, userId, slotNum));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(itemId, userId));
                CacheService.Evict(JellyEmuCacheKeys.UserSaves(userId));

                Logger.LogInformation("[JellyEmu] Saved screenshot for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return Ok(new { saved = true });
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Logger.LogWarning(ex, "Failed to parse/save screenshot data for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return BadRequest("Could not read image data.");
            }
        }

        /// <summary>
        /// Returns 200 if SRAM data exists for the given user/item/slot, 404 otherwise.
        /// Path: HEAD /jellyemu/sram/{itemId}/{userId}
        /// Path: HEAD /jellyemu/sram/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpHead("/jellyemu/sram/{itemId}/{userId}")]
        [HttpHead("/jellyemu/sram/{itemId}/{userId}/{slotRoute:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult HeadSram(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var path = GetSramPath(userId, itemId, slotNum);

            if (System.IO.File.Exists(path))
            {
                var lastModified = System.IO.File.GetLastWriteTimeUtc(path);
                Response.Headers["last-modified"] = lastModified.ToString("R");
                return Ok();
            }

            return NotFound();
        }

        /// <summary>
        /// Downloads the binary SRAM data for a given user/item/slot.
        /// Path: GET /jellyemu/sram/{itemId}/{userId}
        /// Path: GET /jellyemu/sram/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpGet("/jellyemu/sram/{itemId}/{userId}")]
        [HttpGet("/jellyemu/sram/{itemId}/{userId}/{slotRoute:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetSram(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var cacheKey = JellyEmuCacheKeys.Sram(itemId, userId, slotNum);
            if (CacheService.TryGetValue<byte[]>(cacheKey, out var cachedBytes) && cachedBytes != null)
            {
                Logger.LogInformation("[JellyEmu] Pipeline: Serving SRAM from cache for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum, cachedBytes.Length);
                return File(cachedBytes, "application/octet-stream", $"{itemId}.sav");
            }

            var path = GetSramPath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(path))
            {
                Logger.LogInformation("[JellyEmu] No SRAM found for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NotFound();
            }

            var fileInfo = new System.IO.FileInfo(path);
            Logger.LogInformation("[JellyEmu] Pipeline: Serving SRAM for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                SanitizeForLog(itemId), SanitizeForLog(userId), slotNum, fileInfo.Length);

            if (CacheService.ShouldCacheBinary(fileInfo.Length))
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                CacheService.Set(cacheKey, bytes, slidingExpiration: TimeSpan.FromMinutes(60));
                return File(bytes, "application/octet-stream", $"{itemId}.sav");
            }

            var stream = System.IO.File.OpenRead(path);
            return File(stream, "application/octet-stream", $"{itemId}.sav");
        }

        /// <summary>
        /// Uploads and stores binary SRAM data into the active (or specified) slot.
        /// Path: POST /jellyemu/sram/{itemId}/{userId}
        /// Path: POST /jellyemu/sram/{itemId}/{userId}/{slot}
        /// Body: Raw binary SRAM data.
        /// </summary>
        [HttpPost("/jellyemu/sram/{itemId}/{userId}")]
        [HttpPost("/jellyemu/sram/{itemId}/{userId}/{slotRoute:int}")]
        [Authorize]
        [DisableRequestSizeLimit]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> PostSram(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!VerifyUser(userId)) return Forbid();
            if (Request.ContentLength is 0)
                return BadRequest("Empty SRAM body.");

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var path = GetSramPath(userId, itemId, slotNum);
            var tempPath = path + ".tmp";

            try
            {
                using (var fs = System.IO.File.Create(tempPath))
                    await Request.Body.CopyToAsync(fs, HttpContext.RequestAborted);

                var writtenFile = new System.IO.FileInfo(tempPath);
                if (writtenFile.Length < 8)
                {
                    Logger.LogWarning("SRAM too small.");
                    System.IO.File.Delete(tempPath);
                    return BadRequest("SRAM was empty or corrupt.");
                }

                Logger.LogInformation("[JellyEmu] Pipeline: Saved SRAM for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum, writtenFile.Length);
                System.IO.File.Move(tempPath, path, overwrite: true);

                // Evict cache entries
                CacheService.Evict(JellyEmuCacheKeys.Sram(itemId, userId, slotNum));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(itemId, userId));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to write SRAM for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
                throw;
            }
            return Ok();
        }

        /// <summary>
        /// Deletes SRAM data. Only the authenticated owner may delete their own SRAM.
        /// Path: DELETE /jellyemu/sram/{itemId}/{userId}
        /// Path: DELETE /jellyemu/sram/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpDelete("/jellyemu/sram/{itemId}/{userId}")]
        [HttpDelete("/jellyemu/sram/{itemId}/{userId}/{slotRoute:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult DeleteSram(string itemId, string userId, [FromQuery] int? slot, [FromRoute] int? slotRoute = null)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!VerifyUser(userId))
            {
                Logger.LogWarning("[JellyEmu] Unauthorized SRAM delete attempt.");
                return Forbid();
            }

            var slotNum = Math.Max(1, slot ?? slotRoute ?? 1);
            var path = GetSramPath(userId, itemId, slotNum);

            if (!System.IO.File.Exists(path))
            {
                Logger.LogInformation("[JellyEmu] Cannot delete: No SRAM found for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NotFound();
            }

            try
            {
                System.IO.File.Delete(path);
                CacheService.Evict(JellyEmuCacheKeys.Sram(itemId, userId, slotNum));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(itemId, userId));

                Logger.LogInformation("[JellyEmu] Successfully deleted SRAM for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return NoContent();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to delete SRAM file for item {ItemId} user {UserId} slot {Slot}",
                    SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
