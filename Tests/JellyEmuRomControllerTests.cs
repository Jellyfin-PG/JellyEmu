using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using JellyEmu.Controllers;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace JellyEmu.Tests
{
    public class JellyEmuRomControllerTests
    {
        private static (JellyEmuRomController controller, Mock<ILibraryManager> libraryManagerMock, string tempDir) CreateController()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "je_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var libraryManagerMock = new Mock<ILibraryManager>();
            var appPathsMock = new Mock<IApplicationPaths>();
            appPathsMock.Setup(a => a.PluginConfigurationsPath).Returns(tempDir);
            appPathsMock.Setup(a => a.DataPath).Returns(tempDir);

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            var platformResolver = new PlatformResolver(NullLogger<PlatformResolver>.Instance);
            var fileService = new JellyEmuFileService(libraryManagerMock.Object, appPathsMock.Object, NullLogger<JellyEmuFileService>.Instance);

            var userManagerMock = new Mock<IUserManager>();
            userManagerMock.Setup(u => u.GetUserById(TestUser.Id)).Returns(TestUser);

            var controller = new JellyEmuRomController(
                libraryManagerMock.Object,
                appPathsMock.Object,
                NullLogger<JellyEmuRomController>.Instance,
                null!,
                null!,
                httpClientFactoryMock.Object,
                platformResolver,
                fileService,
                userManagerMock.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("Jellyfin-UserId", TestUser.Id.ToString()) }, "Test"))
                }
            };

            return (controller, libraryManagerMock, tempDir);
        }

        private static readonly JellyfinUser TestUser = new JellyfinUser("player", "default", "default");

        /// <summary>A JellyEmu-tagged Book visible to <see cref="TestUser"/> unless <paramref name="visible"/> is false.</summary>
        private static Mock<Book> RomItem(string path, bool visible = true, string[]? tags = null)
        {
            var mock = new Mock<Book>();
            mock.Setup(i => i.Path).Returns(path);
            mock.Setup(i => i.IsVisible(It.IsAny<JellyfinUser>(), It.IsAny<bool>())).Returns(visible);
            mock.Object.Tags = tags ?? new[] { "JellyEmu", "Game" };
            return mock;
        }

        [Fact]
        public async Task Rom_ServesSingleFileDirectly_WhenDirectoryContainsSingleArchive()
        {
            var (controller, libraryManagerMock, tempDir) = CreateController();
            try
            {
                // Create game folder DOS/Doom/Doom.zip
                var gameDir = Path.Combine(tempDir, "Doom");
                Directory.CreateDirectory(gameDir);
                var zipPath = Path.Combine(gameDir, "Doom.zip");
                await File.WriteAllBytesAsync(zipPath, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

                var itemId = Guid.NewGuid();
                var mockItem = RomItem(gameDir);
                libraryManagerMock.Setup(l => l.GetItemById(itemId)).Returns(mockItem.Object);

                var result = await controller.Rom(itemId.ToString("N"));

                var fileResult = Assert.IsType<FileStreamResult>(result);
                Assert.Equal("application/octet-stream", fileResult.ContentType);
                Assert.True(fileResult.EnableRangeProcessing);
                Assert.Contains("Doom.zip", controller.Response.Headers["Content-Disposition"].ToString());
                fileResult.FileStream.Dispose();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Rom_CreatesValidTemporaryZip_WhenDirectoryContainsMultipleFiles()
        {
            var (controller, libraryManagerMock, tempDir) = CreateController();
            try
            {
                // Create multi-file folder DOS/Wolf3D with exe and data files
                var gameDir = Path.Combine(tempDir, "Wolf3D");
                Directory.CreateDirectory(gameDir);
                var exePath = Path.Combine(gameDir, "WOLF3D.EXE");
                var datPath = Path.Combine(gameDir, "GAMEMAPS.WL6");
                await File.WriteAllTextAsync(exePath, "MOCK_EXE");
                await File.WriteAllTextAsync(datPath, "MOCK_DATA");

                var itemId = Guid.NewGuid();
                var mockItem = RomItem(gameDir);
                libraryManagerMock.Setup(l => l.GetItemById(itemId)).Returns(mockItem.Object);

                var result = await controller.Rom(itemId.ToString("N"));

                var fileResult = Assert.IsType<FileStreamResult>(result);
                Assert.Equal("application/zip", fileResult.ContentType);
                Assert.True(fileResult.EnableRangeProcessing);
                Assert.Contains("Wolf3D.zip", controller.Response.Headers["Content-Disposition"].ToString());

                // Verify that the generated stream is a valid readable zip archive
                using (var archive = new ZipArchive(fileResult.FileStream, ZipArchiveMode.Read, leaveOpen: false))
                {
                    Assert.NotNull(archive.GetEntry("WOLF3D.EXE"));
                    Assert.NotNull(archive.GetEntry("GAMEMAPS.WL6"));
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task Rom_SetsNoCryptoFlag_WhenServingDecrypted3dsRomWithStaleHeader()
        {
            var (controller, libraryManagerMock, tempDir) = CreateController();
            try
            {
                var image = N3dsRomInspectorTests.BuildNcsd(bootPlain: true, bootNoCryptoFlag: false);
                var romPath = Path.Join(tempDir, "Game (Europe) Decrypted.3ds");
                await File.WriteAllBytesAsync(romPath, image);

                var itemId = Guid.NewGuid();
                var mockItem = RomItem(romPath);
                libraryManagerMock.Setup(l => l.GetItemById(itemId)).Returns(mockItem.Object);

                var result = await controller.Rom(itemId.ToString("N"));

                var fileResult = Assert.IsType<FileStreamResult>(result);
                Assert.True(fileResult.EnableRangeProcessing);
                using var served = new MemoryStream();
                await fileResult.FileStream.CopyToAsync(served);
                fileResult.FileStream.Dispose();

                var bytes = served.ToArray();
                Assert.Equal(image.Length, bytes.Length);
                Assert.Equal(0x04, bytes[0x4000 + 0x18F]);
                bytes[0x4000 + 0x18F] = image[0x4000 + 0x18F];
                Assert.Equal(image, bytes);

                // The library file itself is left untouched.
                Assert.Equal(image, await File.ReadAllBytesAsync(romPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task DownloadZip_ReturnsNotFound_WhenItemMissingOrEmpty()
        {
            var (controller, libraryManagerMock, tempDir) = CreateController();
            try
            {
                var missingId = Guid.NewGuid();
                libraryManagerMock.Setup(l => l.GetItemById(missingId)).Returns((BaseItem?)null);

                var result = await controller.DownloadZip(missingId.ToString("N"));

                Assert.IsType<NotFoundResult>(result);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Theory]
        [InlineData("hidden")]   // user lacks library access / parental block
        [InlineData("untagged")] // not a JellyEmu ROM
        [InlineData("anonymous")]
        public async Task Rom_And_DownloadZip_ReturnNotFound_WhenNotAccessible(string scenario)
        {
            var (controller, libraryManagerMock, tempDir) = CreateController();
            try
            {
                var gameDir = Path.Combine(tempDir, "Secret");
                Directory.CreateDirectory(gameDir);
                await File.WriteAllTextAsync(Path.Combine(gameDir, "a.bin"), "A");
                await File.WriteAllTextAsync(Path.Combine(gameDir, "b.bin"), "B");

                var itemId = Guid.NewGuid();
                var mockItem = RomItem(gameDir,
                    visible: scenario != "hidden",
                    tags: scenario == "untagged" ? new[] { "Book" } : null);
                libraryManagerMock.Setup(l => l.GetItemById(itemId)).Returns(mockItem.Object);
                if (scenario == "anonymous")
                {
                    controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
                }

                Assert.IsType<NotFoundResult>(await controller.Rom(itemId.ToString("N")));
                Assert.IsType<NotFoundResult>(await controller.DownloadZip(itemId.ToString("N")));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
