using Xunit;
using JellyEmu.Controllers;

namespace JellyEmu.Tests
{
    public class FilenameCleanerTests
    {
        [Theory]
        [InlineData("Tony Hawk's Pro Skater", "Tony Hawks Pro Skater")]
        [InlineData("Super Mario \"Bros.\"", "Super Mario Bros.")]
        [InlineData("Sonic #1", "Sonic 1")]
        [InlineData("Who?", "Who")]
        [InlineData("Pokemon & Friends", "Pokemon  Friends")]
        [InlineData("Back\\Slash", "BackSlash")]
        [InlineData("NormalFilename", "NormalFilename")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void CleanCosmeticFilename_ShouldRemoveUnsafeCharacters(string? input, string expected)
        {
            // Act
            var result = JellyEmuPlayController.CleanCosmeticFilename(input!);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
