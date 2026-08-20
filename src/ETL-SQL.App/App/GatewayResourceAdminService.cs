using ETL_SQL.Common;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App;

internal static class GatewayResourceAdminService
{
    public static async Task<int> RunAsync(CliContext context, ILogger logger)
    {
        if (!File.Exists(GatewaySetupService.ConfigPath))
        {
            logger.Error("Gateway is not enrolled. Run 'etlsql gateway setup' first.");
            return 1;
        }

        try
        {
            var registry = new GatewayResourceRegistry(
                Path.Combine(GatewaySetupService.ConfigDirectory, "resources.protected"));
            switch (context.Command)
            {
                case "gateway-resource-propose":
                    if (string.IsNullOrWhiteSpace(context.GatewayResourceId)
                        || string.IsNullOrWhiteSpace(context.GatewayConnectorType)
                        || string.IsNullOrWhiteSpace(context.GatewayLocalTarget)
                        || string.IsNullOrWhiteSpace(context.GatewayCredentialReference))
                    {
                        logger.Error("propose requires --resource-id, --connector, --target, and --credential-ref.");
                        return 1;
                    }
                    if (!context.GatewayCredentialReference.StartsWith("ENV:", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Error("--credential-ref must use ENV:name; raw credentials are not accepted.");
                        return 1;
                    }
                    var operations = ParseOperations(context.GatewayOperations);
                    await registry.ProposeAsync(new GatewayResource(
                        context.GatewayResourceId, context.GatewayConnectorType,
                        context.GatewayLocalTarget, context.GatewayCredentialReference,
                        operations, new GatewayResourceLimits())).ConfigureAwait(false);
                    logger.Info("Gateway resource {ResourceId} is proposed and remains inert until approved.", context.GatewayResourceId);
                    return 0;

                case "gateway-resource-approve":
                    RequireId(context);
                    await registry.ApproveAsync(context.GatewayResourceId!).ConfigureAwait(false);
                    logger.Info("Gateway resource {ResourceId} is approved.", context.GatewayResourceId);
                    return 0;

                case "gateway-resource-disable":
                    RequireId(context);
                    await registry.DisableAsync(context.GatewayResourceId!).ConfigureAwait(false);
                    logger.Info("Gateway resource {ResourceId} is disabled.", context.GatewayResourceId);
                    return 0;

                case "gateway-resource-list":
                    foreach (var resource in await registry.ListAsync().ConfigureAwait(false))
                        logger.Info("{ResourceId}\t{State}\t{Connector}\t{Operations}",
                            resource.ResourceId, resource.State, resource.ConnectorType, resource.AllowedOperations);
                    return 0;
                default:
                    return 1;
            }
        }
        catch (GatewayResourceException ex)
        {
            logger.Error(ex.Message);
            return 1;
        }
    }

    private static void RequireId(CliContext context)
    {
        if (string.IsNullOrWhiteSpace(context.GatewayResourceId))
            throw new GatewayResourceException("This operation requires --resource-id.");
    }

    private static GatewayOperationClass ParseOperations(string? value)
    {
        var result = GatewayOperationClass.None;
        foreach (var item in (value ?? "READ").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<GatewayOperationClass>(item, true, out var parsed) || parsed == GatewayOperationClass.None)
                throw new GatewayResourceException("--operations accepts READ, WRITE, and EXECUTE.");
            result |= parsed;
        }
        return result;
    }
}
