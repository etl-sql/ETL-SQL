using System;
using System.Collections.Generic;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Directory
{
    public class DirectoryConnector : IConnector
    {
        public string Name => "DIRECTORY";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("Directory Connector 1.0");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        public string GetHelp() => 
            "DIRECTORY Connector: Represents a file system directory.\n" +
            "Used to list files and perform file system operations (COPY, MOVE, etc.).";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null)
        {
            return new DirectoryDataSource(connectionString, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new DirectoryDataSource(connectionString);
            return await ds.GetColumnsAsync();
        }
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds a directory path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
