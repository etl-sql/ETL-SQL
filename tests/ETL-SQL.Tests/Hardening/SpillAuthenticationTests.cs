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
                foreach (var damage in new[] { "magic", "codec", "compression", "salt", "iv", "ciphertext", "tag", "truncated", "block-removed", "appended", "header-only" })
                    yield return [format, compress, damage];
    }

    [Theory]
    [MemberData(nameof(CorruptionCases))]
    public async Task Corruption_IsRejectedBeforeOpeningTheCodec(string format, bool compress, string damage)
    {
        using var store = CreateStore(format, compress);
        await using (var writer = await store.CreateWriterAsync("rows.tmp"))
            await writer.WriteRowAsync(new Row { ["amount"] = 1m, ["payload"] = new string('x', 200000) });
        var path = Path.Combine(store.RootPath, "rows.tmp");
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        switch (damage)
        {
            case "magic": bytes[0] ^= 1; break;
            case "codec": bytes[8] ^= 3; break;
            case "compression": bytes[9] ^= 1; break;
            case "salt": bytes[10] ^= 1; break;
            case "iv": bytes[42] ^= 1; break;
            case "ciphertext": bytes[^33] ^= 1; break;
            case "tag": bytes[^1] ^= 1; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "block-removed": bytes = [.. bytes[..58], .. bytes[74..]]; break;
            case "appended": bytes = [.. bytes, (byte)0]; break;
            case "header-only": bytes = bytes[..58]; break;
        }
        await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);
        var error = await Assert.ThrowsAsync<ExecutionException>(() => store.CreateReaderAsync("rows.tmp"));
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
        Assert.Equal("ETLSPL02", Encoding.ASCII.GetString(bytes, 0, 8));
        await File.WriteAllBytesAsync(Path.Combine(store.RootPath, "swapped.tmp"), bytes, CancellationToken.None);
        await Assert.ThrowsAsync<ExecutionException>(() => store.CreateReaderAsync("swapped.tmp"));
    }

    [Fact]
    public async Task Json_RejectsIvBitFlipBeforeReturningRows()
    {
        using var store = CreateStore("Json", compress: false);
        await using (var writer = await store.CreateWriterAsync("rows.tmp"))
            await writer.WriteRowAsync(new Row { ["amount"] = 1m });

        var path = Path.Combine(store.RootPath, "rows.tmp");
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        // The old CBC envelope's IV can change the first row's 1 to 9 without breaking JSON.
        var digitOffset = Encoding.UTF8.GetPreamble().Length + "{\"amount\":".Length;
        bytes[digitOffset] ^= 8;
        await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);

        await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await using var reader = await store.CreateReaderAsync("rows.tmp");
            await reader.ReadRowAsync();
        });
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

    private static SpillStore CreateStore(string format, bool compress, string root = null, byte[] key = null, bool encrypt = true)
    {
        var context = new Mock<IExecutionContext>();
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
