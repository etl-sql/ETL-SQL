using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting
{
    public interface IMetadataProvider
    {
        Task<IEnumerable<string>> GetTablesAsync(string connectionName);
        Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName);
        IEnumerable<string> GetConnections();
        string? GetConnectionType(string connectionName);
    }

    public interface ILintContext
    {
        IMetadataProvider? Metadata { get; }
        string DocumentUri { get; }
    }

    public class DefaultLintContext : ILintContext
    {
        public IMetadataProvider? Metadata { get; set; }
        public string DocumentUri { get; set; } = string.Empty;
    }
}
