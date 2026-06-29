using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Scale
{
    /// <summary>
    /// Read-only data source that <b>generates</b> its rows lazily, one batch at a time, instead of
    /// materializing them in memory like <see cref="InMemoryDataSource"/>. This is what makes the
    /// large scale tiers (e.g. 50M rows) viable: the engine streams batches through its governed,
    /// spilling operators while the input itself never occupies more than one batch of RAM.
    ///
    /// Generation is deterministic and stateless, so the engine may read it any number of times
    /// (e.g. a self-join) and always get identical data.
    /// </summary>
    internal sealed class StreamingRowSource : IDataSource
    {
        private readonly int _rowCount;
        private readonly int _groups;
        private readonly int _buckets; // <= 0 => no "bucket" column (plain grp/val shape)
        private readonly string[] _columns;

        public StreamingRowSource(int rowCount, int groups, int buckets = 0)
        {
            _rowCount = rowCount;
            _groups = Math.Max(1, groups);
            _buckets = buckets;
            _columns = buckets > 0 ? new[] { "grp", "bucket", "val" } : new[] { "grp", "val" };
        }

        public string Path => "";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "GENSTREAM";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (batchSize <= 0) batchSize = 10000;
            int produced = 0;
            while (produced < _rowCount)
            {
                var table = new DataTable();
                table.SetColumns(_columns);
                int n = Math.Min(batchSize, _rowCount - produced);
                for (int j = 0; j < n; j++, produced++)
                {
                    var r = new Row(table.Schema);
                    r["grp"] = produced % _groups;
                    if (_buckets > 0) r["bucket"] = (produced / _groups) % _buckets;
                    r["val"] = (decimal)(produced + 1);
                    await table.AddRowAsync(r);
                }
                yield return table;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            => throw new NotSupportedException("StreamingRowSource is read-only.");

        public Task<IEnumerable<string>> GetColumnsAsync()
            => Task.FromResult<IEnumerable<string>>(_columns.ToArray());

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
