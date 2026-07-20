using ETL_SQL.Common;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// <see cref="MetadataManager.GetConnectionHost"/> exists so a caller serving cached schema can apply
/// egress policy per request. Reads served from the schema cache never touch the connector that would
/// normally enforce that, so without a real host the check would exist but never fire — these pin that
/// it resolves an actual host rather than silently returning null and making the guard vacuous.
/// </summary>
[Trait("Category", "Connectors")]
public class MetadataConnectionHostTests
{
    // A private registry, never ConnectorRegistry.Instance: the global is mutable shared state and a
    // documented source of order-dependent connector test failures.
    private static MetadataManager NewManager()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new SqlServerConnector());
        // A real no-op logger, not null!: RegisterConnection logs, so a null logger NREs on setup.
        return new MetadataManager(NullLogger.Instance, registry);
    }

    [Fact]
    public void GetConnectionHost_ResolvesHostFromConnectionString()
    {
        var metadata = NewManager();
        metadata.RegisterConnection("warehouse", "MSSQL", "Server=db.internal.example.com;Database=Sales;");

        Assert.Equal("db.internal.example.com", metadata.GetConnectionHost("warehouse"));
    }

    [Fact]
    public void GetConnectionHost_ReturnsNull_ForUnknownConnection()
    {
        var metadata = NewManager();

        // Null means "nothing to validate", not "permitted" — the caller decides.
        Assert.Null(metadata.GetConnectionHost("never-registered"));
    }

    [Fact]
    public void GetConnectionHost_ReturnsNull_WhenConnectorTypeIsUnknown()
    {
        var metadata = NewManager();
        metadata.RegisterConnection("mystery", "NOT_A_REAL_CONNECTOR", "Server=db.example.com;");

        Assert.Null(metadata.GetConnectionHost("mystery"));
    }

    [Fact]
    public void GetConnectionHost_DoesNotThrow_OnAnUnparseableConnectionString()
    {
        var metadata = NewManager();
        metadata.RegisterConnection("broken", "MSSQL", "this is not a connection string");

        // Best-effort by contract: a schema request must not fail because a host could not be parsed.
        var exception = Record.Exception(() => metadata.GetConnectionHost("broken"));
        Assert.Null(exception);
    }
}
