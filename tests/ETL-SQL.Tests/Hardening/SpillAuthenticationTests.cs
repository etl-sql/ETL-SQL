using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Execution;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

[Trait("CompatBreak", "0.22")]
public class SpillAuthenticationTests
{
    public static IEnumerable<object[]> CorruptionCases()
    {
        foreach (var format in new[] { "Json", "Arrow" })
            foreach (var compress in new[] { false, true })
                foreach (var damage in new[]
                {
                    "magic", "codec", "compression", "maxFrame", "headerFlags",
                    "salt", "chunkId", "ciphertext", "tag", "truncated",
                    "frame-truncated", "appended", "header-only"
                })
                    yield return [format, compress, damage];
    }

    [Theory]
    [MemberData(nameof(CorruptionCases))]
    public async Task Corruption_IsRejectedFailClosed(string format, bool compress, string damage)
    {
        using var store = CreateStore(format, compress);
        await using (var writer = await store.CreateWriterAsync("rows.tmp"))
            await writer.WriteRowAsync(new Row { ["amount"] = 1m, ["payload"] = new string('x', 200_000) });

        var path = Path.Combine(store.RootPath, "rows.tmp");
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        switch (damage)
        {
            case "magic": bytes[0] ^= 1; break;
            case "codec": bytes[8] ^= 3; break;
            case "compression": bytes[9] ^= 1; break;
            case "maxFrame":
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(10, 4), 1024); // below min 4096
                break;
            case "headerFlags": bytes[63] ^= 1; break;
            case "salt": bytes[14] ^= 1; break;
            case "chunkId": bytes[46] ^= 1; break;
            case "ciphertext": bytes[^1] ^= 1; break;
            case "tag":
                // Flip the last frame's tag byte (frame 0 when compressed, frame 1 when uncompressed).
                var tagOffset = FindLastFrameTagOffset(bytes);
                bytes[tagOffset] ^= 1;
                break;
            case "truncated": bytes = bytes[..^1]; break;
            case "frame-truncated":
                var lastFrameStart = FindLastFrameStartOffset(bytes);
                bytes = bytes[..lastFrameStart];
                break;
            case "appended": bytes = [.. bytes, (byte)0]; break;
            case "header-only": bytes = bytes[..AuthenticatedSpillEncryption.HeaderSizeV3]; break;
        }
        await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await using var reader = await store.CreateReaderAsync("rows.tmp");
            while (await reader.ReadRowAsync() != null) { }
        });
        Assert.Contains("authentication failed", error.Message);
    }

    [Theory]
    [InlineData("Json", false)]
    [InlineData("Json", true)]
    [InlineData("Arrow", false)]
    [InlineData("Arrow", true)]
    public async Task ValidEnvelope_RoundTripsAndBindsTheChunkName(string format, bool compress)
    {
        using var store = CreateStore(format, compress);
        await using (var writer = await store.CreateWriterAsync("rows.tmp"))
        {
            await writer.WriteRowAsync(new Row { ["amount"] = 7.25m, ["label"] = "café Δ", ["flag"] = true });
            await writer.WriteRowAsync(new Row { ["amount"] = 8.5m, ["label"] = "second", ["flag"] = false });
        }
        await using (var reader = await store.CreateReaderAsync("rows.tmp"))
        {
            var first = await reader.ReadRowAsync();
            Assert.NotNull(first);
            Assert.Equal(7.25m, first["amount"]);
            Assert.Equal("café Δ", first["label"]);
            Assert.Equal(true, first["flag"]);
            Assert.Equal(8.5m, (await reader.ReadRowAsync())["amount"]);
            Assert.Null(await reader.ReadRowAsync());
        }

        var bytes = await File.ReadAllBytesAsync(Path.Combine(store.RootPath, "rows.tmp"), CancellationToken.None);
        Assert.Equal("ETLSPL03", Encoding.ASCII.GetString(bytes, 0, 8));
        await File.WriteAllBytesAsync(Path.Combine(store.RootPath, "swapped.tmp"), bytes, CancellationToken.None);
        await Assert.ThrowsAsync<ExecutionException>(() => store.CreateReaderAsync("swapped.tmp"));
    }

    [Fact]
    public async Task SinglePassReader_StreamsMultiFramePayloadWithoutSeeking()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var payload = RandomNumberGenerator.GetBytes(40_000); // > 4 frames at 8 KiB
        using var backing = new MemoryStream();
        await using (var writer = AuthenticatedSpillEncryption.CreateWriter(
            backing, key, "stream.chunk", codec: 2, compressed: false, CancellationToken.None, maxFramePlaintextBytes: 8_192))
        {
            await writer.WriteAsync(payload, CancellationToken.None);
            await writer.CompleteAsync();
        }

        var wireBytes = backing.ToArray();
        Assert.Equal("ETLSPL03", Encoding.ASCII.GetString(wireBytes, 0, 8));

        using var nonSeekable = new NonSeekableReadOnlyStream(wireBytes);
        await using var reader = await AuthenticatedSpillEncryption.CreateReaderAsync(
            nonSeekable, key, "stream.chunk", codec: 2, compressed: false, CancellationToken.None);

        using var recovered = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, CancellationToken.None)) > 0)
            recovered.Write(buffer, 0, read);

        Assert.Equal(payload, recovered.ToArray());
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("tag")]
    [InlineData("flags")]
    [InlineData("payloadLength")]
    public async Task MidStreamTampering_InLaterFrame_FailsClosedBeforeYieldingCorruptPlaintext(string kind)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wireBytes = await WriteMultiFrameEnvelopeAsync(key, "mid.chunk", framePlaintextBytes: 4_096, totalPlaintextBytes: 18_000);
        var frames = ParseFrames(wireBytes);
        Assert.True(frames.Count >= 4);

        var target = frames[2]; // mid-stream frame
        switch (kind)
        {
            case "ciphertext":
                wireBytes[target.Offset + AuthenticatedSpillEncryption.FrameHeaderSizeV3 + AuthenticatedSpillEncryption.TagSizeV3 + 10] ^= 0xFF;
                break;
            case "tag":
                wireBytes[target.Offset + AuthenticatedSpillEncryption.FrameHeaderSizeV3 + 5] ^= 0xFF;
                break;
            case "flags":
                wireBytes[target.Offset + 8] |= 0x02; // reserved bit
                break;
            case "payloadLength":
                BinaryPrimitives.WriteUInt32BigEndian(wireBytes.AsSpan(target.Offset + 9, 4), (uint)(target.PayloadLength + 1));
                break;
        }

        using var stream = new MemoryStream(wireBytes);
        // Frame 0 is valid so opening succeeds; reading through Frame 2 must fail closed.
        await using var reader = await AuthenticatedSpillEncryption.CreateReaderAsync(
            stream, key, "mid.chunk", codec: 2, compressed: false, CancellationToken.None);

        var buffer = new byte[4_096];
        var totalRead = 0;
        var error = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer, CancellationToken.None);
                if (read == 0) break;
                totalRead += read;
            }
        });
        Assert.Contains("authentication failed", error.Message);
        Assert.True(totalRead <= 8_192, $"Expected fail-closed before yielding frame 2 plaintext, read {totalRead} bytes.");
    }

    [Theory]
    [InlineData("swap")]
    [InlineData("duplicate")]
    [InlineData("skip")]
    public async Task FrameReorderingOrDuplication_FailsClosed(string mutation)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wireBytes = await WriteMultiFrameEnvelopeAsync(key, "order.chunk", framePlaintextBytes: 4_096, totalPlaintextBytes: 18_000);
        var header = wireBytes[..AuthenticatedSpillEncryption.HeaderSizeV3];
        var frames = ParseFrames(wireBytes);
        Assert.True(frames.Count >= 4);

        byte[] SliceFrame(FrameInfo f) => wireBytes[f.Offset..(f.Offset + f.TotalBytes)];

        var mutated = new List<byte>(header);
        switch (mutation)
        {
            case "swap":
                mutated.AddRange(SliceFrame(frames[0]));
                mutated.AddRange(SliceFrame(frames[2]));
                mutated.AddRange(SliceFrame(frames[1]));
                for (var i = 3; i < frames.Count; i++) mutated.AddRange(SliceFrame(frames[i]));
                break;
            case "duplicate":
                mutated.AddRange(SliceFrame(frames[0]));
                mutated.AddRange(SliceFrame(frames[1]));
                mutated.AddRange(SliceFrame(frames[1]));
                for (var i = 2; i < frames.Count; i++) mutated.AddRange(SliceFrame(frames[i]));
                break;
            case "skip":
                mutated.AddRange(SliceFrame(frames[0]));
                for (var i = 2; i < frames.Count; i++) mutated.AddRange(SliceFrame(frames[i]));
                break;
        }

        await AssertStreamFailsAuthenticationAsync(mutated.ToArray(), key, "order.chunk");
    }

    [Fact]
    public async Task PrefixTruncation_And_AbortedWriterWithoutFinalMarker_FailClosed()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wireBytes = await WriteMultiFrameEnvelopeAsync(key, "prefix.chunk", framePlaintextBytes: 4_096, totalPlaintextBytes: 18_000);
        var frames = ParseFrames(wireBytes);
        Assert.True(frames.Count >= 4);

        // Drop the final frame on an exact frame boundary so every present frame is authentic, yet IS_FINAL is missing.
        var withoutFinalFrame = wireBytes[..frames[^1].Offset];
        await AssertStreamFailsAuthenticationAsync(withoutFinalFrame, key, "prefix.chunk");

        // Also verify disposing a writer without CompleteAsync() produces an unreadable truncated chunk.
        using var abortedStream = new MemoryStream();
        var writer = AuthenticatedSpillEncryption.CreateWriter(
            abortedStream, key, "aborted.chunk", codec: 2, compressed: false, CancellationToken.None, maxFramePlaintextBytes: 4_096);
        await writer.WriteAsync(new byte[5_000], CancellationToken.None);
        await writer.DisposeAsync(); // Intentionally omit CompleteAsync()

        await AssertStreamFailsAuthenticationAsync(abortedStream.ToArray(), key, "aborted.chunk");
    }

    [Fact]
    public async Task CrossChunkFrameSplicing_SameSessionKey_FailsClosed()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var bytesA = await WriteMultiFrameEnvelopeAsync(key, "chunkA.tmp", framePlaintextBytes: 4_096, totalPlaintextBytes: 12_000);
        var bytesB = await WriteMultiFrameEnvelopeAsync(key, "chunkB.tmp", framePlaintextBytes: 4_096, totalPlaintextBytes: 12_000);

        var framesA = ParseFrames(bytesA);
        var framesB = ParseFrames(bytesB);
        Assert.True(framesA.Count >= 3 && framesB.Count >= 3);

        // Splice authentic Frame 1 from chunkB into chunkA while preserving chunkA's header and frames 0 & 2.
        var spliced = new List<byte>(bytesA[..AuthenticatedSpillEncryption.HeaderSizeV3]);
        spliced.AddRange(bytesA[framesA[0].Offset..(framesA[0].Offset + framesA[0].TotalBytes)]);
        spliced.AddRange(bytesB[framesB[1].Offset..(framesB[1].Offset + framesB[1].TotalBytes)]);
        for (var i = 2; i < framesA.Count; i++)
            spliced.AddRange(bytesA[framesA[i].Offset..(framesA[i].Offset + framesA[i].TotalBytes)]);

        await AssertStreamFailsAuthenticationAsync(spliced.ToArray(), key, "chunkA.tmp");
    }

    [Theory]
    [InlineData("Json", false)]
    [InlineData("Json", true)]
    [InlineData("Arrow", false)]
    [InlineData("Arrow", true)]
    public async Task EmptyPartitionsRemainReadableButDeletedChunksFail(string format, bool compress)
    {
        using var store = CreateStore(format, compress);
        await using (await store.CreateWriterAsync("empty.tmp")) { }
        Assert.True(File.Exists(Path.Combine(store.RootPath, "empty.tmp")));
        await using (var empty = await store.CreateReaderAsync("empty.tmp"))
            Assert.Null(await empty.ReadRowAsync());
        await using (var writer = await store.CreateWriterAsync("rows.tmp"))
            await writer.WriteRowAsync(new Row { ["amount"] = 1m });
        File.Delete(Path.Combine(store.RootPath, "rows.tmp"));
        await Assert.ThrowsAsync<ExecutionException>(() => store.CreateReaderAsync("rows.tmp"));
    }

    [Theory]
    [InlineData("Json", false)]
    [InlineData("Json", true)]
    [InlineData("Arrow", false)]
    [InlineData("Arrow", true)]
    public async Task PersistentChunksRequireTheOriginalSessionKey(string format, bool compress)
    {
        var root = Path.Combine(Path.GetTempPath(), "spill-auth-" + Guid.NewGuid().ToString("N"));
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using (var first = CreateStore(format, compress, root, key))
            await using (var writer = await first.CreateWriterAsync("rows.tmp"))
                await writer.WriteRowAsync(new Row { ["amount"] = 1m });
            using (var reopened = CreateStore(format, compress, root, key))
            {
                await using var reader = await reopened.CreateReaderAsync("rows.tmp");
                Assert.Equal(1m, (await reader.ReadRowAsync())["amount"]);
            }
            using var wrongKey = CreateStore(format, compress, root, RandomNumberGenerator.GetBytes(32));
            await Assert.ThrowsAsync<ExecutionException>(() => wrongKey.CreateReaderAsync("rows.tmp"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyV2Envelope_RemainsReadableDuringCompatWindow()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("legacy-etlspl02-payload-0123456789abcdef");

        var header = new byte[58];
        "ETLSPL02"u8.CopyTo(header);
        header[8] = 2; // Arrow codec id
        header[9] = 0; // Uncompressed
        RandomNumberGenerator.Fill(header.AsSpan(10));

        var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 64, header.AsSpan(10, 32).ToArray(), "ETL-SQL spill v2"u8.ToArray());
        byte[] ciphertext;
        using (var aes = Aes.Create())
        {
            aes.Key = keys[..32];
            ciphertext = aes.EncryptCbc(plaintext, header[42..]);
        }

        var v2Envelope = new List<byte>(header);
        v2Envelope.AddRange(ciphertext);
        using (var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keys.AsSpan(32)))
        {
            var name = Encoding.UTF8.GetBytes("legacy.tmp");
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, name.Length);
            hmac.AppendData(header);
            hmac.AppendData(len);
            hmac.AppendData(name);
            hmac.AppendData(ciphertext);
            v2Envelope.AddRange(hmac.GetHashAndReset());
        }

        var rawV2 = v2Envelope.ToArray();
        using (var v2Stream = new MemoryStream(rawV2))
        {
            await using var reader = await AuthenticatedSpillEncryption.CreateReaderAsync(
                v2Stream, key, "legacy.tmp", codec: 2, compressed: false, CancellationToken.None);
            using var output = new MemoryStream();
            await reader.CopyToAsync(output, CancellationToken.None);
            Assert.Equal(plaintext, output.ToArray());
        }

        // Legacy v2 still fails closed on wrong chunk name or ciphertext tampering.
        await AssertStreamFailsAuthenticationAsync(rawV2, key, "wrong-legacy-name.tmp");
    }

    [Theory]
    [InlineData("Json", false)]
    [InlineData("Json", true)]
    [InlineData("Arrow", false)]
    [InlineData("Arrow", true)]
    public async Task LegacyUnauthenticatedEnvelopeIsRejected(string format, bool compress)
    {
        using var plain = CreateStore(format, compress, encrypt: false);
        await using (var writer = await plain.CreateWriterAsync("rows.tmp"))
            await writer.WriteRowAsync(new Row { ["amount"] = 1m, ["label"] = "legacy payload" });
        var payload = await File.ReadAllBytesAsync(Path.Combine(plain.RootPath, "rows.tmp"), CancellationToken.None);
        using var aes = Aes.Create();
        var legacy = aes.IV.Concat(aes.EncryptCbc(payload, aes.IV)).ToArray();
        using var secured = CreateStore(format, compress);
        await File.WriteAllBytesAsync(Path.Combine(secured.RootPath, "rows.tmp"), legacy, CancellationToken.None);
        await Assert.ThrowsAsync<ExecutionException>(() => secured.CreateReaderAsync("rows.tmp"));
    }

    private static async Task<byte[]> WriteMultiFrameEnvelopeAsync(
        byte[] key,
        string chunkName,
        int framePlaintextBytes,
        int totalPlaintextBytes)
    {
        var payload = RandomNumberGenerator.GetBytes(totalPlaintextBytes);
        using var ms = new MemoryStream();
        await using var writer = AuthenticatedSpillEncryption.CreateWriter(
            ms, key, chunkName, codec: 2, compressed: false, CancellationToken.None, maxFramePlaintextBytes: framePlaintextBytes);
        await writer.WriteAsync(payload, CancellationToken.None);
        await writer.CompleteAsync();
        return ms.ToArray();
    }

    private static async Task AssertStreamFailsAuthenticationAsync(byte[] wireBytes, byte[] key, string chunkName)
    {
        var error = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            using var stream = new MemoryStream(wireBytes);
            await using var reader = await AuthenticatedSpillEncryption.CreateReaderAsync(
                stream, key, chunkName, codec: 2, compressed: false, CancellationToken.None);
            var buf = new byte[4096];
            while (await reader.ReadAsync(buf, CancellationToken.None) > 0) { }
        });
        Assert.Contains("authentication failed", error.Message);
    }

    private static int FindLastFrameStartOffset(byte[] bytes) => ParseFrames(bytes)[^1].Offset;

    private static int FindLastFrameTagOffset(byte[] bytes) =>
        ParseFrames(bytes)[^1].Offset + AuthenticatedSpillEncryption.FrameHeaderSizeV3;

    private readonly record struct FrameInfo(int Offset, uint PayloadLength, int TotalBytes);

    private static List<FrameInfo> ParseFrames(byte[] bytes)
    {
        var frames = new List<FrameInfo>();
        var pos = AuthenticatedSpillEncryption.HeaderSizeV3;
        while (pos + AuthenticatedSpillEncryption.FrameHeaderSizeV3 <= bytes.Length)
        {
            var payloadLen = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 9, 4));
            var totalBytes = AuthenticatedSpillEncryption.FrameHeaderSizeV3 + AuthenticatedSpillEncryption.TagSizeV3 + (int)payloadLen;
            frames.Add(new FrameInfo(pos, payloadLen, totalBytes));
            pos += totalBytes;
        }
        return frames;
    }

    private sealed class NonSeekableReadOnlyStream(byte[] data) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException("Single-pass reader must not inspect Length.");
        public override long Position
        {
            get => throw new NotSupportedException("Single-pass reader must not inspect Position.");
            set => throw new NotSupportedException("Single-pass reader must not seek.");
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = data.Length - _offset;
            if (remaining <= 0 || buffer.IsEmpty) return ValueTask.FromResult(0);
            var toCopy = Math.Min(buffer.Length, Math.Min(remaining, 1_500)); // force partial reads across frame boundaries
            data.AsSpan(_offset, toCopy).CopyTo(buffer.Span);
            _offset += toCopy;
            return ValueTask.FromResult(toCopy);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Single-pass reader must not seek.");
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static SpillStore CreateStore(string format, bool compress, string root = null, byte[] key = null, bool encrypt = true)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupGet(value => value.AdaptiveMetrics).Returns(new global::ETL_SQL.Core.Adaptive.AdaptiveRuntimeMetrics());
        context.SetupProperty(value => value.SpillFormat, format);
        context.SetupProperty(value => value.SpillEncryptionEnabled, encrypt);
        context.SetupProperty(value => value.SpillCompressionEnabled, compress);
        context.SetupGet(value => value.SessionRoot).Returns(root ?? Path.GetTempPath());
        if (root == null) context.SetupGet(value => value.SessionStateManager).Returns(new NullSessionStateManager());
        else
        {
            context.SetupGet(value => value.IsPersistentSession).Returns(true);
            context.SetupGet(value => value.SessionId).Returns("authentication-session");
            var manager = new Mock<ISessionStateManager>();
            manager.Setup(value => value.GetSpillKey(It.IsAny<string>())).Returns(() => key.ToArray());
            context.SetupGet(value => value.SessionStateManager).Returns(manager.Object);
        }
        return new SpillStore(context.Object);
    }
}
