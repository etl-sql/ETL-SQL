using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// Manages database metadata (tables, columns) and connection information for the Language Server.
    /// Supports both global connections and document-specific connections (defined in-script).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="connectors">The registry of available database connectors.</param>
    public class MetadataManager(ILogger<MetadataManager> logger, IConnectorRegistry connectors) : IMetadataManager
    {
        private readonly ConcurrentDictionary<string, ConnectionInfo> _globalConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<ConnectionInfo>> _docConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _views = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _docTempTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _columns = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Normalizes a document URI for consistent cache lookups.</summary>
        /// <param name="uri">The document URI to normalize.</param>
        /// <returns>A normalized URI string.</returns>
        private string NormalizeUri(string? uri)
        {
            if (string.IsNullOrEmpty(uri)) return "";
            try
            {
                var decoded = Uri.UnescapeDataString(uri);
                // Ensure file:/// is preserved but normalized
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


        /// <summary>Registers a global connection that is available across all documents.</summary>
        /// <param name="name">The unique name of the connection.</param>
        /// <param name="type">The connector type (e.g., MSSQL, POSTGRES).</param>
        /// <param name="connectionString">The connection string.</param>
        public void RegisterConnection(string name, string type, string connectionString)
        {
            _globalConnections[name] = new ConnectionInfo(name, type, connectionString, false);
            logger.LogInformation("Registered global connection {Name} of type {Type}", name, type);
            // Clear cache for this connection across all documents to be safe
            ClearCacheForConnection(name);
        }

        /// <summary>Registers a connection scoped to a specific document.</summary>
        /// <param name="uri">The document URI.</param>
        /// <param name="name">The unique name of the connection.</param>
        /// <param name="type">The connector type.</param>
        /// <param name="connectionString">The connection string.</param>
        public void RegisterDocumentConnection(string uri, string name, string type, string connectionString)
        {
            var normalizedUri = NormalizeUri(uri);
            var list = _docConnections.GetOrAdd(normalizedUri, _ => new List<ConnectionInfo>());
            lock (list)
            {
                list.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                list.Add(new ConnectionInfo(name, type, connectionString, true));
            }
            logger.LogInformation("Registered document connection {Name} for {Uri} (Normalized: {Normalized})", name, uri, normalizedUri);
            ClearCacheForDocument(normalizedUri, name);
        }

        /// <summary>Clears all connections scoped to the specified document.</summary>
        /// <param name="uri">The document URI.</param>
        public void ClearDocumentConnections(string uri)
        {
            var normalizedUri = NormalizeUri(uri);
            if (_docConnections.TryRemove(normalizedUri, out _))
            {
                logger.LogDebug("Cleared document connections for {Uri}.", normalizedUri);
                ClearCacheForDocument(normalizedUri);
            }
        }


        /// <summary>Returns a list of all currently registered connections (global + optional document-specific).</summary>
        /// <param name="uri">The optional document URI to include local connections for.</param>
        /// <returns>A list of connection information objects.</returns>
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

        /// <summary>Finds a connection by name, checking document-specific first if a URI is provided, then global.</summary>
        /// <param name="connectionName">The name of the connection.</param>
        /// <param name="uri">The optional document URI.</param>
        /// <returns>The connection info if found; otherwise, null.</returns>
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
            if (DebugMode) logger.LogWarning("LSP: Connection '{Name}' NOT FOUND in global or doc connections (URI: {Uri})", connectionName, normalizedUri);
            return null;
        }

        /// <summary>Gets or sets a value indicating whether debug logging is enabled.</summary>
        public bool DebugMode { get; set; } = false;

        /// <summary>Returns the resolved type of a connection (e.g., MSSQL, POSTGRES, DOCKER).</summary>
        public string? GetConnectionType(string connectionName, string? uri = null)
        {
            return GetConnection(connectionName, uri)?.Type;
        }

        /// <summary>Asynchronously retrieves a list of table names for the specified connection.</summary>
        /// <param name="connectionName">The name of the connection.</param>
        /// <param name="uri">The optional document URI for local connection lookups.</param>
        /// <returns>A collection of table names, from cache if available.</returns>
        public async Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null)
        {
            try
            {
                var conn = GetConnection(connectionName, uri);
                if (conn == null) return Enumerable.Empty<string>();

                var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null);
                if (_tables.TryGetValue(key, out var cached)) return cached;

                var connector = connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                var tables = (await connector.GetTablesAsync(conn.ConnectionString)).ToList();
                
                // Whitelist virtual DUAL table for all connections to support SELECT @var without FROM
                if (!tables.Contains("DUAL", StringComparer.OrdinalIgnoreCase))
                {
                    tables.Insert(0, "DUAL");
                }

                // Merge in document-level temp tables if any
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
                logger.LogError(ex, "GetTablesAsync ERROR: {Message}", ex.Message);
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

                var connector = connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                var views = (await connector.GetViewsAsync(conn.ConnectionString)).ToList();
                _views[key] = views;
                return views;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GetViewsAsync ERROR: {Message}", ex.Message);
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
            
            // Register columns for the temp table using a consistent global key for the document
            var cacheKey = GetTempTableCacheKey(normalizedUri, name);
            _columns[cacheKey] = columns;
            logger.LogInformation("Registered temp table {Name} for {Uri}", name, normalizedUri);
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
                logger.LogInformation("Cleared {Count} temp tables for {Uri}", tables.Count, normalizedUri);
            }
        }

        private string GetTempTableCacheKey(string normalizedUri, string tableName)
        {
             return $"{normalizedUri}:{tableName}".ToUpperInvariant();
        }

        /// <summary>Asynchronously retrieves a list of column names for the specified table and connection.</summary>
        /// <param name="connectionName">The name of the connection.</param>
        /// <param name="tableName">The name of the table.</param>
        /// <param name="uri">The optional document URI for local connection lookups.</param>
        /// <returns>A collection of column names, from cache if available.</returns>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null)
        {
            try
            {
                var normalizedUri = NormalizeUri(uri);
                
                // 1. Check if it's a temp table in the document
                if (tableName.StartsWith("#") && !string.IsNullOrEmpty(normalizedUri))
                {
                    var tempKey = GetTempTableCacheKey(normalizedUri, tableName);
                    if (_columns.TryGetValue(tempKey, out var tempCols)) return tempCols;
                }

                // 1.1 Check if it's the virtual DUAL table
                if (tableName.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "DUMMY" };
                }

                var conn = GetConnection(connectionName, uri);
                if (conn == null) 
                {
                    logger.LogWarning("GetColumnsAsync: Connection '{Connection}' not found for URI '{Uri}'", connectionName, uri);
                    return Enumerable.Empty<string>();
                }

                var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null) + ":" + tableName.ToUpperInvariant();
                if (_columns.TryGetValue(key, out var cached)) return cached;

                var connector = connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                var columns = (await connector.GetColumnsAsync(conn.ConnectionString, tableName)).ToList();
                _columns[key] = columns;
                return columns;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GetColumnsAsync ERROR: {Message}", ex.Message);
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>Returns a collection of all registered connector names.</summary>
        /// <returns>Enumerable of connector types.</returns>
        public IEnumerable<string> GetRegisteredNames() => connectors.GetRegisteredNames();

        /// <summary>Returns the connector instance for the specified type name.</summary>
        /// <param name="name">The name of the connector.</param>
        /// <returns>IConnector instance or null.</returns>
        public IConnector? GetConnector(string name) => connectors.GetConnector(name);

        /// <summary>Generates a composite cache key for connection-specific metadata.</summary>
        /// <param name="connectionName">The connection name.</param>
        /// <param name="uri">The optional URI.</param>
        /// <returns>A uppercase cache key string.</returns>
        private string GetCacheKey(string connectionName, string? uri)
        {
            var normalizedUri = NormalizeUri(uri);
            return $"{(string.IsNullOrEmpty(normalizedUri) ? "GLOBAL" : normalizedUri)}:{connectionName}".ToUpperInvariant();
        }

        /// <summary>Clears all cached metadata (tables and columns).</summary>
        public void ClearCache()
        {
            _tables.Clear();
            _views.Clear();
            _columns.Clear();
            _docTempTables.Clear();
        }

        /// <summary>Clears cached metadata for a specific connection name across all documents.</summary>
        /// <param name="connectionName">The name of the connection to clear.</param>
        private void ClearCacheForConnection(string connectionName)
        {
            // Find all cache keys that end with the connection name (global or document-specific)
            var keysToRemove = _tables.Keys.Where(k => k.EndsWith($":{connectionName.ToUpperInvariant()}")).ToList();
            foreach (var key in keysToRemove) _tables.TryRemove(key, out _);

            var colKeysToRemove = _columns.Keys.Where(k => k.Contains($":{connectionName.ToUpperInvariant()}:")).ToList();
            foreach (var key in colKeysToRemove) _columns.TryRemove(key, out _);
        }

        /// <summary>Clears all cached table/column metadata for a specific document URI.</summary>
        public void ClearCacheForUri(string uri) => ClearCacheForDocument(uri);

        /// <summary>Clears cached metadata for a specific document, or a specific connection in that document.</summary>
        /// <param name="uri">The document URI.</param>
        /// <param name="connectionName">Optional connection name to target.</param>
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

