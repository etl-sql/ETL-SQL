using ETL_SQL.Common;
using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Storage
{
    /// <summary>
    /// A data source that wraps a session variable, allowing data to be written to
    /// and read from @Variables using SELECT INTO and standard SELECT FROM syntax.
    /// </summary>
    public class VariableDataSource : IDataSource
    {
        private readonly string _variableName;
        private readonly IExecutionContext _context;
        private readonly InMemoryDataSource _inner;

        public string Path => _variableName;
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "VARIABLE";

        public VariableDataSource(string variableName, IExecutionContext context)
        {
            _variableName = variableName;
            _context = context;
            _inner = new InMemoryDataSource 
            { 
                Validator = context as IDataValidator,
                ExecutionContext = context,
                MaxInMemoryBatches = context.MaxInMemoryBatches
            };
        }

        private bool _loaded = false;
        private async Task EnsureLoaded()
        {
            if (_loaded) return;
            if (_context.ContainsVariable(_variableName))
            {
                var val = _context.GetVariable(_variableName);
                if (val is DataTable dt)
                {
                    await _inner.WriteBatches(new[] { dt }.ToAsyncEnumerable());
                }
            }
            _loaded = true;
        }

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            await EnsureLoaded();
            await foreach (var batch in _inner.ReadBatches(batchSize))
            {
                yield return batch;
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            await _inner.WriteBatches(batches, append);
            
            // Consolidate into a single DataTable for the variable.
            // This ensures that @Variables behave like standard DataTables for all other engine operations.
            var result = new DataTable();
            bool columnSet = false;
            
            await foreach (var batch in _inner.ReadBatches())
            {
                if (!columnSet)
                {
                    result.SetColumns(batch.ColumnNames);
                    columnSet = true;
                }
                foreach (var row in batch.Rows)
                {
                    await result.AddRowAsync(row);
                }
            }
            
            if (!_context.ContainsVariable(_variableName))
            {
                _context.DeclareVariable(_variableName, result);
            }
            else
            {
                _context.SetVariable(_variableName, result);
            }

        }

        public Task TruncateAsync() => _inner.TruncateAsync();
        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            await EnsureLoaded();
            return await _inner.GetColumnsAsync();
        }
        public object? Snapshot() => _inner.Snapshot();
        public void Restore(object? snapshot) => _inner.Restore(snapshot);
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
        }
    }
}
