using System;
using System.Collections.Generic;

namespace JellyEmu.Utilities
{
    /// <summary>
    /// Maps JellyEmu canonical platform names to ScreenScraper.fr numeric system IDs (systemeid).
    /// </summary>
    public static class ScreenScraperSystemMap
    {
        private static readonly Dictionary<string, int> PlatformToSystemId = new(StringComparer.OrdinalIgnoreCase)
        {
            // Sega
            { "Sega Genesis", 1 },
            { "Mega Drive", 1 },
            { "Master System", 2 },
            { "Sega 32X", 19 },
            { "Sega CD", 20 },
            { "Game Gear", 21 },
            { "Sega Saturn", 22 },
            { "Dreamcast", 23 },

            // Nintendo Home
            { "NES", 3 },
            { "Famicom", 3 },
            { "SNES", 4 },
            { "Super Famicom", 4 },
            { "N64", 5 },
            { "Nintendo 64", 5 },
            { "GameCube", 13 },
            { "Wii", 14 },
            { "Wii U", 18 },
            { "Nintendo Switch", 225 },

            // Nintendo Handheld
            { "Game Boy", 9 },
            { "Game Boy Color", 10 },
            { "Virtual Boy", 11 },
            { "Game Boy Advance", 12 },
            { "Nintendo DS", 15 },
            { "Nintendo 3DS", 17 },

            // Sony
            { "PlayStation", 57 },
            { "PS1", 57 },
            { "PlayStation 2", 58 },
            { "PS2", 58 },
            { "PlayStation 3", 59 },
            { "PSP", 61 },
            { "PlayStation Vita", 62 },

            // Atari
            { "Atari 2600", 26 },
            { "Atari 5200", 40 },
            { "Atari 7800", 27 },
            { "Atari Lynx", 28 },
            { "Atari Jaguar", 29 },

            // NEC
            { "TurboGrafx-16", 31 },
            { "PC Engine", 31 },
            { "PC Engine CD", 32 },
            { "SuperGrafx", 105 },
            { "PC-FX", 72 },

            // SNK
            { "Neo Geo", 142 },
            { "NeoGeo Pocket", 25 },
            { "NeoGeo Pocket Color", 82 },

            // Bandai
            { "WonderSwan", 45 },
            { "WonderSwan Color", 46 },

            // Coleco
            { "ColecoVision", 48 },

            // Commodore
            { "Commodore 64", 66 },
            { "Commodore Amiga", 64 },

            // Other
            { "Arcade", 75 },
            { "MAME 2003", 75 },
            { "DOS", 135 },
            { "3DO", 111 }
        };

        /// <summary>
        /// Attempts to get the ScreenScraper system ID for a platform name or alias.
        /// </summary>
        public static int? GetSystemId(string? platform)
        {
            if (string.IsNullOrWhiteSpace(platform)) return null;

            if (PlatformToSystemId.TryGetValue(platform.Trim(), out var systemId))
            {
                return systemId;
            }

            return null;
        }
    }
}
