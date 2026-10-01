using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

[Trait("CompatBreak", "0.20.0")]
public sealed class DatabaseTransactionRegressionTests
{
    [Theory]
    [InlineData(false, 1L)]
    [InlineData(true, 2L)]
    public async Task NestedCommit_DefersProviderCommitUntilTheRootCompletes(bool commitRoot, long expectedRows)
    {
        await WithDatabase(async (evaluator, connectionString) =>
        {
            await evaluator.Evaluate(Parse($"""
                BEGIN TRANSACTION;
                BEGIN TRANSACTION;
                INSERT INTO transaction_db.items (id, amount) VALUES (2, 20);
                COMMIT;
                {(commitRoot ? "COMMIT;" : "ROLLBACK;")}
                """));
            Assert.Equal(expectedRows, await Scalar(connectionString, "SELECT COUNT(*) FROM items"));
        });
    }

    [Theory]
    [InlineData("UPDATE transaction_db.items SET amount = id;", "SELECT amount FROM items WHERE id = 1", false, 10L)]
    [InlineData("UPDATE transaction_db.items SET amount = id;", "SELECT amount FROM items WHERE id = 1", true, 1L)]
    [InlineData("DELETE FROM transaction_db.items WHERE id = 1;", "SELECT COUNT(*) FROM items", false, 1L)]
    [InlineData("DELETE FROM transaction_db.items WHERE id = 1;", "SELECT COUNT(*) FROM items", true, 0L)]
    public async Task FirstMutation_EnlistsBeforeExecuting(string mutation, string check, bool commit, long expected)
    {
        await WithDatabase(async (evaluator, connectionString) =>
        {
            await evaluator.Evaluate(Parse($"BEGIN TRANSACTION; {mutation} {(commit ? "COMMIT;" : "ROLLBACK;")}"));
            Assert.Equal(expected, await Scalar(connectionString, check));
        });
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

    [Theory]
    [InlineData("UPDATE transaction_db.items SET amount = 20 WHERE id = 1;", 20L)]
    [InlineData("UPDATE transaction_db.items SET id = 1, amount = 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10 + 11 WHERE id = 1;", 66L)]
    public async Task Update_BindsDistinctParametersAcrossAssignmentsAndPredicate(string update, long expected)
    {
        await WithDatabase(async (evaluator, connectionString) =>
        {
            await evaluator.Evaluate(Parse($"BEGIN TRANSACTION; {update} COMMIT;"));
            Assert.Equal(expected, await Scalar(connectionString, "SELECT amount FROM items WHERE id = 1"));
        });
    }

    private static async Task WithDatabase(Func<Evaluator, string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"etlsql-transaction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "transaction.db");
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
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            await using var providerLifetime = provider as IAsyncDisposable;
            provider.GetRequiredService<ILogger>().SuppressConsole = true;
            var evaluator = provider.GetRequiredService<Evaluator>();
            await evaluator.Evaluate(Parse($"CREATE CONNECTION transaction_db AS SQLITE(DATABASE='{path.Replace('\\', '/')}');"));
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
