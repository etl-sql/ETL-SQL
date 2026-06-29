using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Scale
{
    /// <summary>
    /// Read-only data source that <b>generates</b> its rows lazily, one batch at a time, from a set of
    /// per-column generator functions — instead of materializing them in memory like
    /// <see cref="InMemoryDataSource"/>. This is what makes the large scale tiers (50M+ rows) viable:
    /// the engine streams batches through its governed, spilling operators while the input itself never
    /// occupies more than one batch of RAM.
    ///
    /// Generation is deterministic and stateless, so the engine may read it any number of times
    /// (e.g. a self-join) and always get identical data.
    /// </summary>
    internal sealed class StreamingRowSource : IDataSource
    {
        private readonly long _rowCount;
        private readonly (string Name, Func<long, object?> Gen)[] _columns;
        private readonly string[] _columnNames;

        /// <param name="rowCount">Number of rows to generate.</param>
        /// <param name="columns">Column name + a generator invoked with the 0-based row index.</param>
        public StreamingRowSource(long rowCount, params (string Name, Func<long, object?> Gen)[] columns)
        {
            _rowCount = rowCount;
            _columns = columns;
            _columnNames = columns.Select(c => c.Name).ToArray();
        }

        public string Path => "";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "GENSTREAM";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (batchSize <= 0) batchSize = 10000;
            long produced = 0;
            while (produced < _rowCount)
            {
                var table = new DataTable();
                table.SetColumns(_columnNames);
                int n = (int)Math.Min(batchSize, _rowCount - produced);
                for (int j = 0; j < n; j++, produced++)
                {
                    var r = new Row(table.Schema);
                    foreach (var (name, gen) in _columns) r[name] = gen(produced);
                    await table.AddRowAsync(r);
                }
                yield return table;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            => throw new NotSupportedException("StreamingRowSource is read-only.");

        public Task<IEnumerable<string>> GetColumnsAsync()
            => Task.FromResult<IEnumerable<string>>(_columnNames.ToArray());

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // ── Convenience factories for the standard cert shapes ─────────────────
        public static StreamingRowSource GrpVal(long rowCount, int groups) => new(rowCount,
            ("grp", i => (int)(i % groups)),
            ("val", i => (decimal)(i + 1)));

        public static StreamingRowSource GrpBucketVal(long rowCount, int groups, int buckets) => new(rowCount,
            ("grp", i => (int)(i % groups)),
            ("bucket", i => (int)((i / groups) % buckets)),
            ("val", i => (decimal)(i + 1)));
    }
}
