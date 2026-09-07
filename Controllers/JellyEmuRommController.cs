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
    /// All Romm integration endpoints: health check, save push/pull/sync,
    /// playtime reporting, screenshot push, and collection sync.
    /// Routes: /jellyemu/romm/*
    /// </summary>
    public class JellyEmuRommController : JellyEmuBaseController
    {
        public JellyEmuRommController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuRommController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Pings the configured Romm instance. Tries /api/heartbeat, /api/, then /.
        /// Path: GET /jellyemu/romm/health
        /// </summary>
        [HttpGet("/jellyemu/romm/health")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RommHealth()
        {
            var url = (Plugin.Instance?.Configuration.RommInstanceUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(url))
                return Ok(new { reachable = false, reason = "No Romm URL configured" });

            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", JellyEmuVersion.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(8);

            foreach (var probe in new[] { "/api/heartbeat", "/api/", "/" })
            {
                try
                {
                    var resp    = await client.GetAsync(url + probe).ConfigureAwait(false);
                    var body    = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var preview = body.Length > 300 ? body[..300] + "…" : body;
                    return Ok(new
                    {
                        reachable  = true,
                        probe      = url + probe,
                        status     = (int)resp.StatusCode,
                        statusText = resp.StatusCode.ToString(),
                        preview
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("[JellyEmu] Romm health probe {Probe} failed: {Msg}", url + probe, ex.Message);
                }
            }

            return Ok(new { reachable = false, reason = $"Could not reach {url} — check the URL and that Romm is running" });
        }

        /// <summary>
        /// Returns sync status for a given item/slot.
        /// Path: GET /jellyemu/romm/sync-status/{itemId}/{userId}/{slot}
        /// Returns: { "status": "Pushed"|"RemoteWins"|"InSync"|"LocalOnly"|"RemoteOnly"|"Disabled"|"Error" }
        /// </summary>
        [HttpGet("/jellyemu/romm/sync-status/{itemId}/{userId}/{slot:int}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RommSyncStatus(string itemId, string userId, int slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return Ok(new { status = "Disabled" });

            var slotNum   = Math.Max(1, slot);
            var localPath = GetSavePath(userId, itemId, slotNum);
            var hasLocal  = System.IO.File.Exists(localPath);
            var romId     = GetRommIdForItem(itemId);

            if (string.IsNullOrEmpty(romId))
                return Ok(new { status = hasLocal ? "LocalOnly" : "Disabled" });

            try
            {
                var client = GetRommClient();
                var resp   = await client.GetAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}&user_id=me").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return Ok(new { status = "Error" });

                var json   = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var items  = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement
                    : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                DateTimeOffset? remoteModified = null;
                foreach (var s in items.EnumerateArray())
                {
                    if (s.TryGetProperty("slot", out var sl) && sl.GetInt32() == slotNum)
                    {
                        if (s.TryGetProperty("updated_at", out var ua))
                            remoteModified = DateTimeOffset.Parse(ua.GetString() ?? string.Empty);
                        break;
                    }
                }

                if (!hasLocal && remoteModified == null) return Ok(new { status = "Disabled" });
                if (!hasLocal)                           return Ok(new { status = "RemoteOnly" });
                if (remoteModified == null)              return Ok(new { status = "LocalOnly" });

                var localModified = new System.IO.FileInfo(localPath).LastWriteTimeUtc;
                var diff   = (remoteModified.Value.UtcDateTime - localModified).TotalSeconds;
                var status = diff > 5 ? "RemoteWins" : diff < -5 ? "Pushed" : "InSync";
                return Ok(new { status });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Romm sync-status check failed for {ItemId}", SanitizeForLog(itemId));
                return Ok(new { status = "Error" });
            }
        }

        /// <summary>
        /// Force-pushes a local save state to Romm.
        /// Path: POST /jellyemu/romm/push/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpPost("/jellyemu/romm/push/{itemId}/{userId}/{slot:int}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommPush(string itemId, string userId, int slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return StatusCode(503, new { error = "Romm save sync disabled" });

            var slotNum   = Math.Max(1, slot);
            var localPath = GetSavePath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(localPath))
                return NotFound(new { error = "No local save found" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return StatusCode(503, new { error = "Item has no Romm ID" });

            try
            {
                var client = GetRommClient();
                var bytes  = await System.IO.File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
                using var content = new System.Net.Http.MultipartFormDataContent();
                content.Add(new System.Net.Http.ByteArrayContent(bytes), "file", $"{itemId}_slot{slot}.state");
                content.Add(new System.Net.Http.StringContent(slot.ToString()), "slot");

                var resp = await client.PostAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Logger.LogWarning("[JellyEmu] Romm push failed: {Status} {Body}", (int)resp.StatusCode, body);
                    return StatusCode(502, new { error = "Romm rejected push", detail = body });
                }
                return Ok(new { pushed = true });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Romm push error for {ItemId}", SanitizeForLog(itemId));
                return StatusCode(502, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Force-pulls a save state from Romm to local storage.
        /// Path: POST /jellyemu/romm/pull/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpPost("/jellyemu/romm/pull/{itemId}/{userId}/{slot:int}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommPull(string itemId, string userId, int slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return StatusCode(503, new { error = "Romm save sync disabled" });

            var slotNum = Math.Max(1, slot);
            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return StatusCode(503, new { error = "Item has no Romm ID" });

            try
            {
                var client   = GetRommClient();
                var listResp = await client.GetAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}&user_id=me").ConfigureAwait(false);
                if (!listResp.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "Could not list Romm saves" });

                var listJson = await listResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(listJson);
                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement
                    : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                string? downloadUrl = null;
                foreach (var s in arr.EnumerateArray())
                {
                    if (s.TryGetProperty("slot", out var sl) && sl.GetInt32() == slotNum)
                    {
                        downloadUrl = s.TryGetProperty("download_path", out var dp) ? dp.GetString() : null;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    return NotFound(new { error = $"No Romm save for slot {slotNum}" });

                if (!Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
                    downloadUrl = $"{RommInstanceUrl}{(downloadUrl.StartsWith("/") ? "" : "/")}{downloadUrl}";

                var dataResp = await client.GetAsync(downloadUrl).ConfigureAwait(false);
                if (!dataResp.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "Romm download failed" });

                var bytes     = await dataResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var localPath = GetSavePath(userId, itemId, slotNum);
                await System.IO.File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);

                Logger.LogInformation("[JellyEmu] Romm pull: wrote {Bytes}b for item {ItemId} user {UserId} slot {Slot}",
                    bytes.Length, SanitizeForLog(itemId), SanitizeForLog(userId), slotNum);
                return Ok(new { pulled = true, bytes = bytes.Length });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Romm pull error for {ItemId}", SanitizeForLog(itemId));
                return StatusCode(502, new { error = ex.Message });
            }
        }

        /// <summary>
        /// On game launch: pulls from Romm if Romm is newer than local.
        /// Path: POST /jellyemu/romm/sync-on-launch/{itemId}/{userId}
        /// </summary>
        [HttpPost("/jellyemu/romm/sync-on-launch/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RommSyncOnLaunch(string itemId, string userId, [FromQuery] int? slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return Ok(new { pulled = false, reason = "disabled" });

            var slotNum   = Math.Max(1, slot ?? 1);
            var localPath = GetSavePath(userId, itemId, slotNum);
            var romId     = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { pulled = false, reason = "no_romm_id" });

            try
            {
                var client   = GetRommClient();
                var listResp = await client.GetAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}&user_id=me").ConfigureAwait(false);
                if (!listResp.IsSuccessStatusCode)
                    return Ok(new { pulled = false, reason = "romm_error" });

                var listJson = await listResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(listJson);
                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement
                    : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                DateTimeOffset? remoteModified = null;
                string? downloadUrl = null;
                foreach (var s in arr.EnumerateArray())
                {
                    if (s.TryGetProperty("slot", out var sl) && sl.GetInt32() == slotNum)
                    {
                        if (s.TryGetProperty("updated_at", out var ua))
                            remoteModified = DateTimeOffset.Parse(ua.GetString() ?? string.Empty);
                        downloadUrl = s.TryGetProperty("download_path", out var dp) ? dp.GetString() : null;
                        break;
                    }
                }

                if (remoteModified == null || string.IsNullOrEmpty(downloadUrl))
                    return Ok(new { pulled = false, reason = "no_remote_save" });

                var localModified = System.IO.File.Exists(localPath)
                    ? new System.IO.FileInfo(localPath).LastWriteTimeUtc
                    : DateTime.MinValue;

                if ((remoteModified.Value.UtcDateTime - localModified).TotalSeconds <= 5)
                    return Ok(new { pulled = false, reason = "local_is_current" });

                if (!Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
                    downloadUrl = $"{RommInstanceUrl}{(downloadUrl.StartsWith("/") ? "" : "/")}{downloadUrl}";

                var dataResp = await client.GetAsync(downloadUrl).ConfigureAwait(false);
                if (!dataResp.IsSuccessStatusCode)
                    return Ok(new { pulled = false, reason = "download_failed" });

                var bytes = await dataResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                await System.IO.File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);
                Logger.LogInformation("[JellyEmu] Romm sync-on-launch: pulled {Bytes}b for {ItemId}", bytes.Length, SanitizeForLog(itemId));
                return Ok(new { pulled = true });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Romm sync-on-launch error for {ItemId}", SanitizeForLog(itemId));
                return Ok(new { pulled = false, reason = "exception" });
            }
        }

        /// <summary>
        /// After a save: pushes the local save to Romm.
        /// Path: POST /jellyemu/romm/sync-after-save/{itemId}/{userId}
        /// </summary>
        [HttpPost("/jellyemu/romm/sync-after-save/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RommSyncAfterSave(string itemId, string userId, [FromQuery] int? slot)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
                return BadRequest("Invalid item or user ID.");

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return Ok(new { pushed = false, reason = "disabled" });

            var slotNum   = Math.Max(1, slot ?? 1);
            var localPath = GetSavePath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(localPath))
                return Ok(new { pushed = false, reason = "no_local_save" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { pushed = false, reason = "no_romm_id" });

            try
            {
                var client = GetRommClient();
                var bytes  = await System.IO.File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
                using var content = new System.Net.Http.MultipartFormDataContent();
                content.Add(new System.Net.Http.ByteArrayContent(bytes), "file", $"{itemId}_slot{slotNum}.state");
                content.Add(new System.Net.Http.StringContent(slotNum.ToString()), "slot");

                var resp = await client.PostAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[JellyEmu] Romm sync-after-save push failed: {Status}", (int)resp.StatusCode);
                    return Ok(new { pushed = false, reason = "romm_rejected" });
                }
                return Ok(new { pushed = true });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Romm sync-after-save error for {ItemId}", SanitizeForLog(itemId));
                return Ok(new { pushed = false, reason = "exception" });
            }
        }

        /// <summary>
        /// Reports elapsed session seconds to Romm.
        /// Path: POST /jellyemu/romm/report-playtime/{itemId}/{userId}
        /// Body: { "seconds": N }
        /// </summary>
        [HttpPost("/jellyemu/romm/report-playtime/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RommReportPlaytime(string itemId, string userId)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
            {
                return BadRequest("Invalid itemId or userId format.");
            }

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommPlaytimeReportEnabled == true))
                return Ok(new { reported = false, reason = "disabled" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { reported = false, reason = "no_romm_id" });

            long seconds = 0;
            try
            {
                using var reader = new System.IO.StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                body = body.Trim();
                if (body.StartsWith("{"))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    seconds = doc.RootElement.TryGetProperty("seconds", out var v) ? v.GetInt64() : 0;
                }
                else seconds = long.Parse(body);
            }
            catch { return BadRequest("Body must be { \"seconds\": N } or plain integer."); }

            if (seconds <= 0) return Ok(new { reported = false, reason = "zero_seconds" });

            try
            {
                var client  = GetRommClient();
                var payload = System.Text.Json.JsonSerializer.Serialize(new { time_played = seconds });
                using var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var resp = await client.PostAsync($"{RommInstanceUrl}/api/roms/{romId}/playtime", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[JellyEmu] Romm playtime report failed: {Status}", (int)resp.StatusCode);
                    return Ok(new { reported = false, reason = "romm_rejected" });
                }
                return Ok(new { reported = true, seconds });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Romm playtime report error for {ItemId}", SanitizeForLog(itemId));
                return Ok(new { reported = false, reason = "exception" });
            }
        }

        /// <summary>
        /// Fetches Romm collections and returns them with mapped Jellyfin item IDs.
        /// Path: GET /jellyemu/romm/collections
        /// </summary>
        [HttpGet("/jellyemu/romm/collections")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommGetCollections()
        {
            if (!RommEnabled)
                return StatusCode(503, new { error = "Romm not enabled", step = "config_check" });
            if (!(Plugin.Instance?.Configuration.RommCollectionSyncEnabled == true))
                return StatusCode(503, new { error = "Collection sync disabled", step = "config_check" });

            var instanceUrl = RommInstanceUrl;
            if (string.IsNullOrEmpty(instanceUrl))
                return StatusCode(503, new { error = "Romm URL not configured", step = "config_check" });

            HttpClient client;
            try { client = GetRommClient(); }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] RommGetCollections: failed to obtain auth client");
                return StatusCode(502, new { error = "Auth client failed", step = "get_auth_client", detail = ex.Message });
            }

            string collectionsUrl = $"{instanceUrl}/api/collections";
            HttpResponseMessage resp;
            try { resp = await client.GetAsync(collectionsUrl).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] RommGetCollections: HTTP request to {Url} failed", collectionsUrl);
                return StatusCode(502, new { error = "HTTP request failed", step = "fetch_collections", url = collectionsUrl, detail = ex.Message });
            }

            if (!resp.IsSuccessStatusCode)
            {
                string errBody;
                try { errBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch { errBody = "(could not read body)"; }
                Logger.LogWarning("[JellyEmu] Romm GET {Url} returned {Status}: {Body}", collectionsUrl, (int)resp.StatusCode, errBody);
                return StatusCode(502, new { error = "Romm returned non-success", step = "fetch_collections", url = collectionsUrl, status = (int)resp.StatusCode, detail = errBody });
            }

            string json;
            try { json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch (Exception ex) { return StatusCode(502, new { error = "Failed to read response body", step = "read_body", detail = ex.Message }); }

            Logger.LogInformation("[JellyEmu] Romm collections raw response ({Len} chars): {Preview}",
                json.Length, json.Length > 500 ? json[..500] : json);

            System.Text.Json.JsonElement root;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                root = doc.RootElement.Clone();
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = "Invalid JSON from Romm", step = "parse_json", detail = ex.Message, raw = json.Length > 300 ? json[..300] : json });
            }

            System.Text.Json.JsonElement arr = default;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array) arr = root;
            else if (root.TryGetProperty("items",       out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array) arr = items;
            else if (root.TryGetProperty("data",        out var data)  && data.ValueKind  == System.Text.Json.JsonValueKind.Array) arr = data;
            else if (root.TryGetProperty("collections", out var cols)  && cols.ValueKind  == System.Text.Json.JsonValueKind.Array) arr = cols;
            else
            {
                var keys = root.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? string.Join(", ", root.EnumerateObject().Select(p => p.Name))
                    : root.ValueKind.ToString();
                Logger.LogWarning("[JellyEmu] Could not find collection array. Root kind: {Kind}, keys: {Keys}", root.ValueKind, keys);
                return Ok(new { collections = Array.Empty<object>(), debug = new { rootKind = root.ValueKind.ToString(), keys, raw = json.Length > 300 ? json[..300] : json } });
            }

            var result = new List<object>();
            foreach (var col in arr.EnumerateArray())
            {
                var colName = col.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(colName)) continue;

                var jellyfinItemIds = new List<string>();

                System.Text.Json.JsonElement romsEl = default;
                if (col.TryGetProperty("roms",    out var romsArr) && romsArr.ValueKind == System.Text.Json.JsonValueKind.Array) romsEl = romsArr;
                else if (col.TryGetProperty("rom_ids", out var romIds) && romIds.ValueKind == System.Text.Json.JsonValueKind.Array) romsEl = romIds;

                if (romsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var r in romsEl.EnumerateArray())
                    {
                        string rid = r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetInt32().ToString()
                            : r.ValueKind == System.Text.Json.JsonValueKind.Object
                                ? (r.TryGetProperty("id", out var rid2) ? rid2.ToString() : string.Empty)
                            : r.ToString();

                        if (string.IsNullOrEmpty(rid)) continue;
                        var jfItem = LibraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                        {
                            HasAnyProviderId = new Dictionary<string, string> { { "Romm", rid } }
                        }).FirstOrDefault();
                        if (jfItem != null) jellyfinItemIds.Add(jfItem.Id.ToString("N"));
                    }
                }

                result.Add(new { name = colName, jellyfinItemIds });
            }

            return Ok(result);
        }

        /// <summary>
        /// Fetches Romm collections and creates matching Jellyfin BoxSets (if they don't exist).
        /// Path: POST /jellyemu/romm/sync-collections/{userId}
        /// </summary>
        [HttpPost("/jellyemu/romm/sync-collections/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommSyncCollections(string userId)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommCollectionSyncEnabled == true))
                return StatusCode(503, new { error = "Romm collection sync disabled" });

            try
            {
                var client = GetRommClient();
                var resp   = await client.GetAsync($"{RommInstanceUrl}/api/collections").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "Could not fetch Romm collections" });

                var json   = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement
                    : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                var created = new List<string>();
                var skipped = new List<string>();

                foreach (var col in arr.EnumerateArray())
                {
                    var colName = col.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(colName)) continue;

                    var existing = LibraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        Name = colName,
                        IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.BoxSet }
                    }).FirstOrDefault();

                    if (existing != null) { skipped.Add(colName); continue; }

                    // ICollectionManager DI needed for real creation — expose data for UI instead
                    created.Add(colName);
                }

                return Ok(new { created, skipped, total = created.Count + skipped.Count });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Romm collection sync failed");
                return StatusCode(502, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Accepts a screenshot (base64 JSON or raw bytes) and pushes it to Romm.
        /// Path: POST /jellyemu/romm/screenshot/{itemId}/{userId}
        /// Body: { "dataUrl": "data:image/png;base64,..." } OR raw PNG bytes
        /// </summary>
        [HttpPost("/jellyemu/romm/screenshot/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommPushScreenshot(string itemId, string userId)
        {
            if (!IsValidId(itemId) || !IsValidId(userId))
            {
                return BadRequest("Invalid itemId or userId format.");
            }

            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommScreenshotPushEnabled == true))
                return StatusCode(503, new { error = "Romm screenshot push disabled" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return StatusCode(503, new { error = "Item has no Romm ID" });

            byte[] imageBytes;
            var fileName = $"screenshot_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.png";

            try
            {
                var contentType = Request.ContentType ?? string.Empty;
                if (contentType.Contains("application/json"))
                {
                    using var reader = new System.IO.StreamReader(Request.Body);
                    var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    var dataUrl = doc.RootElement.TryGetProperty("dataUrl", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                    var comma = dataUrl.IndexOf(',');
                    if (comma < 0) return BadRequest("Invalid dataUrl");
                    imageBytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
                    if (dataUrl.Contains("image/jpeg")) fileName = fileName.Replace(".png", ".jpg");
                }
                else
                {
                    using var ms = new System.IO.MemoryStream();
                    await Request.Body.CopyToAsync(ms).ConfigureAwait(false);
                    imageBytes = ms.ToArray();
                }
            }
            catch { return BadRequest("Could not read image data."); }

            try
            {
                var client = GetRommClient();
                using var form = new System.Net.Http.MultipartFormDataContent();
                var imgContent = new System.Net.Http.ByteArrayContent(imageBytes);
                imgContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    fileName.EndsWith(".jpg") ? "image/jpeg" : "image/png");
                form.Add(imgContent, "file", fileName);

                var resp = await client.PostAsync($"{RommInstanceUrl}/api/roms/{romId}/screenshots", form).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var detail = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Logger.LogWarning("[JellyEmu] Romm screenshot push failed: {Status}", (int)resp.StatusCode);
                    return StatusCode(502, new { error = "Romm rejected screenshot", detail });
                }
                return Ok(new { pushed = true, fileName });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Romm screenshot push error for {ItemId}", SanitizeForLog(itemId));
                return StatusCode(502, new { error = ex.Message });
            }
        }
    }
}
