using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Adaptive;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using ETL_SQL.Engine.Spill;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

/// <summary>
/// SP02 regressions proving plaintext scratch/packed buffers are zeroed on return/disposal,
/// oversized scratch buffers are bounded rather than permanently pinned, and spill session
/// keys are wiped on store disposal.
/// </summary>
public sealed class SpillBufferHygieneTests
{
    [Fact]
    public void RowPacker_ClearsPlaintextScratchBetweenPacksAndOnDispose()
    {
        const string secret = "SECRET_TOKEN_ALPHA_99182";
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var cols = new[] { "Id", "SecretPayload" };

        using var packer = new RowPacker();
        var packed = packer.Pack(new Row { ["Id"] = 1m, ["SecretPayload"] = secret }, cols);

        // The returned blob retains the encoded row for the consumer, while reused scratch must be wiped.
        var scratchAfterPack = packer.GetBackingBufferSnapshotForTests();
        Assert.Equal(-1, scratchAfterPack.AsSpan().IndexOf(secretBytes));

        var unpacked = RowPacker.Unpack(packed, cols);
        Assert.Equal(1m, unpacked["Id"]);
        Assert.Equal(secret, unpacked["SecretPayload"]);

        packer.Dispose();
        Assert.Equal(0, packer.RetainedCapacity);
        Assert.Throws<ObjectDisposedException>(() => packer.Pack(new Row { ["Id"] = 2m, ["SecretPayload"] = "x" }, cols));
    }

    [Fact]
    public void RowPacker_BoundsRetainedScratchCapacityAfterOversizedRow()
    {
        var cols = new[] { "Id", "Wide" };
        var wideValue = new string('X', RowPacker.MaxRetainedBufferBytes + 16_384);

        using var packer = new RowPacker();
        var wideBlob = packer.Pack(new Row { ["Id"] = 7m, ["Wide"] = wideValue }, cols);

        Assert.True(packer.RetainedCapacity <= RowPacker.MaxRetainedBufferBytes,
            $"Expected retained scratch capacity <= {RowPacker.MaxRetainedBufferBytes}, got {packer.RetainedCapacity}.");

        var restored = RowPacker.Unpack(wideBlob, cols);
        Assert.Equal(7m, restored["Id"]);
        Assert.Equal(wideValue, restored["Wide"]);
    }

    [Fact]
    public async Task SpillStore_DisposeZeroesCachedSessionKey()
    {
        var mockContext = new Mock<IExecutionContext>();
        mockContext.SetupGet(value => value.AdaptiveMetrics).Returns(new AdaptiveRuntimeMetrics());
        mockContext.SetupProperty(c => c.SpillEncryptionEnabled, true);
        mockContext.SetupProperty(c => c.SpillCompressionEnabled, true);
        mockContext.Setup(c => c.SessionStateManager).Returns(new NullSessionStateManager());
        mockContext.Setup(c => c.SessionRoot).Returns(Path.GetTempPath());

        byte[]? capturedKeyRef;
        using (var store = new SpillStore(mockContext.Object))
        {
            await using (var writer = await store.CreateWriterAsync("hygiene_key_check.tmp"))
            {
                await writer.WriteRowAsync(new Row { ["k"] = "v" });
            }

            capturedKeyRef = typeof(SpillStore)
                .GetField("_cachedSessionKey", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(store) as byte[];
            Assert.NotNull(capturedKeyRef);
            Assert.Contains(capturedKeyRef!, b => b != 0);
        }

        Assert.All(capturedKeyRef!, b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public async Task SpilledJoinAndDistinct_PreserveCorrectResultsWithPackedHygiene()
    {
        var provider = ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var sec = scope.ServiceProvider.GetRequiredService<SecurityService>();
        sec.IsTestMode = true;

        var evaluator = scope.ServiceProvider.GetRequiredService<ETL_SQL.Engine.Evaluator>();
        evaluator.JoinSpillThreshold = 3;
        evaluator.ExternalHashPartitions = 2;

        const string setupSql = """
            CREATE TABLE #left_items (Id INT, Tag VARCHAR);
            CREATE TABLE #right_items (Id INT, Label VARCHAR);
            INSERT INTO #left_items VALUES (1, 'a'), (2, 'b'), (2, 'b'), (3, 'c'), (4, 'd'), (5, 'e'), (6, 'f');
            INSERT INTO #right_items VALUES (2, 'B2'), (4, 'D4'), (6, 'F6'), (8, 'H8');
            """;

        var setupAst = new Parser(new Lexer(setupSql).Tokenize(), setupSql).Parse();
        Assert.Empty(setupAst.Diagnostics);
        await evaluator.Evaluate(setupAst);

        const string querySql = """
            SELECT DISTINCT l.Id, r.Label
            FROM #left_items l
            INNER JOIN #right_items r ON l.Id = r.Id
            ORDER BY l.Id;
            """;
        var queryAst = new Parser(new Lexer(querySql).Tokenize(), querySql).Parse();
        Assert.Empty(queryAst.Diagnostics);

        var rows = new List<Row>();
        await foreach (var batch in evaluator.ExecuteQuery(queryAst.Statements[0]))
            rows.AddRange(batch.Rows);

        Assert.Equal(3, rows.Count);
        Assert.Equal("B2", rows[0]["Label"]);
        Assert.Equal("D4", rows[1]["Label"]);
        Assert.Equal("F6", rows[2]["Label"]);
    }
}
