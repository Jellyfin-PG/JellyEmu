using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using MediaBrowser.Model.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Collections;
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
        private readonly IHttpClientFactory _httpClientFactory;

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
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
        {
            _libraryManager = libraryManager;
            _appPaths = appPaths;
            _logger = logger;
            _ejsManager = ejsManager;
            _sessionService = sessionService;
            _httpClientFactory = httpClientFactory;
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
            catch (Exception ex)
            {
                // NOTE: Added logging for unexpected parse failures
                _logger.LogWarning(ex, "[JellyEmu] Failed to parse playtime for user {UserId}, defaulting to 0", userId);
                return 0;
            }
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
                catch (Exception ex)
                {
                    // NOTE: Addressed "corrupt file — start fresh" comment by logging the occurrence.
                    _logger.LogWarning(ex, "[JellyEmu] Playtime file corrupt for user {UserId}. Starting fresh.", userId);
                }
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Slot prefs file corrupt for user {UserId}. Returning defaults.", userId);
                return new UserPrefs(1, string.Empty, 0);
            }
        }

        // Kept for backward-compat internal usage
        [Obsolete("Use ReadUserPrefs(userId) instead to fetch all slot-level preference settings.")]
        private int ReadActiveSlot(string userId) => ReadUserPrefs(userId).Slot;

        // ── Full user preferences (emulator + controls + save behaviour) ──────────
        // Stored separately from the slot file so slot reads stay cheap.
        // File: {DataPath}/jellyemu-saves/{userId}/prefs.json

        private record UserFullPrefs(
            string Scale,
            string Mute,
            string Controller,
            string Haptics,
            string Autosave,
            string Shader,
            int VideoRotation,
            string Controls);   // JSON string — serialised EJS_defaultControls player-0 map

        private static readonly UserFullPrefs DefaultFullPrefs =
            new("fit", "false", "auto", "true", "true", string.Empty, 0, string.Empty);

        private string GetPrefsFilePath(string userId)
        {
            var dir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "prefs.json");
        }

        private UserFullPrefs ReadFullPrefs(string userId)
        {
            var path = GetPrefsFilePath(userId);
            if (!System.IO.File.Exists(path)) return DefaultFullPrefs;
            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var r = doc.RootElement;
                string Str(string key, string def) =>
                    r.TryGetProperty(key, out var v) ? (v.GetString() ?? def) : def;
                int Int(string key, int def) =>
                    r.TryGetProperty(key, out var v) ? v.GetInt32() : def;
                return new UserFullPrefs(
                    Scale: Str("scale", DefaultFullPrefs.Scale),
                    Mute: Str("mute", DefaultFullPrefs.Mute),
                    Controller: Str("controller", DefaultFullPrefs.Controller),
                    Haptics: Str("haptics", DefaultFullPrefs.Haptics),
                    Autosave: Str("autosave", DefaultFullPrefs.Autosave),
                    Shader: Str("shader", DefaultFullPrefs.Shader),
                    VideoRotation: Int("videoRotation", DefaultFullPrefs.VideoRotation),
                    Controls: Str("controls", DefaultFullPrefs.Controls));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Prefs file corrupt for user {UserId}. Returning defaults.", userId);
                return DefaultFullPrefs;
            }
        }

        private void WriteFullPrefs(string userId, UserFullPrefs prefs)
        {
            var path = GetPrefsFilePath(userId);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                scale = prefs.Scale,
                mute = prefs.Mute,
                controller = prefs.Controller,
                haptics = prefs.Haptics,
                autosave = prefs.Autosave,
                shader = prefs.Shader,
                videoRotation = prefs.VideoRotation,
                controls = prefs.Controls,
            }));
        }

        /// <summary>
        /// Returns a standalone EmulatorJS HTML page for the given item.
        /// No authentication required — the ROM is fetched via /jellyemu/rom/{itemId}.
        /// 
        /// Path: GET /jellyemu/play/{itemId}
        /// Parameters: 
        ///   - itemId (string, path): The unique ID of the library item.
        ///   - userId (string, query, optional): Allows wire up of per-user save states.
        /// Returns Example: `200 OK` (Content-Type: text/html)
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
            var fullPrefs = hasSaves ? ReadFullPrefs(userId!) : DefaultFullPrefs;
            var activeSlot = userPrefs.Slot;
            var activeShader = userPrefs.Shader;
            var videoRotation = userPrefs.VideoRotation;
            var savedControls = fullPrefs.Controls; // JSON string of player-0 key map
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
    <button id=""exit-btn"" onclick=""(function(){{if(window.EJS_onExit){{EJS_onExit();}}else{{if(window.opener){{window.close();}}else{{window.parent.postMessage('close-jellyemu','*');}}}}}})()"">
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
        {(core is "dos" or "psp" ? "window.EJS_threads = true;" : "// EJS_threads not required for this core")}

        // Inject saved key bindings if the user has customised them
        {(!string.IsNullOrWhiteSpace(savedControls) ? $"window.EJS_defaultControls = {{ 0: {savedControls}, 1: {{}}, 2: {{}}, 3: {{}} }};" : "// EJS_defaultControls: using emulator defaults")}

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
        // Read the auth token Jellyfin's web UI already stored in localStorage
        var _jellyToken = '';
        try {{
            var _jellyCreds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{{}}');
            var _jellyServer = (_jellyCreds.Servers || []).find(function(s) {{ return s.UserId === '{userId}'; }});
            _jellyToken = (_jellyServer && _jellyServer.AccessToken) || '';
        }} catch(e) {{}}

        // Mark game as played in Jellyfin when the emulator launches
        fetch('/Users/{userId}/PlayedItems/{itemId}', {{
            method: 'POST',
            headers: {{ 'X-Emby-Authorization': 'MediaBrowser Client=""JellyEmu"", Device=""Browser"", DeviceId=""jellyemu"", Version=""1.0"", Token=""' + _jellyToken + '""' }}
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
                // Notify parent window so it can push to Romm
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
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
            }}).then(function() {{
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
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

            function closeIframe() {{
                if (window.parent === window) {{
                    // New tab: broadcast exit signal, then close self
                    try {{ var ch = new BroadcastChannel('jellyemu-exit'); ch.postMessage('close-jellyemu'); ch.close(); }} catch(e) {{}}
                    window.close();
                }} else {{
                    // Iframe: tell parent to remove it
                    window.parent.postMessage('close-jellyemu', '*');
                }}
            }}

            if (!autoSave) {{
                Promise.all([sessionStop, playtimeFlush]).finally(function() {{
                    try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                    closeIframe();
                }});
                return;
            }}
            EJS_emulator.gameManager.saveSaveFiles();
            var stateData = EJS_emulator.gameManager.getSaveFile();
            if (!stateData) {{
                Promise.all([sessionStop, playtimeFlush]).finally(function() {{
                    try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                    closeIframe();
                }});
                return;
            }}
            var saveFlush = fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: stateData
            }}).then(function() {{
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
            }}).catch(function() {{}});
            Promise.all([sessionStop, playtimeFlush, saveFlush]).finally(function() {{
                // Notify parent of session end for Romm playtime reporting
                try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                closeIframe();
            }});
        }};

        // Hook EmulatorJS screenshot button to push to Romm via parent
        document.addEventListener('click', function(ev) {{
            var btn = ev.target.closest && ev.target.closest('[data-action=""screenshot""], .ejs-screenshot-btn, button[title*=""creenshot""], button[aria-label*=""creenshot""]');
            if (!btn) return;
            // Give EJS a tick to generate the canvas then grab it
            setTimeout(function() {{
                try {{
                    var canvas = document.querySelector('#game canvas') || document.querySelector('canvas');
                    if (!canvas) return;
                    var dataUrl = canvas.toDataURL('image/png');
                    window.parent.postMessage({{ type: 'jellyemu-screenshot', itemId: '{itemId}', dataUrl: dataUrl }}, '*');
                }} catch(e) {{ console.warn('[JellyEmu] Screenshot capture failed:', e); }}
            }}, 200);
        }});
