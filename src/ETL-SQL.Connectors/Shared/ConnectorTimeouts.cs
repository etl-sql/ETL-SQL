using ETL_SQL.Core;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Connectors.Shared;

internal static class ConnectorTimeouts
{
    public static int ResolveCommandTimeoutSeconds(
        IExecutionContext? context,
        Dictionary<string, string>? options,
        int fallbackSeconds = 30)
    {
        if (options != null
            && options.TryGetValue("TIMEOUT_SECONDS", out var optionValue)
            && int.TryParse(optionValue, out var optionSeconds)
            && optionSeconds > 0)
        {
            return optionSeconds;
        }

        var configured = GetConfiguration(context)
            ?.GetValue<int?>("Connectors:DataWarehouse:DefaultCommandTimeoutSeconds");
        return configured.GetValueOrDefault(fallbackSeconds) > 0
            ? configured!.Value
            : fallbackSeconds;
    }

    private static IConfiguration? GetConfiguration(IExecutionContext? context)
    {
        try
        {
            return context?.ServiceProvider?.GetService(typeof(IConfiguration)) as IConfiguration;
        }
        catch
        {
            return null;
        }
    }
}
