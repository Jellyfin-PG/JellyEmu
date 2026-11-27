using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using JellyEmu.Utilities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;
using Scriban;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves the EmulatorJS HTML play page, ROM files, and core resolution.
    /// Routes: /jellyemu/play/*, /jellyemu/rom/*, /jellyemu/core/*
    /// </summary>
    public class JellyEmuPlayController : JellyEmuBaseController
    {
        public JellyEmuPlayController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuPlayController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        [HttpGet("/jellyemu/play/{itemId}")]
        public async Task<IActionResult> Play(string itemId, [FromQuery] string? userId, [FromQuery] int? slot, [FromQuery] string? core,
            [FromQuery] string? engine, [FromServices] IHttpClientFactory httpClientFactory)
        {
            if (!IsValidId(itemId) || (!string.IsNullOrEmpty(userId) && !IsValidId(userId)))
                return BadRequest("Invalid item or user ID.");

            var item = LibraryManager.GetItemById(itemId);
            if (item == null) return NotFound();

            var resolvedCore = ResolveCore(item, userId, core);

            // Allow legacy fallback query parameter ?engine=legacy if requested
            if (string.Equals(engine, "legacy", StringComparison.OrdinalIgnoreCase))
            {
                return resolvedCore switch
                {
                    "pico8" => PlayPico8(itemId, userId),
                    "play"  => await PlayPlay(itemId, userId, slot),
                    _       => await PlayEjs(itemId, userId, slot, core, httpClientFactory)
                };
            }

            return await PlayRetroKit(item, userId, slot, resolvedCore);
        }

        private async Task<IActionResult> PlayRetroKit(MediaBrowser.Controller.Entities.BaseItem item, string? userId, int? slot, string? resolvedCore)
        {
            var itemId = item.Id.ToString("N");
            var platformTag = ResolvePlatformTag(item);
            var isDirectoryItem = !string.IsNullOrEmpty(item.Path) && Directory.Exists(item.Path);
            var isCueItem = !string.IsNullOrEmpty(item.Path) && item.Path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase);
            var isM3u = !string.IsNullOrEmpty(item.Path) && (item.Path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || item.Path.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase));

            var effectivePath = !string.IsNullOrEmpty(item.Path) ? RomExtensions.EffectiveRomPath(item.Path) : string.Empty;
            var ext = !string.IsNullOrEmpty(effectivePath) && !Directory.Exists(effectivePath)
                ? Path.GetExtension(effectivePath)
                : ".bin";
            if (isDirectoryItem && string.IsNullOrEmpty(Path.GetExtension(effectivePath)))
            {
                var fileService = HttpContext.RequestServices.GetService(typeof(JellyEmuFileService)) as JellyEmuFileService;
                var dirFiles = fileService?.ResolveAllRomFiles(item.Path) ?? new List<string>();
                if (dirFiles.Count > 0)
                {
                    var primaryDirFile = dirFiles
                        .OrderBy(p => Path.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase) ? 0 : (PlatformResolver.AllRomExtensions.Contains(Path.GetExtension(p)) ? 1 : 2))
                        .First();
                    ext = Path.GetExtension(primaryDirFile);
                }
            }
            if (string.IsNullOrEmpty(ext)) ext = ".bin";

            var filename = !string.IsNullOrEmpty(item.Path) ? Path.GetFileNameWithoutExtension(item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : itemId;
            var cleanFilename = CleanCosmeticFilename(filename);
            if (string.IsNullOrWhiteSpace(cleanFilename)) cleanFilename = itemId;

            var romUrl = ToAppUrl($"jellyemu/rom/{itemId}/{cleanFilename}{ext}");
            if (!string.IsNullOrEmpty(userId)) romUrl += $"?userId={userId}";

            var hasAdditionalFiles = isDirectoryItem || isCueItem || isM3u;
            var hasSaves = !string.IsNullOrEmpty(userId);
            var effectivePrefs = hasSaves
                ? await PreferenceService.GetEffectivePreferencesAsync(userId!, platformTag)
                : JellyEmuPreferenceService.SystemDefaults;

            var activeSlot = Math.Max(1, slot ?? 1);
            var activeShader = effectivePrefs.Shader ?? "crt-easymode";
            var videoRotation = effectivePrefs.VideoRotation;
            var saveGetUrl = hasSaves ? ToAppUrl($"jellyemu/save/{itemId}/{userId}") : "";
            var savePostUrl = hasSaves ? ToAppUrl($"jellyemu/save/{itemId}/{userId}") : "";

            var igdbId = item.GetProviderId("IGDB");
            int numericGameId;
            if (!int.TryParse(igdbId, out numericGameId) || numericGameId <= 0)
            {
                numericGameId = (item.Id.GetHashCode() & 0x7FFFFFFF);
                if (numericGameId == 0) numericGameId = 1;
            }

            var netplayServer = ToAppUrl("jellyemu/netplay");
            var netplayIceServers = Plugin.Instance?.Configuration.NetplayIceServers ?? string.Empty;
            var netplayIceServersJson = System.Text.Json.JsonSerializer.Serialize(netplayIceServers);
            const bool hasNetplay = true;

            var biosService = HttpContext.RequestServices.GetService(typeof(JellyEmuBiosService)) as JellyEmuBiosService;
            var relBios = biosService?.ResolveBiosRelativePath(platformTag, resolvedCore ?? string.Empty);
            var biosUrl = !string.IsNullOrEmpty(relBios) ? ToAppUrl($"jellyemu/bios/file/{relBios}") : string.Empty;
            var dataBaseUrl = ToAppUrl("jellyemu/data");

            var availableCores = GetAvailableCoresForItem(item);
            var availableCoresJson = System.Text.Json.JsonSerializer.Serialize(availableCores.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                needsThreads = c.NeedsThreads
            }));

            var inputService = HttpContext.RequestServices.GetService(typeof(JellyEmuInputService)) as JellyEmuInputService;
            var inputScheme = inputService?.GetScheme(!string.IsNullOrWhiteSpace(platformTag) ? platformTag : resolvedCore);
            var inputSchemeJson = inputScheme != null
                ? System.Text.Json.JsonSerializer.Serialize(inputScheme, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
                : "null";

            var gameName = HtmlEncoder.Default.Encode(item.Name);

            var assembly = typeof(JellyEmuPlayController).Assembly;
            var resourceName = "JellyEmu.Templates.player.html";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogError("[JellyEmu] Player embedded template not found at {ResourceName}", resourceName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Template resource is missing.");
            }

            using StreamReader reader = new StreamReader(stream);
            var templateContent = reader.ReadToEnd();
            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                var errors = string.Join(", ", template.Messages);
                Logger.LogError("[JellyEmu] Player template parse error: {Errors}", errors);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error parsing Scriban template.");
            }

            var safeCore = MapLegacyCore(resolvedCore);
            if (string.IsNullOrEmpty(safeCore)) safeCore = "mgba";

            var html = template.Render(new
            {
                game_name = gameName,
                base_url = JsEnc(GetPathBase()),
                item_id = JsEnc(itemId),
                user_id = JsEnc(userId),
                platform_tag = JsEnc(platformTag),
                core = JsEnc(safeCore),
                rom_url = JsEnc(romUrl),
                bios_url = JsEnc(biosUrl),
                data_base_url = JsEnc(dataBaseUrl),
                available_cores_json = availableCoresJson,
                input_scheme_json = inputSchemeJson,
                save_get_url = JsEnc(saveGetUrl),
                save_post_url = JsEnc(savePostUrl),
                active_slot = activeSlot,
                slot_value = slot ?? 0,
                is_m3u = isM3u,
                has_additional_files = hasAdditionalFiles,
                has_netplay = hasNetplay,
                netplay_server = JsEnc(netplayServer),
                netplay_ice_servers_json = netplayIceServersJson,
                game_id = numericGameId,
                active_shader = JsEnc(activeShader),
                scale = JsEnc(effectivePrefs.Scale ?? "auto"),
                video_rotation = videoRotation,
                show_fps = JsEnc(effectivePrefs.ShowFps ?? "0"),
                ffrate = JsEnc(effectivePrefs.FfRate ?? "3"),
                smrate = JsEnc(effectivePrefs.SmRate ?? "3"),
                volume = SafeNumericString(effectivePrefs.Volume, "1"),
                mute = SafeBoolFlag(effectivePrefs.Mute),
                audio_eq_json = SafeJsonObject(effectivePrefs.AudioEq, "{}"),
                screenshots_to_library = !string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.ScreenshotsFolder),
                version = JsEnc(JellyEmuVersion.Value)
            });

            ApplyCrossOriginIsolationHeaders();
            return Content(html, MediaTypeNames.Text.Html);
        }

        /// <summary>
        /// Returns a standalone Play! PS2 HTML play page for the given item.
        /// Loads the Play! WebAssembly runtime and streams the disc via worker-based IO.
        ///
        /// Path: GET /jellyemu/play/play/{itemId}
        /// </summary>
        [HttpGet("/jellyemu/play/play/{itemId}")]
        [HttpGet("/jellyemu/ps2/play/{itemId}")]
        [Produces(MediaTypeNames.Text.Html)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PlayPlay(string itemId, [FromQuery] string? userId = null, [FromQuery] int? slot = null)
        {
            if (!IsValidId(itemId) || (!string.IsNullOrEmpty(userId) && !IsValidId(userId)))
                return BadRequest("Invalid item or user ID.");

            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] PlayPlay: item {ItemId} not found", SanitizeForLog(itemId));
                return NotFound();
            }

            var ext = !string.IsNullOrEmpty(item.Path) ? Path.GetExtension(item.Path) : ".iso";
            var filename = !string.IsNullOrEmpty(item.Path) ? Path.GetFileNameWithoutExtension(item.Path) : itemId;
            var cleanFilename = CleanCosmeticFilename(filename);
            if (string.IsNullOrWhiteSpace(cleanFilename)) cleanFilename = itemId;

            var romUrl = ToAppUrl($"jellyemu/rom/{itemId}/{cleanFilename}{ext}");
            if (!string.IsNullOrEmpty(userId)) romUrl += $"?userId={userId}";

            var hasSaves = !string.IsNullOrEmpty(userId);
            var activeSlot = Math.Max(1, slot ?? 1);
            var saveGetUrl = hasSaves ? ToAppUrl($"jellyemu/save/{itemId}/{userId}") : "";
            var savePostUrl = hasSaves ? ToAppUrl($"jellyemu/save/{itemId}/{userId}") : "";

            var platformTag = "PlayStation 2";
            var effectivePrefs = hasSaves
                ? await PreferenceService.GetEffectivePreferencesAsync(userId!, platformTag)
                : JellyEmuPreferenceService.SystemDefaults;

            var customBindingsJson = SafeJsonObject(effectivePrefs.Controls, "null");

            var gameName = HtmlEncoder.Default.Encode(item.Name);

            var inputService = HttpContext.RequestServices.GetService(typeof(JellyEmuInputService)) as JellyEmuInputService;
            var inputScheme = inputService?.GetScheme("ps2");
            var inputSchemeJson = inputScheme != null
                ? System.Text.Json.JsonSerializer.Serialize(inputScheme, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
                : "null";

            var assembly = typeof(JellyEmuPlayController).Assembly;
            var resourceName = "JellyEmu.Templates.play.html";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogError("[JellyEmu] Play! PS2 embedded template not found at {ResourceName}", resourceName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Template resource is missing.");
            }

            using StreamReader reader = new StreamReader(stream);
            var templateContent = reader.ReadToEnd();
            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                var errors = string.Join(", ", template.Messages);
                Logger.LogError("[JellyEmu] Play! PS2 template parse error: {Errors}", errors);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error parsing Scriban template.");
            }

            var html = template.Render(new
            {
                game_name = gameName,
                item_id = itemId,
                user_id = userId ?? string.Empty,
                base_url = GetPathBase(),
                platform_tag = platformTag,
                input_scheme_json = inputSchemeJson,
                rom_url = romUrl,
                save_get_url = saveGetUrl,
                save_post_url = savePostUrl,
                active_slot = activeSlot,
                custom_bindings_json = customBindingsJson,
                screenshots_to_library = !string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.ScreenshotsFolder),
                version = Plugin.Instance?.Version.ToString() ?? "1.0.8"
            });

            ApplyCrossOriginIsolationHeaders();

            return Content(html, MediaTypeNames.Text.Html);
        }

        /// <summary>
        /// Returns a standalone PICO-8 HTML play page for the given item.
        /// The page replicates the Lexaloffle BBS shell JS and loads the
        /// PICO-8 runtime from /jellyemu/pico8/runtime.js, passing the cart
        /// via Module.arguments exactly as the BBS does.
        ///
        /// Supports .p8 and .p8.png cart files.
        /// No save-state support — PICO-8 manages its own internal storage.
        ///
        /// Path: GET /jellyemu/pico8/play/{itemId}
        /// </summary>
        [HttpGet("/jellyemu/pico8/play/{itemId}")]
        [Produces(MediaTypeNames.Text.Html)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlayPico8(string itemId, [FromQuery] string? userId = null)
        {
            if (!IsValidId(itemId) || (!string.IsNullOrEmpty(userId) && !IsValidId(userId)))
                return BadRequest("Invalid item or user ID.");

            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] Pico8Play: item {ItemId} not found", SanitizeForLog(itemId));
                return NotFound();
            }

            var ext = RomExtensions.GetPico8Extension(item);

            var cartUrl = ToAppUrl($"jellyemu/rom/{itemId}/{itemId}{ext}");
            var gameName = HtmlEncoder.Default.Encode(item.Name);

            Logger.LogInformation("[JellyEmu] PICO-8 play: {Name} ({ItemId}) cart={CartUrl}",
                item.Name, itemId, cartUrl);

            var assembly = typeof(JellyEmuPlayController).Assembly;
            var resourceName = "JellyEmu.Templates.pico8.html";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogError("[JellyEmu] PICO-8 embedded template not found at {ResourceName}", resourceName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Template resource is missing.");
            }

            using StreamReader reader = new StreamReader(stream);
            var templateContent = reader.ReadToEnd();

            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                var errors = string.Join(", ", template.Messages);
                Logger.LogError("[JellyEmu] PICO-8 template parse error: {Errors}", errors);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error parsing Scriban template.");
            }

            var html = template.Render(new
            {
                game_name = gameName,
                base_url = GetPathBase(),
                cart_url = cartUrl,
                item_id = itemId,
                user_id = userId ?? string.Empty,
                version = JellyEmuVersion.Value,
                screenshots_to_library = !string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.ScreenshotsFolder)
            });

            ApplyCrossOriginIsolationHeaders();

            return Content(html, MediaTypeNames.Text.Html);
        }

        /// <summary>
        /// Returns a standalone EmulatorJS HTML page for the given item.
        /// The page itself is unauthenticated; the ROM fetch from /jellyemu/rom/{itemId} carries the user's token.
        /// 
        /// Path: GET /jellyemu/play/{itemId}
        /// Parameters: 
        ///   - itemId (string, path): The unique ID of the library item.
        ///   - userId (string, query, optional): Allows wire up of per-user save states.
        /// Returns Example: `200 OK` (Content-Type: text/html)
        /// </summary>
        [HttpGet("/jellyemu/ejs/play/{itemId}")]
        [Produces(MediaTypeNames.Text.Html)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> PlayEjs(string itemId, [FromQuery] string? userId, [FromQuery] int? slot, [FromQuery] string? core,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            if (!IsValidId(itemId) || (!string.IsNullOrEmpty(userId) && !IsValidId(userId)))
                return BadRequest("Invalid item or user ID.");

            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] Play: item {ItemId} not found", SanitizeForLog(itemId));
                return NotFound();
            }

            var resolvedCore = ResolveCore(item, userId, core);
            if (string.IsNullOrEmpty(resolvedCore))
            {
                Logger.LogWarning("[JellyEmu] Play: could not resolve a core for {Name} ({ItemId}), path={Path}",
                    item.Name, SanitizeForLog(itemId), item.Path);
                return UnprocessableEntity(
                    $"JellyEmu could not determine which emulator core to use for \"{item.Name}\". " +
                    "Tag the item with its console name (e.g. \"SNES\") or rename the ROM to use a recognised file extension.");
            }

            var platformTag = ResolvePlatformTag(item);
            var availableCores = GetAvailableCoresForItem(item);
            var availableCoresJson = System.Text.Json.JsonSerializer.Serialize(availableCores.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                needsThreads = c.NeedsThreads
            }));
            var ext = !string.IsNullOrEmpty(item.Path) ? Path.GetExtension(item.Path) : ".zip";
            var filename = !string.IsNullOrEmpty(item.Path) ? Path.GetFileNameWithoutExtension(item.Path) : itemId;
            var cleanFilename = CleanCosmeticFilename(filename);
            if (string.IsNullOrWhiteSpace(cleanFilename))
            {
                cleanFilename = itemId;
            }
            var romUrl = ToAppUrl($"jellyemu/rom/{itemId}/{cleanFilename}{ext}");
            if (!string.IsNullOrEmpty(userId))
            {
                romUrl += $"?userId={userId}";
            }

            var romError = GetN3dsRomError(item.Path, ref romUrl);

            var isJ3u = !string.IsNullOrEmpty(item.Path) && item.Path.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase);

            var hasSaves = !string.IsNullOrEmpty(userId);
            var effectivePrefs = hasSaves
                ? await PreferenceService.GetEffectivePreferencesAsync(userId!, platformTag)
                : JellyEmuPreferenceService.SystemDefaults;

            var activeSlot = Math.Max(1, slot ?? 1);
            var activeShader = effectivePrefs.Shader;
            var videoRotation = effectivePrefs.VideoRotation;
            var saveGetUrl = hasSaves ? ToAppUrl($"jellyemu/save/{itemId}/{userId}") : "";
            var savePostUrl = hasSaves ? ToAppUrl($"jellyemu/save/{itemId}/{userId}") : "";

            var saveExists = hasSaves && System.IO.File.Exists(GetSavePath(userId!, itemId, activeSlot));

            var igdbId = item.GetProviderId("IGDB");
            int numericGameId;
            if (!int.TryParse(igdbId, out numericGameId) || numericGameId <= 0)
            {
                numericGameId = (item.Id.GetHashCode() & 0x7FFFFFFF);
                if (numericGameId == 0) numericGameId = 1;
            }

            var netplayServer = ToAppUrl("jellyemu/netplay");
            var netplayIceServers = Plugin.Instance?.Configuration.NetplayIceServers ?? string.Empty;
            var netplayIceServersJson = System.Text.Json.JsonSerializer.Serialize(netplayIceServers);
            const bool hasNetplay = true;

            var gameName = HtmlEncoder.Default.Encode(item.Name);
            var ejsBase = EjsManager.IsReady
                ? ToAppUrl("jellyemu/ejs")
                : JellyEmuEjsManager.CdnBase;

            // Load the embedded Scriban template
            var assembly = typeof(JellyEmuPlayController).Assembly;
            var resourceName = "JellyEmu.Templates.ejs.html";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogError("[JellyEmu] EJS embedded template not found at {ResourceName}", resourceName);
                return StatusCode(StatusCodes.Status500InternalServerError, "EJS template resource is missing.");
            }

            using StreamReader reader = new StreamReader(stream);
            var templateContent = reader.ReadToEnd();
            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                var errors = string.Join(", ", template.Messages);
                Logger.LogError("[JellyEmu] EJS template parse error: {Errors}", errors);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error parsing EJS Scriban template.");
            }

            var biosService = HttpContext.RequestServices.GetService(typeof(JellyEmuBiosService)) as JellyEmuBiosService;
            var relBios = biosService?.ResolveBiosRelativePath(platformTag, resolvedCore);
            var biosUrl = !string.IsNullOrEmpty(relBios) ? ToAppUrl($"jellyemu/bios/file/{relBios}") : string.Empty;

            var inputService = HttpContext.RequestServices.GetService(typeof(JellyEmuInputService)) as JellyEmuInputService;
            var inputScheme = inputService?.GetScheme(platformTag ?? resolvedCore);
            var inputSchemeJson = inputScheme != null
                ? System.Text.Json.JsonSerializer.Serialize(inputScheme, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
                : "null";

            var customBindingsJson = SafeJsonObject(effectivePrefs.Controls, "null");

            var html = template.Render(new
            {
                game_name = gameName,
                base_url = JsEnc(GetPathBase()),
                core = JsEnc(MapLegacyCore(resolvedCore)),
                platform_tag = JsEnc(platformTag),
                input_scheme_json = inputSchemeJson,
                custom_bindings_json = customBindingsJson,
                available_cores_json = availableCoresJson,
                rom_url = JsEnc(romUrl),
                ejs_base = JsEnc(ejsBase),
                item_id = JsEnc(itemId),
                user_id = JsEnc(userId),
                bios_url = JsEnc(biosUrl),
                active_slot = activeSlot,
                slot_value = slot ?? 0,
                has_saves = hasSaves,
                active_shader = JsEnc(activeShader),
                video_rotation = videoRotation,
                igdb_id = JsEnc(igdbId),
                game_id = numericGameId,
                has_netplay = hasNetplay,
                netplay_server = JsEnc(netplayServer),
                netplay_ice_servers = JsEnc(netplayIceServers),
                netplay_ice_servers_json = netplayIceServersJson,
                save_exists = saveExists,
                save_get_url = JsEnc(saveGetUrl),
                save_post_url = JsEnc(savePostUrl),
                is_m3u = isJ3u,
                needs_threads = IsThreadedCore(resolvedCore),
                rom_error_json = romError == null ? "null" : System.Text.Json.JsonSerializer.Serialize(romError),
                virtual_gamepad = JsEnc(effectivePrefs.VirtualGamepad),
                virtual_gamepad_lefty = JsEnc(effectivePrefs.VirtualGamepadLefty),
                vsync = JsEnc(effectivePrefs.Vsync),
                ffrate = JsEnc(effectivePrefs.FfRate),
                smrate = JsEnc(effectivePrefs.SmRate),
                show_fps = JsEnc(effectivePrefs.ShowFps),
                scale = JsEnc(effectivePrefs.Scale),
                volume = SafeNumericString(effectivePrefs.Volume, "1"),
                mute = SafeBoolFlag(effectivePrefs.Mute),
                version = JsEnc(JellyEmuVersion.Value),
                screenshots_to_library = !string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.ScreenshotsFolder)
            });

            // When opened as a new tab (threaded cores), these headers make the page
            // cross-origin isolated so SharedArrayBuffer is available. Harmless for iframe mode.
            ApplyCrossOriginIsolationHeaders();

            return Content(html, MediaTypeNames.Text.Html);
        }

        private static string JsEnc(string? value)
            => JavaScriptEncoder.Default.Encode(value ?? string.Empty);

        private static string SafeNumericString(string? value, string fallback)
            => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : fallback;

        /// <summary>
        /// Re-serializes a stored JSON object for output inside a script block. The default
        /// encoder escapes &lt; &gt; &amp; and quotes, so "&lt;/script&gt;" cannot close the block.
        /// Anything that is not a JSON object yields <paramref name="fallback"/>.
        /// </summary>
        internal static string SafeJsonObject(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(value);
                return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? System.Text.Json.JsonSerializer.Serialize(doc.RootElement)
                    : fallback;
            }
            catch (System.Text.Json.JsonException)
            {
                return fallback;
            }
        }

        private static string SafeBoolFlag(string? value)
            => string.Equals(value, "1", StringComparison.Ordinal) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                ? "1"
                : "0";

        internal const string EncryptedN3dsRomMessage =
            "This Nintendo 3DS ROM is encrypted, and the Azahar core can only run decrypted games. " +
            "Replace it with a decrypted dump (.3ds/.cci, e.g. made with GodMode9) to play it.";

        /// <summary>
        /// Azahar shows a black screen, without any error, on encrypted 3DS ROMs. Detect them
        /// before launching so the player can explain what is wrong. For decrypted ROMs whose
        /// header is fixed while serving, a distinct ROM URL is used so that browsers which
        /// cached the unpatched copy (EmulatorJS caches ROMs by URL) download it again.
        /// </summary>
        /// <returns>A user-facing error message, or null when the ROM can be launched.</returns>
        internal string? GetN3dsRomError(string? romPath, ref string romUrl)
        {
            if (string.IsNullOrEmpty(romPath) || !N3dsRomInspector.IsCandidate(romPath) || !System.IO.File.Exists(romPath))
                return null;

            N3dsRomInspection inspection;
            try
            {
                inspection = N3dsRomInspector.Inspect(romPath);
            }
            catch (IOException ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Could not inspect 3DS ROM header of {Path}", romPath);
                return null;
            }

            switch (inspection.State)
            {
                case N3dsCryptoState.Encrypted:
                    Logger.LogWarning("[JellyEmu] Play: 3DS ROM {Path} is encrypted and cannot run in Azahar", romPath);
                    return EncryptedN3dsRomMessage;
                case N3dsCryptoState.DecryptedMissingFlag:
                    romUrl += (romUrl.Contains('?') ? "&" : "?") + "ncch=nocrypto";
                    return null;
                default:
                    return null;
            }
        }

        internal static string CleanCosmeticFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return string.Empty;

            // Remove characters that break URL routing or JavaScript string literals:
            // ', ", #, ?, &, \
            return filename
                .Replace("'", "")
                .Replace("\"", "")
                .Replace("#", "")
                .Replace("?", "")
                .Replace("&", "")
                .Replace("\\", "");
        }
    }
}