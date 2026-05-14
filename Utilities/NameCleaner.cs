using System.Text.RegularExpressions;

namespace JellyEmu.Utilities
{
    public static partial class NameCleaner
    {
        [GeneratedRegex(@"\s*\([^)]*\)|\s*\[[^\]]*\]|\s*\{[^}]*\}|\s*<[^>]*>")]
        private static partial Regex NestedBracketsRegex();

        [GeneratedRegex(@"[()\[\]{}<>]")]
        private static partial Regex DanglingBracketsRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex MultipleSpacesRegex();

        public static string CleanName(string name)
        {
            var stripped = PlatformResolver.CleanDisplayName(name ?? string.Empty);

            if (string.IsNullOrWhiteSpace(stripped)) 
                return stripped;

            string previous;
            
            do
            {
                previous = stripped;
                stripped = NestedBracketsRegex().Replace(stripped, " ");
            } while (stripped != previous);

            var noSymbols = stripped.Replace('_', ' ').Replace('-', ' ');

            var noDanglingBrackets = DanglingBracketsRegex().Replace(noSymbols, "");
            var cleaned = MultipleSpacesRegex().Replace(noDanglingBrackets, " ").Trim();
            
            return cleaned;
        }
    }
}