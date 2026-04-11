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
        private readonly Dictionary<string, IDataSource> _connections;

        public MetadataManager(Dictionary<string, IDataSource> connections)
        {
            _connections = connections;
        }

        public void RefreshConnections(string script, bool force = false)
        {
            if (!force && script == _lastScript) return;
            _lastScript = script;

            // Only clear if the script actually contains connection or table definitions
            // to avoid clearing manual/global connections during partial script edits.
            if (script.Contains("CREATE CONNECTION", StringComparison.OrdinalIgnoreCase) || 
                script.Contains("CREATE TABLE #", StringComparison.OrdinalIgnoreCase))
            {
                _connections.Clear();
            }

            // Regex-based connection discovery 
            var matches = Regex.Matches(script, @"CREATE\s+CONNECTION\s+(\w+)\s+ON\s+(\w+)(?:\s*\(?\s*['""]?([^'""\);]*)['""]?\s*\)?)?(?:\s+WITH\s*\((.*?)\))?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                var type = match.Groups[2].Value.ToUpper();
                var path = match.Groups[3].Value.Trim();
                var optionsPart = match.Groups[4].Value;

                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(optionsPart))
                {
                    var optMatches = Regex.Matches(optionsPart, @"(\w+)\s*=\s*(#?\w+|['""].*?['""])");
                    foreach (Match om in optMatches) options[om.Groups[1].Value] = om.Groups[2].Value.Trim('\'', '\"');
                }

                if ((type == "FLATFILE" || type == "FILE" || type == "CSV") && File.Exists(path)) _connections[name] = new FlatFileDataSource(path, options);
                else _connections[name] = new MockSqlDataSource(path, type);
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

        public IEnumerable<string> GetConnections() => _connections.Keys;

        public string? GetConnectionType(string connectionName)
        {
            if (!_connections.TryGetValue(connectionName, out var ds)) return null;
            if (ds is IDatabaseSource db) return db.Dialect;
            return "FLATFILE";
        }
    }
}
