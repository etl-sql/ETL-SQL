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
    public async Task DefaultCreatesEligibleTempTableWithColumnarStoreAndPreservesSemanticFallback()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
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
    public async Task ConfigurationCanOptOutOfColumnarTempStorage()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
        {
            ["Engine:UseColumnarTempTables"] = "false"
        }).GetRequiredService<Evaluator>();
        evaluator.IsPersistentSession = false;

        await evaluator.Evaluate(ParseScript("CREATE TABLE #rows (Id INT);"));

        Assert.IsType<InMemoryDataSource>(evaluator.Connections["#rows"]);
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
    public async Task DefaultRoutesCompositeConstrainedTempTableAndEnforcesUniqueKey()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT NOT NULL, Code VARCHAR(20) NOT NULL, " +
            "CONSTRAINT UQ_native UNIQUE (Id, Code)); " +
            "INSERT INTO #native VALUES (1, 'one');"));

        Assert.IsType<AppendOnlyColumnDataSource>(evaluator.Connections["#native"]);
        var duplicate = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(ParseScript("INSERT INTO #native VALUES (1, 'one');")));
        Assert.Contains("constraint 'UQ_native'", duplicate.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task ColumnarTempDowngradesForIndexAndPreservesRowsAndConstraints()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT PRIMARY KEY, Name VARCHAR(20)); " +
            "INSERT INTO #native VALUES (1, 'one'); " +
            "CREATE INDEX ix_native ON #native (Name);"));

        var rowStore = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#native"]);
        Assert.Single((await rowStore.ReadBatches().ToListAsync()).SelectMany(batch => batch.Rows));

        var duplicate = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(ParseScript("INSERT INTO #native VALUES (1, 'duplicate');")));
        Assert.Contains("constraint", duplicate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ColumnarTempDowngradesForReplaceUpdateAndDeleteWithEstablishedSemantics()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #replace (Id INT PRIMARY KEY, Name VARCHAR(20)); " +
            "INSERT INTO #replace VALUES (1, 'one'); " +
            "INSERT OR REPLACE INTO #replace VALUES (1, 'replaced'); " +
            "CREATE TABLE #update (Id INT, Name VARCHAR(20)); " +
            "INSERT INTO #update VALUES (1, 'one'), (2, 'two'); " +
            "UPDATE #update SET Name = 'updated' WHERE Id = 1; " +
            "DELETE FROM #update WHERE Id = 2;"));

        var replace = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#replace"]);
        var replaceRows = (await replace.ReadBatches().ToListAsync()).SelectMany(batch => batch.Rows).ToList();
        Assert.Single(replaceRows);
        Assert.Equal("replaced", replaceRows[0]["Name"]);

        var update = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#update"]);
        var updateRows = (await update.ReadBatches().ToListAsync()).SelectMany(batch => batch.Rows).ToList();
        Assert.Single(updateRows);
        Assert.Equal("updated", updateRows[0]["Name"]);
    }

    [Fact]
    public async Task ColumnarTempMutationDowngradeRestoresOriginalStoreOnRollback()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;
        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT); " +
            "INSERT INTO #native VALUES (1); " +
            "BEGIN TRANSACTION; " +
            "INSERT INTO #native VALUES (2); " +
            "UPDATE #native SET Id = 3 WHERE Id = 1; " +
            "ROLLBACK;"));

        var native = Assert.IsType<AppendOnlyColumnDataSource>(evaluator.Connections["#native"]);
        var rows = (await native.ReadBatches().ToListAsync()).SelectMany(batch => batch.Rows).ToList();
        Assert.Single(rows);
        Assert.Equal(1, Convert.ToInt32(rows[0]["Id"]));
    }

    [Fact]
    public async Task ColumnarTempMutationDowngradeRetainsRowStoreOnCommit()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;

        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT); " +
            "INSERT INTO #native VALUES (1); " +
            "BEGIN TRANSACTION; " +
            "UPDATE #native SET Id = 2; " +
            "COMMIT;"));

        var rowStore = Assert.IsType<InMemoryDataSource>(evaluator.Connections["#native"]);
        var rows = (await rowStore.ReadBatches().ToListAsync()).SelectMany(batch => batch.Rows).ToList();
        Assert.Single(rows);
        Assert.Equal(2, Convert.ToInt32(rows[0]["Id"]));
    }

    [Fact]
    public async Task WhatIfMutationDoesNotDowngradeColumnarTempStorage()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.UseColumnarTempTables = true;
        evaluator.IsPersistentSession = false;

        await evaluator.Evaluate(ParseScript(
            "CREATE TABLE #native (Id INT PRIMARY KEY); " +
            "INSERT INTO #native VALUES (1); " +
            "SET WHAT_IF ON; " +
            "UPDATE #native SET Id = 2; " +
            "DELETE FROM #native WHERE Id = 1; " +
            "INSERT OR REPLACE INTO #native VALUES (1); " +
            "SET WHAT_IF OFF;"));

        var native = Assert.IsType<AppendOnlyColumnDataSource>(evaluator.Connections["#native"]);
        Assert.Equal(1, native.EstimatedRowCount);
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
    public async Task GlobalMinMaxSupportsTemporalAndGuidBuffersNatively()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var firstDate = new DateTime(2026, 1, 1);
        var lastDate = new DateTime(2026, 12, 31);
        var firstGuid = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var lastGuid = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var firstOffset = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(-5));
        var lastOffset = new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.FromHours(2));
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("EventDate", typeof(DateTime), "DATETIME"),
            new ColumnBatchField("Duration", typeof(TimeSpan), "TIME"),
            new ColumnBatchField("Identifier", typeof(Guid), "UUID"),
            new ColumnBatchField("ObservedAt", typeof(DateTimeOffset), "DATETIMEOFFSET")
        });
        await using var source = new NativeOnlyDataSource(new[]
        {
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<DateTime>(new[] { lastDate, default }, 2, new byte[] { 0b0000_0010 }),
                new ColumnBuffer<TimeSpan>(new[] { TimeSpan.FromHours(5), TimeSpan.FromHours(2) }, 2),
                new ColumnBuffer<Guid>(new[] { lastGuid, firstGuid }, 2),
                new ColumnBuffer<DateTimeOffset>(new[] { lastOffset, firstOffset }, 2)
            }, 2),
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<DateTime>(new[] { firstDate }, 1),
                new ColumnBuffer<TimeSpan>(new[] { TimeSpan.FromHours(8) }, 1),
                new ColumnBuffer<Guid>(new[] { Guid.Parse("11111111-1111-1111-1111-111111111111") }, 1),
                new ColumnBuffer<DateTimeOffset>(new[] { firstOffset.AddDays(1) }, 1)
            }, 1)
        }, throwOnRowRead: true);
        evaluator.Connections["events"] = source;

        var result = Assert.Single(await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(ParseSelect(
                "SELECT MIN(EventDate) AS FirstDate, MAX(EventDate) AS LastDate, " +
                "MIN(Duration) AS Shortest, MAX(Duration) AS Longest, " +
                "MIN(Identifier) AS FirstId, MAX(Identifier) AS LastId, " +
                "MIN(ObservedAt) AS FirstOffset, MAX(ObservedAt) AS LastOffset FROM events;"), evaluator)
            .ToListAsync());
        var row = Assert.Single(result.Rows);

        Assert.Equal(firstDate, row["FirstDate"]);
        Assert.Equal(lastDate, row["LastDate"]);
        Assert.Equal(TimeSpan.FromHours(2), row["Shortest"]);
        Assert.Equal(TimeSpan.FromHours(8), row["Longest"]);
        Assert.Equal(firstGuid, row["FirstId"]);
        Assert.Equal(lastGuid, row["LastId"]);
        Assert.Equal(firstOffset, row["FirstOffset"]);
        Assert.Equal(lastOffset, row["LastOffset"]);
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
            "AVG(Value) AS MeanValue, MIN(Value) AS Minimum, MAX(Value) AS Maximum, " +
            "SUM(OtherValue) AS OtherTotal, AVG(OtherValue) AS OtherMean " +
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
        Assert.Equal(300m, rows[0]["OtherTotal"]);
        Assert.Equal(150m, rows[0]["OtherMean"]);
        Assert.Equal(2m, rows[1]["GroupId"]);
        Assert.Equal(2m, rows[1]["RowCount"]);
        Assert.Equal(12m, rows[1]["Total"]);
        Assert.Equal(120m, rows[1]["OtherTotal"]);

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
    public async Task ProjectedGroupedHavingFiltersBoundedNativeResults()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateGroupedSource();
        evaluator.Connections["grouped"] = source;
        var statement = ParseSelect(
            "SELECT GroupId, SUM(Value) AS Total FROM grouped " +
            "GROUP BY GroupId HAVING SUM(Value) > 20;");

        var results = await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(statement, evaluator).ToListAsync();

        Assert.Single(results.SelectMany(batch => batch.Rows));
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task UnprojectedGroupedHavingDeclinesNativeRoute()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await using var source = CreateGroupedSource(throwOnRowRead: false);
        evaluator.Connections["grouped"] = source;
        var statement = ParseSelect(
            "SELECT GroupId FROM grouped GROUP BY GroupId HAVING COUNT(*) > 1;");

        await new SelectStatementHandler(NullLogger.Instance).EvaluateQuery(statement, evaluator).ToListAsync();

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

        var countOnly = ParseSelect(
            "SELECT EventDate, COUNT(*) AS RowCount FROM events GROUP BY EventDate;");
        var countRows = Assert.Single(await new SelectStatementHandler(NullLogger.Instance)
            .EvaluateQuery(countOnly, evaluator).ToListAsync()).Rows;
        Assert.Equal(2m, Assert.Single(countRows, row => row["EventDate"] == null)["RowCount"]);
        Assert.Equal(2m, Assert.Single(countRows, row => Equals(row["EventDate"], firstDate))["RowCount"]);
        Assert.Equal(0, source.RowReadAttempts);
    }

    [Fact]
    public async Task GroupedNativePlannerMatchesRowPipeline()
    {
        var definitions = new[]
        {
            new ColumnDefinition("GroupId", "INT", false),
            new ColumnDefinition("Value", "INT", false)
        };
        var logicalSchema = definitions.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var firstRows = CreateRows((1, 10), (null, 5), (1, null), (2, 3));
        var secondRows = CreateRows((2, 9), (null, 7), (1, 20));
        await using var native = new NativeOnlyDataSource(new[]
        {
            ColumnBatchAdapter.FromDataTable(firstRows, logicalSchema),
            ColumnBatchAdapter.FromDataTable(secondRows, logicalSchema)
        }, throwOnRowRead: true);
        await using var rowSource = new InMemoryDataSource();
        rowSource.SetSchema(definitions);
        await rowSource.WriteBatches(new[] { firstRows, secondRows }.ToAsyncEnumerable());
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.Connections["native_grouped"] = native;
        evaluator.Connections["row_grouped"] = rowSource;
        const string projection =
            "GroupId, COUNT(*) AS RowCount, COUNT(Value) AS ValueCount, " +
            "SUM(Value) AS Total, AVG(Value) AS MeanValue, MIN(Value) AS Minimum, MAX(Value) AS Maximum";
        var handler = new SelectStatementHandler(NullLogger.Instance);

        var nativeRows = (await handler.EvaluateQuery(ParseSelect(
            $"SELECT {projection} FROM native_grouped WHERE Value > 4 GROUP BY GroupId;"), evaluator).ToListAsync())
            .SelectMany(batch => batch.Rows).Select(Normalize).OrderBy(value => value).ToArray();
        var rowRows = (await handler.EvaluateQuery(ParseSelect(
            $"SELECT {projection} FROM row_grouped WHERE Value > 4 GROUP BY GroupId;"), evaluator).ToListAsync())
            .SelectMany(batch => batch.Rows).Select(Normalize).OrderBy(value => value).ToArray();

        Assert.Equal(rowRows, nativeRows);
        Assert.Equal(0, native.RowReadAttempts);

        static DataTable CreateRows(params (int? Key, int? Value)[] values)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "GroupId", "Value" });
            foreach (var (key, value) in values)
            {
                var row = table.NewRow();
                row["GroupId"] = key.HasValue ? (decimal)key.Value : null;
                row["Value"] = value.HasValue ? (decimal)value.Value : null;
                table.Rows.Add(row);
            }
            return table;
        }

        static string Normalize(Row row)
            => string.Join("|", new[]
            {
                row["GroupId"]?.ToString() ?? "NULL",
                Convert.ToDecimal(row["RowCount"]).ToString(),
                Convert.ToDecimal(row["ValueCount"]).ToString(),
                Convert.ToDecimal(row["Total"]).ToString(),
                Convert.ToDecimal(row["MeanValue"]).ToString(),
                Convert.ToDecimal(row["Minimum"]).ToString(),
                Convert.ToDecimal(row["Maximum"]).ToString()
            });
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
        var lineage = evaluator.LineageTracker.GetLineage("#dest").ToList();
        Assert.Contains(lineage, entry => entry.Operation == "SELECT INTO" && entry.SourceTables.Contains("col"));
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
            new ColumnBatchField("Value", typeof(int), "INT"),
            new ColumnBatchField("OtherValue", typeof(int), "INT")
        });
        return new NativeOnlyDataSource(new[]
        {
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<int>(new[] { 1, 2, 1 }, 3),
                new ColumnBuffer<int>(new[] { 10, 5, 0 }, 3, new byte[] { 0b0000_0100 }),
                new ColumnBuffer<int>(new[] { 100, 50, 0 }, 3, new byte[] { 0b0000_0100 })
            }, 3),
            new ColumnBatch(schema, new IColumnBuffer[]
            {
                new ColumnBuffer<int>(new[] { 1, 2 }, 2),
                new ColumnBuffer<int>(new[] { 20, 7 }, 2),
                new ColumnBuffer<int>(new[] { 200, 70 }, 2)
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
