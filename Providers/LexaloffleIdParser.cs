using System.Text.RegularExpressions;

namespace JellyEmu.Providers
{
    public static class LexaloffleIdParser
    {
        // Matches [loid-12345] anywhere in a string
        private static readonly Regex LoIdRegex =
            new Regex(@"\[loid-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Extracts the Lexaloffle BBS post ID from a filename or path.
        /// Returns null if not found.
        /// </summary>
        public static string? ParseFromString(string input)
        {
            var match = LoIdRegex.Match(input);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}