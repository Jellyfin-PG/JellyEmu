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
                string versionStr = JellyEmuVersion.Value;

                string injection = $$"""
                <script data-jellyemu-mods="1">
                    (function() {
                        var base = '';
                        if (window.location && window.location.pathname) {
                            var idx = window.location.pathname.indexOf('/web');
                            if (idx > 0) {
                                base = window.location.pathname.substring(0, idx);
                            }
                        }
                        base = (base || '').replace(/\/+$/, '');
                        window.__JELLYEMU_CONFIG__ = { vantageEnabled: {{vantageStr}} };

                        var link = document.createElement('link');
                        link.rel = 'stylesheet';
                        link.href = (base ? base : '') + '/jellyemu/assets/injection/bundle.css?v={{versionStr}}';
                        link.setAttribute('data-jellyemu-mods', '1');
                        document.head.appendChild(link);

                        var script = document.createElement('script');
                        script.src = (base ? base : '') + '/jellyemu/assets/injection/bundle.js?v={{versionStr}}';
                        script.defer = true;
                        script.setAttribute('data-jellyemu-mods', '1');
                        document.body.appendChild(script);
                    })();
                </script>
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