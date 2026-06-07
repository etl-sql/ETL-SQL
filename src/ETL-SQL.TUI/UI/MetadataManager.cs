using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.TUI.UI
{
    public class MetadataManager
    {
        private string _lastScript = "";
        private readonly IExecutionContext _context;
        private readonly Dictionary<string, IDataSource> _connections;

        public MetadataManager(IExecutionContext context, Dictionary<string, IDataSource> connections)
        {
            _context = context;
            _connections = connections;
        }
    

        public void RefreshConnections(string script, bool force = false)
        {
            if (!force && script == _lastScript) return;
            _lastScript = script;

            // Only clear if the script actually contains connection or table definitions
            if (script.Contains("CREATE CONNECTION", StringComparison.OrdinalIgnoreCase) || 
                script.Contains("CREATE TABLE #", StringComparison.OrdinalIgnoreCase))
            {
                _connections.Clear();
            }

            // Regex: captures name, type, and paren content for AS TYPE(...) syntax
            var matches = Regex.Matches(script, @"CREATE\s+CONNECTION\s+(\w+)\s+AS\s+(\w+)\s*\(([^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                var type = match.Groups[2].Value.ToUpper();
                var parenContent = match.Groups[3].Value;

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
            if (!_connections.TryGetValue(connectionName, out var ds)) return Enumerable.Empty<string>();
            if (ds is IDatabaseSource db) return await db.GetTablesAsync();
            return new[] { connectionName }; // For file sources, the connection name acts as the table name
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
        {
            if (!_connections.TryGetValue(connectionName, out var ds)) return Enumerable.Empty<string>();
            if (ds is IDatabaseSource db) return await db.GetColumnsAsync(tableName);
            return await ds.GetColumnsAsync();
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

        public IEnumerable<string> GetConnections() => _connections.Keys;

        public string? GetConnectionType(string connectionName)
        {
            if (!_connections.TryGetValue(connectionName, out var ds)) return null;
            if (ds is IDatabaseSource db) return db.Dialect;
            return "FLATFILE";
        }
    }
}
