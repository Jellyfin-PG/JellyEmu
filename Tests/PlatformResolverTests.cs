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
        [InlineData("C:\\Games\\Windows\\Fallout.exe", "Windows")]
        [InlineData("C:\\Games\\windows\\Half-Life.zip", "Windows")]
        [InlineData("C:\\Games\\Linux\\Celeste.AppImage", "Linux")]
        [InlineData("C:\\Games\\linux\\HollowKnight.sh", "Linux")]
        [InlineData("C:\\Games\\MacOS\\StardewValley.app", "MacOS")]
        [InlineData("C:\\Games\\mac\\Braid.dmg", "MacOS")]
        [InlineData("C:\\Games\\Android\\Minecraft.apk", "Android")]
        [InlineData("C:\\Games\\android\\DeadCells.xapk", "Android")]
        [InlineData("C:\\Games\\GOG\\Game.cue", "Unknown")] // "gog" folder alias removed, cue is ambiguous without platform
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

        [Fact]
        public void IsRomPath_AudiobookCue_ShouldReturnFalse()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var mp4Path = Path.Combine(tempDir, "audiobook.mp4");
                File.WriteAllText(mp4Path, "dummy content");

                var cuePath = Path.Combine(tempDir, "audiobook.cue");
                File.WriteAllText(cuePath, $"FILE \"{Path.GetFileName(mp4Path)}\" BINARY\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00");

                Assert.False(RomExtensions.IsRomPath(cuePath));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void IsRomPath_GameCue_ShouldReturnTrue()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var binPath = Path.Combine(tempDir, "game.bin");
                File.WriteAllText(binPath, "dummy content");

                var cuePath = Path.Combine(tempDir, "game.cue");
                File.WriteAllText(cuePath, $"FILE \"{Path.GetFileName(binPath)}\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00");

                Assert.True(RomExtensions.IsRomPath(cuePath));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Theory]
        [InlineData("windows", "Skyrim")]
        [InlineData("linux", "Celeste")]
        [InlineData("macos", "StardewValley")]
        [InlineData("android", "Minecraft")]
        public void IsRomPath_ModernPlatformGameFolder_ShouldReturnTrue(string platformFolder, string gameFolder)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), platformFolder, gameFolder);
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "Game.exe"), "dummy exe");
                File.WriteAllText(Path.Combine(tempDir, "DATA01.DAT"), "dummy data");
                File.WriteAllText(Path.Combine(tempDir, "INPUT.INI"), "dummy ini");

                Assert.True(RomExtensions.IsRomPath(tempDir));
            }
            finally
            {
                var root = Path.GetDirectoryName(Path.GetDirectoryName(tempDir));
                if (root != null && Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Theory]
        [InlineData("windows")]
        [InlineData("linux")]
        [InlineData("macos")]
        [InlineData("android")]
        public void IsRomPath_PlatformCategoryFolderItself_ShouldReturnFalse(string platformFolder)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), platformFolder);
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "somefile.txt"), "dummy");
                Assert.False(RomExtensions.IsRomPath(tempDir));
            }
            finally
            {
                var root = Path.GetDirectoryName(tempDir);
                if (root != null && Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
