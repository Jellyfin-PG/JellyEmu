using JellyEmu.Controllers;
using Xunit;

namespace JellyEmu.Tests
{
    public class ScreenshotNameTests
    {
        private const string FallbackId = "2999f82e6359450a8885d75fadc93aaa";

        [Theory]
        [InlineData("Super Metroid", "Super Metroid")]
        [InlineData("Metroid/Prime", "MetroidPrime")]
        [InlineData("  Trimmed  ", "Trimmed")]
        public void GetSafeScreenshotName_KeepsReadableNames(string input, string expected)
        {
            Assert.Equal(expected, JellyEmuSaveController.GetSafeScreenshotName(input, FallbackId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("..")]
        [InlineData("...")]
        public void GetSafeScreenshotName_FallsBackToItemId(string? input)
        {
            Assert.Equal(FallbackId, JellyEmuSaveController.GetSafeScreenshotName(input, FallbackId));
        }

        [Fact]
        public void GetSafeScreenshotName_StripsPathSeparators()
        {
            var result = JellyEmuSaveController.GetSafeScreenshotName("../../etc/passwd", FallbackId);
            Assert.DoesNotContain("/", result);
            Assert.DoesNotContain("\\", result);
        }
    }
}
