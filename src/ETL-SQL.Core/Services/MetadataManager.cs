using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Core.Services;
/// <summary>
/// Manages database metadata (tables, columns) and connection information.
/// Supports both global connections and document-specific connections (defined in-script).
/// </summary>
public class MetadataManager : IMetadataManager
{
    private readonly TimeSpan _warehouseCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>How old an in-memory entry may get before a read triggers a background refresh
    /// (stale-while-revalidate): reads stay instant, and the schema keeps converging without a
    /// blocking fetch on every access.</summary>
    public TimeSpan SoftRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum number of tables/views a background refresh pre-fetches columns for. 0
    /// (default) means unlimited; set it on very large warehouses to bound the crawl — columns
    /// beyond the cap are still fetched lazily on first use.</summary>
    public int MaxPrefetchColumnObjects { get; set; } = 0;

    /// <summary>On-disk schema cache files older than this are ignored on read and pruned on
    /// write. Defaults to 14 days.</summary>
    public TimeSpan DiskCacheMaxAge { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Maximum combined table/view/column entries kept in memory before least-recently-used
    /// entries are pruned. Defaults to 2048.</summary>
    public int MaxMemoryCacheEntries { get; set; } = 2048;

    /// <summary>Maximum document-scoped metadata contexts retained in memory. Defaults to 256.</summary>
    public int MaxDocumentContexts { get; set; } = 256;

    /// <summary>Document-scoped connections and temp tables idle longer than this are pruned. Defaults
    /// to one hour.</summary>
    public TimeSpan DocumentContextTtl { get; set; } = TimeSpan.FromHours(1);

    private readonly ILogger _logger;
    private readonly IConnectorRegistry _connectors;
    private readonly SemaphoreSlim _refreshGate;
    private readonly ConcurrentDictionary<string, ConnectionInfo> _globalConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ConnectionInfo>> _docConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<string>> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<string>> _docTempTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ColumnMetadata>> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cacheTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _ongoingRefreshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _connectionSecrets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _secretAccess = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _documentAccess = new(StringComparer.OrdinalIgnoreCase);

    public MetadataManager(ILogger logger, IConnectorRegistry connectors, IConfiguration? configuration = null)
    {
        _logger = logger;
        _connectors = connectors;

        var warehouseCacheTtlSeconds = configuration?.GetValue<int?>("Connectors:DataWarehouse:SchemaCacheTtlSeconds");
        if (warehouseCacheTtlSeconds is > 0)
        {
            _warehouseCacheTtl = TimeSpan.FromSeconds(warehouseCacheTtlSeconds.Value);
            SoftRefreshInterval = _warehouseCacheTtl;
        }

        var softRefreshSeconds = configuration?.GetValue<int?>("Connectors:DataWarehouse:SchemaSoftRefreshIntervalSeconds");
        if (softRefreshSeconds is > 0)
        {
            SoftRefreshInterval = TimeSpan.FromSeconds(softRefreshSeconds.Value);
        }

        var diskCacheMaxAgeDays = configuration?.GetValue<int?>("Connectors:DataWarehouse:SchemaDiskCacheMaxAgeDays");
        if (diskCacheMaxAgeDays is > 0)
        {
            DiskCacheMaxAge = TimeSpan.FromDays(diskCacheMaxAgeDays.Value);
        }

        var maxMemoryEntries = configuration?.GetValue<int?>("Metadata:MaxMemoryCacheEntries");
        if (maxMemoryEntries is > 0)
            MaxMemoryCacheEntries = maxMemoryEntries.Value;

        var maxDocumentContexts = configuration?.GetValue<int?>("Metadata:MaxDocumentContexts");
        if (maxDocumentContexts is > 0)
            MaxDocumentContexts = maxDocumentContexts.Value;

        var documentContextTtlSeconds = configuration?.GetValue<int?>("Metadata:DocumentContextTtlSeconds");
        if (documentContextTtlSeconds is > 0)
            DocumentContextTtl = TimeSpan.FromSeconds(documentContextTtlSeconds.Value);

        var maxConcurrentRefreshes = Math.Max(1, configuration?.GetValue<int?>("Metadata:MaxConcurrentRefreshes") ?? 2);
        _refreshGate = new SemaphoreSlim(maxConcurrentRefreshes, maxConcurrentRefreshes);
    }

    private bool IsCacheValid(string cacheKey, string? connectorType)
    {
        if (!_cacheTimestamps.TryGetValue(cacheKey, out var fetchedAt)) return false;
        var connector = connectorType != null ? _connectors.GetConnector(connectorType) : null;
        if (connector?.IsDataWarehouse == true)
            return DateTimeOffset.UtcNow - fetchedAt < _warehouseCacheTtl;
        return true;
    }

    private void StampCache(string cacheKey)
    {
        _cacheTimestamps[cacheKey] = DateTimeOffset.UtcNow;
        PruneMemoryCachesIfNeeded();
    }

    /// <summary>True when a still-valid in-memory entry is old enough to refresh in the
    /// background (stale-while-revalidate), so non-warehouse schemas keep converging instead of
    /// freezing after their first load.</summary>
    private bool ShouldBackgroundRefresh(string cacheKey)
    {
        if (DisableBackgroundRefresh) return false;
        return !_cacheTimestamps.TryGetValue(cacheKey, out var fetchedAt)
            || DateTimeOffset.UtcNow - fetchedAt > SoftRefreshInterval;
    }

    /// <summary>Splits a possibly-qualified object name (table, schema.table, or db.schema.table)
    /// into (schema, name), taking the last segment as the object name.</summary>
    private static (string Schema, string Name) SplitQualifiedName(string qualified)
    {
        var parts = qualified.Split('.');
        return parts.Length > 1 ? (parts[^2], parts[^1]) : ("", qualified);
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
        var handle = StoreConnectionSecret(connectionString);
        if (_globalConnections.TryGetValue(name, out var existing))
            RemoveConnectionSecret(existing);
        _globalConnections[name] = new ConnectionInfo(name, type, string.Empty, false, SecretHandle: handle);
        _logger.Info("Registered global connection {Name} of type {Type}", name, type);
        ClearCacheForConnection(name);
        TryLoadAndRefreshCache(name, connectionString, null);
    }

    public void RegisterDocumentConnection(string uri, string name, string type, string connectionString)
    {
        var normalizedUri = NormalizeUri(uri);
        TouchDocument(normalizedUri);
        var handle = StoreConnectionSecret(connectionString);
        var list = _docConnections.GetOrAdd(normalizedUri, _ => new List<ConnectionInfo>());
        lock (list)
        {
            var removed = list.Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var connection in removed)
            {
                RemoveConnectionSecret(connection);
                list.Remove(connection);
            }
            list.Add(new ConnectionInfo(name, type, string.Empty, true, SecretHandle: handle));
        }
        _logger.Info("Registered document connection {Name} for {Uri}", name, uri);
        ClearCacheForDocument(normalizedUri, name);
        TryLoadAndRefreshCache(name, connectionString, normalizedUri);
        PruneDocumentContextsIfNeeded();
    }

