using JellyEmu.Controllers;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace JellyEmu.Tests
{
    public class CoreResolutionTests
    {
        private static BaseItem MakeItem(string? path = null, params string[] tags)
        {
            return new Book
            {
                Path = path,
                Tags = tags,
            };
        }

        [Theory]
        [InlineData("Game Boy Color", "gb")]
        [InlineData("Game Boy", "gb")]
        [InlineData("Game Boy Advance", "gba")]
        [InlineData("SNES", "snes")]
        [InlineData("Sega Saturn", "segaSaturn")]
        [InlineData("Atari 2600", "atari2600")]
        [InlineData("Atari 7800", "atari7800")]
        public void ResolveCore_ConsoleTag_ResolvesExpectedCore(string tag, string expected)
        {
            var item = MakeItem("game.bin", tag);

            Assert.Equal(expected, JellyEmuBaseController.ResolveCore(item));
        }

        [Theory]
        [InlineData("Pokemon Crystal (USA).gbc", "gb")]
        [InlineData("Tetris (World).gb", "gb")]
        [InlineData("Super Mario World (USA).sfc", "snes")]
        [InlineData("Sonic the Hedgehog (Japan).md", "segaMD")]
        public void ResolveCore_Extension_ResolvesExpectedCore(string path, string expected)
        {
            var item = MakeItem(path);

            Assert.Equal(expected, JellyEmuBaseController.ResolveCore(item));
        }

        [Fact]
        public void ResolveCore_GbcTagAndExtension_AgreeOnGbCore()
        {
            var tagged = MakeItem("Pokemon Crystal (USA).gbc", "Game Boy Color");
            var untagged = MakeItem("Pokemon Crystal (USA).gbc");

            Assert.Equal("gb", JellyEmuBaseController.ResolveCore(tagged));
            Assert.Equal("gb", JellyEmuBaseController.ResolveCore(untagged));
        }

        [Theory]
        [InlineData("mystery.xyz")]
        [InlineData("untagged.bin")]
        [InlineData(null)]
        public void ResolveCore_Unresolvable_ReturnsEmpty(string? path)
        {
            var item = MakeItem(path);

            Assert.Equal(string.Empty, JellyEmuBaseController.ResolveCore(item));
        }
    }
}
