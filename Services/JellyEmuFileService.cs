using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    /// <summary>
    /// Service for locating and managing game files, multi-disc playlists, and sidecar assets.
    /// </summary>
    public class JellyEmuFileService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuFileService> _logger;

        public JellyEmuFileService(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuFileService> logger)
        {
            _libraryManager = libraryManager;
            _appPaths = appPaths;
            _logger = logger;
        }

        #region Manuals and Sidecars

        /// <summary>
        /// Locates the physical path to a local manual file for a library item.
        /// </summary>
        public string? GetLocalManualPath(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
            {
                return null;
            }

            return TryGetLocalManualPath(item.Path);
        }

        /// <summary>
        /// Checks if a local manual file exists alongside the ROM file.
        /// Priority:
        /// 1. Same name as the ROM, with .pdf or .PDF extension (e.g. Game.sfc -> Game.pdf)
        /// 2. Variants like Game-manual.pdf, Game_manual.pdf
        /// 3. Generic manual.pdf or Manual.pdf in the same directory
        /// </summary>
        public static string? TryGetLocalManualPath(string? itemPath)
        {
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return null;
            }

            try
            {
                // 1. Same base filename with .pdf or .PDF
                var pdfPath = Path.ChangeExtension(itemPath, ".pdf");
                if (File.Exists(pdfPath)) return pdfPath;

                var pdfUpper = Path.ChangeExtension(itemPath, ".PDF");
                if (File.Exists(pdfUpper)) return pdfUpper;

                var dir = Path.GetDirectoryName(itemPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var baseName = Path.GetFileNameWithoutExtension(itemPath);

                    // 2. BaseName-manual.pdf / BaseName_manual.pdf
                    var dashManual = Path.Combine(dir, $"{baseName}-manual.pdf");
                    if (File.Exists(dashManual)) return dashManual;

                    var underscoreManual = Path.Combine(dir, $"{baseName}_manual.pdf");
                    if (File.Exists(underscoreManual)) return underscoreManual;

                    // 3. manual.pdf / Manual.pdf
                    var manual = Path.Combine(dir, "manual.pdf");
                    if (File.Exists(manual)) return manual;

                    var manualUpper = Path.Combine(dir, "Manual.pdf");
                    if (File.Exists(manualUpper)) return manualUpper;
                }
            }
            catch
            {
                // Ignore file system / format exceptions
            }

            return null;
        }

        #endregion

        #region Playlists and Multi-disc ROMs

        private static readonly Regex SafeIdRegex = new Regex("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

        /// <summary>
        /// Validates that an identifier contains only safe alphanumeric/hyphen/underscore characters.
        /// </summary>
        public static bool IsValidId(string? id)
        {
            return !string.IsNullOrWhiteSpace(id) && SafeIdRegex.IsMatch(id);
        }

        /// <summary>
        /// Gets the sanitized and validated user directory inside the jellyemu-saves storage.
        /// </summary>
        public string GetSafeUserSavesDir(string userId)
        {
            if (!IsValidId(userId))
            {
                throw new ArgumentException("Invalid user ID format.", nameof(userId));
            }

            var safeUserId = Path.GetFileName(userId.TrimStart('/', '\\'));
            var baseDir = Path.GetFullPath(Path.Combine(_appPaths.DataPath, "jellyemu-saves"));
            var userDir = Path.GetFullPath(Path.Combine(baseDir, safeUserId));
            if (!userDir.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Path traversal detected in user ID.");
            }

            return userDir;
        }

        /// <summary>
        /// Returns the validated path to the user's active disc metadata file for an item.
        /// </summary>
        public string GetItemMetaPath(string userId, string itemId)
        {
            if (!IsValidId(itemId))
            {
                throw new ArgumentException("Invalid item ID format.", nameof(itemId));
            }

            var userDir = GetSafeUserSavesDir(userId);
            var safeItemId = Path.GetFileName(itemId.TrimStart('/', '\\'));
            var metaPath = Path.GetFullPath(Path.Combine(userDir, $"{safeItemId}-meta.json"));
            if (!metaPath.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Path traversal detected in item ID.");
            }

            return metaPath;
        }

        /// <summary>
        /// Reads the active disc index for a multi-disc game from user metadata. Defaults to 1 if not found or out of range.
        /// </summary>
        public int GetActiveDiscIndex(string? userId, string itemId, int maxDiscs = int.MaxValue)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(itemId) || !IsValidId(userId) || !IsValidId(itemId))
            {
                return 1;
            }

            try
            {
                var metaPath = GetItemMetaPath(userId, itemId);
                if (!File.Exists(metaPath))
                {
                    return 1;
                }

                var json = File.ReadAllText(metaPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("activeDiscIndex", out var prop))
                {
                    int index = prop.GetInt32();
                    if (index >= 1 && index <= maxDiscs)
                    {
                        return index;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[JellyEmu] Could not read active disc index from user {UserId} item {ItemId}", userId, itemId);
            }

            return 1;
        }

        /// <summary>
        /// Sets and saves the active disc index for a multi-disc game.
        /// </summary>
        public void SetActiveDiscIndex(string userId, string itemId, int activeDiscIndex)
        {
            if (!IsValidId(userId) || !IsValidId(itemId))
            {
                _logger.LogWarning("[JellyEmu] Cannot set active disc index: invalid userId '{UserId}' or itemId '{ItemId}'", userId, itemId);
                return;
            }

            var userDir = GetSafeUserSavesDir(userId);
            Directory.CreateDirectory(userDir);

            var metaPath = GetItemMetaPath(userId, itemId);
            var payload = new { activeDiscIndex };
            File.WriteAllText(metaPath, JsonSerializer.Serialize(payload));
        }

        /// <summary>
        /// Resolves the effective ROM file path to execute/serve, considering .j3u playlists and the user's active disc index.
        /// </summary>
        public string? ResolveActiveRomPath(string itemPath, string? userId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemPath) || !File.Exists(itemPath))
            {
                return null;
            }

            if (itemPath.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase))
            {
                var discFiles = J3uParser.GetReferencedFiles(itemPath);
                if (discFiles.Count == 0)
                {
                    return null;
                }

                int activeDiscIndex = GetActiveDiscIndex(userId, itemId, discFiles.Count);
                return discFiles[activeDiscIndex - 1];
            }

            return itemPath;
        }

        /// <summary>
        /// Resolves all physical files associated with a ROM item (expanding .j3u playlists) that exist on disk.
        /// </summary>
        public List<string> ResolveAllRomFiles(string itemPath)
        {
            var files = new List<string>();
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return files;
            }

            if (itemPath.EndsWith(".j3u", StringComparison.OrdinalIgnoreCase))
            {
                files.AddRange(J3uParser.GetReferencedFiles(itemPath));
            }
            else
            {
                files.Add(itemPath);
            }

            return files.Where(File.Exists).ToList();
        }

        #endregion
    }
}