    public void RegisterDocumentMetadata(
        string uri,
        string name,
        string type,
        IEnumerable<string> tables,
        IReadOnlyDictionary<string, IEnumerable<ColumnMetadata>> columns,
        IEnumerable<string>? views = null)
    {
        var normalizedUri = NormalizeUri(uri);
        TouchDocument(normalizedUri);
        var list = _docConnections.GetOrAdd(normalizedUri, _ => new List<ConnectionInfo>());
        lock (list)
        {
            list.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            list.Add(new ConnectionInfo(name, type, string.Empty, true, IsMetadataOnly: true));
        }

        ClearCacheForDocument(normalizedUri, name);

        var key = GetCacheKey(name, normalizedUri);
        var tableList = tables
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!tableList.Contains("DUAL", StringComparer.OrdinalIgnoreCase))
            tableList.Add("DUAL");

        _tables[key] = tableList;
        _views[key] = views?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        foreach (var kvp in columns)
        {
            var colKey = key + ":" + kvp.Key.ToUpperInvariant();
            _columns[colKey] = kvp.Value.ToList();
            StampCache(colKey);
        }
        var dualKey = key + ":DUAL";
        _columns[dualKey] = new List<ColumnMetadata> { new("DUMMY", "VARCHAR") };
        StampCache(dualKey);
        StampCache(key);

