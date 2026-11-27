using System.Text.Json;
using JellyEmu.Controllers;
using Xunit;

namespace JellyEmu.Tests
{
    /// <summary>
    /// Stored prefs (audioEq, controls) are rendered raw inside a script block on the
    /// anonymous play pages, so they must never be able to close the block or break out.
    /// </summary>
    public class PlayTemplateJsonTests
    {
        [Theory]
        [InlineData("{}</script><script>alert(1)</script>")] // trailing payload after valid JSON
        [InlineData("1};alert(1);//")]
        [InlineData("[1,2]")]                                  // valid JSON but not an object
        [InlineData("\"str\"")]
        [InlineData("not json")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void SafeJsonObject_ReturnsFallback_ForNonObjects(string? input)
        {
            Assert.Equal("FALLBACK", JellyEmuPlayController.SafeJsonObject(input, "FALLBACK"));
        }

        [Theory]
        [InlineData("{\"x\":\"</script><script>alert(1)</script>\"}")]
        [InlineData("{\"</script><script>alert(1)</script>\":1}")]
        [InlineData("{\"x\":\"\\u003c/script>\"}")]
        [InlineData("{\"x\":\"a\u2028b\u2029c\"}")]
        public void SafeJsonObject_EscapesScriptBreakouts(string input)
        {
            var output = JellyEmuPlayController.SafeJsonObject(input, "{}");

            Assert.DoesNotContain("<", output);
            Assert.DoesNotContain(">", output);
            Assert.DoesNotContain("\u2028", output);
            Assert.DoesNotContain("\u2029", output);
            // Same data after the round trip, just encoded.
            Assert.True(JsonElement.DeepEquals(
                JsonDocument.Parse(input).RootElement,
                JsonDocument.Parse(output).RootElement));
        }

        [Fact]
        public void SafeJsonObject_PreservesLegitimateObjects()
        {
            const string input = "{\"bands\":[1.5,-2,0],\"enabled\":true,\"btn\":{\"a\":\"KeyX\"}}";
            var output = JellyEmuPlayController.SafeJsonObject(input, "{}");
            Assert.True(JsonElement.DeepEquals(
                JsonDocument.Parse(input).RootElement,
                JsonDocument.Parse(output).RootElement));
        }
    }
}
