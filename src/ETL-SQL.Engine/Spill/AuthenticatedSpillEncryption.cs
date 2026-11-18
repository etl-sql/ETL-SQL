using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Spill;

internal static class AuthenticatedSpillEncryption
{
    private static ReadOnlySpan<byte> MagicV2 => "ETLSPL02"u8;
    private static ReadOnlySpan<byte> MagicV3 => "ETLSPL03"u8;

    private const int HeaderSizeV2 = 58; // magic (8), codec (1), compression (1), salt (32), IV (16)
    private const int TagSizeV2 = 32;

    internal const int HeaderSizeV3 = 64; // magic (8), codec (1), compression (1), maxFrame (4), salt (32), chunkId (16), flags (2)
    internal const int DefaultMaxFramePlaintextBytes = 131_072; // 128 KiB
    internal const int MinAllowedFramePlaintextBytes = 4_096;
    internal const int MaxAllowedFramePlaintextBytes = 1_048_576;
    internal const int FrameHeaderSizeV3 = 13; // frameIndex (8), frameFlags (1), payloadLength (4)
    internal const int TagSizeV3 = 16;
    internal const byte FrameFlagFinal = 0x01;

    private const string AuthenticationFailedMessage =
        "Spill chunk authentication failed. Rerun the statement or recreate the saved session.";

