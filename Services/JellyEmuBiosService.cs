using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

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

        private static readonly Dictionary<string, List<string>> KnownBiosFilenames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "PlayStation", new() { "scph5501.bin", "scph1001.bin", "scph7001.bin", "scph5500.bin", "scph5502.bin", "scph1000.bin", "ps1_rom.bin", "psx.bin", "psx.zip", "psx.7z" } },
            { "Game Boy Advance", new() { "gba_bios.bin", "gba.bin", "gba_bios.zip", "gba.zip" } },
            { "Nintendo DS", new() { "nds_bios_arm7.bin", "nds_bios_arm9.bin", "firmware.bin", "bios7.bin", "bios9.bin", "nds.zip" } },
            { "NES", new() { "disksys.rom", "disksys.bin", "fds.rom" } },
            { "Sega CD", new() { "bios_CD_U.bin", "bios_CD_E.bin", "bios_CD_J.bin", "segacd_bios.bin" } },
            { "Sega Saturn", new() { "saturn_bios.bin", "sega_101.bin", "mpr-17933.bin" } },
            { "Dreamcast", new() { "dc_boot.bin", "dc_flash.bin" } },
            { "Neo Geo", new() { "neogeo.zip", "neogeo.bin" } },
            { "Nintendo 3DS", new() { "boot.firm", "sysdata.zip", "3ds_bios.bin" } }
        };

        public string? ResolveBiosRelativePath(string platformTag, string core)
        {
            var root = GetBiosDirectory();
            if (!Directory.Exists(root)) return null;

            var assignments = Plugin.Instance?.Configuration.BiosAssignments;
            if (assignments != null && assignments.Count > 0)
            {
                var keysToCheck = new[] { platformTag, core, MapTagToShortName(platformTag) }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var k in keysToCheck)
                {
                    if (assignments.TryGetValue(k, out var assignedRel) && !string.IsNullOrWhiteSpace(assignedRel))
                    {
                        var fullAssigned = Path.Combine(root, assignedRel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullAssigned))
                        {
                            return assignedRel.Replace('\\', '/');
                        }
                    }
                }
            }

            var candidateDirs = new[] { platformTag, core, MapTagToShortName(platformTag) }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var sub in candidateDirs)
            {
                var subDir = Path.Combine(root, sub);
                if (Directory.Exists(subDir))
                {
                    var files = Directory.GetFiles(subDir);
                    if (files.Length > 0)
                    {
                        var first = files[0];
                        return Path.GetRelativePath(root, first).Replace('\\', '/');
                    }
                }
            }

            if (!string.IsNullOrEmpty(platformTag) && KnownBiosFilenames.TryGetValue(platformTag, out var knownList))
            {
                foreach (var fn in knownList)
                {
                    var p = Path.Combine(root, fn);
                    if (File.Exists(p)) return fn;
                }
            }

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

        public List<BiosInfo> ListInstalledBios()
        {
            var list = new List<BiosInfo>();
            var root = GetBiosDirectory();
            if (!Directory.Exists(root)) return list;

            var assignments = Plugin.Instance?.Configuration.BiosAssignments;

            var allFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var fileName = Path.GetFileName(file);
                var fi = new FileInfo(file);
                
                string sys = string.Empty;
                if (assignments != null)
                {
                    foreach (var kvp in assignments)
                    {
                        if (string.Equals(kvp.Value, rel, StringComparison.OrdinalIgnoreCase))
                        {
                            sys = kvp.Key;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(sys))
                {
                    sys = GuessSystem(rel);
                }

                list.Add(new BiosInfo(rel, fileName, sys, fi.Length));
            }
            return list;
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
