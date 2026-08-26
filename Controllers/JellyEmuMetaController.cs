using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
using System.Text.Json;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Handles external metadata fetching e.g. RetroAchievements.
    /// Routes: /jellyemu/meta/*
    /// </summary>
    public class JellyEmuMetaController : JellyEmuBaseController
    {
        public JellyEmuMetaController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuMetaController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Returns lightweight card metadata for a batch of item IDs (tags, rating, providerIds).
        /// Replaces N individual getItem calls with a single request per batch (max 100 IDs).
        /// Path: GET /jellyemu/cardmeta?ids=id1,id2,...
        /// </summary>
        [HttpGet("/jellyemu/cardmeta")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult CardMeta([FromQuery] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return Ok(new { });

            var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Take(100)
                            .ToList();

            var result = new Dictionary<string, object>(idList.Count);
            foreach (var id in idList)
            {
                var item = LibraryManager.GetItemById(id);
                if (item == null) continue;
                result[id] = new
                {
                    tags            = item.Tags ?? Array.Empty<string>(),
                    communityRating = item.CommunityRating,
                    providerIds     = item.ProviderIds ?? new Dictionary<string, string>(),
                };
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Returns all supported console systems and their available emulation cores.
        /// Path: GET /jellyemu/systems
        /// </summary>
        [HttpGet("/jellyemu/systems")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetSystems()
        {
            var systems = PlatformCoreRegistry.Select(kvp => new
            {
                name = kvp.Key,
                cores = kvp.Value.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    needsThreads = c.NeedsThreads
                })
            });

            return Ok(new
            {
                systems,
                platformCoreMap = PlatformCoreRegistry
            });
        }

        public record ShaderOption(string Id, string Label);

        public static readonly List<ShaderOption> AvailableShaders = new()
        {
            new("disabled", "None"),
            new("2xScaleHQ.glslp", "2x ScaleHQ"),
            new("4xScaleHQ.glslp", "4x ScaleHQ"),
            new("sabr", "SABR"),
            new("crt-aperture.glslp", "CRT Aperture"),
            new("crt-easymode.glslp", "CRT Easymode"),
            new("crt-geom.glslp", "CRT Geom"),
            new("crt-mattias.glslp", "CRT Mattias"),
            new("crt-beam", "CRT Beam"),
            new("crt-caligari", "CRT Caligari"),
            new("crt-lottes", "CRT Lottes"),
            new("crt-zfast", "CRT ZFast"),
            new("crt-yeetron", "CRT Yeetron"),
            new("bicubic", "Bicubic"),
            new("mix-frames", "Mix Frames")
        };

        /// <summary>
        /// Returns the canonical list of supported emulator shaders.
        /// Path: GET /jellyemu/shaders
        /// </summary>
        [HttpGet("/jellyemu/shaders")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetShaders()
        {
            return Ok(AvailableShaders.Select(s => new { id = s.Id, label = s.Label }));
        }
    }
}