    public static FramedEncryptionWriter CreateWriter(
        Stream file,
        byte[] sessionKey,
        string chunkName,
        byte codec,
        bool compressed,
        CancellationToken cancellationToken,
        int maxFramePlaintextBytes = DefaultMaxFramePlaintextBytes)
    {
        if (maxFramePlaintextBytes < MinAllowedFramePlaintextBytes || maxFramePlaintextBytes > MaxAllowedFramePlaintextBytes)
            throw new ArgumentOutOfRangeException(nameof(maxFramePlaintextBytes));

        var header = new byte[HeaderSizeV3];
        MagicV3.CopyTo(header);
        header[8] = codec;
        header[9] = compressed ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(10, 4), (uint)maxFramePlaintextBytes);
        RandomNumberGenerator.Fill(header.AsSpan(14, 48)); // salt (32) + chunkId (16)
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(62, 2), 0);

        var key = DeriveKeyV3(sessionKey, header);
        try
        {
            return new FramedEncryptionWriter(file, header, key, chunkName, maxFramePlaintextBytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static async Task<Stream> CreateReaderAsync(
        Stream file,
        byte[] sessionKey,
        string chunkName,
        byte codec,
        bool compressed,
        CancellationToken cancellationToken)
    {
        try
        {
            var magic = new byte[8];
            await file.ReadExactlyAsync(magic, cancellationToken);

            if (magic.AsSpan().SequenceEqual(MagicV3))
                return await CreateV3ReaderAsync(file, magic, sessionKey, chunkName, codec, compressed, cancellationToken);

            if (magic.AsSpan().SequenceEqual(MagicV2))
                return await CreateV2ReaderAsync(file, magic, sessionKey, chunkName, codec, compressed, cancellationToken);

            throw new CryptographicException();
        }
        catch (ExecutionException)
        {
            throw;
        }
        catch (Exception error) when (error is CryptographicException or EndOfStreamException)
        {
            throw new ExecutionException(AuthenticationFailedMessage);
        }
    }

    private static async Task<Stream> CreateV3ReaderAsync(
        Stream file,
        byte[] magic,
        byte[] sessionKey,
        string chunkName,
        byte codec,
        bool compressed,
        CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSizeV3];
        magic.CopyTo(header, 0);
        await file.ReadExactlyAsync(header.AsMemory(8, HeaderSizeV3 - 8), cancellationToken);

        if (header[8] != codec || header[9] != (compressed ? 1 : 0))
            throw new CryptographicException();

        var maxFrameBytes = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(10, 4));
        var headerFlags = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(62, 2));
        if (headerFlags != 0 || maxFrameBytes < MinAllowedFramePlaintextBytes || maxFrameBytes > MaxAllowedFramePlaintextBytes)
            throw new CryptographicException();

        var key = DeriveKeyV3(sessionKey, header);
        FramedEncryptionReader? reader = null;
        try
        {
            reader = new FramedEncryptionReader(file, header, key, chunkName, (int)maxFrameBytes);
            await reader.AuthenticateFirstFrameAsync(cancellationToken);
            return reader;
        }
        catch
        {
            if (reader != null)
                await reader.DisposeAsync();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task<CryptoStream> CreateV2ReaderAsync(
        Stream file,
        byte[] magic,
        byte[] sessionKey,
        string chunkName,
        byte codec,
        bool compressed,
        CancellationToken cancellationToken)
    {
        byte[]? keys = null;
        try
        {
            var header = new byte[HeaderSizeV2];
            magic.CopyTo(header, 0);
            await file.ReadExactlyAsync(header.AsMemory(8, HeaderSizeV2 - 8), cancellationToken);
            // COMPAT_BREAK: 0.22 — encrypted spill files require an authenticated envelope.
            if (header[8] != codec || header[9] != (compressed ? 1 : 0))
                throw new CryptographicException();
            var length = file.Length - HeaderSizeV2 - TagSizeV2;
            if (length < 16 || length % 16 != 0) throw new CryptographicException();
            keys = DeriveKeysV2(sessionKey, header);
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
            var tag = new byte[TagSizeV2];
            await file.ReadExactlyAsync(tag, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(hmac.GetHashAndReset(), tag)) throw new CryptographicException();
            file.Position = HeaderSizeV2;
            return new CryptoStream(new CiphertextReader(file, length), CreateTransformV2(keys, header, encrypt: false), CryptoStreamMode.Read);
        }
        finally
        {
            if (keys != null) CryptographicOperations.ZeroMemory(keys);
        }
    }

    internal static byte[] BuildIdentityPrefix(byte[] header, string chunkName)
    {
        var nameBytes = Encoding.UTF8.GetBytes(chunkName);
        var identity = new byte[header.Length + 4 + nameBytes.Length];
        header.CopyTo(identity, 0);
        BinaryPrimitives.WriteInt32BigEndian(identity.AsSpan(header.Length, 4), nameBytes.Length);
        nameBytes.CopyTo(identity.AsSpan(header.Length + 4));
        return identity;
    }

    internal static byte[] DeriveKeyV3(byte[] sessionKey, ReadOnlySpan<byte> header) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sessionKey, 32, header.Slice(14, 32).ToArray(), "ETL-SQL spill v3"u8.ToArray());

    private static byte[] DeriveKeysV2(byte[] sessionKey, byte[] header) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sessionKey, 64, header.AsSpan(10, 32).ToArray(), "ETL-SQL spill v2"u8.ToArray());

    private static ICryptoTransform CreateTransformV2(byte[] keys, byte[] header, bool encrypt)
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

    internal sealed class FramedEncryptionWriter : Stream
    {
        private readonly Stream _file;
        private readonly byte[] _header;
        private readonly AesGcm _aesGcm;
        private readonly CancellationToken _cancellationToken;
        private readonly int _maxFramePlaintextBytes;
        private readonly int _maxFrameBufferBytes;
        private byte[] _plainBuf;
        private byte[] _frameBuf;
        private readonly byte[] _aadBuf;
        private readonly int _identityLength;
        private readonly byte[] _nonce = new byte[12];
        private int _plainOffset;
        private ulong _frameIndex;
        private bool _started;
        private bool _completed;
        private bool _disposed;

        internal FramedEncryptionWriter(
            Stream file,
            byte[] header,
            byte[] derivedKey,
            string chunkName,
            int maxFramePlaintextBytes,
            CancellationToken cancellationToken)
        {
            _file = file;
            _header = header;
            _aesGcm = new AesGcm(derivedKey, TagSizeV3);
            _maxFramePlaintextBytes = maxFramePlaintextBytes;
            _maxFrameBufferBytes = FrameHeaderSizeV3 + TagSizeV3 + maxFramePlaintextBytes;
            _cancellationToken = cancellationToken;

            _plainBuf = ArrayPool<byte>.Shared.Rent(maxFramePlaintextBytes);
            try
            {
                _frameBuf = ArrayPool<byte>.Shared.Rent(_maxFrameBufferBytes);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(_plainBuf);
                _plainBuf = Array.Empty<byte>();
                throw;
            }

            var identity = BuildIdentityPrefix(header, chunkName);
            _identityLength = identity.Length;
            _aadBuf = new byte[_identityLength + FrameHeaderSizeV3];
            identity.CopyTo(_aadBuf, 0);

            header.AsSpan(46, 4).CopyTo(_nonce);
        }

        private async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            if (_completed) throw new InvalidOperationException("Spill encryption is already complete.");
            if (_disposed) throw new ObjectDisposedException(nameof(FramedEncryptionWriter));
            _cancellationToken.ThrowIfCancellationRequested();
            if (!_started)
            {
                await _file.WriteAsync(_header, cancellationToken);
                _started = true;
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var token = cancellationToken.CanBeCanceled ? cancellationToken : _cancellationToken;
            await StartAsync(token);

            var remaining = buffer;
            while (!remaining.IsEmpty)
            {
                if (_plainOffset == _maxFramePlaintextBytes)
                    await EmitFrameAsync(_plainOffset, isFinal: false, token);

                var space = _maxFramePlaintextBytes - _plainOffset;
                var toCopy = Math.Min(space, remaining.Length);
                remaining.Span[..toCopy].CopyTo(_plainBuf.AsSpan(_plainOffset, toCopy));
                _plainOffset += toCopy;
                remaining = remaining[toCopy..];
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public async Task CompleteAsync()
        {
            if (_completed) return;
            await StartAsync(_cancellationToken);
            await EmitFrameAsync(_plainOffset, isFinal: true, _cancellationToken);
            await _file.FlushAsync(_cancellationToken);
            _completed = true;
        }

        private async ValueTask EmitFrameAsync(int payloadLength, bool isFinal, CancellationToken cancellationToken)
        {
            var frameHeader = _frameBuf.AsSpan(0, FrameHeaderSizeV3);
            BinaryPrimitives.WriteUInt64BigEndian(frameHeader[..8], _frameIndex);
            frameHeader[8] = isFinal ? FrameFlagFinal : (byte)0;
            BinaryPrimitives.WriteUInt32BigEndian(frameHeader.Slice(9, 4), (uint)payloadLength);

            frameHeader.CopyTo(_aadBuf.AsSpan(_identityLength, FrameHeaderSizeV3));
            BinaryPrimitives.WriteUInt64BigEndian(_nonce.AsSpan(4, 8), _frameIndex);

            var tagSpan = _frameBuf.AsSpan(FrameHeaderSizeV3, TagSizeV3);
            var cipherSpan = _frameBuf.AsSpan(FrameHeaderSizeV3 + TagSizeV3, payloadLength);
            var plainSpan = _plainBuf.AsSpan(0, payloadLength);

            _aesGcm.Encrypt(_nonce, plainSpan, cipherSpan, tagSpan, _aadBuf);
            CryptographicOperations.ZeroMemory(plainSpan);

            await _file.WriteAsync(_frameBuf.AsMemory(0, FrameHeaderSizeV3 + TagSizeV3 + payloadLength), cancellationToken);
            _plainOffset = 0;
            _frameIndex++;
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => _file.FlushAsync(cancellationToken);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_completed && !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Spill writes must be asynchronous.");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                if (_plainBuf.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(_plainBuf.AsSpan(0, _maxFramePlaintextBytes));
                    ArrayPool<byte>.Shared.Return(_plainBuf);
                    _plainBuf = Array.Empty<byte>();
                }
                if (_frameBuf.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(_frameBuf.AsSpan(0, _maxFrameBufferBytes));
                    ArrayPool<byte>.Shared.Return(_frameBuf);
                    _frameBuf = Array.Empty<byte>();
                }
                CryptographicOperations.ZeroMemory(_aadBuf);
                CryptographicOperations.ZeroMemory(_nonce);
                _aesGcm.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class FramedEncryptionReader : Stream
    {
        private readonly Stream _file;
        private readonly AesGcm _aesGcm;
        private readonly int _maxFramePlaintextBytes;
        private readonly int _maxCipherBufferBytes;
        private byte[] _plainBuf;
        private byte[] _cipherBuf;
        private readonly byte[] _aadBuf;
        private readonly int _identityLength;
        private readonly byte[] _nonce = new byte[12];
        private readonly byte[] _frameHeader = new byte[FrameHeaderSizeV3];
        private readonly byte[] _trailingProbe = new byte[1];
        private int _plainOffset;
        private int _plainLength;
        private ulong _expectedFrameIndex;
        private bool _seenFinal;
        private bool _eof;
        private bool _failed;
        private bool _disposed;

        internal FramedEncryptionReader(
            Stream file,
            byte[] header,
            byte[] derivedKey,
            string chunkName,
            int maxFramePlaintextBytes)
        {
            _file = file;
            _aesGcm = new AesGcm(derivedKey, TagSizeV3);
            _maxFramePlaintextBytes = maxFramePlaintextBytes;
            _maxCipherBufferBytes = TagSizeV3 + maxFramePlaintextBytes;

            _plainBuf = ArrayPool<byte>.Shared.Rent(maxFramePlaintextBytes);
            try
            {
                _cipherBuf = ArrayPool<byte>.Shared.Rent(_maxCipherBufferBytes);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(_plainBuf);
                _plainBuf = Array.Empty<byte>();
                throw;
            }

            var identity = BuildIdentityPrefix(header, chunkName);
            _identityLength = identity.Length;
            _aadBuf = new byte[_identityLength + FrameHeaderSizeV3];
            identity.CopyTo(_aadBuf, 0);

            header.AsSpan(46, 4).CopyTo(_nonce);
        }

        internal async ValueTask AuthenticateFirstFrameAsync(CancellationToken cancellationToken)
        {
            await ReadAndAuthenticateNextFrameAsync(cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_failed) throw new ExecutionException(AuthenticationFailedMessage);
            if (_disposed) throw new ObjectDisposedException(nameof(FramedEncryptionReader));
            if (buffer.IsEmpty) return 0;

            while (true)
            {
                if (_plainOffset < _plainLength)
                {
                    var available = _plainLength - _plainOffset;
                    var toCopy = Math.Min(buffer.Length, available);
                    _plainBuf.AsSpan(_plainOffset, toCopy).CopyTo(buffer.Span);
                    CryptographicOperations.ZeroMemory(_plainBuf.AsSpan(_plainOffset, toCopy));
                    _plainOffset += toCopy;
                    return toCopy;
                }

                if (_eof)
                    return 0;

                await ReadAndAuthenticateNextFrameAsync(cancellationToken);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private async ValueTask ReadAndAuthenticateNextFrameAsync(CancellationToken cancellationToken)
        {
            if (_seenFinal)
            {
                _eof = true;
                return;
            }

            try
            {
                await _file.ReadExactlyAsync(_frameHeader, cancellationToken);

                var frameIndex = BinaryPrimitives.ReadUInt64BigEndian(_frameHeader.AsSpan(0, 8));
                var frameFlags = _frameHeader[8];
                var payloadLengthU = BinaryPrimitives.ReadUInt32BigEndian(_frameHeader.AsSpan(9, 4));

                if (frameIndex != _expectedFrameIndex)
                    throw new CryptographicException();

                if ((frameFlags & ~FrameFlagFinal) != 0)
                    throw new CryptographicException();

                var isFinal = (frameFlags & FrameFlagFinal) != 0;
                if (payloadLengthU > (uint)_maxFramePlaintextBytes || (!isFinal && payloadLengthU == 0))
                    throw new CryptographicException();

                var payloadLength = (int)payloadLengthU;
                var totalBodyBytes = TagSizeV3 + payloadLength;
                await _file.ReadExactlyAsync(_cipherBuf.AsMemory(0, totalBodyBytes), cancellationToken);

                _frameHeader.CopyTo(_aadBuf.AsSpan(_identityLength, FrameHeaderSizeV3));
                BinaryPrimitives.WriteUInt64BigEndian(_nonce.AsSpan(4, 8), frameIndex);

                var tagSpan = _cipherBuf.AsSpan(0, TagSizeV3);
                var cipherSpan = _cipherBuf.AsSpan(TagSizeV3, payloadLength);
                var plainSpan = _plainBuf.AsSpan(0, payloadLength);

                _aesGcm.Decrypt(_nonce, cipherSpan, tagSpan, plainSpan, _aadBuf);

                _expectedFrameIndex++;
                _plainOffset = 0;
                _plainLength = payloadLength;

                if (isFinal)
                {
                    _seenFinal = true;
                    // Reject trailing bytes after the authenticated completion marker without seeking.
                    var extra = await _file.ReadAsync(_trailingProbe.AsMemory(0, 1), cancellationToken);
                    if (extra != 0)
                        throw new CryptographicException();
                    if (payloadLength == 0)
                        _eof = true;
                }
            }
            catch (ExecutionException)
            {
                _failed = true;
                if (_plainBuf.Length >= _maxFramePlaintextBytes)
                    CryptographicOperations.ZeroMemory(_plainBuf.AsSpan(0, _maxFramePlaintextBytes));
                throw;
            }
            catch (Exception error) when (error is CryptographicException or EndOfStreamException)
            {
                _failed = true;
                if (_plainBuf.Length >= _maxFramePlaintextBytes)
                    CryptographicOperations.ZeroMemory(_plainBuf.AsSpan(0, _maxFramePlaintextBytes));
                throw new ExecutionException(AuthenticationFailedMessage);
            }
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Spill reads must be asynchronous.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                if (_plainBuf.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(_plainBuf.AsSpan(0, _maxFramePlaintextBytes));
                    ArrayPool<byte>.Shared.Return(_plainBuf);
                    _plainBuf = Array.Empty<byte>();
                }
                if (_cipherBuf.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(_cipherBuf.AsSpan(0, _maxCipherBufferBytes));
                    ArrayPool<byte>.Shared.Return(_cipherBuf);
                    _cipherBuf = Array.Empty<byte>();
                }
                CryptographicOperations.ZeroMemory(_aadBuf);
                CryptographicOperations.ZeroMemory(_nonce);
                CryptographicOperations.ZeroMemory(_frameHeader);
                _aesGcm.Dispose();
            }
            base.Dispose(disposing);
        }
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
