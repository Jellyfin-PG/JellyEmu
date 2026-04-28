using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace JellyEmu
{
    /// <summary>
    /// Resolves the console/platform tag for a ROM file using a three-step chain:
    ///
    ///   1. Inline platform token in the filename   — [Sega CD].chd  or  (PlayStation).iso
    ///   2. Parent / grandparent folder name        — /roms/Sega CD/Sonic.chd
    ///   3. File extension                          — .nes → "NES" (unambiguous only)
    ///                                                .chd → "Unknown" (ambiguous — needs folder/token)
    ///
    /// Region tags in the filename (e.g. (USA), (Europe), (Japan)) are also parsed and
    /// exposed via <see cref="ResolveRegion"/> so providers can surface them as metadata.
    ///
    /// IGDB and RAWG are intentionally excluded from platform resolution.
    /// Their platform lists are unreliable (multi-platform entries, inconsistent ordering).
    /// Those providers are used only for game metadata: name, description, artwork, genres.
    /// </summary>
    public class PlatformResolver
    {
        private static readonly Regex TokenRegex =
            new(@"[\[\(]([^\]\)]+)[\]\)]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Canonical region labels keyed by every common abbreviation / full form
        /// found in No-Intro, Redump, and GoodTools naming conventions.
        /// </summary>
        public static readonly Dictionary<string, string> RegionAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // North America / USA
                { "usa",        "USA" }, { "us",          "USA" }, { "ntsc-u",    "USA" },
                { "u",          "USA" }, { "america",      "USA" }, { "north america", "USA" },
                // Europe
                { "europe",     "Europe" }, { "eur",       "Europe" }, { "pal",     "Europe" },
                { "e",          "Europe" },
                // Japan
                { "japan",      "Japan" }, { "jpn",        "Japan" }, { "jp",      "Japan" },
                { "j",          "Japan" }, { "ntsc-j",     "Japan" },
                // World / Multi-region
                { "world",      "World" }, { "w",          "World" },
                // Other regions
                { "australia",  "Australia" }, { "aus",    "Australia" },
                { "brazil",     "Brazil" },    { "bra",    "Brazil" },
                { "canada",     "Canada" },    { "can",    "Canada" },
                { "china",      "China" },     { "chn",    "China" },
                { "france",     "France" },    { "fra",    "France" }, { "f", "France" },
                { "germany",    "Germany" },   { "ger",    "Germany" }, { "deu", "Germany" },
                { "italy",      "Italy" },     { "ita",    "Italy" },
                { "korea",      "Korea" },     { "kor",    "Korea" }, { "k", "Korea" },
                { "netherlands","Netherlands" },{ "ned",   "Netherlands" },
                { "russia",     "Russia" },    { "rus",    "Russia" },
                { "spain",      "Spain" },     { "spa",    "Spain" }, { "esp", "Spain" },
                { "sweden",     "Sweden" },    { "swe",    "Sweden" },
                { "asia",       "Asia" },
                { "scandinavia","Scandinavia" },
                // Unlicensed / special
                { "unlicensed", "Unlicensed" }, { "unl", "Unlicensed" },
                { "proto",      "Prototype" }, { "prototype", "Prototype" },
                { "demo",       "Demo" },
                { "sample",     "Sample" },
            };

        /// <summary>
        /// Attempts to extract the first recognisable region tag from a ROM filename.
        /// Returns <c>null</c> when no known region token is present.
        /// </summary>
        public static string? ResolveRegion(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            foreach (Match m in TokenRegex.Matches(fileName))
            {
                var inner = m.Groups[1].Value.Trim();
                // Region tokens can be comma-separated, e.g. (USA, Europe)
                foreach (var part in inner.Split(',', StringSplitOptions.TrimEntries))
                {
                    if (RegionAliases.TryGetValue(part, out var region))
                        return region;
                }
            }
            return null;
        }

        /// <summary>
        /// Regex that matches disc tokens in a variety of common ROM naming conventions:
        ///   (Disc 1), (Disc1), [Disc 2], (Disk 3), Disc1, disc2, etc.
        /// Captures the disc number (digit or Roman numeral up to VIII).
        /// </summary>
        private static readonly Regex DiscTokenRegex = new(
            @"(?:[\[\(]\s*dis[ck]\s*([1-9IVX]{1,4})\s*[\]\)]|(?<![a-zA-Z])dis[ck]\s*([1-9IVX]{1,4})(?![a-zA-Z0-9]))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Normalises a raw disc identifier (digit or Roman numeral) to "Disc N" label.
        /// Roman numerals I–VIII are converted to their Arabic equivalents.
        /// </summary>
        private static string? NormaliseDiscLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            // Arabic digit — straightforward
            if (int.TryParse(raw, out var n)) return $"Disc {n}";
            // Simple Roman numeral map (I–VIII covers virtually all multi-disc games)
            return raw.ToUpperInvariant() switch
            {
                "I" => "Disc 1",
                "II" => "Disc 2",
                "III" => "Disc 3",
                "IV" => "Disc 4",
                "V" => "Disc 5",
                "VI" => "Disc 6",
                "VII" => "Disc 7",
                "VIII" => "Disc 8",
                _ => $"Disc {raw.ToUpperInvariant()}"
            };
        }

        /// <summary>
        /// Attempts to extract a disc label (e.g. "Disc 1") from a ROM filename.
        /// Returns <c>null</c> when no disc token is present.
        /// Supports formats: (Disc 1), (Disc1), [Disk 2], Disc1, disc 3, (Disc II), etc.
        /// </summary>
        public static string? ResolveDisc(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var m = DiscTokenRegex.Match(fileName);
            if (!m.Success) return null;
            // Group 1 = bracketed form, Group 2 = bare form
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return NormaliseDiscLabel(raw.Trim());
        }

        public static readonly Dictionary<string, string> Aliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // NES
                { "nes", "NES" }, { "famicom", "NES" },
                { "nintendo entertainment system", "NES" },
                // SNES
                { "snes", "SNES" }, { "super nintendo", "SNES" }, { "super famicom", "SNES" },
                { "super nintendo entertainment system", "SNES" },
                // N64
                { "n64", "N64" }, { "nintendo 64", "N64" },
                // Game Boy / GBC — same core
                { "gb", "Game Boy" }, { "game boy", "Game Boy" }, { "gameboy", "Game Boy" },
                { "gbc", "Game Boy" }, { "game boy color", "Game Boy" }, { "gameboy color", "Game Boy" },
                // GBA
                { "gba", "Game Boy Advance" }, { "game boy advance", "Game Boy Advance" },
                { "gameboy advance", "Game Boy Advance" },
                // NDS
                { "nds", "Nintendo DS" }, { "nintendo ds", "Nintendo DS" }, { "ds", "Nintendo DS" },
                // Virtual Boy
                { "vb", "Virtual Boy" }, { "virtual boy", "Virtual Boy" },
                // Master System
                { "sms", "Master System" }, { "master system", "Master System" },
                { "sega master system", "Master System" },
                // Game Gear
                { "gg", "Game Gear" }, { "game gear", "Game Gear" }, { "sega game gear", "Game Gear" },
                // Genesis / Mega Drive
                { "genesis", "Sega Genesis" }, { "sega genesis", "Sega Genesis" },
                { "mega drive", "Sega Genesis" }, { "sega mega drive", "Sega Genesis" }, { "md", "Sega Genesis" },
                // Sega CD
                { "sega cd", "Sega CD" }, { "segacd", "Sega CD" }, { "mega cd", "Sega CD" },
                { "sega-cd", "Sega CD" },
                // Sega 32X
                { "32x", "Sega 32X" }, { "sega 32x", "Sega 32X" },
                // PlayStation
                { "psx", "PlayStation" }, { "ps1", "PlayStation" }, { "playstation", "PlayStation" },
                { "playstation 1", "PlayStation" }, { "ps one", "PlayStation" },
                // Atari
                { "atari 2600", "Atari 2600" }, { "2600", "Atari 2600" },
                { "atari 7800", "Atari 7800" }, { "7800", "Atari 7800" },
                { "lynx", "Atari Lynx" }, { "atari lynx", "Atari Lynx" },
                { "jaguar", "Atari Jaguar" }, { "atari jaguar", "Atari Jaguar" },
                // WonderSwan
                { "ws", "WonderSwan" }, { "wonderswan", "WonderSwan" }, { "wonder swan", "WonderSwan" },
                // TurboGrafx
                { "pce", "TurboGrafx-16" }, { "turbografx", "TurboGrafx-16" },
                { "turbografx-16", "TurboGrafx-16" }, { "turbografx 16", "TurboGrafx-16" },
                { "pc engine", "TurboGrafx-16" },
                // ColecoVision
                { "coleco", "ColecoVision" }, { "colecovision", "ColecoVision" },
                // NeoGeo Pocket
                { "ngp", "NeoGeo Pocket" }, { "neogeo pocket", "NeoGeo Pocket" },
                { "neo geo pocket", "NeoGeo Pocket" }, { "ngpc", "NeoGeo Pocket" },
                // DOS / PC
                { "dos", "DOS" }, { "ms-dos", "DOS" }, { "msdos", "DOS" }, { "pc dos", "DOS" },
                // Arcade / FBNeo / MAME
                { "arcade", "Arcade" }, { "fbneo", "Arcade" }, { "finalburn neo", "Arcade" },
                { "neogeo", "Arcade" }, { "neo geo", "Arcade" },
                { "mame", "MAME 2003" }, { "mame 2003", "MAME 2003" }, { "mame2003", "MAME 2003" },
                // PSP
                { "psp", "PSP" }, { "playstation portable", "PSP" },
                // Sega Saturn
                { "saturn", "Sega Saturn" }, { "sega saturn", "Sega Saturn" },
                // 3DO
                { "3do", "3DO" }, { "3do interactive multiplayer", "3DO" }, { "panasonic 3do", "3DO" },
                // Atari 5200
                { "atari 5200", "Atari 5200" }, { "5200", "Atari 5200" },
                // Commodore variants (all real EJS systems)
                { "amiga", "Commodore Amiga" }, { "commodore amiga", "Commodore Amiga" },
                { "amiga 500", "Commodore Amiga" }, { "amiga 1200", "Commodore Amiga" },
                { "c64", "Commodore 64" }, { "commodore 64", "Commodore 64" }, { "c-64", "Commodore 64" },
                { "c128", "Commodore 128" }, { "commodore 128", "Commodore 128" },
                { "pet", "Commodore PET" }, { "commodore pet", "Commodore PET" },
                { "plus4", "Commodore Plus/4" }, { "commodore plus4", "Commodore Plus/4" },
                { "vic20", "Commodore VIC-20" }, { "commodore vic-20", "Commodore VIC-20" },
                { "vic-20", "Commodore VIC-20" },
                // PC-FX
                { "pc-fx", "PC-FX" }, { "pcfx", "PC-FX" }, { "nec pc-fx", "PC-FX" },
            };

        /// <summary>
        /// A mapping of file extensions that unambiguously correspond to exactly one system.
        /// </summary>
        /// <remarks>
        /// Ambiguous formats (such as <c>.iso</c>, <c>.chd</c>, <c>.cue</c>, and <c>.pbp</c>) are intentionally omitted. 
        /// Encountering these formats will return "Unknown", indicating to the user that additional context 
        /// (like a token or folder name) is required for resolution.
        /// </remarks>
        private static readonly Dictionary<string, string> UnambiguousExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // NES
                { ".nes", "NES" }, { ".fds", "NES" }, { ".unf", "NES" }, { ".unif", "NES" },
                // SNES
                { ".smc", "SNES" }, { ".sfc", "SNES" }, { ".swc", "SNES" }, { ".fig", "SNES" },
                // N64
                { ".z64", "N64" }, { ".n64", "N64" }, { ".v64", "N64" },
                // Game Boy / GBC
                { ".gb",  "Game Boy" }, { ".gbc", "Game Boy" },
                // GBA
                { ".gba", "Game Boy Advance" },
                // NDS
                { ".nds", "Nintendo DS" },
                // Virtual Boy
                { ".vb",  "Virtual Boy" },
                // Master System
                { ".sms", "Master System" },
                // Game Gear
                { ".gg",  "Game Gear" },
                // Genesis — .md/.smd/.gen/.68k are genesis-only in practice
                { ".md",  "Sega Genesis" }, { ".smd", "Sega Genesis" },
                { ".gen", "Sega Genesis" }, { ".68k", "Sega Genesis" }, { ".sgd", "Sega Genesis" },
                // Sega 32X
                { ".32x", "Sega 32X" },
                // Atari
                { ".a26", "Atari 2600" },
                { ".a78", "Atari 7800" },
                { ".lnx", "Atari Lynx" },
                { ".jag", "Atari Jaguar" }, { ".j64", "Atari Jaguar" },
                // WonderSwan
                { ".ws",  "WonderSwan" }, { ".wsc", "WonderSwan" },
                // TurboGrafx-16
                { ".pce", "TurboGrafx-16" },
                // ColecoVision
                { ".col", "ColecoVision" }, { ".cv", "ColecoVision" },
                // NeoGeo Pocket
                { ".ngp", "NeoGeo Pocket" }, { ".ngc", "NeoGeo Pocket" },
                // PSP — .cso is a compressed ISO format exclusive to PSP
                { ".cso", "PSP" },

                // Intentionally absent (ambiguous — need folder or inline token):
                // .iso  — PlayStation, PSP, Sega CD, PC Engine CD, Sega Saturn, 3DO...
                // .chd  — same as above
                // .cue  — same as above
                // .pbp  — PlayStation, PSP
                // .zip  — DOS, Arcade, Amiga, C64, and others — always needs folder/token
                // .bin  — too generic (Sega CD, PlayStation, Odyssey, Amiga, etc.)
                // .dsk  — DOS / Amiga / Amstrad — always needs folder/token
            };

        /// <summary>
        /// Platforms that JellyEmu recognises and tags for library management,
        /// but which EmulatorJS does not support. ROMs on these platforms will
        /// appear in the library with a proper console tag but without a Play button.
        /// </summary>
        public static readonly Dictionary<string, string> LibraryOnlyAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Dreamcast
                { "dreamcast",          "Dreamcast" }, { "dc",             "Dreamcast" },
                { "sega dreamcast",     "Dreamcast" },
                // PlayStation 2
                { "ps2",                "PlayStation 2" }, { "playstation 2", "PlayStation 2" },
                { "playstation2",       "PlayStation 2" },
                // PlayStation 3
                { "ps3",                "PlayStation 3" }, { "playstation 3", "PlayStation 3" },
                { "playstation3",       "PlayStation 3" },
                // Xbox
                { "xbox",               "Xbox" }, { "microsoft xbox", "Xbox" },
                // Xbox 360
                { "xbox 360",           "Xbox 360" }, { "xbox360", "Xbox 360" },
                { "x360",               "Xbox 360" },
                // GameCube
                { "gamecube",           "GameCube" }, { "game cube", "GameCube" },
                { "nintendo gamecube",  "GameCube" }, { "gc", "GameCube" },
                // Wii
                { "wii",                "Wii" }, { "nintendo wii", "Wii" },
                // Wii U
                { "wii u",              "Wii U" }, { "wiiu", "Wii U" },
                { "nintendo wii u",     "Wii U" },
                // Nintendo Switch
                { "switch",             "Nintendo Switch" }, { "nintendo switch", "Nintendo Switch" },
                { "nx",                 "Nintendo Switch" },
                // Nintendo 3DS
                { "3ds",                "Nintendo 3DS" }, { "nintendo 3ds", "Nintendo 3DS" },
                { "new 3ds",            "Nintendo 3DS" },
                // PlayStation Portable — already in EJS, but PSVita is not
                { "psvita",             "PlayStation Vita" }, { "ps vita", "PlayStation Vita" },
                { "playstation vita",   "PlayStation Vita" },
            };

        /// <summary>
        /// File extensions that unambiguously map to a library-only (non-EJS) platform.
        /// </summary>
        private static readonly Dictionary<string, string> LibraryOnlyExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { ".nds", "Nintendo DS" },  // already in EJS but listed for completeness
                { ".3ds", "Nintendo 3DS" },
                { ".cci", "Nintendo 3DS" },
                { ".cia", "Nintendo 3DS" },
                { ".gcm", "GameCube"     },
                { ".gcz", "GameCube"     },
                { ".rvz", "Wii"          },
                { ".wbfs", "Wii"         },
                { ".wad", "Wii"          },
                { ".xex", "Xbox 360"     },
                { ".xiso", "Xbox"        },
                { ".vpk", "PlayStation Vita" },
            };

        /// <summary>
        /// Returns true when the resolved platform tag is supported by EmulatorJS.
        /// "Unknown" and all library-only platforms return false.
        /// </summary>
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
        /// Resolves the platform tag for a ROM at the given path.
        /// Returns "Unknown" if the platform cannot be determined confidently.
        /// </summary>
        public string Resolve(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";

            var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var dirPath = Path.GetDirectoryName(path) ?? string.Empty;

            var fromToken = MatchAliasFromTokens(fileName);
            if (fromToken != null)
            {
                _logger.LogDebug("[JellyEmu] Platform from inline token '{File}': {Tag}", fileName, fromToken);
                return fromToken;
            }

            var fromFolder = MatchAliasFromFolders(dirPath);
            if (fromFolder != null)
            {
                _logger.LogDebug("[JellyEmu] Platform from folder '{Dir}': {Tag}", dirPath, fromFolder);
                return fromFolder;
            }

            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && UnambiguousExtensions.TryGetValue(ext, out var extTag))
            {
                _logger.LogDebug("[JellyEmu] Platform from extension '{Ext}': {Tag}", ext, extTag);
                return extTag;
            }

            // Check library-only extensions (e.g. .3ds, .gcm, .wbfs) before giving up
            if (!string.IsNullOrEmpty(ext) && LibraryOnlyExtensions.TryGetValue(ext, out var libTag))
            {
                _logger.LogDebug("[JellyEmu] Library-only platform from extension '{Ext}': {Tag}", ext, libTag);
                return libTag;
            }

            _logger.LogDebug(
                "[JellyEmu] Could not resolve platform for '{File}' (ambiguous or unknown extension '{Ext}'). " +
                "Add a folder name or inline token e.g. [Sega CD].",
                fileName, ext);
            return "Unknown";
        }

        /// <summary>
        /// Strips JellyEmu-specific tokens from a raw filename so the display
        /// name is clean. Removes [Platform], (Platform), [igdb-NNN], [rawg-slug],
        /// and known region tokens such as (USA) or (Europe).
        /// Non-provider, non-platform, non-region bracket content (revision codes, [!]) is kept.
        /// e.g. "Sonic Adventure [igdb-3273][Sega CD](USA)" → "Sonic Adventure"
        ///      "Castlevania [!]"                            → "Castlevania [!]"
        /// </summary>
        public static string CleanDisplayName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            var result = TokenRegex.Replace(raw, m =>
            {
                var inner = m.Groups[1].Value.Trim();

                if (inner.StartsWith("igdb-", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("rawg-", StringComparison.OrdinalIgnoreCase))
                    return "";

                if (Aliases.ContainsKey(inner))
                    return "";

                // Strip region tokens (including comma-separated ones like "USA, Europe")
                var allParts = inner.Split(',', StringSplitOptions.TrimEntries);
                if (allParts.Length > 0 && allParts.All(p => RegionAliases.ContainsKey(p)))
                    return "";

                // Strip bracketed disc tokens e.g. (Disc 1), [Disk 2]
                if (DiscTokenRegex.IsMatch("(" + inner + ")"))
                    return "";

                return m.Value;
            });

            // Also strip bare disc tokens not wrapped in brackets e.g. "Game Disc1.iso"
            result = DiscTokenRegex.Replace(result, "");

            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private static string? MatchAliasFromTokens(string fileName)
        {
            foreach (Match m in TokenRegex.Matches(fileName))
            {
                var inner = m.Groups[1].Value.Trim();

                if (inner.StartsWith("igdb-", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("rawg-", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip pure region tokens so they don't pollute platform resolution
                var allParts = inner.Split(',', StringSplitOptions.TrimEntries);
                if (allParts.Length > 0 && allParts.All(p => RegionAliases.ContainsKey(p)))
                    continue;

                if (Aliases.TryGetValue(inner, out var tag))
                    return tag;

                if (LibraryOnlyAliases.TryGetValue(inner, out var libTag))
                    return libTag;
            }
            return null;
        }

        private static string? MatchAliasFromFolders(string dirPath)
        {
            var dir = new DirectoryInfo(dirPath);
            for (int depth = 0; depth < 2 && dir != null; depth++, dir = dir.Parent)
            {
                if (Aliases.TryGetValue(dir.Name, out var tag))
                    return tag;

                if (LibraryOnlyAliases.TryGetValue(dir.Name, out var libTag))
                    return libTag;
            }
            return null;
        }

        /// <summary>
        /// All known ROM extensions — both unambiguous and ambiguous.
        /// Used only to decide whether a file should be treated as a ROM at all.
        /// </summary>
        internal static readonly HashSet<string> AllRomExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Cartridge-based systems
                ".nes", ".fds", ".unf", ".unif",
                ".smc", ".sfc", ".swc", ".fig",
                ".z64", ".n64", ".v64",
                ".gb",  ".gbc", ".gba", ".nds", ".vb",
                ".sms", ".gg",
                ".md",  ".smd", ".gen", ".68k", ".sgd", ".32x",
                ".a26", ".a78", ".lnx", ".jag", ".j64",
                ".ws",  ".wsc", ".pce",
                ".col", ".cv", ".ngp", ".ngc",
                // Disc-based (ambiguous, need folder/token for platform)
                ".pbp", ".cue", ".iso", ".chd", ".gdi", ".cdi", ".mdf",
                // PSP compressed ISO — unambiguous PSP format
                ".cso",
                // DOS / Arcade / multi-file platforms — zip is the delivery format
                ".zip",
                // Commodore 64 disk/tape/cart formats
                ".d64", ".t64", ".crt", ".tap", ".prg",
                // Amiga floppy formats
                ".adf", ".dms", ".ipf", ".adz",
                // Generic disk image (DOS, Amiga, MSX etc.)
                ".dsk",
                // Catch-all binary ROM (many obscure carts ship as .bin)
                ".bin",
                // Library-only platforms (recognised but not emulated by EJS)
                ".3ds", ".cci", ".cia",         // Nintendo 3DS
                ".gcm", ".gcz", ".rvz",         // GameCube / Wii
                ".wbfs", ".wad",                // Wii
                ".xex", ".xiso",                // Xbox / Xbox 360
                ".vpk",                         // PlayStation Vita
            };
    }
    /// <summary>
    /// Minimal CUE sheet parser. A .cue file is plain text - no external dependency needed.
    /// </summary>
    public static class CueParser
    {
        /// <summary>
        /// Returns all FILE entries referenced by a CUE sheet as absolute paths,
        /// resolved relative to the directory containing the .cue file.
        /// </summary>
        public static List<string> GetReferencedFiles(string cuePath)
        {
            var result = new List<string>();
            try
            {
                var dir = Path.GetDirectoryName(cuePath) ?? string.Empty;
                foreach (var line in File.ReadLines(cuePath))
                {
                    var trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                        continue;

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

                    var candidate = Path.IsPathRooted(fileName)
                        ? fileName
                        : Path.Combine(dir, fileName);
                    result.Add(candidate);
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Returns true if <paramref name="binPath"/> is referenced by any sibling .cue file.
        /// </summary>
        public static bool IsReferencedByAnyCue(string binPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(binPath) ?? string.Empty;
                var binName = Path.GetFileName(binPath);
                foreach (var cueFile in Directory.GetFiles(dir, "*.cue"))
                    foreach (var referenced in GetReferencedFiles(cueFile))
                        if (string.Equals(Path.GetFileName(referenced), binName,
                                StringComparison.OrdinalIgnoreCase))
                            return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Returns the first existing binary track file referenced by the .cue, or null.
        /// </summary>
        public static string? GetFirstBinPath(string cuePath)
        {
            foreach (var path in GetReferencedFiles(cuePath))
                if (File.Exists(path)) return path;
            return null;
        }

        /// <summary>
        /// Returns true if the .cue has at least one referenced FILE that exists on disk.
        /// </summary>
        public static bool HasResolvedBin(string cuePath) => GetFirstBinPath(cuePath) != null;
    }

}