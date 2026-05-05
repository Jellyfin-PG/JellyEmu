using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
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
        public async Task<IActionResult> Play(string itemId, [FromQuery] string? userId, [FromQuery] int? slot,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null) return NotFound();

            var core = ResolveCore(item);

            return core switch
            {
                "pico8" => PlayPico8(itemId),
                _       => await PlayEjs(itemId, userId, slot, httpClientFactory)
            };
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
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult PlayPico8(string itemId)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] Pico8Play: item {ItemId} not found", itemId);
                return NotFound();
            }

            var ext = RomExtensions.GetPico8Extension(item);

            var cartUrl = $"/jellyemu/rom/{itemId}/{itemId}{ext}";
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
                cart_url = cartUrl
            });

            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";

            return Content(html, MediaTypeNames.Text.Html);
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
        [HttpGet("/jellyemu/ejs/play/{itemId}")]
        [Produces(MediaTypeNames.Text.Html)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PlayEjs(string itemId, [FromQuery] string? userId, [FromQuery] int? slot,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
            {
                Logger.LogWarning("[JellyEmu] Play: item {ItemId} not found", itemId);
                return NotFound();
            }

            var core = ResolveCore(item);
            var ext = !string.IsNullOrEmpty(item.Path) ? Path.GetExtension(item.Path) : ".zip";
            var romUrl = $"/jellyemu/rom/{itemId}/{itemId}{ext}";

            var hasSaves = !string.IsNullOrEmpty(userId);
            var userPrefs = hasSaves ? ReadUserPrefs(userId!) : new UserPrefs(1, string.Empty, 0);
            var fullPrefs = hasSaves ? ReadFullPrefs(userId!) : DefaultFullPrefs;
            var activeSlot = (slot.HasValue && slot.Value > 0) ? slot.Value : userPrefs.Slot;
            var activeShader = userPrefs.Shader;
            var videoRotation = userPrefs.VideoRotation;
            var saveGetUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";
            var savePostUrl = hasSaves ? $"/jellyemu/save/{itemId}/{userId}" : "";

            var saveExists = hasSaves && System.IO.File.Exists(GetSavePath(userId!, itemId, activeSlot));

            var igdbId = item.GetProviderId("IGDB");
            var netplayServer = Plugin.Instance?.Configuration.NetplayServer ?? string.Empty;
            var hasNetplay = !string.IsNullOrWhiteSpace(netplayServer);

            var gameName = HtmlEncoder.Default.Encode(item.Name);
            var ejsBase = EjsManager.IsReady
                ? $"/jellyemu/ejs"
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

            var html = template.Render(new
            {
                game_name = gameName,
                core = core,
                rom_url = romUrl,
                ejs_base = ejsBase,
                item_id = itemId,
                user_id = userId,
                active_slot = activeSlot,
                slot_value = slot ?? 0,
                has_saves = hasSaves,
                active_shader = activeShader ?? string.Empty,
                video_rotation = videoRotation,
                igdb_id = igdbId ?? string.Empty,
                has_netplay = hasNetplay,
                netplay_server = netplayServer,
                save_exists = saveExists,
                save_get_url = saveGetUrl,
                save_post_url = savePostUrl,
                is_dos_or_psp = (core == "dos" || core == "psp")
            });

            // When opened as a new tab (threaded cores), these headers make the page
            // cross-origin isolated so SharedArrayBuffer is available. Harmless for iframe mode.
            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";

            return Content(html, MediaTypeNames.Text.Html);
        }
    }
}