using JellyEmu.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using Xunit;

namespace JellyEmu.Tests
{
    public class BiosServiceTests
    {
        [Fact]
        public void BiosService_ShouldCreateDirectoryAndResolveKnownBiosFiles()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jellyemu_bios_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appPaths = new MockAppPaths(tempDir);
                var biosService = new JellyEmuBiosService(appPaths, NullLogger<JellyEmuBiosService>.Instance);

                var expectedDir = biosService.GetBiosDirectory();
                Assert.True(Directory.Exists(expectedDir));

                // Initially no BIOS files
                Assert.Null(biosService.ResolveBiosRelativePath("PlayStation", "pcsx_rearmed"));
                Assert.Empty(biosService.ListInstalledBios());

                // Create a PS1 BIOS file
                var ps1Bios = Path.Combine(expectedDir, "scph5501.bin");
                File.WriteAllBytes(ps1Bios, new byte[] { 1, 2, 3 });

                // Create a GBA BIOS file inside a GBA subfolder
                var gbaSubDir = Path.Combine(expectedDir, "GBA");
                Directory.CreateDirectory(gbaSubDir);
                var gbaBios = Path.Combine(gbaSubDir, "gba_bios.bin");
                File.WriteAllBytes(gbaBios, new byte[] { 4, 5, 6, 7 });

                // Test resolution
                var ps1Resolved = biosService.ResolveBiosRelativePath("PlayStation", "pcsx_rearmed");
                Assert.Equal("scph5501.bin", ps1Resolved);

                var gbaResolved = biosService.ResolveBiosRelativePath("Game Boy Advance", "mgba");
                Assert.Equal("GBA/gba_bios.bin", gbaResolved);

                Assert.Null(biosService.ResolveBiosRelativePath("Sega Genesis", "genesis_plus_gx"));

                // Test list properties
                var list = biosService.ListInstalledBios();
                Assert.Equal(2, list.Count);

                var ps1Item = list.Find(i => i.FileName == "scph5501.bin");
                Assert.NotNull(ps1Item);
                Assert.Equal("scph5501.bin", ps1Item!.RelativePath);
                Assert.Equal("PlayStation", ps1Item.SystemOrCore);
                Assert.Equal(3, ps1Item.SizeBytes);

                var gbaItem = list.Find(i => i.FileName == "gba_bios.bin");
                Assert.NotNull(gbaItem);
                Assert.Equal("GBA/gba_bios.bin", gbaItem!.RelativePath);
                Assert.Equal("Game Boy Advance", gbaItem.SystemOrCore);
                Assert.Equal(4, gbaItem.SizeBytes);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
