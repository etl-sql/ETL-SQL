using System;
using System.IO;

namespace ETL_SQL.Core.Common
{
    public sealed class ChainedStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly IDisposable[] _disposables;

        public ChainedStream(Stream innerStream, params IDisposable[] disposables)
        {
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _disposables = disposables ?? Array.Empty<IDisposable>();
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
                foreach (var d in _disposables)
                {
                    d?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
