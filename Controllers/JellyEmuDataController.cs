using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves RetroKit core binaries, WASM runtimes, and engine assets from JellyEmu's local data directory,
    /// caching upstream assets automatically on first request.
    /// Route: /jellyemu/data/{category}/{**filename}
    /// </summary>
    public class JellyEmuDataController : JellyEmuBaseController
    {
        private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "cores", "play", "pico8", "love", "ruffle", "assets", "shaders"
        };

        private static readonly Dictionary<string, string> UpstreamCdns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cores"] = "https://web.libretro.com/",
            ["play"] = "https://playjs.purei.org/",
            ["love"] = "https://cdn.jsdelivr.net/gh/2dengine/love.js@main/11.5/",
            ["ruffle"] = "https://unpkg.com/@ruffle-rs/ruffle/"
        };

        public JellyEmuDataController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuDataController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            EnsureDataDirectories();
        }

        private string DataRoot => Path.Combine(AppPaths.DataPath, "jellyemu-data");

        private void EnsureDataDirectories()
        {
            try
            {
                if (!Directory.Exists(DataRoot))
                {
                    Directory.CreateDirectory(DataRoot);
                }

                foreach (var category in AllowedCategories)
                {
                    var catDir = Path.Combine(DataRoot, category);
                    if (!Directory.Exists(catDir))
                    {
                        Directory.CreateDirectory(catDir);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Failed to pre-create jellyemu-data directories.");
            }
        }

        /// <summary>
        /// Serves an engine runtime file or WASM core from the local JellyEmu data directory.
        /// If the file is not yet cached locally, it is fetched from upstream CDN, saved into
        /// {DataPath}/jellyemu-data/{category}/{filename}, and streamed to the client.
        /// </summary>
        [HttpGet("/jellyemu/data/{category}/{**filename}")]
        [HttpHead("/jellyemu/data/{category}/{**filename}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDataAsset(string category, string filename)
        {
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(filename) || !AllowedCategories.Contains(category))
            {
                return BadRequest("Invalid data category or filename.");
            }

            var cleanCategory = category.ToLowerInvariant();
            var normalizedFilename = filename.Replace('\\', '/').TrimStart('/');
            if (normalizedFilename.Contains("..", StringComparison.Ordinal))
            {
                return BadRequest("Invalid path.");
            }

            var categoryRoot = Path.GetFullPath(Path.Combine(DataRoot, cleanCategory));
            var localPath = Path.GetFullPath(Path.Combine(categoryRoot, normalizedFilename));

            if (!localPath.StartsWith(categoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Invalid path traversal.");
            }

            // 1. Check primary jellyemu-data folder
            if (System.IO.File.Exists(localPath))
            {
                return ServeLocalFile(localPath);
            }

            // 2. Check legacy Play! or PICO-8 managed folders so existing downloads work immediately
            if (cleanCategory == "play")
            {
                var legacyPlayPath = Path.Combine(AppPaths.DataPath, "jellyemu-play", normalizedFilename);
                if (System.IO.File.Exists(legacyPlayPath))
                {
                    return ServeLocalFile(legacyPlayPath);
                }
            }
            else if (cleanCategory == "pico8")
            {
                var legacyPicoDir = Path.Combine(AppPaths.DataPath, "jellyemu-pico8");
                var directPicoPath = Path.Combine(legacyPicoDir, normalizedFilename);
                if (System.IO.File.Exists(directPicoPath))
                {
                    return ServeLocalFile(directPicoPath);
                }

                if (normalizedFilename.Equals("pico8.js", StringComparison.OrdinalIgnoreCase))
                {
                    var fallbackRuntimePath = Path.Combine(legacyPicoDir, "runtime.js");
                    if (System.IO.File.Exists(fallbackRuntimePath))
                    {
                        return ServeLocalFile(fallbackRuntimePath);
                    }
                }
            }

            // 3. Fetch from upstream CDN and cache locally into jellyemu-data
            if (UpstreamCdns.TryGetValue(cleanCategory, out var cdnBase))
            {
                var upstreamUrl = cdnBase + normalizedFilename;
                try
                {
                    var client = HttpClientFactory.CreateClient("JellyEmuData");
                    client.Timeout = TimeSpan.FromMinutes(3);

                    using var resp = await client.GetAsync(upstreamUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        await System.IO.File.WriteAllBytesAsync(localPath, bytes);
                        Logger.LogInformation("[JellyEmu] Cached RetroKit asset: {Category}/{Filename} ({Bytes} bytes)",
                            cleanCategory, SanitizeForLog(normalizedFilename), bytes.Length);

                        return ServeBytes(bytes, normalizedFilename);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[JellyEmu] Failed to fetch upstream asset from {Url}", upstreamUrl);
                }
            }

            return NotFound();
        }

        private IActionResult ServeLocalFile(string filePath)
        {
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "public, max-age=86400";

            var contentType = GetMimeType(filePath);
            if (HttpMethods.IsHead(Request.Method))
            {
                var fileInfo = new FileInfo(filePath);
                Response.ContentLength = fileInfo.Length;
                Response.ContentType = contentType;
                return Ok();
            }

            return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
        }

        private IActionResult ServeBytes(byte[] bytes, string filename)
        {
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "public, max-age=86400";

            var contentType = GetMimeType(filename);
            if (HttpMethods.IsHead(Request.Method))
            {
                Response.ContentLength = bytes.Length;
                Response.ContentType = contentType;
                return Ok();
            }

            return File(bytes, contentType);
        }

        private static string GetMimeType(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".js" or ".mjs" => "application/javascript",
                ".wasm" => "application/wasm",
                ".data" or ".bin" => "application/octet-stream",
                ".json" => "application/json",
                ".css" => "text/css",
                ".glsl" or ".glslp" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}
