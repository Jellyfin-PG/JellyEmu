using System.Buffers.Binary;

namespace JellyEmu.Utilities
{
    /// <summary>
    /// Crypto state of the bootable (first) NCCH partition of a Nintendo 3DS ROM.
    /// </summary>
    public enum N3dsCryptoState
    {
        /// <summary>The file is not an NCSD (.3ds/.cci) or NCCH (.cxi) image.</summary>
        NotN3ds,

        /// <summary>The bootable partition is decrypted and correctly flagged as such.</summary>
        Decrypted,

        /// <summary>
        /// The bootable partition's content is decrypted, but its header still claims it is
        /// encrypted. Typical of older "Decrypted" dumps made with XOR pads. Azahar refuses
        /// these (ErrorEncrypted) unless the NoCrypto flag is set.
        /// </summary>
        DecryptedMissingFlag,

        /// <summary>The bootable partition is really encrypted; Azahar cannot run it.</summary>
        Encrypted,
    }

    /// <summary>
    /// Result of inspecting a 3DS ROM: the crypto state of its bootable partition, and the
    /// single-byte header patches that make every decrypted-but-mislabelled partition
    /// loadable by Azahar.
    /// </summary>
    public sealed record N3dsRomInspection(N3dsCryptoState State, IReadOnlyDictionary<long, byte> Patches)
    {
        public static readonly N3dsRomInspection NotN3ds =
            new(N3dsCryptoState.NotN3ds, new Dictionary<long, byte>());
    }

    /// <summary>
    /// Reads NCSD/NCCH headers (see https://www.3dbrew.org/wiki/NCSD and /NCCH) to find out
    /// whether a 3DS ROM's content is really encrypted, independently of what its header
    /// flags claim. Only a handful of small header reads are performed.
    /// </summary>
    public static class N3dsRomInspector
    {
        private const int HeaderSize = 0x200;
        private const int MagicOffset = 0x100;
        private const int PartitionTableOffset = 0x120;
        private const int PartitionCount = 8;
        private const int FlagsOffset = 0x188;
        private const int MediaUnitSizeFlag = 6;
        private const int CryptoFlagsIndex = 7;
        private const byte NoCryptoFlag = 0x04;
        private const int ExHeaderSizeOffset = 0x180;
        private const int ExeFsOffsetOffset = 0x1A0;
        private const int RomFsOffsetOffset = 0x1B0;
        private const uint IvfcRomFsMagicId = 0x10000;

        private static readonly string[] CandidateExtensions = { ".3ds", ".cci", ".cxi" };

        /// <summary>Whether a file extension can hold an NCSD or NCCH image.</summary>
        public static bool IsCandidate(string path)
        {
            var ext = Path.GetExtension(path);
            return CandidateExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Inspects a seekable stream positioned anywhere. The stream position is left undefined.
        /// </summary>
        public static N3dsRomInspection Inspect(Stream stream)
        {
            if (!stream.CanSeek || !stream.CanRead || stream.Length < HeaderSize)
                return N3dsRomInspection.NotN3ds;

            var header = ReadAt(stream, 0, HeaderSize);
            if (header == null) return N3dsRomInspection.NotN3ds;

            var magic = header.AsSpan(MagicOffset, 4);
            var partitions = new List<long>();

            if (magic.SequenceEqual("NCSD"u8))
            {
                if (!TryGetMediaUnit(header[FlagsOffset + MediaUnitSizeFlag], out long mediaUnit))
                    return N3dsRomInspection.NotN3ds;
                for (int i = 0; i < PartitionCount; i++)
                {
                    var entry = header.AsSpan(PartitionTableOffset + (i * 8), 8);
                    long offset = BinaryPrimitives.ReadUInt32LittleEndian(entry) * mediaUnit;
                    long size = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]) * mediaUnit;
                    // Partition 0 is the bootable one; keep its slot even if empty so that
                    // index 0 of the list always refers to it.
                    if (size == 0)
                    {
                        if (i == 0) return N3dsRomInspection.NotN3ds;
                        continue;
                    }
                    partitions.Add(offset);
                }
            }
            else if (magic.SequenceEqual("NCCH"u8))
            {
                partitions.Add(0);
            }
            else
            {
                return N3dsRomInspection.NotN3ds;
            }

            var patches = new Dictionary<long, byte>();
            var bootState = N3dsCryptoState.Encrypted;

            for (int i = 0; i < partitions.Count; i++)
            {
                var state = InspectNcch(stream, partitions[i], out var patchOffset, out var patchValue);
                if (state == N3dsCryptoState.DecryptedMissingFlag)
                    patches[patchOffset] = patchValue;
                if (i == 0) bootState = state;
            }

