using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Data;
using ETL_SQL.Engine.Spill;
using ETL_SQL.Core.Common.Exceptions;
using System.Text.Json;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

[Trait("CompatBreak", "0.22")]
public class SpillScalarFidelityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task JsonSpilledQueryPreservesTextAndPredicateResults(bool encrypt, bool compress)
    {
        await using var services = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = services.GetRequiredService<Evaluator>();
        evaluator.SpillFormat = "Json";
        evaluator.SpillEncryptionEnabled = encrypt;
        evaluator.SpillCompressionEnabled = compress;
        evaluator.TelemetryEnabled = true;
        var script = """
            SET TEMP_TABLE_SPILL_THRESHOLD = 2;
            SET EXTERNAL_SORT_CHUNK_SIZE = 2;
            CREATE TABLE #codes (id INT, code VARCHAR(100));
            INSERT INTO #codes VALUES (1, '001'), (2, '1'), (3, '2026-10-25'), (4, '123.4500'), (5, '1e3');
            SELECT id, code FROM #codes ORDER BY id DESC;
            """;
        await evaluator.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
        var result = Assert.IsType<DataTable>(evaluator.LastResult);
        Assert.Equal(new[] { "1e3", "123.4500", "2026-10-25", "1", "001" },
            result.Rows.Select(row => Assert.IsType<string>(row["code"])));
        Assert.True(evaluator.Telemetry.TotalSpilledBytes > 0);

        await evaluator.Evaluate(new Parser(new Lexer("SELECT id, code FROM #codes WHERE LEN(code) = 3 AND code = '001';").Tokenize()).Parse());
        var row = Assert.Single(evaluator.LastResult.Rows);
        Assert.Equal(1m, row["id"]);
        Assert.Equal("001", Assert.IsType<string>(row["code"]));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task JsonPreservesScalarKindsAndValues(bool encrypt, bool compress, bool persistent)
    {
        object[] values = [null, "001", 123.4500m, decimal.MaxValue, double.Epsilon, double.NaN,
            double.PositiveInfinity, -0.0d, float.Epsilon, float.NegativeInfinity, true, false,
            byte.MaxValue, sbyte.MinValue, short.MinValue, ushort.MaxValue, int.MinValue,
            uint.MaxValue, long.MinValue, ulong.MaxValue, 'é',
            new DateTime(638969509231234567, DateTimeKind.Utc),
            new DateTime(638969509231234567, DateTimeKind.Unspecified),
            new DateTime(638969509231234567, DateTimeKind.Local),
            new DateTimeOffset(2026, 10, 25, 1, 2, 3, TimeSpan.FromMinutes(345)).AddTicks(1234567),
            TimeSpan.FromTicks(-123456789), new DateOnly(2026, 10, 25), new TimeOnly(1, 2, 3).Add(TimeSpan.FromTicks(7)),
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), new byte[] { 0, 1, 255 },
            JsonSerializer.SerializeToElement(new { text = "001", nested = new[] { "2026-10-25" } })];
        var context = new Mock<IExecutionContext>();
        context.SetupProperty(value => value.SpillFormat, "Json");
        context.SetupProperty(value => value.SpillEncryptionEnabled, encrypt);
        context.SetupProperty(value => value.SpillCompressionEnabled, compress);
        context.SetupGet(value => value.SessionStateManager).Returns(new NullSessionStateManager());
        context.SetupGet(value => value.IsPersistentSession).Returns(persistent);
        // Each persistent fixture owns a fresh directory; the production store owns its spill child.
        var root = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString());
        context.SetupGet(value => value.SessionRoot).Returns(root);
        context.SetupGet(value => value.SessionId).Returns("scalar-fidelity");
        var manager = new Mock<ISessionStateManager>();
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        manager.Setup(value => value.GetSpillKey(It.IsAny<string>())).Returns(() => key.ToArray());
        if (persistent) context.SetupGet(value => value.SessionStateManager).Returns(manager.Object);
        try
        {
            using (var store = new SpillStore(context.Object))
            {
                await using var writer = await store.CreateWriterAsync("scalars.tmp");
                foreach (var value in values) await writer.WriteRowAsync(new Row { ["value"] = value });
                if (!persistent)
                {
                    await writer.DisposeAsync();
                    await Verify(store);
                }
            }
            if (persistent)
            {
                using var reopened = new SpillStore(context.Object);
                await Verify(reopened);
                reopened.Cleanup();
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        async Task Verify(SpillStore store)
        {
            await using var reader = await store.CreateReaderAsync("scalars.tmp");
            var rows = await reader.AsEnumerableAsync().ToListAsync();
            Assert.Equal(values.Length, rows.Count);
            for (var index = 0; index < values.Length; index++)
            {
                var expected = values[index];
                var actual = rows[index]["value"];
                if (expected == null) { Assert.Null(actual); continue; }
                Assert.Equal(expected.GetType(), actual.GetType());
                if (expected is JsonElement json) Assert.Equal(json.GetRawText(), ((JsonElement)actual).GetRawText());
                else if (expected is DateTime date) Assert.Equal(date.ToBinary(), ((DateTime)actual).ToBinary());
                else if (expected is DateTimeOffset offset) Assert.True(offset.EqualsExact((DateTimeOffset)actual));
                else if (expected is decimal number) Assert.Equal(decimal.GetBits(number), decimal.GetBits((decimal)actual));
                else if (expected is double floating) Assert.Equal(BitConverter.DoubleToInt64Bits(floating), BitConverter.DoubleToInt64Bits((double)actual));
                else Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [InlineData("{\"value\":\"001\"}", false)]
    [InlineData("{\"value\":{\"Type\":\"string\",\"Value\":\"001\"}}", false)]
    [InlineData("ETLSQL-JSON-2", false)]
    [InlineData("null", true)]
    [InlineData("{\"value\":{\"Type\":\"System.Object\",\"Value\":\"001\"}}", true)]
    [InlineData("{\"value\":{\"Type\":\"decimal\",\"Value\":\"001\"}}", true)]
    [InlineData("{\"value\":null}", true)]
    public async Task JsonRejectsAmbiguousOrUnsupportedRows(string payload, bool typed)
    {
        var context = new Mock<IExecutionContext>();
        context.SetupProperty(value => value.SpillFormat, "Json");
        context.SetupGet(value => value.SessionStateManager).Returns(new NullSessionStateManager());
        using var store = new SpillStore(context.Object);
        await File.WriteAllTextAsync(Path.Combine(store.RootPath, "old.tmp"),
            (typed ? "ETLSQL-JSON-1" + Environment.NewLine : "") + payload + Environment.NewLine, CancellationToken.None);
        await using var reader = await store.CreateReaderAsync("old.tmp");
        var error = await Assert.ThrowsAsync<ExecutionException>(() => reader.ReadRowAsync());
        Assert.Contains("recreate the saved session", error.Message);
        Assert.DoesNotContain(payload, error.Message);
    }

    [Theory]
    [InlineData("Json", false, false)]
    [InlineData("Json", true, false)]
    [InlineData("Json", false, true)]
    [InlineData("Json", true, true)]
    [InlineData("Arrow", false, false)]
    [InlineData("Arrow", true, false)]
    [InlineData("Arrow", false, true)]
    [InlineData("Arrow", true, true)]
    public async Task TextRemainsTextAcrossSpill(string format, bool encrypt, bool compress)
    {
        string[] values = ["001", "123.4500", "1,234", "1e3", "2026-10-25", "2026-10-25T01:02:03.1234567Z", "true", "null", "é中文🌿"];
        var context = new Mock<IExecutionContext>();
        context.SetupProperty(value => value.SpillFormat, format);
        context.SetupProperty(value => value.SpillEncryptionEnabled, encrypt);
        context.SetupProperty(value => value.SpillCompressionEnabled, compress);
        context.SetupGet(value => value.SessionStateManager).Returns(new NullSessionStateManager());
        using var store = new SpillStore(context.Object);
        await using (var writer = await store.CreateWriterAsync("text-fidelity.tmp"))
        {
            foreach (var value in values)
                await writer.WriteRowAsync(new Row { ["value"] = value });
        }
        await using var reader = await store.CreateReaderAsync("text-fidelity.tmp");
        var rows = await reader.AsEnumerableAsync().ToListAsync();
        Assert.Equal(values.Length, rows.Count);
        for (var index = 0; index < values.Length; index++)
            Assert.Equal(values[index], Assert.IsType<string>(rows[index]["value"]));
    }
}
