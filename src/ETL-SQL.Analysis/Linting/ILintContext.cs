using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Analysis.Linting;
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
    ILogger? Logger => null;
}

public class DefaultLintContext : ILintContext
{
    public IMetadataProvider? Metadata { get; set; }
    public string DocumentUri { get; set; } = string.Empty;
    public ILogger? Logger { get; set; }
}