        _logger.Info("Registered metadata-only document connection {Name} for {Uri}", name, uri);
        PruneDocumentContextsIfNeeded();
    }

    public void ClearDocumentConnections(string uri)
    {
        var normalizedUri = NormalizeUri(uri);
        if (_docConnections.TryRemove(normalizedUri, out var connections))
        {
            _logger.Debug("Cleared document connections for {Uri}.", normalizedUri);
            lock (connections)
                ClearDocumentSecrets(normalizedUri, connections.ToList());
            ClearCacheForDocument(normalizedUri);
            _documentAccess.TryRemove(normalizedUri, out _);
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
            TouchDocument(normalizedUri);
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
        if (!result.Any(c => c.Name.Equals("eng", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new ConnectionInfo("eng", "ENG", string.Empty, false));
        }
        return result.Select(RedactConnection).ToList();
    }

    private ConnectionInfo? GetConnection(string connectionName, string? uri = null)
    {
        var normalizedUri = NormalizeUri(uri);
        if (!string.IsNullOrEmpty(normalizedUri))
        {
            TouchDocument(normalizedUri);
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

    private string StoreConnectionSecret(string connectionString)
    {
        var handle = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _connectionSecrets[handle] = connectionString;
        _secretAccess[handle] = DateTimeOffset.UtcNow;
        return handle;
    }

    private string ResolveConnectionString(ConnectionInfo connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.SecretHandle)
            && _connectionSecrets.TryGetValue(connection.SecretHandle, out var value))
        {
            _secretAccess[connection.SecretHandle] = DateTimeOffset.UtcNow;
            return value;
        }

        return connection.ConnectionString;
    }

    private static ConnectionInfo RedactConnection(ConnectionInfo connection) =>
        connection with { ConnectionString = string.Empty };

    private void TouchDocument(string normalizedUri)
    {
        if (!string.IsNullOrWhiteSpace(normalizedUri))
            _documentAccess[normalizedUri] = DateTimeOffset.UtcNow;
    }

    private void RemoveConnectionSecret(ConnectionInfo connection)
    {
        if (string.IsNullOrWhiteSpace(connection.SecretHandle)) return;
        _connectionSecrets.TryRemove(connection.SecretHandle, out _);
        _secretAccess.TryRemove(connection.SecretHandle, out _);
    }

    private void ClearDocumentSecrets(string normalizedUri, List<ConnectionInfo>? connections = null)
    {
        if (connections is null && _docConnections.TryGetValue(normalizedUri, out var active))
        {
            lock (active)
                connections = active.ToList();
        }

        if (connections is null) return;
        foreach (var connection in connections)
            RemoveConnectionSecret(connection);
    }

    private void PruneMemoryCachesIfNeeded()
    {
        var maxEntries = Math.Max(0, MaxMemoryCacheEntries);
        if (maxEntries == 0) return;

        while (_tables.Count + _views.Count + _columns.Count > maxEntries)
        {
            var oldest = _cacheTimestamps.OrderBy(kvp => kvp.Value).FirstOrDefault();
            if (string.IsNullOrEmpty(oldest.Key)) break;
            RemoveCacheEntry(oldest.Key);
        }
    }

    private void RemoveCacheEntry(string cacheKey)
    {
        _cacheTimestamps.TryRemove(cacheKey, out _);
        _tables.TryRemove(cacheKey, out _);
        _views.TryRemove(cacheKey, out _);
        _columns.TryRemove(cacheKey, out _);

        var prefix = cacheKey + ":";
        foreach (var key in _columns.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _columns.TryRemove(key, out _);
            _cacheTimestamps.TryRemove(key, out _);
        }
    }

    private void PruneDocumentContextsIfNeeded()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = DocumentContextTtl > TimeSpan.Zero
            ? _documentAccess
                .Where(kvp => now - kvp.Value > DocumentContextTtl)
                .Select(kvp => kvp.Key)
                .ToList()
            : new List<string>();

        foreach (var uri in expired)
            ClearDocumentContext(uri);

        var maxContexts = Math.Max(0, MaxDocumentContexts);
        if (maxContexts == 0) return;

        while (_documentAccess.Count > maxContexts)
        {
            var oldest = _documentAccess.OrderBy(kvp => kvp.Value).FirstOrDefault();
            if (string.IsNullOrEmpty(oldest.Key)) break;
            ClearDocumentContext(oldest.Key);
        }
    }

    private void ClearDocumentContext(string normalizedUri)
    {
        if (_docConnections.TryRemove(normalizedUri, out var connections))
        {
            lock (connections)
                ClearDocumentSecrets(normalizedUri, connections.ToList());
        }

        ClearCacheForDocument(normalizedUri);
        _documentAccess.TryRemove(normalizedUri, out _);
    }

    public bool DebugMode { get; set; } = false;
    public string? SchemaCacheDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "SchemaCache");
    public bool DisableBackgroundRefresh { get; set; } = false;

    public string? GetConnectionType(string connectionName, string? uri = null)
    {
        if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase)) return "ENG";
        return GetConnection(connectionName, uri)?.Type;
    }

    public async Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null)
    {
        if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase)) return EngineCatalog.Tables;
        try
        {
            var conn = GetConnection(connectionName, uri);
            if (conn == null) return Enumerable.Empty<string>();

            var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null);
            if (_tables.TryGetValue(key, out var cached) && IsCacheValid(key, conn.Type))
            {
                // Stale-while-revalidate: serve instantly, refresh in the background when aged.
                if (!conn.IsMetadataOnly && ShouldBackgroundRefresh(key))
                    TriggerBackgroundRefresh(connectionName, ResolveConnectionString(conn), conn.IsDocument ? uri : null);
                return cached;
            }

            if (conn.IsMetadataOnly)
                return Enumerable.Empty<string>();

            var connectionString = ResolveConnectionString(conn);

            // Try to load disk cache
            var diskCache = await LoadSchemaFromDiskAsync(connectionName, connectionString);
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
                TriggerBackgroundRefresh(connectionName, connectionString, conn.IsDocument ? uri : null);

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

            await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, connectionString);
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
            TriggerBackgroundRefresh(connectionName, connectionString, conn.IsDocument ? uri : null);

            return tables;
        }
        catch (Exception ex)
        {
            _logger.Error("GetTablesAsync error: {Message}", ex, ex.Message);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null)
    {
        if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase)) return Enumerable.Empty<string>();
        try
        {
            var conn = GetConnection(connectionName, uri);
            if (conn == null) return Enumerable.Empty<string>();

            var key = GetCacheKey(connectionName, conn.IsDocument ? uri : null);
            if (_views.TryGetValue(key, out var cached) && IsCacheValid(key, conn.Type))
            {
                if (!conn.IsMetadataOnly && ShouldBackgroundRefresh(key))
                    TriggerBackgroundRefresh(connectionName, ResolveConnectionString(conn), conn.IsDocument ? uri : null);
                return cached;
            }

            if (conn.IsMetadataOnly)
                return Enumerable.Empty<string>();

            var connectionString = ResolveConnectionString(conn);

            // Try to load disk cache
            var diskCache = await LoadSchemaFromDiskAsync(connectionName, connectionString);
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
                TriggerBackgroundRefresh(connectionName, connectionString, conn.IsDocument ? uri : null);

                return diskCache.Views;
            }

            var connector = _connectors.GetConnector(conn.Type);
            if (connector == null) return Enumerable.Empty<string>();

            await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, connectionString);
            var views = Enumerable.Empty<string>();
            if (source is IDatabaseSource db)
            {
                views = (await db.GetViewsAsync()).ToList();
            }

            _views[key] = views.ToList();
            StampCache(key);

            // Trigger background refresh to fetch and cache columns as well
            TriggerBackgroundRefresh(connectionName, connectionString, conn.IsDocument ? uri : null);

            return views;
        }
        catch (Exception ex)
        {
            _logger.Error("GetViewsAsync error: {Message}", ex, ex.Message);
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

    public void RegisterTempTable(string uri, string name, List<string> columns) =>
        RegisterTempTable(uri, name, columns.Select(c => new ColumnMetadata(c, "ANY")).ToList());

    public void RegisterTempTable(string uri, string name, List<ColumnMetadata> columns)
    {
        var normalizedUri = NormalizeUri(uri);
        var list = _docTempTables.GetOrAdd(normalizedUri, _ => new List<string>());
        lock (list)
        {
            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        }

        var cacheKey = GetTempTableCacheKey(normalizedUri, name);
        _columns[cacheKey] = columns.ToList();
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
        if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase))
        {
            if (EngineCatalog.TableColumns.TryGetValue(tableName, out var cols)) return cols;
            return Enumerable.Empty<ColumnMetadata>();
        }
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
            if (_columns.TryGetValue(key, out var cached) && IsCacheValid(key, conn.Type))
            {
                if (!conn.IsMetadataOnly && ShouldBackgroundRefresh(GetCacheKey(connectionName, conn.IsDocument ? uri : null)))
                    TriggerBackgroundRefresh(connectionName, ResolveConnectionString(conn), conn.IsDocument ? uri : null);
                return cached;
            }

            if (conn.IsMetadataOnly)
                return Enumerable.Empty<ColumnMetadata>();

            var connectionString = ResolveConnectionString(conn);

            // Try to load disk cache if memory cache is empty
            var diskCache = await LoadSchemaFromDiskAsync(connectionName, connectionString);
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
                TriggerBackgroundRefresh(connectionName, connectionString, conn.IsDocument ? uri : null);

                if (_columns.TryGetValue(key, out var diskLoadedCols))
                {
                    return diskLoadedCols;
                }
            }

            // If not cached anywhere, we must fetch synchronously
            var connector = _connectors.GetConnector(conn.Type);
            if (connector == null) return Enumerable.Empty<ColumnMetadata>();

            await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, connectionString);
            var colList = new List<ColumnMetadata>();

            var catalogProvider = source.GetCatalogProvider();
            if (catalogProvider != null)
            {
                try
                {
                    var (schema, tName) = SplitQualifiedName(tableName);
                    var catCols = await catalogProvider.GetColumnMetadataAsync(schema, tName);
                    if (catCols != null && catCols.Count > 0)
                    {
                        colList.AddRange(catCols.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Catalog column lookup failed for {Table}; falling back to GetColumns: {Message}",
                        tableName, ex.Message);
                }
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
            TriggerBackgroundRefresh(connectionName, connectionString, conn.IsDocument ? uri : null);

            return colList;
        }
        catch (Exception ex)
        {
            _logger.Error("GetColumnDetailsAsync error: {Message}", ex, ex.Message);
            return Enumerable.Empty<ColumnMetadata>();
        }
    }

    public IEnumerable<string> GetRegisteredNames() => _connectors.GetRegisteredNames();

    public IConnector? GetConnector(string name) => _connectors.GetConnector(name);

    /// <inheritdoc />
    public string? GetConnectionHost(string connectionName, string? uri = null)
    {
        var connection = GetConnection(connectionName, uri);
        if (connection is null) return null;

        var connector = _connectors.GetConnector(connection.Type);
        if (connector is null) return null;

        try
        {
            // Resolve inside the manager so the credential-bearing string is never returned to the
            // caller. Options are not retained on ConnectionInfo, so the connector falls back to
            // parsing the connection string, which is how every GetHost implementation behaves.
            return connector.GetHost(ResolveConnectionString(connection));
        }
        catch
        {
            // A host we cannot parse is reported as "unknown", not as "permitted" — the caller
            // decides what that means. Deliberately not logged: the exception text from a connector
            // routinely quotes the connection string, and this type is constructed with a null
            // logger in several call sites, so logging here would trade a credential leak for a
            // NullReferenceException.
            return null;
        }
    }

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
        _ongoingRefreshes.Clear();
    }

    public void ClearCacheForUri(string uri) => ClearCacheForDocument(uri);

    private void ClearCacheForConnection(string connectionName)
    {
        var suffix = $":{connectionName}";
        var upperSuffix = $":{connectionName.ToUpperInvariant()}:";
        var keysToRemove = _tables.Keys.Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keysToRemove) _tables.TryRemove(key, out _);

        var viewKeysToRemove = _views.Keys.Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in viewKeysToRemove) _views.TryRemove(key, out _);

        var colKeysToRemove = _columns.Keys.Where(k => k.Contains(upperSuffix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in colKeysToRemove) _columns.TryRemove(key, out _);

        foreach (var key in keysToRemove.Concat(viewKeysToRemove).Concat(colKeysToRemove))
            _cacheTimestamps.TryRemove(key, out _);
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
                    RemoveConnectionSecret(c);
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
            _cacheTimestamps.TryRemove(cacheKey, out _);
            var colKeysToRemove = _columns.Keys.Where(k => k.StartsWith($"{cacheKey}:", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in colKeysToRemove) _columns.TryRemove(k, out _);
            foreach (var k in colKeysToRemove) _cacheTimestamps.TryRemove(k, out _);
        }
        else
        {
            var prefix = $"{normalizedUri}:";
            var keysToRemove = _tables.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in keysToRemove) _tables.TryRemove(k, out _);

            var viewKeysToRemove = _views.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in viewKeysToRemove) _views.TryRemove(k, out _);

            var colKeysToRemove = _columns.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in colKeysToRemove) _columns.TryRemove(k, out _);

            foreach (var k in keysToRemove.Concat(viewKeysToRemove).Concat(colKeysToRemove))
                _cacheTimestamps.TryRemove(k, out _);
            _docTempTables.TryRemove(normalizedUri, out _);
        }
    }

    private void TryLoadAndRefreshCache(string connectionName, string connectionString, string? uri)
    {
        // Warm from disk and refresh live, both off the caller's thread (registration is sync and
        // must not block on file I/O). A getter racing this will load the disk cache itself on miss.
        _ = Task.Run(async () =>
        {
            try
            {
                var cached = await LoadSchemaFromDiskAsync(connectionName, connectionString);
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
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to warm schema cache for connection {Name}: {Message}", ex, connectionName, ex.Message);
            }

            TriggerBackgroundRefresh(connectionName, connectionString, uri);
        });
    }

    private void TriggerBackgroundRefresh(string connectionName, string connectionString, string? uri)
    {
        if (DisableBackgroundRefresh) return;
        var connKey = GetCacheKey(connectionName, uri);

        // Claim the slot atomically BEFORE launching. Using ContainsKey-then-assign let two callers
        // both start, and let a fast/failed task's finally run before the post-assignment — which
        // re-inserted a completed task that never cleared, permanently disabling refresh for the key.
        if (!_ongoingRefreshes.TryAdd(connKey, Task.CompletedTask)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _refreshGate.WaitAsync();
                try
                {
                    await RefreshSchemaInternalAsync(connectionName, connectionString, uri);
                }
                finally
                {
                    _refreshGate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error during background schema refresh for connection {Name}: {Message}",
                    ex, connectionName, ex.Message);
            }
            finally
            {
                _ongoingRefreshes.TryRemove(connKey, out _);
            }
        });
    }

    private async Task RefreshSchemaInternalAsync(string connectionName, string connectionString, string? uri)
    {
        var conn = GetConnection(connectionName, uri);
        if (conn == null) return;

        var connector = _connectors.GetConnector(conn.Type);
        if (connector == null) return;

        _logger.Info("Starting background schema refresh for connection {Name} ({Type})", connectionName, conn.Type);

        var resolvedConnectionString = ResolveConnectionString(conn);
        if (string.IsNullOrEmpty(resolvedConnectionString))
            resolvedConnectionString = connectionString;

        await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, resolvedConnectionString);

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
        // Bound the column crawl on very large schemas; objects past the cap still get columns
        // lazily on first use via GetColumnDetailsAsync.
        var prefetchObjects = MaxPrefetchColumnObjects > 0
            ? allObjects.Take(MaxPrefetchColumnObjects)
            : allObjects;
        foreach (var obj in prefetchObjects)
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
                    var (schema, tName) = SplitQualifiedName(obj);
                    var catCols = await catalogProvider.GetColumnMetadataAsync(schema, tName);
                    if (catCols != null)
                    {
                        colList.AddRange(catCols.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Catalog column lookup failed for {Object}; falling back: {Message}", obj, ex.Message);
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
                catch (Exception ex)
                {
                    _logger.Debug("Column lookup failed for {Object}; skipping: {Message}", obj, ex.Message);
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
        await SaveSchemaToDiskAsync(connectionName, resolvedConnectionString, cacheData);

        _logger.Info("Background schema refresh complete for connection {Name}. Cached {TableCount} tables/views.", connectionName, allObjects.Count);
    }

    private async Task SaveSchemaToDiskAsync(string connectionName, string connectionString, ConnectionSchemaCache cacheData)
    {
        var directory = ResolveSafeSchemaCacheDirectory();
        if (directory == null) return;
        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileName = GetCacheFileName(connectionName, connectionString);
            var filePath = ResolveSchemaCacheFile(directory, fileName);

            var json = JsonSerializer.Serialize(cacheData);
            var plaintextBytes = Encoding.UTF8.GetBytes(json);
            var ciphertextBytes = Common.MachineBoundCrypto.Protect(plaintextBytes);

            await File.WriteAllBytesAsync(filePath, ciphertextBytes);
            PruneStaleDiskCaches(directory);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save schema cache to disk: {Message}", ex, ex.Message);
        }
    }

    private async Task<ConnectionSchemaCache?> LoadSchemaFromDiskAsync(string connectionName, string connectionString)
    {
        var directory = ResolveSafeSchemaCacheDirectory();
        if (directory == null) return null;
        try
        {
            var fileName = GetCacheFileName(connectionName, connectionString);
            var filePath = ResolveSchemaCacheFile(directory, fileName);

            if (!File.Exists(filePath)) return null;

            // Ignore caches older than the staleness bound; a live refresh will repopulate them.
            if (DiskCacheMaxAge > TimeSpan.Zero
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath) > DiskCacheMaxAge)
            {
                return null;
            }

            var ciphertextBytes = await File.ReadAllBytesAsync(filePath);
            var plaintextBytes = Common.MachineBoundCrypto.Unprotect(ciphertextBytes);
            var json = Encoding.UTF8.GetString(plaintextBytes);

            return JsonSerializer.Deserialize<ConnectionSchemaCache>(json);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load schema cache from disk: {Message}", ex, ex.Message);
            return null;
        }
    }

    /// <summary>Best-effort removal of cache files past the staleness bound, so orphaned files
    /// (renamed/removed connections, rotated connection strings) do not accumulate forever.</summary>
    private void PruneStaleDiskCaches(string directory)
    {
        if (DiskCacheMaxAge <= TimeSpan.Zero) return;
        try
        {
            var cutoff = DateTime.UtcNow - DiskCacheMaxAge;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cache"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    try { File.Delete(file); } catch { /* best effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("Schema cache prune skipped: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Bumped when a change makes previously cached schema wrong rather than merely stale, so old
    /// entries are bypassed immediately instead of being served until they age out.
    ///
    /// v2: MOCKDB and SQLite gained catalog providers. Entries written before that recorded every
    /// column as <c>ANY</c>, and the disk cache is consulted ahead of the provider, so without a bump
    /// an existing workstation would keep showing <c>ANY</c> for up to <see cref="DiskCacheMaxAge"/>.
    /// </summary>
    private const string CacheSchemaVersion = "v2";

    private string GetCacheFileName(string connectionName, string connectionString)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        // Salt with a per-user/machine value so the filename is not a portable, confirmable
        // SHA-256 of the (credential-bearing) connection string.
        var salt = $"{Environment.UserName}|{Environment.MachineName}|{CacheSchemaVersion}|";
        var inputBytes = Encoding.UTF8.GetBytes(salt + connectionString);
        var hashBytes = sha.ComputeHash(inputBytes);
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var safeName = new string(connectionName.Where(char.IsLetterOrDigit).ToArray());
        return $"{safeName}_{hashHex}.cache";
    }

    private string? ResolveSafeSchemaCacheDirectory()
    {
        if (string.IsNullOrWhiteSpace(SchemaCacheDirectory)) return null;

        try
        {
            var fullPath = Path.GetFullPath(SchemaCacheDirectory);
            if (IsProtectedCacheDirectory(fullPath))
            {
                _logger.Warning("Schema cache directory '{Path}' is restricted; disk schema cache is disabled.", fullPath);
                return null;
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.Warning("Schema cache directory '{Path}' is invalid; disk schema cache is disabled: {Message}", SchemaCacheDirectory, ex.Message);
            return null;
        }
    }

    private static string ResolveSchemaCacheFile(string directory, string fileName)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var fullPath = Path.GetFullPath(Path.Combine(fullDirectory, fileName));
        if (!SafePath.IsWithinRoot(fullDirectory, fullPath))
            throw new InvalidOperationException("Schema cache file path escapes the configured schema cache directory.");
        return fullPath;
    }

    private static bool IsProtectedCacheDirectory(string fullPath)
    {
        var trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(trimmed);
        if (!string.IsNullOrEmpty(root) && trimmed.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return true;

        var normalized = trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var upper = normalized.ToUpperInvariant();
        return upper.Contains($"{Path.DirectorySeparatorChar}.GIT", StringComparison.Ordinal)
            || upper.Contains($"{Path.DirectorySeparatorChar}.SSH", StringComparison.Ordinal)
            || upper.StartsWith(@"C:\WINDOWS", StringComparison.Ordinal)
            || upper.StartsWith(@"C:\PROGRAM FILES", StringComparison.Ordinal)
            || upper.Equals("/ETC", StringComparison.Ordinal)
            || upper.StartsWith("/ETC/", StringComparison.Ordinal)
            || upper.Equals("/ROOT", StringComparison.Ordinal)
            || upper.StartsWith("/ROOT/", StringComparison.Ordinal);
    }
}

public class ConnectionSchemaCache
{
    public List<string> Tables { get; set; } = new();
    public List<string> Views { get; set; } = new();
    public Dictionary<string, List<ColumnMetadata>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
