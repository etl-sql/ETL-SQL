using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Directory
{
    public class DirectoryDataSource : IDataSource
    {
        private readonly string _directoryPath;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        public string Path => _directoryPath;
        public IDataSource WithTable(string tableName) => this;
        public Dictionary<string, string>? Options { get; }
        public string ConnectorType => "DIRECTORY";

        public DirectoryDataSource(IExecutionContext context, string path, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;
            _directoryPath = context.ResolvePath(path.Trim('\'', '\"', ' ', '\t', '\r', '\n'));

            // Security Hardening: Defense in depth
            context.SecurityService.ValidatePath(_directoryPath);

            Options = options;

            bool create = true;
            if (options != null && options.TryGetValue("CREATE", out var createStr))
            {
                create = createStr.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                         createStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            }

            if (create && !System.IO.Directory.Exists(_directoryPath) && context != null && !context.IsWhatIf)
            {
                System.IO.Directory.CreateDirectory(_directoryPath);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "FileName", "Path", "Extension", "Size", "LastModified", "IsReadOnly", "CreationTime" });

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize)
        {
            if (!System.IO.Directory.Exists(Path)) yield break;

            var files = System.IO.Directory.GetFiles(Path);
            int count = 0;
            var currentBatch = new DataTable();
            currentBatch.SetColumns(await GetColumnsAsync());

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var row = new Row();
                row["FileName"] = info.Name;
                row["Path"] = info.FullName;
                row["Extension"] = info.Extension;
                row["Size"] = (decimal)info.Length;
                row["LastModified"] = info.LastWriteTime;
                row["IsReadOnly"] = info.IsReadOnly;
                row["CreationTime"] = info.CreationTime;

                await currentBatch.AddRowAsync(row);
                count++;

                if (count >= batchSize)
                {
                    yield return currentBatch;
                    currentBatch = new DataTable();
                    currentBatch.SetColumns(await GetColumnsAsync());
                    count = 0;
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                yield return currentBatch;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("Writing to a DIRECTORY connection is not supported. Use file operations instead.");
        public object? Snapshot() => Path;
        public void Restore(object? snapshot) { }
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}
