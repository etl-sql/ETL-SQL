using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App;

internal static class ToolAdminService
{
    internal static async Task<int> RunAsync(CliContext ctx, ILogger logger)
    {
        var config = Program.ServiceProvider.GetService<IConfiguration>();
        IToolCatalogProvider? catalog = null;

        try
        {
            var providerName = config?["Governance:Tools:Provider"];
            var localRoot = config?["Governance:Tools:LocalRoot"] ?? config?["Governance:Tools:OsStoreRoot"];

            if (!string.IsNullOrWhiteSpace(providerName))
            {
                catalog = ToolCatalogProviderFactory.Create(new ToolCatalogOptions
                {
                    Provider = providerName,
                    LocalRoot = localRoot
                });
            }
        }
        catch (Exception ex)
        {
            logger.WriteLine($"Tool catalog configuration error: {SecretRedactor.Redact(ex.Message)}", ConsoleColor.Red);
            return 1;
        }

        var action = ctx.Command;
        try
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            return await ExecuteAsync(action, ctx, catalog, logger, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.WriteLine("Operation canceled.", ConsoleColor.Yellow);
            return 130;
        }
    }

    internal static async Task<int> ExecuteAsync(
        string action,
        CliContext ctx,
        IToolCatalogProvider? catalog,
        ILogger logger,
        CancellationToken ct)
    {
        switch (action)
        {
            case "admin-machine-tool-list":
                return await ListAsync(catalog, logger, ct);
            case "admin-machine-tool-set":
                return await SetAsync(ctx, catalog, logger, ct);
            case "admin-machine-tool-delete":
                return await DeleteAsync(ctx, catalog, logger, ct);
            default:
                logger.WriteLine($"Unknown tool command: {action}", ConsoleColor.Red);
                return 1;
        }
    }

    private static async Task<int> ListAsync(IToolCatalogProvider? catalog, ILogger logger, CancellationToken ct)
    {
        if (catalog is not IWritableToolCatalogProvider writable)
            return LifecycleUnsupported(catalog, logger);

        try
        {
            var entries = await writable.ListAsync(ct);
            logger.WriteLine($"Machine tool catalog: provider '{catalog.ProviderName}'.");
            if (entries.Count == 0) logger.WriteLine("(none)");
            foreach (var entry in entries.OrderBy(e => e))
            {
                logger.WriteLine(entry);
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "Tool listing failed.", ConsoleColor.Red);
            return 1;
        }
    }

    private static async Task<int> SetAsync(CliContext ctx, IToolCatalogProvider? catalog, ILogger logger, CancellationToken ct)
    {
        if (catalog is not IWritableToolCatalogProvider writable)
            return LifecycleUnsupported(catalog, logger);

        if (string.IsNullOrWhiteSpace(ctx.ToolName))
        {
            logger.WriteLine("--name is required.", ConsoleColor.Red);
            return 1;
        }
        if (string.IsNullOrWhiteSpace(ctx.ToolType))
        {
            logger.WriteLine("--type is required.", ConsoleColor.Red);
            return 1;
        }

        try
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ctx.ToolOptions != null)
            {
                foreach (var opt in ctx.ToolOptions)
                {
                    var parts = opt.Split('=', 2);
                    if (parts.Length != 2)
                    {
                        logger.WriteLine($"Option '{opt}' is invalid. Must be KEY=VALUE.", ConsoleColor.Red);
                        return 1;
                    }
                    options[parts[0].Trim()] = parts[1].Trim();
                }
            }

            var def = new ToolDefinition(
                ctx.ToolName.Trim(),
                ctx.ToolType.Trim().ToUpperInvariant(),
                options,
                Disabled: false);

            await writable.StoreAsync(def, ct);

            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Information,
                SecurityEventType.CatalogMutation,
                Environment.UserName,
                Environment.UserName,
                $"Tool:{ctx.ToolName.Trim()}",
                SecurityEventDecision.Allowed,
                $"Tool '{ctx.ToolName.Trim()}' ({ctx.ToolType.Trim().ToUpperInvariant()}) added or updated in machine catalog."));

            logger.WriteLine($"Machine tool '{ctx.ToolName.Trim()}' stored ({ctx.ToolType.Trim().ToUpperInvariant()}, " +
                             $"{options.Count} option(s)).");
            return 0;
        }
        catch (Exception ex)
        {
            logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "Tool storage failed.", ConsoleColor.Red);
            return 1;
        }
    }

    private static async Task<int> DeleteAsync(CliContext ctx, IToolCatalogProvider? catalog, ILogger logger, CancellationToken ct)
    {
        if (catalog is not IWritableToolCatalogProvider writable)
            return LifecycleUnsupported(catalog, logger);

        if (string.IsNullOrWhiteSpace(ctx.ToolName))
        {
            logger.WriteLine("--name is required.", ConsoleColor.Red);
            return 1;
        }

        try
        {
            await writable.DeleteAsync(ctx.ToolName.Trim(), ct);

            SecurityEventRuntime.Emit(SecurityEventContract.Create(
                SecurityEventSeverity.Information,
                SecurityEventType.CatalogMutation,
                Environment.UserName,
                Environment.UserName,
                $"Tool:{ctx.ToolName.Trim()}",
                SecurityEventDecision.Allowed,
                $"Tool '{ctx.ToolName.Trim()}' deleted from machine catalog."));

            logger.WriteLine($"Machine tool '{ctx.ToolName.Trim()}' deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "Tool deletion failed.", ConsoleColor.Red);
            return 1;
        }
    }

    private static int LifecycleUnsupported(IToolCatalogProvider? catalog, ILogger logger)
    {
        if (catalog == null)
            logger.WriteLine("No tool catalog provider is configured. Configure Governance:Tools:Provider=LOCAL to manage tools from the CLI.", ConsoleColor.Red);
        else
            logger.WriteLine($"Provider '{catalog.ProviderName}' does not support writes.", ConsoleColor.Red);
        return 1;
    }
}
