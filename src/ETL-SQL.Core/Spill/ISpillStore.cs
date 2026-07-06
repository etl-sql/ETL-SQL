using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Spill;
/// <summary>
/// Provides secure, line-by-line row writing for large disk-spilling operations.
/// Encrypts data using the store's session key and compresses with GZip.
/// </summary>
public interface ISpillWriter : IAsyncDisposable
{
    string ChunkName { get; }
    long BytesWritten { get; }
    Task WriteRowAsync(Row row);
    Task WriteRowsAsync(IEnumerable<Row> rows);
}

/// <summary>
/// Provides secure, line-by-line row reading for large disk-spilling operations.
/// Decrypts and decompresses data on the fly.
/// </summary>
public interface ISpillReader : IAsyncDisposable
{
    string ChunkName { get; }
    Task<Row?> ReadRowAsync();
    IAsyncEnumerable<Row> AsEnumerableAsync();
}

/// <summary>Optional capability for writing owned native batches without reconstructing rows.</summary>
public interface IColumnarSpillWriter
{
    Task WriteBatchAsync(ColumnBatch batch);
}

/// <summary>
/// Optional capability implemented by spill readers that can expose native typed batches without
/// reconstructing a <see cref="Row"/> object graph. A reader must be consumed through either this
/// interface or <see cref="ISpillReader"/>'s row methods, not both.
/// </summary>
public interface IColumnarSpillReader
{
    IAsyncEnumerable<ColumnBatch> AsColumnBatchesAsync();
}

/// <summary>
/// Centralized store for encrypted and compressed session data.
/// Owns the temp directory and a unique AES-256 session key.
/// </summary>
public interface ISpillStore : IDisposable
{
    Task<ISpillWriter> CreateWriterAsync(string chunkName);
    Task<ISpillReader> CreateReaderAsync(string chunkName);
    /// <summary>Deletes a specific chunk from the store.</summary>
    void DeleteChunk(string chunkName);
    void Cleanup();
    /// <summary>Whether this store should persist after disposal.</summary>
    bool IsPersistent { get; set; }
    /// <summary>The root directory of the spill store.</summary>
    string RootPath { get; }
}
