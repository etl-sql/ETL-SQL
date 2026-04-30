using System.Collections.Generic;
using System.Threading.Tasks;

using ETL_SQL.Data;

namespace ETL_SQL.Core
{
    public record ConnectionInfo(string Name, string Type, string ConnectionString, bool IsDocument);

    /// <summary>
    /// Manages metadata discovery (tables, columns) and connection registration 
    /// for IntelliSense and external tool integration.
    /// </summary>
    public interface IMetadataManager
    {
        /// <summary>Enables or disables verbose metadata logging.</summary>
        bool DebugMode { get; set; }

        /// <summary>Registers a new connection in the global metadata context.</summary>
        void RegisterConnection(string name, string type, string connectionString);
        void RegisterDocumentConnection(string uri, string name, string type, string connectionString);
        void ClearDocumentConnections(string uri);
        List<ConnectionInfo> GetConnections(string? uri = null);
        Task<IEnumerable<string>> GetTablesAsync(string connectionName, string? uri = null);
        Task<IEnumerable<string>> GetViewsAsync(string connectionName, string? uri = null);
        Task<IEnumerable<string>> GetTempTablesAsync(string? uri = null);
        void RegisterTempTable(string uri, string name, List<string> columns);
        void ClearTempTables(string uri);
        Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName, string? uri = null);
        IEnumerable<string> GetRegisteredNames();
        IConnector? GetConnector(string name);
        string? GetConnectionType(string connectionName, string? uri = null);
        void ClearCache();
        void ClearCacheForUri(string uri);
    }
}
