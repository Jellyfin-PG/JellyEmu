using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace JellyEmu.Tests
{
    public class PlatformResolverTests
    {
        private readonly PlatformResolver _resolver;

        public PlatformResolverTests()
        {
            _resolver = new PlatformResolver(NullLogger<PlatformResolver>.Instance);
        }

        [Theory]
        [InlineData("Super Mario World (USA).sfc", "SNES")]
        [InlineData("Sonic the Hedgehog (Japan).md", "Sega Genesis")]
        [InlineData("Pokemon Emerald (USA, Europe).gba", "Game Boy Advance")]
        [InlineData("Grand Theft Auto (GBA).zip", "Game Boy Advance")]
        [InlineData("Tony Hawk's Pro Skater.chd", "Unknown")] // Needs folder or name hint since .chd is ambiguous
        [InlineData("C:\\Games\\Game Boy Advance\\Pokemon.zip", "Game Boy Advance")] // Matches directory name
        [InlineData("C:\\Games\\genesis\\Sonic.zip", "Sega Genesis")] // Matches directory name
        [InlineData("C:\\Games\\PSX\\Spyro.cue", "PlayStation")] // Matches directory name
        public void ResolvePlatform_ShouldResolveCorrectly(string path, string expected)
        {
            // Act
            var result = _resolver.Resolve(path);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Tony Hawk's Pro Skater (USA) (PS1).chd", "Tony Hawk's Pro Skater (USA) (PS1)", "PlayStation")]
        [InlineData("Spyro the Dragon.chd", "Spyro the Dragon (PSX)", "PlayStation")]
        public void ResolvePlatform_WithNameHint_ShouldResolveCorrectly(string path, string name, string expected)
        {
            // Act
            var result = _resolver.ResolvePlatform(path, name);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Super Mario World (USA) (SNES)", "Super Mario World")]
        [InlineData("Sonic the Hedgehog (Japan) (Europe)", "Sonic the Hedgehog")]
        [InlineData("Crash Bandicoot (USA) (Disc 1)", "Crash Bandicoot")]
        [InlineData("Legend of Zelda, The (USA)", "Legend of Zelda, The")]
        public void CleanDisplayName_ShouldRemoveBracketsAndTokens(string input, string expected)
        {
            // Act
            var result = PlatformResolver.CleanDisplayName(input);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
