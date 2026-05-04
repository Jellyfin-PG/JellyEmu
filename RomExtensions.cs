using System.Text.RegularExpressions;

namespace JellyEmu
{
    internal static class RomExtensions
    {
        public static bool IsRomPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase)) return true;
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext)) return PlatformResolver.AllRomExtensions.Contains(ext);
            if (Directory.Exists(path))
            {
                try
                {
                    var cues = Directory.GetFiles(path, "*.cue");
                    return cues.Length == 1 && CueParser.HasResolvedBin(cues[0]);
                }
                catch { }
            }
            return false;
        }

        public static string EffectiveRomPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (Directory.Exists(path))
            {
                try
                {
                    var cues = Directory.GetFiles(path, "*.cue");
                    if (cues.Length == 1 && CueParser.HasResolvedBin(cues[0]))
                        return cues[0];
                }
                catch { }
            }
            return path;
        }

        public static string CleanName(string name)
        {
            var stripped = PlatformResolver.CleanDisplayName(name ?? string.Empty);
            var cleaned = Regex.Replace(stripped.Replace("_", " ").Replace("-", " "), @"\s+", " ").Trim();
            return cleaned;
        }

        /// <summary>
        /// Normalizes accented/special characters for fuzzy matching.
        /// e.g. "Pokémon" -> "Pokemon", "Résumé" -> "Resume"
        /// </summary>
        public static string NormalizeForSearch(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var normalized = name.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }
    }
}