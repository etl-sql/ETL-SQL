using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// Two defects that made the shipped SQLite sample unrunnable. Both were found by trying to run it
/// rather than by reading it, which is the argument for keeping an executable recipe.
/// </summary>
public class SqliteConnectionDeclarationTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"etlsql_sqlite_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private SqliteDataSource Source(string? table = null) =>
        new(SystemExecutionContext.Instance, $"Data Source={_dbPath}", table);

    /// <summary>
    /// Declaring a connection probes it for schema. A connection-level source has no table bound,
    /// and throwing here made CREATE CONNECTION ... AS SQLITE(...) fail outright on a database with
    /// no table — including a brand-new file, which is the normal way to start. SQL Server and
    /// PostgreSQL both return nothing in this position.
    /// </summary>
    [Fact]
    public async Task ColumnsOnATablelessConnectionReturnNothingRatherThanThrowing()
    {
        var columns = await Source().GetColumnsAsync();

        Assert.Empty(columns);
    }

    [Fact]
    public async Task ColumnsStillResolveWhenATableIsBound()
    {
        var source = Source();
        await foreach (var _ in source.ExecuteRawSql("CREATE TABLE widget (id INTEGER, name TEXT);")) { }

        var columns = (await Source("widget").GetColumnsAsync()).ToList();

        Assert.Equal(["id", "name"], columns);
    }

    /// <summary>
    /// The query compiler emits @p0, @p1…; parameters were registered as $p0, $p1…
    /// Microsoft.Data.Sqlite accepts either prefix but binds by name, so the values never bound and
    /// the provider rejected the command. Any pushdown carrying a literal hit this — a WHERE with a
    /// constant is enough.
    /// </summary>
    [Fact]
    public async Task PushdownParametersBindAgainstTheCompilersNames()
    {
        var source = Source();
        await foreach (var _ in source.ExecuteRawSql("CREATE TABLE widget (id INTEGER, name TEXT);")) { }
        await foreach (var _ in source.ExecuteRawSql(
            "INSERT INTO widget (id, name) VALUES (1, 'gear'), (2, 'shaft');")) { }

        var rows = 0;
        await foreach (var batch in source.ExecuteRawSql(
            "SELECT id, name FROM widget WHERE id > @p0;", new object?[] { 1 }))
        {
            rows += batch.Rows.Count;
        }

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task SeveralPushdownParametersBindPositionally()
    {
        var source = Source();
        await foreach (var _ in source.ExecuteRawSql("CREATE TABLE widget (id INTEGER, name TEXT);")) { }
        await foreach (var _ in source.ExecuteRawSql(
            "INSERT INTO widget (id, name) VALUES (1, 'gear'), (2, 'shaft'), (3, 'bushing');")) { }

        var names = "";
        await foreach (var batch in source.ExecuteRawSql(
            "SELECT name FROM widget WHERE id >= @p0 AND id <= @p1 ORDER BY id;",
            new object?[] { 2, 3 }))
        {
            names += string.Join(",", batch.Rows.Select(r => r[0]?.ToString()));
        }

        Assert.Equal("shaft,bushing", names);
    }
}
