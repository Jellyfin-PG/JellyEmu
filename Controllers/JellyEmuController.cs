using System.Net.Mime;
using System.Text.Encodings.Web;
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
            ILogger<JellyEmuController> logger)
        {
            _libraryManager = libraryManager;
            _appPaths = appPaths;
            _logger = logger;
        }

        // Saves are stored at: {DataPath}/jellyemu-saves/{userId}/{itemId}.state
        private string GetSavePath(string userId, string itemId)
        {
            var dir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{itemId}.state");
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
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var romUrl = $"{baseUrl}/jellyemu/rom/{itemId}";

            // Save state URLs — only wired up if a userId was provided
            var hasSaves = !string.IsNullOrEmpty(userId);
            var saveGetUrl = hasSaves ? $"{baseUrl}/jellyemu/save/{itemId}/{userId}" : "";
            var savePostUrl = hasSaves ? $"{baseUrl}/jellyemu/save/{itemId}/{userId}" : "";

            // Check if a save already exists for this user+item to auto-load it
            var saveExists = hasSaves && System.IO.File.Exists(GetSavePath(userId!, itemId));

            var gameName = HtmlEncoder.Default.Encode(item.Name);

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
        window.EJS_pathtodata    = 'https://cdn.emulatorjs.org/latest/data/';
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
    <script src=""https://cdn.emulatorjs.org/latest/data/loader.js""></script>
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
            var path = GetSavePath(userId, itemId);
            if (!System.IO.File.Exists(path))
            {
                _logger.LogInformation("[JellyEmu] No save found for item {ItemId} user {UserId}", itemId, userId);
                return NotFound();
            }

            _logger.LogInformation("[JellyEmu] Serving save for item {ItemId} user {UserId}", itemId, userId);
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

            var path = GetSavePath(userId, itemId);

            using var fs = System.IO.File.Create(path);
            await Request.Body.CopyToAsync(fs);

            _logger.LogInformation("[JellyEmu] Saved state for item {ItemId} user {UserId} ({Bytes} bytes)",
                itemId, userId, fs.Length);

            return Ok();
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
