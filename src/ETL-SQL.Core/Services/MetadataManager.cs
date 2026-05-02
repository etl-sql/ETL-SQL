using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Services
{
    /// <summary>
    /// Manages database metadata (tables, columns) and connection information.
    /// Supports both global connections and document-specific connections (defined in-script).
    /// </summary>
    public class MetadataManager : IMetadataManager
    {
        private readonly ILogger _logger;
        private readonly IConnectorRegistry _connectors;
        private readonly ConcurrentDictionary<string, ConnectionInfo> _globalConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<ConnectionInfo>> _docConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _views = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _docTempTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _columns = new(StringComparer.OrdinalIgnoreCase);

        public MetadataManager(ILogger logger, IConnectorRegistry connectors)
        {
            _logger = logger;
            _connectors = connectors;
        }

        /// <summary>Normalizes a document URI for consistent cache lookups.</summary>
        private string NormalizeUri(string? uri)
        {
            if (string.IsNullOrEmpty(uri)) return "";
            try
            {
                var decoded = Uri.UnescapeDataString(uri);
                if (decoded.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var uriObj = new Uri(decoded);
                    return uriObj.ToString().ToLowerInvariant();
                }
                return decoded.ToLowerInvariant();
            }
            catch
            {
                return (uri ?? "").ToLowerInvariant();
            }
        }

        public void RegisterConnection(string name, string type, string connectionString)
        {
            _globalConnections[name] = new ConnectionInfo(name, type, connectionString, false);
            _logger.Info("Registered global connection {Name} of type {Type}", name, type);
            ClearCacheForConnection(name);
        }

        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString)
        {
            var normalizedUri = NormalizeUri(uri);
            var list = _docConnections.GetOrAdd(normalizedUri, _ => new List<ConnectionInfo>());
            lock (list)
            {
                list.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                list.Add(new ConnectionInfo(name, type, connectionString, true));
            }
            _logger.Info("Registered document connection {Name} for {Uri}", name, uri);
            ClearCacheForDocument(normalizedUri, name);
        }

        public void ClearDocumentConnections(string uri)
        {
            var normalizedUri = NormalizeUri(uri);
            if (_docConnections.TryRemove(normalizedUri, out _))
            {
                _logger.Debug("Cleared document connections for {Uri}.", normalizedUri);
                ClearCacheForDocument(normalizedUri);
            }
        }

        public List<ConnectionInfo> GetConnections(string? uri = null)
        {
            var result = _globalConnections.Values.ToList();
            var normalizedUri = NormalizeUri(uri);
            if (!string.IsNullOrEmpty(uri) && _docConnections.TryGetValue(normalizedUri, out var docs))
            {
                lock (docs)
                {
                    result.AddRange(docs);
                }
            }
            return result;
        }

        private ConnectionInfo? GetConnection(string connectionName, string? uri = null)
        {
            var normalizedUri = NormalizeUri(uri);
            if (!string.IsNullOrEmpty(uri) && _docConnections.TryGetValue(normalizedUri, out var docs))
            {
                lock (docs)
                {
                    var found = docs.FirstOrDefault(c => c.Name.Equals(connectionName, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                }
            }
            
            if (_globalConnections.TryGetValue(connectionName, out var global)) return global;
            return null;
        }

        public bool DebugMode { get; set; } = false;

        public string? GetConnectionType(string connectionName, string? uri = null)
        {
            return GetConnection(connectionName, uri)?.Type;
        }

        public async Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null)
        {
            try
            {
                var conn = GetConnection(connectionName, uri);
                if (conn == null) return Enumerable.Empty<string>();

                var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null);
                if (_tables.TryGetValue(key, out var cached)) return cached;

                var connector = _connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
                var tables = (await source.GetTablesAsync()).ToList();
                
                if (!tables.Contains("DUAL", StringComparer.OrdinalIgnoreCase))
                {
                    tables.Insert(0, "DUAL");
                }

                var normalizedUri = NormalizeUri(uri);
                if (!string.IsNullOrEmpty(normalizedUri) && _docTempTables.TryGetValue(normalizedUri, out var temps))
                {
                    tables.AddRange(temps.Where(t => !tables.Contains(t, StringComparer.OrdinalIgnoreCase)));
                }

                _tables[key] = tables;
                return tables;
            }
            catch (Exception ex)
            {
                _logger.Error("GetTablesAsync ERROR: {Message}", ex);
                return Enumerable.Empty<string>();
            }
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null)
        {
            try
            {
                var conn = GetConnection(connectionName, uri);
                if (conn == null) return Enumerable.Empty<string>();

                var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null);
                if (_views.TryGetValue(key, out var cached)) return cached;

                var connector = _connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
                var views = Enumerable.Empty<string>();
                if (source is IDatabaseSource db)
                {
                    views = (await db.GetViewsAsync()).ToList();
                }
                
                _views[key] = views.ToList();
                return views;
            }
            catch (Exception ex)
            {
                _logger.Error("GetViewsAsync ERROR: {Message}", ex);
                return Enumerable.Empty<string>();
            }
        }

        public Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null)
        {
            var normalizedUri = NormalizeUri(uri);
            if (_docTempTables.TryGetValue(normalizedUri, out var list)) return Task.FromResult((IEnumerable<string>)list);
            return Task.FromResult(Enumerable.Empty<string>());
        }

        public void RegisterTempTable(string uri, string name, List<string> columns)
        {
            var normalizedUri = NormalizeUri(uri);
            var list = _docTempTables.GetOrAdd(normalizedUri, _ => new List<string>());
            lock (list)
            {
                if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            }
            
            var cacheKey = GetTempTableCacheKey(normalizedUri, name);
            _columns[cacheKey] = columns;
            _logger.Info("Registered temp table {Name} for {Uri}", name, normalizedUri);
        }

        public void ClearTempTables(string uri)
        {
            var normalizedUri = NormalizeUri(uri);
            if (_docTempTables.TryRemove(normalizedUri, out var tables))
            {
                foreach (var table in tables)
                {
                    var cacheKey = GetTempTableCacheKey(normalizedUri, table);
                    _columns.TryRemove(cacheKey, out _);
                }
                _logger.Info("Cleared {Count} temp tables for {Uri}", tables.Count, normalizedUri);
            }
        }

        private string GetTempTableCacheKey(string normalizedUri, string tableName)
        {
             return $"{normalizedUri}:{tableName}".ToUpperInvariant();
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null)
        {
            try
            {
                var normalizedUri = NormalizeUri(uri);
                
                if (tableName.StartsWith("#") && !string.IsNullOrEmpty(normalizedUri))
                {
                    var tempKey = GetTempTableCacheKey(normalizedUri, tableName);
                    if (_columns.TryGetValue(tempKey, out var tempCols)) return tempCols;
                }

                if (tableName.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "DUMMY" };
                }

                var conn = GetConnection(connectionName, uri);
                if (conn == null) return Enumerable.Empty<string>();

                var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null) + ":" + tableName.ToUpperInvariant();
                if (_columns.TryGetValue(key, out var cached)) return cached;

                var connector = _connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
                var columns = Enumerable.Empty<string>();
                
                if (source is IDatabaseSource db)
                {
                    columns = (await db.GetColumnsAsync(tableName)).ToList();
                }
                else
                {
                    columns = (await source.GetColumnsAsync()).ToList();
                }

                _columns[key] = columns.ToList();
                return columns;
            }
            catch (Exception ex)
            {
                _logger.Error("GetColumnsAsync ERROR: {Message}", ex);
                return Enumerable.Empty<string>();
            }
        }

        public IEnumerable<string> GetRegisteredNames() => _connectors.GetRegisteredNames();

        public IConnector? GetConnector(string name) => _connectors.GetConnector(name);

        private string GetCacheKey(string connectionName, string? uri)
        {
            var normalizedUri = NormalizeUri(uri);
            return $"{(string.IsNullOrEmpty(normalizedUri) ? "GLOBAL" : normalizedUri)}:{connectionName}".ToUpperInvariant();
        }

        public void ClearCache()
        {
            _tables.Clear();
            _views.Clear();
            _columns.Clear();
            _docTempTables.Clear();
        }

        public void ClearCacheForUri(string uri) => ClearCacheForDocument(uri);

        private void ClearCacheForConnection(string connectionName)
        {
            var keysToRemove = _tables.Keys.Where(k => k.EndsWith($":{connectionName.ToUpperInvariant()}")).ToList();
            foreach (var key in keysToRemove) _tables.TryRemove(key, out _);

            var colKeysToRemove = _columns.Keys.Where(k => k.Contains($":{connectionName.ToUpperInvariant()}:")).ToList();
            foreach (var key in colKeysToRemove) _columns.TryRemove(key, out _);
        }

        private void ClearCacheForDocument(string uri, string? connectionName = null)
        {
            var normalizedUri = NormalizeUri(uri);
            if (connectionName != null)
            {
                var cacheKey = GetCacheKey(connectionName, normalizedUri);
                _tables.TryRemove(cacheKey, out _);
                _views.TryRemove(cacheKey, out _);
                var colKeysToRemove = _columns.Keys.Where(k => k.StartsWith($"{cacheKey}:")).ToList();
                foreach (var k in colKeysToRemove) _columns.TryRemove(k, out _);
            }
            else
            {
                var prefix = $"{normalizedUri}:";
                var keysToRemove = _tables.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in keysToRemove) _tables.TryRemove(k, out _);

                var viewKeysToRemove = _views.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in viewKeysToRemove) _views.TryRemove(k, out _);
                
                var colKeysToRemove = _columns.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in colKeysToRemove) _columns.TryRemove(k, out _);

                _docTempTables.TryRemove(normalizedUri, out _);
            }
        }
    }
}
