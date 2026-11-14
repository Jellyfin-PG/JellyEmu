using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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

            var controller = new JellyEmuRomController(
                libraryManagerMock.Object,
                appPathsMock.Object,
                NullLogger<JellyEmuRomController>.Instance,
                null!,
                null!,
                httpClientFactoryMock.Object,
                platformResolver,
                fileService);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return (controller, libraryManagerMock, tempDir);
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
                var mockItem = new Mock<BaseItem>();
                mockItem.Setup(i => i.Path).Returns(gameDir);
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
                var mockItem = new Mock<BaseItem>();
                mockItem.Setup(i => i.Path).Returns(gameDir);
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
    }
}
