using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves cached/proxied third-party JS dependencies used by JellyEmu.
    /// Routes: /jellyemu/pico8/runtime.js, /jellyemu/threejs/three.min.js
    /// </summary>
    public class JellyEmuDependencyController : JellyEmuBaseController
    {
        private readonly JellyEmuPico8Manager _pico8Manager;
        private readonly JellyEmuThreeJsManager _threeJsManager;
        private readonly JellyEmuEjsManager _ejsManager;
        private readonly JellyEmuBiosService _biosService;

        public JellyEmuDependencyController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuDependencyController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            JellyEmuPico8Manager pico8Manager,
            JellyEmuThreeJsManager threeJsManager,
            JellyEmuBiosService biosService)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _pico8Manager = pico8Manager;
            _threeJsManager = threeJsManager;
            _ejsManager = ejsManager;
            _biosService = biosService;
        }

        /// <summary>
        /// Serves the PICO-8 web runtime JS.
        /// Tries local cache first; falls back to live proxy from Lexaloffle.
        ///
        /// Path: GET /jellyemu/pico8/runtime.js
        /// </summary>
        [HttpGet("/jellyemu/pico8/runtime.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Pico8Runtime()
        {
            const string contentType = "application/javascript; charset=utf-8";
            Response.ContentType = contentType;
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            if (_pico8Manager.IsReady)
            {
                var localPath = Path.Combine(_pico8Manager.LocalRoot, JellyEmuPico8Manager.RuntimeFilename);
                if (System.IO.File.Exists(localPath))
                {
                    Logger.LogDebug("[JellyEmu] Serving PICO-8 runtime from local cache");
                    return File(System.IO.File.OpenRead(localPath), contentType);
                }
            }

            Logger.LogWarning("[JellyEmu] PICO-8 runtime not cached yet — proxying from Lexaloffle");
            try
            {
                var client = HttpClientFactory.CreateClient("JellyEmuPico8");
                client.DefaultRequestHeaders.Add("User-Agent", JellyEmuVersion.BrowserUserAgent);

                using var upstream = await client.GetAsync(
                    JellyEmuPico8Manager.RuntimeUrl,
                    HttpCompletionOption.ResponseHeadersRead);

                if (!upstream.IsSuccessStatusCode)
                {
                    Logger.LogError("[JellyEmu] Lexaloffle returned {Status} for runtime", (int)upstream.StatusCode);
                    return StatusCode(502);
                }

                return File(await upstream.Content.ReadAsByteArrayAsync(), contentType);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to proxy PICO-8 runtime from Lexaloffle");
                return StatusCode(502);
            }
        }

        /// <summary>
        /// Serves the Three.js r128 runtime JS.
        /// Tries local cache first; falls back to live proxy from cdnjs.
        ///
        /// Path: GET /jellyemu/threejs/three.min.js
        /// </summary>
        [HttpGet("/jellyemu/threejs/three.min.js")]
        [Produces("application/javascript")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> ThreeJs()
        {
            const string contentType = "application/javascript; charset=utf-8";
            Response.ContentType = contentType;
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            if (_threeJsManager.IsReady)
            {
                var localPath = Path.Combine(_threeJsManager.LocalRoot, JellyEmuThreeJsManager.RuntimeFilename);
                if (System.IO.File.Exists(localPath))
                {
                    Logger.LogDebug("[JellyEmu] Serving Three.js runtime from local cache");
                    return File(System.IO.File.OpenRead(localPath), contentType);
                }
            }

            Logger.LogWarning("[JellyEmu] Three.js runtime not cached yet — proxying from cdnjs");
            try
            {
                var client = HttpClientFactory.CreateClient("JellyEmuThreeJs");
                client.DefaultRequestHeaders.Add("User-Agent", JellyEmuVersion.BrowserUserAgent);

                using var upstream = await client.GetAsync(
                    JellyEmuThreeJsManager.RuntimeUrl,
                    HttpCompletionOption.ResponseHeadersRead);

                if (!upstream.IsSuccessStatusCode)
                {
                    Logger.LogError("[JellyEmu] cdnjs returned {Status} for Three.js", (int)upstream.StatusCode);
                    return StatusCode(502);
                }

                return File(await upstream.Content.ReadAsByteArrayAsync(), contentType);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to proxy Three.js runtime from cdnjs");
                return StatusCode(502);
            }
        }

        /// <summary>
        /// Serves EmulatorJS assets from local cache (if downloaded) or proxies from CDN.
        /// Path: GET /jellyemu/ejs/{*path}
        /// </summary>
        [HttpGet("/jellyemu/ejs/{*path}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> EjsAsset(string path,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            if (string.IsNullOrEmpty(path)) return NotFound();

            path = path.Replace('\\', '/').TrimStart('/');
            if (path.Contains("..")) return BadRequest();

            var contentType = path switch
            {
                var p when p.EndsWith(".mjs",  StringComparison.OrdinalIgnoreCase) => "application/javascript; charset=utf-8",
                var p when p.EndsWith(".cjs",  StringComparison.OrdinalIgnoreCase) => "application/javascript; charset=utf-8",
                var p when p.EndsWith(".jsx",  StringComparison.OrdinalIgnoreCase) => "text/javascript; charset=utf-8",
                var p when p.EndsWith(".js",   StringComparison.OrdinalIgnoreCase) => "application/javascript; charset=utf-8",
                var p when p.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) => "application/wasm",
                var p when p.EndsWith(".css",  StringComparison.OrdinalIgnoreCase) => "text/css",
                var p when p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) => "application/json",
                var p when p.EndsWith(".png",  StringComparison.OrdinalIgnoreCase) => "image/png",
                var p when p.EndsWith(".svg",  StringComparison.OrdinalIgnoreCase) => "image/svg+xml",
                var p when p.EndsWith(".txt",  StringComparison.OrdinalIgnoreCase) => "text/plain",
                var p when p.EndsWith(".csv",  StringComparison.OrdinalIgnoreCase) => "text/csv",
                var p when p.EndsWith(".xml",  StringComparison.OrdinalIgnoreCase) => "application/xml",
                _ => "application/octet-stream"
            };

            Response.ContentType = contentType;
            Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

            if (path.Equals("cores/azahar-thread-legacy-wasm.data", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("cores/azahar-legacy-wasm.data", StringComparison.OrdinalIgnoreCase))
            {
                path = "cores/azahar-thread-wasm.data";
            }

            if (_ejsManager.IsReady)
            {
                var localPath = Path.Combine(_ejsManager.LocalRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(localPath))
                {
                    Logger.LogDebug("[JellyEmu] Serving EJS asset locally: {Path}", path);
                    return File(System.IO.File.OpenRead(localPath), contentType);
                }
                Logger.LogWarning("[JellyEmu] EJS asset missing from local cache, proxying: {Path}", path);
            }

            // Fall back to CDN proxy: use nightly CDN channel for azahar (3DS core)
            var baseUrl = path.Contains("azahar", StringComparison.OrdinalIgnoreCase)
                ? "https://cdn.emulatorjs.org/nightly/data"
                : JellyEmuEjsManager.CdnBase.TrimEnd('/');

            var cdnUrl = $"{baseUrl}/{path}";
            Logger.LogInformation("[JellyEmu] Proxying EJS asset from CDN: {Url}", cdnUrl);

            try
            {
                var client = httpClientFactory.CreateClient("JellyEmuEjs");
                using var cdnResponse = await client.GetAsync(cdnUrl, HttpCompletionOption.ResponseHeadersRead);

                if (!cdnResponse.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[JellyEmu] CDN returned {Status} for {Url}", (int)cdnResponse.StatusCode, cdnUrl);
                    return NotFound();
                }

                return File(await cdnResponse.Content.ReadAsByteArrayAsync(), contentType);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to proxy EJS asset from CDN: {Url}", cdnUrl);
                return StatusCode(502);
            }
        }

        /// <summary>
        /// Forces a background or synchronous re-download of EmulatorJS assets based on configured channel.
        /// Path: POST /jellyemu/ejs/redownload
        /// </summary>
        [HttpPost("/jellyemu/ejs/redownload")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RedownloadEjs()
        {
            Logger.LogInformation("[JellyEmu] Manual trigger to re-download EmulatorJS assets for channel {Channel}", JellyEmuEjsManager.CurrentChannel);
            _ = Task.Run(async () => await _ejsManager.RedownloadAsync());
            return Ok(new { success = true, channel = JellyEmuEjsManager.CurrentChannel, message = $"Started re-downloading EmulatorJS assets for channel '{JellyEmuEjsManager.CurrentChannel}' in background." });
        }

        /// <summary>
        /// Serves a BIOS file from the BIOS folder.
        /// Path: GET /jellyemu/bios/file/{*filename}, GET /jellyemu/bios/file?name=...
        /// </summary>
        [HttpGet("/jellyemu/bios/file")]
        [HttpHead("/jellyemu/bios/file")]
        [HttpGet("/jellyemu/bios/file/{*filename}")]
        [HttpHead("/jellyemu/bios/file/{*filename}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetBiosFile([FromQuery] string? name, string? filename = null)
        {
            var targetName = !string.IsNullOrWhiteSpace(filename) ? filename : name;
            if (string.IsNullOrWhiteSpace(targetName)) return NotFound();

            var biosRoot = Path.GetFullPath(_biosService.GetBiosDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(biosRoot, targetName.Replace('/', Path.DirectorySeparatorChar)));

            // Comparing against root + separator stops both "../" traversal and sibling
            // directories that merely share the root as a name prefix.
            if (!fullPath.StartsWith(biosRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Logger.LogWarning("[JellyEmu] Security attempt to access file outside BIOS folder: {Name}", targetName);
                return NotFound();
            }

            if (!System.IO.File.Exists(fullPath))
            {
                Logger.LogWarning("[JellyEmu] BIOS file not found: {FullPath}", fullPath);
                return NotFound();
            }

            var fileInfo = new FileInfo(fullPath);
            var contentType = targetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "application/octet-stream";
            var fileName = Path.GetFileName(fullPath);

            Response.Headers["Content-Length"] = fileInfo.Length.ToString();
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Expose-Headers"] = "Content-Length, Content-Range, Accept-Ranges, Content-Type, Content-Disposition";

            return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
        }

        /// <summary>
        /// Returns the list of detected BIOS files in the BIOS folder.
        /// Path: GET /jellyemu/bios/list
        /// </summary>
        [HttpGet("/jellyemu/bios/list")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetBiosList()
        {
            var biosFolder = _biosService.GetBiosDirectory();
            var list = _biosService.ListInstalledBios();
            return Ok(new { directory = biosFolder, items = list, total = list.Count });
        }

        /// <summary>
        /// Serves modular configuration tab partial HTML files from embedded resources.
        /// Path: GET /jellyemu/config/partial/{name}
        /// </summary>
        [HttpGet("/jellyemu/config/partial/{name}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetConfigPartial(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return NotFound();

            var cleanName = Path.GetFileNameWithoutExtension(name);
            var assembly = typeof(JellyEmuDependencyController).Assembly;
            var resourceName = $"JellyEmu.Configuration.tabs.{cleanName}.html";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogWarning("[JellyEmu] Config partial template not found: {ResourceName}", resourceName);
                return NotFound();
            }

            using StreamReader reader = new StreamReader(stream);
            var html = reader.ReadToEnd();
            return Content(html, "text/html");
        }

        /// <summary>
        /// Serves modular configuration JavaScript files from embedded resources.
        /// Path: GET /jellyemu/config/js/{name}
        /// </summary>
        [HttpGet("/jellyemu/config/js/{name}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetConfigJs(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return NotFound();

            var cleanName = Path.GetFileNameWithoutExtension(name);
            var assembly = typeof(JellyEmuDependencyController).Assembly;
            var resourceName = $"JellyEmu.Configuration.js.{cleanName}.js";

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Logger.LogWarning("[JellyEmu] Config JS module not found: {ResourceName}", resourceName);
                return NotFound();
            }

            using StreamReader reader = new StreamReader(stream);
            var js = reader.ReadToEnd();
            return Content(js, "application/javascript");
        }

        /// <summary>
        /// <summary>
        /// Fetches public GitHub Structured Issues for JellyEmu repository.
        /// Path: GET /jellyemu/community/discussions
        /// </summary>
        [HttpGet("/jellyemu/community/discussions")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPublicDiscussions()
        {
            try
            {
                Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";

                var client = HttpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("JellyEmu-Plugin");
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };

                var apiUrl = "https://api.github.com/repos/Jellyfin-PG/JellyEmu/issues?state=all&per_page=50&sort=updated";
                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return Ok(new List<object>());
                }

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonStr);

                var results = new List<object>();

                foreach (var element in jsonDoc.RootElement.EnumerateArray())
                {
                    // Skip pull requests
                    if (element.TryGetProperty("pull_request", out _)) continue;

                    var rawTitle = element.GetProperty("title").GetString() ?? "";
                    var rawBody = element.TryGetProperty("body", out var bElProp) ? (bElProp.GetString() ?? "") : "";

                    var labels = new List<string>();
                    string category = "";
                    bool isStructured = false;

                    if (element.TryGetProperty("labels", out var labelsArray))
                    {
                        foreach (var l in labelsArray.EnumerateArray())
                        {
                            var name = l.GetProperty("name").GetString() ?? "";
                            labels.Add(name);

                            var lowerName = name.ToLowerInvariant();
                            if (lowerName.StartsWith("jellyemu:"))
                            {
                                isStructured = true;
                                if (lowerName.Contains("announcement")) category = "Announcements";
                                else if (lowerName.Contains("idea")) category = "Ideas";
                                else if (lowerName.Contains("poll")) category = "Polls";
                                else if (lowerName.Contains("qna") || lowerName.Contains("q&a")) category = "Q&A";
                                else if (lowerName.Contains("showcase") || lowerName.Contains("show and tell")) category = "Show and Tell";
                                else category = "General";
                            }
                        }
                    }

                    // Check title [Category] prefix or body structure fallback
                    if (!isStructured)
                    {
                        var titleMatch = System.Text.RegularExpressions.Regex.Match(rawTitle, @"^\[(Announcements|General|Ideas|Polls|Q&A|Show and Tell)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (titleMatch.Success)
                        {
                            isStructured = true;
                            var matchedCat = titleMatch.Groups[1].Value;
                            if (matchedCat.Equals("q&a", StringComparison.OrdinalIgnoreCase)) category = "Q&A";
                            else if (matchedCat.Equals("show and tell", StringComparison.OrdinalIgnoreCase)) category = "Show and Tell";
                            else category = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(matchedCat.ToLower());
                        }
                        else if (rawBody.Contains("## Options", StringComparison.OrdinalIgnoreCase) || rawBody.Contains("## Question", StringComparison.OrdinalIgnoreCase))
                        {
                            isStructured = true;
                            category = "Polls";
                        }
                    }

                    // Strict filtering: require structured format (jellyemu:* label, [Category] title prefix, or poll structure)
                    if (!isStructured)
                    {
                        continue;
                    }

                    var number = element.GetProperty("number").GetInt32();
                    var rawTitleStr = element.GetProperty("title").GetString() ?? "";
                    var title = System.Text.RegularExpressions.Regex.Replace(rawTitleStr, @"^\[(Announcements|General|Ideas|Polls|Q&A|Show and Tell)\]\s*", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    if (string.IsNullOrWhiteSpace(title)) title = rawTitleStr;
                    var htmlUrl = element.GetProperty("html_url").GetString() ?? "";
                    var body = element.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
                    var created = element.GetProperty("created_at").GetString() ?? "";
                    var updated = element.GetProperty("updated_at").GetString() ?? "";
                    var commentsCount = element.GetProperty("comments").GetInt32();

                    var userObj = element.GetProperty("user");
                    var authorName = userObj.GetProperty("login").GetString() ?? "Community Member";
                    var avatarUrl = userObj.GetProperty("avatar_url").GetString() ?? "https://github.githubassets.com/favicons/favicon.png";

                    object? pollData = null;
                    if (category == "Polls" || body.Contains("## Options", StringComparison.OrdinalIgnoreCase))
                    {
                        var optionNodes = new List<object>();
                        var lines = body.Split('\n');
                        int idx = 0;
                        int totalVotesSum = 0;
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("- [ ]") || trimmed.StartsWith("- [x]") || trimmed.StartsWith("- [X]") || trimmed.StartsWith("* [ ]") || trimmed.StartsWith("* [x]") || trimmed.StartsWith("* [X]"))
                            {
                                idx++;
                                var isChecked = trimmed.Contains("[x]", StringComparison.OrdinalIgnoreCase);
                                int vCount = isChecked ? 1 : 0;
                                totalVotesSum += vCount;

                                var optText = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^[\-\*]\s*\[[ xX]\]\s*", string.Empty).Trim();
                                if (!string.IsNullOrWhiteSpace(optText))
                                {
                                    optionNodes.Add(new
                                    {
                                        id = $"opt_{idx}",
                                        option = optText,
                                        voteCount = vCount,
                                        viewerHasVoted = isChecked
                                    });
                                }
                            }
                        }

                        if (optionNodes.Count > 0)
                        {
                            pollData = new
                            {
                                id = htmlUrl,
                                question = title,
                                totalVoteCount = totalVotesSum,
                                viewerHasVoted = false,
                                viewerCanVote = true,
                                options = new { nodes = optionNodes }
                            };
                        }
                    }

                    var cleanSummary = System.Text.RegularExpressions.Regex.Replace(body, @"##\s*Question[\s\S]*?##\s*Options[\s\S]*", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    cleanSummary = System.Text.RegularExpressions.Regex.Replace(cleanSummary, @"##\s*Options[\s\S]*", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    cleanSummary = System.Text.RegularExpressions.Regex.Replace(cleanSummary, @"^[\-\*]\s*\[[ xX]\]\s*.*$", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
                    cleanSummary = System.Text.RegularExpressions.Regex.Replace(cleanSummary, "<.*?>", string.Empty).Trim();

                    results.Add(new
                    {
                        id = htmlUrl,
                        number = number,
                        title = title,
                        url = htmlUrl,
                        created = created,
                        updated = updated,
                        summary = cleanSummary,
                        body = body,
                        author = authorName,
                        avatar = avatarUrl,
                        category = category,
                        upvotes = 1,
                        replies = commentsCount,
                        comments = new List<object>(),
                        poll = pollData
                    });
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed fetching public GitHub issues.");
                return Ok(new List<object>());
            }
        }

        /// <summary>
        /// Fetches comments for a specific GitHub Issue/Discussion.
        /// Path: GET /jellyemu/community/discussions/{number}/comments
        /// </summary>
        [HttpGet("/jellyemu/community/discussions/{number}/comments")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPublicDiscussionComments(int number)
        {
            try
            {
                Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";

                var client = HttpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("JellyEmu-Plugin");
                client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };

                var apiUrl = $"https://api.github.com/repos/Jellyfin-PG/JellyEmu/issues/{number}/comments";
                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return Ok(new List<object>());
                }

                var jsonStr = await response.Content.ReadAsStringAsync();
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonStr);

                var comments = new List<object>();

                foreach (var element in jsonDoc.RootElement.EnumerateArray())
                {
                    var id = element.GetProperty("id").GetInt64().ToString();
                    var body = element.TryGetProperty("body", out var bProp) ? (bProp.GetString() ?? "") : "";
                    var createdAt = element.GetProperty("created_at").GetString() ?? "";

                    var userObj = element.GetProperty("user");
                    var authorName = userObj.GetProperty("login").GetString() ?? "Community Member";
                    var avatarUrl = userObj.GetProperty("avatar_url").GetString() ?? "https://github.githubassets.com/favicons/favicon.png";

                    comments.Add(new
                    {
                        id = id,
                        body = body,
                        createdAt = createdAt,
                        author = new
                        {
                            login = authorName,
                            avatarUrl = avatarUrl
                        }
                    });
                }

                return Ok(comments);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed fetching GitHub issue comments for issue #{Number}.", number);
                return Ok(new List<object>());
            }
        }
    }
}