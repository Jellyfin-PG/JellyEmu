using System.Net.Mime;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves platform control schemes, button definitions, hotkeys, and defaults as the single source of truth.
    /// Routes: /jellyemu/input/*
    /// </summary>
    [ApiController]
    public class JellyEmuInputController : JellyEmuBaseController
    {
        private readonly JellyEmuInputService _inputService;

        public JellyEmuInputController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuInputController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            JellyEmuInputService inputService)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _inputService = inputService;
        }

        /// <summary>
        /// Returns all supported platform control schemes, button definitions, and default bindings.
        /// Path: GET /jellyemu/input/schemes
        /// </summary>
        [HttpGet("/jellyemu/input/schemes")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetAllSchemes()
        {
            var cacheKey = JellyEmuCacheKeys.InputSchemes();
            if (CacheService.TryGetValue<object>(cacheKey, out var cached) && cached != null)
            {
                return Ok(cached);
            }

            var schemes = _inputService.GetAllSchemes();
            var result = new
            {
                hotkeys = JellyEmuInputService.Hotkeys,
                schemes
            };
            CacheService.Set(cacheKey, (object)result, slidingExpiration: TimeSpan.FromHours(24));
            return Ok(result);
        }

        /// <summary>
        /// Returns the control scheme, buttons, and defaults for a specific platform, core, or scheme key.
        /// Path: GET /jellyemu/input/schemes/{platformOrCore}
        /// </summary>
        [HttpGet("/jellyemu/input/schemes/{platformOrCore}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetScheme(string platformOrCore)
        {
            var cacheKey = JellyEmuCacheKeys.InputScheme(platformOrCore);
            if (CacheService.TryGetValue<object>(cacheKey, out var cached) && cached != null)
            {
                return Ok(cached);
            }

            var scheme = _inputService.GetScheme(platformOrCore);
            var result = new
            {
                query = platformOrCore,
                hotkeys = JellyEmuInputService.Hotkeys,
                scheme
            };
            CacheService.Set(cacheKey, (object)result, slidingExpiration: TimeSpan.FromHours(24));
            return Ok(result);
        }
    }
}
