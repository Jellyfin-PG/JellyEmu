using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Implements WebDAV endpoints for RetroArch's Cloud Sync driver (network/cloud_sync/webdav.c).
    /// Stores saves, save states, and manifest files per Jellyfin user under:
    ///   {DataPath}/retroarch/cloudsync/{userId}/
    /// Also handles bidirectional SRAM sync pairing with JellyEmu's web player storage (/jellyemu/sram/*).
    /// </summary>
    [ApiController]
    public class RetroArchCloudSyncController : JellyEmuBaseController
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<RetroArchCloudSyncController> _logger;
        private readonly IUserManager _userManager;
        private readonly IAuthenticationManager? _authManager;

        public RetroArchCloudSyncController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<RetroArchCloudSyncController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            IUserManager userManager,
            IAuthenticationManager? authManager = null)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _libraryManager = libraryManager;
            _appPaths = appPaths;
            _logger = logger;
            _userManager = userManager;
            _authManager = authManager;
        }

        // =========================================================================
        // Authentication & Sandbox Helpers
        // =========================================================================

        private async Task<JellyfinUser?> AuthenticateAsync()
        {
            // 1. ClaimsPrincipal authentication (Jellyfin Bearer / session / token)
            var claimUserId = User.FindFirstValue("Jellyfin-UserId")
                           ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(claimUserId) && Guid.TryParse(claimUserId, out var authGuid))
            {
                var existingUser = _userManager.GetUserById(authGuid);
                if (existingUser != null)
                    return existingUser;
            }

            // 2. HTTP Basic Auth (RetroArch Cloud Sync driver)
            var authHeader = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var encoded = authHeader.Substring(6).Trim();
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    var colonIdx = decoded.IndexOf(':');
                    var username = colonIdx >= 0 ? decoded.Substring(0, colonIdx) : decoded;
                    var password = colonIdx >= 0 ? decoded.Substring(colonIdx + 1) : string.Empty;

                    var remoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                    // Lookup user by username or Guid
                    JellyfinUser? user = _userManager.GetUserByName(username);
                    if (user == null && Guid.TryParse(username, out var userGuid))
                    {
                        user = _userManager.GetUserById(userGuid);
                    }

                    // A. Authenticate with Jellyfin credentials
                    if (user != null)
                    {
                        try
                        {
                            var authResult = await _userManager.AuthenticateUser(user.Username, password, remoteEndPoint, false);
                            if (authResult != null)
                                return authResult;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("[RetroArchCloudSync] AuthenticateUser failed for {Username}: {Message}", username, ex.Message);
                        }
                    }

                    // B. Authenticate with Jellyfin API Key
                    if (_authManager != null)
                    {
                        try
                        {
                            var apiKeys = await _authManager.GetApiKeys();
                            var matchedKey = apiKeys.FirstOrDefault(k =>
                                string.Equals(k.AccessToken, password, StringComparison.Ordinal) ||
                                string.Equals(k.AccessToken, username, StringComparison.Ordinal));

                            if (matchedKey != null)
                            {
                                if (user != null)
                                    return user;

                                var firstUser = _userManager.GetFirstUser();
                                if (firstUser != null)
                                    return firstUser;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("[RetroArchCloudSync] ApiKey check failed: {Message}", ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[RetroArchCloudSync] Malformed Basic Authorization header: {Message}", ex.Message);
                }
            }

            return null;
        }

        private IActionResult UnauthorizedChallenge()
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"JellyEmu WebDAV\"";
            return Unauthorized();
        }

        private string GetUserSyncRoot(JellyfinUser user)
        {
            var path = Path.GetFullPath(Path.Combine(_appPaths.DataPath, "retroarch", "cloudsync", user.Id.ToString("N")));
            Directory.CreateDirectory(path);
            return path;
        }

        private string GetSafePath(JellyfinUser user, string? subpath)
        {
            var userRoot = GetUserSyncRoot(user);
            if (string.IsNullOrWhiteSpace(subpath))
                return userRoot;

            var normalized = Uri.UnescapeDataString(subpath.Replace('\\', '/')).TrimStart('/');
            var fullPath = Path.GetFullPath(Path.Combine(userRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.Equals(userRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(userRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Path traversal attempt detected in WebDAV subpath.");
            }
            return fullPath;
        }

        // =========================================================================
        // WebDAV Endpoints
        // =========================================================================

        /// <summary>
        /// Responds to WebDAV capability inquiries.
        /// </summary>
        [HttpOptions("/jellyemu/retroarch/cloudsync")]
        [HttpOptions("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public IActionResult Options(string? subpath = null)
        {
            Response.Headers["DAV"] = "1, 2";
            Response.Headers["MS-Author-Via"] = "DAV";
            Response.Headers["Allow"] = "OPTIONS, GET, HEAD, POST, PUT, DELETE, MKCOL, PROPFIND, MOVE, COPY";
            Response.Headers["Accept-Ranges"] = "bytes";
            return Ok();
        }

        /// <summary>
        /// WebDAV resource and directory property listing (Depth 0 and 1).
        /// </summary>
        [AcceptVerbs("PROPFIND")]
        [Route("/jellyemu/retroarch/cloudsync")]
        [Route("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> Propfind(string? subpath = null)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedChallenge();

            string targetPath;
            try
            {
                targetPath = GetSafePath(user, subpath);
            }
            catch (SecurityException)
            {
                return BadRequest("Invalid path.");
            }

            if (!Directory.Exists(targetPath) && !System.IO.File.Exists(targetPath))
                return NotFound();

            var depthHeader = Request.Headers["Depth"].ToString();
            bool isDepthZero = string.Equals(depthHeader, "0", StringComparison.Ordinal);

            // Pre-sync any newer web player saves to WebDAV files in this directory
            SyncWebPlayerSavesToDirectory(user, targetPath);

            var xml = BuildPropfindResponseXml(user, targetPath, isDepthZero);
            Response.StatusCode = StatusCodes.Status207MultiStatus;
            return Content(xml, "application/xml; charset=utf-8", Encoding.UTF8);
        }

        /// <summary>
        /// WebDAV collection (directory) creation.
        /// </summary>
        [AcceptVerbs("MKCOL")]
        [Route("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> Mkcol(string? subpath = null)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedChallenge();

            if (string.IsNullOrWhiteSpace(subpath))
                return StatusCode(StatusCodes.Status405MethodNotAllowed);

            string targetPath;
            try
            {
                targetPath = GetSafePath(user, subpath);
            }
            catch (SecurityException)
            {
                return BadRequest("Invalid path.");
            }

            if (Directory.Exists(targetPath) || System.IO.File.Exists(targetPath))
                return StatusCode(StatusCodes.Status405MethodNotAllowed);

            var parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                return StatusCode(StatusCodes.Status409Conflict);

            Directory.CreateDirectory(targetPath);
            return StatusCode(StatusCodes.Status201Created);
        }

        /// <summary>
        /// WebDAV file download and metadata query.
        /// </summary>
        [HttpGet("/jellyemu/retroarch/cloudsync/{**subpath}")]
        [HttpHead("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> GetOrHead(string? subpath = null)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedChallenge();

            if (string.IsNullOrWhiteSpace(subpath))
                return BadRequest("Cannot GET collection root.");

            string targetPath;
            try
            {
                targetPath = GetSafePath(user, subpath);
            }
            catch (SecurityException)
            {
                return BadRequest("Invalid path.");
            }

            // If an .srm file is requested, ensure latest save from web player is synced
            if (subpath.EndsWith(".srm", StringComparison.OrdinalIgnoreCase))
            {
                SyncWebPlayerSaveToFile(user, targetPath);
            }

            if (Directory.Exists(targetPath))
                return BadRequest("Target is a collection.");

            if (!System.IO.File.Exists(targetPath))
                return NotFound();

            var fileInfo = new FileInfo(targetPath);
            var etag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\"";
            Response.Headers["ETag"] = etag;
            Response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R");
            Response.Headers["Accept-Ranges"] = "bytes";

            if (HttpMethods.IsHead(Request.Method))
            {
                Response.Headers["Content-Length"] = fileInfo.Length.ToString();
                Response.ContentType = "application/octet-stream";
                return Ok();
            }

            var stream = System.IO.File.OpenRead(targetPath);
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        /// <summary>
        /// WebDAV file upload.
        /// </summary>
        [HttpPut("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> Put(string? subpath = null)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedChallenge();

            if (string.IsNullOrWhiteSpace(subpath))
                return BadRequest("Cannot PUT to collection root.");

            string targetPath;
            try
            {
                targetPath = GetSafePath(user, subpath);
            }
            catch (SecurityException)
            {
                return BadRequest("Invalid path.");
            }

            var parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            bool existed = System.IO.File.Exists(targetPath);
            var tempFile = targetPath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                await using (var fs = System.IO.File.Create(tempFile))
                {
                    await Request.Body.CopyToAsync(fs, HttpContext.RequestAborted);
                }

                System.IO.File.Move(tempFile, targetPath, overwrite: true);

                // Mirror .srm files to JellyEmu web player SRAM store
                if (subpath.EndsWith(".srm", StringComparison.OrdinalIgnoreCase))
                {
                    MirrorSrmToWebPlayer(user, targetPath);
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }

            if (existed)
                return NoContent();

            Response.Headers["Location"] = Request.Path.Value ?? "/jellyemu/retroarch/cloudsync";
            return StatusCode(StatusCodes.Status201Created);
        }

        /// <summary>
        /// WebDAV resource removal.
        /// </summary>
        [HttpDelete("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> Delete(string? subpath = null)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedChallenge();

            if (string.IsNullOrWhiteSpace(subpath))
                return BadRequest("Cannot DELETE collection root.");

            string targetPath;
            try
            {
                targetPath = GetSafePath(user, subpath);
            }
            catch (SecurityException)
            {
                return BadRequest("Invalid path.");
            }

            if (System.IO.File.Exists(targetPath))
            {
                System.IO.File.Delete(targetPath);
                return NoContent();
            }

            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
                return NoContent();
            }

            return NotFound();
        }

        /// <summary>
        /// WebDAV resource move.
        /// </summary>
        [AcceptVerbs("MOVE")]
        [Route("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> Move(string? subpath = null)
        {
            return await MoveOrCopyAsync(subpath, isMove: true);
        }

        /// <summary>
        /// WebDAV resource copy.
        /// </summary>
        [AcceptVerbs("COPY")]
        [Route("/jellyemu/retroarch/cloudsync/{**subpath}")]
        public async Task<IActionResult> Copy(string? subpath = null)
        {
            return await MoveOrCopyAsync(subpath, isMove: false);
        }

        private async Task<IActionResult> MoveOrCopyAsync(string? subpath, bool isMove)
        {
            var user = await AuthenticateAsync();
            if (user == null)
                return UnauthorizedChallenge();

            if (string.IsNullOrWhiteSpace(subpath))
                return BadRequest("Invalid source path.");

            var destinationHeader = Request.Headers["Destination"].ToString();
            var destSubpath = ParseDestinationSubpath(destinationHeader);
            if (string.IsNullOrWhiteSpace(destSubpath))
                return BadRequest("Missing or invalid Destination header.");

            string sourcePath, destPath;
            try
            {
                sourcePath = GetSafePath(user, subpath);
                destPath = GetSafePath(user, destSubpath);
            }
            catch (SecurityException)
            {
                return BadRequest("Invalid path.");
            }

            bool isSourceFile = System.IO.File.Exists(sourcePath);
            bool isSourceDir = Directory.Exists(sourcePath);
            if (!isSourceFile && !isSourceDir)
                return NotFound();

            bool destExists = System.IO.File.Exists(destPath) || Directory.Exists(destPath);
            bool overwrite = !string.Equals(Request.Headers["Overwrite"].ToString(), "F", StringComparison.OrdinalIgnoreCase);

            if (destExists && !overwrite)
                return StatusCode(StatusCodes.Status412PreconditionFailed);

            var destParent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destParent))
                Directory.CreateDirectory(destParent);

            if (destExists)
            {
                if (System.IO.File.Exists(destPath))
                    System.IO.File.Delete(destPath);
                else if (Directory.Exists(destPath))
                    Directory.Delete(destPath, recursive: true);
            }

            if (isMove)
            {
                if (isSourceFile)
                    System.IO.File.Move(sourcePath, destPath, overwrite: true);
                else
                    Directory.Move(sourcePath, destPath);

                if (destSubpath.EndsWith(".srm", StringComparison.OrdinalIgnoreCase))
                    MirrorSrmToWebPlayer(user, destPath);
            }
            else
            {
                if (isSourceFile)
                    System.IO.File.Copy(sourcePath, destPath, overwrite: true);
                else
                    CopyDirectoryRecursively(sourcePath, destPath);

                if (destSubpath.EndsWith(".srm", StringComparison.OrdinalIgnoreCase))
                    MirrorSrmToWebPlayer(user, destPath);
            }

            return StatusCode(destExists ? StatusCodes.Status204NoContent : StatusCodes.Status201Created);
        }

        // =========================================================================
        // SRAM Synchronization & Rom Resolution
        // =========================================================================

        private void MirrorSrmToWebPlayer(JellyfinUser user, string srmFilePath)
        {
            try
            {
                var gameBaseName = Path.GetFileNameWithoutExtension(srmFilePath);
                var item = ResolveRomItem(gameBaseName);
                if (item == null)
                {
                    _logger.LogDebug("[RetroArchCloudSync] No matching library ROM found for save '{GameName}'", gameBaseName);
                    return;
                }

                var sramPath = GetSramPath(user.Id.ToString("N"), item.Id.ToString("N"), 1);
                var sramDir = Path.GetDirectoryName(sramPath);
                if (!string.IsNullOrEmpty(sramDir))
                    Directory.CreateDirectory(sramDir);

                System.IO.File.Copy(srmFilePath, sramPath, overwrite: true);
                System.IO.File.SetLastWriteTimeUtc(sramPath, System.IO.File.GetLastWriteTimeUtc(srmFilePath));

                // Evict both Guid representations from cache
                CacheService.Evict(JellyEmuCacheKeys.Sram(item.Id.ToString("N"), user.Id.ToString("N"), 1));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(item.Id.ToString("N"), user.Id.ToString("N")));
                CacheService.Evict(JellyEmuCacheKeys.Sram(item.Id.ToString(), user.Id.ToString("N"), 1));
                CacheService.Evict(JellyEmuCacheKeys.SaveSlots(item.Id.ToString(), user.Id.ToString("N")));

                _logger.LogInformation("[RetroArchCloudSync] Mirrored RetroArch SRAM to JellyEmu web save: {Game} (item {ItemId})",
                    item.Name, item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RetroArchCloudSync] Failed to mirror SRAM {Path} to web player", srmFilePath);
            }
        }

        private void SyncWebPlayerSaveToFile(JellyfinUser user, string srmFilePath)
        {
            try
            {
                var gameBaseName = Path.GetFileNameWithoutExtension(srmFilePath);
                var item = ResolveRomItem(gameBaseName);
                if (item == null)
                    return;

                var sramPath = GetSramPath(user.Id.ToString("N"), item.Id.ToString("N"), 1);
                if (!System.IO.File.Exists(sramPath))
                    return;

                var sramTime = System.IO.File.GetLastWriteTimeUtc(sramPath);
                if (!System.IO.File.Exists(srmFilePath) || sramTime > System.IO.File.GetLastWriteTimeUtc(srmFilePath))
                {
                    var srmDir = Path.GetDirectoryName(srmFilePath);
                    if (!string.IsNullOrEmpty(srmDir))
                        Directory.CreateDirectory(srmDir);

                    System.IO.File.Copy(sramPath, srmFilePath, overwrite: true);
                    System.IO.File.SetLastWriteTimeUtc(srmFilePath, sramTime);
                    _logger.LogInformation("[RetroArchCloudSync] Updated WebDAV SRAM from web player save: {Game} (item {ItemId})",
                        item.Name, item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RetroArchCloudSync] Failed to sync web save to WebDAV SRAM file {Path}", srmFilePath);
            }
        }

        private void SyncWebPlayerSavesToDirectory(JellyfinUser user, string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            try
            {
                foreach (var srmFile in Directory.EnumerateFiles(directoryPath, "*.srm", SearchOption.TopDirectoryOnly))
                {
                    SyncWebPlayerSaveToFile(user, srmFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[RetroArchCloudSync] Error scanning directory {Path} for SRAM sync", directoryPath);
            }
        }

        private BaseItem? ResolveRomItem(string gameBaseName)
        {
            if (string.IsNullOrWhiteSpace(gameBaseName))
                return null;

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            var items = _libraryManager.GetItemList(query)
                .Where(i => i.Tags != null && i.Tags.Contains("JellyEmu", StringComparer.OrdinalIgnoreCase));

            foreach (var item in items)
            {
                if (string.Equals(item.Name, gameBaseName, StringComparison.OrdinalIgnoreCase))
                    return item;

                if (!string.IsNullOrEmpty(item.Path))
                {
                    var fileName = Path.GetFileNameWithoutExtension(item.Path);
                    if (string.Equals(fileName, gameBaseName, StringComparison.OrdinalIgnoreCase))
                        return item;

                    var effective = RomExtensions.EffectiveRomPath(item.Path);
                    var effectiveName = Path.GetFileNameWithoutExtension(effective);
                    if (string.Equals(effectiveName, gameBaseName, StringComparison.OrdinalIgnoreCase))
                        return item;
                }

                if (item.Id.ToString("N").Equals(gameBaseName, StringComparison.OrdinalIgnoreCase) ||
                    item.Id.ToString().Equals(gameBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        // =========================================================================
        // XML & File Utilities
        // =========================================================================

        private string BuildPropfindResponseXml(JellyfinUser user, string targetPath, bool isDepthZero)
        {
            XNamespace d = "DAV:";
            var responses = new List<XElement>();

            var requestPath = Request.Path.Value?.TrimEnd('/') ?? "/jellyemu/retroarch/cloudsync";
            var pathBase = Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
            var baseUri = $"{pathBase}{requestPath}";

            bool isDir = Directory.Exists(targetPath);
            var targetInfo = isDir ? (FileSystemInfo)new DirectoryInfo(targetPath) : new FileInfo(targetPath);

            var targetHref = isDir ? $"{baseUri}/" : baseUri;
            responses.Add(CreateResponseElement(d, targetHref, targetInfo));

            if (isDir && !isDepthZero)
            {
                var dirInfo = (DirectoryInfo)targetInfo;
                foreach (var child in dirInfo.EnumerateFileSystemInfos())
                {
                    var isChildDir = child is DirectoryInfo;
                    var childHref = $"{baseUri.TrimEnd('/')}/{Uri.EscapeDataString(child.Name)}{(isChildDir ? "/" : string.Empty)}";
                    responses.Add(CreateResponseElement(d, childHref, child));
                }
            }

            var multistatus = new XElement(d + "multistatus",
                new XAttribute(XNamespace.Xmlns + "D", "DAV:"),
                responses);
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), multistatus);
            return doc.ToString();
        }

        private static XElement CreateResponseElement(XNamespace d, string href, FileSystemInfo info)
        {
            bool isDir = info is DirectoryInfo;
            var prop = new XElement(d + "prop",
                new XElement(d + "displayname", info.Name),
                new XElement(d + "resourcetype", isDir ? new XElement(d + "collection") : null),
                new XElement(d + "getlastmodified", info.LastWriteTimeUtc.ToString("R")),
                new XElement(d + "creationdate", info.CreationTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            );

            if (!isDir && info is FileInfo fileInfo)
            {
                prop.Add(new XElement(d + "getcontentlength", fileInfo.Length));
                prop.Add(new XElement(d + "getetag", $"\"{fileInfo.LastWriteTimeUtc.Ticks:x}-{fileInfo.Length:x}\""));
            }

            return new XElement(d + "response",
                new XElement(d + "href", href),
                new XElement(d + "propstat",
                    prop,
                    new XElement(d + "status", "HTTP/1.1 200 OK")
                )
            );
        }

        private string? ParseDestinationSubpath(string destinationHeader)
        {
            if (string.IsNullOrWhiteSpace(destinationHeader))
                return null;

            string destPath;
            if (Uri.TryCreate(destinationHeader, UriKind.Absolute, out var uri))
            {
                destPath = uri.AbsolutePath;
            }
            else
            {
                destPath = destinationHeader;
            }

            var pathBase = Request.PathBase.Value;
            if (!string.IsNullOrEmpty(pathBase) && destPath.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
            {
                destPath = destPath.Substring(pathBase.Length);
            }

            const string prefix = "/jellyemu/retroarch/cloudsync";
            var idx = destPath.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                destPath = destPath.Substring(idx + prefix.Length);
            }

            return destPath.TrimStart('/');
        }

        private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                System.IO.File.Copy(file, dest, overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectoryRecursively(dir, dest);
            }
        }
    }
}
