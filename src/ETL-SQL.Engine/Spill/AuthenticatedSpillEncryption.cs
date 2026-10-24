using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Spill;

internal static class AuthenticatedSpillEncryption
{
    private static ReadOnlySpan<byte> Magic => "ETLSPL02"u8;
    private const int HeaderSize = 58; // magic, codec, compression, salt (32), IV (16)
    private const int TagSize = 32;

    public static (CryptoStream Cipher, AuthenticationWriter Authentication) CreateWriter(
        Stream file, byte[] sessionKey, string chunkName, byte codec, bool compressed, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        header[8] = codec;
        header[9] = compressed ? (byte)1 : (byte)0;
        RandomNumberGenerator.Fill(header.AsSpan(10));
        var keys = DeriveKeys(sessionKey, header);
        try
        {
            var authentication = new AuthenticationWriter(file, header, keys.AsSpan(32), chunkName, cancellationToken);
            return (new CryptoStream(authentication, CreateTransform(keys, header, encrypt: true),
                CryptoStreamMode.Write, leaveOpen: true), authentication);
        }
        finally { CryptographicOperations.ZeroMemory(keys); }
    }

    public static async Task<CryptoStream> CreateReaderAsync(
        Stream file, byte[] sessionKey, string chunkName, byte codec, bool compressed, CancellationToken cancellationToken)
    {
        byte[]? keys = null;
        try
        {
            var header = new byte[HeaderSize];
            await file.ReadExactlyAsync(header, cancellationToken);
            // COMPAT_BREAK: 0.22 — encrypted spill files require the authenticated v2 envelope.
            if (!header.AsSpan(0, 8).SequenceEqual(Magic) || header[8] != codec || header[9] != (compressed ? 1 : 0))
                throw new CryptographicException();
            var length = file.Length - HeaderSize - TagSize;
            if (length < 16 || length % 16 != 0) throw new CryptographicException();
            keys = DeriveKeys(sessionKey, header);
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keys.AsSpan(32));
            AppendIdentity(hmac, header, chunkName);
            var buffer = new byte[81920];
            for (long remaining = length; remaining > 0;)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read == 0) throw new CryptographicException();
                hmac.AppendData(buffer.AsSpan(0, read));
                remaining -= read;
            }
            var tag = new byte[TagSize];
            await file.ReadExactlyAsync(tag, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(hmac.GetHashAndReset(), tag)) throw new CryptographicException();
            file.Position = HeaderSize;
            return new CryptoStream(new CiphertextReader(file, length), CreateTransform(keys, header, encrypt: false), CryptoStreamMode.Read);
        }
        catch (Exception error) when (error is CryptographicException or EndOfStreamException)
        {
            throw new ExecutionException("Spill chunk authentication failed. Rerun the statement or recreate the saved session.");
        }
        finally { if (keys != null) CryptographicOperations.ZeroMemory(keys); }
    }

    private static byte[] DeriveKeys(byte[] sessionKey, byte[] header) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sessionKey, 64, header.AsSpan(10, 32).ToArray(), "ETL-SQL spill v2"u8.ToArray());

    private static ICryptoTransform CreateTransform(byte[] keys, byte[] header, bool encrypt)
    {
        var key = keys[..32];
        try
        {
            using var aes = Aes.Create();
            return encrypt ? aes.CreateEncryptor(key, header[42..]) : aes.CreateDecryptor(key, header[42..]);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static void AppendIdentity(IncrementalHash hmac, byte[] header, string chunkName)
    {
        var name = Encoding.UTF8.GetBytes(chunkName);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, name.Length);
        hmac.AppendData(header);
        hmac.AppendData(length);
        hmac.AppendData(name);
    }

    internal sealed class AuthenticationWriter : Stream
    {
        private readonly Stream _file;
        private readonly byte[] _header;
        private readonly IncrementalHash _hmac;
        private readonly CancellationToken _cancellationToken;
        private bool _started;
        private bool _completed;

        internal AuthenticationWriter(Stream file, byte[] header, ReadOnlySpan<byte> key, string chunkName, CancellationToken cancellationToken)
        {
            _file = file;
            _header = header;
            _cancellationToken = cancellationToken;
            _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
            AppendIdentity(_hmac, header, chunkName);
        }

        private async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            if (_completed) throw new InvalidOperationException("Spill encryption is already complete.");
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_started)
            {
                await _file.WriteAsync(_header, cancellationToken);
                _started = true;
            }
        }

        public async Task CompleteAsync()
        {
            await StartAsync(_cancellationToken);
            await _file.WriteAsync(_hmac.GetHashAndReset(), _cancellationToken);
            await _file.FlushAsync(_cancellationToken);
            _completed = true;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var token = cancellationToken.CanBeCanceled ? cancellationToken : _cancellationToken;
            await StartAsync(token);
            await _file.WriteAsync(buffer, token);
            _hmac.AppendData(buffer.Span);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override Task FlushAsync(CancellationToken cancellationToken) => _file.FlushAsync(cancellationToken);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_completed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Spill writes must be asynchronous.");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _hmac.Dispose(); base.Dispose(disposing); }
    }

    private sealed class CiphertextReader(Stream file, long length) : Stream
    {
        private readonly long _length = length;
        private long _remaining = length;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining == 0 || buffer.IsEmpty) return 0;
            var read = await file.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
            if (read == 0) throw new ExecutionException("Authenticated spill ciphertext was truncated while reading.");
            _remaining -= read;
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Spill reads must be asynchronous.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
