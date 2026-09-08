using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

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

    /// <summary>
    /// The connectors the dialect rules consult.
    ///
    /// <para>They read <see cref="ConnectorRegistry.Instance"/> directly until now, which is
    /// process-wide mutable state: a test that registers a connector to exercise one rule changes
    /// what every later test in the run lints against, and the resulting failure is attributed to
    /// whichever test happened to run next rather than to the one that moved the registry. Taking
    /// the registry from the context lets a caller answer that question for its own lint run and
    /// depend on nothing global.</para>
    ///
    /// <para>Defaulted, so every existing implementer keeps compiling and every caller that has not
    /// been given a registry behaves exactly as it did. <see cref="ConnectorRegistry.UseScoped"/> is
    /// the other half of this, for callers with no context to hand.</para>
    /// </summary>
    IConnectorRegistry? Connectors => ConnectorRegistry.Instance;
}

public class DefaultLintContext : ILintContext
{
    public IMetadataProvider? Metadata { get; set; }
    public string DocumentUri { get; set; } = string.Empty;
    public ILogger? Logger { get; set; }

    private IConnectorRegistry? connectors;

    /// <summary>
    /// Unset falls through to the process-wide registry, which is what every caller got before this
    /// property existed. Set it to lint against a registry of your own.
    /// </summary>
    public IConnectorRegistry? Connectors
    {
        get => connectors ?? ConnectorRegistry.Instance;
        set => connectors = value;
    }
}
