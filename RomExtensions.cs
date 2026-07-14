using System.IO.Compression;
using JellyEmu.Utilities;
using MediaBrowser.Controller.Entities;
using System.Security.Cryptography;

namespace JellyEmu
{
    internal static class RomExtensions
    {
        /// <summary>
        /// For sidecar lookups, returns the "logical" base path.
        /// .p8.png  → strips both extensions  → "Game"
        /// .p8      → strips extension         → "Game"
        /// .zip     → strips extension         → "Game"
        /// </summary>
        public static string EffectivePicoPath(string path)
        {
            if (path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
                return path[..^7];
            return Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty,
                Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>
        /// Returns true for any PICO-8 cart path (.p8, .p8.png, or a .zip containing a .p8 or .p8.png entry).
        /// Used by JellyEmuLexaloffleProvider; excluded from all ROM providers.
        /// </summary>
        public static bool IsPico8Path(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase)) return true;
            if (Path.GetExtension(path).Equals(".p8", StringComparison.OrdinalIgnoreCase)) return true;
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(path);
                    return zip.Entries.Any(e =>
                        e.FullName.EndsWith(".p8", StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }
            return false;
        }

        public static bool IsWindowsRom(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return new PlatformResolver(null!).Resolve(EffectiveRomPath(path)) == "Windows";
        }

        public static bool IsRomPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext))
            {
                if (string.Equals(ext, ".cue", StringComparison.OrdinalIgnoreCase))
                {
                    return CueParser.HasResolvedBin(path);
                }
                return PlatformResolver.AllRomExtensions.Contains(ext);
            }
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
            if (string.Equals(Path.GetExtension(path), ".j3u", StringComparison.OrdinalIgnoreCase))
            {
                var refs = J3uParser.GetReferencedFiles(path);
                if (refs.Count > 0)
                    return refs[0];
            }
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

        public static string GetPico8Extension(BaseItem item)
        {
            var ext = !string.IsNullOrEmpty(item.Path) ? Path.GetExtension(item.Path) : ".p8.png";

            if (!string.IsNullOrEmpty(item.Path) &&
                item.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
                ext = ".p8.png";
            return ext;
        }

        public static string CleanName(string name)
        {
            return NameCleaner.CleanName(name);
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

        public static string GetFileHash(string path)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(path);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}