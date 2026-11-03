using ETL_SQL.Core;

namespace ETL_SQL.Connectors.Shared;

public static class ConnectorTimeouts
{
    /// <summary>
    /// Resolves a command timeout for OLTP and HTTP connectors: the <c>TIMEOUT_SECONDS</c> option when
    /// it is a positive integer, otherwise <paramref name="fallbackSeconds"/> (30 seconds by default).
    /// The context parameter is kept for call-site compatibility; the data-warehouse default
    /// (<c>Connectors:DataWarehouse:DefaultCommandTimeoutSeconds</c>) is deliberately not read here,
    /// because it gave every OLTP, REST and webhook connector a 30-minute timeout.
    /// </summary>
    public static int ResolveCommandTimeoutSeconds(
        IExecutionContext? context,
        Dictionary<string, string>? options,
        int fallbackSeconds = 30)
    {
        _ = context;
        if (options != null
            && options.TryGetValue("TIMEOUT_SECONDS", out var optionValue)
            && int.TryParse(optionValue, out var optionSeconds)
            && optionSeconds > 0)
        {
            return optionSeconds;
        }

        return fallbackSeconds;
    }
}
