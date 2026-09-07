namespace JellyEmu
{
    /// <summary>
    /// Single source of truth for the plugin's version and User-Agent strings,
    /// derived from the assembly version so they never go stale.
    /// </summary>
    public static class JellyEmuVersion
    {
        /// <summary>
        /// The plugin version, e.g. "0.9.0.0".
        /// </summary>
        public static string Value { get; } =
            typeof(JellyEmuVersion).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        /// <summary>
        /// The plain User-Agent product token, e.g. "JellyEmu/0.9.0.0".
        /// </summary>
        public static string UserAgent { get; } = $"JellyEmu/{Value}";

        /// <summary>
        /// A browser-compatible User-Agent for services that reject bare product tokens.
        /// </summary>
        public static string BrowserUserAgent { get; } = $"Mozilla/5.0 (compatible; JellyEmu/{Value})";
    }
}