            // A boot partition that does not even look like an NCCH is not something we know.
            if (bootState == N3dsCryptoState.NotN3ds) return N3dsRomInspection.NotN3ds;

            return new N3dsRomInspection(bootState, patches);
        }

        /// <summary>Inspects a ROM file on disk.</summary>
        public static N3dsRomInspection Inspect(string path)
        {
            if (!IsCandidate(path)) return N3dsRomInspection.NotN3ds;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            return Inspect(fs);
        }

        private static N3dsCryptoState InspectNcch(Stream stream, long baseOffset, out long patchOffset, out byte patchValue)
        {
            patchOffset = baseOffset + FlagsOffset + CryptoFlagsIndex;
            patchValue = 0;

            var ncch = ReadAt(stream, baseOffset, HeaderSize);
            if (ncch == null || !ncch.AsSpan(MagicOffset, 4).SequenceEqual("NCCH"u8))
                return N3dsCryptoState.NotN3ds;

            byte cryptoFlags = ncch[FlagsOffset + CryptoFlagsIndex];
            if ((cryptoFlags & NoCryptoFlag) != 0)
                return N3dsCryptoState.Decrypted;

            if (!TryGetMediaUnit(ncch[FlagsOffset + MediaUnitSizeFlag], out long mediaUnit))
                return N3dsCryptoState.NotN3ds;
            uint exHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(ncch.AsSpan(ExHeaderSizeOffset));
            long exeFsOffset = BinaryPrimitives.ReadUInt32LittleEndian(ncch.AsSpan(ExeFsOffsetOffset)) * mediaUnit;
            long exeFsSize = BinaryPrimitives.ReadUInt32LittleEndian(ncch.AsSpan(ExeFsOffsetOffset + 4)) * mediaUnit;
            long romFsOffset = BinaryPrimitives.ReadUInt32LittleEndian(ncch.AsSpan(RomFsOffsetOffset)) * mediaUnit;
            long romFsSize = BinaryPrimitives.ReadUInt32LittleEndian(ncch.AsSpan(RomFsOffsetOffset + 4)) * mediaUnit;

            bool checkedAnything = false;

            // Executable content (CXI): the ExeFS file table must be readable plain text.
            if (exHeaderSize > 0 || exeFsSize > 0)
            {
                if (exeFsSize == 0) return N3dsCryptoState.Encrypted;
                var exeFs = ReadAt(stream, baseOffset + exeFsOffset, 16);
                if (exeFs == null || !LooksLikePlainExeFsEntry(exeFs)) return N3dsCryptoState.Encrypted;
                checkedAnything = true;
            }

            // Data content (manual, update partition, ...) or the RomFS of a CXI.
            if (romFsSize > 0)
            {
                var romFs = ReadAt(stream, baseOffset + romFsOffset, 8);
                if (romFs == null
                    || !romFs.AsSpan(0, 4).SequenceEqual("IVFC"u8)
                    || BinaryPrimitives.ReadUInt32LittleEndian(romFs.AsSpan(4)) != IvfcRomFsMagicId)
                {
                    return N3dsCryptoState.Encrypted;
                }
                checkedAnything = true;
            }

            if (!checkedAnything) return N3dsCryptoState.Encrypted;

            patchValue = (byte)(cryptoFlags | NoCryptoFlag);
            return N3dsCryptoState.DecryptedMissingFlag;
        }

        /// <summary>
        /// First ExeFS file header entry: an 8-byte NUL-padded ASCII name (".code", "icon",
        /// "banner", ...) whose data starts at offset 0. Encrypted data is random bytes.
        /// </summary>
        private static bool LooksLikePlainExeFsEntry(byte[] entry)
        {
            int nameLength = 0;
            while (nameLength < 8 && entry[nameLength] != 0)
            {
                if (entry[nameLength] < 0x20 || entry[nameLength] > 0x7E) return false;
                nameLength++;
            }
            if (nameLength == 0) return false;
            for (int i = nameLength; i < 8; i++)
            {
                if (entry[i] != 0) return false;
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(8)) == 0;
        }

        private static bool TryGetMediaUnit(byte exponent, out long mediaUnit)
        {
            // Retail images always use 0x200; anything beyond a few MiB per unit is garbage.
            mediaUnit = exponent <= 8 ? 0x200L << exponent : 0;
            return mediaUnit != 0;
        }

        private static byte[]? ReadAt(Stream stream, long offset, int count)
        {
            if (offset < 0 || offset + count > stream.Length) return null;
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[count];
            stream.ReadExactly(buffer);
            return buffer;
        }
    }
}
