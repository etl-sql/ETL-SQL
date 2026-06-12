using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Directory
{
    public class DirectoryConnector : IConnector
    {
        public string Name => "DIRECTORY";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public bool IsFileBased => true;


        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Directory Connector 1.0");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "PATH", Array.Empty<string>() },
            { "CREATE", new[] { "ON", "OFF", "TRUE", "FALSE" } }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        public string GetHelp() =>
            "DIRECTORY Connector: Represents a file system directory.\n" +
            "Used to list files and perform file system operations (COPY, MOVE, etc.).";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new DirectoryDataSource(context, connectionString, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds a directory path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) =>
            ConnectionStringBuilder.Build(Name, properties);
    }
}
