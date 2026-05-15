using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace JellyEmu
{
    public class PlatformResolver
    {
        private static readonly Regex TokenRegex =
            new(@"[\[\(]([^\]\)]+)[\]\)]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Dictionary<string, string> RegionAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "usa", "USA" }, { "us", "USA" }, { "ntsc-u", "USA" }, { "u", "USA" }, { "america", "USA" }, { "north america", "USA" },
                { "europe", "Europe" }, { "eur", "Europe" }, { "pal", "Europe" }, { "e", "Europe" }, { "eu", "Europe" }, { "uk", "Europe" },
                { "japan", "Japan" }, { "jpn", "Japan" }, { "jp", "Japan" }, { "j", "Japan" }, { "ntsc-j", "Japan" },
                { "world", "World" }, { "w", "World" },
                { "australia", "Australia" }, { "aus", "Australia" }, { "brazil", "Brazil" }, { "bra", "Brazil" }, { "br", "Brazil" },
                { "canada", "Canada" }, { "can", "Canada" }, { "china", "China" }, { "chn", "China" }, { "cn", "China" },
                { "france", "France" }, { "fra", "France" }, { "f", "France" }, { "fr", "France" }, { "germany", "Germany" },
                { "ger", "Germany" }, { "deu", "Germany" }, { "de", "Germany" }, { "italy", "Italy" }, { "ita", "Italy" }, { "it", "Italy" },
                { "korea", "Korea" }, { "kor", "Korea" }, { "k", "Korea" }, { "kr", "Korea" }, { "netherlands", "Netherlands" },
                { "ned", "Netherlands" }, { "nl", "Netherlands" }, { "russia", "Russia" }, { "rus", "Russia" }, { "ru", "Russia" }, { "spain", "Spain" },
                { "spa", "Spain" }, { "esp", "Spain" }, { "es", "Spain" }, { "sweden", "Sweden" }, { "swe", "Sweden" },
                { "asia", "Asia" }, { "scandinavia", "Scandinavia" },
                { "unlicensed", "Unlicensed" }, { "unl", "Unlicensed" }, { "proto", "Prototype" },
                { "prototype", "Prototype" }, { "demo", "Demo" }, { "sample", "Sample" },
            };

        public static IEnumerable<string> ResolveRegions(string? path)
        {
            var regions = new List<string>();
            if (string.IsNullOrEmpty(path)) return regions;
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            foreach (Match m in TokenRegex.Matches(fileName))
            {
                var inner = m.Groups[1].Value.Trim();
                foreach (var part in inner.Split(',', StringSplitOptions.TrimEntries))
                {
                    if (RegionAliases.TryGetValue(part, out var region))
                    {
                        if (!regions.Contains(region)) regions.Add(region);
                    }
                }
            }
            return regions;
        }

        [Obsolete("Use ResolveRegions instead")]
        public static string? ResolveRegion(string? path) => ResolveRegions(path).FirstOrDefault();

        private static readonly Regex DiscTokenRegex = new(
            @"(?:[\[\(]\s*dis[ck]\s*([1-9IVX]{1,4})\s*[\]\)]|(?<![a-zA-Z])dis[ck]\s*([1-9IVX]{1,4})(?![a-zA-Z0-9]))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string? NormaliseDiscLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (int.TryParse(raw, out var n)) return $"Disc {n}";
            return raw.ToUpperInvariant() switch
            {
                "I" => "Disc 1", "II" => "Disc 2", "III" => "Disc 3", "IV" => "Disc 4",
                "V" => "Disc 5", "VI" => "Disc 6", "VII" => "Disc 7", "VIII" => "Disc 8",
                _ => $"Disc {raw.ToUpperInvariant()}"
            };
        }

        public static string? ResolveDisc(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var m = DiscTokenRegex.Match(fileName);
            if (!m.Success) return null;
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return NormaliseDiscLabel(raw.Trim());
        }

        public static readonly Dictionary<string, string> Aliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // NES
                { "nes", "NES" }, { "famicom", "NES" }, { "nintendo", "NES" }, { "nintendo entertainment system", "NES" },
                // SNES
                { "snes", "SNES" }, { "super nintendo", "SNES" }, { "super famicom", "SNES" }, { "supernintendo", "SNES" }, { "super nintendo entertainment system", "SNES" },
                // N64
                { "n64", "N64" }, { "nintendo 64", "N64" }, { "nintendo64", "N64" },
                // Game Boy / GBC
                { "gb", "Game Boy" }, { "game boy", "Game Boy" }, { "gameboy", "Game Boy" },
                { "gbc", "Game Boy Color" }, { "game boy color", "Game Boy Color" }, { "gameboy color", "Game Boy Color" }, { "gameboycolor", "Game Boy Color" },
                // GBA
                { "gba", "Game Boy Advance" }, { "game boy advance", "Game Boy Advance" }, { "gameboy advance", "Game Boy Advance" }, { "gameboyadvance", "Game Boy Advance" },
                // NDS
                { "nds", "Nintendo DS" }, { "nintendo ds", "Nintendo DS" }, { "ds", "Nintendo DS" }, { "nintendods", "Nintendo DS" },
                // Virtual Boy
                { "vb", "Virtual Boy" }, { "virtual boy", "Virtual Boy" }, { "virtualboy", "Virtual Boy" },
                // Master System
                { "sms", "Master System" }, { "master system", "Master System" }, { "sega master system", "Master System" }, { "mastersystem", "Master System" },
                // Game Gear
                { "gg", "Game Gear" }, { "game gear", "Game Gear" }, { "sega game gear", "Game Gear" }, { "gamegear", "Game Gear" },
                // Genesis / Mega Drive
                { "genesis", "Sega Genesis" }, { "sega genesis", "Sega Genesis" }, { "megadrive", "Sega Genesis" }, { "mega drive", "Sega Genesis" }, { "sega mega drive", "Sega Genesis" }, { "md", "Sega Genesis" },
                // Sega CD
                { "sega cd", "Sega CD" }, { "segacd", "Sega CD" }, { "mega cd", "Sega CD" }, { "sega-cd", "Sega CD" },
                // Sega 32X
                { "32x", "Sega 32X" }, { "sega 32x", "Sega 32X" },
                // Sega Saturn
                { "ss", "Sega Saturn"}, { "saturn", "Sega Saturn" }, { "sega saturn", "Sega Saturn" }, { "segasaturn", "Sega Saturn" },
                // PlayStation
                { "psx", "PlayStation" }, { "ps1", "PlayStation" }, { "playstation", "PlayStation" }, { "playstation 1", "PlayStation" }, { "ps one", "PlayStation" }, { "playstation1", "PlayStation" },
                // Atari
                { "atari 2600", "Atari 2600" }, { "2600", "Atari 2600" }, { "atari 7800", "Atari 7800" }, { "7800", "Atari 7800" },
                { "lynx", "Atari Lynx" }, { "atari lynx", "Atari Lynx" }, { "jaguar", "Atari Jaguar" }, { "atari jaguar", "Atari Jaguar" },
                // WonderSwan
                { "ws", "WonderSwan" }, { "wonderswan", "WonderSwan" }, { "wonder swan", "WonderSwan" },
                // TurboGrafx
                { "pce", "TurboGrafx-16" }, { "turbografx", "TurboGrafx-16" }, { "turbografx-16", "TurboGrafx-16" }, { "turbografx 16", "TurboGrafx-16" }, { "pc engine", "TurboGrafx-16" }, { "pcengine", "TurboGrafx-16" },
                // ColecoVision
                { "coleco", "ColecoVision" }, { "colecovision", "ColecoVision" },
                // NeoGeo Pocket
                { "ngp", "NeoGeo Pocket" }, { "neogeo pocket", "NeoGeo Pocket" }, { "neo geo pocket", "NeoGeo Pocket" }, { "ngpc", "NeoGeo Pocket" },
                // DOS / PC
                { "dos", "DOS" }, { "ms-dos", "DOS" }, { "msdos", "DOS" }, { "pc dos", "DOS" },
                // Arcade
                { "arcade", "Arcade" }, { "fbneo", "Arcade" }, { "finalburn neo", "Arcade" }, { "neogeo", "Arcade" }, { "neo geo", "Arcade" },
                { "mame", "MAME 2003" }, { "mame 2003", "MAME 2003" }, { "mame2003", "MAME 2003" },
                // PSP
                { "psp", "PSP" }, { "playstation portable", "PSP" },
                // 3DO
                { "3do", "3DO" }, { "3do interactive multiplayer", "3DO" }, { "panasonic 3do", "3DO" },
                // Atari 5200
                { "atari 5200", "Atari 5200" }, { "5200", "Atari 5200" },
                // Commodore
                { "amiga", "Commodore Amiga" }, { "commodore amiga", "Commodore Amiga" },
                { "c64", "Commodore 64" }, { "commodore 64", "Commodore 64" },
                // PC-FX
                { "pc-fx", "PC-FX" }, { "pcfx", "PC-FX" }, { "nec pc-fx", "PC-FX" },
                // Pico 8
                { "pico-8", "PICO-8" }, { "pico8", "PICO-8" }, { "pico 8", "PICO-8" }, { "pico", "PICO-8" },
            };

        private static readonly Dictionary<string, string> UnambiguousExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { ".nes", "NES" }, { ".fds", "NES" }, { ".unf", "NES" }, { ".unif", "NES" },
                { ".smc", "SNES" }, { ".sfc", "SNES" }, { ".swc", "SNES" }, { ".fig", "SNES" },
                { ".z64", "N64" }, { ".n64", "N64" }, { ".v64", "N64" },
                { ".gb",  "Game Boy" }, { ".gbc", "Game Boy Color" }, { ".gba", "Game Boy Advance" },
                { ".nds", "Nintendo DS" }, { ".vb",  "Virtual Boy" }, { ".sms", "Master System" }, { ".gg",  "Game Gear" },
                { ".md",  "Sega Genesis" }, { ".smd", "Sega Genesis" }, { ".gen", "Sega Genesis" }, { ".68k", "Sega Genesis" }, { ".sgd", "Sega Genesis" },
                { ".32x", "Sega 32X" }, { ".a26", "Atari 2600" }, { ".a78", "Atari 7800" }, { ".lnx", "Atari Lynx" },
                { ".jag", "Atari Jaguar" }, { ".j64", "Atari Jaguar" }, { ".ws",  "WonderSwan" }, { ".wsc", "WonderSwan" },
                { ".pce", "TurboGrafx-16" }, { ".col", "ColecoVision" }, { ".cv", "ColecoVision" },
                { ".ngp", "NeoGeo Pocket" }, { ".ngc", "NeoGeo Pocket" }, { ".cso", "PSP" }, { ".p8", "PICO-8" },
            };

        public static readonly Dictionary<string, string> LibraryOnlyAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "dreamcast", "Dreamcast" }, { "dc", "Dreamcast" }, { "sega dreamcast", "Dreamcast" },
                { "ps2", "PlayStation 2" }, { "playstation 2", "PlayStation 2" },
                { "ps3", "PlayStation 3" }, { "playstation 3", "PlayStation 3" },
                { "xbox", "Xbox" }, { "xbox 360", "Xbox 360" }, { "x360", "Xbox 360" },
                { "gamecube", "GameCube" }, { "nintendo gamecube", "GameCube" }, { "gc", "GameCube" },
                { "wii", "Wii" }, { "nintendo wii", "Wii" }, { "wii u", "Wii U" }, { "wiiu", "Wii U" },
                { "switch", "Nintendo Switch" }, { "nintendo switch", "Nintendo Switch" },
                { "3ds", "Nintendo 3DS" }, { "nintendo 3ds", "Nintendo 3DS" },
                { "psvita", "PlayStation Vita" }, { "ps vita", "PlayStation Vita" },
            };

        private static readonly Dictionary<string, string> LibraryOnlyExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { ".3ds", "Nintendo 3DS" }, { ".cci", "Nintendo 3DS" }, { ".cia", "Nintendo 3DS" },
                { ".gcm", "GameCube" }, { ".gcz", "GameCube" }, { ".rvz", "Wii" },
                { ".wbfs", "Wii" }, { ".wad", "Wii" }, { ".xex", "Xbox 360" },
                { ".xiso", "Xbox" }, { ".vpk", "PlayStation Vita" },
            };

        public static bool IsEjsSupported(string? tag)
        {
            if (string.IsNullOrEmpty(tag) || tag == "Unknown") return false;
            return !LibraryOnlyAliases.Values.Contains(tag, StringComparer.OrdinalIgnoreCase);
        }

        private readonly ILogger<PlatformResolver> _logger;

        public PlatformResolver(ILogger<PlatformResolver> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Global platform resolution engine. Checks brackets, folders, and extensions in sequence.
        /// </summary>
        public string Resolve(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";
            return ResolvePlatform(path, Path.GetFileName(path));
        }

        /// <summary>
        /// Advanced resolution that can analyze both a path and a standalone name (for manual search).
        /// </summary>
        public string ResolvePlatform(string? path, string? name)
        {
            // 1. Check brackets/parentheses in the Name or Filename
            var nameToTarget = name ?? (path != null ? Path.GetFileName(path) : null);
            if (!string.IsNullOrEmpty(nameToTarget))
            {
                var fromToken = MatchAliasFromTokens(nameToTarget);
                if (fromToken != null) return fromToken;
            }

            // 2. Check Folders (if path is provided)
            if (!string.IsNullOrEmpty(path))
            {
                var dirPath = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dirPath))
                {
                    var fromFolder = MatchAliasFromFolders(dirPath);
                    if (fromFolder != null) return fromFolder;
                }
            }

            // 3. Check Extension (if path is provided)
            if (!string.IsNullOrEmpty(path))
            {
                var ext = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext))
                {
                    if (UnambiguousExtensions.TryGetValue(ext, out var extTag)) return extTag;
                    if (LibraryOnlyExtensions.TryGetValue(ext, out var libTag)) return libTag;
                }
            }

            return "Unknown";
        }

        public static string CleanDisplayName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var result = TokenRegex.Replace(raw, m =>
            {
                var inner = m.Groups[1].Value.Trim();
                if (inner.StartsWith("igdb-", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("rawg-", StringComparison.OrdinalIgnoreCase)) return "";
                if (Aliases.ContainsKey(inner)) return "";
                var allParts = inner.Split(',', StringSplitOptions.TrimEntries);
                if (allParts.Length > 0 && allParts.All(p => RegionAliases.ContainsKey(p))) return "";
                if (DiscTokenRegex.IsMatch("(" + inner + ")")) return "";
                return m.Value;
            });
            result = DiscTokenRegex.Replace(result, "");
            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private static string? MatchAliasFromTokens(string fileName)
        {
            foreach (Match m in TokenRegex.Matches(fileName))
            {
                var inner = m.Groups[1].Value.Trim();
                if (inner.StartsWith("igdb-", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("rawg-", StringComparison.OrdinalIgnoreCase)) continue;

                // Decompose comma-separated tokens like (USA, GBC)
                var parts = inner.Split(',', StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    if (RegionAliases.ContainsKey(part)) continue;
                    if (Aliases.TryGetValue(part, out var tag)) return tag;
                    if (LibraryOnlyAliases.TryGetValue(part, out var libTag)) return libTag;
                }
            }
            return null;
        }

        private static string? MatchAliasFromFolders(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath)) return null;
            var dir = new DirectoryInfo(dirPath);

            for (int depth = 0; depth < 3 && dir != null; depth++, dir = dir.Parent)
            {
                var folderName = dir.Name;

                if (Aliases.TryGetValue(folderName, out var tag)) return tag;
                if (LibraryOnlyAliases.TryGetValue(folderName, out var libTag)) return libTag;

                var folderCollapsed = Regex.Replace(folderName, @"\s+", "", RegexOptions.None);

                static string CollapseSpaces(string s) => Regex.Replace(s, @"\s+", "");

                bool KeywordMatch(string folder, string folderCol, string aliasKey)
                {
                    var idx = folder.IndexOf(aliasKey, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var before = idx > 0 ? folder[idx - 1] : ' ';
                        var after  = idx + aliasKey.Length < folder.Length ? folder[idx + aliasKey.Length] : ' ';
                        if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after)) return true;
                    }

                    var aliasCol = CollapseSpaces(aliasKey);
                    if (aliasCol.Length <= 2) return false;
                    var idxCol = folderCol.IndexOf(aliasCol, StringComparison.OrdinalIgnoreCase);
                    if (idxCol >= 0)
                    {
                        var before = idxCol > 0 ? folderCol[idxCol - 1] : ' ';
                        var after  = idxCol + aliasCol.Length < folderCol.Length ? folderCol[idxCol + aliasCol.Length] : ' ';
                        if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after)) return true;
                    }

                    return false;
                }

                foreach (var kvp in Aliases.OrderByDescending(k => k.Key.Length))
                {
                    if (kvp.Key.Length <= 2) continue;
                    if (KeywordMatch(folderName, folderCollapsed, kvp.Key)) return kvp.Value;
                }

                foreach (var kvp in LibraryOnlyAliases.OrderByDescending(k => k.Key.Length))
                {
                    if (kvp.Key.Length <= 2) continue;
                    if (KeywordMatch(folderName, folderCollapsed, kvp.Key)) return kvp.Value;
                }
            }
            return null;
        }

        internal static readonly HashSet<string> AllRomExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".nes", ".fds", ".unf", ".unif", ".smc", ".sfc", ".swc", ".fig", ".z64", ".n64", ".v64",
                ".gb",  ".gbc", ".gba", ".nds", ".vb", ".sms", ".gg", ".md",  ".smd", ".gen", ".68k",
                ".sgd", ".32x", ".a26", ".a78", ".lnx", ".jag", ".j64", ".ws",  ".wsc", ".pce",
                ".col", ".cv", ".ngp", ".ngc", ".pbp", ".cue", ".iso", ".chd", ".gdi", ".cdi", ".mdf",
                ".cso", ".zip", ".7z", ".d64", ".t64", ".crt", ".tap", ".prg", ".adf", ".dms", ".ipf",
                ".adz", ".dsk", ".bin", ".3ds", ".cci", ".cia", ".gcm", ".gcz", ".rvz", ".wbfs", ".wad",
                ".xex", ".xiso", ".vpk",
            };
    }

    public static class CueParser
    {
        public static List<string> GetReferencedFiles(string cuePath)
        {
            var result = new List<string>();
            try
            {
                var dir = Path.GetDirectoryName(cuePath) ?? string.Empty;
                foreach (var line in File.ReadLines(cuePath))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase)) continue;
                    var rest = trimmed.Substring(4).TrimStart();
                    string fileName;
                    if (rest.StartsWith("\""))
                    {
                        var closing = rest.IndexOf('"', 1);
                        if (closing < 0) continue;
                        fileName = rest.Substring(1, closing - 1);
                    }
                    else
                    {
                        var space = rest.IndexOfAny(new[] { ' ', '\t' });
                        fileName = space >= 0 ? rest.Substring(0, space) : rest;
                    }
                    if (string.IsNullOrWhiteSpace(fileName)) continue;
                    var candidate = Path.IsPathRooted(fileName) ? fileName : Path.Combine(dir, fileName);
                    result.Add(candidate);
                }
            }
            catch { }
            return result;
        }

        public static bool IsReferencedByAnyCue(string binPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(binPath) ?? string.Empty;
                var binName = Path.GetFileName(binPath);
                foreach (var cueFile in Directory.GetFiles(dir, "*.cue"))
                    foreach (var referenced in GetReferencedFiles(cueFile))
                        if (string.Equals(Path.GetFileName(referenced), binName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        public static string? GetFirstBinPath(string cuePath)
        {
            foreach (var path in GetReferencedFiles(cuePath))
                if (File.Exists(path)) return path;
            return null;
        }

        public static bool HasResolvedBin(string cuePath) => GetFirstBinPath(cuePath) != null;
    }
}