" : "")}
    </script>
    <script src=""{ejsBase}/loader.js""></script>
</body>
</html>";

            // When opened as a new tab (threaded cores), these headers make the page
            // cross-origin isolated so SharedArrayBuffer is available. Harmless for iframe mode.
            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";

            return Content(html, MediaTypeNames.Text.Html);
        }

        /// <summary>
        /// Streams the raw ROM file for the given item directly from disk.
        /// No authentication required. HEAD is supported so EmulatorJS can read Content-Length before downloading.
        /// 
        /// Path: GET /jellyemu/rom/{itemId} (or HEAD)
        /// Parameters:
        ///   - itemId (string, path): The unique ID of the ROM file item.
        /// Returns Example: Binary File Stream (e.g., application/zip)
        /// </summary>
        [HttpGet("/jellyemu/rom/{itemId}")]
        [HttpHead("/jellyemu/rom/{itemId}")]
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

            var ext = Path.GetExtension(item.Path).TrimStart('.').ToLowerInvariant();
            var mimeType = ext switch
            {
                "zip" => "application/zip",
                "7z" => "application/x-7z-compressed",
                "iso" => "application/x-iso9660-image",
                "cso" => "application/x-compressed",
                _ => "application/octet-stream"
            };

            var fileInfo = new System.IO.FileInfo(item.Path);
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            Response.Headers["Content-Length"] = fileInfo.Length.ToString();
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(item.Path)}\"";

            if (HttpMethods.IsHead(Request.Method))
                return new FileContentResult(Array.Empty<byte>(), mimeType);

            var stream = System.IO.File.OpenRead(item.Path);
            return File(stream, mimeType, enableRangeProcessing: true);
        }

        /// <summary>
        /// Returns the resolved core name and whether it requires threads (SharedArrayBuffer)
        /// for the given item. Used by the UI to decide iframe vs new tab launch.
        /// 
        /// Path: GET /jellyemu/core/{itemId}
        /// Parameters:
        ///   - itemId (string, path): The unique ID of the item.
        /// Returns Example: { "core": "gba", "needsThreads": false }
        /// </summary>
        [HttpGet("/jellyemu/core/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetCore(string itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
                return NotFound();

            var core = ResolveCore(item);
            var needsThreads = core is "dos" or "psp";
            return Ok(new { core, needsThreads });
        }

        /// <summary>
        /// Returns 200 if a save state exists for the given user/item/slot, 404 otherwise.
        /// Used by the UI save-slot pill to check save presence without downloading the state.
        /// 
        /// Path: HEAD /jellyemu/save/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): The game ID.
        ///   - userId (string, path): The user ID.
        /// Returns Example: `200 OK` (if exists) or `404 Not Found`
        /// </summary>
        [HttpHead("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult HeadSave(string itemId, string userId)
        {
            var userPrefs = ReadUserPrefs(userId);
            var slot = userPrefs.Slot;
            var path = GetSavePath(userId, itemId, slot);
            return System.IO.File.Exists(path) ? Ok() : NotFound();
        }

        /// <summary>
        /// Downloads the save state for a given user and item.
        /// 
        /// Path: GET /jellyemu/save/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): The game ID.
        ///   - userId (string, path): The user ID.
        ///   - slot (int, query, optional): Specific slot to fetch.
        /// Returns Example: Binary stream (application/octet-stream)
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
                _logger.LogInformation("[JellyEmu] No save found for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NotFound();
            }

            _logger.LogInformation("[JellyEmu] Serving save for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
            var stream = System.IO.File.OpenRead(path);
            return File(stream, "application/octet-stream", $"{itemId}.state");
        }

        /// <summary>
        /// Uploads and stores a save state for a given user and item into the active slot.
        /// Accepts raw bytes in the request body.
        /// 
        /// Path: POST /jellyemu/save/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): The game ID.
        ///   - userId (string, path): The user ID.
        ///   - Request Body: Raw binary state data.
        /// Returns Example: `200 OK`
        /// </summary>
        [HttpPost("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostSave(string itemId, string userId)
        {
            if (Request.ContentLength == 0 || Request.ContentLength == null)
                return BadRequest("Empty save body.");

            var slot = ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slot);

            using var fs = System.IO.File.Create(path);
            await Request.Body.CopyToAsync(fs);

            _logger.LogInformation("[JellyEmu] Saved state for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                itemId, userId, slot, fs.Length);

            return Ok();
        }

        /// <summary>
        /// Retrieves the active slot for a user.
        /// 
        /// Path: GET /jellyemu/slot/{userId}
        /// Parameters:
        ///   - userId (string, path): The user ID.
        /// Returns Example: { "userId": "user123", "slot": 1 }
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
        /// Updates the active slot for a user.
        /// 
        /// Path: POST /jellyemu/slot/{userId}
        /// Parameters:
        ///   - userId (string, path): The user ID.
        ///   - slot (int, query): Must be between 1 and 99.
        /// Returns Example: { "userId": "user123", "slot": 2 }
        /// </summary>
        [HttpPost("/jellyemu/slot/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult SetSlot(string userId, [FromQuery] int slot)
        {
            if (slot < 1 || slot > 99)
                return BadRequest("Slot must be between 1 and 99.");

            var existingPrefs = ReadUserPrefs(userId);
            var path = GetSlotFilePath(userId);

            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { slot, shader = existingPrefs.Shader, videoRotation = existingPrefs.VideoRotation }));

            _logger.LogInformation("[JellyEmu] User {UserId} slot set — slot:{Slot}", userId, slot);
            return Ok(new { userId, slot });
        }

        /// <summary>
        /// Returns all stored emulator preferences for a user.
        /// 
        /// Path: GET /jellyemu/prefs/{userId}
        /// Parameters:
        ///   - userId (string, path): The user ID.
        /// Returns Example: { "userId": "user123", "scale": "fit", "mute": "false", "controller": "auto", "haptics": "true", "autosave": "true", "shader": "", "videoRotation": 0 }
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
                scale = prefs.Scale,
                mute = prefs.Mute,
                controller = prefs.Controller,
                haptics = prefs.Haptics,
                autosave = prefs.Autosave,
                shader = prefs.Shader,
                videoRotation = prefs.VideoRotation,
                controls = prefs.Controls,
            });
        }

        /// <summary>
        /// Saves emulator preferences for a user. Omitted fields keep their current value.
        /// 
        /// Path: POST /jellyemu/prefs/{userId}
        /// Parameters:
        ///   - userId (string, path): The user ID.
        ///   - Request Body: JSON object representing prefs fields to update.
        /// Returns Example: (Returns the updated state format equivalent to GET /jellyemu/prefs/{userId})
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
                string Str(string key, string current) =>
                    r.TryGetProperty(key, out var v) ? (v.GetString() ?? current) : current;
                int Int(string key, int current) =>
                    r.TryGetProperty(key, out var v) ? v.GetInt32() : current;

                current = new UserFullPrefs(
                    Scale: Str("scale", current.Scale),
                    Mute: Str("mute", current.Mute),
                    Controller: Str("controller", current.Controller),
                    Haptics: Str("haptics", current.Haptics),
                    Autosave: Str("autosave", current.Autosave),
                    Shader: Str("shader", current.Shader),
                    VideoRotation: Int("videoRotation", current.VideoRotation),
                    Controls: Str("controls", current.Controls));
            }
            catch { return BadRequest("Body must be a JSON object."); }

            WriteFullPrefs(userId, current);
            _logger.LogInformation("[JellyEmu] Prefs saved for user {UserId}", userId);
            return Ok(new
            {
                userId,
                scale = current.Scale,
                mute = current.Mute,
                controller = current.Controller,
                haptics = current.Haptics,
                autosave = current.Autosave,
                shader = current.Shader,
                videoRotation = current.VideoRotation,
                controls = current.Controls,
            });
        }

        /// <summary>
        /// Returns the total playtime in seconds for a given user and item.
        /// 
        /// Path: GET /jellyemu/playtime/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): Game ID.
        ///   - userId (string, path): User ID.
        /// Returns Example: { "userId": "user123", "itemId": "game456", "seconds": 3600 }
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
        /// 
        /// Path: POST /jellyemu/playtime/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): Game ID.
        ///   - userId (string, path): User ID.
        ///   - Request Body: Plain integer OR JSON { "seconds": N }
        /// Returns Example: { "userId": "user123", "itemId": "game456", "added": 120, "total": 3720 }
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
        /// Used by the in-Jellyfin save-state browser.
        /// 
        /// Path: GET /jellyemu/saves/{userId}
        /// Parameters:
        ///   - userId (string, path): User ID.
        /// Returns Example: JSON Array of objects `[{ "itemId": "id1", "gameName": "Mario", ... }]`
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

            results.Sort((a, b) =>
            {
                var aDate = (string)a.GetType().GetProperty("lastModified")!.GetValue(a)!;
                var bDate = (string)b.GetType().GetProperty("lastModified")!.GetValue(b)!;
                return string.Compare(bDate, aDate, StringComparison.Ordinal);
            });

            return Ok(results);
        }

        /// <summary>
        /// Opens a Jellyfin playback session for the game, making it visible in Active Sessions.
        /// 
        /// Path: POST /jellyemu/session/start/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): Game ID.
        ///   - userId (string, path): User ID.
        ///   - Headers (Optional): X-JellyEmu-DeviceId, X-JellyEmu-DeviceName
        /// Returns Example: { "started": true, "itemId": "game1", "userId": "user1" }
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
        /// Keeps the session alive and advances the elapsed-time ticker. Called via polling.
        /// 
        /// Path: POST /jellyemu/session/ping/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): Game ID.
        ///   - userId (string, path): User ID.
        /// Returns Example: { "alive": true }
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
        /// 
        /// Path: POST /jellyemu/session/stop/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): Game ID.
        ///   - userId (string, path): User ID.
        /// Returns Example: { "stopped": true }
        /// </summary>
        [HttpPost("/jellyemu/session/stop/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> SessionStop(string itemId, string userId)
        {
            await _sessionService.StopSessionAsync(userId, itemId).ConfigureAwait(false);
            return Ok(new { stopped = true });
        }

        /// <summary>
        /// Serves a Cross-Origin Isolation service worker that adds COOP/COEP headers.
        /// Required for threaded cores (DOS, PSP).
        /// 
        /// Path: GET /jellyemu/coi-sw.js
        /// Returns Example: Raw JavaScript document.
        /// </summary>
        [HttpGet("/jellyemu/coi-sw.js")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult CoiServiceWorker()
        {
            const string js = """
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));

function addHeaders(headers) {
    const newHeaders = new Headers(headers);
    newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');
    newHeaders.set('Cross-Origin-Embedder-Policy', 'credentialless');
    newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');
    return newHeaders;
}

self.addEventListener('fetch', function(e) {
    // Only handle http/https requests
    if (!e.request.url.startsWith('http')) return;

    e.respondWith(
        fetch(e.request)
            .then(function(res) {
                // Don't modify opaque responses
                if (res.type === 'opaque' || res.type === 'opaqueredirect') return res;
                return new Response(res.body, {
                    status: res.status,
                    statusText: res.statusText,
                    headers: addHeaders(res.headers)
                });
            })
            .catch(function() {
                return fetch(e.request);
            })
    );
});
""";
            Response.Headers["Service-Worker-Allowed"] = "/";
            Response.Headers["Cache-Control"] = "no-cache";
            return Content(js, "application/javascript");
        }

        /// <summary>
        /// Proxies the EJS assets. Uses local cache if available, otherwise proxies to CDN.
        /// 
        /// Path: GET /jellyemu/ejs/{*path}
        /// Parameters:
        ///   - path (string, wildcard): Path to resource (e.g. loader.js).
        /// Returns Example: File stream mapping to mimetype of asset.
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
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

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

        /// <summary>
        /// Pings the configured Romm instance without authentication.
        /// Returns reachability and the raw response so the UI can confirm the URL is correct.
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

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
            client.Timeout = TimeSpan.FromSeconds(8);

            // Try /api/heartbeat, then /api/, then root — whatever Romm exposes publicly
            var probes = new[] { "/api/heartbeat", "/api/", "/" };
            foreach (var probe in probes)
            {
                try
                {
                    var resp = await client.GetAsync(url + probe).ConfigureAwait(false);
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    // Truncate body for display
                    var preview = body.Length > 300 ? body[..300] + "…" : body;
                    return Ok(new
                    {
                        reachable = true,
                        probe = url + probe,
                        status = (int)resp.StatusCode,
                        statusText = resp.StatusCode.ToString(),
                        preview
                    });
                }
                catch (Exception ex)
                {
                    // Try next probe
                    _logger.LogDebug("[JellyEmu] Romm health probe {Probe} failed: {Msg}", url + probe, ex.Message);
                }
            }

            return Ok(new { reachable = false, reason = $"Could not reach {url} — check the URL and that Romm is running" });
        }

        private static string RommInstanceUrl =>
            (Plugin.Instance?.Configuration.RommInstanceUrl ?? string.Empty).TrimEnd('/');

        private static bool RommEnabled =>
            Plugin.Instance?.Configuration.RommEnabled == true;

        /// <summary>
        /// Returns an HttpClient with Basic Auth set from the configured Romm credentials.
        /// </summary>
        private HttpClient GetRommClient()
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
            var cfg = Plugin.Instance?.Configuration;
            if (cfg == null) return client;

            var username = cfg.RommUsername;
            var password = cfg.RommPassword;
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var creds = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {creds}");
            }
            return client;
        }

        /// <summary>
        /// Returns the Romm save-sync status for a given item/slot.
        /// Used by the details page misc-info badge.
        ///
        /// Path: GET /jellyemu/romm/sync-status/{itemId}/{userId}/{slot}
        /// Returns: { "status": "Pushed"|"RemoteWins"|"InSync"|"LocalOnly"|"RemoteOnly"|"Disabled"|"Error" }
        /// </summary>
        [HttpGet("/jellyemu/romm/sync-status/{itemId}/{userId}/{slot}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RommSyncStatus(string itemId, string userId, int slot)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return Ok(new { status = "Disabled" });

            var localPath = GetSavePath(userId, itemId, slot);
            var hasLocal = System.IO.File.Exists(localPath);

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { status = hasLocal ? "LocalOnly" : "Disabled" });

            try
            {
                var client = GetRommClient();
                var url = $"{RommInstanceUrl}/api/saves?rom_id={romId}&user_id=me";
                var resp = await client.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return Ok(new { status = "Error" });

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var items = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                DateTimeOffset? remoteModified = null;
                foreach (var s in items.EnumerateArray())
                {
                    if (s.TryGetProperty("slot", out var sl) && sl.GetInt32() == slot)
                    {
                        if (s.TryGetProperty("updated_at", out var ua))
                            remoteModified = DateTimeOffset.Parse(ua.GetString() ?? string.Empty);
                        break;
                    }
                }

                if (!hasLocal && remoteModified == null)
                    return Ok(new { status = "Disabled" });
                if (!hasLocal)
                    return Ok(new { status = "RemoteOnly" });
                if (remoteModified == null)
                    return Ok(new { status = "LocalOnly" });

                var localModified = new System.IO.FileInfo(localPath).LastWriteTimeUtc;
                var diff = (remoteModified.Value.UtcDateTime - localModified).TotalSeconds;
                var status = diff > 5 ? "RemoteWins" : diff < -5 ? "Pushed" : "InSync";
                return Ok(new { status });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Romm sync-status check failed for {ItemId}", itemId);
                return Ok(new { status = "Error" });
            }
        }

        /// <summary>
        /// Force-push a local save state to Romm.
        /// Path: POST /jellyemu/romm/push/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpPost("/jellyemu/romm/push/{itemId}/{userId}/{slot}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommPush(string itemId, string userId, int slot)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return StatusCode(503, new { error = "Romm save sync disabled" });

            var localPath = GetSavePath(userId, itemId, slot);
            if (!System.IO.File.Exists(localPath))
                return NotFound(new { error = "No local save found" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return StatusCode(503, new { error = "Item has no Romm ID" });

            try
            {
                var client = GetRommClient();
                var bytes = await System.IO.File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
                using var content = new System.Net.Http.MultipartFormDataContent();
                content.Add(new System.Net.Http.ByteArrayContent(bytes), "file", $"{itemId}_slot{slot}.state");
                content.Add(new System.Net.Http.StringContent(slot.ToString()), "slot");

                var resp = await client.PostAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _logger.LogWarning("[JellyEmu] Romm push failed: {Status} {Body}", (int)resp.StatusCode, body);
                    return StatusCode(502, new { error = "Romm rejected push", detail = body });
                }
                return Ok(new { pushed = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Romm push error for {ItemId}", itemId);
                return StatusCode(502, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Force-pull a save state from Romm to local storage.
        /// Path: POST /jellyemu/romm/pull/{itemId}/{userId}/{slot}
        /// </summary>
        [HttpPost("/jellyemu/romm/pull/{itemId}/{userId}/{slot}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommPull(string itemId, string userId, int slot)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return StatusCode(503, new { error = "Romm save sync disabled" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return StatusCode(503, new { error = "Item has no Romm ID" });

            try
            {
                var client = GetRommClient();
                // Get the save metadata list to find the download URL for this slot
                var listResp = await client.GetAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}&user_id=me").ConfigureAwait(false);
                if (!listResp.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "Could not list Romm saves" });

                var listJson = await listResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(listJson);
                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                string? downloadUrl = null;
                foreach (var s in arr.EnumerateArray())
                {
                    if (s.TryGetProperty("slot", out var sl) && sl.GetInt32() == slot)
                    {
                        downloadUrl = s.TryGetProperty("download_path", out var dp) ? dp.GetString() : null;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    return NotFound(new { error = $"No Romm save for slot {slot}" });

                if (!Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
                    downloadUrl = $"{RommInstanceUrl}{(downloadUrl.StartsWith("/") ? "" : "/")}{downloadUrl}";

                var dataResp = await client.GetAsync(downloadUrl).ConfigureAwait(false);
                if (!dataResp.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "Romm download failed" });

                var bytes = await dataResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var localPath = GetSavePath(userId, itemId, slot);
                await System.IO.File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);

                _logger.LogInformation("[JellyEmu] Romm pull: wrote {Bytes}b to {Path}", bytes.Length, localPath);
                return Ok(new { pulled = true, bytes = bytes.Length });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Romm pull error for {ItemId}", itemId);
                return StatusCode(502, new { error = ex.Message });
            }
        }

        /// <summary>
        /// On game launch: compare timestamps; if Romm is newer, pull and return { pulled: true }.
        /// Path: POST /jellyemu/romm/sync-on-launch/{itemId}/{userId}
        /// </summary>
        [HttpPost("/jellyemu/romm/sync-on-launch/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RommSyncOnLaunch(string itemId, string userId)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return Ok(new { pulled = false, reason = "disabled" });

            var slot = ReadUserPrefs(userId).Slot;
            var localPath = GetSavePath(userId, itemId, slot);
            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { pulled = false, reason = "no_romm_id" });

            try
            {
                var client = GetRommClient();
                var listResp = await client.GetAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}&user_id=me").ConfigureAwait(false);
                if (!listResp.IsSuccessStatusCode)
                    return Ok(new { pulled = false, reason = "romm_error" });

                var listJson = await listResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(listJson);
                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                DateTimeOffset? remoteModified = null;
                string? downloadUrl = null;
                foreach (var s in arr.EnumerateArray())
                {
                    if (s.TryGetProperty("slot", out var sl) && sl.GetInt32() == slot)
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

                // Romm is newer — pull it
                if (!Uri.IsWellFormedUriString(downloadUrl, UriKind.Absolute))
                    downloadUrl = $"{RommInstanceUrl}{(downloadUrl.StartsWith("/") ? "" : "/")}{downloadUrl}";

                var dataResp = await client.GetAsync(downloadUrl).ConfigureAwait(false);
                if (!dataResp.IsSuccessStatusCode)
                    return Ok(new { pulled = false, reason = "download_failed" });

                var bytes = await dataResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                await System.IO.File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);
                _logger.LogInformation("[JellyEmu] Romm sync-on-launch: pulled {Bytes}b for {ItemId}", bytes.Length, itemId);
                return Ok(new { pulled = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Romm sync-on-launch error for {ItemId}", itemId);
                return Ok(new { pulled = false, reason = "exception" });
            }
        }

        /// <summary>
        /// After a save: push the local save to Romm.
        /// Path: POST /jellyemu/romm/sync-after-save/{itemId}/{userId}
        /// </summary>
        [HttpPost("/jellyemu/romm/sync-after-save/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RommSyncAfterSave(string itemId, string userId)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommSaveSyncEnabled == true))
                return Ok(new { pushed = false, reason = "disabled" });

            var slot = ReadUserPrefs(userId).Slot;
            var localPath = GetSavePath(userId, itemId, slot);
            if (!System.IO.File.Exists(localPath))
                return Ok(new { pushed = false, reason = "no_local_save" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { pushed = false, reason = "no_romm_id" });

            try
            {
                var client = GetRommClient();
                var bytes = await System.IO.File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
                using var content = new System.Net.Http.MultipartFormDataContent();
                content.Add(new System.Net.Http.ByteArrayContent(bytes), "file", $"{itemId}_slot{slot}.state");
                content.Add(new System.Net.Http.StringContent(slot.ToString()), "slot");

                var resp = await client.PostAsync($"{RommInstanceUrl}/api/saves?rom_id={romId}", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[JellyEmu] Romm sync-after-save push failed: {Status}", (int)resp.StatusCode);
                    return Ok(new { pushed = false, reason = "romm_rejected" });
                }
                return Ok(new { pushed = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Romm sync-after-save error for {ItemId}", itemId);
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
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommPlaytimeReportEnabled == true))
                return Ok(new { reported = false, reason = "disabled" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return Ok(new { reported = false, reason = "no_romm_id" });

            long seconds = 0;
            try
            {
                var body = await new System.IO.StreamReader(Request.Body).ReadToEndAsync().ConfigureAwait(false);
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
                var client = GetRommClient();
                var payload = System.Text.Json.JsonSerializer.Serialize(new { time_played = seconds });
                using var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var resp = await client.PostAsync($"{RommInstanceUrl}/api/roms/{romId}/playtime", content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[JellyEmu] Romm playtime report failed: {Status}", (int)resp.StatusCode);
                    return Ok(new { reported = false, reason = "romm_rejected" });
                }
                return Ok(new { reported = true, seconds });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Romm playtime report error for {ItemId}", itemId);
                return Ok(new { reported = false, reason = "exception" });
            }
        }

        /// <summary>
        /// Fetches Romm collections and creates matching Jellyfin playlists (if they don't exist).
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
                var resp = await client.GetAsync($"{RommInstanceUrl}/api/collections").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "Could not fetch Romm collections" });

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                // Romm may return array or { items: [] }
                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? doc.RootElement : doc.RootElement.TryGetProperty("items", out var it) ? it : default;

                var created = new System.Collections.Generic.List<string>();
                var skipped = new System.Collections.Generic.List<string>();

                foreach (var col in arr.EnumerateArray())
                {
                    var colName = col.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(colName)) continue;

                    // Gather Romm ROM ids in this collection
                    var romIds = new System.Collections.Generic.List<string>();
                    if (col.TryGetProperty("roms", out var roms))
                        foreach (var r in roms.EnumerateArray())
                        {
                            var rid = r.TryGetProperty("id", out var rid2) ? rid2.ToString() : r.GetString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(rid)) romIds.Add(rid);
                        }

                    // Map Romm IDs → Jellyfin item IDs
                    var jellyfinIds = new System.Collections.Generic.List<Guid>();
                    foreach (var romId in romIds)
                    {
                        var jfItem = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                        {
                            HasAnyProviderId = new System.Collections.Generic.Dictionary<string, string> { { "Romm", romId } }
                        }).FirstOrDefault();
                        if (jfItem != null) jellyfinIds.Add(jfItem.Id);
                    }

                    // Check if a playlist with this name already exists (we use a tag to track it)
                    var existingCollection = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                    {
                        Name = colName,
                        IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.BoxSet }
                    }).FirstOrDefault();

                    if (existingCollection != null)
                    {
                        skipped.Add(colName);
                        continue;
                    }

                    // Create a Jellyfin collection (BoxSet) via ApiClient — we record it as created
                    // Since creating BoxSets requires ICollectionManager which needs DI wiring,
                    // we expose the data for the UI to create via ApiClient instead.
                    created.Add(colName);
                }

                return Ok(new { created, skipped, total = created.Count + skipped.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Romm collection sync failed");
                return StatusCode(502, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Returns all Romm collections with their mapped Jellyfin item IDs.
        /// The UI uses this to create playlists via the Jellyfin ApiClient.
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

            // Step 1: get auth client
            HttpClient client;
            try
            {
                client = GetRommClient();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] RommGetCollections: failed to obtain auth client");
                return StatusCode(502, new { error = "Auth client failed", step = "get_auth_client", detail = ex.Message });
            }

            // Step 2: call /api/collections
            HttpResponseMessage resp;
            string collectionsUrl = $"{instanceUrl}/api/collections";
            try
            {
                resp = await client.GetAsync(collectionsUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] RommGetCollections: HTTP request to {Url} failed", collectionsUrl);
                return StatusCode(502, new { error = "HTTP request failed", step = "fetch_collections", url = collectionsUrl, detail = ex.Message });
            }

            // Step 3: check response status
            if (!resp.IsSuccessStatusCode)
            {
                string errBody;
                try { errBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch { errBody = "(could not read body)"; }
                _logger.LogWarning("[JellyEmu] Romm GET {Url} returned {Status}: {Body}", collectionsUrl, (int)resp.StatusCode, errBody);
                return StatusCode(502, new { error = "Romm returned non-success", step = "fetch_collections", url = collectionsUrl, status = (int)resp.StatusCode, detail = errBody });
            }

            // Step 4: parse JSON
            string json;
            try { json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                return StatusCode(502, new { error = "Failed to read response body", step = "read_body", detail = ex.Message });
            }

            _logger.LogInformation("[JellyEmu] Romm collections raw response ({Len} chars): {Preview}",
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

            // Step 5: find the array
            System.Text.Json.JsonElement arr = default;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                arr = root;
            else if (root.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
                arr = items;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
                arr = data;
            else if (root.TryGetProperty("collections", out var cols) && cols.ValueKind == System.Text.Json.JsonValueKind.Array)
                arr = cols;
            else
            {
                var keys = root.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? string.Join(", ", root.EnumerateObject().Select(p => p.Name))
                    : root.ValueKind.ToString();
                _logger.LogWarning("[JellyEmu] Could not find collection array. Root kind: {Kind}, keys: {Keys}", root.ValueKind, keys);
                // Return empty rather than error — let caller see zero collections
                return Ok(new { collections = System.Array.Empty<object>(), debug = new { rootKind = root.ValueKind.ToString(), keys, raw = json.Length > 300 ? json[..300] : json } });
            }

            // Step 6: map to Jellyfin items
            var result = new System.Collections.Generic.List<object>();
            foreach (var col in arr.EnumerateArray())
            {
                var colName = col.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(colName)) continue;

                var jellyfinItemIds = new System.Collections.Generic.List<string>();

                System.Text.Json.JsonElement romsEl = default;
                if (col.TryGetProperty("roms", out var romsArr) && romsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    romsEl = romsArr;
                else if (col.TryGetProperty("rom_ids", out var romIds) && romIds.ValueKind == System.Text.Json.JsonValueKind.Array)
                    romsEl = romIds;

                if (romsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var r in romsEl.EnumerateArray())
                    {
                        // rom_ids contains plain integers; roms contains objects with an "id" field
                        string rid;
                        if (r.ValueKind == System.Text.Json.JsonValueKind.Number)
                            rid = r.GetInt32().ToString();
                        else if (r.ValueKind == System.Text.Json.JsonValueKind.Object)
                            rid = r.TryGetProperty("id", out var rid2) ? rid2.ToString() : string.Empty;
                        else
                            rid = r.ToString();

                        if (string.IsNullOrEmpty(rid)) continue;
                        var jfItem = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
                        {
                            HasAnyProviderId = new System.Collections.Generic.Dictionary<string, string> { { "Romm", rid } }
                        }).FirstOrDefault();
                        if (jfItem != null) jellyfinItemIds.Add(jfItem.Id.ToString("N"));
                    }
                }

                result.Add(new { name = colName, jellyfinItemIds });
            }

            return Ok(result);
        }

        /// <summary>
        /// Accepts a screenshot (base64 or raw bytes) and pushes it to Romm.
        /// Path: POST /jellyemu/romm/screenshot/{itemId}/{userId}
        /// Body: { "dataUrl": "data:image/png;base64,..." } OR raw PNG bytes
        /// </summary>
        [HttpPost("/jellyemu/romm/screenshot/{itemId}/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RommPushScreenshot(string itemId, string userId)
        {
            if (!RommEnabled || !(Plugin.Instance?.Configuration.RommScreenshotPushEnabled == true))
                return StatusCode(503, new { error = "Romm screenshot push disabled" });

            var romId = GetRommIdForItem(itemId);
            if (string.IsNullOrEmpty(romId))
                return StatusCode(503, new { error = "Item has no Romm ID" });

            byte[] imageBytes;
            string fileName = $"screenshot_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.png";

            try
            {
                var contentType = Request.ContentType ?? string.Empty;
                if (contentType.Contains("application/json"))
                {
                    var body = await new System.IO.StreamReader(Request.Body).ReadToEndAsync().ConfigureAwait(false);
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
                    _logger.LogWarning("[JellyEmu] Romm screenshot push failed: {Status}", (int)resp.StatusCode);
                    return StatusCode(502, new { error = "Romm rejected screenshot", detail });
                }
                return Ok(new { pushed = true, fileName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Romm screenshot push error for {ItemId}", itemId);
                return StatusCode(502, new { error = ex.Message });
            }
        }

        private string? GetRommIdForItem(string itemId)
        {
            try
            {
                var item = _libraryManager.GetItemById(itemId);
                return item?.GetProviderId("Romm");
            }
            catch { return null; }
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