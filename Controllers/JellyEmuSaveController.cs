using System.Net.Mime;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using MediaBrowser.Controller.Entities;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles save-state CRUD, slot management, save listing, and save screenshots.
    /// Routes: /jellyemu/save/*, /jellyemu/slot/*, /jellyemu/saves/*,
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
        /// Path: HEAD /jellyemu/save/{itemId}/{userId}
        /// </summary>
        [HttpHead("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult HeadSave(string itemId, string userId, [FromQuery] int? slot)
        {
            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slotNum);
            return System.IO.File.Exists(path) ? Ok() : NotFound();
        }

        /// <summary>
        /// Downloads the binary save state for a given user/item/slot.
        /// Path: GET /jellyemu/save/{itemId}/{userId}
        /// </summary>
        [HttpGet("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetSave(string itemId, string userId, [FromQuery] int? slot)
        {
            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(path))
            {
                Logger.LogInformation("[JellyEmu] No save found for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NotFound();
            }

            var fileInfo = new System.IO.FileInfo(path);
            Logger.LogInformation("[JellyEmu] Pipeline STAGE 3 (Server Send): Serving save for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                itemId, userId, slotNum, fileInfo.Length);
            var stream = System.IO.File.OpenRead(path);
            return File(stream, "application/octet-stream", $"{itemId}.state");
        }

        /// <summary>
        /// Uploads and stores a binary save state into the active (or specified) slot.
        /// Path: POST /jellyemu/save/{itemId}/{userId}
        /// Body: Raw binary save data.
        /// </summary>
        [HttpPost("/jellyemu/save/{itemId}/{userId}")]
        [DisableRequestSizeLimit]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostSave(string itemId, string userId, [FromQuery] int? slot)
        {
            if (Request.ContentLength is 0)
                return BadRequest("Empty save body.");

            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
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
                    itemId, userId, slotNum, writtenFile.Length);
                System.IO.File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
                throw;
            }
            return Ok();
        }

        /// <summary>
        /// Deletes a save state. Only the authenticated owner may delete their own saves.
        /// Path: DELETE /jellyemu/save/{itemId}/{userId}
        /// </summary>
        [HttpDelete("/jellyemu/save/{itemId}/{userId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult DeleteSave(string itemId, string userId, [FromQuery] int? slot)
        {
            var authenticatedUserId = User.FindFirstValue("Jellyfin-UserId")
                                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(authenticatedUserId, out var authGuid) ||
                !Guid.TryParse(userId, out var targetGuid) ||
                authGuid != targetGuid)
            {
                Logger.LogWarning("[JellyEmu] Unauthorized delete attempt.");
                return Forbid();
            }

            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slotNum);

            if (!System.IO.File.Exists(path))
            {
                Logger.LogInformation("[JellyEmu] Cannot delete: No save found for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NotFound();
            }

            try
            {
                System.IO.File.Delete(path);
                Logger.LogInformation("[JellyEmu] Successfully deleted save for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NoContent();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to delete save file for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Returns the active save slot for a user.
        /// Path: GET /jellyemu/slot/{userId}
        /// </summary>
        [HttpGet("/jellyemu/slot/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetSlot(string userId)
        {
            var prefs = ReadUserPrefs(userId);
            return Ok(new { userId, slot = prefs.Slot });
        }

        /// <summary>
        /// Updates the active save slot for a user (1–99).
        /// Path: POST /jellyemu/slot/{userId}?slot={n}
        /// </summary>
        [HttpPost("/jellyemu/slot/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult SetSlot(string userId, [FromQuery] int slot)
        {
            if (slot < 1 || slot > 99)
                return BadRequest("Slot must be between 1 and 99.");

            var existing = ReadUserPrefs(userId);
            var path = GetSlotFilePath(userId);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
                new { slot, shader = existing.Shader, videoRotation = existing.VideoRotation }));

            Logger.LogInformation("[JellyEmu] User {UserId} slot set — slot:{Slot}", userId, slot);
            return Ok(new { userId, slot });
        }

        /// <summary>
        /// Returns all save states for a user, enriched with library metadata.
        /// Path: GET /jellyemu/saves/{userId}
        /// </summary>
        [HttpGet("/jellyemu/saves/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult ListSaves(string userId)
        {
            var userDir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId);
            if (!Directory.Exists(userDir))
                return Ok(Array.Empty<object>());

            var knownRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
                "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
                "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
            };

            var results = new List<object>();

            foreach (var slotDir in Directory.GetDirectories(userDir, "slot*"))
            {
                var slotName = Path.GetFileName(slotDir);
                if (!int.TryParse(slotName.AsSpan(4), out var slotNumber)) continue;

                foreach (var stateFile in Directory.GetFiles(slotDir, "*.state"))
                {
                    var itemId = Path.GetFileNameWithoutExtension(stateFile);
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
                                    if (tag == "Game") continue;
                                    if (knownRegions.Contains(tag)) { if (string.IsNullOrEmpty(region)) region = tag; }
                                    else                             { if (string.IsNullOrEmpty(platform)) platform = tag; }
                                }
                            }
                        }
                    }
                    catch { /* item may have been removed from library */ }

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

            return Ok(results);
        }

        /// <summary>
        /// Returns the save-state screenshot as JSON { dataUrl: "data:image/png;base64,..." }.
        /// Path: GET /jellyemu/save-screenshot/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpGet("/jellyemu/save-screenshot/{itemId}/{userId}/{slot}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSaveScreenshot(string itemId, string userId, int slot)
        {
            var path = GetSaveScreenshotPath(userId, itemId, slot);
            if (!System.IO.File.Exists(path)) return NotFound();
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(false);
                Response.Headers["Cache-Control"] = "no-cache";
                return Content(json, MediaTypeNames.Application.Json);
            }
            catch { return NotFound(); }
        }

        /// <summary>
        /// Stores a save-state screenshot.
        /// Body: { "dataUrl": "data:image/png;base64,..." }
        /// Path: POST /jellyemu/save-screenshot/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpPost("/jellyemu/save-screenshot/{itemId}/{userId}/{slot}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostSaveScreenshot(string itemId, string userId, int slot)
        {
            try
            {
                var body = await new System.IO.StreamReader(Request.Body).ReadToEndAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var dataUrl = doc.RootElement.TryGetProperty("dataUrl", out var d)
                    ? d.GetString() ?? string.Empty : string.Empty;
                if (!dataUrl.StartsWith("data:image"))
                    return BadRequest("Body must contain a valid dataUrl.");

                var path = GetSaveScreenshotPath(userId, itemId, slot);
                await System.IO.File.WriteAllTextAsync(path,
                    System.Text.Json.JsonSerializer.Serialize(new { dataUrl }),
                    System.Text.Encoding.UTF8).ConfigureAwait(false);

                Logger.LogInformation("[JellyEmu] Saved screenshot for item {ItemId} user {UserId} slot {Slot}",
                    itemId, userId, slot);
                return Ok(new { saved = true });
            }
            catch { return BadRequest("Could not read image data."); }
        }
    }
}
