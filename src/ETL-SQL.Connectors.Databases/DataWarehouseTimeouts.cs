using ETL_SQL.Core;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Connectors.Shared;

/// <summary>
/// Default command timeout for data-warehouse connectors (Snowflake, BigQuery) when
/// TIMEOUT_SECONDS is not given: <c>Connectors:DataWarehouse:DefaultCommandTimeoutSeconds</c>
/// when it is a positive integer, otherwise 1800 seconds. OLTP and HTTP connectors do not use it.
/// </summary>
internal static class DataWarehouseTimeouts
{
    internal const int BuiltInDefaultSeconds = 1800;

    internal static int DefaultSeconds(IExecutionContext? context)
    {
        int? configured = null;
        try
        {
            var configuration = context?.ServiceProvider?.GetService(typeof(IConfiguration)) as IConfiguration;
            configured = configuration?.GetValue<int?>("Connectors:DataWarehouse:DefaultCommandTimeoutSeconds");
        }
        catch (InvalidOperationException)
        {
            // A disposed (ObjectDisposedException) or half-built provider, or a value that is not an
            // integer: use the built-in default rather than fail the connection.
        }

        return configured is > 0 ? configured.Value : BuiltInDefaultSeconds;
    }
}
