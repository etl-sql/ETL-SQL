using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Review;

// These tests assert the required behavior. Failures are evidence for the review, not fixes.
public sealed class CorrectnessReproductions
{
    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

    private static IServiceProvider Provider()
    {
        var provider = DependencyInjectionSetup.BuildServiceProvider();
        provider.GetRequiredService<ILogger>().SuppressConsole = true;
        return provider;
    }

    private static async Task<List<Row>> Query(Evaluator evaluator, string sql)
    {
        var result = new List<Row>();
        await foreach (var batch in evaluator.ExecuteQuery(Parse(sql).Statements[0]))
            result.AddRange(batch.Rows);
        return result;
    }

    [Fact]
    public async Task HashJoinMustAgreeWithLoopJoinUnderDefaultCaseInsensitiveComparison()
    {
        var provider = Provider();
        await using var evaluator = provider.GetRequiredService<Evaluator>();
        await evaluator.Evaluate(Parse("""
            CREATE TABLE #l (join_key VARCHAR(20));
            CREATE TABLE #r (join_key VARCHAR(20));
            INSERT INTO #l VALUES ('alpha');
            INSERT INTO #r VALUES ('ALPHA');
            """));
        var loop = await Query(evaluator, "SELECT l.join_key FROM #l l INNER LOOP JOIN #r r ON l.join_key = r.join_key;");
        var hash = await Query(evaluator, "SELECT l.join_key FROM #l l INNER HASH JOIN #r r ON l.join_key = r.join_key;");
        Assert.Single(loop);
        Assert.Single(hash);
    }

    [Theory]
    [InlineData("SEMI", 1)]
    [InlineData("ANTI", 0)]
    public async Task SemiAndAntiJoinMustReturnTheCorrectLeftRows(string kind, int expected)
    {
        var provider = Provider();
        await using var evaluator = provider.GetRequiredService<Evaluator>();
        await evaluator.Evaluate(Parse("""
            CREATE TABLE #l (id INT);
            CREATE TABLE #r (id INT);
            INSERT INTO #l VALUES (1);
            INSERT INTO #r VALUES (1), (1);
            """));
        var rows = await Query(evaluator, $"SELECT l.id FROM #l l LEFT {kind} JOIN #r r ON l.id = r.id;");
        Assert.Equal(expected, rows.Count);
    }

    [Fact]
    public async Task NestedCommitMustRemainRollbackableByTheOuterTransaction()
    {
        await UsingSqlite(async (evaluator, connectionString) =>
        {
            await evaluator.Evaluate(Parse("""
                BEGIN TRANSACTION;
                BEGIN TRANSACTION;
                INSERT INTO reviewdb.items (id, amount) VALUES (2, 20);
                COMMIT;
                ROLLBACK;
                """));
            Assert.Equal(1L, await Scalar(connectionString, "SELECT COUNT(*) FROM items"));
        });
    }

    [Theory]
    [InlineData("UPDATE reviewdb.items SET amount = id;", "SELECT amount FROM items WHERE id = 1", 10L)]
    [InlineData("DELETE FROM reviewdb.items WHERE id = 1;", "SELECT COUNT(*) FROM items", 1L)]
    public async Task MutationAsTheFirstTransactionalOperationMustBeRollbackable(string mutation, string check, long expected)
    {
        await UsingSqlite(async (evaluator, connectionString) =>
        {
            await evaluator.Evaluate(Parse($"BEGIN TRANSACTION; {mutation} ROLLBACK;"));
            Assert.Equal(expected, await Scalar(connectionString, check));
        });
    }

    [Fact]
    public async Task SqliteUpdateWithLiteralsMustBindItsParameters()
    {
        await UsingSqlite(async (evaluator, connectionString) =>
        {
            await evaluator.Evaluate(Parse("BEGIN TRANSACTION; UPDATE reviewdb.items SET amount = 20 WHERE id = 1; COMMIT;"));
            Assert.Equal(20L, await Scalar(connectionString, "SELECT amount FROM items WHERE id = 1"));
        });
    }

    private static async Task UsingSqlite(Func<Evaluator, string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "etlsql-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "review.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE items(id INTEGER, amount INTEGER); INSERT INTO items VALUES(1, 10);";
                await command.ExecuteNonQueryAsync();
            }
            var provider = Provider();
            await using var evaluator = provider.GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse($"CREATE CONNECTION reviewdb AS SQLITE(DATABASE='{path.Replace('\\', '/')}');"));
            await test(evaluator, connectionString);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<long> Scalar(string connectionString, string query)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = query;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
