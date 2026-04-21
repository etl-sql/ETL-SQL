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

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            // If the variable already has data (e.g. from a previous write), we should lead with that?
            // But usually the IDataSource is the primary source of truth during the 'SELECT' operation.
            return _inner.ReadBatches(batchSize);
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches)
        {
            await _inner.WriteBatches(batches);
            
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
        public Task<IEnumerable<string>> GetColumnsAsync() => _inner.GetColumnsAsync();
        public object? Snapshot() => _inner.Snapshot();
        public void Restore(object? snapshot) => _inner.Restore(snapshot);
        public IDataSource WithTable(string tableName) => this;

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
        }
    }
}
