using System.Globalization;
using System.IO.Compression;
using System.Text;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Exposes the JellyEmu library as a libretro buildbot assets server, so
    /// RetroArch's Online Updater (Content Downloader / Core System Files Downloader)
    /// can browse and download ROMs and BIOS files directly. Point RetroArch's
    /// "Buildbot Assets URL" at http://your-server:8096/jellyemu/
    ///
    ///   GET /jellyemu/cores/.index-dirs              — one system per line ("NES", "SNES", …)
    ///   GET /jellyemu/cores/{system}/.index          — one downloadable filename per line
    ///   GET /jellyemu/cores/{system}/.index-extended — "yyyy-MM-dd hash filename" per line
    ///   GET /jellyemu/cores/{system}/{filename}      — the ROM itself (multi-file sets as a zip)
    ///   GET /jellyemu/system/{*path}                 — BIOS folder browsing (.index/.index-dirs/.index-extended) and files
    ///   GET /jellyemu/frontend/{file}                — 302 to the libretro buildbot's frontend assets
    /// </summary>
    public class RetroArchAssetsController : JellyEmuBaseController
    {
        private readonly JellyEmuBiosService _biosService;

        public RetroArchAssetsController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<RetroArchAssetsController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory,
            JellyEmuBiosService biosService)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory)
        {
            _biosService = biosService;
        }

        /// <summary>
        /// A single downloadable entry within a system directory.
        /// </summary>
        private sealed record AssetEntry(string DisplayName, List<string> Files, bool IsZipBundle, DateTime AddedDate);

        // =========================================================================
        // Library
        // =========================================================================

        private List<BaseItem> GetRomItems()
        {
            var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Book },
                Recursive = true,
            };

            return LibraryManager.GetItemList(query)
                .Where(i => i.Tags != null && i.Tags.Contains("JellyEmu", StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Resolves an item to the physical files RetroArch should receive.
        /// Returns null when the item has no usable files on disk.
        /// </summary>
        private static AssetEntry? BuildEntry(BaseItem item)
        {
            var path = item.Path;
            if (string.IsNullOrEmpty(path))
                return null;

            // The date the item was added to the Jellyfin library.
            var added = item.DateCreated;

            // .j3u multi-disc playlists become .zip files
            if (path.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase))
            {
                var discs = J3uParser.GetReferencedFiles(path).Where(System.IO.File.Exists).ToList();
                if (discs.Count == 0)
                    return null;
                return new AssetEntry($"{Path.GetFileNameWithoutExtension(path)}.zip", discs, IsZipBundle: true, added);
            }

            // Directory item (single cue inside) or bare .cue becomes a zip of cue + bins
            var effective = RomExtensions.EffectiveRomPath(path);
            if (effective.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(effective))
            {
                var files = new List<string> { effective };
                files.AddRange(CueParser.GetReferencedFiles(effective).Where(System.IO.File.Exists));
                var baseName = Directory.Exists(path)
                    ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : Path.GetFileNameWithoutExtension(effective);
                return new AssetEntry($"{baseName}.zip", files, IsZipBundle: true, added);
            }

            if (!System.IO.File.Exists(effective))
                return null;

            return new AssetEntry(Path.GetFileName(effective), new List<string> { effective }, IsZipBundle: false, added);
        }

        /// <summary>
        /// Groups the library into system to entries.
        /// 
        /// This uses the canonical platform names ("NES", "Game Boy Advance", etc). The
        /// same mapping is used for the file serving too.
        /// </summary>
        private Dictionary<string, Dictionary<string, AssetEntry>> GetSystems()
        {
            var systems = new Dictionary<string, Dictionary<string, AssetEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in GetRomItems())
            {
                var platform = ResolvePlatformTag(item);
                if (string.IsNullOrEmpty(platform) || string.Equals(platform, "Unknown", StringComparison.OrdinalIgnoreCase))
                    continue;

                var entry = BuildEntry(item);
                if (entry == null)
                    continue;

                if (!systems.TryGetValue(platform, out var entries))
                {
                    entries = new Dictionary<string, AssetEntry>(StringComparer.OrdinalIgnoreCase);
                    systems[platform] = entries;
                }

                if (!entries.TryAdd(entry.DisplayName, entry))
                {
                    Logger.LogWarning("[JellyEmu/RetroArch] Duplicate filename {Name} in system {System}; keeping first", entry.DisplayName, platform);
                }
            }

            return systems;
        }

        private ContentResult IndexResult(IEnumerable<string> lines)
        {
            var sb = new StringBuilder();
            foreach (var line in lines)
                sb.Append(line).Append('\n');
            return Content(sb.ToString(), "text/plain", Encoding.UTF8);
        }

        private static string EntryDate(IEnumerable<string> files)
        {
            var latest = DateTime.MinValue;
            foreach (var f in files)
            {
                try
                {
                    var t = System.IO.File.GetLastWriteTimeUtc(f);
                    if (t > latest) latest = t;
                }
                catch { }
            }
            if (latest == DateTime.MinValue) latest = DateTime.UtcNow;
            return latest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // =========================================================================
        // GET /jellyemu/cores/.index-dirs
        // =========================================================================
        [HttpGet("/jellyemu/cores/.index-dirs")]
        [HttpHead("/jellyemu/cores/.index-dirs")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult CoresIndexDirs()
        {
            var systems = GetSystems().Keys.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
            return IndexResult(systems);
        }

        // =========================================================================
        // GET /jellyemu/cores/{system}/.index
        // =========================================================================
        [HttpGet("/jellyemu/cores/{system}/.index")]
        [HttpHead("/jellyemu/cores/{system}/.index")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CoresIndex(string system)
        {
            if (!GetSystems().TryGetValue(system, out var entries))
                return NotFound();

            return IndexResult(entries.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        }

        // =========================================================================
        // GET /jellyemu/cores/{system}/.index-extended
        // =========================================================================
        [HttpGet("/jellyemu/cores/{system}/.index-extended")]
        [HttpHead("/jellyemu/cores/{system}/.index-extended")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult CoresIndexExtended(string system)
        {
            if (!GetSystems().TryGetValue(system, out var entries))
                return NotFound();

            // TODO: The .index-extended should provide the CRC32 of the file, though
            // that could take a while to calculate, so for now we use "0" as a
            // placeholder. This is the intended format:
            // "yyyy-MM-dd crc32 filename"
            var lines = entries.Values
                .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(e => $"{e.AddedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 0 {e.DisplayName}");
            return IndexResult(lines);
        }

        // =========================================================================
        // GET /jellyemu/cores/{system}/{filename}
        //
        // Serves the ROM named in the .index. Multi-file sets stream as a zip,
        // which RetroArch extracts into its downloads directory.
        // =========================================================================
        [HttpGet("/jellyemu/cores/{system}/{filename}")]
        [HttpHead("/jellyemu/cores/{system}/{filename}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CoresFile(string system, string filename)
        {
            if (!GetSystems().TryGetValue(system, out var entries) ||
                !entries.TryGetValue(filename, out var entry))
            {
                Logger.LogWarning("[JellyEmu/RetroArch] Asset not found: {System}/{Filename}", system, filename);
                return NotFound();
            }

            Logger.LogInformation("[JellyEmu/RetroArch] Serving asset {System}/{Filename}", system, filename);

            if (!entry.IsZipBundle)
            {
                var stream = System.IO.File.OpenRead(entry.Files[0]);
                Response.Headers["Content-Disposition"] = $"attachment; filename=\"{entry.DisplayName}\"";
                return File(stream, "application/octet-stream", enableRangeProcessing: true);
            }

            Response.ContentType = "application/zip";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Uri.EscapeDataString(entry.DisplayName)}\"";

            using (var archive = new ZipArchive(Response.Body, ZipArchiveMode.Create, true))
            {
                foreach (var filePath in entry.Files)
                {
                    var zipEntry = archive.CreateEntry(Path.GetFileName(filePath), CompressionLevel.Fastest);
                    using var entryStream = zipEntry.Open();
                    using var fileStream = System.IO.File.OpenRead(filePath);
                    await fileStream.CopyToAsync(entryStream).ConfigureAwait(false);
                }
            }

            return new EmptyResult();
        }

        // =========================================================================
        // GET /jellyemu/system/{**path}
        //
        // Buildbot-style browsing of the BIOS directory (jellyemu-bios or the
        // configured BiosPath), for RetroArch's Core System Files Downloader:
        //   .index-dirs      — subdirectories at this level
        //   .index           — files at this level
        //   .index-extended  — files with date + hash
        //   anything else    — the BIOS file itself
        // =========================================================================
        [HttpGet("/jellyemu/system")]
        [HttpHead("/jellyemu/system")]
        [HttpGet("/jellyemu/system/{**path}")]
        [HttpHead("/jellyemu/system/{**path}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult SystemAssets(string? path = null)
        {
            var biosRoot = Path.GetFullPath(_biosService.GetBiosDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relative = (path ?? string.Empty).Replace('\\', '/').Trim('/');

            string? listing = null;
            if (relative.Length == 0 || relative == ".index-dirs" || relative.EndsWith("/.index-dirs", StringComparison.Ordinal))
                listing = ".index-dirs";
            else if (relative == ".index-extended" || relative.EndsWith("/.index-extended", StringComparison.Ordinal))
                listing = ".index-extended";
            else if (relative == ".index" || relative.EndsWith("/.index", StringComparison.Ordinal))
                listing = ".index";

            var target = listing == null
                ? relative
                : relative[..Math.Max(0, relative.Length - listing.Length)].TrimEnd('/');

            var fullPath = Path.GetFullPath(Path.Combine(biosRoot, target.Replace('/', Path.DirectorySeparatorChar)));

            // The path must be the BIOS root itself or live under it.
            if (!string.Equals(fullPath, biosRoot, StringComparison.Ordinal) &&
                !fullPath.StartsWith(biosRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Logger.LogWarning("[JellyEmu/RetroArch] Rejected BIOS path traversal attempt: {Path}", path);
                return NotFound();
            }

            if (listing != null)
            {
                if (!Directory.Exists(fullPath))
                    return NotFound();

                if (listing == ".index-dirs")
                {
                    var dirs = Directory.GetDirectories(fullPath)
                        .Select(d => Path.GetFileName(d)!)
                        .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
                    return IndexResult(dirs);
                }

                var files = Directory.GetFiles(fullPath)
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                if (listing == ".index")
                    return IndexResult(files.Select(f => Path.GetFileName(f)!));

                // TODO: Provide the CRC32 here. "0" is a placeholder.
                // "yyyy-MM-dd crc32 filename"
                return IndexResult(files.Select(f => $"{EntryDate(new[] { f })} 0 {Path.GetFileName(f)}"));
            }

            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            Logger.LogInformation("[JellyEmu/RetroArch] Serving BIOS file {Path}", relative);
            var stream = System.IO.File.OpenRead(fullPath);
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{Path.GetFileName(fullPath)}\"";
            return File(stream, "application/octet-stream", enableRangeProcessing: true);
        }

        // =========================================================================
        // GET /jellyemu/frontend/{file}
        //
        // RetroArch also fetches frontend assets (assets.zip, overlays.zip, …) from
        // its buildbot assets URL. JellyEmu doesn't host those, so redirect the
        // known asset names to the real libretro buildbot to keep the rest of the
        // Online Updater working while pointed at JellyEmu.
        // =========================================================================
        private static readonly HashSet<string> FrontendAssets = new(StringComparer.OrdinalIgnoreCase)
        {
            "assets.zip",
            "autoconfig.zip",
            "cheats.zip",
            "database-cursors.zip",
            "database-rdb.zip",
            "glui_minimal_assets.zip",
            "info.zip",
            "overlays.zip",
            "shaders_cg.zip",
            "shaders_glsl.zip",
            "shaders_slang.zip",
        };

        [HttpGet("/jellyemu/frontend/{file}")]
        [HttpHead("/jellyemu/frontend/{file}")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Frontend(string file)
        {
            if (!FrontendAssets.Contains(file))
                return NotFound();

            return Redirect($"https://buildbot.libretro.com/assets/frontend/{file}");
        }
    }
}
