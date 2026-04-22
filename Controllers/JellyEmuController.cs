using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using MediaBrowser.Model.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    [ApiController]
    public class JellyEmuController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuController> _logger;
        private readonly JellyEmuEjsManager _ejsManager;
        private readonly JellyEmuSessionService _sessionService;

        private static readonly System.Collections.Generic.Dictionary<string, string> CoreMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // --- Verified EmulatorJS systems (from https://emulatorjs.org/docs4devs/cores) ---
                { "NES",              "nes"         },
                { "SNES",             "snes"        },
                { "N64",              "n64"         },
                { "Game Boy",         "gb"          },  // gambatte handles both GB and GBC
                { "Game Boy Advance", "gba"         },
                { "Nintendo DS",      "nds"         },
                { "Virtual Boy",      "vb"          },
                { "Master System",    "segaMS"      },
                { "Game Gear",        "segaGG"      },
                { "Sega Genesis",     "segaMD"      },
                { "Sega CD",          "segaCD"      },
                { "Sega 32X",         "sega32x"     },
                { "Sega Saturn",      "segaSaturn"  },  // yabause core
                { "PlayStation",      "psx"         },
                { "PSP",              "psp"         },  // ppsspp core
                { "3DO",              "3do"         },  // opera core
                { "Atari 2600",       "atari2600"   },
                { "Atari 5200",       "a5200"       },
                { "Atari 7800",       "atari7800"   },
                { "Atari Lynx",       "lynx"        },
                { "Atari Jaguar",     "jaguar"      },
                { "WonderSwan",       "ws"          },
                { "TurboGrafx-16",    "pce"         },  // mednafen_pce core; also handles SuperGrafx
                { "PC-FX",            "pcfx"        },  // mednafen_pcfx core
                { "ColecoVision",     "coleco"      },
                { "NeoGeo Pocket",    "ngp"         },
                { "Commodore 64",     "c64"         },  // vice_x64sc core
                { "Commodore 128",    "c128"        },  // vice_x128 core
                { "Commodore Amiga",  "amiga"       },  // puae core
                { "Commodore PET",    "pet"         },  // vice_xpet core
                { "Commodore Plus/4", "plus4"       },  // vice_xplus4 core
                { "Commodore VIC-20", "vic20"       },  // vice_xvic core
                { "Arcade",           "arcade"      },  // fbneo core by default; mame2003 also valid
                { "MAME 2003",        "mame2003"    },  // explicit mame2003 system type
                { "DOS",              "dos"         },  // dosbox_pure core
            };

        public JellyEmuController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService)
        {
            _libraryManager = libraryManager;
            _appPaths = appPaths;
            _logger = logger;
            _ejsManager = ejsManager;
            _sessionService = sessionService;
        }

        // Saves are stored at: {DataPath}/jellyemu-saves/{userId}/slot{slot}/{itemId}.state
        // Active slot preference: {DataPath}/jellyemu-saves/{userId}/active-slot.json
        private string GetSavePath(string userId, string itemId, int slot)
        {
            var dir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId, $"slot{slot}");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{itemId}.state");
        }

        private string GetSlotFilePath(string userId)
        {
            var dir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "active-slot.json");
        }

        // Playtime is stored at: {DataPath}/jellyemu-saves/{userId}/playtime.json
        // Format: { "itemId": totalSeconds, ... }
        private string GetPlaytimePath(string userId)
        {
            var dir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "playtime.json");
        }

        private long ReadPlaytimeSeconds(string userId, string itemId)
        {
            var path = GetPlaytimePath(userId);
            if (!System.IO.File.Exists(path)) return 0;
            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty(itemId, out var v) ? v.GetInt64() : 0;
            }
            catch { return 0; }
        }

        private void AddPlaytimeSeconds(string userId, string itemId, long seconds)
        {
            if (seconds <= 0) return;
            var path = GetPlaytimePath(userId);
            var dict = new System.Collections.Generic.Dictionary<string, long>(StringComparer.Ordinal);
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var existing = System.IO.File.ReadAllText(path);
                    using var doc = System.Text.Json.JsonDocument.Parse(existing);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        dict[prop.Name] = prop.Value.GetInt64();
                }
                catch { /* corrupt file — start fresh */ }
            }
            dict[itemId] = (dict.TryGetValue(itemId, out var current) ? current : 0) + seconds;
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(dict));
        }

        private record UserPrefs(int Slot, string Shader, int VideoRotation);

        private UserPrefs ReadUserPrefs(string userId)
        {
            var path = GetSlotFilePath(userId);
            if (!System.IO.File.Exists(path)) return new UserPrefs(1, string.Empty, 0);
            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var slot = root.TryGetProperty("slot", out var s) ? Math.Max(1, s.GetInt32()) : 1;
                var shader = root.TryGetProperty("shader", out var sh) ? (sh.GetString() ?? string.Empty) : string.Empty;
                var rot = root.TryGetProperty("videoRotation", out var r) ? r.GetInt32() : 0;
                return new UserPrefs(slot, shader, rot);
            }
            catch { return new UserPrefs(1, string.Empty, 0); }
        }

        // Kept for backward-compat internal usage
        private int ReadActiveSlot(string userId) => ReadUserPrefs(userId).Slot;

        /// <summary>
        /// Returns a standalone EmulatorJS HTML page for the given item.
        /// Pass ?userId= so the player can wire up per-user save states.
        /// No authentication required — the ROM is fetched via /jellyemu/rom/{itemId}.
        /// </summary>
        [HttpGet("/jellyemu/play/{itemId}")]
        [Produces(MediaTypeNames.Text.Html)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Play(string itemId, [FromQuery] string? userId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                _logger.LogWarning("[JellyEmu] Play: item {ItemId} not found", itemId);
                return NotFound();
            }

            var core = ResolveCore(item);
            var romUrl = $"/jellyemu/rom/{itemId}";

            var hasSaves = !string.IsNullOrEmpty(userId);
            var userPrefs = hasSaves ? ReadUserPrefs(userId!) : new UserPrefs(1, string.Empty, 0);
            var activeSlot = userPrefs.Slot;
            var activeShader = userPrefs.Shader;
            var videoRotation = userPrefs.VideoRotation;
            var saveGetUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";
            var savePostUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";

            var saveExists = hasSaves && System.IO.File.Exists(GetSavePath(userId!, itemId, activeSlot));

            var igdbId = item.GetProviderId("IGDB");
            var netplayServer = Plugin.Instance?.Configuration.NetplayServer ?? string.Empty;
            var hasNetplay = !string.IsNullOrWhiteSpace(netplayServer);

            var gameName = HtmlEncoder.Default.Encode(item.Name);
            var ejsBase = _ejsManager.IsReady
                ? $"/jellyemu/ejs"
                : JellyEmuEjsManager.CdnBase;

            var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{gameName}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{ width: 100%; height: 100%; background: #000; overflow: hidden; }}
        #game {{ width: 100%; height: 100%; }}
        #exit-btn {{
            position: fixed; top: 20px; left: 20px; z-index: 2147483647;
            background: rgba(0,0,0,0.7); color: #fff;
            border: 1px solid #fff; padding: 10px 20px; font-size: 16px;
            border-radius: 5px; cursor: pointer;
            backdrop-filter: blur(5px);
            display: flex; align-items: center; gap: 8px;
        }}
    </style>
