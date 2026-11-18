namespace JellyEmu.Utilities
{
    /// <summary>
    /// Read-only, seekable view over another stream in which a few individual bytes are
    /// replaced on the fly. Used to fix ROM headers while serving them, without touching the
    /// file on disk (library folders are often read-only). Works with HTTP range requests
    /// because every read, whatever its position, gets the overlay applied.
    /// </summary>
    public sealed class PatchedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly KeyValuePair<long, byte>[] _patches;

        public PatchedReadStream(Stream inner, IReadOnlyDictionary<long, byte> patches)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(patches);
            if (!inner.CanSeek) throw new ArgumentException("The inner stream must be seekable.", nameof(inner));
            _inner = inner;
            _patches = patches.OrderBy(p => p.Key).ToArray();
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            long start = _inner.Position;
            int read = _inner.Read(buffer);
            ApplyPatches(start, buffer[..read]);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            long start = _inner.Position;
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            ApplyPatches(start, buffer.Span[..read]);
            return read;
        }

        private void ApplyPatches(long start, Span<byte> data)
        {
            long end = start + data.Length;
            foreach (var patch in _patches)
            {
                if (patch.Key >= end) break;
                if (patch.Key >= start) data[(int)(patch.Key - start)] = patch.Value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void Flush()
        {
            // Read-only stream: nothing to flush.
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
