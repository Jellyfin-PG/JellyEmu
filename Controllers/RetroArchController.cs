using System.Net.Mime;
using System.Text;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Exposes ROM content to RetroArch (desktop and Android) via:
    ///   GET /jellyemu/retroarch/playlist          — full library as a .lpl playlist (JSON)
    ///   GET /jellyemu/retroarch/playlist/{system} — filtered to one system tag (e.g. "NES")
    ///   GET /jellyemu/retroarch/launch/{itemId}   — RetroArch deep-link redirect (Android / desktop URI)
    ///   GET /jellyemu/retroarch/info              — server info JSON for client discovery
    /// </summary>
    [ApiController]
    public class RetroArchController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<RetroArchController> _logger;

        // Maps the platform tag stored on each ROM item to the RetroArch core filename.
        // These are the canonical libretro core names used in .lpl playlists.
        private static readonly Dictionary<string, string> CoreFileMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",              "nestopia_libretro"     },
                { "SNES",             "snes9x_libretro"       },
                { "N64",              "mupen64plus_next_libretro" },
                { "Game Boy",         "gambatte_libretro"     },
                { "Game Boy Advance", "mgba_libretro"         },
                { "Nintendo DS",      "melonds_libretro"      },
                { "Virtual Boy",      "beetle_vb_libretro"    },
                { "Master System",    "genesis_plus_gx_libretro" },
                { "Game Gear",        "genesis_plus_gx_libretro" },
                { "Sega Genesis",     "genesis_plus_gx_libretro" },
                { "Sega CD",          "genesis_plus_gx_libretro" },
                { "Sega 32X",         "picodrive_libretro"    },
                { "PlayStation",      "pcsx_rearmed_libretro" },
                { "Atari 2600",       "stella_libretro"       },
                { "Atari 7800",       "prosystem_libretro"    },
                { "Atari Lynx",       "handy_libretro"        },
                { "Atari Jaguar",     "virtualjaguar_libretro"},
                { "WonderSwan",       "beetle_wswan_libretro" },
                { "TurboGrafx-16",    "beetle_pce_libretro"   },
                { "ColecoVision",     "bluemsx_libretro"      },
                { "NeoGeo Pocket",    "beetle_ngp_libretro"   },
            };

        public RetroArchController(
            ILibraryManager libraryManager,
            ILogger<RetroArchController> logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        private string ServerBase()
            => $"{Request.Scheme}://{Request.Host}";

        private static string ResolveCoreFile(BaseItem item)
        {
            if (item.Tags != null)
                foreach (var tag in item.Tags)
                    if (CoreFileMap.TryGetValue(tag, out var core))
                        return core;
            return "DETECT";
        }

        private IEnumerable<BaseItem> GetRomItems(string? systemFilter = null)
        {
            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            var items = _libraryManager.GetItemList(query)
                .Where(i => i.Tags != null && i.Tags.Contains("Game", StringComparer.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(systemFilter))
                items = items.Where(i =>
                    i.Tags!.Any(t => string.Equals(t, systemFilter, StringComparison.OrdinalIgnoreCase)));

            return items;
        }

        // =========================================================================
        // GET /jellyemu/retroarch/playlist
        // GET /jellyemu/retroarch/playlist/{system}
        //
        // Returns a RetroArch JSON playlist (.lpl) containing every ROM in the
        // library (or only those matching {system}). Each entry points at the
        // /jellyemu/rom/{itemId} streaming endpoint so RetroArch downloads the
        // ROM on demand — no local file access required.
        //
        // RetroArch can import this via:
        //   Main Menu → Import Content → Manual Scan → (paste URL)
        // or by placing the .lpl file in the RetroArch playlists folder.
        // =========================================================================
        [HttpGet("/jellyemu/retroarch/playlist")]
        [HttpGet("/jellyemu/retroarch/playlist/{system}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Playlist([FromRoute] string? system = null)
        {
            var items = GetRomItems(system).ToList();
            _logger.LogInformation("[JellyEmu/RetroArch] Playlist requested (system={System}), {Count} ROMs", system ?? "all", items.Count);

            var entries = items.Select(item =>
            {
                var romUrl = $"{ServerBase()}/jellyemu/rom/{item.Id}";
                var coreName = ResolveCoreFile(item);
                return new
                {
                    path = romUrl,
                    label = item.Name ?? Path.GetFileNameWithoutExtension(item.Path ?? string.Empty),
                    core_path = "DETECT",          // RetroArch will pick the core from core_name
                    core_name = coreName,
                    crc32 = "00000000|crc",    // unknown; RetroArch accepts this sentinel
                    db_name = ResolveDbName(item),
                };
            }).ToList();

            var playlist = new
            {
                version = "1.5",
                default_core_path = "DETECT",
                default_core_name = "DETECT",
                label_display_mode = 0,
                right_thumbnail_mode = 0,
                left_thumbnail_mode = 0,
                items = entries,
            };

            var json = JsonSerializer.Serialize(playlist, new JsonSerializerOptions { WriteIndented = true });

            var suggestedName = string.IsNullOrWhiteSpace(system) ? "JellyEmu.lpl" : $"JellyEmu-{system}.lpl";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{suggestedName}\"";

            return Content(json, MediaTypeNames.Application.Json, Encoding.UTF8);
        }

        // =========================================================================
        // GET /jellyemu/retroarch/launch/{itemId}
        //
        // Issues a RetroArch deep-link URI so a browser (on Android or desktop)
        // can hand the ROM directly to RetroArch.
        //
        // Android URI scheme:  retroarch://rom?path=<url>&core=<core>
        // Desktop (Windows/Linux/macOS) URI scheme: retroarch://load?content=<url>
        //
        // The ?client= query param lets the caller hint which flavour to generate:
        //   ?client=android  → retroarch:// intent-style link
        //   ?client=desktop  → retroarch:// deep link (default)
        //   ?client=redirect → 302 to the deep link (default, usable from anchor tags)
        // =========================================================================
        [HttpGet("/jellyemu/retroarch/launch/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Launch(string itemId, [FromQuery] string? client = "redirect")
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                _logger.LogWarning("[JellyEmu/RetroArch] Launch: item {ItemId} not found", itemId);
                return NotFound();
            }

            var romUrl = $"{ServerBase()}/jellyemu/rom/{itemId}";
            var coreName = ResolveCoreFile(item);

            var deepLink = $"retroarch://load?content={Uri.EscapeDataString(romUrl)}&core={Uri.EscapeDataString(coreName)}";

            _logger.LogInformation("[JellyEmu/RetroArch] Launch deep-link for {ItemId}: {Link}", itemId, deepLink);

            if (string.Equals(client, "json", StringComparison.OrdinalIgnoreCase))
            {
                // Machine-readable response — useful for custom front-ends
                return Ok(new
                {
                    item_id = itemId,
                    name = item.Name,
                    rom_url = romUrl,
                    core_name = coreName,
                    deep_link = deepLink,
                });
            }

            return Redirect(deepLink);
        }

        // =========================================================================
        // GET /jellyemu/retroarch/info
        //
        // Returns lightweight JSON that a RetroArch companion app or custom
        // front-end can use to discover the server and enumerate systems.
        // =========================================================================
        [HttpGet("/jellyemu/retroarch/info")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Info()
        {
            var allRoms = GetRomItems().ToList();

            var systems = allRoms
                .SelectMany(i => i.Tags ?? Array.Empty<string>())
                .Where(t => !string.Equals(t, "Game", StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    system = g.Key,
                    rom_count = g.Count(),
                    playlist_url = $"{ServerBase()}/jellyemu/retroarch/playlist/{Uri.EscapeDataString(g.Key)}",
                })
                .OrderBy(s => s.system)
                .ToList();

            return Ok(new
            {
                server = "JellyEmu",
                version = "1.0",
                total_roms = allRoms.Count,
                playlist_all = $"{ServerBase()}/jellyemu/retroarch/playlist",
                launch_template = $"{ServerBase()}/jellyemu/retroarch/launch/{{itemId}}",
                systems = systems,
            });
        }

        private static string ResolveDbName(BaseItem item)
        {
            if (item.Tags == null) return "DETECT";
            var dbMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",              "Nintendo - Nintendo Entertainment System" },
                { "SNES",             "Nintendo - Super Nintendo Entertainment System" },
                { "N64",              "Nintendo - Nintendo 64" },
                { "Game Boy",         "Nintendo - Game Boy" },
                { "Game Boy Advance", "Nintendo - Game Boy Advance" },
                { "Nintendo DS",      "Nintendo - Nintendo DS" },
                { "Master System",    "Sega - Master System - Mark III" },
                { "Game Gear",        "Sega - Game Gear" },
                { "Sega Genesis",     "Sega - Mega Drive - Genesis" },
                { "Sega CD",          "Sega - Mega-CD - Sega CD" },
                { "Sega 32X",         "Sega - 32X" },
                { "PlayStation",      "Sony - PlayStation" },
                { "Atari 2600",       "Atari - 2600" },
                { "Atari 7800",       "Atari - 7800" },
                { "Atari Lynx",       "Atari - Lynx" },
                { "TurboGrafx-16",    "NEC - PC Engine - TurboGrafx 16" },
                { "NeoGeo Pocket",    "SNK - Neo Geo Pocket Color" },
                { "WonderSwan",       "Bandai - WonderSwan Color" },
            };
            foreach (var tag in item.Tags)
                if (dbMap.TryGetValue(tag, out var db))
                    return db;
            return "DETECT";
        }
    }
}
