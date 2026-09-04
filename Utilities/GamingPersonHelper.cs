using System;
using System.Text;
using Jellyfin.Data.Enums;

namespace JellyEmu.Utilities
{
    /// <summary>
    /// Helper for formatting and disambiguating video game creators in Jellyfin.
    /// Uses the disambiguation suffix " (Gaming)" to guarantee that Jellyfin's database
    /// stores gaming creators as separate entities from movie/IMDb actors, preventing
    /// unwanted merging and blocking movie metadata scrapers (TMDB) from overwriting them.
    /// </summary>
    public static class GamingPersonHelper
    {
        /// <summary>
        /// Suffix appended to gaming person names in Jellyfin's database to prevent merging with movie actors.
        /// </summary>
        public const string GamingSuffix = " (Gaming)";

        /// <summary>
        /// Cleans a person name by stripping "(Gaming)" suffixes, legacy "(RAWG)" tags, and whitespace.
        /// Returns a clean human-readable name suitable for search queries and API calls.
        /// </summary>
        public static string CleanPersonName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var cleaned = name.Trim();
            while (cleaned.EndsWith("(RAWG)", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 6).Trim();
            }
            while (cleaned.EndsWith("(Gaming)", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 8).Trim();
            }

            return cleaned;
        }

        /// <summary>
        /// Determines whether a person name corresponds to a video game creator (i.e. contains the gaming disambiguation suffix).
        /// </summary>
        public static bool IsGamingPerson(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.TrimEnd().EndsWith(GamingSuffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Formats a gaming creator name by appending the "(Gaming)" suffix.
        /// This ensures Jellyfin's database stores a distinct, non-merging person record
        /// that TMDB/IMDb scrapers will not match or overwrite.
        /// </summary>
        public static string ToGamingPersonName(string? name)
        {
            var clean = CleanPersonName(name);
            if (string.IsNullOrWhiteSpace(clean)) return string.Empty;
            return clean + GamingSuffix;
        }

        /// <summary>
        /// Formats a role string so that each word begins with a capital letter (e.g. "designer" -> "Designer").
        /// </summary>
        public static string FormatRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return "Developer";

            var trimmed = role.Trim();
            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (w.Length > 0 && char.IsLower(w[0]))
                {
                    words[i] = char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : "");
                }
            }
            return string.Join(" ", words);
        }

        /// <summary>
        /// Maps a freeform gaming role or position string to a valid standard Jellyfin PersonKind enum.
        /// Guaranteed not to crash Android or strict enum clients.
        /// </summary>
        public static PersonKind MapPersonKind(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return PersonKind.Creator;
            var r = role.Trim().ToLowerInvariant();

            if (r.Contains("director")) return PersonKind.Director;
            if (r.Contains("producer")) return PersonKind.Producer;
            if (r.Contains("sound") || r.Contains("audio") || r.Contains("sfx")) return PersonKind.Engineer;
            if (r.Contains("music") || r.Contains("composer")) return PersonKind.Composer;
            if (r.Contains("design")) return PersonKind.Creator;
            if (r.Contains("program") || r.Contains("coder") || r.Contains("engine")) return PersonKind.Creator;
            if (r.Contains("art") || r.Contains("graphics") || r.Contains("animat")) return PersonKind.Artist;
            if (r.Contains("writing") || r.Contains("writer") || r.Contains("story") || r.Contains("scenario")) return PersonKind.Writer;
            if (r.Contains("voice") || r.Contains("actor") || r.Contains("cast")) return PersonKind.Actor;
            if (r.Contains("editor")) return PersonKind.Editor;
            if (r.Contains("illustrat")) return PersonKind.Illustrator;
            if (r.Contains("translat") || r.Contains("localiz")) return PersonKind.Translator;
            if (r.Contains("author")) return PersonKind.Author;

            return PersonKind.Creator;
        }
    }
}
