using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Spill
{
    /// <summary>
    /// Provides secure, line-by-line row writing for large disk-spilling operations.
    /// Encrypts data using the store's session key and compresses with GZip.
    /// </summary>
    public interface ISpillWriter : IAsyncDisposable
    {
        string ChunkName { get; }
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
    }
}
