using System;
using System.Text.RegularExpressions;

namespace JellyEmu.Services
{
    public class PatchRequestPayload
    {
        public string? Path { get; set; }
        public string? Contents { get; set; }
    }

    public static class JellyEmuUIInjector
    {
        private const string StartMarker = "<!-- JellyEmu-Mods-Start -->";
        private const string EndMarker = "<!-- JellyEmu-Mods-End -->";

        public static string InjectMods(PatchRequestPayload payload)
        {
            try
            {
                string htmlContent = payload.Contents ?? string.Empty;

                if (string.IsNullOrEmpty(htmlContent) || !htmlContent.Contains("</body>"))
                {
                    return htmlContent;
                }

                htmlContent = Regex.Replace(htmlContent, Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?", string.Empty);

                bool vantageEnabled = Plugin.Instance?.Configuration?.VantageEnabled ?? true;
                string vantageStr = vantageEnabled ? "true" : "false";
                string versionStr = typeof(JellyEmuUIInjector).Assembly.GetName().Version?.ToString() ?? "0.8.8";

                string injection = $$"""
                <link rel="stylesheet" href="/jellyemu/assets/injection/bundle.css?v={{versionStr}}" data-jellyemu-mods="1">
                <script data-jellyemu-mods="1">window.__JELLYEMU_CONFIG__ = { vantageEnabled: {{vantageStr}} };</script>
                <script src="/jellyemu/assets/injection/bundle.js?v={{versionStr}}" defer data-jellyemu-mods="1"></script>
                """;

                string block = "\n" + StartMarker + "\n" + injection + "\n" + EndMarker + "\n";
                htmlContent = Regex.Replace(htmlContent, @"(</body>)", block + "$1");

                return htmlContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JellyEmu] Fatal Error injecting mods: {ex.Message}");
                return payload?.Contents ?? string.Empty;
            }
        }
    }
}