using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JellyEmu.Services
{
    public enum BiosValidationStatus
    {
        Verified,
        Mismatch,
        Unrecognized
    }

    public class LibretroBiosEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("fileNameOnly")]
        public string FileNameOnly { get; set; } = string.Empty;

        [JsonPropertyName("rawSystem")]
        public string RawSystem { get; set; } = string.Empty;

        [JsonPropertyName("platformTag")]
        public string PlatformTag { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("crc")]
        public string Crc { get; set; } = string.Empty;

        [JsonPropertyName("md5")]
        public string Md5 { get; set; } = string.Empty;

        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = string.Empty;
    }

    public class BiosValidationResult
    {
        public BiosValidationStatus Status { get; set; }
        public string SystemOrPlatform { get; set; } = string.Empty;
        public string SystemDescription { get; set; } = string.Empty;
        public string? ExpectedMd5 { get; set; }
        public string? ExpectedSha1 { get; set; }
        public long? ExpectedSize { get; set; }
        public string? CanonicalName { get; set; }
    }

    /// <summary>
    /// In-memory catalog of official Libretro BIOS entries loaded from modern JSON data.
    /// Provides filename, MD5, and SHA1 hash lookup and integrity validation.
    /// </summary>
    public class LibretroSystemDatabase
    {
        private static readonly Lazy<LibretroSystemDatabase> _instance =
            new(() => new LibretroSystemDatabase());

        public static LibretroSystemDatabase Instance => _instance.Value;

        private readonly List<LibretroBiosEntry> _entries = new();
        private readonly Dictionary<string, List<LibretroBiosEntry>> _byFileName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LibretroBiosEntry> _byMd5 = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LibretroBiosEntry> _bySha1 = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<LibretroBiosEntry> Entries => _entries;

        public LibretroSystemDatabase(Stream? stream = null)
        {
            if (stream != null)
            {
                LoadFromJsonStream(stream);
            }
            else
            {
                LoadEmbeddedDatabase();
            }
        }

        private void LoadEmbeddedDatabase()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("system_bios.json", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(resourceName))
            {
                using var resStream = assembly.GetManifestResourceStream(resourceName);
                if (resStream != null)
                {
                    LoadFromJsonStream(resStream);
                    return;
                }
            }

            // Fallback: check file on disk if running in test / dev environment
            var localPath = Path.Combine(AppContext.BaseDirectory, "Resources", "system_bios.json");
            if (File.Exists(localPath))
            {
                using var fileStream = File.OpenRead(localPath);
                LoadFromJsonStream(fileStream);
            }
        }

        public void LoadFromJsonStream(Stream stream)
        {
            var entries = JsonSerializer.Deserialize<List<LibretroBiosEntry>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries == null) return;

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.PlatformTag) && !string.IsNullOrEmpty(entry.RawSystem))
                {
                    entry.PlatformTag = MapLibretroSystemToPlatformTag(entry.RawSystem);
                }
                if (string.IsNullOrEmpty(entry.FileNameOnly) && !string.IsNullOrEmpty(entry.Name))
                {
                    entry.FileNameOnly = Path.GetFileName(entry.Name);
                }

                _entries.Add(entry);

                if (!string.IsNullOrEmpty(entry.FileNameOnly))
                {
                    if (!_byFileName.TryGetValue(entry.FileNameOnly, out var list))
                    {
                        list = new List<LibretroBiosEntry>();
                        _byFileName[entry.FileNameOnly] = list;
                    }
                    list.Add(entry);
                }

                if (!string.IsNullOrEmpty(entry.Md5) && !_byMd5.ContainsKey(entry.Md5))
                {
                    _byMd5[entry.Md5] = entry;
                }

                if (!string.IsNullOrEmpty(entry.Sha1) && !_bySha1.ContainsKey(entry.Sha1))
                {
                    _bySha1[entry.Sha1] = entry;
                }
            }
        }

        public BiosValidationResult ValidateBios(string fileName, string? md5, string? sha1, long? sizeBytes)
        {
            var normalizedMd5 = md5?.Trim().ToLowerInvariant();
            var normalizedSha1 = sha1?.Trim().ToLowerInvariant();
            var fileNameOnly = Path.GetFileName(fileName);

            // Direct MD5 match
            if (!string.IsNullOrEmpty(normalizedMd5) && _byMd5.TryGetValue(normalizedMd5, out var md5Entry))
            {
                return new BiosValidationResult
                {
                    Status = BiosValidationStatus.Verified,
                    SystemOrPlatform = md5Entry.PlatformTag,
                    SystemDescription = md5Entry.RawSystem,
                    ExpectedMd5 = md5Entry.Md5,
                    ExpectedSha1 = md5Entry.Sha1,
                    ExpectedSize = md5Entry.Size,
                    CanonicalName = md5Entry.FileNameOnly
                };
            }

            // Direct SHA1 match
            if (!string.IsNullOrEmpty(normalizedSha1) && _bySha1.TryGetValue(normalizedSha1, out var sha1Entry))
            {
                return new BiosValidationResult
                {
                    Status = BiosValidationStatus.Verified,
                    SystemOrPlatform = sha1Entry.PlatformTag,
                    SystemDescription = sha1Entry.RawSystem,
                    ExpectedMd5 = sha1Entry.Md5,
                    ExpectedSha1 = sha1Entry.Sha1,
                    ExpectedSize = sha1Entry.Size,
                    CanonicalName = sha1Entry.FileNameOnly
                };
            }

            // Filename matched in database but hashes do not match -> Checksum Mismatch (bad dump/corrupted)
            if (_byFileName.TryGetValue(fileNameOnly, out var namedEntries) && namedEntries.Count > 0)
            {
                var candidate = namedEntries[0];
                return new BiosValidationResult
                {
                    Status = BiosValidationStatus.Mismatch,
                    SystemOrPlatform = candidate.PlatformTag,
                    SystemDescription = candidate.RawSystem,
                    ExpectedMd5 = candidate.Md5,
                    ExpectedSha1 = candidate.Sha1,
                    ExpectedSize = candidate.Size,
                    CanonicalName = candidate.FileNameOnly
                };
            }

            // Unrecognized
            return new BiosValidationResult
            {
                Status = BiosValidationStatus.Unrecognized,
                SystemOrPlatform = "Unknown",
                SystemDescription = "Unrecognized BIOS"
            };
        }

        public static string MapLibretroSystemToPlatformTag(string libretroSystem)
        {
            if (string.IsNullOrWhiteSpace(libretroSystem)) return "Unknown";

            var s = libretroSystem.Trim();

            if (s.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)) return "PlayStation 2";
            if (s.Contains("PlayStation Portable", StringComparison.OrdinalIgnoreCase) || s.Contains("PPSSPP", StringComparison.OrdinalIgnoreCase)) return "PlayStation Portable";
            if (s.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) || s.Contains("PSX", StringComparison.OrdinalIgnoreCase)) return "PlayStation";

            if (s.Contains("Game Boy Advance", StringComparison.OrdinalIgnoreCase)) return "Game Boy Advance";
            if (s.Contains("Gameboy Color", StringComparison.OrdinalIgnoreCase)) return "Game Boy Color";
            if (s.Contains("Gameboy", StringComparison.OrdinalIgnoreCase)) return "Game Boy";
            if (s.Contains("Nintendo DSi", StringComparison.OrdinalIgnoreCase)) return "Nintendo DS";
            if (s.Contains("Nintendo DS", StringComparison.OrdinalIgnoreCase)) return "Nintendo DS";
            if (s.Contains("Nintendo 3DS", StringComparison.OrdinalIgnoreCase)) return "Nintendo 3DS";
            if (s.Contains("GameCube", StringComparison.OrdinalIgnoreCase)) return "GameCube";
            if (s.Contains("Nintendo 64", StringComparison.OrdinalIgnoreCase)) return "Nintendo 64";
            if (s.Contains("Famicom Disk System", StringComparison.OrdinalIgnoreCase) || s.Contains("Entertainment System", StringComparison.OrdinalIgnoreCase) || s.Contains("NES", StringComparison.OrdinalIgnoreCase)) return "NES";
            if (s.Contains("Pokemon Mini", StringComparison.OrdinalIgnoreCase)) return "Pokemon Mini";

            if (s.Contains("Mega-CD", StringComparison.OrdinalIgnoreCase) || s.Contains("Sega CD", StringComparison.OrdinalIgnoreCase)) return "Sega CD";
            if (s.Contains("Saturn", StringComparison.OrdinalIgnoreCase)) return "Sega Saturn";
            if (s.Contains("Dreamcast", StringComparison.OrdinalIgnoreCase)) return "Dreamcast";
            if (s.Contains("Master System", StringComparison.OrdinalIgnoreCase)) return "Sega Master System";
            if (s.Contains("Genesis", StringComparison.OrdinalIgnoreCase) || s.Contains("Mega Drive", StringComparison.OrdinalIgnoreCase)) return "Sega Genesis";

            if (s.Contains("NeoGeo CD", StringComparison.OrdinalIgnoreCase) || s.Contains("Neo Geo CD", StringComparison.OrdinalIgnoreCase)) return "Neo Geo CD";
            if (s.Contains("NeoGeo", StringComparison.OrdinalIgnoreCase) || s.Contains("Neo Geo", StringComparison.OrdinalIgnoreCase) || s.Contains("Arcade", StringComparison.OrdinalIgnoreCase)) return "Neo Geo";

            if (s.Contains("PC Engine", StringComparison.OrdinalIgnoreCase) || s.Contains("TurboGrafx", StringComparison.OrdinalIgnoreCase) || s.Contains("SuperGrafx", StringComparison.OrdinalIgnoreCase)) return "PC Engine / TurboGrafx-16";
            if (s.Contains("PC-FX", StringComparison.OrdinalIgnoreCase)) return "PC-FX";
            if (s.Contains("PC-98", StringComparison.OrdinalIgnoreCase)) return "PC-98";
            if (s.Contains("PC-88", StringComparison.OrdinalIgnoreCase)) return "PC-8801";

            if (s.Contains("Atari - 5200", StringComparison.OrdinalIgnoreCase)) return "Atari 5200";
            if (s.Contains("Atari - 7800", StringComparison.OrdinalIgnoreCase)) return "Atari 7800";
            if (s.Contains("Atari - Lynx", StringComparison.OrdinalIgnoreCase) || s.Contains("Lynx", StringComparison.OrdinalIgnoreCase)) return "Atari Lynx";
            if (s.Contains("Atari", StringComparison.OrdinalIgnoreCase)) return "Atari 2600";

            if (s.Contains("ColecoVision", StringComparison.OrdinalIgnoreCase) || s.Contains("Coleco", StringComparison.OrdinalIgnoreCase)) return "ColecoVision";
            if (s.Contains("Amiga", StringComparison.OrdinalIgnoreCase)) return "Amiga";
            if (s.Contains("C128", StringComparison.OrdinalIgnoreCase) || s.Contains("C64", StringComparison.OrdinalIgnoreCase) || s.Contains("Commodore", StringComparison.OrdinalIgnoreCase)) return "Commodore 64";
            if (s.Contains("DOS", StringComparison.OrdinalIgnoreCase)) return "DOS";
            if (s.Contains("ScummVM", StringComparison.OrdinalIgnoreCase)) return "ScummVM";
            if (s.Contains("Odyssey", StringComparison.OrdinalIgnoreCase)) return "Magnavox Odyssey 2";
            if (s.Contains("Intellivision", StringComparison.OrdinalIgnoreCase)) return "Intellivision";
            if (s.Contains("MSX", StringComparison.OrdinalIgnoreCase)) return "MSX";
            if (s.Contains("3DO", StringComparison.OrdinalIgnoreCase)) return "3DO";

            var dashIdx = s.IndexOf('-');
            if (dashIdx >= 0 && dashIdx + 1 < s.Length)
            {
                return s[(dashIdx + 1)..].Trim();
            }

            return s;
        }
    }
}
