using System.Net.Mime;
using System.Text.Encodings.Web;
using JellyEmu.Services;
using JellyEmu.Utilities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Serves Roms and core information
    /// </summary>
    public class JellyEmuRomController : JellyEmuBaseController
    {
        private readonly PlatformResolver _platformResolver;
        private readonly JellyEmuFileService _fileService;

        public JellyEmuRomController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuRomController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            PlatformResolver platformResolver,
            JellyEmuFileService fileService)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _platformResolver = platformResolver;
            _fileService = fileService;
        }

        [HttpGet("/jellyemu/rom/{itemId}/{filename?}")]
        [HttpHead("/jellyemu/rom/{itemId}/{filename?}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Rom(string itemId, string? filename = null, [FromQuery] string? userId = null)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                Logger.LogWarning("[JellyEmu] Rom: item {ItemId} not found or path missing", itemId);
                return NotFound();
            }

            if (!VerifyItemAccess(item))
            {
                return Forbid();
            }

            if (Directory.Exists(item.Path))
            {
                return await ServeDirectoryOrZipAsync(item).ConfigureAwait(false);
            }

            var romPath = _fileService.ResolveActiveRomPath(item.Path, userId, itemId);
            if (string.IsNullOrEmpty(romPath) || !System.IO.File.Exists(romPath))
            {
                Logger.LogWarning("[JellyEmu] Rom: resolved path {Path} not found", romPath);
                return NotFound();
            }

            Logger.LogInformation("[JellyEmu] Serving ROM: {Path}", romPath);

            var fileInfo = new FileInfo(romPath);
            Response.Headers["X-Rom-Hash"] = GetFileHash(romPath);
            Response.Headers["X-Rom-Size"] = fileInfo.Length.ToString();
            Response.Headers["X-Rom-Extension"] = fileInfo.Extension;
            Response.Headers["X-Rom-Name"] = Path.GetFileNameWithoutExtension(romPath);

            var stream = OpenRomStream(romPath);
            var finalFileName = Path.GetFileName(romPath);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{finalFileName}\"";
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        [HttpGet("/jellyemu/rom/{itemId}/files")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult ListRomFiles(string itemId, [FromQuery] string? userId = null)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path))
                return NotFound();

            if (!VerifyItemAccess(item))
                return Forbid();

            var (primaryPath, allFiles) = ResolveItemFilesWithPrimary(item, userId);
            if (allFiles.Count == 0 || string.IsNullOrEmpty(primaryPath))
                return NotFound();

            var baseDir = Directory.Exists(item.Path)
                ? item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : (Path.GetDirectoryName(item.Path) ?? string.Empty);

            string ToRel(string p)
            {
                if (!string.IsNullOrEmpty(baseDir))
                {
                    var rel = Path.GetRelativePath(baseDir, p).Replace('\\', '/');
                    if (!rel.StartsWith("..", StringComparison.Ordinal))
                        return rel;
                }
                return Path.GetFileName(p);
            }

            string ToFileUrl(string relName)
            {
                var encoded = string.Join("/", relName.Split('/').Select(Uri.EscapeDataString));
                return ToAppUrl($"jellyemu/rom/{itemId}/file/{encoded}");
            }

            var primaryRel = ToRel(primaryPath);
            var entries = allFiles.Select(p =>
            {
                var rel = ToRel(p);
                return new
                {
                    name = rel,
                    url = ToFileUrl(rel),
                    size = new FileInfo(p).Length
                };
            }).ToList();

            return Ok(new
            {
                primaryFile = primaryRel,
                primaryUrl = ToFileUrl(primaryRel),
                files = entries
            });
        }

        [HttpGet("/jellyemu/rom/{itemId}/file/{*filePath}")]
        [HttpHead("/jellyemu/rom/{itemId}/file/{*filePath}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetRomCompanionFile(string itemId, string filePath, [FromQuery] string? userId = null)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path) || string.IsNullOrEmpty(filePath))
                return NotFound();

            if (!VerifyItemAccess(item))
                return Forbid();

            var (_, allFiles) = ResolveItemFilesWithPrimary(item, userId);
            var baseDir = Directory.Exists(item.Path)
                ? item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : (Path.GetDirectoryName(item.Path) ?? string.Empty);

            var targetNorm = Uri.UnescapeDataString(filePath).Replace('\\', '/').TrimStart('/');
            string? matched = null;
            foreach (var p in allFiles)
            {
                var rel = (!string.IsNullOrEmpty(baseDir)
                    ? Path.GetRelativePath(baseDir, p).Replace('\\', '/')
                    : Path.GetFileName(p));
                if (string.Equals(rel, targetNorm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(p), targetNorm, StringComparison.OrdinalIgnoreCase))
                {
                    matched = p;
                    break;
                }
            }

            if (string.IsNullOrEmpty(matched) || !System.IO.File.Exists(matched))
                return NotFound();

            var stream = OpenRomStream(matched);
            var name = Path.GetFileName(matched);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{name}\"";
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        private (string? PrimaryPath, List<string> AllFiles) ResolveItemFilesWithPrimary(BaseItem item, string? userId)
        {
            var files = new List<string>();
            if (Directory.Exists(item.Path))
            {
                files = _fileService.ResolveAllRomFiles(item.Path);
            }
            else
            {
                var activePath = _fileService.ResolveActiveRomPath(item.Path, userId, item.Id.ToString("N")) ?? item.Path;
                if (System.IO.File.Exists(activePath))
                    files.Add(activePath);

                if (activePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var refFile in CueParser.GetReferencedFiles(activePath))
                    {
                        if (System.IO.File.Exists(refFile) && !files.Contains(refFile, StringComparer.OrdinalIgnoreCase))
                            files.Add(refFile);
                    }
                }
                else if (item.Path.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase) || item.Path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var refFile in _fileService.ResolveAllRomFiles(item.Path))
                    {
                        if (System.IO.File.Exists(refFile) && !files.Contains(refFile, StringComparer.OrdinalIgnoreCase))
                            files.Add(refFile);
                    }
                }
            }

            if (files.Count == 0)
                return (null, files);

            static int Priority(string p)
            {
                var ext = Path.GetExtension(p).ToLowerInvariant();
                return ext switch
                {
                    ".m3u" or ".j3u" => 0,
                    ".cue" => 1,
                    ".chd" or ".iso" or ".pbp" or ".cso" or ".gcz" or ".rvz" or ".wbfs" => 2,
                    _ when PlatformResolver.AllRomExtensions.Contains(ext) => 3,
                    _ => 4
                };
            }

            var primary = files
                .OrderBy(Priority)
                .ThenBy(p => p.Length)
                .First();

            return (primary, files);
        }

        [HttpGet("/jellyemu/rom/download-zip/{itemId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DownloadZip(string itemId)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                return NotFound();
            }

            if (!VerifyItemAccess(item))
            {
                return Forbid();
            }

            return await ServeDirectoryOrZipAsync(item).ConfigureAwait(false);
        }

        private async Task<IActionResult> ServeDirectoryOrZipAsync(BaseItem item)
        {
            var filesToZip = _fileService.ResolveAllRomFiles(item.Path);
            if (filesToZip.Count == 0)
            {
                return NotFound();
            }

            // If the directory holds a single ROM / archive file, serve it directly without re-zipping
            if (filesToZip.Count == 1)
            {
                var singleFile = filesToZip[0];
                if (System.IO.File.Exists(singleFile))
                {
                    var singleInfo = new FileInfo(singleFile);
                    Response.Headers["X-Rom-Hash"] = GetFileHash(singleFile);
                    Response.Headers["X-Rom-Size"] = singleInfo.Length.ToString();
                    Response.Headers["X-Rom-Extension"] = singleInfo.Extension;
                    Response.Headers["X-Rom-Name"] = Path.GetFileNameWithoutExtension(singleFile);

                    var singleStream = OpenRomStream(singleFile);
                    var singleName = Path.GetFileName(singleFile);
                    Response.Headers["Content-Disposition"] = $"attachment; filename=\"{singleName}\"";
                    return File(singleStream, "application/octet-stream", enableRangeProcessing: true);
                }
            }

            var isDir = Directory.Exists(item.Path);
            var baseDir = item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var zipName = isDir
                ? $"{Path.GetFileName(baseDir)}.zip"
                : $"{Path.GetFileNameWithoutExtension(item.Path)}.zip";

            var tempZipPath = Path.Combine(Path.GetTempPath(), $"jellyemu_{Guid.NewGuid():N}.zip");

            try
            {
                using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        foreach (var filePath in filesToZip)
                        {
                            var entryName = isDir
                                ? Path.GetRelativePath(baseDir, filePath).Replace('\\', '/')
                                : Path.GetFileName(filePath);

                            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                            using var entryStream = entry.Open();
                            using var fileStream = System.IO.File.OpenRead(filePath);
                            await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
                        }
                    }
                }

                var zipFileInfo = new FileInfo(tempZipPath);
                Response.Headers["X-Rom-Size"] = zipFileInfo.Length.ToString();
                Response.Headers["X-Rom-Extension"] = ".zip";
                Response.Headers["X-Rom-Name"] = Path.GetFileNameWithoutExtension(zipName);
                Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Uri.EscapeDataString(zipName)}\"";

                // Open with DeleteOnClose so the temp zip file is cleaned up when the client response finishes streaming
                var stream = CreateDeleteOnCloseStream(tempZipPath);
                Response.RegisterForDispose(stream);
                return File(stream, "application/zip", enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to package directory into zip for {ItemPath}", item.Path);
                if (System.IO.File.Exists(tempZipPath))
                {
                    try { System.IO.File.Delete(tempZipPath); } catch { }
                }
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Opens a ROM for streaming to the emulator. 3DS images whose content is decrypted
        /// but whose NCCH header still claims encryption (old "Decrypted" dumps) get the
        /// NoCrypto flag set on the fly, otherwise Azahar refuses them and shows a black screen.
        /// The file on disk is never modified.
        /// </summary>
        private Stream OpenRomStream(string path)
        {
            var stream = System.IO.File.OpenRead(path);
            if (!N3dsRomInspector.IsCandidate(path)) return stream;

            try
            {
                var inspection = N3dsRomInspector.Inspect(stream);
                stream.Position = 0;

                if (inspection.State == N3dsCryptoState.Encrypted)
                {
                    Logger.LogWarning("[JellyEmu] 3DS ROM {Path} is encrypted; Azahar can only run decrypted dumps", path);
                }

                if (inspection.Patches.Count > 0)
                {
                    Logger.LogInformation(
                        "[JellyEmu] 3DS ROM {Path} is decrypted but flagged as encrypted; setting the NoCrypto flag on {Count} partition(s) while serving",
                        path, inspection.Patches.Count);
                    return new PatchedReadStream(stream, inspection.Patches);
                }
            }
            catch (IOException ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Could not inspect 3DS ROM header of {Path}; serving it unchanged", path);
                stream.Position = 0;
            }

            return stream;
        }

        private static FileStream CreateDeleteOnCloseStream(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        }

        /// <summary>
        /// Returns the resolved core name, whether it requires threads (SharedArrayBuffer),
        /// and which launcher to use for the given item.
        /// Used by the UI to decide iframe vs new tab launch, and which play page to load.
        /// 
        /// Path: GET /jellyemu/core/{itemId}
        /// Parameters:
        ///   - itemId (string, path): The unique ID of the item.
        /// Returns Example: { "core": "gba", "needsThreads": false, "launcher": "ejs" }
        ///          Example: { "core": "pico8", "needsThreads": false, "launcher": "pico8" }
        /// </summary>
        [HttpGet("/jellyemu/core/{itemId}")]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetCore(string itemId, [FromQuery] string? userId)
        {
            var item = LibraryManager.GetItemById(itemId);
            if (item == null)
                return NotFound();

            var info = ResolveCoreInfo(item, userId);
            return Ok(new { core = info.Core, needsThreads = info.NeedsThreads, launcher = info.Launcher });
        }

        /// <summary>
        /// Returns the total number of scanned ROMs in the library (items with the tag JellyEmu).
        /// Path: GET /jellyemu/roms/count/{userId}
        /// </summary>
        [HttpGet("/jellyemu/roms/count/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetRomCount(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            var count = LibraryManager.GetItemList(query)
                .Count(i => i.Tags != null && i.Tags.Contains("JellyEmu", StringComparer.OrdinalIgnoreCase));

            return Ok(new { total = count, count = count, total_roms = count });
        }

        /// <summary>
        /// Returns the list of system tags of all scanned ROMs, merged, with 1 entry per system.
        /// Dynamically returns the total number of systems currently uploaded.
        /// Path: GET /jellyemu/roms/systems/{userId}
        /// </summary>
        [HttpGet("/jellyemu/roms/systems/{userId}")]
        [Authorize]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult GetRomSystems(string userId)
        {
            if (!VerifyUser(userId)) return Forbid();

            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            var items = LibraryManager.GetItemList(query)
                .Where(i => i.Tags != null && i.Tags.Contains("JellyEmu", StringComparer.OrdinalIgnoreCase))
                .ToList();

            var knownSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var val in PlatformResolver.Aliases.Values)
                knownSystems.Add(val);
            foreach (var val in PlatformResolver.LibraryOnlyAliases.Values)
                knownSystems.Add(val);
            foreach (var key in CoreMap.Keys)
                knownSystems.Add(key);

            var systemsList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var resolved = _platformResolver.Resolve(item.Path);
                if (!string.IsNullOrEmpty(resolved) && !string.Equals(resolved, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    systemsList.Add(resolved);
                }

                if (item.Tags != null)
                {
                    foreach (var tag in item.Tags)
                    {
                        if (knownSystems.Contains(tag))
                        {
                            systemsList.Add(tag);
                        }
                    }
                }
            }

            var sortedSystems = systemsList.OrderBy(s => s).ToList();

            return Ok(new
            {
                systems = sortedSystems,
                totalSystems = sortedSystems.Count,
                count = sortedSystems.Count
            });
        }
    }
}