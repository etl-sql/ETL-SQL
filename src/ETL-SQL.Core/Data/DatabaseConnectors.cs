using ETL_SQL.Core;

namespace ETL_SQL.Data
{
    /// <summary>
    /// Defines the contract for external database connectors (SQL Server, PostgreSql, etc.).
    /// Each connector provides metadata and data source creation capabilities.
    /// </summary>
    public interface IConnector
    {
        /// <summary>The unique internal name of the connector (e.g., "MSSQL").</summary>
        string Name { get; }
        /// <summary>Alternative names or aliases for the connector.</summary>
        IReadOnlyList<string> Aliases { get; }
        /// <summary>Returns the version of the remote database engine.</summary>
        Task<string> GetVersionAsync(string connectionString);
        /// <summary>Returns a set of SQL functions supported by this connector.</summary>
        HashSet<string> GetSupportedFunctions();
        /// <summary>Returns a set of keywords supported by this connector.</summary>
        HashSet<string> GetSupportedKeywords();
        /// <summary>Returns ETL-SQL baseline keywords that are NOT supported in this connector's dialect (e.g., TOP for Postgres).
        /// File-based and non-SQL connectors return an empty set by default.</summary>
        HashSet<string> GetExcludedKeywords() => new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Returns supported connection configuration options.</summary>
        Dictionary<string, string[]> GetSupportedOptions();
        /// <summary>Returns predefined values for connection options.</summary>
        Dictionary<string, string[]> GetOptionValues();
        /// <summary>Returns a help string for using this connector.</summary>
        string GetHelp();
        /// <summary>Creates a new <see cref="IDataSource"/> instance for the specified connection string.</summary>
        IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null);
        /// <summary>Creates a new <see cref="IDataSource"/> instance for the specified connection string with a template schema.</summary>
        IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options, IEnumerable<ColumnDefinition>? templateSchema) => CreateDataSource(connectionString, options);
        /// <summary>Returns a list of tables available in the database.</summary>
        Task<IEnumerable<string>> GetTablesAsync(string connectionString);
        /// <summary>Returns a list of views available in the database.</summary>
        Task<IEnumerable<string>> GetViewsAsync(string connectionString);
        /// <summary>Returns a list of columns for the specified table.</summary>
        Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName);
        /// <summary>Returns a list of stored procedures available in the database.</summary>
        Task<IEnumerable<string>> GetProceduresAsync(string connectionString);
        /// <summary>Builds a provider-specific connection string from a dictionary of properties.</summary>
        string BuildConnectionString(Dictionary<string, string> properties) => string.Empty;
    }

    public interface IConnectorRegistry
    {
        void Register(IConnector connector);
        IConnector? GetConnector(string name);
        IEnumerable<string> GetRegisteredNames();
        HashSet<string> GetAllConnectorKeywords();
        HashSet<string> GetAllConnectorFunctions();
        Dictionary<string, string[]> GetAllConnectorOptionValues();
    }

    public class ConnectorRegistry : IConnectorRegistry
    {
        public static IConnectorRegistry? Instance { get; internal set; }
        private readonly Dictionary<string, IConnector> _connectors = new(StringComparer.OrdinalIgnoreCase);

        public ConnectorRegistry() { }

        public ConnectorRegistry(IEnumerable<IConnector> connectors)
        {
            foreach (var c in connectors) Register(c);
            Instance = this;
        }

        public void Register(IConnector connector)
        {
            _connectors[connector.Name] = connector;
            foreach (var alias in connector.Aliases)
            {
                _connectors[alias] = connector;
            }
        }

        public IConnector? GetConnector(string name)
        {
            if (_connectors.TryGetValue(name, out var connector)) return connector;
            return null;
        }

        public IEnumerable<string> GetRegisteredNames() => _connectors.Keys;

        public HashSet<string> GetAllConnectorKeywords()
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _connectors.Values)
                foreach (var k in c.GetSupportedKeywords()) keywords.Add(k);
            return keywords;
        }

        public HashSet<string> GetAllConnectorFunctions()
        {
            var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _connectors.Values)
                foreach (var f in c.GetSupportedFunctions()) functions.Add(f);
            return functions;
        }

        public Dictionary<string, string[]> GetAllConnectorOptionValues()
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _connectors.Values)
            {
                foreach (var entry in c.GetOptionValues())
                {
                    if (map.TryGetValue(entry.Key, out var existing))
                        map[entry.Key] = existing.Union(entry.Value, StringComparer.OrdinalIgnoreCase).ToArray();
                    else
                        map[entry.Key] = entry.Value;
                }
            }
            return map;
        }
    }
}
