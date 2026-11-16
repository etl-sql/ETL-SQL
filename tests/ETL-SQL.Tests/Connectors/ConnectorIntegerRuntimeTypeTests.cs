using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// The section 6 review said database connectors break the engine rule that integers are decimal
/// at runtime because they store the provider's CLR integer in each row. These tests read a SQLite
/// INTEGER column through the real CREATE CONNECTION / SELECT path and check what comes out.
/// </summary>
[Trait("Category", "Connectors")]
public sealed class ConnectorIntegerRuntimeTypeTests
{
    [Fact]
    public async Task SqliteIntegerColumn_ThroughTheEngine_ComputesLikeAnEngineInteger()
    {
        await WithDatabase(async (evaluator) =>
        {
            await evaluator.Evaluate(Parse("""
                SELECT id, id / 2 AS half, id + 0.5 AS plus_half, id * 3 AS triple
                FROM int_db.items WHERE id = 7;
                """));

            var row = Assert.Single(evaluator.LastResult!.Rows);
            Assert.Equal(7m, Convert.ToDecimal(row["id"]));
            Assert.Equal(3.5m, Convert.ToDecimal(row["half"]));
            Assert.Equal(7.5m, Convert.ToDecimal(row["plus_half"]));
            Assert.Equal(21m, Convert.ToDecimal(row["triple"]));
        });
    }

    [Fact]
    public async Task SqliteIntegerColumn_JoinsAndGroupsWithEngineDecimals()
    {
        await WithDatabase(async (evaluator) =>
        {
            await evaluator.Evaluate(Parse("""
                SELECT * INTO #keys FROM (VALUES (7), (8)) AS v(k);
                SELECT i.id, k.k FROM int_db.items i JOIN #keys k ON i.id = k.k ORDER BY i.id;
                """));

            var rows = evaluator.LastResult!.Rows;
            Assert.Equal(2, rows.Count);
            Assert.Equal(7m, Convert.ToDecimal(rows[0]["id"]));
            Assert.Equal(8m, Convert.ToDecimal(rows[1]["id"]));
        });
    }

    /// <summary>
    /// UNION de-duplicates by hashing values. A provider long and an engine decimal literal must
    /// hash as the same key, or the union returns the same number twice.
    /// </summary>
    [Fact]
    public async Task SqliteIntegerColumn_UnionWithEngineLiteral_DeduplicatesAcrossTypes()
    {
        await WithDatabase(async (evaluator) =>
        {
            await evaluator.Evaluate(Parse("""
                SELECT id FROM int_db.items WHERE id = 7
                UNION
                SELECT 7 AS id;
                """));

            var row = Assert.Single(evaluator.LastResult!.Rows);
            Assert.Equal(7m, Convert.ToDecimal(row["id"]));
        });
    }

    [Fact]
    public async Task SqliteIntegerColumn_GroupByAndSum_StayExact()
    {
        await WithDatabase(async (evaluator) =>
        {
            await evaluator.Evaluate(Parse("""
                SELECT SUM(id) AS total, COUNT(*) AS n, AVG(id) AS mean FROM int_db.items;
                """));

            var row = Assert.Single(evaluator.LastResult!.Rows);
            Assert.Equal(24m, Convert.ToDecimal(row["total"]));
            Assert.Equal(3m, Convert.ToDecimal(row["n"]));
            Assert.Equal(8m, Convert.ToDecimal(row["mean"]));
        });
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

    private static async Task WithDatabase(Func<Evaluator, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ints.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE items(id INTEGER); INSERT INTO items VALUES(7), (8), (9);";
                await command.ExecuteNonQueryAsync();
            }
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            await using var providerLifetime = provider as IAsyncDisposable;
            provider.GetRequiredService<ILogger>().SuppressConsole = true;
            var evaluator = provider.GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse($"CREATE CONNECTION int_db AS SQLITE(DATABASE='{path.Replace('\\', '/')}');"));
            await test(evaluator);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
