using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JellyEmu.Controllers;
using JellyEmu.Utilities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyEmu.Tests
{
    public class N3dsRomInspectorTests
    {
        private const int MediaUnit = 0x200;
        private const long Partition0Offset = 0x20 * MediaUnit;
        private const long Partition1Offset = 0x30 * MediaUnit;
        private const long Partition0CryptoFlags = Partition0Offset + 0x18F;
        private const long Partition1CryptoFlags = Partition1Offset + 0x18F;

        /// <summary>
        /// Builds a minimal NCSD (.3ds) image: partition 0 is an executable NCCH (ExHeader,
        /// ExeFS, RomFS), optional partition 1 is a data-only NCCH (RomFS only, like a manual).
        /// </summary>
        internal static byte[] BuildNcsd(bool bootPlain, bool bootNoCryptoFlag, bool? dataPartitionPlain = null)
        {
            var image = new byte[0x40 * MediaUnit];
            var rng = new Random(1234);
            rng.NextBytes(image);

            // NCSD header
            Encoding.ASCII.GetBytes("NCSD").CopyTo(image, 0x100);
            image[0x188 + 6] = 0; // media unit = 0x200
            Array.Clear(image, 0x120, 8 * 8);
            WritePartition(image, 0, Partition0Offset, 0x10 * MediaUnit);
            if (dataPartitionPlain.HasValue)
                WritePartition(image, 1, Partition1Offset, 0x08 * MediaUnit);

            // Partition 0: executable NCCH
            WriteNcchHeader(image, Partition0Offset, bootNoCryptoFlag, exHeaderSize: 0x400,
                exeFsUnit: 4, romFsUnit: 6);
            if (bootPlain)
            {
                WriteExeFsEntry(image, Partition0Offset + (4 * MediaUnit), ".code");
                WriteIvfc(image, Partition0Offset + (6 * MediaUnit));
            }

            // Partition 1: data-only NCCH
            if (dataPartitionPlain.HasValue)
            {
                WriteNcchHeader(image, Partition1Offset, noCrypto: false, exHeaderSize: 0,
                    exeFsUnit: 0, romFsUnit: 1);
                if (dataPartitionPlain.Value)
                    WriteIvfc(image, Partition1Offset + MediaUnit);
            }

            return image;
        }

        private static void WritePartition(byte[] image, int index, long offset, long size)
        {
            var entry = image.AsSpan(0x120 + (index * 8), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)(offset / MediaUnit));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)(size / MediaUnit));
        }

        private static void WriteNcchHeader(byte[] image, long offset, bool noCrypto, uint exHeaderSize, uint exeFsUnit, uint romFsUnit)
        {
            var h = image.AsSpan((int)offset, 0x200);
            Encoding.ASCII.GetBytes("NCCH").CopyTo(h[0x100..]);
            h.Slice(0x188, 8).Clear();
            h[0x188 + 4] = 0x01; // platform: CTR
            h[0x188 + 5] = 0x03; // content type
            h[0x188 + 7] = noCrypto ? (byte)0x04 : (byte)0x00;
            BinaryPrimitives.WriteUInt32LittleEndian(h[0x180..], exHeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(h[0x1A0..], exeFsUnit);
            BinaryPrimitives.WriteUInt32LittleEndian(h[0x1A4..], exeFsUnit == 0 ? 0u : 1u);
            BinaryPrimitives.WriteUInt32LittleEndian(h[0x1B0..], romFsUnit);
            BinaryPrimitives.WriteUInt32LittleEndian(h[0x1B4..], romFsUnit == 0 ? 0u : 1u);
        }

        private static void WriteExeFsEntry(byte[] image, long offset, string name)
        {
            var e = image.AsSpan((int)offset, 16);
            e.Clear();
            Encoding.ASCII.GetBytes(name).CopyTo(e);
            BinaryPrimitives.WriteUInt32LittleEndian(e[12..], 0x1000);
        }

        private static void WriteIvfc(byte[] image, long offset)
        {
            var e = image.AsSpan((int)offset, 8);
            Encoding.ASCII.GetBytes("IVFC").CopyTo(e);
            BinaryPrimitives.WriteUInt32LittleEndian(e[4..], 0x10000);
        }

        private static N3dsRomInspection Inspect(byte[] image)
        {
            using var ms = new MemoryStream(image, writable: false);
            return N3dsRomInspector.Inspect(ms);
        }

        [Fact]
        public void Inspect_DecryptedContentWithoutNoCryptoFlag_ReturnsFlagPatch()
        {
            var result = Inspect(BuildNcsd(bootPlain: true, bootNoCryptoFlag: false));

            Assert.Equal(N3dsCryptoState.DecryptedMissingFlag, result.State);
            var patch = Assert.Single(result.Patches);
            Assert.Equal(Partition0CryptoFlags, patch.Key);
            Assert.Equal(0x04, patch.Value);
        }

        [Fact]
        public void Inspect_EncryptedContent_ReportsEncryptedWithoutPatches()
        {
            var result = Inspect(BuildNcsd(bootPlain: false, bootNoCryptoFlag: false));

            Assert.Equal(N3dsCryptoState.Encrypted, result.State);
            Assert.Empty(result.Patches);
        }

        [Fact]
        public void Inspect_NoCryptoFlagAlreadySet_ReportsDecryptedWithoutPatches()
        {
            var result = Inspect(BuildNcsd(bootPlain: true, bootNoCryptoFlag: true));

            Assert.Equal(N3dsCryptoState.Decrypted, result.State);
            Assert.Empty(result.Patches);
        }

        [Fact]
        public void Inspect_OnlyPatchesPartitionsWhoseContentIsReallyDecrypted()
        {
            var plainManual = Inspect(BuildNcsd(bootPlain: true, bootNoCryptoFlag: false, dataPartitionPlain: true));
            Assert.Equal(new[] { Partition0CryptoFlags, Partition1CryptoFlags }, plainManual.Patches.Keys);

            // Old XOR-pad dumps often left the manual / update partitions encrypted.
            var encryptedManual = Inspect(BuildNcsd(bootPlain: true, bootNoCryptoFlag: false, dataPartitionPlain: false));
            Assert.Equal(N3dsCryptoState.DecryptedMissingFlag, encryptedManual.State);
            Assert.Equal(new[] { Partition0CryptoFlags }, encryptedManual.Patches.Keys);
        }

        [Fact]
        public void Inspect_StandaloneNcch_IsSupported()
        {
            var ncsd = BuildNcsd(bootPlain: true, bootNoCryptoFlag: false);
            var cxi = ncsd.AsSpan((int)Partition0Offset, 0x10 * MediaUnit).ToArray();

            var result = Inspect(cxi);

            Assert.Equal(N3dsCryptoState.DecryptedMissingFlag, result.State);
            Assert.Equal(0x18F, Assert.Single(result.Patches).Key);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0x100)]
        [InlineData(0x4000)]
        public void Inspect_NonN3dsData_ReturnsNotN3ds(int length)
        {
            var data = new byte[length];
            new Random(42).NextBytes(data);

            var result = Inspect(data);

            Assert.Equal(N3dsCryptoState.NotN3ds, result.State);
            Assert.Empty(result.Patches);
        }

        [Fact]
        public void Inspect_TruncatedImage_DoesNotThrow()
        {
            var image = BuildNcsd(bootPlain: true, bootNoCryptoFlag: false);
            var truncated = image.AsSpan(0, (int)Partition0Offset + 0x200).ToArray();

            var result = Inspect(truncated);

            Assert.Equal(N3dsCryptoState.Encrypted, result.State);
            Assert.Empty(result.Patches);
        }

        [Theory]
        [InlineData("game.3ds", true)]
        [InlineData("GAME.CCI", true)]
        [InlineData("game.cxi", true)]
        [InlineData("game.cia", false)]
        [InlineData("game.nds", false)]
        public void IsCandidate_MatchesNcsdAndNcchExtensions(string path, bool expected)
        {
            Assert.Equal(expected, N3dsRomInspector.IsCandidate(path));
        }

        [Theory]
        [InlineData(true, true, null, "/jellyemu/rom/x/game.3ds?userId=u")]
        [InlineData(true, false, null, "/jellyemu/rom/x/game.3ds?userId=u&ncch=nocrypto")]
        [InlineData(false, false, JellyEmuPlayController.EncryptedN3dsRomMessage, "/jellyemu/rom/x/game.3ds?userId=u")]
        public void PlayController_GetN3dsRomError_ExplainsEncryptedRomsAndBustsCacheForPatchedOnes(
            bool bootPlain, bool noCryptoFlag, string? expectedError, string expectedUrl)
        {
            var tempDir = Path.Join(Path.GetTempPath(), "je_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Join(tempDir, "game.3ds");
                File.WriteAllBytes(romPath, BuildNcsd(bootPlain, noCryptoFlag));
                var controller = new JellyEmuPlayController(
                    new Mock<ILibraryManager>().Object,
                    new Mock<IApplicationPaths>().Object,
                    NullLogger<JellyEmuPlayController>.Instance,
                    null!,
                    null!,
                    new Mock<System.Net.Http.IHttpClientFactory>().Object);

                var romUrl = "/jellyemu/rom/x/game.3ds?userId=u";
                var error = controller.GetN3dsRomError(romPath, ref romUrl);

                Assert.Equal(expectedError, error);
                Assert.Equal(expectedUrl, romUrl);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void PatchedReadStream_AppliesPatchesOnFullAndRangedReads()
        {
            var data = new byte[0x1000];
            var patches = new Dictionary<long, byte> { [0x10] = 0xAA, [0x800] = 0xBB, [0xFFF] = 0xCC };
            using var stream = new PatchedReadStream(new MemoryStream(data, writable: false), patches);

            var all = new byte[data.Length];
            stream.ReadExactly(all);
            Assert.Equal(0xAA, all[0x10]);
            Assert.Equal(0xBB, all[0x800]);
            Assert.Equal(0xCC, all[0xFFF]);
            Assert.Equal(3, Array.FindAll(all, b => b != 0).Length);

            // A range that starts in the middle of the file, as served for HTTP Range requests.
            stream.Seek(0x7FF, SeekOrigin.Begin);
            var range = new byte[3];
            stream.ReadExactly(range);
            Assert.Equal(new byte[] { 0, 0xBB, 0 }, range);
        }

        [Fact]
        public async Task PatchedReadStream_AppliesPatchesOnAsyncReads()
        {
            var data = new byte[0x100];
            var patches = new Dictionary<long, byte> { [0x80] = 0x04 };
            await using var stream = new PatchedReadStream(new MemoryStream(data, writable: false), patches);

            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, 7);

            Assert.Equal(data.Length, copy.Length);
            Assert.Equal(0x04, copy.ToArray()[0x80]);
            Assert.False(stream.CanWrite);
        }
    }
}
