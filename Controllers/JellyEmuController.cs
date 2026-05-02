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
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

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

        private string GetSaveScreenshotPath(string userId, string itemId, int slot)
        {
            var dir = Path.Combine(_appPaths.DataPath, "jellyemu-saves", userId, $"slot{slot}");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{itemId}.screenshot.json");
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

        // Full user preferences (emulator + controls + save behaviour)
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
            string Controls,           // JSON — keyboard bindings for EJS player-0
            string ControllerControls); // JSON — gamepad button bindings for EJS player-0

        private static readonly UserFullPrefs DefaultFullPrefs =
            new("fit", "false", "auto", "true", "true", string.Empty, 0, string.Empty, string.Empty);

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
                    Controls: Str("controls", DefaultFullPrefs.Controls),
                    ControllerControls: Str("controllerControls", DefaultFullPrefs.ControllerControls));
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
                controllerControls = prefs.ControllerControls,
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
        public async Task<IActionResult> Play(string itemId, [FromQuery] string? userId, [FromQuery] int? slot,
            [FromServices] IHttpClientFactory httpClientFactory)
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
            var activeSlot = (slot.HasValue && slot.Value > 0) ? slot.Value : userPrefs.Slot;
            var activeShader = userPrefs.Shader;
            var videoRotation = userPrefs.VideoRotation;
            var savedControls = fullPrefs.Controls;           // keyboard bindings JSON
            var savedControllerControls = fullPrefs.ControllerControls; // gamepad bindings JSON
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

            // Fetch cheats server-side so they're inlined before loader.js runs.
            // GetCheatsJson handles disk cache — this is a fast local read on repeat plays.
            var cheatsJson = await GetCheatsJsonAsync(item, httpClientFactory);

            // Handle direct loading of a save state
            var directLoadScript = (slot.HasValue && slot.Value > 0 && hasSaves) ? $@"
                setTimeout(function() {{
                    fetch('/jellyemu/save/{itemId}/{userId}?slot={slot.Value}')
                        .then(function(r) {{ if (r.ok) return r.arrayBuffer(); throw new Error('No save data'); }})
                        .then(function(buf) {{
                            var g = gm(); if (g) g.loadState(new Uint8Array(buf));
                            console.log('[JellyEmu] Pipeline STAGE 4 (Client Direct Receive): Downloaded bytes ->', buf.byteLength);
                        }}).catch(function(e) {{ console.warn('[JellyEmu] Direct load failed:', e); }});
                }}, 500);" : "";

            var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{gameName}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{ width: 100%; height: 100%; background: #000; overflow: hidden; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #fff; }}
        #game {{ width: 100%; height: 100%; }}

        /* ── Hide native EJS UI (specific selectors only — preserve ejs_parent for keyboard focus) ── */
        .ejs_bottom_bar_area, .ejs_loading_text, .ejs_start_button,
        .ejs_settings_parent, .ejs_cheat_parent, .ejs_menu_bar_area,
        .ejs_control_bar, .ejs_menu_bar, .ejs_menu_button, .ejs_bar_top {{ display: none !important; }}

        /* ── Loading Screen ── */
        #je-loader {{ position: fixed; inset: 0; z-index: 99999; background: #000; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 24px; transition: opacity .4s ease, transform .4s ease; }}
        #je-loader.je-dismiss {{ opacity: 0; transform: scale(1.03); pointer-events: none; }}
        #je-loader-title {{ font-size: 28px; font-weight: 700; text-align: center; padding: 0 20px; }}
        #je-loader-system {{ display: inline-block; padding: 4px 16px; border-radius: 20px; background: rgba(255,255,255,.12); font-size: 13px; text-transform: uppercase; letter-spacing: 2px; }}
        #je-loader-status {{ font-size: 14px; opacity: .6; }}
        .je-spinner {{ width: 56px; height: 56px; border: 3px solid rgba(255,255,255,.15); border-top-color: #fff; border-radius: 50%; animation: je-spin 1s linear infinite; }}
        @keyframes je-spin {{ to {{ transform: rotate(360deg); }} }}
        .je-pulse-ring {{ position: absolute; width: 90px; height: 90px; border-radius: 50%; border: 2px solid rgba(255,255,255,.08); animation: je-pulse 2s ease-out infinite; }}
        .je-pulse-ring:nth-child(2) {{ animation-delay: .6s; }}
        .je-pulse-ring:nth-child(3) {{ animation-delay: 1.2s; }}
        @keyframes je-pulse {{ 0% {{ transform: scale(.8); opacity: 1; }} 100% {{ transform: scale(2); opacity: 0; }} }}
        .je-loader-anim {{ position: relative; display: flex; align-items: center; justify-content: center; width: 90px; height: 90px; }}

        /* ── Shared Dock Styles ── */
        .je-bar {{ position: fixed; z-index: 90000; transition: opacity .3s ease, transform .3s ease; }}
        .je-bar.je-hidden {{ opacity: 0; pointer-events: none; }}

        /* ── Top Bar ── */
        #je-topbar {{ top: 0; left: 0; right: 0; height: 48px; display: none; align-items: center; justify-content: space-between; padding: 0 16px; background: rgba(0,0,0,.78); backdrop-filter: blur(14px); -webkit-backdrop-filter: blur(14px); border-bottom: 1px solid rgba(255,255,255,.08); }}
        #je-topbar.je-active {{ display: flex; }}
        #je-topbar-title {{ font-size: 15px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 60%; }}
        .je-topbtn {{ background: none; border: none; color: #fff; cursor: pointer; padding: 8px; border-radius: 8px; display: flex; align-items: center; gap: 6px; font-size: 13px; transition: background .15s; }}
        .je-topbtn:hover {{ background: rgba(255,255,255,.1); }}
        .je-topbtn:active {{ transform: scale(.93); }}
        .je-topbtn svg {{ width: 20px; height: 20px; fill: currentColor; }}

        /* ── Bottom Dock ── */
        #je-dock {{ bottom: 16px; left: 50%; transform: translateX(-50%); display: none; align-items: center; gap: 4px; padding: 6px 10px; border-radius: 28px; background: rgba(0,0,0,.78); backdrop-filter: blur(14px); -webkit-backdrop-filter: blur(14px); border: 1px solid rgba(255,255,255,.1); }}
        #je-dock.je-active {{ display: flex; }}
        #je-dock.je-hidden {{ transform: translateX(-50%) translateY(20px); }}
        .je-dockbtn {{ background: none; border: none; color: #fff; cursor: pointer; width: 42px; height: 42px; border-radius: 50%; display: flex; align-items: center; justify-content: center; transition: background .15s, transform .1s; position: relative; }}
        .je-dockbtn:hover {{ background: rgba(255,255,255,.12); }}
        .je-dockbtn:active {{ transform: scale(.88); }}
        .je-dockbtn:disabled {{ opacity: 0.4; cursor: not-allowed; }}
        .je-dockbtn:disabled:hover {{ background: none; }}
        .je-dockbtn:disabled:active {{ transform: none; }}
        .je-dockbtn svg {{ width: 22px; height: 22px; fill: currentColor; }}
        .je-dockbtn.je-active {{ background: rgba(255,255,255,.2); }}
        .je-dock-sep {{ width: 1px; height: 24px; background: rgba(255,255,255,.15); margin: 0 2px; flex-shrink: 0; }}

        /* ── Popup / Modal ── */
        .je-overlay {{ position: fixed; inset: 0; z-index: 95000; background: rgba(0,0,0,.6); backdrop-filter: blur(4px); display: none; align-items: center; justify-content: center; }}
        .je-overlay.je-open {{ display: flex; }}
        .je-popup {{ background: rgba(20,20,20,.95); backdrop-filter: blur(20px); border: 1px solid rgba(255,255,255,.1); border-radius: 16px; width: 90%; max-width: 480px; max-height: 80vh; display: flex; flex-direction: column; animation: je-pop-in .2s ease; }}
        .je-popup-lg {{ max-width: 680px; }}
        @keyframes je-pop-in {{ from {{ opacity: 0; transform: scale(.95); }} to {{ opacity: 1; transform: scale(1); }} }}
        .je-popup-hdr {{ display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid rgba(255,255,255,.08); flex-shrink: 0; }}
        .je-popup-hdr h3 {{ font-size: 16px; font-weight: 600; }}
        .je-closebtn {{ background: none; border: none; color: #fff; font-size: 22px; cursor: pointer; padding: 4px 8px; border-radius: 8px; line-height: 1; }}
        .je-closebtn:hover {{ background: rgba(255,255,255,.1); }}
        .je-popup-body {{ padding: 16px 20px; overflow-y: auto; flex: 1; }}

        /* ── Save Slots ── */
        .je-slot {{ display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 10px; border: 1px solid rgba(255,255,255,.08); margin-bottom: 8px; transition: border-color .2s; }}
        .je-slot.je-slot-active {{ border-color: rgba(255,255,255,.35); }}
        .je-slot-num {{ width: 32px; height: 32px; border-radius: 8px; background: rgba(255,255,255,.08); display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 14px; flex-shrink: 0; }}
        .je-slot-thumb {{ width: 64px; height: 48px; border-radius: 6px; background: rgba(255,255,255,.05); overflow: hidden; flex-shrink: 0; display: flex; align-items: center; justify-content: center; font-size: 10px; opacity: .5; }}
        .je-slot-thumb img {{ width: 100%; height: 100%; object-fit: cover; }}
        .je-slot-info {{ flex: 1; min-width: 0; }}
        .je-slot-info small {{ opacity: .5; font-size: 11px; }}
        .je-slot-actions {{ display: flex; gap: 6px; flex-shrink: 0; }}
        .je-btn {{ padding: 6px 14px; border-radius: 8px; border: 1px solid rgba(255,255,255,.15); background: rgba(255,255,255,.06); color: #fff; cursor: pointer; font-size: 12px; transition: background .15s; }}
        .je-btn:hover {{ background: rgba(255,255,255,.14); }}
        .je-btn-primary {{ background: rgba(255,255,255,.18); border-color: rgba(255,255,255,.25); }}

        /* ── Volume ── */
        .je-vol-wrap {{ display: flex; flex-direction: column; gap: 16px; align-items: center; }}
        .je-vol-pct {{ font-size: 36px; font-weight: 700; }}
        .je-vol-slider {{ width: 100%; -webkit-appearance: none; appearance: none; height: 6px; border-radius: 3px; background: rgba(255,255,255,.15); outline: none; }}
        .je-vol-slider::-webkit-slider-thumb {{ -webkit-appearance: none; width: 20px; height: 20px; border-radius: 50%; background: #fff; cursor: pointer; }}

        /* ── Cheats ── */
        .je-cheat-row {{ display: flex; align-items: center; gap: 10px; padding: 8px 0; border-bottom: 1px solid rgba(255,255,255,.06); }}
        .je-cheat-row:last-child {{ border-bottom: none; }}
        .je-cheat-name {{ flex: 1; font-size: 13px; }}
        .je-cheat-del {{ background: none; border: none; color: rgba(255,255,255,.4); cursor: pointer; font-size: 18px; padding: 2px 6px; }}
        .je-cheat-del:hover {{ color: #f44; }}
        .je-cheat-add {{ display: flex; gap: 8px; margin-top: 12px; }}
        .je-cheat-add input, .je-cheat-add textarea {{ flex: 1; background: rgba(255,255,255,.08); border: 1px solid rgba(255,255,255,.12); border-radius: 8px; padding: 8px 10px; color: #fff; font-size: 13px; resize: none; }}

        /* ── Toggle Switch ── */
        .je-toggle {{ position: relative; width: 40px; height: 22px; flex-shrink: 0; }}
        .je-toggle input {{ opacity: 0; width: 0; height: 0; }}
        .je-toggle-track {{ position: absolute; inset: 0; border-radius: 11px; background: rgba(255,255,255,.15); transition: background .2s; cursor: pointer; }}
        .je-toggle-track::after {{ content: ''; position: absolute; top: 3px; left: 3px; width: 16px; height: 16px; border-radius: 50%; background: #fff; transition: transform .2s; }}
        .je-toggle input:checked + .je-toggle-track {{ background: rgba(100,200,255,.6); }}
        .je-toggle input:checked + .je-toggle-track::after {{ transform: translateX(18px); }}

        /* ── Settings Rows ── */
        .je-setting {{ display: flex; align-items: center; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid rgba(255,255,255,.06); }}
        .je-setting:last-child {{ border-bottom: none; }}
        .je-setting-label {{ font-size: 13px; }}
        .je-setting select {{ background: rgba(30,30,30,.95); border: 1px solid rgba(255,255,255,.2); border-radius: 8px; padding: 6px 10px; color: #fff; font-size: 13px; -webkit-appearance: none; appearance: none; background-image: url(""data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' fill='white' viewBox='0 0 24 24'%3E%3Cpath d='M7 10l5 5 5-5z'/%3E%3C/svg%3E""); background-repeat: no-repeat; background-position: right 8px center; padding-right: 28px; cursor: pointer; }}
        .je-setting select option {{ background: #1a1a1a; color: #fff; padding: 6px; }}
        .je-section-title {{ font-size: 11px; text-transform: uppercase; letter-spacing: 1.5px; opacity: .4; margin: 16px 0 8px; }}
        .je-section-title:first-child {{ margin-top: 0; }}

        /* ── Input Mapping ── */
        .je-tabs {{ display: flex; gap: 0; border-bottom: 1px solid rgba(255,255,255,.1); margin-bottom: 12px; }}
        .je-tab {{ padding: 8px 16px; font-size: 13px; cursor: pointer; border-bottom: 2px solid transparent; opacity: .5; transition: opacity .2s; background: none; border-top: none; border-left: none; border-right: none; color: #fff; }}
        .je-tab.je-tab-active {{ opacity: 1; border-bottom-color: #fff; }}
        .je-tab-panel {{ display: none; }}
        .je-tab-panel.je-tab-active {{ display: block; }}
        .je-bind-row {{ display: flex; align-items: center; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid rgba(255,255,255,.06); font-size: 13px; }}
        .je-bind-key {{ padding: 4px 12px; border-radius: 6px; background: rgba(255,255,255,.08); border: 1px solid rgba(255,255,255,.12); cursor: pointer; min-width: 80px; text-align: center; font-size: 12px; transition: background .2s; }}
        .je-bind-key:hover {{ background: rgba(255,255,255,.15); }}
        .je-bind-key.je-listening {{ background: rgba(100,200,255,.2); border-color: rgba(100,200,255,.5); animation: je-pulse-bind 1s ease infinite; }}
        @keyframes je-pulse-bind {{ 0%,100% {{ opacity: 1; }} 50% {{ opacity: .6; }} }}
        .je-gp-status {{ font-size: 12px; opacity: .5; margin-bottom: 12px; }}

        /* ── FPS Counter ── */
        #je-fps {{ position: fixed; top: 56px; left: 16px; z-index: 89999; font-size: 13px; font-weight: 700; font-family: 'Courier New', monospace; color: #0f0; background: rgba(0,0,0,.6); padding: 2px 8px; border-radius: 4px; pointer-events: none; display: none; text-shadow: 0 0 4px rgba(0,255,0,.5); }}
        #je-fps.je-active {{ display: block; }}

        /* ── Dock Minimize ── */
        #je-dock-min {{ position: fixed; bottom: 16px; right: 16px; z-index: 90000; width: 42px; height: 42px; border-radius: 50%; background: rgba(0,0,0,.78); backdrop-filter: blur(14px); -webkit-backdrop-filter: blur(14px); border: 1px solid rgba(255,255,255,.1); color: #fff; cursor: pointer; display: none; align-items: center; justify-content: center; transition: opacity .3s ease, transform .3s ease; }}
        #je-dock-min.je-active {{ display: flex; }}
        #je-dock-min.je-hidden {{ opacity: 0; pointer-events: none; }}
        #je-dock-min:hover {{ background: rgba(255,255,255,.12); }}
        #je-dock-min:active {{ transform: scale(.88); }}
        #je-dock-min svg {{ width: 22px; height: 22px; fill: currentColor; }}
        #je-dock.je-minimized {{ display: none !important; }}

        /* ── Mobile ── */
        @media (max-width: 768px) {{
            #je-dock {{ gap: 2px; padding: 5px 8px; bottom: 8px; }}
            .je-dockbtn {{ width: 38px; height: 38px; }}
            .je-dockbtn svg {{ width: 20px; height: 20px; }}
            .je-popup {{ width: 96%; max-height: 85vh; border-radius: 12px; }}
            #je-topbar {{ height: 42px; padding: 0 10px; }}
            #je-topbar-title {{ font-size: 13px; }}
            #je-loader-title {{ font-size: 22px; }}
            #je-dock-min {{ bottom: 8px; right: 8px; width: 38px; height: 38px; }}
        }}
    </style>
</head>
<body>
    <!-- Loading Screen -->
    <div id=""je-loader"">
        <div class=""je-loader-anim"">
            <div class=""je-pulse-ring""></div>
            <div class=""je-pulse-ring""></div>
            <div class=""je-pulse-ring""></div>
            <div class=""je-spinner""></div>
        </div>
        <div id=""je-loader-title"">{gameName}</div>
        <div id=""je-loader-system""></div>
        <div id=""je-loader-status"">Loading ROM…</div>
    </div>

    <!-- Top Bar -->
    <div id=""je-topbar"" class=""je-bar"">
        <span id=""je-topbar-title"">{gameName}</span>
        <button class=""je-topbtn"" id=""je-exit-btn"" title=""Exit"">
            <svg viewBox=""0 0 24 24""><path d=""M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z""/></svg>
            Exit
        </button>
    </div>

    <!-- Bottom Dock -->
    <div id=""je-dock"" class=""je-bar"">
        <button class=""je-dockbtn"" id=""je-btn-pause"" title=""Pause""><svg viewBox=""0 0 24 24""><path d=""M6 19h4V5H6v14zm8-14v14h4V5h-4z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-play"" title=""Play"" style=""display:none""><svg viewBox=""0 0 24 24""><path d=""M8 5v14l11-7z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-restart"" title=""Restart""><svg viewBox=""0 0 24 24""><path d=""M17.65 6.35A7.96 7.96 0 0 0 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08A5.99 5.99 0 0 1 12 18c-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-ff"" title=""Fast Forward""><svg viewBox=""0 0 24 24""><path d=""M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-slow"" title=""Slow Motion""><svg viewBox=""0 0 24 24""><path d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-4h2V8h-2v8zm-3 0h2V8H8v8z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-saves"" title=""Save States""><svg viewBox=""0 0 24 24""><path d=""M17 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-vol"" title=""Volume""><svg viewBox=""0 0 24 24""><path d=""M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05c1.48-.73 2.5-2.25 2.5-3.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-cheats"" title=""Cheats""><svg viewBox=""0 0 24 24""><path d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-inputmap"" title=""Input Mapping""><svg viewBox=""0 0 24 24""><path d=""M15 7.5V2H9v5.5l3 3 3-3zM7.5 9H2v6h5.5l3-3-3-3zM9 16.5V22h6v-5.5l-3-3-3 3zM16.5 9l-3 3 3 3H22V9h-5.5z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-netplay"" title=""Netplay"" disabled><svg viewBox=""0 0 24 24""><path d=""M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zm6.93 6h-2.95c-.32-1.25-.78-2.45-1.38-3.56 1.84.63 3.37 1.91 4.33 3.56zM12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96zM4.26 14C4.1 13.36 4 12.69 4 12s.1-1.36.26-2h3.38c-.08.66-.14 1.32-.14 2s.06 1.34.14 2H4.26zm.82 2h2.95c.32 1.25.78 2.45 1.38 3.56-1.84-.63-3.37-1.9-4.33-3.56zm2.95-8H5.08c.96-1.66 2.49-2.93 4.33-3.56C8.81 5.55 8.35 6.75 8.03 8zM12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96zM14.34 14H9.66c-.09-.66-.16-1.32-.16-2s.07-1.35.16-2h4.68c.09.65.16 1.32.16 2s-.07 1.34-.16 2zm.25 5.56c.6-1.11 1.06-2.31 1.38-3.56h2.95c-.96 1.65-2.49 2.93-4.33 3.56zM16.36 14c.08-.66.14-1.32.14-2s-.06-1.34-.14-2h3.38c.16.64.26 1.31.26 2s-.1 1.36-.26 2h-3.38z""/></svg></button>
        <div class=""je-dock-sep""></div>
        <button class=""je-dockbtn"" id=""je-btn-screenshot"" title=""Screenshot""><svg viewBox=""0 0 24 24""><path d=""M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z""/></svg></button>
        <button class=""je-dockbtn"" id=""je-btn-settings"" title=""Settings""><svg viewBox=""0 0 24 24""><path d=""M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96a.49.49 0 0 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.07.62-.07.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6A3.6 3.6 0 1 1 12 8.4a3.6 3.6 0 0 1 0 7.2z""/></svg></button>
    </div>

    <!-- Dock Minimize FAB -->
    <button id=""je-dock-min"" title=""Expand Controls"">
        <svg viewBox=""0 0 24 24""><path d=""M12 8l-6 6 1.41 1.41L12 10.83l4.59 4.58L18 14z""/></svg>
    </button>

    <!-- Popup: Save States -->
    <div class=""je-overlay"" id=""je-pop-saves"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Save States</h3><button class=""je-closebtn"" data-close=""je-pop-saves"">&times;</button></div>
            <div class=""je-popup-body"" id=""je-saves-body""></div>
        </div>
    </div>

    <!-- Popup: Volume -->
    <div class=""je-overlay"" id=""je-pop-vol"">
        <div class=""je-popup"" style=""max-width:340px"">
            <div class=""je-popup-hdr""><h3>Volume</h3><button class=""je-closebtn"" data-close=""je-pop-vol"">&times;</button></div>
            <div class=""je-popup-body"">
                <div class=""je-vol-wrap"">
                    <div id=""je-vol-pct"" class=""je-vol-pct"">50%</div>
                    <input type=""range"" min=""0"" max=""1"" step=""0.01"" value=""0.5"" class=""je-vol-slider"" id=""je-vol-slider"">
                    <button class=""je-btn"" id=""je-vol-mute"">Mute</button>
                </div>
            </div>
        </div>
    </div>

    <!-- Popup: Cheats -->
    <div class=""je-overlay"" id=""je-pop-cheats"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Cheats</h3><button class=""je-closebtn"" data-close=""je-pop-cheats"">&times;</button></div>
            <div class=""je-popup-body"">
                <div id=""je-cheat-list""></div>
                <div class=""je-section-title"" style=""margin-top:16px"">Add Cheat</div>
                <div style=""display:flex;flex-direction:column;gap:8px"">
                    <input id=""je-cheat-name"" placeholder=""Cheat name"" style=""background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.12);border-radius:8px;padding:8px 10px;color:#fff;font-size:13px"">
                    <textarea id=""je-cheat-code"" placeholder=""Cheat code"" rows=""2"" style=""background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.12);border-radius:8px;padding:8px 10px;color:#fff;font-size:13px;resize:none""></textarea>
                    <button class=""je-btn je-btn-primary"" id=""je-cheat-add"">Add Cheat</button>
                </div>
            </div>
        </div>
    </div>

    <!-- Popup: Input Mapping -->
    <div class=""je-overlay"" id=""je-pop-inputmap"">
        <div class=""je-popup je-popup-lg"">
            <div class=""je-popup-hdr""><h3>Input Mapping</h3><button class=""je-closebtn"" data-close=""je-pop-inputmap"">&times;</button></div>
            <div class=""je-popup-body"">
                <div class=""je-tabs"">
                    <button class=""je-tab je-tab-active"" data-tab=""kb"">Keyboard</button>
                    <button class=""je-tab"" data-tab=""gp"">Gamepad</button>
                    <button class=""je-tab"" data-tab=""vg"">Virtual Controls</button>
                </div>
                <div class=""je-tab-panel je-tab-active"" id=""je-tab-kb""></div>
                <div class=""je-tab-panel"" id=""je-tab-gp"">
                    <div class=""je-gp-status"" id=""je-gp-status"">No gamepad detected</div>
                    <div id=""je-gp-binds""></div>
                </div>
                <div class=""je-tab-panel"" id=""je-tab-vg"">
                    <div class=""je-setting"">
                        <span class=""je-setting-label"">Enable Virtual Controls</span>
                        <label class=""je-toggle""><input type=""checkbox"" id=""je-vg-toggle""><span class=""je-toggle-track""></span></label>
                    </div>
                    <div class=""je-setting"">
                        <span class=""je-setting-label"">Left-Handed Mode</span>
                        <label class=""je-toggle""><input type=""checkbox"" id=""je-vg-lefty""><span class=""je-toggle-track""></span></label>
                    </div>
                </div>
                <div style=""margin-top:12px""><button class=""je-btn"" id=""je-input-reset"">Reset to Defaults</button></div>
            </div>
        </div>
    </div>

    <!-- Popup: Settings -->
    <div class=""je-overlay"" id=""je-pop-settings"">
        <div class=""je-popup"">
            <div class=""je-popup-hdr""><h3>Settings</h3><button class=""je-closebtn"" data-close=""je-pop-settings"">&times;</button></div>
            <div class=""je-popup-body"">
                <div class=""je-section-title"">Graphics</div>
                <div class=""je-setting""><span class=""je-setting-label"">Shader</span><select id=""je-set-shader""><option value=""disabled"">None</option><option value=""2xScaleHQ.glslp"">2x ScaleHQ</option><option value=""4xScaleHQ.glslp"">4x ScaleHQ</option><option value=""sabr"">SABR</option><option value=""crt-aperture.glslp"">CRT Aperture</option><option value=""crt-easymode.glslp"">CRT Easymode</option><option value=""crt-geom.glslp"">CRT Geom</option><option value=""crt-mattias.glslp"">CRT Mattias</option><option value=""crt-beam"">CRT Beam</option><option value=""crt-caligari"">CRT Caligari</option><option value=""crt-lottes"">CRT Lottes</option><option value=""crt-zfast"">CRT ZFast</option><option value=""crt-yeetron"">CRT Yeetron</option><option value=""bicubic"">Bicubic</option><option value=""mix-frames"">Mix Frames</option></select></div>
                <div class=""je-setting""><span class=""je-setting-label"">VSync</span><label class=""je-toggle""><input type=""checkbox"" id=""je-set-vsync"" checked><span class=""je-toggle-track""></span></label></div>
                <div class=""je-setting""><span class=""je-setting-label"">Video Rotation</span><select id=""je-set-rotation""><option value=""0"">0°</option><option value=""1"">90°</option><option value=""2"">180°</option><option value=""3"">270°</option></select></div>
                <div class=""je-section-title"">Performance</div>
                <div class=""je-setting""><span class=""je-setting-label"">Fast Forward Rate</span><select id=""je-set-ffrate""><option value=""2"">2x</option><option value=""3"" selected>3x</option><option value=""4"">4x</option><option value=""5"">5x</option><option value=""8"">8x</option><option value=""10"">10x</option><option value=""unlimited"">Unlimited</option></select></div>
                <div class=""je-setting""><span class=""je-setting-label"">Slow Motion Rate</span><select id=""je-set-smrate""><option value=""2"">2x</option><option value=""3"" selected>3x</option><option value=""4"">4x</option><option value=""5"">5x</option></select></div>
                <div class=""je-section-title"">Display</div>
                <div class=""je-setting""><span class=""je-setting-label"">Screen Size</span><select id=""je-set-screensize""><option value=""fit"" selected>Fit to Screen</option><option value=""native"">Native</option><option value=""2x"">2x</option><option value=""3x"">3x</option><option value=""4x"">4x</option></select></div>
                <div class=""je-setting""><span class=""je-setting-label"">Show FPS</span><label class=""je-toggle""><input type=""checkbox"" id=""je-set-fps""><span class=""je-toggle-track""></span></label></div>
            </div>
        </div>
    </div>
    <div id=""je-fps""></div>
    <div id=""game""></div>
    <script>
        // Patch getContext BEFORE loader.js so EJS gets a WebGL context with
        // preserveDrawingBuffer:true — without this, toDataURL always returns black
        // because the buffer is cleared after each frame is composited to screen.
        (function() {{
            var _origGetContext = HTMLCanvasElement.prototype.getContext;
            HTMLCanvasElement.prototype.getContext = function(type, attrs) {{
                if (type === 'webgl' || type === 'webgl2' || type === 'experimental-webgl') {{
                    attrs = Object.assign({{}}, attrs || {{}}, {{ preserveDrawingBuffer: true }});
                }}
                return _origGetContext.call(this, type, attrs);
            }};
        }})();
    </script>
    <script>
        (function() {{
            // Core name map for loading screen badge
            var coreNames = {{nes:'NES',snes:'SNES',n64:'N64',gb:'Game Boy',gba:'Game Boy Advance',nds:'Nintendo DS',
                vb:'Virtual Boy',segaMD:'Sega Genesis',segaGG:'Game Gear',segaMS:'Master System',segaCD:'Sega CD',
                sega32x:'Sega 32X',psx:'PlayStation',psp:'PSP',a2600:'Atari 2600',a7800:'Atari 7800',lynx:'Atari Lynx',
                pce:'TurboGrafx-16',coleco:'ColecoVision',ngp:'Neo Geo Pocket',arcade:'Arcade',dos:'DOS',
                '3do':'3DO',jaguar:'Atari Jaguar',mame2003:'MAME',ws:'WonderSwan', ss: 'Sega Saturn'}};
            var sysEl = document.getElementById('je-loader-system');
            if (sysEl) {{ var cn = coreNames[window.EJS_core] || window.EJS_core || ''; sysEl.textContent = cn; }}

            // Loading screen lifecycle
            var loader = document.getElementById('je-loader');
            var topbar = document.getElementById('je-topbar');
            var dock   = document.getElementById('je-dock');

            function dismissLoader() {{
                if (!loader || loader.classList.contains('je-dismiss')) return;
                loader.classList.add('je-dismiss');
                setTimeout(function() {{ loader.style.display = 'none'; }}, 450);
                topbar.classList.add('je-active');
                dock.classList.add('je-active');
                startAutoHide();
            }}
            // Fallback: auto-dismiss after 30s
            setTimeout(dismissLoader, 30000);

            // Hook into EJS start event
            window.EJS_onGameStart = function() {{
                dismissLoader();
                setTimeout(refocusGame, 500);
{directLoadScript}
                // CRITICAL: EJS checks settingsMenu.style.display !== 'none' in keyChange()
                // to decide whether to block keyboard input. Our CSS class-based hiding
                // doesn't set inline style, so EJS thinks settings menu is open and blocks
                // ALL keyboard input. Force inline style to 'none'.
                setTimeout(function() {{
                    var e = emu();
                    if (e) {{
                        if (e.settingsMenu) e.settingsMenu.style.display = 'none';
                        if (e.controlMenu) e.controlMenu.style.display = 'none';
                    }}
                }}, 200);
            }};

            // Auto-hide docks
            var hideTimer = null;
            var HIDE_MS = 3000;
            var popupOpen = false;

            function showDocks() {{
                if (!topbar.classList.contains('je-active')) return;
                topbar.classList.remove('je-hidden');
                dock.classList.remove('je-hidden');
                clearTimeout(hideTimer);
                if (!popupOpen) {{
                    hideTimer = setTimeout(function() {{
                        topbar.classList.add('je-hidden');
                        dock.classList.add('je-hidden');
                    }}, HIDE_MS);
                }}
            }}
            function startAutoHide() {{
                ['mousemove','mousedown','touchstart','touchmove','keydown'].forEach(function(evt) {{
                    document.addEventListener(evt, showDocks, {{ passive: true }});
                }});
                [topbar, dock].forEach(function(el) {{
                    el.addEventListener('mouseenter', function() {{ clearTimeout(hideTimer); topbar.classList.remove('je-hidden'); dock.classList.remove('je-hidden'); }});
                    el.addEventListener('mouseleave', showDocks);
                }});
                showDocks();
            }}

            // Refocus game on any click on the game area or after dock button press
            document.getElementById('game').addEventListener('mousedown', function() {{
                setTimeout(refocusGame, 50);
            }});
            // Dock buttons: refocus game after each click (unless a popup opened)
            document.addEventListener('click', function(ev) {{
                var btn = ev.target.closest && ev.target.closest('.je-dockbtn');
                if (btn && !popupOpen) {{ setTimeout(refocusGame, 50); }}
            }}, true);

            // Popup management
            function openPopup(id) {{
                closeAllPopups();
                var el = document.getElementById(id);
                if (el) {{ el.classList.add('je-open'); popupOpen = true; clearTimeout(hideTimer); }}
            }}
            function closePopup(id) {{
                var el = document.getElementById(id);
                if (el) el.classList.remove('je-open');
                popupOpen = false;
                showDocks();
                refocusGame();
            }}
            function closeAllPopups() {{
                document.querySelectorAll('.je-overlay.je-open').forEach(function(el) {{ el.classList.remove('je-open'); }});
                popupOpen = false;
                refocusGame();
            }}
            // Close buttons
            document.querySelectorAll('[data-close]').forEach(function(btn) {{
                btn.addEventListener('click', function() {{ closePopup(btn.getAttribute('data-close')); }});
            }});
            // Click outside popup to close
            document.querySelectorAll('.je-overlay').forEach(function(ov) {{
                ov.addEventListener('click', function(e) {{ if (e.target === ov) {{ closeAllPopups(); showDocks(); refocusGame(); }} }});
            }});

            // Tab switching
            document.querySelectorAll('.je-tab').forEach(function(tab) {{
                tab.addEventListener('click', function() {{
                    var tabs = tab.parentElement.querySelectorAll('.je-tab');
                    tabs.forEach(function(t) {{ t.classList.remove('je-tab-active'); }});
                    tab.classList.add('je-tab-active');
                    var panels = tab.closest('.je-popup-body').querySelectorAll('.je-tab-panel');
                    panels.forEach(function(p) {{ p.classList.remove('je-tab-active'); }});
                    var target = document.getElementById('je-tab-' + tab.getAttribute('data-tab'));
                    if (target) target.classList.add('je-tab-active');
                }});
            }});

            // Helper: get emulator
            function emu() {{ return window.EJS_emulator; }}
            function gm()  {{ var e = emu(); return e ? e.gameManager : null; }}

            function _jeEnsureBinary(data) {{
                if (!data) return null;
                if (data instanceof Blob) return data;
                if (data instanceof Uint8Array) return new Blob([data], {{ type: 'application/octet-stream' }});
                if (data instanceof ArrayBuffer) return new Blob([new Uint8Array(data)], {{ type: 'application/octet-stream' }});
                if (typeof data === 'string') {{
                    try {{
                        var binary = window.atob(data);
                        var bytes = new Uint8Array(binary.length);
                        for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
                        return new Blob([bytes], {{ type: 'application/octet-stream' }});
                    }} catch(ex) {{ return new Blob([new TextEncoder().encode(data)], {{ type: 'application/octet-stream' }}); }}
                }}
                return new Blob([new TextEncoder().encode(JSON.stringify(data))], {{ type: 'application/octet-stream' }});
            }}

            // Focus management: EJS listens for keyboard on ejs_parent, not document
            function refocusGame() {{
                var e = emu();
                if (e && e.elements && e.elements.parent) {{
                    e.elements.parent.focus();
                }} else {{
                    var gp = document.querySelector('.ejs_parent');
                    if (gp) gp.focus();
                }}
            }}

            // Server sync for controls & settings
            var _syncTimer = null;
            function syncControlsToServer() {{
                clearTimeout(_syncTimer);
                _syncTimer = setTimeout(function() {{
                    var e = emu(); if (!e) return;
                    var c = e.controls && e.controls[0] ? e.controls[0] : {{}};
                    // Split keyboard (value) and gamepad (value2) bindings
                    var kb = {{}}, gp = {{}};
                    for (var k in c) {{
                        if (c[k].value !== undefined) {{
                            if (!kb[k]) kb[k] = {{}};
                            kb[k].value = c[k].value;
                        }}
                        if (c[k].value2 !== undefined) {{
                            if (!gp[k]) gp[k] = {{}};
                            gp[k].value2 = c[k].value2;
                        }}
                    }}
                    var payload = {{
                        controls: JSON.stringify(kb),
                        controllerControls: JSON.stringify(gp)
                    }};
                    // Also sync settings if available
                    if (e.settings) {{
                        if (e.settings['shader']) payload.shader = e.settings['shader'];
                        if (e.settings['videoRotation'] !== undefined) payload.videoRotation = parseInt(e.settings['videoRotation']) || 0;
                    }}
                    fetch('/jellyemu/prefs/{userId}', {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/json' }},
                        body: JSON.stringify(payload)
                    }}).catch(function(err) {{ console.warn('[JellyEmu] Controls sync failed:', err); }});
                }}, 800);
            }}

            // Exit button
            document.getElementById('je-exit-btn').addEventListener('click', function() {{
                if (window.EJS_onExit) {{ EJS_onExit(); }}
                else if (window.opener) {{ window.close(); }}
                else {{ window.parent.postMessage('close-jellyemu','*'); }}
            }});

            // Pause / Play
            var btnPause = document.getElementById('je-btn-pause');
            var btnPlay  = document.getElementById('je-btn-play');
            btnPause.addEventListener('click', function() {{
                var e = emu(); if (!e || !e.started) return;
                e.pause();
                btnPause.style.display = 'none'; btnPlay.style.display = '';
            }});
            btnPlay.addEventListener('click', function() {{
                var e = emu(); if (!e || !e.started) return;
                e.play();
                btnPlay.style.display = 'none'; btnPause.style.display = '';
            }});

            // Restart
            document.getElementById('je-btn-restart').addEventListener('click', function() {{
                var g = gm(); if (g) g.restart();
            }});

            // Fast Forward
            var ffActive = false;
            document.getElementById('je-btn-ff').addEventListener('click', function() {{
                var g = gm(); if (!g) return;
                ffActive = !ffActive;
                g.toggleFastForward(ffActive ? 1 : 0);
                this.classList.toggle('je-active', ffActive);
            }});

            // Slow Motion
            var slowActive = false;
            document.getElementById('je-btn-slow').addEventListener('click', function() {{
                var g = gm(); if (!g) return;
                slowActive = !slowActive;
                g.toggleSlowMotion(slowActive ? 1 : 0);
                this.classList.toggle('je-active', slowActive);
            }});

            // Save States popup
            document.getElementById('je-btn-saves').addEventListener('click', function() {{
                buildSaveSlots();
                openPopup('je-pop-saves');
            }});

            function buildSaveSlots() {{
                var body = document.getElementById('je-saves-body');
                body.innerHTML = '';
                for (var i = 1; i <= 5; i++) {{
                    var slot = document.createElement('div');
                    slot.className = 'je-slot';
                    slot.innerHTML = '<div class=""je-slot-num"">' + i + '</div>' +
                        '<div class=""je-slot-info""><div>Slot ' + i + '</div><small id=""je-slot-status-' + i + '"">Checking…</small></div>' +
                        '<div class=""je-slot-actions"">' +
                        '<button class=""je-btn"" data-save=""' + i + '"">Save</button>' +
                        '<button class=""je-btn je-btn-primary"" data-load=""' + i + '"">Load</button></div>';
                    body.appendChild(slot);
                    // Check if slot has data
                    (function(s) {{
                        fetch('/jellyemu/save/{itemId}/{userId}?slot=' + s, {{ method: 'HEAD' }})
                            .then(function(r) {{
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = r.ok ? 'Has save data' : 'Empty';
                            }}).catch(function() {{
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'Empty';
                            }});
                    }})(i);
                }}
                // Wire save/load button(s)
                body.querySelectorAll('[data-save]').forEach(function(btn) {{
                    btn.addEventListener('click', function() {{
                        var s = parseInt(btn.getAttribute('data-save'));
                        var g = gm(); if (!g) return;
                        Promise.resolve(g.getState()).then(function(rawState) {{
                            var state = _jeEnsureBinary(rawState);
                            if (!state) return;
                            console.log('[JellyEmu] Pipeline STAGE 1 (Client Gen): Payload size ->', state.size || state.byteLength, 'bytes');
                            // Upload save state to server
                            fetch('/jellyemu/save/{itemId}/{userId}?slot=' + s, {{
                                method: 'POST',
                                headers: {{ 'Content-Type': 'application/octet-stream' }},
                                body: state
                            }}).then(function(r) {{
                                if (!r.ok) throw new Error('Save rejected by server');
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'Saved!';
                                // Capture and upload screenshot for this slot
                                var canvas = document.querySelector('canvas.ejs_canvas') || document.querySelector('canvas');
                                if (canvas) {{
                                    try {{
                                        var dataUrl = canvas.toDataURL('image/png');
                                        fetch('/jellyemu/save-screenshot/{itemId}/{userId}/' + s, {{
                                            method: 'POST',
                                            headers: {{ 'Content-Type': 'application/json' }},
                                            body: JSON.stringify({{ dataUrl: dataUrl }})
                                        }}).catch(function(err) {{ console.warn('[JellyEmu] Screenshot upload failed:', err); }});
                                    }} catch(ex) {{ console.warn('[JellyEmu] Screenshot capture failed:', ex); }}
                                }}
                            }}).catch(function(err) {{
                                console.error('[JellyEmu] Save failed:', err);
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'Save Failed';
                            }});
                        }});
                    }});
                }});
                body.querySelectorAll('[data-load]').forEach(function(btn) {{
                    btn.addEventListener('click', function() {{
                        var s = parseInt(btn.getAttribute('data-load'));
                        fetch('/jellyemu/save/{itemId}/{userId}?slot=' + s)
                            .then(function(r) {{
                                if (!r.ok) throw new Error('No save');
                                return r.arrayBuffer();
                            }}).then(function(buf) {{
                                var g = gm(); if (!g) return;
                                console.log('[JellyEmu] Pipeline STAGE 4 (Client Receive): Downloaded bytes ->', buf.byteLength);
                                g.loadState(new Uint8Array(buf));
                                closePopup('je-pop-saves');
                            }}).catch(function() {{
                                var el = document.getElementById('je-slot-status-' + s);
                                if (el) el.textContent = 'No save to load';
                            }});
                    }});
                }});
            }}

            // Volume popup
            document.getElementById('je-btn-vol').addEventListener('click', function() {{
                var e = emu();
                if (e) {{
                    var slider = document.getElementById('je-vol-slider');
                    slider.value = e.volume;
                    document.getElementById('je-vol-pct').textContent = Math.round(e.volume * 100) + '%';
                }}
                openPopup('je-pop-vol');
            }});
            document.getElementById('je-vol-slider').addEventListener('input', function() {{
                var v = parseFloat(this.value);
                document.getElementById('je-vol-pct').textContent = Math.round(v * 100) + '%';
                var e = emu();
                if (e) {{ e.volume = v; e.muted = false; e.setVolume(v); }}
            }});
            document.getElementById('je-vol-mute').addEventListener('click', function() {{
                var e = emu(); if (!e) return;
                e.muted = !e.muted;
                e.setVolume(e.muted ? 0 : e.volume);
                this.textContent = e.muted ? 'Unmute' : 'Mute';
                document.getElementById('je-vol-pct').textContent = e.muted ? '0%' : Math.round(e.volume * 100) + '%';
            }});

            // Cheats popup
            document.getElementById('je-btn-cheats').addEventListener('click', function() {{
                buildCheats();
                openPopup('je-pop-cheats');
            }});
            function buildCheats() {{
                var e = emu(); if (!e) return;
                var list = document.getElementById('je-cheat-list');
                list.innerHTML = '';
                for (var i = 0; i < e.cheats.length; i++) {{
                    (function(idx) {{
                        var ch = e.cheats[idx];
                        var row = document.createElement('div');
                        row.className = 'je-cheat-row';
                        row.innerHTML = '<label class=""je-toggle""><input type=""checkbox""' + (ch.checked ? ' checked' : '') +
                            '><span class=""je-toggle-track""></span></label><span class=""je-cheat-name"">' +
                            ch.desc + '</span>' + (!ch.is_permanent ? '<button class=""je-cheat-del"">&times;</button>' : '');
                        var cb = row.querySelector('input');
                        cb.addEventListener('change', function() {{
                            e.cheats[idx].checked = cb.checked;
                            e.cheatChanged(cb.checked, ch.code, idx);
                            e.saveSettings();
                        }});
                        var del = row.querySelector('.je-cheat-del');
                        if (del) {{
                            del.addEventListener('click', function() {{
                                e.cheatChanged(false, ch.code, idx);
                                e.cheats.splice(idx, 1);
                                e.updateCheatUI();
                                e.saveSettings();
                                buildCheats();
                            }});
                        }}
                        list.appendChild(row);
                    }})(i);
                }}
                if (e.cheats.length === 0) {{ list.innerHTML = '<div style=""opacity:.4;font-size:13px"">No cheats loaded</div>'; }}
            }}
            document.getElementById('je-cheat-add').addEventListener('click', function() {{
                var name = document.getElementById('je-cheat-name').value.trim();
                var code = document.getElementById('je-cheat-code').value.trim();
                if (!name || !code) return;
                var e = emu(); if (!e) return;
                e.cheats.push({{ desc: name, code: code, checked: false }});
                e.updateCheatUI();
                e.saveSettings();
                document.getElementById('je-cheat-name').value = '';
                document.getElementById('je-cheat-code').value = '';
                buildCheats();
            }});

            // EJS uses numeric button indices internally
            var inputButtons = [0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29];
            var inputLabels  = ['B','Y','Select','Start','D-Up','D-Down','D-Left','D-Right','A','X','L','R','L2','R2','L3','R3',
                'L-Stick Right','L-Stick Left','L-Stick Down','L-Stick Up','R-Stick Right','R-Stick Left','R-Stick Down','R-Stick Up',
                'Quick Save','Quick Load','Change State','FF','Rewind','Slow-Mo'];

            document.getElementById('je-btn-inputmap').addEventListener('click', function() {{
                buildKeyboardBinds();
                buildGamepadBinds();
                syncVGToggles();
                openPopup('je-pop-inputmap');
            }});

            function buildKeyboardBinds() {{
                var panel = document.getElementById('je-tab-kb');
                panel.innerHTML = '';
                var e = emu(); if (!e) return;
                var c = (e.controls && e.controls[0]) || {{}};
                // EJS keyMap: keyCode(number) → friendly name(string)
                var km = e.keyMap || {{}};
                function keyName(code) {{
                    if (code === undefined || code === null || code === 0) return '—';
                    if (typeof code === 'number') return km[code] || ('key ' + code);
                    return String(code);
                }}
                for (var i = 0; i < inputButtons.length; i++) {{
                    (function(idx) {{
                        var key = inputButtons[idx];
                        var row = document.createElement('div');
                        row.className = 'je-bind-row';
                        var rawVal = (c[key] && c[key].value !== undefined) ? c[key].value : null;
                        var displayName = rawVal !== null ? keyName(rawVal) : '—';
                        row.innerHTML = '<span>' + inputLabels[idx] + '</span><span class=""je-bind-key"" data-btn=""' + key + '"">' + displayName + '</span>';
                        var bk = row.querySelector('.je-bind-key');
                        bk.addEventListener('click', function() {{
                            if (bk.classList.contains('je-listening')) return;
                            bk.classList.add('je-listening');
                            bk.textContent = 'Press a key…';
                            function onKey(ev) {{
                                ev.preventDefault();
                                ev.stopPropagation();
                                if (ev.keyCode === 27) {{ bk.textContent = displayName; bk.classList.remove('je-listening'); document.removeEventListener('keydown', onKey, true); return; }}
                                var kc = ev.keyCode;
                                bk.textContent = keyName(kc);
                                bk.classList.remove('je-listening');
                                document.removeEventListener('keydown', onKey, true);
                                if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                                if (!e.controls[0]) e.controls[0] = {{}};
                                if (!e.controls[0][key]) e.controls[0][key] = {{}};
                                e.controls[0][key].value = kc;
                                e.saveSettings();
                                syncControlsToServer();
                            }}
                            document.addEventListener('keydown', onKey, true);
                        }});
                        panel.appendChild(row);
                    }})(i);
                }}
            }}

            function buildGamepadBinds() {{
                var panel = document.getElementById('je-gp-binds');
                panel.innerHTML = '';
                // Detect gamepad
                var gps = navigator.getGamepads ? navigator.getGamepads() : [];
                var gp = null;
                for (var g = 0; g < gps.length; g++) {{ if (gps[g]) {{ gp = gps[g]; break; }} }}
                document.getElementById('je-gp-status').textContent = gp ? ('Detected: ' + gp.id) : 'No gamepad detected';
                var e = emu(); if (!e) return;
                var c = (e.controls && e.controls[0]) || {{}};

                // Friendly display for gamepad value2
                var gpLabels = {{
                    'BUTTON_1': 'A', 'BUTTON_2': 'B', 'BUTTON_3': 'X', 'BUTTON_4': 'Y',
                    'SELECT': 'Back', 'START': 'Start',
                    'LEFT_TOP_SHOULDER': 'LB', 'RIGHT_TOP_SHOULDER': 'RB',
                    'LEFT_BOTTOM_SHOULDER': 'LT', 'RIGHT_BOTTOM_SHOULDER': 'RT',
                    'LEFT_STICK': 'L3', 'RIGHT_STICK': 'R3',
                    'DPAD_UP': 'D-Up', 'DPAD_DOWN': 'D-Down', 'DPAD_LEFT': 'D-Left', 'DPAD_RIGHT': 'D-Right',
                    'LEFT_STICK_X:+1': 'L-Stick →', 'LEFT_STICK_X:-1': 'L-Stick ←',
                    'LEFT_STICK_Y:+1': 'L-Stick ↓', 'LEFT_STICK_Y:-1': 'L-Stick ↑',
                    'RIGHT_STICK_X:+1': 'R-Stick →', 'RIGHT_STICK_X:-1': 'R-Stick ←',
                    'RIGHT_STICK_Y:+1': 'R-Stick ↓', 'RIGHT_STICK_Y:-1': 'R-Stick ↑'
                }};
                function gpLabel(v) {{ return gpLabels[v] || v || '—'; }}

                for (var i = 0; i < inputButtons.length; i++) {{
                    (function(idx) {{
                        var key = inputButtons[idx];
                        var row = document.createElement('div');
                        row.className = 'je-bind-row';
                        var rawMapped = (c[key] && c[key].value2 !== undefined) ? c[key].value2 : null;
                        var displayMapped = rawMapped !== null ? gpLabel(String(rawMapped)) : '—';
                        row.innerHTML = '<span>' + inputLabels[idx] + '</span><span class=""je-bind-key"" data-btn=""' + key + '"">' + displayMapped + '</span>';
                        var bk = row.querySelector('.je-bind-key');
                        bk.addEventListener('click', function() {{
                            if (bk.classList.contains('je-listening')) return;
                            bk.classList.add('je-listening');
                            bk.textContent = 'Move stick or press…';
                            // Snapshot current axes to detect movement
                            var baseAxes = [];
                            var gps0 = navigator.getGamepads ? navigator.getGamepads() : [];
                            for (var gi0 = 0; gi0 < gps0.length; gi0++) {{
                                var p0 = gps0[gi0]; if (!p0) continue;
                                for (var ai0 = 0; ai0 < p0.axes.length; ai0++) {{ baseAxes[ai0] = p0.axes[ai0]; }}
                                break;
                            }}
                            var AXIS_THRESHOLD = 0.5;
                            var pollId = setInterval(function() {{
                                var gps2 = navigator.getGamepads ? navigator.getGamepads() : [];
                                for (var gi = 0; gi < gps2.length; gi++) {{
                                    var pad = gps2[gi]; if (!pad) continue;
                                    // Check buttons first
                                    for (var bi = 0; bi < pad.buttons.length; bi++) {{
                                        if (pad.buttons[bi].pressed) {{
                                            clearInterval(pollId);
                                            // Map standard gamepad buttons to EJS names
                                            var btnMap = ['BUTTON_2','BUTTON_4','BUTTON_1','BUTTON_3',
                                                'LEFT_TOP_SHOULDER','RIGHT_TOP_SHOULDER','LEFT_BOTTOM_SHOULDER','RIGHT_BOTTOM_SHOULDER',
                                                'SELECT','START','LEFT_STICK','RIGHT_STICK',
                                                'DPAD_UP','DPAD_DOWN','DPAD_LEFT','DPAD_RIGHT'];
                                            var ejsVal = bi < btnMap.length ? btnMap[bi] : ('BUTTON_' + bi);
                                            bk.textContent = gpLabel(ejsVal);
                                            bk.classList.remove('je-listening');
                                            if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                                            if (!e.controls[0]) e.controls[0] = {{}};
                                            if (!e.controls[0][key]) e.controls[0][key] = {{}};
                                            e.controls[0][key].value2 = ejsVal;
                                            e.saveSettings();
                                            syncControlsToServer();
                                            return;
                                        }}
                                    }}
                                    // Check axes (analog sticks)
                                    for (var ai = 0; ai < pad.axes.length; ai++) {{
                                        var base = baseAxes[ai] || 0;
                                        var val = pad.axes[ai];
                                        if (Math.abs(val - base) > AXIS_THRESHOLD) {{
                                            clearInterval(pollId);
                                            // Map axis index + direction to EJS names
                                            // Standard: 0=LX, 1=LY, 2=RX, 3=RY
                                            var axisNames = ['LEFT_STICK_X','LEFT_STICK_Y','RIGHT_STICK_X','RIGHT_STICK_Y'];
                                            var axisName = ai < axisNames.length ? axisNames[ai] : ('AXIS_' + ai);
                                            var dir = val > base ? ':+1' : ':-1';
                                            var ejsVal = axisName + dir;
                                            bk.textContent = gpLabel(ejsVal);
                                            bk.classList.remove('je-listening');
                                            if (!e.controls) e.controls = {{ 0: {{}}, 1: {{}}, 2: {{}}, 3: {{}} }};
                                            if (!e.controls[0]) e.controls[0] = {{}};
                                            if (!e.controls[0][key]) e.controls[0][key] = {{}};
                                            e.controls[0][key].value2 = ejsVal;
                                            e.saveSettings();
                                            syncControlsToServer();
                                            return;
                                        }}
                                    }}
                                }}
                            }}, 100);
                            // Timeout after 10s
                            setTimeout(function() {{ clearInterval(pollId); bk.classList.remove('je-listening'); bk.textContent = displayMapped; }}, 10000);
                        }});
                        panel.appendChild(row);
                    }})(i);
                }}
            }}

            function syncVGToggles() {{
                var e = emu(); if (!e) return;
                var vgOn = document.getElementById('je-vg-toggle');
                if (e.virtualGamepad) {{ vgOn.checked = e.virtualGamepad.style.display !== 'none'; }}
            }}
            document.getElementById('je-vg-toggle').addEventListener('change', function() {{
                var e = emu(); if (!e || !e.toggleVirtualGamepad) return;
                e.toggleVirtualGamepad(this.checked);
            }});
            document.getElementById('je-vg-lefty').addEventListener('change', function() {{
                var e = emu(); if (!e || !e.toggleVirtualGamepadLeftHanded) return;
                e.toggleVirtualGamepadLeftHanded(this.checked);
            }});
            document.getElementById('je-input-reset').addEventListener('click', function() {{
                var e = emu(); if (!e) return;
                e.controls = JSON.parse(JSON.stringify(e.defaultControllers));
                e.saveSettings();
                syncControlsToServer();
                buildKeyboardBinds();
                buildGamepadBinds();
            }});

            // Screenshot
            document.getElementById('je-btn-screenshot').addEventListener('click', function() {{
                var g = gm(); if (!g) return;
                g.screenshot().then(function(pngBytes) {{
                    var blob = new Blob([pngBytes], {{ type: 'image/png' }});
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = (window.EJS_gameName || 'screenshot') + '.png';
                    a.click();
                    URL.revokeObjectURL(url);
                    // Also post to parent
                    try {{
                        var canvas = document.querySelector('canvas');
                        if (canvas) window.parent.postMessage({{ type: 'jellyemu-screenshot', itemId: '{itemId}', dataUrl: canvas.toDataURL('image/png') }}, '*');
                    }} catch(ex) {{}}
                }}).catch(function(err) {{ console.warn('[JellyEmu] Screenshot failed:', err); }});
            }});

            // Settings popup
            document.getElementById('je-btn-settings').addEventListener('click', function() {{
                syncSettingsUI();
                openPopup('je-pop-settings');
            }});
            function syncSettingsUI() {{
                var e = emu(); if (!e) return;
                var sel = document.getElementById('je-set-shader');
                // Sync current values
                if (e.settings) {{
                    if (e.settings['shader']) sel.value = e.settings['shader'];
                    if (e.settings['ff-ratio']) document.getElementById('je-set-ffrate').value = e.settings['ff-ratio'];
                    if (e.settings['sm-ratio']) document.getElementById('je-set-smrate').value = e.settings['sm-ratio'];
                    var rot = e.settings['videoRotation'];
                    if (rot !== undefined) document.getElementById('je-set-rotation').value = rot;
                    document.getElementById('je-set-vsync').checked = e.settings['vsync'] !== 'disabled';
                }}
            }}
            document.getElementById('je-set-shader').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('shader', this.value);
                syncControlsToServer();
            }});
            document.getElementById('je-set-vsync').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('vsync', this.checked ? 'enabled' : 'disabled');
                syncControlsToServer();
            }});
            document.getElementById('je-set-rotation').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('videoRotation', parseInt(this.value));
                syncControlsToServer();
            }});
            document.getElementById('je-set-ffrate').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('ff-ratio', this.value);
                syncControlsToServer();
            }});
            document.getElementById('je-set-smrate').addEventListener('change', function() {{
                var e = emu(); if (!e) return;
                e.changeSettingOption('sm-ratio', this.value);
                syncControlsToServer();
            }});

            // Screen Size
            document.getElementById('je-set-screensize').addEventListener('change', function() {{
                var val = this.value;
                var e = emu(); if (!e) return;
                var canvas = e.canvas || document.querySelector('canvas');
                var parent = canvas ? canvas.parentElement : null;
                if (!canvas || !parent) return;
                if (val === 'fit') {{
                    canvas.style.width = '100%';
                    canvas.style.height = '100%';
                    canvas.style.objectFit = 'contain';
                    parent.style.display = 'flex';
                    parent.style.alignItems = 'center';
                    parent.style.justifyContent = 'center';
                }} else {{
                    var w = e.gameManager ? e.gameManager.getVideoDimensions('width') : 256;
                    var h = e.gameManager ? e.gameManager.getVideoDimensions('height') : 224;
                    var mult = val === 'native' ? 1 : parseInt(val);
                    canvas.style.width = (w * mult) + 'px';
                    canvas.style.height = (h * mult) + 'px';
                    canvas.style.objectFit = '';
                    parent.style.display = 'flex';
                    parent.style.alignItems = 'center';
                    parent.style.justifyContent = 'center';
                }}
            }});

            // FPS Counter
            var fpsEl = document.getElementById('je-fps');
            var fpsOn = false;
            var fpsFrames = 0;
            var fpsLast = performance.now();
            function fpsLoop() {{
                if (!fpsOn) return;
                fpsFrames++;
                var now = performance.now();
                if (now - fpsLast >= 1000) {{
                    fpsEl.textContent = fpsFrames + ' FPS';
                    fpsFrames = 0;
                    fpsLast = now;
                }}
                requestAnimationFrame(fpsLoop);
            }}
            document.getElementById('je-set-fps').addEventListener('change', function() {{
                fpsOn = this.checked;
                if (fpsOn) {{
                    fpsEl.classList.add('je-active');
                    fpsFrames = 0;
                    fpsLast = performance.now();
                    requestAnimationFrame(fpsLoop);
                }} else {{
                    fpsEl.classList.remove('je-active');
                }}
            }});

            // Dock minimize / expand
            var dockMinimized = false;
            var dockMinBtn = document.getElementById('je-dock-min');
            // Add minimize button to end of dock
            var minDockBtn = document.createElement('button');
            minDockBtn.className = 'je-dockbtn';
            minDockBtn.title = 'Minimize';
            minDockBtn.innerHTML = '<svg viewBox=""0 0 24 24""><path d=""M19 13H5v-2h14v2z"" fill=""currentColor""/></svg>';
            dock.appendChild(minDockBtn);
            minDockBtn.addEventListener('click', function() {{
                dockMinimized = true;
                dock.classList.add('je-minimized');
                dockMinBtn.classList.add('je-active');
                dockMinBtn.classList.remove('je-hidden');
            }});
            dockMinBtn.addEventListener('click', function() {{
                dockMinimized = false;
                dock.classList.remove('je-minimized');
                dockMinBtn.classList.remove('je-active');
                showDocks();
            }});
            // Override showDocks to handle minimize FAB visibility
            var _origShowDocks = showDocks;
            showDocks = function() {{
                _origShowDocks();
                if (dockMinimized) {{
                    dockMinBtn.classList.remove('je-hidden');
                }}
            }};

            // Dock popup buttons
            document.getElementById('je-btn-inputmap').addEventListener('click', function() {{
                buildKeyboardBinds();
                buildGamepadBinds();
                syncVGToggles();
                openPopup('je-pop-inputmap');
            }});
        }})();
    </script>
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
            {(string.IsNullOrEmpty(activeShader) ? "" : $",\n            'shader': '{activeShader}'")}
        }};
        {(videoRotation != 0 ? $"window.EJS_videoRotation = {videoRotation};" : "// EJS_videoRotation: 0 (default, no rotation)")}
        {(core is "dos" or "psp" ? "window.EJS_threads = true;" : "// EJS_threads not required for this core")}

        // Inject saved key and/or gamepad bindings (or defaults)
        {((!string.IsNullOrWhiteSpace(savedControls) || !string.IsNullOrWhiteSpace(savedControllerControls))
            ? $@"window.EJS_defaultControls = {{
            0: Object.assign({{}}, {(string.IsNullOrWhiteSpace(savedControls) ? "{}" : savedControls)}, {(string.IsNullOrWhiteSpace(savedControllerControls) ? "{}" : savedControllerControls)}),
            1: {{}}, 2: {{}}, 3: {{}}
        }};"
            : @"window.EJS_defaultControls = {
            0: {
                0:  { 'value': 'x',           'value2': 'BUTTON_2' },
                1:  { 'value': 's',           'value2': 'BUTTON_4' },
                2:  { 'value': 'v',           'value2': 'SELECT' },
                3:  { 'value': 'enter',       'value2': 'START' },
                4:  { 'value': 'up arrow',    'value2': 'DPAD_UP' },
                5:  { 'value': 'down arrow',  'value2': 'DPAD_DOWN' },
                6:  { 'value': 'left arrow',  'value2': 'DPAD_LEFT' },
                7:  { 'value': 'right arrow', 'value2': 'DPAD_RIGHT' },
                8:  { 'value': 'z',           'value2': 'BUTTON_1' },
                9:  { 'value': 'a',           'value2': 'BUTTON_3' },
                10: { 'value': 'q',           'value2': 'LEFT_TOP_SHOULDER' },
                11: { 'value': 'e',           'value2': 'RIGHT_TOP_SHOULDER' },
                12: { 'value': 'tab',         'value2': 'LEFT_BOTTOM_SHOULDER' },
                13: { 'value': 'r',           'value2': 'RIGHT_BOTTOM_SHOULDER' },
                14: { 'value': '',            'value2': 'LEFT_STICK' },
                15: { 'value': '',            'value2': 'RIGHT_STICK' },
                16: { 'value': 'h',           'value2': 'LEFT_STICK_X:+1' },
                17: { 'value': 'f',           'value2': 'LEFT_STICK_X:-1' },
                18: { 'value': 'g',           'value2': 'LEFT_STICK_Y:+1' },
                19: { 'value': 't',           'value2': 'LEFT_STICK_Y:-1' },
                20: { 'value': 'l',           'value2': 'RIGHT_STICK_X:+1' },
                21: { 'value': 'j',           'value2': 'RIGHT_STICK_X:-1' },
                22: { 'value': 'k',           'value2': 'RIGHT_STICK_Y:+1' },
                23: { 'value': 'i',           'value2': 'RIGHT_STICK_Y:-1' },
                24: { 'value': '1' },
                25: { 'value': '2' },
                26: { 'value': '3' },
                27: { 'value': 'add' },
                28: { 'value': 'space' },
                29: { 'value': 'subtract' }
            },
            1: {}, 2: {}, 3: {}
        };")}

        {(!string.IsNullOrEmpty(igdbId) ? $"window.EJS_gameID = {igdbId};" : "")}
        {(!string.IsNullOrEmpty(cheatsJson) ? $"window.EJS_cheats = {cheatsJson};" : "")}
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
        // Capture the canvas and POST as save-state screenshot.
        // preserveDrawingBuffer:true is patched onto getContext above so
        // toDataURL works at any time, not just during the render tick.
        function _jePostCanvasScreenshot(savePostPromise) {{
            var dataUrl = null;
            try {{
                var canvas = document.querySelector('canvas.ejs_canvas') ||
                             document.querySelector('#game canvas') ||
                             document.querySelector('canvas');
                if (canvas && canvas.width > 0 && canvas.height > 0) {{
                    dataUrl = canvas.toDataURL('image/jpeg', 0.88);
                }}
            }} catch(e) {{ console.warn('[JellyEmu] Screenshot capture failed:', e); }}
            if (!dataUrl || !dataUrl.startsWith('data:image')) return;
            (savePostPromise || Promise.resolve()).then(function() {{
                fetch('/jellyemu/save-screenshot/{itemId}/{userId}/{activeSlot}', {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ dataUrl: dataUrl }})
                }}).catch(function() {{}});
            }});
        }}

        window.EJS_onSaveState = function(e) {{
            if (!e || !e.state) return;
            var state = _jeEnsureBinary(e.state);
            console.log('[JellyEmu] Pipeline STAGE 1 (Client AutoGen): Payload size ->', state.size || state.byteLength, 'bytes');
            // Capture canvas NOW — synchronously, before the fetch, so we get the
            // current game frame rather than whatever is on screen after the round-trip
            var savePromise = fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: state
            }}).then(function(r) {{
                if (!r.ok) throw new Error('Server rejected save');
                console.log('[JellyEmu] Save uploaded, status:', r.status);
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
            }}).catch(function(err) {{
                console.error('[JellyEmu] Save upload failed:', err);
            }});
            _jePostCanvasScreenshot(savePromise);
        }};
        // EJS_onSaveUpdate fires on battery/SRAM saves detected by hash comparison.
        window.EJS_onSaveUpdate = function(e) {{
            if (!e || !e.save) return;
            var save = _jeEnsureBinary(e.save);
            console.log('[JellyEmu] Pipeline STAGE 1 (Client SRAM Gen): Payload size ->', save.size || save.byteLength, 'bytes');
            var savePromise = fetch('{savePostUrl}', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/octet-stream' }},
                body: save
            }}).then(function(r) {{
                if (!r.ok) throw new Error('Server rejected save');
                try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
            }}).catch(function(err) {{
                console.error('[JellyEmu] Save update upload failed:', err);
            }});
            _jePostCanvasScreenshot(savePromise);
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

            function _fallbackClose() {{
                Promise.all([sessionStop, playtimeFlush]).finally(function() {{
                    try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                    closeIframe();
                }});
            }}

            if (!autoSave) {{
                _fallbackClose();
                return;
            }}
            
            try {{
                if (!window.EJS_emulator || !window.EJS_emulator.gameManager) throw new Error('EJS_emulator not fully initialized');
                
                EJS_emulator.gameManager.saveSaveFiles();
                Promise.resolve(EJS_emulator.gameManager.getSaveFile()).then(function(rawState) {{
                    var stateData = _jeEnsureBinary(rawState);
                    if (!stateData) {{
                        _fallbackClose();
                        return;
                    }}
                    // Capture before the fetch so rAF fires on the current frame
                    var saveFlush = fetch('{savePostUrl}', {{
                        method: 'POST',
                        headers: {{ 'Content-Type': 'application/octet-stream' }},
                        body: stateData
                    }}).then(function(r) {{
                        if (!r.ok) throw new Error('Server rejected save');
                        try {{ window.parent.postMessage({{ type: 'jellyemu-save-written', itemId: '{itemId}' }}, '*'); }} catch(_) {{}}
                    }}).catch(function(err) {{
                        console.error('[JellyEmu] Auto-save on exit failed:', err);
                    }});
                    _jePostCanvasScreenshot(saveFlush);
                    Promise.all([sessionStop, playtimeFlush, saveFlush]).finally(function() {{
                        // Notify parent of session end for Romm playtime reporting
                        try {{ window.parent.postMessage({{ type: 'jellyemu-session-end', itemId: '{itemId}', seconds: sessionSeconds }}, '*'); }} catch(_) {{}}
                        closeIframe();
                    }});
                }}).catch(function(err) {{
                    console.warn('[JellyEmu] Promise.resolve(getSaveFile) failed', err);
                    _fallbackClose();
                }});
            }} catch (ex) {{
                console.warn('[JellyEmu] Auto-save sequence crashed, triggering fallback exit:', ex);
                _fallbackClose();
            }}
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
        /// Returns the cheats JSON string for inline injection into the EJS launch page,
        /// or null if no cheats are available. Handles caching internally.
        /// </summary>
        /// <summary>
        /// Strips No-Intro parenthetical tokens from a filename — regions, revisions,
        /// publisher tags, disc numbers, cheat-device labels, etc. — leaving just the
        /// bare game title for comparison.
        /// e.g. "Super Mario Bros (USA) (Rev 1)" → "super mario bros"
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex ParenRegex =
            new(@"\s*\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string StripParens(string name) =>
            ParenRegex.Replace(name, "").Trim().ToLowerInvariant();

        /// <summary>
        /// Fetches and caches the list of .cht filenames for a system folder from the
        /// GitHub Contents API. Cache lifetime is 30 days — the libretro database
        /// doesn't change frequently enough to warrant shorter.
        /// Returns bare filenames (without path), or null on failure.
        /// </summary>
        private async Task<List<string>?> GetSystemCheatListAsync(
            string dbFolder, IHttpClientFactory httpClientFactory)
        {
            var cacheDir = Path.Combine(_appPaths.DataPath, "jellyemu-cheats", "index");
            Directory.CreateDirectory(cacheDir);
            // Safe filename from folder name
            var safeName = string.Concat(dbFolder.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var cacheFile = Path.Combine(cacheDir, safeName + ".json");

            if (System.IO.File.Exists(cacheFile) &&
                (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(cacheFile)).TotalDays < 30)
            {
                try
                {
                    var cached = await System.IO.File.ReadAllTextAsync(cacheFile);
                    return System.Text.Json.JsonSerializer.Deserialize<List<string>>(cached);
                }
                catch { /* fall through to re-fetch */ }
            }

            try
            {
                var encoded = Uri.EscapeDataString(dbFolder);
                var url = $"https://api.github.com/repos/libretro/libretro-database/contents/cht/{encoded}";
                var client = httpClientFactory.CreateClient("JellyEmuCheats");
                // GitHub API requires a User-Agent header
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "JellyEmu-Plugin");
                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[JellyEmu] GitHub Contents API returned {Status} for {Folder}",
                        response.StatusCode, dbFolder);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);

                var names = doc.RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("name", out var n) &&
                                n.GetString()?.EndsWith(".cht", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(e => e.GetProperty("name").GetString()!)
                    .ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(names);
                await System.IO.File.WriteAllTextAsync(cacheFile, json);
                _logger.LogInformation("[JellyEmu] Cached {Count} cheat entries for {Folder}", names.Count, dbFolder);
                return names;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Failed to fetch cheat index for {Folder}", dbFolder);
                return null;
            }
        }

        /// <summary>
        /// Finds the best-matching .cht filename from a system's listing for the given
        /// ROM name, using stripped-paren comparison. Returns null if no confident match.
        /// </summary>
        private static string? FuzzyMatchCht(string romName, List<string> candidates)
        {
            var stripped = StripParens(romName);
            if (string.IsNullOrWhiteSpace(stripped)) return null;

            // 1. Exact match after stripping parens from both sides
            foreach (var c in candidates)
            {
                if (StripParens(Path.GetFileNameWithoutExtension(c)) == stripped)
                    return c;
            }

            // 2. Candidate starts-with match — handles cases where the DB entry has
            //    extra subtitle tokens the ROM name omits, e.g. "Zelda" vs "Zelda - A Link to the Past"
            var startsWith = candidates
                .Where(c => StripParens(Path.GetFileNameWithoutExtension(c)).StartsWith(stripped,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Length) // prefer shortest (fewest extra tokens)
                .FirstOrDefault();
            if (startsWith != null) return startsWith;

            // 3. ROM-name starts-with candidate — ROM has more info than DB entry
            var romStartsWith = candidates
                .Where(c => stripped.StartsWith(
                    StripParens(Path.GetFileNameWithoutExtension(c)),
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Length) // prefer longest match
                .FirstOrDefault();
            return romStartsWith;
        }

        /// <summary>
        /// Resolves and returns the EJS-ready cheats JSON for a given item,
        /// using fuzzy filename matching against the libretro cheat database.
        /// Results are cached to disk for 7 days per item.
        /// </summary>
        private async Task<string?> GetCheatsJsonAsync(MediaBrowser.Controller.Entities.BaseItem item,
            IHttpClientFactory httpClientFactory)
        {
            var consoleTags = (item.Tags ?? Array.Empty<string>())
                .Where(t => CheatDbFolderMap.ContainsKey(t))
                .ToList();
            if (consoleTags.Count == 0) return null;

            var dbFolder = CheatDbFolderMap[consoleTags[0]];
            var romName = Path.GetFileNameWithoutExtension(item.Path ?? item.Name ?? "");

            var cacheDir = Path.Combine(_appPaths.DataPath, "jellyemu-cheats");
            Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, item.Id + ".json");

            if (System.IO.File.Exists(cacheFile) &&
                (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(cacheFile)).TotalDays < 7)
            {
                var cached = await System.IO.File.ReadAllTextAsync(cacheFile);
                return cached == "[]" ? null : cached;
            }

            try
            {
                // Step 1: get the directory listing for this system (cached 30 days)
                var candidates = await GetSystemCheatListAsync(dbFolder, httpClientFactory);
                if (candidates == null || candidates.Count == 0)
                {
                    await System.IO.File.WriteAllTextAsync(cacheFile, "[]");
                    return null;
                }

                // Step 2: fuzzy-match the ROM name against the listing
                var matched = FuzzyMatchCht(romName, candidates);
                if (matched == null)
                {
                    _logger.LogDebug("[JellyEmu] No cheat match for '{Rom}' in {Folder}", romName, dbFolder);
                    await System.IO.File.WriteAllTextAsync(cacheFile, "[]");
                    return null;
                }

                _logger.LogInformation("[JellyEmu] Matched '{Rom}' → '{Matched}'", romName, matched);

                // Step 3: fetch the matched .cht file
                var encodedFolder = Uri.EscapeDataString(dbFolder);
                var encodedFile = Uri.EscapeDataString(matched);
                var url = $"https://raw.githubusercontent.com/libretro/libretro-database/master/cht/{encodedFolder}/{encodedFile}";

                var client = httpClientFactory.CreateClient("JellyEmuCheats");
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    await System.IO.File.WriteAllTextAsync(cacheFile, "[]");
                    return null;
                }

                var chtText = await response.Content.ReadAsStringAsync();
                var cheats = ParseChtFile(chtText);
                var json = System.Text.Json.JsonSerializer.Serialize(cheats);
                await System.IO.File.WriteAllTextAsync(cacheFile, json);
                _logger.LogInformation("[JellyEmu] Loaded {Count} cheats for '{Rom}'", cheats.Count, romName);
                return cheats.Count > 0 ? json : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Failed to fetch cheats for '{Rom}'", romName);
                return null;
            }
        }

        /// <summary>
        /// Maps JellyEmu console tags to their folder names in the libretro cheat database.
        /// https://github.com/libretro/libretro-database/tree/master/cht
        /// </summary>
        private static readonly Dictionary<string, string> CheatDbFolderMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",              "Nintendo - Nintendo Entertainment System" },
                { "SNES",             "Nintendo - Super Nintendo Entertainment System" },
                { "N64",              "Nintendo - Nintendo 64" },
                { "Game Boy",         "Nintendo - Game Boy" },
                { "Game Boy Color",   "Nintendo - Game Boy Color" },
                { "Game Boy Advance", "Nintendo - Game Boy Advance" },
                { "Nintendo DS",      "Nintendo - Nintendo DS" },
                { "Virtual Boy",      "Nintendo - Virtual Boy" },
                { "Master System",    "Sega - Master System - Mark III" },
                { "Game Gear",        "Sega - Game Gear" },
                { "Sega Genesis",     "Sega - Mega Drive - Genesis" },
                { "Sega CD",          "Sega - Mega-CD - Sega CD" },
                { "Sega 32X",         "Sega - 32X" },
                { "PlayStation",      "Sony - PlayStation" },
                { "PSP",              "Sony - PlayStation Portable" },
                { "Atari 2600",       "Atari - 2600" },
                { "Atari 7800",       "Atari - 7800" },
                { "Atari Lynx",       "Atari - Lynx" },
                { "TurboGrafx-16",    "NEC - PC Engine - TurboGrafx 16" },
                { "ColecoVision",     "Coleco - ColecoVision" },
                { "NeoGeo Pocket",    "SNK - Neo Geo Pocket Color" },
                { "Arcade",           "FBNeo - Arcade Games" },
            };

        /// <summary>
        /// Fetches and parses cheats for a ROM from the libretro cheat database on GitHub.
        /// Results are cached to disk for 7 days so subsequent launches are instant.
        /// 
        /// Path: GET /jellyemu/cheats/{itemId}
        /// Returns: JSON array of [name, code] pairs, or empty array if no cheats found.
        /// </summary>
        [HttpGet("/jellyemu/cheats/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCheats(string itemId,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null) return Ok(Array.Empty<object>());

            var json = await GetCheatsJsonAsync(item, httpClientFactory);
            return Content(json ?? "[]", "application/json");
        }

        /// <summary>
        /// Parses a libretro .cht file into EJS-compatible [description, code, status] triples.
        /// EJS_cheats format: [[name, code, ""], ...] where "" = disabled, "+" = enabled.
        /// All cheats start disabled — the user enables them from the in-game cheat menu.
        /// </summary>
        private static List<string[]> ParseChtFile(string cht)
        {
            var result = new List<string[]>();
            var entries = new Dictionary<int, (string? Name, string? Code)>();

            foreach (var rawLine in cht.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.Contains('=')) continue;

                var eqIdx = line.IndexOf('=');
                var key = line[..eqIdx].Trim();
                var value = line[(eqIdx + 1)..].Trim().Trim('"');

                if (!key.StartsWith("cheat", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = key.Split('_', 2);
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0]["cheat".Length..], out var idx)) continue;

                var field = parts[1].ToLowerInvariant();
                if (!entries.ContainsKey(idx)) entries[idx] = (null, null);
                var entry = entries[idx];

                if (field == "desc") entries[idx] = (value, entry.Code);
                else if (field == "code") entries[idx] = (entry.Name, value);
            }

            foreach (var (_, (name, code)) in entries.OrderBy(e => e.Key))
            {
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                    result.Add(new[] { name, code, "" }); // "" = disabled by default
            }

            return result;
        }

        /// <summary>
        /// Returns lightweight card metadata for a batch of item IDs — only the fields
        /// JellyEmu actually needs for card badge rendering: Tags, CommunityRating, ProviderIds.
        /// This replaces N individual getItem calls with a single request per batch.
        ///
        /// Path: GET /jellyemu/cardmeta?ids=id1,id2,...
        /// Returns: JSON object keyed by item ID: { "id": { tags, communityRating, providerIds } }
        /// </summary>
        [HttpGet("/jellyemu/cardmeta")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult CardMeta([FromQuery] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return Ok(new { });

            var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Take(100) // hard cap — client should batch to ≤50 but protect server
                            .ToList();

            var result = new Dictionary<string, object>(idList.Count);

            foreach (var id in idList)
            {
                var item = _libraryManager.GetItemById(id);
                if (item == null) continue;

                result[id] = new
                {
                    tags = item.Tags ?? Array.Empty<string>(),
                    communityRating = item.CommunityRating,
                    providerIds = item.ProviderIds ?? new Dictionary<string, string>(),
                };
            }

            return new JsonResult(result);
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

            // For .cue files, EmulatorJS needs the actual binary track data.
            // Parse the .cue sheet and serve the first FILE entry's .bin instead.
            var servePath = item.Path;
            if (string.Equals(Path.GetExtension(item.Path), ".cue", StringComparison.OrdinalIgnoreCase))
            {
                var binPath = CueParser.GetFirstBinPath(item.Path);
                if (binPath != null)
                {
                    _logger.LogInformation("[JellyEmu] CUE sheet resolved to BIN: {Path}", binPath);
                    servePath = binPath;
                }
                else
                {
                    _logger.LogWarning("[JellyEmu] CUE sheet has no resolvable BIN track: {Path}", item.Path);
                }
            }

            _logger.LogInformation("[JellyEmu] Serving ROM: {Path}", servePath);

            var ext = Path.GetExtension(servePath).TrimStart('.').ToLowerInvariant();
            var mimeType = ext switch
            {
                "zip" => "application/zip",
                "7z" => "application/x-7z-compressed",
                "iso" => "application/x-iso9660-image",
                "cso" => "application/x-compressed",
                _ => "application/octet-stream"
            };

            var fileInfo = new System.IO.FileInfo(servePath);
            var lastModified = fileInfo.LastWriteTimeUtc;
            // ETag: size + last-modified ticks — unique per file version, no hashing required
            var etag = $"\"{fileInfo.Length}-{lastModified.Ticks}\"";

            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            Response.Headers["Content-Length"] = fileInfo.Length.ToString();
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(servePath)}\"";
            Response.Headers["ETag"] = etag;
            Response.Headers["Last-Modified"] = lastModified.ToString("R"); // RFC1123
            // ROMs are immutable in practice — allow EJS to cache for 7 days before re-validating
            Response.Headers["Cache-Control"] = "public, max-age=604800, must-revalidate";

            // Conditional GET/HEAD — return 304 if EJS already has a fresh cached copy
            var ifNoneMatch = Request.Headers["If-None-Match"].FirstOrDefault();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
                return StatusCode(304);

            if (DateTimeOffset.TryParseExact(
                    Request.Headers["If-Modified-Since"].FirstOrDefault() ?? "",
                    "R", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var ifModifiedSince)
                && lastModified <= ifModifiedSince.UtcDateTime)
                return StatusCode(304);

            if (HttpMethods.IsHead(Request.Method))
                return new FileContentResult(Array.Empty<byte>(), mimeType);

            var stream = System.IO.File.OpenRead(servePath);
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
        public IActionResult HeadSave(string itemId, string userId, [FromQuery] int? slot)
        {
            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slotNum);
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

            var fileInfo = new System.IO.FileInfo(path);
            _logger.LogInformation("[JellyEmu] Pipeline STAGE 3 (Server Send): Serving save for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)", itemId, userId, slotNum, fileInfo.Length);
            var stream = System.IO.File.OpenRead(path);
            return File(stream, "application/octet-stream", $"{itemId}.state");
        }

        /// <summary>
        /// Deletes the save state for a given user and item.
        /// 
        /// Path: DELETE /jellyemu/save/{itemId}/{userId}
        /// Parameters:
        ///   - itemId (string, path): The game ID.
        ///   - userId (string, path): The user ID.
        ///   - slot (int, query, optional): Specific slot to delete.
        /// Returns Example: `204 No Content` (if deleted) or `404 Not Found` (if it didn't exist)
        /// </summary>
        [HttpDelete("/jellyemu/save/{itemId}/{userId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult DeleteSave(string itemId, string userId, [FromQuery] int? slot)
        {
            var authenticatedUserId = User.FindFirstValue("Jellyfin-UserId") 
                                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            bool isValidAuthGuid = Guid.TryParse(authenticatedUserId, out Guid authUserGuid);
            bool isValidTargetGuid = Guid.TryParse(userId, out Guid targetUserGuid);

            if (string.IsNullOrEmpty(authenticatedUserId) || !isValidAuthGuid || !isValidTargetGuid || authUserGuid != targetUserGuid)
            {
                _logger.LogWarning("[JellyEmu] Unauthorized delete attempt.");
                return Forbid(); 
            }

            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slotNum);

            if (!System.IO.File.Exists(path))
            {
                _logger.LogInformation("[JellyEmu] Cannot delete: No save found for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NotFound();
            }

            try
            {
                System.IO.File.Delete(path);
                _logger.LogInformation("[JellyEmu] Successfully deleted save for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return NoContent();
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to delete save file for item {ItemId} user {UserId} slot {Slot}", itemId, userId, slotNum);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
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
        [DisableRequestSizeLimit]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PostSave(string itemId, string userId, [FromQuery] int? slot)
        {
            if (Request.ContentLength == 0)
                return BadRequest("Empty save body.");

            var slotNum = slot.HasValue ? slot.Value : ReadUserPrefs(userId).Slot;
            var path = GetSavePath(userId, itemId, slotNum);

            using (var fs = System.IO.File.Create(path))
            {
                await Request.Body.CopyToAsync(fs);
            }

            var writtenFile = new System.IO.FileInfo(path);
            if (writtenFile.Length < 50)
            {
                _logger.LogWarning("[JellyEmu] Save state for item {ItemId} user {UserId} slot {Slot} was suspiciously small ({Bytes} bytes). Deleting it.", itemId, userId, slotNum, writtenFile.Length);
                System.IO.File.Delete(path);
                return BadRequest("Save state was empty or corrupt.");
            }

            _logger.LogInformation("[JellyEmu] Pipeline STAGE 2 (Server Receive): Saved state for item {ItemId} user {UserId} slot {Slot} ({Bytes} bytes)",
                itemId, userId, slotNum, writtenFile.Length);

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
                    Controls: Str("controls", current.Controls),
                    ControllerControls: Str("controllerControls", current.ControllerControls));
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
                controllerControls = current.ControllerControls,
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
                        hasScreenshot = System.IO.File.Exists(GetSaveScreenshotPath(userId, itemId, slotNumber)),
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

        /// <summary>
        /// Returns the save-state screenshot as JSON { dataUrl: "data:image/png;base64,..." }.
        /// The frontend assigns dataUrl directly to img.src — no URL-as-image-src needed.
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
        /// Stores a save-state screenshot for a given user/item/slot.
        /// Body: { "dataUrl": "data:image/png;base64,..." }
        /// The dataUrl is stored as-is in a JSON file and decoded on read.
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
                _logger.LogInformation("[JellyEmu] Saved screenshot for item {ItemId} user {UserId} slot {Slot}",
                    itemId, userId, slot);
                return Ok(new { saved = true });
            }
            catch { return BadRequest("Could not read image data."); }
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