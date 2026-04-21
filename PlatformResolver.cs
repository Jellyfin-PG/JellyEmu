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

                // Intentionally absent (ambiguous — need folder or inline token):
                // .iso  — PlayStation, Sega CD, PC Engine CD, Dreamcast, Saturn...
                // .chd  — same as above
                // .cue  — same as above
                // .pbp  — PlayStation, PSP
            };

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

                return m.Value;
            });

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
                ".nes", ".fds", ".unf", ".unif",
                ".smc", ".sfc", ".swc", ".fig",
                ".z64", ".n64", ".v64",
                ".gb",  ".gbc", ".gba", ".nds", ".vb",
                ".sms", ".gg",
                ".md",  ".smd", ".gen", ".68k", ".sgd", ".32x",
                ".pbp", ".cue", ".iso", ".chd",
                ".a26", ".a78", ".lnx", ".jag", ".j64",
                ".ws",  ".wsc", ".pce",
                ".col", ".cv", ".ngp", ".ngc",
            };
    }
}
