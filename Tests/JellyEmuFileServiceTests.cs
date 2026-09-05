using System;
using System.IO;
using JellyEmu.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyEmu.Tests
{
    public class JellyEmuFileServiceTests
    {
        [Fact]
        public void GetActiveDiscIndex_DefaultsToOne_WhenMetaFileMissing()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuFileService(null!, appPaths, NullLogger<JellyEmuFileService>.Instance);

                var discIndex = service.GetActiveDiscIndex("user1", "item1", maxDiscs: 3);
                Assert.Equal(1, discIndex);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SetAndGetActiveDiscIndex_SavesAndRetrievesSuccessfully()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuFileService(null!, appPaths, NullLogger<JellyEmuFileService>.Instance);

                service.SetActiveDiscIndex("user1", "item1", 2);

                var discIndex = service.GetActiveDiscIndex("user1", "item1", maxDiscs: 3);
                Assert.Equal(2, discIndex);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetActiveDiscIndex_ClampsOutOfBoundsIndex_ToOne()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuFileService(null!, appPaths, NullLogger<JellyEmuFileService>.Instance);

                service.SetActiveDiscIndex("user1", "item1", 99);

                // Max discs is 2, so 99 is invalid and should default to 1
                var discIndex = service.GetActiveDiscIndex("user1", "item1", maxDiscs: 2);
                Assert.Equal(1, discIndex);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveActiveRomPath_ReturnsDirectPath_ForSingleRom()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Combine(tempDir, "game.sfc");
                File.WriteAllText(romPath, "dummy");

                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuFileService(null!, appPaths, NullLogger<JellyEmuFileService>.Instance);

                var resolved = service.ResolveActiveRomPath(romPath, "user1", "item1");
                Assert.Equal(romPath, resolved);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveActiveRomPath_ReturnsCorrectDisc_ForJ3uPlaylist()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var disc1 = Path.Combine(tempDir, "disc1.iso");
                var disc2 = Path.Combine(tempDir, "disc2.iso");
                File.WriteAllText(disc1, "disc 1");
                File.WriteAllText(disc2, "disc 2");

                var j3uPath = Path.Combine(tempDir, "game.j3u");
                File.WriteAllLines(j3uPath, new[] { "disc1.iso", "disc2.iso" });

                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuFileService(null!, appPaths, NullLogger<JellyEmuFileService>.Instance);

                // Default disc 1
                var resolved1 = service.ResolveActiveRomPath(j3uPath, "user1", "item1");
                Assert.Equal(disc1, resolved1);

                // Switch to disc 2
                service.SetActiveDiscIndex("user1", "item1", 2);
                var resolved2 = service.ResolveActiveRomPath(j3uPath, "user1", "item1");
                Assert.Equal(disc2, resolved2);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveAllRomFiles_ExpandsJ3uPlaylistFiles()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var disc1 = Path.Combine(tempDir, "disc1.iso");
                var disc2 = Path.Combine(tempDir, "disc2.iso");
                File.WriteAllText(disc1, "disc 1");
                File.WriteAllText(disc2, "disc 2");

                var j3uPath = Path.Combine(tempDir, "game.j3u");
                File.WriteAllLines(j3uPath, new[] { "disc1.iso", "disc2.iso" });

                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuFileService(null!, appPaths, NullLogger<JellyEmuFileService>.Instance);

                var allFiles = service.ResolveAllRomFiles(j3uPath);
                Assert.Equal(2, allFiles.Count);
                Assert.Contains(disc1, allFiles);
                Assert.Contains(disc2, allFiles);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
