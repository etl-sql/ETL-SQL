using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Npgsql;
using Xunit;

namespace ETL_SQL.Tests.Integration;

/// <summary>
/// Live round trip for the POSTGRES text COPY write path. DB07 measured binary COPY against this
/// loader (104 ms vs 92 ms on 25,000 rows) and was not adopted, so these tests pin the text path.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Connector", "POSTGRES")]
[Collection("Database collection")]
public sealed class PostgresCopyRoundTripTests
{
    private readonly DatabaseFixture _fixture;

    public PostgresCopyRoundTripTests(DatabaseFixture fixture) => _fixture = fixture;

    private static async IAsyncEnumerable<DataTable> Batches(params DataTable[] tables)
    {
        foreach (var t in tables) yield return t;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<DataTable> FailAfter(DataTable first)
    {
        yield return first;
        await Task.CompletedTask;
        throw new InvalidOperationException("upstream source failed");
    }

    private static async Task<long> CountAsync(NpgsqlConnection admin, string table)
    {
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM public.{table};", admin);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task TextCopy_RoundTripsTypes_LoadsEnums_AndCommitsNothingOnFailure()
    {
        var connStr = _fixture.PostgresConnectionString;
        var context = SystemExecutionContext.Instance;
        var suffix = Guid.NewGuid().ToString("N");
        var table = "copy_rt_" + suffix;
        var enumType = "copy_mood_" + suffix;
        var enumTable = "copy_enum_" + suffix;

        await using var admin = new NpgsqlConnection(connStr);
        await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand($@"
CREATE TABLE public.{table} (id INT, txt TEXT, num NUMERIC(18,4), ts TIMESTAMPTZ, dt DATE, uid UUID, flag BOOLEAN, raw BYTEA);
CREATE TYPE public.{enumType} AS ENUM ('happy', 'sad');
CREATE TABLE public.{enumTable} (id INT, mood public.{enumType});", admin))
            {
                await create.ExecuteNonQueryAsync();
            }

            var ds = new PostgresDataSource(context, connStr, $"public.{table}");
            var guid = Guid.Parse("11112222-3333-4444-5555-666677778888");
            var ts = new DateTimeOffset(2026, 11, 24, 10, 30, 45, TimeSpan.Zero);
            var date = new DateOnly(2026, 11, 24);
            var bytes = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF };
            const string tricky = "col1\tcol2\nline2\r\nC:\\new\\path";

            // 1. Tabs, newlines, backslashes, a literal \N string, typed values and NULLs.
            var batch = new DataTable();
            batch.SetColumns(["id", "txt", "num", "ts", "dt", "uid", "flag", "raw"]);
            await batch.AddRowAsync(new Row
            {
                ["id"] = 1m,
                ["txt"] = tricky,
                ["num"] = 12345.6789m,
                ["ts"] = ts,
                ["dt"] = date,
                ["uid"] = guid,
                ["flag"] = true,
                ["raw"] = bytes
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
            await ds.WriteBatches(Batches(batch), append: false);

            await using (var verify = new NpgsqlCommand($"SELECT id, txt, num, ts, dt, uid, flag, raw FROM public.{table} ORDER BY id;", admin))
            await using (var reader = await verify.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.Equal(tricky, reader.GetString(1));
                Assert.Equal(12345.6789m, reader.GetDecimal(2));
                Assert.Equal(ts.UtcDateTime, reader.GetDateTime(3).ToUniversalTime());
                Assert.Equal(date, reader.GetFieldValue<DateOnly>(4));
                Assert.Equal(guid, reader.GetGuid(5));
                Assert.True(reader.GetBoolean(6));
                Assert.Equal(bytes, (byte[])reader.GetValue(7));

                Assert.True(await reader.ReadAsync());
                Assert.Equal("\\N", reader.GetString(1));
                for (int i = 2; i <= 7; i++) Assert.True(reader.IsDBNull(i));
                Assert.False(await reader.ReadAsync());
            }

            // 2. A bad value on row 2 fails the whole COPY; the message names the line and column.
            var bad = new DataTable();
            bad.SetColumns(["id", "txt", "uid"]);
            await bad.AddRowAsync(new Row { ["id"] = 10m, ["txt"] = "ok", ["uid"] = guid });
            await bad.AddRowAsync(new Row { ["id"] = 11m, ["txt"] = "bad", ["uid"] = "not-a-valid-uuid" });
            var badEx = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(Batches(bad), append: true));
            Assert.Contains("line 2", badEx.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uid", badEx.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2L, await CountAsync(admin, table));

            // 3. The source fails after rows were already sent: the COPY is cancelled, not completed.
            var partial = new DataTable();
            partial.SetColumns(["id", "txt"]);
            await partial.AddRowAsync(new Row { ["id"] = 20m, ["txt"] = "sent before the failure" });
            await Assert.ThrowsAnyAsync<Exception>(() => ds.WriteBatches(FailAfter(partial), append: true));
            Assert.Equal(2L, await CountAsync(admin, table));

            // 4. Enum columns load through the server's input function.
            var enumDs = new PostgresDataSource(context, connStr, $"public.{enumTable}");
            var enumBatch = new DataTable();
            enumBatch.SetColumns(["id", "mood"]);
            await enumBatch.AddRowAsync(new Row { ["id"] = 1m, ["mood"] = "happy" });
            await enumDs.WriteBatches(Batches(enumBatch), append: true);
            Assert.Equal(1L, await CountAsync(admin, enumTable));

            // 5. Rollback discards COPY writes made inside a transaction.
            await using (var txDs = new PostgresDataSource(context, connStr, $"public.{table}"))
            {
                await txDs.BeginTransactionAsync();
                var tx = new DataTable();
                tx.SetColumns(["id", "txt"]);
                await tx.AddRowAsync(new Row { ["id"] = 99m, ["txt"] = "rolled-back" });
                await txDs.WriteBatches(Batches(tx), append: true);
                await txDs.RollbackAsync();
            }
            Assert.Equal(2L, await CountAsync(admin, table));
        }
        finally
        {
            await using var drop = new NpgsqlCommand(
                $"DROP TABLE IF EXISTS public.{table}; DROP TABLE IF EXISTS public.{enumTable}; DROP TYPE IF EXISTS public.{enumType};",
                admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
