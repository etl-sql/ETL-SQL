using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class SqlitePragmaOptionsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnector _connector = new();

    public SqlitePragmaOptionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"etlsql_sqlite_pragma_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup on Windows.
        }
    }

    private static Mock<IExecutionContext> CreateContext()
    {
        var context = new Mock<IExecutionContext>();
        context.Setup(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.Setup(c => c.Logger).Returns(NullLogger.Instance);
        context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
        return context;
    }

    private static async Task<object?> ReadScalarAsync(SqliteDataSource ds, string sql)
    {
        await foreach (var batch in ds.ExecuteRawSql(sql))
        {
            if (batch.Rows.Count > 0)
                return batch.Rows[0][0];
        }
        return null;
    }

    [Theory]
    [InlineData("DELETE", "delete")]
    [InlineData("TRUNCATE", "truncate")]
    [InlineData("PERSIST", "persist")]
    [InlineData("WAL", "wal")]
    [InlineData("MEMORY", "memory")]
    [InlineData("OFF", "off")]
    [InlineData("wal", "wal")]
    public async Task JournalMode_ValidValues_AppliedOnEveryConnectionOpen(string configured, string expectedPragma)
    {
        var dbPath = Path.Combine(_tempDir, $"jm_{configured}_{Guid.NewGuid():N}.db");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = dbPath,
            ["JOURNAL_MODE"] = configured
        };

        var connStr = _connector.BuildConnectionString(options);
        await using var ds = (SqliteDataSource)_connector.CreateDataSource(CreateContext().Object, connStr, options);

        var actual = Convert.ToString(await ReadScalarAsync(ds, "PRAGMA journal_mode;"));
        Assert.Equal(expectedPragma, actual, ignoreCase: true);

        // Also verified on transactional connection open.
        await ds.BeginTransactionAsync();
        try
        {
            var txActual = Convert.ToString(await ReadScalarAsync(ds, "PRAGMA journal_mode;"));
            Assert.Equal(expectedPragma, txActual, ignoreCase: true);
        }
        finally
        {
            await ds.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("2500", 2500L)]
    [InlineData(" 5000 ", 5000L)]
    public async Task BusyTimeoutMs_ValidValues_AppliedOnEveryConnectionOpen(string configured, long expectedMs)
    {
        var dbPath = Path.Combine(_tempDir, $"bt_{expectedMs}_{Guid.NewGuid():N}.db");
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = dbPath,
            ["BUSY_TIMEOUT_MS"] = configured
        };

        var connStr = _connector.BuildConnectionString(options);
        await using var ds = (SqliteDataSource)_connector.CreateDataSource(CreateContext().Object, connStr, options);

        var actual = Convert.ToInt64(await ReadScalarAsync(ds, "PRAGMA busy_timeout;"));
        Assert.Equal(expectedMs, actual);

        // Also verified on transactional connection open.
        await ds.BeginTransactionAsync();
        try
        {
            var txActual = Convert.ToInt64(await ReadScalarAsync(ds, "PRAGMA busy_timeout;"));
            Assert.Equal(expectedMs, txActual);
        }
        finally
        {
            await ds.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID")]
    [InlineData("WAL; DROP TABLE x")]
    public void JournalMode_InvalidValues_FailAtBuildAndCreate(string invalid)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = ":memory:",
            ["JOURNAL_MODE"] = invalid
        };

        Assert.Throws<ExecutionException>(() => _connector.BuildConnectionString(options));
        Assert.Throws<ExecutionException>(() => new SqliteDataSource(CreateContext().Object, "Data Source=:memory:", null, options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1")]
    [InlineData("-500")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void BusyTimeoutMs_InvalidValues_FailAtBuildAndCreate(string invalid)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = ":memory:",
            ["BUSY_TIMEOUT_MS"] = invalid
        };

        Assert.Throws<ExecutionException>(() => _connector.BuildConnectionString(options));
        Assert.Throws<ExecutionException>(() => new SqliteDataSource(CreateContext().Object, "Data Source=:memory:", null, options));
    }

    [Fact]
    public async Task CreateConnection_InvalidJournalModeOrBusyTimeout_FailsAtCreateTime()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

        const string badJournal = "CREATE CONNECTION sq_bad_jm AS SQLITE(DATABASE=':memory:', JOURNAL_MODE='NOPE');";
        await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(badJournal).Tokenize()).Parse()));

        const string badTimeout = "CREATE CONNECTION sq_bad_bt AS SQLITE(DATABASE=':memory:', BUSY_TIMEOUT_MS=-10);";
        await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(badTimeout).Tokenize()).Parse()));

        const string okSql = "CREATE CONNECTION sq_ok AS SQLITE(DATABASE=':memory:', JOURNAL_MODE='MEMORY', BUSY_TIMEOUT_MS=2000);";
        await evaluator.Evaluate(new Parser(new Lexer(okSql).Tokenize()).Parse());
        Assert.True(((IExecutionContext)evaluator).Connections.ContainsKey("sq_ok"));
    }

    [Fact]
    public async Task TwoWriters_WithBusyTimeoutMs_WaitsInsteadOfFailingSqliteBusy()
    {
        var dbPath = Path.Combine(_tempDir, $"concurrent_{Guid.NewGuid():N}.db");
        var options1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = dbPath,
            ["JOURNAL_MODE"] = "WAL",
            ["BUSY_TIMEOUT_MS"] = "5000"
        };
        var options2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATABASE"] = dbPath,
            ["JOURNAL_MODE"] = "WAL",
            ["BUSY_TIMEOUT_MS"] = "5000"
        };

        var connStr = _connector.BuildConnectionString(options1);
        await using var writer1 = (SqliteDataSource)_connector.CreateDataSource(CreateContext().Object, connStr, options1);
        await using var writer2 = (SqliteDataSource)_connector.CreateDataSource(CreateContext().Object, connStr, options2);

        await foreach (var _ in writer1.ExecuteRawSql("CREATE TABLE items (id INTEGER PRIMARY KEY, val TEXT);")) { }

        // Writer 1 acquires the write lock and holds it briefly.
        await writer1.BeginTransactionAsync();
        await foreach (var _ in writer1.ExecuteRawSql("INSERT INTO items (id, val) VALUES (1, 'one');")) { }

        // Writer 2 attempts a write while Writer 1 holds the lock; BUSY_TIMEOUT_MS=5000 makes it wait.
        var writer2Task = Task.Run(async () =>
        {
            await foreach (var _ in writer2.ExecuteRawSql("INSERT INTO items (id, val) VALUES (2, 'two');")) { }
        });

        await Task.Delay(250);
        Assert.False(writer2Task.IsCompleted);

        await writer1.CommitAsync();
        await writer2Task;

        var count = Convert.ToInt64(await ReadScalarAsync(writer1, "SELECT COUNT(*) FROM items;"));
        Assert.Equal(2L, count);
    }

    [Fact]
    public void MetadataAndHelp_IncludeJournalModeAndBusyTimeoutMs_AndNoGenericPragma()
    {
        var supported = _connector.GetSupportedOptions();
        Assert.Contains("JOURNAL_MODE", supported.Keys);
        Assert.Contains("BUSY_TIMEOUT_MS", supported.Keys);
        Assert.DoesNotContain("PRAGMA", supported.Keys);

        var help = _connector.GetHelp();
        Assert.Contains("JOURNAL_MODE", help, StringComparison.Ordinal);
        Assert.Contains("BUSY_TIMEOUT_MS", help, StringComparison.Ordinal);

        Assert.True(LanguageMetadata.IsKeyword("JOURNAL_MODE"));
        Assert.True(LanguageMetadata.IsKeyword("BUSY_TIMEOUT_MS"));
    }
}