</head>
<body>
    <button id=""exit-btn"" onclick=""(function(){{if(window.EJS_onExit){{EJS_onExit();}}else{{window.parent.postMessage('close-jellyemu','*');}}}})()"">
        <svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""white"">
            <path d=""M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z""/>
        </svg>
        Exit Game
    </button>
    <div id=""game""></div>
    <script>
        window.EJS_player        = '#game';
        window.EJS_core          = '{core}';
        window.EJS_gameUrl       = '{romUrl}';
        window.EJS_gameName      = '{gameName}';
        window.EJS_pathtodata    = '{ejsBase}/';
        window.EJS_startOnLoaded = true;
        window.EJS_askBeforeExit = true;
        window.EJS_color         = '#00a4dc';
        
        // Inject default options for save states, shader and video rotation
        window.EJS_defaultOptions = {{
            'save-state-slot': {activeSlot},
            'save-state-location': 'browser'{(string.IsNullOrEmpty(activeShader) ? "" : $",\n            'shader': '{activeShader}'")}
        }};
        {(videoRotation != 0 ? $"window.EJS_videoRotation = {videoRotation};" : "// EJS_videoRotation: 0 (default, no rotation)")}

        {(!string.IsNullOrEmpty(igdbId) ? $"window.EJS_gameID = {igdbId};" : "")}
        {(hasNetplay ? $@"window.EJS_netplayServer = '{netplayServer}';
        window.EJS_netplayICEServers = [
            {{ urls: 'stun:stun.l.google.com:19302' }},
            {{ urls: 'stun:stun1.l.google.com:19302' }},
            {{ urls: 'stun:stun2.l.google.com:19302' }},
            {{ urls: 'stun:stun.nextcloud.com:3478' }},
            {{ urls: 'turn:openrelay.metered.ca:80',  username: 'openrelayproject', credential: 'openrelayproject' }},
            {{ urls: 'turn:openrelay.metered.ca:443', username: 'openrelayproject', credential: 'openrelayproject' }}
        ];" : "")}

        {(saveExists ? $"window.EJS_loadStateURL = '{saveGetUrl}';" : "")}
        {(hasSaves ? $@"
        // Mark game as played in Jellyfin when the emulator launches
        fetch('/Users/{userId}/PlayedItems/{itemId}', {{
            method: 'POST',
            headers: {{ 'X-Emby-Authorization': 'MediaBrowser Client=""JellyEmu"", Device=""Browser"", DeviceId=""jellyemu"", Version=""1.0""' }}
        }}).catch(function(err) {{
            console.warn('[JellyEmu] Could not mark item as played:', err);
        }});

        // Open a Jellyfin session so the game appears in Dashboard → Active Sessions
        var _jellyEmuDeviceId = 'jellyemu-' + Math.random().toString(36).slice(2, 10);
        fetch('/jellyemu/session/start/{itemId}/{userId}', {{
            method: 'POST',
            headers: {{
                'X-JellyEmu-DeviceId':   _jellyEmuDeviceId,
                'X-JellyEmu-DeviceName': (navigator.userAgent.indexOf('Mobi') !== -1 ? 'JellyEmu Mobile' : 'JellyEmu Browser')
            }}
        }}).catch(function(err) {{
            console.warn('[JellyEmu] Could not open session:', err);
        }});

        // Ping the session every 30 s to keep it alive and advance the elapsed timer
        var _jellyEmuPingInterval = setInterval(function() {{
            fetch('/jellyemu/session/ping/{itemId}/{userId}', {{ method: 'POST' }})
                .catch(function() {{}});
        }}, 30000);

        // Record session start time for playtime tracking
        var _jellyEmuSessionStart = Date.now();

        // Auto-upload save state whenever EmulatorJS writes one
        window.EJS_onSaveState = function(e) {{
            if (!e || !e.state) return;
            fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: e.state
            }}).then(function(r) {{
                console.log('[JellyEmu] Save uploaded, status:', r.status);
            }}).catch(function(err) {{
                console.error('[JellyEmu] Save upload failed:', err);
            }});
        }};
        // Also upload on any incremental save update (e.g. battery saves)
        window.EJS_onSaveUpdate = function(e) {{
            if (!e || !e.save) return;
            fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: e.save
            }}).catch(function(err) {{
                console.error('[JellyEmu] Save update upload failed:', err);
            }});
        }};
        // Auto-save on exit if the user pref is enabled.
        // EJS_onExit is called by both EmulatorJS's own exit menu item and our
        // custom exit button. The _jellyEmuExiting flag prevents double-firing.
        var _jellyEmuExiting = false;
        window.EJS_onExit = function() {{
            if (_jellyEmuExiting) return;
            _jellyEmuExiting = true;

            var prefs = {{}};
            try {{ prefs = JSON.parse(localStorage.getItem('jellyemu-userprefs') || '{{}}'); }} catch(e) {{}}
            var autoSave = prefs.autosave !== 'false'; // default on

            // Stop the session ping and close the Jellyfin session
            clearInterval(_jellyEmuPingInterval);
            var sessionStop = fetch('/jellyemu/session/stop/{itemId}/{userId}', {{ method: 'POST' }})
                .catch(function() {{}});

            // Always record playtime for this session
            var sessionSeconds = Math.round((Date.now() - (_jellyEmuSessionStart || Date.now())) / 1000);
            var playtimeFlush = sessionSeconds > 0
                ? fetch('/jellyemu/playtime/{itemId}/{userId}', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'text/plain' }},
                    body: String(sessionSeconds)
                  }}).catch(function() {{}})
                : Promise.resolve();

            function closeIframe() {{ window.parent.postMessage('close-jellyemu', '*'); }}

            if (!autoSave) {{
                Promise.all([sessionStop, playtimeFlush]).finally(closeIframe);
                return;
            }}
            EJS_emulator.gameManager.saveSaveFiles();
            var stateData = EJS_emulator.gameManager.getSaveFile();
            if (!stateData) {{
                Promise.all([sessionStop, playtimeFlush]).finally(closeIframe);
                return;
            }}
            var saveFlush = fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: stateData
            }}).catch(function() {{}});
            Promise.all([sessionStop, playtimeFlush, saveFlush]).finally(closeIframe);
        }};" : "")}
    </script>
    <script src=""{ejsBase}/loader.js""></script>
