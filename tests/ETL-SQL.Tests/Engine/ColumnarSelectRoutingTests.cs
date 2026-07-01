using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public sealed class ColumnarSelectRoutingTests
{
    [Fact]
    public async Task OptInCreatesEligibleTempTableWithColumnarStoreAndPreservesSemanticFallback()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
        {
            ["Engine:UseColumnarTempTables"] = "true"
        }).GetRequiredService<Evaluator>();
        evaluator.IsPersistentSession = false;

        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT NOT NULL, Name VARCHAR(20)); " +
            "CREATE TABLE #fallback (Id INT DEFAULT 7); " +
            "INSERT INTO #native VALUES (1, 'one');"));

        var native = Assert.IsType<AppendOnlyColumnDataSource>(evaluator.Connections["#native"]);
        Assert.Equal(1, native.EstimatedRowCount);
        Assert.IsType<InMemoryDataSource>(evaluator.Connections["#fallback"]);
    }

    [Fact]
    public async Task OptInColumnarTempTableParticipatesInEngineRollback()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT PRIMARY KEY); " +
            "INSERT INTO #native VALUES (1); " +
            "BEGIN TRANSACTION; " +
            "INSERT INTO #native VALUES (2); " +
            "ROLLBACK;"));

        var native = Assert.IsType<AppendOnlyColumnDataSource>(evaluator.Connections["#native"]);
        Assert.Equal(1, native.EstimatedRowCount);
        var batches = await native.ReadColumnBatches().ToListAsync();
        try
        {
            Assert.Equal(new[] { 1 }, batches.SelectMany(batch => batch.GetColumn<int>("Id").Values.ToArray()));
        }
        finally
        {
            foreach (var batch in batches) batch.Dispose();
        }
    }

    [Fact]
    public async Task PersistentSessionKeepsTempTableOnPersistableRowStore()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = true;

        await evaluator.Evaluate(ParseScript("CREATE TABLE #persisted (Id INT);"));

        Assert.IsType<InMemoryDataSource>(evaluator.Connections["#persisted"]);
    }

    [Fact]
    public async Task OptInColumnarTempRejectsUnsupportedIndexAndReplaceSemantics()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript("CREATE TABLE #native (Id INT PRIMARY KEY);"));

        var indexError = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(ParseScript("CREATE INDEX ix_native ON #native (Id);")));
        Assert.Contains("not supported", indexError.Message, StringComparison.OrdinalIgnoreCase);

        var replaceError = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(ParseScript("INSERT OR REPLACE INTO #native VALUES (1);")));
        Assert.Contains("not supported", replaceError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpectSchemaReadsColumnarTempLogicalTypes()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT, Amount DECIMAL(18,2)); " +
            "EXPECT SCHEMA #native (Id INT, Amount DECIMAL);"));

        var error = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(ParseScript("EXPECT SCHEMA #native (Id VARCHAR);")));
        Assert.Contains("TYPE DRIFT", error.Message);
    }

    [Fact]
    public async Task SimpleSelectFiltersAndProjectsNativeSourceWithoutCallingRowReader()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        evaluator.Connections["col"] = source;
        var statement = ParseSelect("SELECT Id AS ResultId FROM col WHERE Id * 2 > 2;");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var results = await handler.EvaluateQuery(statement, evaluator).ToListAsync();

        Assert.Equal(new[] { "ResultId" }, results.Single().ColumnNames);
        Assert.Equal(new object?[] { 3m }, results.Single().Rows.Select(row => row["ResultId"]).ToArray());
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task UnsupportedStringPredicateReplaysNativeBatchesThroughRowFallback()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        evaluator.Connections["col"] = source;
        var statement = ParseSelect("SELECT Name FROM col WHERE Name = 'one';");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var results = await handler.EvaluateQuery(statement, evaluator).ToListAsync();

        Assert.Equal(new[] { "ONE" }, results.SelectMany(batch => batch.Rows).Select(row => row["Name"]));
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task LateralAliasDeclinesNativeRouteAndUsesEstablishedRowPlan()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource(throwOnRowRead: false);
        evaluator.Connections["col"] = source;
        var statement = ParseSelect("SELECT Id AS x, x AS y FROM col;");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var results = await handler.EvaluateQuery(statement, evaluator).ToListAsync();

        Assert.Equal(1, source.RowReadAttempts);
        Assert.Equal(
            results.SelectMany(batch => batch.Rows).Select(row => row["x"]),
            results.SelectMany(batch => batch.Rows).Select(row => row["y"]));
    }

    [Fact]
    public async Task NullPredicateUsesNativeRoute()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        evaluator.Connections["col"] = source;
        var statement = ParseSelect("SELECT Name FROM col WHERE Id IS NULL;");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var results = await handler.EvaluateQuery(statement, evaluator).ToListAsync();

        Assert.Equal(new[] { "null-id" }, results.SelectMany(batch => batch.Rows).Select(row => row["Name"]));
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task GlobalAggregatesAccumulateAcrossNativeBatchesWithDecimalPromotion()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateAggregateSource();
        evaluator.Connections["col"] = source;
        var statement = ParseSelect(
            "SELECT COUNT(*) AS n, COUNT(Id) AS ni, SUM(Id) AS s, MIN(Id) AS mn, " +
            "MAX(Id) AS mx, AVG(Id) AS av FROM col;");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var result = Assert.Single(await handler.EvaluateQuery(statement, evaluator).ToListAsync());
        var row = Assert.Single(result.Rows);
        Assert.Equal(5m, row["n"]);
        Assert.Equal(4m, row["ni"]);
        Assert.Equal(4_294_967_298m, row["s"]);
        Assert.Equal(1m, row["mn"]);
        Assert.Equal((decimal)int.MaxValue, row["mx"]);
        Assert.Equal(1_073_741_824.5m, row["av"]);
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task UnsupportedAggregatePredicateReplaysIntoExistingAggregatePipeline()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        evaluator.Connections["col"] = source;
        var statement = ParseSelect("SELECT SUM(Id) AS total FROM col WHERE Name = 'one';");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var result = Assert.Single(await handler.EvaluateQuery(statement, evaluator).ToListAsync());

        Assert.Equal(1m, Assert.Single(result.Rows)["total"]);
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task GroupedAggregatesAccumulateAcrossNativeBatches()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateGroupedSource();
        evaluator.Connections["grouped"] = source;
        var statement = ParseSelect(
            "SELECT GroupId, COUNT(*) AS RowCount, COUNT(Value) AS ValueCount, SUM(Value) AS Total, " +
            "AVG(Value) AS MeanValue, MIN(Value) AS Minimum, MAX(Value) AS Maximum " +
            "FROM grouped GROUP BY GroupId;");

        var results = await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(statement, evaluator).ToListAsync();

        var rows = Assert.Single(results).Rows.OrderBy(row => row["GroupId"]?.ToString()).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1m, rows[0]["GroupId"]);
        Assert.Equal(3m, rows[0]["RowCount"]);
        Assert.Equal(2m, rows[0]["ValueCount"]);
        Assert.Equal(30m, rows[0]["Total"]);
        Assert.Equal(15m, rows[0]["MeanValue"]);
        Assert.Equal(10m, rows[0]["Minimum"]);
        Assert.Equal(20m, rows[0]["Maximum"]);
        Assert.Equal(2m, rows[1]["GroupId"]);
        Assert.Equal(2m, rows[1]["RowCount"]);
        Assert.Equal(12m, rows[1]["Total"]);

        var filteredStatement = ParseSelect(
            "SELECT GroupId, COUNT(*) AS RowCount, SUM(Value) AS Total " +
            "FROM grouped WHERE Value > 5 GROUP BY GroupId;");
        var filtered = Assert.Single(await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(filteredStatement, evaluator).ToListAsync());
        var filteredRows = filtered.Rows.OrderBy(row => row["GroupId"]?.ToString()).ToList();
        Assert.Equal(2m, filteredRows[0]["RowCount"]);
        Assert.Equal(30m, filteredRows[0]["Total"]);
        Assert.Equal(1m, filteredRows[1]["RowCount"]);
        Assert.Equal(7m, filteredRows[1]["Total"]);
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task GroupedHavingDeclinesNativeRouteAndUsesEstablishedPipeline()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateGroupedSource(throwOnRowRead: false);
        evaluator.Connections["grouped"] = source;
        var statement = ParseSelect(
            "SELECT GroupId, SUM(Value) AS Total FROM grouped " +
            "GROUP BY GroupId HAVING SUM(Value) > 20;");

        var results = await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(statement, evaluator).ToListAsync();

        Assert.Single(results.SelectMany(batch => batch.Rows));
        Assert.Equal(1, source.RowReadAttempts);
    }

    [Fact]
    public async Task GroupedAggregatesSupportDateAndNullKeysNatively()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("EventDate", typeof(DateTime), "DATE"),
            new ColumnBatchField("Amount", typeof(int), "INT")
        });
        var firstDate = new DateTime(2026, 1, 1);
        var secondDate = new DateTime(2026, 1, 2);
        await using var source = new NativeOnlyDataSource(new[]
        {
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<DateTime>(new[] { firstDate, default, secondDate }, 3, new byte[] { 0b0000_0010 }),
                new ColumnBuffer<int>(new[] { 10, 5, 7 }, 3)
            }, 3),
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<DateTime>(new[] { firstDate, default }, 2, new byte[] { 0b0000_0010 }),
                new ColumnBuffer<int>(new[] { 20, 3 }, 2)
            }, 2)
        }, throwOnRowRead: true);
        evaluator.Connections["events"] = source;
        var statement = ParseSelect(
            "SELECT EventDate, COUNT(*) AS RowCount, SUM(Amount) AS Total " +
            "FROM events GROUP BY EventDate;");

        var rows = Assert.Single(await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(statement, evaluator).ToListAsync()).Rows;

        var first = Assert.Single(rows, row => Equals(row["EventDate"], firstDate));
        Assert.Equal(2m, first["RowCount"]);
        Assert.Equal(30m, first["Total"]);
        var nullKey = Assert.Single(rows, row => row["EventDate"] == null);
        Assert.Equal(2m, nullKey["RowCount"]);
        Assert.Equal(8m, nullKey["Total"]);
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task MinMaxDoNotPerformUnusedOverflowingSum()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var schema = new ColumnBatchSchema(new[] { new ColumnBatchField("Amount", typeof(decimal), "DECIMAL") });
        await using var source = new NativeOnlyDataSource(new[]
        {
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<decimal>(new[] { decimal.MaxValue, decimal.MaxValue - 1 }, 2)
            }, 2)
        }, throwOnRowRead: true);
        evaluator.Connections["col"] = source;
        var statement = ParseSelect("SELECT MIN(Amount) AS mn, MAX(Amount) AS mx FROM col;");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var row = Assert.Single(Assert.Single(
            await handler.EvaluateQuery(statement, evaluator).ToListAsync()).Rows);

        Assert.Equal(decimal.MaxValue - 1, row["mn"]);
        Assert.Equal(decimal.MaxValue, row["mx"]);
    }

    [Fact]
    public async Task SelectIntoTransfersCompatibleNativeBatchesWithoutRowMaterialization()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        await using var destination = new AppendOnlyColumnDataSource(new[]
        {
            new ColumnDefinition("Id", "INT", false),
            new ColumnDefinition("Name", "VARCHAR(20)", false)
        });
        evaluator.Connections["col"] = source;
        evaluator.Connections["#dest"] = destination;
        var statement = ParseSelect("SELECT * INTO #dest FROM col;");
        var handler = new SelectStatementHandler(NullLogger.Instance);

        await handler.Execute(statement, evaluator);

        Assert.Equal(3, destination.EstimatedRowCount);
        Assert.Equal(0, source.RowReadAttempts);
        Assert.Equal(3L, evaluator.Variables["@@ROWCOUNT"]);
        var batches = await destination.ReadColumnBatches().ToListAsync();
        try
        {
            var ids = Assert.Single(batches).GetColumn<int>("Id");
            Assert.Equal(1, ids.Values.Span[0]);
            Assert.True(ids.IsNull(1));
            Assert.Equal(3, ids.Values.Span[2]);
        }
        finally
        {
            foreach (var batch in batches) batch.Dispose();
        }
    }

    [Fact]
    public async Task FilteredSelectIntoCompactsNativeBuffersWithoutCallingRowReader()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        await using var destination = new AppendOnlyColumnDataSource(new[]
        {
            new ColumnDefinition("Id", "INT", false),
            new ColumnDefinition("Name", "VARCHAR(20)", false)
        });
        evaluator.Connections["col"] = source;
        evaluator.Connections["#dest"] = destination;
        var statement = ParseSelect("SELECT * INTO #dest FROM col WHERE Id > 1;");

        await new SelectStatementHandler(NullLogger.Instance).Execute(statement, evaluator);

        Assert.Equal(1, destination.EstimatedRowCount);
        Assert.Equal(0, source.RowReadAttempts);
        Assert.Equal(1L, evaluator.Variables["@@ROWCOUNT"]);
        var batches = await destination.ReadColumnBatches().ToListAsync();
        try
        {
            var batch = Assert.Single(batches);
            Assert.Equal(3, batch.GetColumn<int>("Id").Values.Span[0]);
            Assert.Equal("three", batch.GetUtf8Column("Name").GetBoxedValue(0));
        }
        finally
        {
            foreach (var batch in batches) batch.Dispose();
        }
    }

    [Fact]
    public async Task ReorderedAliasedSelectIntoCompactsAndRenamesNativeSchema()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateSource();
        await using var destination = new AppendOnlyColumnDataSource(new[]
        {
            new ColumnDefinition("Label", "VARCHAR(20)", false),
            new ColumnDefinition("ResultId", "INT", false)
        });
        evaluator.Connections["col"] = source;
        evaluator.Connections["#dest"] = destination;
        var statement = ParseSelect("SELECT Name AS Label, Id AS ResultId INTO #dest FROM col WHERE Id > 1;");

        await new SelectStatementHandler(NullLogger.Instance).Execute(statement, evaluator);

        Assert.Equal(0, source.RowReadAttempts);
        var batches = await destination.ReadColumnBatches().ToListAsync();
        try
        {
            var batch = Assert.Single(batches);
            Assert.Equal(new[] { "Label", "ResultId" }, batch.Schema.Fields.Select(field => field.Name));
            Assert.Equal("three", batch.GetUtf8Column("Label").GetBoxedValue(0));
            Assert.Equal(3, batch.GetColumn<int>("ResultId").Values.Span[0]);
        }
        finally
        {
            foreach (var batch in batches) batch.Dispose();
        }
    }

    private static SelectStatement ParseSelect(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        return Assert.IsType<SelectStatement>(Assert.Single(script.Statements));
    }

    private static Script ParseScript(string sql)
        => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static NativeOnlyDataSource CreateSource(bool throwOnRowRead = true)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(int), "INT"),
            new ColumnBatchField("Name", typeof(string), "VARCHAR(20)")
        });
        var batch = new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(new[] { 1, 0, 3 }, 3, new byte[] { 0b0000_0010 }),
            Utf8ColumnBuffer.FromStrings(new string?[] { "ONE", "null-id", "three" })
        }, 3);
        return new NativeOnlyDataSource(new[] { batch }, throwOnRowRead);
    }

    private static NativeOnlyDataSource CreateAggregateSource()
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Id", typeof(int), "INT"),
            new ColumnBatchField("Name", typeof(string), "VARCHAR(20)")
        });
        return new NativeOnlyDataSource(new[]
        {
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<int>(new[] { 1, 0, 3 }, 3, new byte[] { 0b0000_0010 }),
                Utf8ColumnBuffer.FromStrings(new string?[] { "one", "null", "three" })
            }, 3),
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<int>(new[] { int.MaxValue, int.MaxValue }, 2),
                Utf8ColumnBuffer.FromStrings(new string?[] { "max-a", "max-b" })
            }, 2)
        }, throwOnRowRead: true);
    }

    private static NativeOnlyDataSource CreateGroupedSource(bool throwOnRowRead = true)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("GroupId", typeof(int), "INT"),
            new ColumnBatchField("Value", typeof(int), "INT")
        });
        return new NativeOnlyDataSource(new[]
        {
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<int>(new[] { 1, 2, 1 }, 3),
                new ColumnBuffer<int>(new[] { 10, 5, 0 }, 3, new byte[] { 0b0000_0100 })
            }, 3),
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<int>(new[] { 1, 2 }, 2),
                new ColumnBuffer<int>(new[] { 20, 7 }, 2)
            }, 2)
        }, throwOnRowRead);
    }

    private sealed class NativeOnlyDataSource(IEnumerable<ColumnBatch> batches, bool throwOnRowRead) : IDataSource, IColumnarDataSource
    {
        private List<ColumnBatch>? _batches = batches.ToList();
        public int RowReadAttempts { get; private set; }
        public string Path => string.Empty;
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "TEST_COLUMNAR";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10_000)
        {
            RowReadAttempts++;
            await Task.Yield();
            if (throwOnRowRead)
                throw new InvalidOperationException("The native SELECT route must not call ReadBatches.");
            foreach (var batch in _batches ?? throw new ObjectDisposedException(nameof(NativeOnlyDataSource)))
            {
                using var retained = batch.Retain();
                yield return ColumnBatchAdapter.ToDataTable(retained);
            }
        }

        public async IAsyncEnumerable<ColumnBatch> ReadColumnBatches(
            int batchSize = 10_000,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            foreach (var batch in _batches ?? throw new ObjectDisposedException(nameof(NativeOnlyDataSource)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch.Retain();
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
            => Task.FromResult<IEnumerable<string>>(
                (_batches ?? throw new ObjectDisposedException(nameof(NativeOnlyDataSource)))[0]
                    .Schema.Fields.Select(field => field.Name).ToArray());
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException();
        public object? Snapshot() => throw new NotSupportedException();
        public void Restore(object? snapshot) => throw new NotSupportedException();
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync()
        {
            var batches = Interlocked.Exchange(ref _batches, null);
            if (batches != null) foreach (var batch in batches) batch.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
