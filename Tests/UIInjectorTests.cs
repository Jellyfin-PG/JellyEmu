using Xunit;
using JellyEmu.Services;

namespace JellyEmu.Tests
{
    public class UIInjectorTests
    {
        [Fact]
        public void InjectMods_ValidHtml_InjectsLinkAndScriptTags()
        {
            var payload = new PatchRequestPayload
            {
                Contents = "<!DOCTYPE html><html><head><title>Jellyfin</title></head><body><div id=\"app\"></div></body></html>"
            };

            var result = JellyEmuUIInjector.InjectMods(payload);

            Assert.Contains("<!-- JellyEmu-Mods-Start -->", result);
            Assert.Contains("<!-- JellyEmu-Mods-End -->", result);
            Assert.Contains("/jellyemu/assets/injection/bundle.css", result);
            Assert.Contains("/jellyemu/assets/injection/bundle.js", result);
            Assert.Contains("window.__JELLYEMU_CONFIG__", result);
            Assert.EndsWith("</body></html>", result);
        }

        [Fact]
        public void InjectMods_ExistingInjection_ReplacesPreviousBlockCleanly()
        {
            var payload = new PatchRequestPayload
            {
                Contents = "<!DOCTYPE html><html><body><!-- JellyEmu-Mods-Start -->old injection<!-- JellyEmu-Mods-End --><div>Content</div></body></html>"
            };

            var result = JellyEmuUIInjector.InjectMods(payload);

            Assert.DoesNotContain("old injection", result);
            Assert.Contains("/jellyemu/assets/injection/bundle.js", result);
        }

        [Fact]
        public void InjectMods_NoBodyTag_ReturnsOriginalUnchanged()
        {
            var payload = new PatchRequestPayload
            {
                Contents = "<div>No body tag</div>"
            };

            var result = JellyEmuUIInjector.InjectMods(payload);

            Assert.Equal("<div>No body tag</div>", result);
        }
    }
}
