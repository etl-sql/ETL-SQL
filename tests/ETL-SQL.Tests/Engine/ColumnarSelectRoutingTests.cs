using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
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
            Assert.Equal(new[] { 1, 0, 3 }, Assert.Single(batches).GetColumn<int>("Id").Values.ToArray());
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
