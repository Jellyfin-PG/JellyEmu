using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace JellyEmu.Services
{
    public class BiosInfo
    {
        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("systemOrCore")]
        public string SystemOrCore { get; set; } = string.Empty;

        [JsonPropertyName("systemDescription")]
        public string SystemDescription { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("md5")]
        public string Md5 { get; set; } = string.Empty;

        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "Unrecognized"; // "Verified", "Mismatch", "Unrecognized"

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("expectedMd5")]
        public string? ExpectedMd5 { get; set; }

        [JsonPropertyName("expectedSha1")]
        public string? ExpectedSha1 { get; set; }

        public BiosInfo() { }

        public BiosInfo(string relativePath, string fileName, string systemOrCore, long sizeBytes)
        {
            RelativePath = relativePath;
            FileName = fileName;
            SystemOrCore = systemOrCore;
            SizeBytes = sizeBytes;
        }
    }

    public class JellyEmuBiosService
    {
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuBiosService> _logger;

        public JellyEmuBiosService(IApplicationPaths appPaths, ILogger<JellyEmuBiosService> logger)
        {
            _appPaths = appPaths;
            _logger = logger;
            EnsureBiosDirectory();
        }

        public string GetBiosDirectory()
        {
            var customPath = Plugin.Instance?.Configuration.BiosPath;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                return customPath;
            }
            return Path.Combine(_appPaths.DataPath, "jellyemu-bios");
        }

        public void EnsureBiosDirectory()
        {
            try
            {
                var dir = GetBiosDirectory();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    _logger.LogInformation("[JellyEmu] Created BIOS folder at {Path}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to create BIOS folder.");
            }
        }

        public static (string md5, string sha1) ComputeFileHashes(string filePath)
        {
            try
            {
                using var fileStream = File.OpenRead(filePath);
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();

                var md5Hash = BitConverter.ToString(md5.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();
                fileStream.Position = 0;
                var sha1Hash = BitConverter.ToString(sha1.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();
                return (md5Hash, sha1Hash);
            }
            catch
            {
                return (string.Empty, string.Empty);
            }
        }

        public List<BiosInfo> ListInstalledBios()
        {
            var list = new List<BiosInfo>();
            var root = GetBiosDirectory();
            if (!Directory.Exists(root)) return list;

            var activeAssignments = Plugin.Instance?.Configuration.ActiveBios;
            var db = LibretroSystemDatabase.Instance;

            var allFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var fileName = Path.GetFileName(file);
                var fi = new FileInfo(file);

                var (md5, sha1) = ComputeFileHashes(file);
                var validation = db.ValidateBios(fileName, md5, sha1, fi.Length);

                string sys = validation.SystemOrPlatform;
                string desc = validation.SystemDescription;
                string status = validation.Status.ToString();

                if (validation.Status == BiosValidationStatus.Unrecognized)
                {
                    var guessed = GuessSystem(rel);
                    if (guessed != "General" && guessed != "Unknown")
                    {
                        sys = guessed;
                        desc = guessed;
                    }
                }

                bool isActive = false;
                if (activeAssignments != null && !string.IsNullOrEmpty(sys))
                {
                    if (activeAssignments.TryGetValue(sys, out var activeRel) &&
                        string.Equals(activeRel, rel, StringComparison.OrdinalIgnoreCase))
                    {
                        isActive = true;
                    }
                }

                list.Add(new BiosInfo
                {
                    RelativePath = rel,
                    FileName = fileName,
                    SystemOrCore = sys,
                    SystemDescription = desc,
                    SizeBytes = fi.Length,
                    Md5 = md5,
                    Sha1 = sha1,
                    Status = status,
                    IsActive = isActive,
                    ExpectedMd5 = validation.ExpectedMd5,
                    ExpectedSha1 = validation.ExpectedSha1
                });
            }

            // Ensure every system with BIOS files has an active selection
            var grouped = list.Where(b => !string.IsNullOrEmpty(b.SystemOrCore) && b.SystemOrCore != "Unknown")
                              .GroupBy(b => b.SystemOrCore, StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                if (!group.Any(b => b.IsActive))
                {
                    // Prioritize first Verified BIOS file, otherwise first in group
                    var defaultActive = group.FirstOrDefault(b => b.Status == "Verified") ?? group.First();
                    defaultActive.IsActive = true;
                }
            }

            return list;
        }

        public string? ResolveBiosRelativePath(string platformTag, string core)
        {
            var root = GetBiosDirectory();
            if (!Directory.Exists(root)) return null;

            var installed = ListInstalledBios();

            var candidateKeys = new[] { platformTag, core, MapTagToShortName(platformTag) }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Explicit ActiveBios configuration match
            var activeAssignments = Plugin.Instance?.Configuration.ActiveBios;
            if (activeAssignments != null && activeAssignments.Count > 0)
            {
                foreach (var k in candidateKeys)
                {
                    if (activeAssignments.TryGetValue(k, out var assignedRel) && !string.IsNullOrWhiteSpace(assignedRel))
                    {
                        var fullAssigned = Path.Combine(root, assignedRel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullAssigned))
                        {
                            return assignedRel.Replace('\\', '/');
                        }
                    }
                }
            }

            // Active file from installed list for this system
            foreach (var k in candidateKeys)
            {
                var activeForSys = installed.FirstOrDefault(b =>
                    string.Equals(b.SystemOrCore, k, StringComparison.OrdinalIgnoreCase) && b.IsActive);
                if (activeForSys != null)
                {
                    return activeForSys.RelativePath;
                }
            }

            // First verified BIOS for this system
            foreach (var k in candidateKeys)
            {
                var verifiedForSys = installed.FirstOrDefault(b =>
                    string.Equals(b.SystemOrCore, k, StringComparison.OrdinalIgnoreCase) && b.Status == "Verified");
                if (verifiedForSys != null)
                {
                    return verifiedForSys.RelativePath;
                }
            }

            // Any BIOS matching the system
            foreach (var k in candidateKeys)
            {
                var anyForSys = installed.FirstOrDefault(b =>
                    string.Equals(b.SystemOrCore, k, StringComparison.OrdinalIgnoreCase));
                if (anyForSys != null)
                {
                    return anyForSys.RelativePath;
                }
            }

            // Check subdirectories directly (e.g. root/GBA/*)
            foreach (var sub in candidateKeys)
            {
                var subDir = Path.Combine(root, sub);
                if (Directory.Exists(subDir))
                {
                    var files = Directory.GetFiles(subDir);
                    if (files.Length > 0)
                    {
                        return Path.GetRelativePath(root, files[0]).Replace('\\', '/');
                    }
                }
            }

            // Name candidates in root
            var nameCandidates = new[]
            {
                $"{platformTag}.bin", $"{platformTag}.rom", $"{platformTag}.zip",
                $"{core}.bin", $"{core}.rom", $"{core}.zip",
                $"{MapTagToShortName(platformTag)}.bin", $"{MapTagToShortName(platformTag)}.rom", $"{MapTagToShortName(platformTag)}.zip"
            };

            foreach (var fn in nameCandidates)
            {
                var p = Path.Combine(root, fn);
                if (File.Exists(p)) return fn;
            }

            return null;
        }

        public void SetActiveBios(string systemOrPlatform, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(systemOrPlatform) || Plugin.Instance == null) return;

            var cfg = Plugin.Instance.Configuration;
            var active = cfg.ActiveBios ?? new(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                active.Remove(systemOrPlatform);
            }
            else
            {
                active[systemOrPlatform] = relativePath.Replace('\\', '/');
            }

            cfg.ActiveBios = active;
            Plugin.Instance.SaveConfiguration();
        }

        private static string MapTagToShortName(string tag)
        {
            return tag switch
            {
                "PlayStation" => "PS1",
                "Game Boy Advance" => "GBA",
                "Nintendo DS" => "NDS",
                "Nintendo 3DS" => "3DS",
                "Sega Genesis" => "SegaMD",
                _ => tag
            };
        }

        private static string GuessSystem(string relPath)
        {
            var fn = Path.GetFileName(relPath).ToLowerInvariant();
            var dir = Path.GetDirectoryName(relPath)?.ToLowerInvariant() ?? "";

            if (dir.Contains("ps1") || dir.Contains("playstation") || fn.Contains("scph") || fn.Contains("psx")) return "PlayStation";
            if (dir.Contains("gba") || fn.Contains("gba")) return "Game Boy Advance";
            if (dir.Contains("nds") || fn.Contains("nds") || fn.Contains("arm7") || fn.Contains("arm9")) return "Nintendo DS";
            if (fn.Contains("disksys") || fn.Contains("fds")) return "NES";
            if (dir.Contains("segacd") || fn.Contains("segacd") || fn.Contains("bios_cd")) return "Sega CD";
            if (dir.Contains("saturn") || fn.Contains("saturn")) return "Sega Saturn";
            if (dir.Contains("dreamcast") || fn.Contains("dc_")) return "Dreamcast";
            if (fn.Contains("neogeo")) return "Neo Geo";
            if (dir.Contains("3ds") || fn.Contains("boot.firm")) return "Nintendo 3DS";
            return "General";
        }
    }
}
