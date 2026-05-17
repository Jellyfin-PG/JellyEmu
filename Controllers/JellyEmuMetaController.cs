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

    }
}
