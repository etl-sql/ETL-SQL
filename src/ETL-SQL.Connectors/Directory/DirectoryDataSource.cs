using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Directory
{
    public class DirectoryDataSource : IDataSource
    {
        private readonly string _directoryPath;
        public string Path => _directoryPath;
        public IDataSource WithTable(string tableName) => this;
        public Dictionary<string, string>? Options { get; }
        public string ConnectorType => "DIRECTORY";

        public DirectoryDataSource(string path, Dictionary<string, string>? options = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Directory path cannot be empty.");
            _directoryPath = path;
            Options = options;
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "FilePath", "FileName", "Extension", "Size", "CreationTime", "LastWriteTime" });

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize)
        {
            if (!System.IO.Directory.Exists(Path)) yield break;

            var files = System.IO.Directory.GetFiles(Path);
            int count = 0;
            var currentBatch = new DataTable();
            currentBatch.SetColumns(new[] { "FileName", "Extension", "Size", "LastModified", "FullPath" });

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var row = new Row();
                row["FileName"] = info.Name;
                row["Extension"] = info.Extension;
                row["Size"] = info.Length;
                row["LastModified"] = info.LastWriteTime;
                row["IsReadOnly"] = info.IsReadOnly;
                
                currentBatch.AddRow(row);
                count++;

                if (count >= batchSize)
                {
                    yield return currentBatch;
                    currentBatch = new DataTable();
                    currentBatch.ColumnNames.AddRange(await GetColumnsAsync());
                    count = 0;
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                yield return currentBatch;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches) => throw new NotSupportedException("Writing to a DIRECTORY connection is not supported. Use file operations instead.");
        public object? Snapshot() => Path;
        public void Restore(object? snapshot) { }
        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }
    }
}

