using JellyEmu.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace JellyEmu.Tests
{
    public class BiosServiceTests
    {
        [Fact]
        public void LibretroSystemDatabase_ValidateBios_MatchesMd5Correctly()
        {
            var db = LibretroSystemDatabase.Instance;
            Assert.NotEmpty(db.Entries);

            // Exact MD5 for scph5501.bin (490f666e1afb15b7362b406ed1cea246)
            var result = db.ValidateBios("my_ps1_bios.bin", "490f666e1afb15b7362b406ed1cea246", "0555c6fae8906f3f09baf5988f00e55f88e9f30b", 524288);
            Assert.Equal(BiosValidationStatus.Verified, result.Status);
            Assert.Equal("PlayStation", result.SystemOrPlatform);
            Assert.Equal("scph5501.bin", result.CanonicalName);

            // Mismatched hash for known filename
            var mismatch = db.ValidateBios("scph5501.bin", "bad00000000000000000000000000000", "bad0000000000000000000000000000000000000", 1234);
            Assert.Equal(BiosValidationStatus.Mismatch, mismatch.Status);
            Assert.Equal("PlayStation", mismatch.SystemOrPlatform);

            // Unrecognized file
            var unrecognized = db.ValidateBios("unknown_file.bin", "deadbeefdeadbeefdeadbeefdeadbeef", null, 9999);
            Assert.Equal(BiosValidationStatus.Unrecognized, unrecognized.Status);
        }

        [Fact]
        public void BiosService_ShouldAutoDetectValidateAndSelectActiveBios()
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

                // Create a PS1 BIOS file (scph5501.bin with dummy content -> mismatch)
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
                Assert.NotEmpty(ps1Item.Md5);
                Assert.Equal("Mismatch", ps1Item.Status); // Dummy bytes != official hash
                Assert.True(ps1Item.IsActive); // Single PS1 BIOS -> active by default

                var gbaItem = list.Find(i => i.FileName == "gba_bios.bin");
                Assert.NotNull(gbaItem);
                Assert.Equal("GBA/gba_bios.bin", gbaItem!.RelativePath);
                Assert.Equal("Game Boy Advance", gbaItem.SystemOrCore);
                Assert.Equal(4, gbaItem.SizeBytes);
                Assert.NotEmpty(gbaItem.Md5);
                Assert.True(gbaItem.IsActive); // Single GBA BIOS -> active by default
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void BiosService_MultipleBiosForSystem_SwitchesActiveSuccessfully()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jellyemu_bios_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var appPaths = new MockAppPaths(tempDir);
                var biosService = new JellyEmuBiosService(appPaths, NullLogger<JellyEmuBiosService>.Instance);

                var dir = biosService.GetBiosDirectory();
                var usBios = Path.Combine(dir, "scph5501.bin");
                var jpBios = Path.Combine(dir, "scph5500.bin");
                File.WriteAllBytes(usBios, new byte[] { 10, 20 });
                File.WriteAllBytes(jpBios, new byte[] { 30, 40 });

                var list1 = biosService.ListInstalledBios();
                Assert.Equal(2, list1.Count);

                // Set JP BIOS as active
                biosService.SetActiveBios("PlayStation", "scph5500.bin");

                var resolved = biosService.ResolveBiosRelativePath("PlayStation", "pcsx_rearmed");
                Assert.Equal("scph5500.bin", resolved);

                var list2 = biosService.ListInstalledBios();
                var jpItem = list2.Find(i => i.FileName == "scph5500.bin");
                var usItem = list2.Find(i => i.FileName == "scph5501.bin");
                Assert.True(jpItem!.IsActive);
                Assert.False(usItem!.IsActive);
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
