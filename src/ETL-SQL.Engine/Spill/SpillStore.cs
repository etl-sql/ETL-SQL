using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Spill;

namespace ETL_SQL.Engine.Spill
{

    public class SpillStore : ISpillStore
    {
        private readonly string _rootPath;
        private readonly byte[] _sessionKey;
        private readonly IExecutionContext _context;
        private bool _disposed;

        public SpillStore(IExecutionContext context)
        {
            _context = context;
            _rootPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-Spill", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);

            // Generate a random 256-bit session key
            _sessionKey = RandomNumberGenerator.GetBytes(32);
        }

        public async Task<ISpillWriter> CreateWriterAsync(string chunkName)
        {
            var path = Path.Combine(_rootPath, chunkName);
            var encrypt = _context.SpillEncryptionEnabled;
            var compress = _context.SpillCompressionEnabled;
            return await Task.FromResult(new SecureSpillWriter(path, chunkName, _sessionKey, _context, encrypt, compress));
        }

        public async Task<ISpillReader> CreateReaderAsync(string chunkName)
        {
            var path = Path.Combine(_rootPath, chunkName);
            var encrypt = _context.SpillEncryptionEnabled;
            var compress = _context.SpillCompressionEnabled;
            return await Task.FromResult(new SecureSpillReader(path, chunkName, _sessionKey, encrypt, compress));
        }

        public void Cleanup()
        {
            if (Directory.Exists(_rootPath))
            {
                try 
                { 
                    Directory.Delete(_rootPath, true); 
                } 
                catch (Exception ex)
                { 
                    _context.Logger.Warning("Failed to cleanup spill directory {Path}: {Message}", _rootPath, ex.Message);
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Cleanup();
                Array.Clear(_sessionKey); // Wipe the key from memory
                _disposed = true;
            }
        }

        private class SecureSpillWriter : ISpillWriter
        {
            private readonly FileStream _fileStream;
            private readonly CryptoStream? _cryptoStream;
            private readonly GZipStream? _gzipStream;
            private readonly StreamWriter _writer;
            private readonly IExecutionContext _context;
            private readonly string _chunkName;

            public string ChunkName => _chunkName;

            public SecureSpillWriter(string path, string chunkName, byte[] key, IExecutionContext context, bool encrypt, bool compress)
            {
                _chunkName = chunkName;
                _context = context;
                _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                
                Stream current = _fileStream;

                if (encrypt)
                {
                    using var aes = Aes.Create();
                    aes.Key = key;
                    aes.GenerateIV();
                    _fileStream.Write(aes.IV, 0, aes.IV.Length); // Write IV to start of file

                    var encryptor = aes.CreateEncryptor(key, aes.IV);
                    _cryptoStream = new CryptoStream(_fileStream, encryptor, CryptoStreamMode.Write);
                    current = _cryptoStream;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, CompressionLevel.Optimal);
                    current = _gzipStream;
                }

                _writer = new StreamWriter(current, Encoding.UTF8);
            }

            public async Task WriteRowAsync(Row row)
            {
                var json = JsonSerializer.Serialize(row.Columns);
                if (_context.TelemetryEnabled)
                {
                    // Fast approximation (+2 for newline)
                    _context.TotalSpilledBytes += json.Length + 2; 
                }
                await _writer.WriteLineAsync(json);
            }

            public async Task WriteRowsAsync(IEnumerable<Row> rows)
            {
                foreach (var r in rows) await WriteRowAsync(r);
            }

            public async ValueTask DisposeAsync()
            {
                await _writer.DisposeAsync();
                if (_gzipStream != null) await _gzipStream.DisposeAsync();
                if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
                await _fileStream.DisposeAsync();
            }
        }

        private class SecureSpillReader : ISpillReader
        {
            private readonly FileStream? _fileStream;
            private readonly CryptoStream? _cryptoStream;
            private readonly GZipStream? _gzipStream;
            private readonly StreamReader? _reader;
            private readonly string _chunkName;

            public string ChunkName => _chunkName;

            public SecureSpillReader(string path, string chunkName, byte[] key, bool encrypt, bool compress)
            {
                _chunkName = chunkName;
                if (!File.Exists(path))
                {
                    _fileStream = null!;
                    return;
                }

                _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Stream current = _fileStream;

                if (encrypt)
                {
                    byte[] iv = new byte[16];
                    _fileStream.ReadExactly(iv, 0, 16);

                    using var aes = Aes.Create();
                    var decryptor = aes.CreateDecryptor(key, iv);
                    _cryptoStream = new CryptoStream(_fileStream, decryptor, CryptoStreamMode.Read);
                    current = _cryptoStream;
                }

                if (compress)
                {
                    _gzipStream = new GZipStream(current, CompressionMode.Decompress);
                    current = _gzipStream;
                }

                _reader = new StreamReader(current, Encoding.UTF8);
            }

            public async Task<Row?> ReadRowAsync()
            {
                if (_reader == null) return null;
                var line = await _reader.ReadLineAsync();
                if (line == null) return null;

                var cols = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
                if (cols == null) return null;

                var row = new Row();
                foreach (var kvp in cols) row[kvp.Key] = UnwrapJsonValue(kvp.Value);
                return row;
            }

            public async IAsyncEnumerable<Row> AsEnumerableAsync()
            {
                while (true)
                {
                    var row = await ReadRowAsync();
                    if (row == null) break;
                    yield return row;
                }
            }

            private static object? UnwrapJsonValue(JsonElement element) =>
                element.ValueKind switch
                {
                    JsonValueKind.Number => element.TryGetDecimal(out var d) ? d : (object?)element.GetDouble(),
                    JsonValueKind.String => decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (object?)d : (DateTime.TryParse(element.GetString() ?? "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString()),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Array => element,  // Preserve array for downstream unwrapping
                    JsonValueKind.Object => element, // Preserve object
                    _ => element.GetRawText()
                };

            public async ValueTask DisposeAsync()
            {
                if (_reader != null) _reader.Dispose();
                if (_gzipStream != null) await _gzipStream.DisposeAsync();
                if (_cryptoStream != null) await _cryptoStream.DisposeAsync();
                if (_fileStream != null) await _fileStream.DisposeAsync();
            }
        }
    }
}
