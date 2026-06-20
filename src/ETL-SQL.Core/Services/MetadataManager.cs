using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Services
{
    /// <summary>
    /// Manages database metadata (tables, columns) and connection information.
    /// Supports both global connections and document-specific connections (defined in-script).
    /// </summary>
    public class MetadataManager : IMetadataManager
    {
        private static readonly TimeSpan WarehouseCacheTtl = TimeSpan.FromMinutes(5);

        private readonly ILogger _logger;
        private readonly IConnectorRegistry _connectors;
        private readonly ConcurrentDictionary<string, ConnectionInfo> _globalConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<ConnectionInfo>> _docConnections = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _views = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<string>> _docTempTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<ColumnMetadata>> _columns = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTimeOffset> _cacheTimestamps = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Task> _ongoingRefreshes = new(StringComparer.OrdinalIgnoreCase);

        public MetadataManager(ILogger logger, IConnectorRegistry connectors)
        {
            _logger = logger;
            _connectors = connectors;
        }

        private bool IsCacheValid(string cacheKey, string? connectorType)
        {
            if (!_cacheTimestamps.TryGetValue(cacheKey, out var fetchedAt)) return false;
            var connector = connectorType != null ? _connectors.GetConnector(connectorType) : null;
            if (connector?.IsDataWarehouse == true)
                return DateTimeOffset.UtcNow - fetchedAt < WarehouseCacheTtl;
            return true;
        }

        private void StampCache(string cacheKey) => _cacheTimestamps[cacheKey] = DateTimeOffset.UtcNow;

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
            TryLoadAndRefreshCache(name, connectionString, null);
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
            TryLoadAndRefreshCache(name, connectionString, normalizedUri);
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

        private string GetBasePath(string uri)
        {
            var hashIndex = uri.IndexOf('#');
            return hashIndex > -1 ? uri.Substring(0, hashIndex) : uri;
        }

        public List<ConnectionInfo> GetConnections(string? uri = null)
        {
            var result = _globalConnections.Values.ToList();
            var normalizedUri = NormalizeUri(uri);
            if (!string.IsNullOrEmpty(normalizedUri))
            {
                var basePath = GetBasePath(normalizedUri);
                foreach (var kvp in _docConnections)
                {
                    if (GetBasePath(kvp.Key) == basePath)
                    {
                        lock (kvp.Value)
                        {
                            result.AddRange(kvp.Value);
                        }
                    }
                }
            }
            return result;
        }

        private ConnectionInfo? GetConnection(string connectionName, string? uri = null)
        {
            var normalizedUri = NormalizeUri(uri);
            if (!string.IsNullOrEmpty(normalizedUri))
            {
                // Exact match first
                if (_docConnections.TryGetValue(normalizedUri, out var docs))
                {
                    lock (docs)
                    {
                        var found = docs.FirstOrDefault(c => c.Name.Equals(connectionName, StringComparison.OrdinalIgnoreCase));
                        if (found != null) return found;
                    }
                }

                // Then match across same notebook
                var basePath = GetBasePath(normalizedUri);
                if (basePath != normalizedUri)
                {
                    foreach (var kvp in _docConnections)
                    {
                        if (GetBasePath(kvp.Key) == basePath)
                        {
                            lock (kvp.Value)
                            {
                                var found = kvp.Value.FirstOrDefault(c => c.Name.Equals(connectionName, StringComparison.OrdinalIgnoreCase));
                                if (found != null) return found;
                            }
                        }
                    }
                }
            }

            if (_globalConnections.TryGetValue(connectionName, out var global)) return global;
            return null;
        }

        public bool DebugMode { get; set; } = false;
        public string? SchemaCacheDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "SchemaCache");
        public bool DisableBackgroundRefresh { get; set; } = false;

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
                if (_tables.TryGetValue(key, out var cached) && IsCacheValid(key, conn.Type)) return cached;

                // Try to load disk cache
                var diskCache = LoadSchemaFromDisk(connectionName, conn.ConnectionString);
                if (diskCache != null)
                {
                    _tables[key] = diskCache.Tables;
                    _views[key] = diskCache.Views;
                    foreach (var kvp in diskCache.Columns)
                    {
                        var colKey = key + ":" + kvp.Key.ToUpperInvariant();
                        _columns[colKey] = kvp.Value;
                    }
                    StampCache(key);

                    // Trigger async background refresh
                    TriggerBackgroundRefresh(connectionName, conn.ConnectionString, conn.IsDocument ? uri : null);

                    var tablesList = diskCache.Tables.ToList();
                    var dcNormalizedUri = NormalizeUri(uri);
                    if (!string.IsNullOrEmpty(dcNormalizedUri) && _docTempTables.TryGetValue(dcNormalizedUri, out var dcTemps))
                    {
                        tablesList.AddRange(dcTemps.Where(t => !tablesList.Contains(t, StringComparer.OrdinalIgnoreCase)));
                    }
                    return tablesList;
                }

                var connector = _connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
                var tables = (await source.GetTablesAsync()).ToList();

                var normalizedUri = NormalizeUri(uri);
                if (!string.IsNullOrEmpty(normalizedUri) && _docTempTables.TryGetValue(normalizedUri, out var temps))
                {
                    tables.AddRange(temps.Where(t => !tables.Contains(t, StringComparer.OrdinalIgnoreCase)));
                }

                if (!tables.Contains("DUAL", StringComparer.OrdinalIgnoreCase))
                {
                    tables.Add("DUAL");
                }

                _tables[key] = tables;
                StampCache(key);

                // Trigger background refresh to fetch and cache columns as well
                TriggerBackgroundRefresh(connectionName, conn.ConnectionString, conn.IsDocument ? uri : null);

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
                if (_views.TryGetValue(key, out var cached) && IsCacheValid(key, conn.Type)) return cached;

                // Try to load disk cache
                var diskCache = LoadSchemaFromDisk(connectionName, conn.ConnectionString);
                if (diskCache != null)
                {
                    _tables[key] = diskCache.Tables;
                    _views[key] = diskCache.Views;
                    foreach (var kvp in diskCache.Columns)
                    {
                        var colKey = key + ":" + kvp.Key.ToUpperInvariant();
                        _columns[colKey] = kvp.Value;
                    }
                    StampCache(key);

                    // Trigger async background refresh
                    TriggerBackgroundRefresh(connectionName, conn.ConnectionString, conn.IsDocument ? uri : null);

                    return diskCache.Views;
                }

                var connector = _connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<string>();

                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
                var views = Enumerable.Empty<string>();
                if (source is IDatabaseSource db)
                {
                    views = (await db.GetViewsAsync()).ToList();
                }

                _views[key] = views.ToList();
                StampCache(key);

                // Trigger background refresh to fetch and cache columns as well
                TriggerBackgroundRefresh(connectionName, conn.ConnectionString, conn.IsDocument ? uri : null);

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
            var result = new List<string>();
            if (!string.IsNullOrEmpty(normalizedUri))
            {
                var basePath = GetBasePath(normalizedUri);
                foreach (var kvp in _docTempTables)
                {
                    if (GetBasePath(kvp.Key) == basePath)
                    {
                        result.AddRange(kvp.Value);
                    }
                }
            }
            return Task.FromResult((IEnumerable<string>)result.Distinct());
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
            _columns[cacheKey] = columns.Select(c => new ColumnMetadata(c, "ANY")).ToList();
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
            var cols = await GetColumnDetailsAsync(connectionName, tableName, uri);
            return cols.Select(c => c.Name);
        }

        public async Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName, string? uri = null)
        {
            try
            {
                var normalizedUri = NormalizeUri(uri);

                if (tableName.StartsWith("#") && !string.IsNullOrEmpty(normalizedUri))
                {
                    // Exact match first
                    var tempKey = GetTempTableCacheKey(normalizedUri, tableName);
                    if (_columns.TryGetValue(tempKey, out var tempCols)) return tempCols;

                    // Match across same notebook
                    var basePath = GetBasePath(normalizedUri);
                    if (basePath != normalizedUri)
                    {
                        foreach (var kvp in _docTempTables)
                        {
                            if (GetBasePath(kvp.Key) == basePath && kvp.Value.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                            {
                                var relatedTempKey = GetTempTableCacheKey(kvp.Key, tableName);
                                if (_columns.TryGetValue(relatedTempKey, out var relatedCols)) return relatedCols;
                            }
                        }
                    }
                }

                if (tableName.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { new ColumnMetadata("DUMMY", "VARCHAR") };
                }

                var conn = GetConnection(connectionName, uri);
                if (conn == null) return Enumerable.Empty<ColumnMetadata>();

                var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null) + ":" + tableName.ToUpperInvariant();
                
                // If in memory cache, return immediately
                if (_columns.TryGetValue(key, out var cached) && IsCacheValid(key, conn.Type)) return cached;

                // Try to load disk cache if memory cache is empty
                var diskCache = LoadSchemaFromDisk(connectionName, conn.ConnectionString);
                if (diskCache != null)
                {
                    var connKey = GetCacheKey(connectionName, conn.IsDocument ? uri : null);
                    _tables[connKey] = diskCache.Tables;
                    _views[connKey] = diskCache.Views;
                    foreach (var kvp in diskCache.Columns)
                    {
                        var colKey = connKey + ":" + kvp.Key.ToUpperInvariant();
                        _columns[colKey] = kvp.Value;
                    }
                    StampCache(connKey);

                    // Trigger async background refresh
                    TriggerBackgroundRefresh(connectionName, conn.ConnectionString, conn.IsDocument ? uri : null);

                    if (_columns.TryGetValue(key, out var diskLoadedCols))
                    {
                        return diskLoadedCols;
                    }
                }

                // If not cached anywhere, we must fetch synchronously
                var connector = _connectors.GetConnector(conn.Type);
                if (connector == null) return Enumerable.Empty<ColumnMetadata>();

                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
                var colList = new List<ColumnMetadata>();

                var catalogProvider = source.GetCatalogProvider();
                if (catalogProvider != null)
                {
                    try
                    {
                        string schema = "";
                        string tName = tableName;
                        if (tableName.Contains("."))
                        {
                            var parts = tableName.Split('.');
                            schema = parts[0];
                            tName = parts[1];
                        }
                        var catCols = await catalogProvider.GetColumnMetadataAsync(schema, tName);
                        if (catCols != null && catCols.Count > 0)
                        {
                            colList.AddRange(catCols.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)));
                        }
                    }
                    catch { }
                }

                if (colList.Count == 0)
                {
                    IEnumerable<string> rawCols;
                    if (source is IDatabaseSource db)
                    {
                        rawCols = (await db.GetColumnsAsync(tableName)).ToList();
                    }
                    else
                    {
                        rawCols = (await source.GetColumnsAsync()).ToList();
                    }
                    colList.AddRange(rawCols.Select(c => new ColumnMetadata(c, "ANY")));
                }

                _columns[key] = colList;
                StampCache(key);

                // Trigger background refresh for the whole connection to pre-fetch other metadata
                TriggerBackgroundRefresh(connectionName, conn.ConnectionString, conn.IsDocument ? uri : null);

                return colList;
            }
            catch (Exception ex)
            {
                _logger.Error("GetColumnDetailsAsync ERROR: {Message}", ex);
                return Enumerable.Empty<ColumnMetadata>();
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
            _cacheTimestamps.Clear();
        }

        public void ClearCacheForUri(string uri) => ClearCacheForDocument(uri);

        private void ClearCacheForConnection(string connectionName)
        {
            var keysToRemove = _tables.Keys.Where(k => k.EndsWith($":{connectionName.ToUpperInvariant()}")).ToList();
            foreach (var key in keysToRemove) _tables.TryRemove(key, out _);

            var colKeysToRemove = _columns.Keys.Where(k => k.Contains($":{connectionName.ToUpperInvariant()}:")).ToList();
            foreach (var key in colKeysToRemove) _columns.TryRemove(key, out _);
        }

        public void CleanUpDocumentConnectionsAndTempTables(string uri, IEnumerable<string> activeConnectionNames, IEnumerable<string> activeTempTableNames)
        {
            var normalizedUri = NormalizeUri(uri);
            var activeConns = new HashSet<string>(activeConnectionNames, StringComparer.OrdinalIgnoreCase);
            var activeTemps = new HashSet<string>(activeTempTableNames, StringComparer.OrdinalIgnoreCase);

            if (_docConnections.TryGetValue(normalizedUri, out var list))
            {
                lock (list)
                {
                    var toRemove = list.Where(c => !activeConns.Contains(c.Name)).ToList();
                    foreach (var c in toRemove)
                    {
                        list.Remove(c);
                        ClearCacheForDocument(normalizedUri, c.Name);
                    }
                }
            }

            if (_docTempTables.TryGetValue(normalizedUri, out var tempTablesList))
            {
                lock (tempTablesList)
                {
                    var toRemove = tempTablesList.Where(t => !activeTemps.Contains(t)).ToList();
                    foreach (var t in toRemove)
                    {
                        tempTablesList.Remove(t);
                        var cacheKey = GetTempTableCacheKey(normalizedUri, t);
                        _columns.TryRemove(cacheKey, out _);
                    }
                }
            }
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

        private void TryLoadAndRefreshCache(string connectionName, string connectionString, string? uri)
        {
            var cached = LoadSchemaFromDisk(connectionName, connectionString);
            if (cached != null)
            {
                var connKey = GetCacheKey(connectionName, uri);
                _tables[connKey] = cached.Tables;
                _views[connKey] = cached.Views;
                foreach (var kvp in cached.Columns)
                {
                    var colKey = connKey + ":" + kvp.Key.ToUpperInvariant();
                    _columns[colKey] = kvp.Value;
                }
                StampCache(connKey);
                _logger.Info("Loaded schema cache from disk for connection {Name}", connectionName);
            }

            TriggerBackgroundRefresh(connectionName, connectionString, uri);
        }

        private void TriggerBackgroundRefresh(string connectionName, string connectionString, string? uri)
        {
            if (DisableBackgroundRefresh) return;
            var connKey = GetCacheKey(connectionName, uri);
            if (_ongoingRefreshes.ContainsKey(connKey)) return;

            var task = Task.Run(async () =>
            {
                try
                {
                    await RefreshSchemaInternalAsync(connectionName, connectionString, uri);
                }
                catch (Exception ex)
                {
                    _logger.Error("Error during background schema refresh for connection {Name}", ex, connectionName);
                }
                finally
                {
                    _ongoingRefreshes.TryRemove(connKey, out _);
                }
            });

            _ongoingRefreshes[connKey] = task;
        }

        private async Task RefreshSchemaInternalAsync(string connectionName, string connectionString, string? uri)
        {
            var conn = GetConnection(connectionName, uri);
            if (conn == null) return;

            var connector = _connectors.GetConnector(conn.Type);
            if (connector == null) return;

            _logger.Info("Starting background schema refresh for connection {Name} ({Type})", connectionName, conn.Type);

            await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, conn.ConnectionString);
            
            var tables = (await source.GetTablesAsync()).ToList();
            if (!tables.Contains("DUAL", StringComparer.OrdinalIgnoreCase))
            {
                tables.Add("DUAL");
            }

            var views = new List<string>();
            if (source is IDatabaseSource db)
            {
                views = (await db.GetViewsAsync()).ToList();
            }

            var columnsMap = new Dictionary<string, List<ColumnMetadata>>(StringComparer.OrdinalIgnoreCase);
            var catalogProvider = source.GetCatalogProvider();

            var allObjects = tables.Concat(views).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var obj in allObjects)
            {
                if (obj.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
                {
                    columnsMap[obj] = new List<ColumnMetadata> { new ColumnMetadata("DUMMY", "VARCHAR") };
                    continue;
                }

                var colList = new List<ColumnMetadata>();
                if (catalogProvider != null)
                {
                    try
                    {
                        string schema = "";
                        string tName = obj;
                        if (obj.Contains("."))
                        {
                            var parts = obj.Split('.');
                            schema = parts[0];
                            tName = parts[1];
                        }
                        var catCols = await catalogProvider.GetColumnMetadataAsync(schema, tName);
                        if (catCols != null)
                        {
                            colList.AddRange(catCols.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)));
                        }
                    }
                    catch
                    {
                        // Fallback below
                    }
                }

                if (colList.Count == 0)
                {
                    try
                    {
                        IEnumerable<string> rawCols;
                        if (source is IDatabaseSource dbSource)
                        {
                            rawCols = await dbSource.GetColumnsAsync(obj);
                        }
                        else
                        {
                            rawCols = await source.GetColumnsAsync();
                        }
                        colList.AddRange(rawCols.Select(c => new ColumnMetadata(c, "ANY")));
                    }
                    catch
                    {
                        // Ignore table/view if we can't query columns
                    }
                }

                if (colList.Count > 0)
                {
                    columnsMap[obj] = colList;
                }
            }

            var connKey = GetCacheKey(connectionName, uri);
            _tables[connKey] = tables;
            _views[connKey] = views;

            var prefix = connKey + ":";
            var keysToRemove = _columns.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var k in keysToRemove) _columns.TryRemove(k, out _);

            foreach (var kvp in columnsMap)
            {
                var colKey = prefix + kvp.Key.ToUpperInvariant();
                _columns[colKey] = kvp.Value;
            }
            StampCache(connKey);

            var cacheData = new ConnectionSchemaCache
            {
                Tables = tables,
                Views = views,
                Columns = columnsMap
            };
            await SaveSchemaToDiskAsync(connectionName, connectionString, cacheData);

            _logger.Info("Background schema refresh complete for connection {Name}. Cached {TableCount} tables/views.", connectionName, allObjects.Count);
        }

        private async Task SaveSchemaToDiskAsync(string connectionName, string connectionString, ConnectionSchemaCache cacheData)
        {
            if (string.IsNullOrEmpty(SchemaCacheDirectory)) return;
            try
            {
                var directory = SchemaCacheDirectory;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var fileName = GetCacheFileName(connectionName, connectionString);
                var filePath = Path.Combine(directory, fileName);

                var json = JsonSerializer.Serialize(cacheData);
                var plaintextBytes = Encoding.UTF8.GetBytes(json);
                var ciphertextBytes = Common.MachineBoundCrypto.Protect(plaintextBytes);

                await File.WriteAllBytesAsync(filePath, ciphertextBytes);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save schema cache to disk", ex);
            }
        }

        private ConnectionSchemaCache? LoadSchemaFromDisk(string connectionName, string connectionString)
        {
            if (string.IsNullOrEmpty(SchemaCacheDirectory)) return null;
            try
            {
                var directory = SchemaCacheDirectory;
                var fileName = GetCacheFileName(connectionName, connectionString);
                var filePath = Path.Combine(directory, fileName);

                if (!File.Exists(filePath)) return null;

                var ciphertextBytes = File.ReadAllBytes(filePath);
                var plaintextBytes = Common.MachineBoundCrypto.Unprotect(ciphertextBytes);
                var json = Encoding.UTF8.GetString(plaintextBytes);

                return JsonSerializer.Deserialize<ConnectionSchemaCache>(json);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load schema cache from disk", ex);
                return null;
            }
        }

        private string GetCacheFileName(string connectionName, string connectionString)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var inputBytes = Encoding.UTF8.GetBytes(connectionString);
            var hashBytes = sha.ComputeHash(inputBytes);
            var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            
            var safeName = new string(connectionName.Where(char.IsLetterOrDigit).ToArray());
            return $"{safeName}_{hashHex}.cache";
        }
    }

    public class ConnectionSchemaCache
    {
        public List<string> Tables { get; set; } = new();
        public List<string> Views { get; set; } = new();
        public Dictionary<string, List<ColumnMetadata>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
