using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
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

        private static readonly System.Collections.Generic.Dictionary<string, string> CoreMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",             "nes"        },
                { "SNES",            "snes"       },
                { "N64",             "n64"        },
                { "Game Boy",        "gb"         },  // gbc also maps here — gambatte handles both
                { "Game Boy Advance","gba"        },
                { "Nintendo DS",     "nds"        },
                { "Virtual Boy",     "vb"         },
                { "Master System",   "segaMS"     },
                { "Game Gear",       "segaGG"     },
                { "Sega Genesis",    "segaMD"     },
                { "Sega CD",         "segaCD"     },
                { "Sega 32X",        "sega32x"    },
                { "PlayStation",     "psx"        },
                { "Atari 2600",      "atari2600"  },
                { "Atari 7800",      "atari7800"  },
                { "Atari Lynx",      "lynx"       },
                { "Atari Jaguar",    "jaguar"     },
                { "WonderSwan",      "ws"         },
                { "TurboGrafx-16",   "pce"        },
                { "ColecoVision",    "coleco"     },
                { "NeoGeo Pocket",   "ngp"        },
            };

        public JellyEmuController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuController> logger,
            JellyEmuEjsManager ejsManager)
        {
            _libraryManager = libraryManager;
            _appPaths = appPaths;
            _logger = logger;
            _ejsManager = ejsManager;
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

        private int ReadActiveSlot(string userId)
        {
            var path = GetSlotFilePath(userId);
            if (!System.IO.File.Exists(path)) return 1;
            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("slot", out var s) ? Math.Max(1, s.GetInt32()) : 1;
            }
            catch { return 1; }
        }

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
            var activeSlot = hasSaves ? ReadActiveSlot(userId!) : 1;
            var saveGetUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";
            var savePostUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";

            var saveExists = hasSaves && System.IO.File.Exists(GetSavePath(userId!, itemId, activeSlot));

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
            position: fixed; top: 20px; left: 20px; z-index: 9999;
            background: rgba(0,0,0,0.7); color: #fff;
            border: 1px solid #fff; padding: 10px 20px; font-size: 16px;
            border-radius: 5px; cursor: pointer;
            backdrop-filter: blur(5px);
            display: flex; align-items: center; gap: 8px;
        }}
    </style>
</head>
<body>
    <button id=""exit-btn"" onclick=""window.parent.postMessage('close-jellyemu', '*')"">
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
        window.EJS_askBeforeExit = false;
        window.EJS_color         = '#00a4dc';
        {(saveExists ? $"window.EJS_loadStateURL = '{saveGetUrl}';" : "")}
        {(hasSaves ? $@"
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
        /// Downloads the save state for a given user and item.
        /// </summary>
        [HttpGet("/jellyemu/save/{itemId}/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetSave(string itemId, string userId)
        {
            var slot = ReadActiveSlot(userId);
            var path = GetSavePath(userId, itemId, slot);
            if (!System.IO.File.Exists(path))
            {
                _logger.LogInformation("[JellyEmu] No save found for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slot);
                return NotFound();
            }

            _logger.LogInformation("[JellyEmu] Serving save for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slot);
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
            var slot = ReadActiveSlot(userId);
            return Ok(new { userId, slot });
        }

        [HttpPost("/jellyemu/slot/{userId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult SetSlot(string userId, [FromQuery] int slot)
        {
            if (slot < 1 || slot > 99)
                return BadRequest("Slot must be between 1 and 99.");

            var path = GetSlotFilePath(userId);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { slot }));

            _logger.LogInformation("[JellyEmu] User {UserId} active slot set to {Slot}", userId, slot);
            return Ok(new { userId, slot });
        }

        
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
                    { "nes", "nes"      }, { "fds", "nes"      }, { "unf", "nes"      }, { "unif", "nes"     },
                    { "smc", "snes"     }, { "sfc", "snes"     }, { "swc", "snes"     }, { "fig",  "snes"    },
                    { "z64", "n64"      }, { "n64", "n64"      }, { "v64", "n64"      },
                    { "gb",  "gb"       }, { "gbc", "gb"       },  // gbc → gb, gambatte handles both
                    { "gba", "gba"      },
                    { "nds", "nds"      },
                    { "vb",  "vb"       },
                    { "sms", "segaMS"   },
                    { "gg",  "segaGG"   },
                    { "md",  "segaMD"   }, { "smd", "segaMD"   }, { "gen", "segaMD"   }, { "68k", "segaMD"   },
                    { "32x", "sega32x"  },
                    { "pbp", "psx"      }, { "cue", "psx"      }, { "iso", "psx"      }, { "chd", "psx"      },
                    { "a26", "atari2600"},
                    { "a78", "atari7800"},
                    { "lnx", "lynx"     },
                    { "jag", "jaguar"   }, { "j64", "jaguar"   },
                    { "ws",  "ws"       }, { "wsc", "ws"       },
                    { "pce", "pce"      },
                    { "col", "coleco"   }, { "cv",  "coleco"   },
                    { "ngp", "ngp"      }, { "ngc", "ngp"      },
                };
                if (extMap.TryGetValue(ext, out var extCore))
                    return extCore;
            }

            return "nes"; // last resort fallback
        }
    }
}
