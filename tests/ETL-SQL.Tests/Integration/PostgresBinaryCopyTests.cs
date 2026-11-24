using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Connector", "POSTGRES")]
[Collection("Database collection")]
public sealed class PostgresBinaryCopyTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PostgresBinaryCopyTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static async IAsyncEnumerable<DataTable> SingleBatch(DataTable t)
    {
        yield return t;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task BinaryCopy_RoundTripAllTypes_AbortsOnBadValueAndUnmappableType_AndMeasuresVsTextCopy()
    {
        var connStr = _fixture.PostgresConnectionString;
        var context = SystemExecutionContext.Instance;
        var tableName = "db07_bin_" + Guid.NewGuid().ToString("N");
        var enumType = "db07_mood_" + Guid.NewGuid().ToString("N");
        var enumTable = "db07_enum_" + Guid.NewGuid().ToString("N");
        var benchTable = "db07_bench_" + Guid.NewGuid().ToString("N");

        await using var admin = new NpgsqlConnection(connStr);
        await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($@"
CREATE TABLE public.{tableName} (
    id INT,
    txt TEXT,
    num NUMERIC(18,4),
    ts TIMESTAMPTZ,
    dt DATE,
    uid UUID,
    flag BOOLEAN,
    raw BYTEA
);
CREATE TYPE public.{enumType} AS ENUM ('happy', 'sad');
CREATE TABLE public.{enumTable} (
    id INT,
    mood public.{enumType}
);
CREATE TABLE public.{benchTable} (
    id INT,
    label TEXT,
    amount NUMERIC(18,2),
    active BOOLEAN,
    created_at TIMESTAMPTZ
);", admin))
            {
                await create.ExecuteNonQueryAsync();
            }

            var ds = new PostgresDataSource(context, connStr, $"public.{tableName}");
            var expectedGuid = Guid.Parse("11112222-3333-4444-5555-666677778888");
            var expectedTs = new DateTimeOffset(2026, 11, 24, 10, 30, 45, TimeSpan.Zero);
            var expectedDate = new DateOnly(2026, 11, 24);
            var expectedBytes = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF };
            const string trickyText = "col1\tcol2\nline2\r\nC:\\new\\path";

            // 1. Round-trip all required types + tricky text + literal \N + NULLs
            var batch = new DataTable();
            batch.SetColumns(["id", "txt", "num", "ts", "dt", "uid", "flag", "raw"]);
            await batch.AddRowAsync(new Row
            {
                ["id"] = 1m,
                ["txt"] = trickyText,
                ["num"] = 12345.6789m,
                ["ts"] = expectedTs,
                ["dt"] = expectedDate,
                ["uid"] = expectedGuid,
                ["flag"] = true,
                ["raw"] = expectedBytes
            });
            await batch.AddRowAsync(new Row
            {
                ["id"] = 2m,
                ["txt"] = "\\N",
                ["num"] = null,
                ["ts"] = null,
                ["dt"] = null,
                ["uid"] = null,
                ["flag"] = null,
                ["raw"] = null
            });

            await ds.WriteBatches(SingleBatch(batch), append: false);

            await using (var verify = new NpgsqlCommand($"SELECT id, txt, num, ts, dt, uid, flag, raw FROM public.{tableName} ORDER BY id;", admin))
            await using (var reader = await verify.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.Equal(trickyText, reader.GetString(1));
                Assert.Equal(12345.6789m, reader.GetDecimal(2));
                Assert.Equal(expectedTs.UtcDateTime, reader.GetDateTime(3).ToUniversalTime());
                Assert.Equal(expectedDate, reader.GetFieldValue<DateOnly>(4));
                Assert.Equal(expectedGuid, reader.GetGuid(5));
                Assert.True(reader.GetBoolean(6));
                Assert.Equal(expectedBytes, (byte[])reader.GetValue(7));

                Assert.True(await reader.ReadAsync());
                Assert.Equal(2, reader.GetInt32(0));
                Assert.Equal("\\N", reader.GetString(1));
                Assert.True(reader.IsDBNull(2));
                Assert.True(reader.IsDBNull(3));
                Assert.True(reader.IsDBNull(4));
                Assert.True(reader.IsDBNull(5));
                Assert.True(reader.IsDBNull(6));
                Assert.True(reader.IsDBNull(7));
                Assert.False(await reader.ReadAsync());
            }

            // 2. Bad value on row 2 aborts before Complete() so 0 new rows are committed, naming row + column
            var badBatch = new DataTable();
            badBatch.SetColumns(["id", "txt", "uid"]);
            await badBatch.AddRowAsync(new Row { ["id"] = 10m, ["txt"] = "ok", ["uid"] = expectedGuid });
            await badBatch.AddRowAsync(new Row { ["id"] = 11m, ["txt"] = "bad", ["uid"] = "not-a-valid-uuid" });

            var badEx = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(SingleBatch(badBatch), append: true));
            Assert.Contains("row 2", badEx.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uid", badEx.Message, StringComparison.OrdinalIgnoreCase);

            await using (var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM public.{tableName};", admin))
            {
                Assert.Equal(2L, Convert.ToInt64(await countCmd.ExecuteScalarAsync()));
            }

            // 3. Unmappable custom enum type fails before committing anything
            var enumDs = new PostgresDataSource(context, connStr, $"public.{enumTable}");
            var enumBatch = new DataTable();
            enumBatch.SetColumns(["id", "mood"]);
            await enumBatch.AddRowAsync(new Row { ["id"] = 1m, ["mood"] = "happy" });

            var unmappableEx = await Assert.ThrowsAsync<ExecutionException>(() => enumDs.WriteBatches(SingleBatch(enumBatch), append: true));
            Assert.Contains("mood", unmappableEx.Message, StringComparison.OrdinalIgnoreCase);

            // 4. Transaction rollback discards binary COPY writes
            await using (var txDs = new PostgresDataSource(context, connStr, $"public.{tableName}"))
            {
                await txDs.BeginTransactionAsync();
                var txBatch = new DataTable();
                txBatch.SetColumns(["id", "txt"]);
                await txBatch.AddRowAsync(new Row { ["id"] = 99m, ["txt"] = "rolled-back" });
                await txDs.WriteBatches(SingleBatch(txBatch), append: true);
                await txDs.RollbackAsync();
            }
            await using (var countAfterRollback = new NpgsqlCommand($"SELECT COUNT(*) FROM public.{tableName};", admin))
            {
                Assert.Equal(2L, Convert.ToInt64(await countAfterRollback.ExecuteScalarAsync()));
            }

            // 5. Measured comparison against text COPY on a fixed 25,000-row fixture
            const int fixtureRows = 25000;
            var benchBatch = new DataTable();
            benchBatch.SetColumns(["id", "label", "amount", "active", "created_at"]);
            for (int i = 0; i < fixtureRows; i++)
            {
                await benchBatch.AddRowAsync(new Row
                {
                    ["id"] = (decimal)i,
                    ["label"] = $"row-{i}\twith\\slash",
                    ["amount"] = 100.25m + (i % 100),
                    ["active"] = (i % 2) == 0,
                    ["created_at"] = expectedTs
                });
            }

            var benchDs = new PostgresDataSource(context, connStr, $"public.{benchTable}");
            var warmBatch = new DataTable();
            warmBatch.SetColumns(["id", "label", "amount", "active", "created_at"]);
            for (int i = 0; i < 100; i++)
                await warmBatch.AddRowAsync(benchBatch.Rows[i]);
            await benchDs.WriteBatchesTextCopyAsync(SingleBatch(warmBatch), append: false);
            await benchDs.WriteBatches(SingleBatch(warmBatch), append: false);

            var swText = Stopwatch.StartNew();
            await benchDs.WriteBatchesTextCopyAsync(SingleBatch(benchBatch), append: false);
            swText.Stop();

            var swBinary = Stopwatch.StartNew();
            await benchDs.WriteBatches(SingleBatch(benchBatch), append: false);
            swBinary.Stop();

            _output.WriteLine($"DB07 benchmark ({fixtureRows} rows, 5 cols): text COPY = {swText.ElapsedMilliseconds} ms, binary COPY = {swBinary.ElapsedMilliseconds} ms");
            await using (var benchCount = new NpgsqlCommand($"SELECT COUNT(*) FROM public.{benchTable};", admin))
            {
                Assert.Equal((long)fixtureRows, Convert.ToInt64(await benchCount.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand(
                $"DROP TABLE IF EXISTS public.{tableName}; DROP TABLE IF EXISTS public.{enumTable}; DROP TYPE IF EXISTS public.{enumType}; DROP TABLE IF EXISTS public.{benchTable};",
                admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
