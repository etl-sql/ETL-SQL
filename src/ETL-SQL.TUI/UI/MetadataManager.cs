using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.TUI.UI
{
    public class MetadataManager
    {
        private string _lastScript = "";
        private readonly IExecutionContext _context;
        private readonly Dictionary<string, IDataSource> _connections;

        private readonly Dictionary<string, List<string>> _tablesCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string, string), List<string>> _columnsCache = new();
        private readonly Dictionary<string, List<string>> _viewsCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string, string), List<ColumnMetadata>> _columnDetailsCache = new();

        public MetadataManager(IExecutionContext context, Dictionary<string, IDataSource> connections)
        {
            _context = context;
            _connections = connections;
        }

        public void ClearCache()
        {
            _tablesCache.Clear();
            _columnsCache.Clear();
            _viewsCache.Clear();
            _columnDetailsCache.Clear();
        }


        public void RefreshConnections(string script, bool force = false)
        {
            if (force) ClearCache();
            if (!force && script == _lastScript) return;
            _lastScript = script;

            // Only clear if the script actually contains connection or table definitions
            if (script.Contains("CREATE CONNECTION", StringComparison.OrdinalIgnoreCase) ||
                script.Contains("CREATE TABLE #", StringComparison.OrdinalIgnoreCase))
            {
                _connections.Clear();
                ClearCache();
            }

            // Captures name, type, and paren content for AS TYPE(...) syntax. The body is matched
            // with a balancing group so multi-line blocks and nested parens (e.g. a value like
            // '(local)') are captured whole instead of truncating at the first ')'.
            var matches = Regex.Matches(script,
                @"CREATE\s+CONNECTION\s+(\w+)\s+AS\s+(\w+)\s*\((?<body>(?:[^()]|\((?<d>)|\)(?<-d>))*(?(d)(?!)))\)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                var type = match.Groups[2].Value.ToUpper();
                var parenContent = match.Groups["body"].Value;

                var pathMatch = Regex.Match(parenContent, @"['""]([^'""]*)['""]");
                var path = pathMatch.Success ? pathMatch.Groups[1].Value : "";

                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var optMatches = Regex.Matches(parenContent, @"(\w+)\s*=\s*(#?\w+|['""].*?['""])");
                foreach (Match om in optMatches) options[om.Groups[1].Value] = om.Groups[2].Value.Trim('\'', '\"');

                if ((type == "FLATFILE" || type == "FILE" || type == "CSV") && File.Exists(path)) _connections[name] = new FlatFileDataSource(_context, path, options, null);
                else _connections[name] = new MockSqlDataSource(_context, path, type);
            }

            // Temp table discovery
            var tableMatches = Regex.Matches(script, @"CREATE\s+TABLE\s+(#\w+)\s*\((.*?)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in tableMatches)
            {
                var name = match.Groups[1].Value;
                var colNames = Regex.Matches(match.Groups[2].Value, @"(#?\w+)\s+\w+").Select(m => m.Groups[1].Value.TrimStart('#')).ToList();
                if (colNames.Any())
                {
                    var ds = new InMemoryDataSource();
                    ds.SetSchema(colNames.Select(n => new ColumnDefinition(n, "VARCHAR", false, null, (Dictionary<string, string>?)null)));
                    _connections[name] = ds;
                }
            }
        }
        public async Task<IEnumerable<string>> GetTablesAsync(string connectionName)
        {
            if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase)) return ETL_SQL.Core.EngineCatalog.Tables;
            if (_tablesCache.TryGetValue(connectionName, out var cached)) return cached;

            if (!_connections.TryGetValue(connectionName, out var ds)) return Enumerable.Empty<string>();
            IEnumerable<string> tables;
            if (ds is IDatabaseSource db) tables = await db.GetTablesAsync();
            else tables = new[] { connectionName };

            var list = tables.ToList();
            _tablesCache[connectionName] = list;
            return list;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
        {
            if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase))
            {
                if (ETL_SQL.Core.EngineCatalog.TableColumns.TryGetValue(tableName, out var cols))
                    return cols.Select(c => c.Name);
                return Enumerable.Empty<string>();
            }
            var key = (connectionName.ToLowerInvariant(), tableName.ToLowerInvariant());
            if (_columnsCache.TryGetValue(key, out var cached)) return cached;

            if (!_connections.TryGetValue(connectionName, out var ds)) return Enumerable.Empty<string>();
            IEnumerable<string> columns;
            if (ds is IDatabaseSource db) columns = await db.GetColumnsAsync(tableName);
            else columns = await ds.GetColumnsAsync();

            var list = columns.ToList();
            _columnsCache[key] = list;
            return list;
        }

        public async Task<IEnumerable<ColumnMetadata>> GetColumnDetailsAsync(string connectionName, string tableName)
        {
            if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase))
            {
                if (ETL_SQL.Core.EngineCatalog.TableColumns.TryGetValue(tableName, out var cols)) return cols;
                return Enumerable.Empty<ColumnMetadata>();
            }
            var key = (connectionName.ToLowerInvariant(), tableName.ToLowerInvariant());
            if (_columnDetailsCache.TryGetValue(key, out var cached)) return cached;

            if (!_connections.TryGetValue(connectionName, out var source)) return Enumerable.Empty<ColumnMetadata>();

            List<ColumnMetadata> resultList = new List<ColumnMetadata>();
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
                        resultList = catCols.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)).ToList();
                    }
                }
                catch { }
            }

            if (resultList.Count == 0)
            {
                if (source is IDatabaseSource db)
                {
                    var cols = (await db.GetColumnsAsync(tableName)).ToList();
                    if (cols.Any()) resultList = cols.Select(c => new ColumnMetadata(c, "ANY")).ToList();
                }
                if (resultList.Count == 0)
                {
                    var defaultCols = await source.GetColumnsAsync();
                    resultList = defaultCols.Select(c => new ColumnMetadata(c, "ANY")).ToList();
                }
            }

            _columnDetailsCache[key] = resultList;
            return resultList;
        }

        public async Task<IEnumerable<string>> GetViewsAsync(string connectionName)
        {
            if (connectionName.Equals("eng", StringComparison.OrdinalIgnoreCase)) return Enumerable.Empty<string>();
            if (_viewsCache.TryGetValue(connectionName, out var cached)) return cached;

            if (!_connections.TryGetValue(connectionName, out var ds)) return Enumerable.Empty<string>();
            IEnumerable<string> views;
            if (ds is IDatabaseSource db) views = await db.GetViewsAsync();
            else views = Enumerable.Empty<string>();

            var list = views.ToList();
            _viewsCache[connectionName] = list;
            return list;
        }

        /// <summary>
        /// Looks up a data source in the live execution context (e.g. a #temp table
        /// materialized by SELECT … INTO during the last run), which the static script
        /// scan in <see cref="RefreshConnections"/> cannot know about.
        /// </summary>
        public IDataSource? GetRuntimeSource(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_context.Connections.TryGetValue(name, out var ds)) return ds;
            if (!name.StartsWith("#") && _context.Connections.TryGetValue("#" + name, out var temp)) return temp;
            return null;
        }

        /// <summary>
        /// Resolves the columns of a #temp table for autocomplete: from the live run if
        /// available, otherwise statically from the `SELECT … INTO #temp` that defines it
        /// (expanding `SELECT *` to the source table's columns).
        /// </summary>
        public async Task<IEnumerable<string>> GetTempColumnsAsync(string script, string tempName)
        {
            var runtime = GetRuntimeSource(tempName);
            if (runtime != null)
            {
                var rc = (await runtime.GetColumnsAsync()).ToList();
                if (rc.Count > 0) return rc;
            }

            var into = Regex.Match(script,
                $@"SELECT\s+(.+?)\s+INTO\s+{Regex.Escape(tempName)}\b(.*?)(?:;|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!into.Success) return Enumerable.Empty<string>();

            var colsPart = into.Groups[1].Value.Trim();
            if (colsPart == "*")
            {
                var from = Regex.Match(into.Groups[2].Value, @"\bFROM\s+([#\w]+)(?:\.([#\w]+))?", RegexOptions.IgnoreCase);
                if (from.Success)
                {
                    string a = from.Groups[1].Value;   // connection or table
                    string b = from.Groups[2].Value;   // table (when conn.table)
                    if (!string.IsNullOrEmpty(b)) return await GetColumnsAsync(a, b);
                    if (_connections.ContainsKey(a)) return await GetColumnsAsync(a, a);
                    if (a.StartsWith("#")) return await GetTempColumnsAsync(script, a);
                }
                return Enumerable.Empty<string>();
            }

            // Explicit projection list: take the alias, else the trailing identifier.
            var cols = new List<string>();
            foreach (var spec in Regex.Split(colsPart, @",(?![^(]*\))"))
            {
                var t = spec.Trim();
                if (t.Length == 0) continue;
                var asMatch = Regex.Match(t, @"\bAS\s+([#\w]+)$", RegexOptions.IgnoreCase);
                if (asMatch.Success) { cols.Add(asMatch.Groups[1].Value); continue; }
                var idMatch = Regex.Match(t, @"(?:[#\w]+\.)?([#\w]+)$");
                if (idMatch.Success) cols.Add(idMatch.Groups[1].Value);
            }
            return cols;
        }

        public IEnumerable<string> GetConnections() => _connections.Keys;

        public string? GetConnectionType(string connectionName)
        {
            if (!_connections.TryGetValue(connectionName, out var ds)) return null;
            if (ds is IDatabaseSource db) return db.Dialect;
            return "FLATFILE";
        }
    }
}
