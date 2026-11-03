using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Turns an object-store listing into <c>Name, FullPath, Size, LastModified, IsDirectory</c>
    /// batches of at most <c>batchSize</c> rows. <c>Size</c> is stored as <see cref="decimal"/>, the
    /// engine's runtime integer type.
    /// </summary>
    internal static class FileListingBatches
    {
        internal static readonly string[] Columns = { "Name", "FullPath", "Size", "LastModified", "IsDirectory" };

        internal static async IAsyncEnumerable<DataTable> ReadAsync(
            IAsyncEnumerable<FileMetaData> files,
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0) batchSize = 10000;
            var table = NewTable();
            var yielded = false;
            await foreach (var f in files.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await table.AddRowAsync(new Row
                {
                    ["Name"] = f.Name,
                    ["FullPath"] = f.FullPath,
                    ["Size"] = (decimal)f.Size,
                    ["LastModified"] = f.LastModified,
                    ["IsDirectory"] = f.IsDirectory
                });
                if (table.Rows.Count >= batchSize)
                {
                    yield return table;
                    yielded = true;
                    table = NewTable();
                }
            }

            // An empty listing still yields one empty batch so the schema reaches the caller.
            if (table.Rows.Count > 0 || !yielded)
                yield return table;
        }

        private static DataTable NewTable()
        {
            var table = new DataTable();
            table.SetColumns(Columns);
            return table;
        }
    }
}
