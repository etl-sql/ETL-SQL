using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class SelectIntoFailureCleanupTests
{
    // A failed SELECT INTO must not leave a partial load behind: the extents it completed before the
    // source failed are deleted with the rest. The Stress scale certification found four completed
    // extents surviving a failure at 80% of a 5M-row input.
    [Fact]
    public async Task FailedSelectInto_DeletesCompletedSpillExtentsAndLeavesTargetEmpty()
    {
        await using var provider = (ServiceProvider)global::ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider();
        var ev = provider.GetRequiredService<Evaluator>();
        ev.IsPersistentSession = false;
        ev.TempTableSpillThresholdRows = 1;
        ev.BatchSize = 50; // deliver each source batch to the target before the source fails
        await ev.Evaluate(TestHelpers.Parse("SELECT 1 AS grp, 1.0 AS val INTO #target;"));
        var target = Assert.IsType<InMemoryDataSource>(await ev.ResolveDataSourceAsync(new TableReference("#target")));
        target.SpillExtentTargetBytes = 1; // every spilled batch completes its own extent
        ev.Connections["#faulty"] = new FailingSource(batches: 6, rowsPerBatch: 50);

        var failure = await Record.ExceptionAsync(() => ev.Evaluate(TestHelpers.Parse("SELECT grp, val INTO #target FROM #faulty;")));

        Assert.IsType<ExecutionException>(failure);
        Assert.True(ev.Telemetry.SpillExtentCount > 1, $"Expected several completed extents, saw {ev.Telemetry.SpillExtentCount}.");
        Assert.Equal(0, target.SpillChunkCount);
        var spillRoot = ev.SpillStore.RootPath;
        var survivors = Directory.Exists(spillRoot) ? Directory.GetFiles(spillRoot, "*", SearchOption.AllDirectories) : [];
        Assert.True(survivors.Length == 0, "Spill files survived the failed SELECT INTO: " + string.Join(", ", survivors.Select(Path.GetFileName)));
        var rows = 0;
        await foreach (var batch in target.ReadBatches()) rows += batch.Rows.Count;
        Assert.Equal(0, rows);
    }

    private sealed class FailingSource(int batches, int rowsPerBatch) : IDataSource
    {
        public string Path => "";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "FAULTY";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            for (var b = 0; b < batches; b++)
            {
                var table = new DataTable();
                table.SetColumns(new[] { "grp", "val" });
                for (var i = 0; i < rowsPerBatch; i++)
                {
                    var row = new Row(table.Schema);
                    row["grp"] = i % 10;
                    row["val"] = (decimal)(b * rowsPerBatch + i);
                    await table.AddRowAsync(row);
                }
                yield return table;
            }
            throw new ExecutionException("Forced source failure after the last batch.");
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> source, bool append = false) => throw new NotSupportedException();
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(new[] { "grp", "val" });
        public IDataSource WithTable(string tableName) => this;
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
