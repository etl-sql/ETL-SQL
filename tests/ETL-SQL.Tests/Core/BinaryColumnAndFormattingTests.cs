using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class BinaryColumnAndFormattingTests
{
    private sealed class CapturingOutputSink : ResultFormatter.IResultOutputSink
    {
        public List<string> Lines { get; } = new();
        public void WriteLine(string text) => Lines.Add(text);
        public ConsoleKeyInfo ReadKey(bool intercept) => default;
    }

    [Fact]
    public void BinaryColumnBuffer_RoundTripsAndHandlesNullsCompactGatherClone()
    {
        byte[][] expected = [[0xDE, 0xAD, 0xBE, 0xEF], [], [0x00, 0x01, 0xFF]];
        using var buffer = BinaryColumnBuffer.FromByteArrays([expected[0], null, expected[1], expected[2]]);

        Assert.Equal(typeof(byte[]), buffer.ElementType);
        Assert.Equal(4, buffer.Count);
        Assert.False(buffer.IsNull(0));
        Assert.True(buffer.IsNull(1));
        Assert.False(buffer.IsNull(2));
        Assert.False(buffer.IsNull(3));

        Assert.Equal(expected[0], buffer.GetBytes(0).ToArray());
        Assert.Null(buffer.GetBoxedValue(1));
        Assert.Equal(expected[1], (byte[])buffer.GetBoxedValue(2)!);
        Assert.Equal(expected[2], (byte[])buffer.GetBoxedValue(3)!);

        using var compacted = buffer.Compact([3, 1, 0], 4);
        Assert.Equal(3, compacted.Count);
        Assert.Equal(expected[2], compacted.GetBytes(0).ToArray());
        Assert.True(compacted.IsNull(1));
        Assert.Equal(expected[0], compacted.GetBytes(2).ToArray());

        using var gathered = buffer.Gather([-1, 0, 2], 4);
        Assert.Equal(3, gathered.Count);
        Assert.True(gathered.IsNull(0));
        Assert.Equal(expected[0], gathered.GetBytes(1).ToArray());
        Assert.Equal(expected[1], gathered.GetBytes(2).ToArray());

        using var cloned = buffer.Clone();
        Assert.Equal(4, cloned.Count);
        Assert.True(cloned.IsNull(1));
        Assert.Equal(expected[0], cloned.GetBytes(0).ToArray());
    }

    [Fact]
    public async Task ColumnBatchAdapter_SupportsByteArrayColumns()
    {
        var table = new DataTable();
        table.SetColumns(["Id", "Hash"]);

        var r1 = table.NewRow();
        r1["Id"] = 1m;
        r1["Hash"] = new byte[] { 0x0A, 0xBC, 0x12 };
        await table.AddRowAsync(r1);

        var r2 = table.NewRow();
        r2["Id"] = 2m;
        r2["Hash"] = null;
        await table.AddRowAsync(r2);

        using var batch = ColumnBatchAdapter.FromDataTable(table);
        Assert.Equal("VARBINARY", batch.Schema.Fields[1].LogicalType);
        Assert.Equal(typeof(byte[]), batch.Schema.Fields[1].ElementType);
        Assert.IsType<BinaryColumnBuffer>(batch.Columns[1]);

        var roundTripped = ColumnBatchAdapter.ToDataTable(batch);
        Assert.Equal(new byte[] { 0x0A, 0xBC, 0x12 }, (byte[])roundTripped.Rows[0]["Hash"]!);
        Assert.Null(roundTripped.Rows[1]["Hash"]);
    }

    [Fact]
    public async Task ArrowSpill_RoundTripsBinaryTempTableAndExternalSort()
    {
        await using var services = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = services.GetRequiredService<Evaluator>();
        evaluator.SpillFormat = "Arrow";
        evaluator.TelemetryEnabled = true;
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;

        const string sql = """
            SET TEMP_TABLE_SPILL_THRESHOLD = 2;
            SET EXTERNAL_SORT_CHUNK_SIZE = 2;
            CREATE CONNECTION demo AS MOCKDB();
            SELECT TOP 6
                UserID,
                CASE WHEN UserID = 2 THEN NULL ELSE HASHBYTES('SHA256', Email) END AS EmailHash
            INTO #hashes
            FROM demo.Users
            ORDER BY UserID;
            SELECT UserID, EmailHash FROM #hashes ORDER BY UserID DESC;
            """;

        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());
        var result = Assert.IsType<DataTable>(evaluator.LastResult);
        Assert.Equal(6, result.Rows.Count);
        Assert.Equal(6m, result.Rows[0]["UserID"]);
        Assert.Equal(32, Assert.IsType<byte[]>(result.Rows[0]["EmailHash"]).Length);
        Assert.Equal(2m, result.Rows[4]["UserID"]);
        Assert.Null(result.Rows[4]["EmailHash"]);
        Assert.True(evaluator.Telemetry.TotalSpilledBytes > 0);
    }

    [Fact]
    public async Task RowPacker_RoundTripsBinaryValues()
    {
        var table = new DataTable();
        table.SetColumns(["Id", "Digest"]);
        var r1 = table.NewRow();
        r1["Id"] = 1m;
        r1["Digest"] = new byte[] { 0x01, 0x23, 0x45, 0x67 };
        await table.AddRowAsync(r1);

        using var packer = new RowPacker();
        var packed = packer.Pack(r1, ["Id", "Digest"]);
        var unpacked = RowPacker.Unpack(packed, ["Id", "Digest"]);

        Assert.Equal(1m, unpacked["Id"]);
        Assert.Equal(new byte[] { 0x01, 0x23, 0x45, 0x67 }, (byte[])unpacked["Digest"]!);
    }

    [Fact]
    public void TypeConverter_FormatsAndParsesHexBinary()
    {
        byte[] sample = [0x0A, 0xFE, 0x00, 0x99];
        var hex = TypeConverter.ToInvariantText(sample);
        Assert.Equal("0x0afe0099", hex);

        var roundTripped = Assert.IsType<byte[]>(TypeConverter.Cast(hex!, "VARBINARY"));
        Assert.Equal(sample, roundTripped);
    }

    [Fact]
    public async Task ResultFormatter_RendersBinaryAsHexInPlainTableAndJson()
    {
        var table = new DataTable();
        table.SetColumns(["Name", "Hash"]);
        var r1 = table.NewRow();
        r1["Name"] = "alice";
        r1["Hash"] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        await table.AddRowAsync(r1);

        var r2 = table.NewRow();
        r2["Name"] = "bob";
        r2["Hash"] = null;
        await table.AddRowAsync(r2);

        var prevSink = ResultFormatter.OutputSink;
        var prevJson = ResultFormatter.IsJsonMode;
        var prevSuppress = ResultFormatter.SuppressOutput;
        try
        {
            var sink = new CapturingOutputSink();
            ResultFormatter.OutputSink = sink;
            ResultFormatter.SuppressOutput = false;

            ResultFormatter.IsJsonMode = false;
            ResultFormatter.PrintBatch(table, true);
            Assert.Single(sink.Lines);
            Assert.Contains("0xdeadbeef", sink.Lines[0]);
            Assert.DoesNotContain("System.Byte[]", sink.Lines[0]);
            Assert.Contains("NULL", sink.Lines[0]);

            sink.Lines.Clear();
            ResultFormatter.IsJsonMode = true;
            ResultFormatter.PrintBatch(table, true);
            Assert.Single(sink.Lines);
            using var doc = JsonDocument.Parse(sink.Lines[0]);
            var rows = doc.RootElement.GetProperty("rows");
            Assert.Equal("0xdeadbeef", rows[0].GetProperty("Hash").GetString());
            Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("Hash").ValueKind);
        }
        finally
        {
            ResultFormatter.OutputSink = prevSink;
            ResultFormatter.IsJsonMode = prevJson;
            ResultFormatter.SuppressOutput = prevSuppress;
        }
    }

    [Fact]
    public async Task HashBytesIntoTempTable_ConvertsToColumnBatchWithoutError()
    {
        const string sql = """
            CREATE CONNECTION demo AS MOCKDB();
            SELECT TOP 5
                UserID,
                MASK_EMAIL(Email) AS MaskedEmail,
                HASHBYTES('SHA256', Email) AS EmailHash
            INTO #anonymized
            FROM demo.Users
            ORDER BY UserID;
            SELECT UserID, MaskedEmail, EmailHash FROM #anonymized ORDER BY UserID;
            """;

        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        await evaluator.Evaluate(script);

        var result = Assert.IsType<DataTable>(evaluator.LastResult);
        Assert.Equal(5, result.Rows.Count);
        var firstHash = Assert.IsType<byte[]>(result.Rows[0]["EmailHash"]);
        Assert.Equal(32, firstHash.Length);

        using var batch = ColumnBatchAdapter.FromDataTable(result);
        Assert.Equal(5, batch.RowCount);
        Assert.IsType<BinaryColumnBuffer>(batch.Columns[2]);
    }
}