</body>
</html>";

            return Content(html, MediaTypeNames.Text.Html);
        }

        /// <summary>
        /// Streams the raw ROM file for the given item directly from disk.
        /// No authentication required.
        /// </summary>
        [HttpGet("/jellyemu/rom/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Rom(string itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path) || !System.IO.File.Exists(item.Path))
            {
                _logger.LogWarning("[JellyEmu] Rom: item {ItemId} not found or path missing", itemId);
                return NotFound();
            }

            _logger.LogInformation("[JellyEmu] Serving ROM: {Path}", item.Path);

            var stream = System.IO.File.OpenRead(item.Path);
            var fileName = Path.GetFileName(item.Path);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        /// <summary>
        /// Returns 200 if a save state exists for the given user/item/slot, 404 otherwise.
        /// Used by the UI save-slot pill to check save presence without downloading the state.
        /// </summary>
        [HttpHead("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult HeadSave(string itemId, string userId)
        {
            var slot = ReadActiveSlot(userId);
            var path = GetSavePath(userId, itemId, slot);
            return System.IO.File.Exists(path) ? Ok() : NotFound();
        }

        /// <summary>
        /// Downloads the save state for a given user and item.
        /// </summary>
        [HttpGet("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetSave(string itemId, string userId, [FromQuery] int? slot)
        {
            var slotNum = slot.HasValue ? slot.Value : ReadActiveSlot(userId);
            var path = GetSavePath(userId, itemId, slotNum);
            if (!System.IO.File.Exists(path))
            {
                _logger.LogInformation("[JellyEmu] No save found for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NotFound();
            }

            _logger.LogInformation("[JellyEmu] Serving save for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
            var stream = System.IO.File.OpenRead(path);
            return File(stream, "application/octet-stream", $"{itemId}.state");
        }

        /// <summary>
        /// Uploads and stores a save state for a given user and item.
        /// Accepts raw bytes in the request body.
        /// </summary>
        [HttpPost("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostSave(string itemId, string userId)
        {
            if (Request.ContentLength == 0 || Request.ContentLength == null)
                return BadRequest("Empty save body.");

            var slot = ReadActiveSlot(userId);
            var path = GetSavePath(userId, itemId, slot);

            using var fs = System.IO.File.Create(path);
            await Request.Body.CopyToAsync(fs);

            _logger.LogInformation("[JellyEmu] Saved state for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                itemId, userId, slot, fs.Length);

            return Ok();
        }

        [HttpGet("/jellyemu/slot/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetSlot(string userId)
        {
            var prefs = ReadUserPrefs(userId);
            return Ok(new { userId, slot = prefs.Slot, shader = prefs.Shader, videoRotation = prefs.VideoRotation });
        }

        [HttpPost("/jellyemu/slot/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult SetSlot(string userId, [FromQuery] int slot, [FromQuery] string? shader, [FromQuery] int? videoRotation)
        {
            if (slot < 1 || slot > 99)
                return BadRequest("Slot must be between 1 and 99.");

            var rotation = videoRotation.HasValue ? Math.Clamp(videoRotation.Value, 0, 3) : ReadUserPrefs(userId).VideoRotation;
            var shaderVal = shader ?? ReadUserPrefs(userId).Shader;

            var path = GetSlotFilePath(userId);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { slot, shader = shaderVal, videoRotation = rotation }));

            _logger.LogInformation("[JellyEmu] User {UserId} prefs set — slot:{Slot} shader:{Shader} rotation:{Rotation}", userId, slot, shaderVal, rotation);
            return Ok(new { userId, slot, shader = shaderVal, videoRotation = rotation });
        }


        /// <summary>
        /// Returns the total playtime in seconds for a given user and item.
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
        /// Body: plain integer (seconds elapsed this session), or JSON { "seconds": N }.
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
            _logger.LogInformation("[JellyEmu] Playtime +{Seconds}s for item {ItemId} user {UserId} (total {Total}s)",
                seconds, itemId, userId, total);
            return Ok(new { userId, itemId, added = seconds, total });
        }

        /// <summary>
        /// Returns all save states for a given user, enriched with game metadata.
        /// Each entry includes: itemId, gameName, platform, region, slot, sizeBytes, lastModified (ISO-8601).
        /// Used by the in-Jellyfin save-state browser.
        /// </summary>
        [HttpGet("/jellyemu/saves/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult ListSaves(string userId)
        {
            var userDir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId);
            if (!Directory.Exists(userDir))
                return Ok(System.Array.Empty<object>());

            var knownRegions = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
                "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
                "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
            };

            var results = new System.Collections.Generic.List<object>();

            foreach (var slotDir in Directory.GetDirectories(userDir, "slot*"))
            {
                var slotName = Path.GetFileName(slotDir); // e.g. "slot2"
                if (!int.TryParse(slotName.AsSpan(4), out var slotNumber)) continue;

                foreach (var stateFile in Directory.GetFiles(slotDir, "*.state"))
                {
                    var itemId = Path.GetFileNameWithoutExtension(stateFile);
                    var fi = new System.IO.FileInfo(stateFile);

                    string gameName = itemId;
                    string platform = string.Empty;
                    string region = string.Empty;
                    bool hasArt = false;

                    try
                    {
                        var item = _libraryManager.GetItemById(itemId);
                        if (item != null)
                        {
                            gameName = item.Name;
                            hasArt = item.HasImage(MediaBrowser.Model.Entities.ImageType.Primary);
                            if (item.Tags != null)
                            {
                                foreach (var tag in item.Tags)
                                {
                                    if (tag == "Game") continue;
                                    if (knownRegions.Contains(tag)) { if (string.IsNullOrEmpty(region)) region = tag; }
                                    else { if (string.IsNullOrEmpty(platform)) platform = tag; }
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
                        slot = slotNumber,
                        sizeBytes = fi.Length,
                        lastModified = fi.LastWriteTimeUtc.ToString("o"),
                        hasArt,
                        downloadUrl = $"/jellyemu/save/{itemId}/{userId}?slot={slotNumber}",
                    });
                }
            }

            // Sort: most recently saved first
            results.Sort((a, b) =>
            {
                var aDate = (string)a.GetType().GetProperty("lastModified")!.GetValue(a)!;
                var bDate = (string)b.GetType().GetProperty("lastModified")!.GetValue(b)!;
                return string.Compare(bDate, aDate, StringComparison.Ordinal);
            });

            return Ok(results);
        }

        /// <summary>
        /// Opens a Jellyfin playback session for the game, making it visible in the
        /// Dashboard → Active Sessions panel exactly like a movie or TV playback.
        /// Called by the player page immediately after the emulator loads.
        /// </summary>
        [HttpPost("/jellyemu/session/start/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SessionStart(string itemId, string userId)
        {
            if (_libraryManager.GetItemById(itemId) == null) return NotFound();

            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var deviceId = Request.Headers["X-JellyEmu-DeviceId"].FirstOrDefault() ?? $"jellyemu-{userId}";
            var deviceName = Request.Headers["X-JellyEmu-DeviceName"].FirstOrDefault() ?? "JellyEmu Browser";

            await _sessionService.StartSessionAsync(userId, itemId, "JellyEmu", deviceId, deviceName, remoteIp)
                .ConfigureAwait(false);

            return Ok(new { started = true, itemId, userId });
        }

        /// <summary>
        /// Keeps the session alive and advances the elapsed-time ticker.
        /// The player page calls this every 30 seconds while the game is running.
        /// </summary>
        [HttpPost("/jellyemu/session/ping/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SessionPing(string itemId, string userId)
        {
            await _sessionService.PingSessionAsync(userId, itemId).ConfigureAwait(false);
            return Ok(new { alive = true });
        }

        /// <summary>
        /// Closes the Jellyfin playback session for the game.
        /// Called from EJS_onExit alongside the playtime flush and auto-save.
        /// </summary>
        [HttpPost("/jellyemu/session/stop/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SessionStop(string itemId, string userId)
        {
            await _sessionService.StopSessionAsync(userId, itemId).ConfigureAwait(false);
            return Ok(new { stopped = true });
        }

        /// <summary>
        /// If the local cache is not ready yet (still downloading or failed),
        /// proxies the request transparently to the CDN so the player always works.
        /// Route: GET /jellyemu/ejs/{*path}  e.g. /jellyemu/ejs/loader.js
        /// </summary>
        [HttpGet("/jellyemu/ejs/{*path}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> EjsAsset(string path,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            if (string.IsNullOrEmpty(path))
                return NotFound();

            path = path.Replace('\\', '/').TrimStart('/');
            if (path.Contains(".."))
                return BadRequest();

            var contentType = path switch
            {
                var p when p.EndsWith(".js", StringComparison.OrdinalIgnoreCase) => "application/javascript",
                var p when p.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) => "application/wasm",
                var p when p.EndsWith(".css", StringComparison.OrdinalIgnoreCase) => "text/css",
                var p when p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) => "application/json",
                var p when p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
                var p when p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) => "image/svg+xml",
                _ => "application/octet-stream"
            };

            Response.Headers["Cache-Control"] = "public, max-age=86400";

            // Local cache
            if (_ejsManager.IsReady)
            {
                var localPath = Path.Combine(_ejsManager.LocalRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(localPath))
                {
                    _logger.LogDebug("[JellyEmu] Serving EJS asset locally: {Path}", path);
                    var stream = System.IO.File.OpenRead(localPath);
                    return File(stream, contentType);
                }

                _logger.LogWarning("[JellyEmu] EJS asset missing from local cache, proxying: {Path}", path);
            }

            // CDN proxy fallback
            var cdnUrl = $"{JellyEmuEjsManager.CdnBase}/{path}";
            _logger.LogDebug("[JellyEmu] Proxying EJS asset from CDN: {Url}", cdnUrl);

            try
            {
                var client = httpClientFactory.CreateClient("JellyEmuEjs");
                using var cdnResponse = await client.GetAsync(cdnUrl, HttpCompletionOption.ResponseHeadersRead);

                if (!cdnResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[JellyEmu] CDN returned {Status} for {Url}",
                        (int)cdnResponse.StatusCode, cdnUrl);
                    return NotFound();
                }

                var bytes = await cdnResponse.Content.ReadAsByteArrayAsync();
                return File(bytes, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to proxy EJS asset from CDN: {Url}", cdnUrl);
                return StatusCode(502);
            }
        }

        private static string ResolveCore(BaseItem item)
        {
            if (item.Tags != null)
            {
                foreach (var tag in item.Tags)
                {
                    if (CoreMap.TryGetValue(tag, out var core))
                        return core;
                }
            }

            // Fallback: guess from file extension
            if (!string.IsNullOrEmpty(item.Path))
            {
                var ext = Path.GetExtension(item.Path).TrimStart('.').ToLowerInvariant();
                var extMap = new System.Collections.Generic.Dictionary<string, string>
                {
                    // NES
                    { "nes",  "nes"        }, { "fds",  "nes"        }, { "unf", "nes"        }, { "unif", "nes"       },
                    // SNES
                    { "smc",  "snes"       }, { "sfc",  "snes"       }, { "swc", "snes"       }, { "fig",  "snes"      },
                    // N64
                    { "z64",  "n64"        }, { "n64",  "n64"        }, { "v64", "n64"        },
                    // Game Boy / GBC — gambatte handles both
                    { "gb",   "gb"         }, { "gbc",  "gb"         },
                    // GBA
                    { "gba",  "gba"        },
                    // NDS
                    { "nds",  "nds"        },
                    // Virtual Boy
                    { "vb",   "vb"         },
                    // Sega
                    { "sms",  "segaMS"     },
                    { "gg",   "segaGG"     },
                    { "md",   "segaMD"     }, { "smd",  "segaMD"     }, { "gen", "segaMD"     }, { "68k",  "segaMD"    },
                    { "32x",  "sega32x"    },
                    // PlayStation (disc formats are ambiguous but psx is the only disc system without a folder hint in most setups)
                    { "pbp",  "psx"        }, { "cue",  "psx"        }, { "chd", "psx"        },
                    // PSP — .cso is unambiguous; .iso reaches here only if the platform tag path was bypassed
                    { "cso",  "psp"        }, { "iso",  "psp"        },
                    // Atari
                    { "a26",  "atari2600"  },
                    { "a78",  "atari7800"  },
                    { "lnx",  "lynx"       },
                    { "jag",  "jaguar"     }, { "j64",  "jaguar"     },
                    // WonderSwan
                    { "ws",   "ws"         }, { "wsc",  "ws"         },
                    // TurboGrafx-16
                    { "pce",  "pce"        },
                    // ColecoVision
                    { "col",  "coleco"     }, { "cv",   "coleco"     },
                    // NeoGeo Pocket
                    { "ngp",  "ngp"        }, { "ngc",  "ngp"        },
                    // Commodore 64 — unambiguous disk/tape/cart formats
                    { "d64",  "c64"        }, { "t64",  "c64"        }, { "crt", "c64"        },
                    { "tap",  "c64"        }, { "prg",  "c64"        },
                    // Amiga
                    { "adf",  "amiga"      }, { "dms",  "amiga"      }, { "ipf", "amiga"      }, { "adz",  "amiga"     },
                    // .zip — intentionally NOT mapped: always needs a folder/tag to know which system.
                    // DOS zips, Arcade ROMs, and Amiga zips are all .zip — the tag "DOS", "Arcade",
                    // or "Commodore Amiga" on the Jellyfin item is the only reliable discriminator.
                };
                if (extMap.TryGetValue(ext, out var extCore))
                    return extCore;
            }

            return "nes"; // last resort fallback
        }
    }
}
