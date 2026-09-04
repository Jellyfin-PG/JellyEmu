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
        public record SelectOption(string Id, string Label);

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

        public static readonly List<SelectOption> AvailableScaling = new()
        {
            new("fit", "Fit Screen (Aspect Ratio)"),
            new("stretch", "Stretch to Fill"),
            new("1", "1x Native Resolution (Original Size)"),
            new("2", "2x Integer Scale"),
            new("3", "3x Integer Scale"),
            new("4", "4x Integer Scale")
        };

        public static readonly List<SelectOption> AvailableRotations = new()
        {
            new("0", "0° (Standard Landscape)"),
            new("90", "90° (Clockwise / Vertical TATE)"),
            new("180", "180° (Inverted)"),
            new("270", "270° (Counter-Clockwise)")
        };

        public static readonly List<SelectOption> AvailableFastForwardRates = new()
        {
            new("2", "2x"),
            new("3", "3x (Default)"),
            new("4", "4x"),
            new("5", "5x"),
            new("8", "8x"),
            new("10", "10x")
        };

        public static readonly List<SelectOption> AvailableSlowMotionRates = new()
        {
            new("2", "2x"),
            new("3", "3x (Default)"),
            new("4", "4x"),
            new("5", "5x")
        };

        public static readonly List<SelectOption> AvailableVolume = new()
        {
            new("1", "100% (Default)"),
            new("0.9", "90%"),
            new("0.8", "80%"),
            new("0.7", "70%"),
            new("0.6", "60%"),
            new("0.5", "50%"),
            new("0.4", "40%"),
            new("0.3", "30%"),
            new("0.2", "20%"),
            new("0.1", "10%"),
            new("0", "Muted (0%)")
        };

        public static readonly List<SelectOption> AvailableMute = new()
        {
            new("0", "Sound Enabled (Default)"),
            new("1", "Muted on Launch")
        };

        public static readonly List<SelectOption> AvailableFps = new()
        {
            new("0", "None"),
            new("1", "FPS"),
            new("2", "Detailed")
        };

        public static readonly List<SelectOption> AvailableAutosave = new()
        {
            new("0", "Disabled (Manual Saves Only)"),
            new("1", "Enabled (Auto Save State)")
        };

        public static readonly List<SelectOption> AvailableHaptics = new()
        {
            new("1", "Enabled (Vibration Feedback)"),
            new("0", "Disabled")
        };

        public static readonly List<SelectOption> AvailableVirtualGamepad = new()
        {
            new("0", "Disabled by Default"),
            new("1", "Enabled by Default")
        };

        public static readonly List<SelectOption> AvailableVirtualGamepadLefty = new()
        {
            new("0", "Right-Handed (Standard)"),
            new("1", "Left-Handed (Lefty Mode)")
        };

        /// <summary>
        /// Returns canonical dropdown options for player and global settings.
        /// Supports optional category/scope filter (e.g. ?scope=shaders or ?category=scaling).
        /// Path: GET /jellyemu/setting-options
        /// </summary>
        [HttpGet("/jellyemu/setting-options")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetSettingOptions([FromQuery] string? scope = null, [FromQuery] string? category = null)
        {
            var filter = (scope ?? category)?.Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(filter))
            {
                return filter switch
                {
                    "shaders" or "shader" => Ok(AvailableShaders.Select(s => new { id = s.Id, label = s.Label })),
                    "scaling" or "scale" => Ok(AvailableScaling.Select(s => new { id = s.Id, label = s.Label })),
                    "rotation" => Ok(AvailableRotations.Select(s => new { id = s.Id, label = s.Label })),
                    "ffrate" or "fastforwardrates" => Ok(AvailableFastForwardRates.Select(s => new { id = s.Id, label = s.Label })),
                    "smrate" or "slowmotionrates" => Ok(AvailableSlowMotionRates.Select(s => new { id = s.Id, label = s.Label })),
                    "volume" => Ok(AvailableVolume.Select(s => new { id = s.Id, label = s.Label })),
                    "mute" => Ok(AvailableMute.Select(s => new { id = s.Id, label = s.Label })),
                    "fps" or "showfps" => Ok(AvailableFps.Select(s => new { id = s.Id, label = s.Label })),
                    "autosave" => Ok(AvailableAutosave.Select(s => new { id = s.Id, label = s.Label })),
                    "haptics" => Ok(AvailableHaptics.Select(s => new { id = s.Id, label = s.Label })),
                    "virtualgamepad" => Ok(AvailableVirtualGamepad.Select(s => new { id = s.Id, label = s.Label })),
                    "virtualgamepadlefty" => Ok(AvailableVirtualGamepadLefty.Select(s => new { id = s.Id, label = s.Label })),
                    _ => Ok(GetAllSettingOptions())
                };
            }

            return Ok(GetAllSettingOptions());
        }

        private object GetAllSettingOptions() => new
        {
            shaders = AvailableShaders.Select(s => new { id = s.Id, label = s.Label }),
            scaling = AvailableScaling.Select(s => new { id = s.Id, label = s.Label }),
            rotation = AvailableRotations.Select(s => new { id = s.Id, label = s.Label }),
            fastForwardRates = AvailableFastForwardRates.Select(s => new { id = s.Id, label = s.Label }),
            slowMotionRates = AvailableSlowMotionRates.Select(s => new { id = s.Id, label = s.Label }),
            volume = AvailableVolume.Select(s => new { id = s.Id, label = s.Label }),
            mute = AvailableMute.Select(s => new { id = s.Id, label = s.Label }),
            fps = AvailableFps.Select(s => new { id = s.Id, label = s.Label }),
            autosave = AvailableAutosave.Select(s => new { id = s.Id, label = s.Label }),
            haptics = AvailableHaptics.Select(s => new { id = s.Id, label = s.Label }),
            virtualGamepad = AvailableVirtualGamepad.Select(s => new { id = s.Id, label = s.Label }),
            virtualGamepadLefty = AvailableVirtualGamepadLefty.Select(s => new { id = s.Id, label = s.Label })
        };
    }
}
