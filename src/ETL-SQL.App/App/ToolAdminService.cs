using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator;
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
            if (config is not null)
                catalog = ToolCatalogProviderFactory.Create(DependencyInjectionExtensions.BuildToolCatalogOptions(config));
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
        CancellationToken ct,
        Func<bool>? isElevated = null)
    {
        switch (action)
        {
            case "admin-machine-tool-list":
                return await ListAsync(catalog, logger, ct);
            case "admin-machine-tool-set":
                return await SetAsync(ctx, catalog, logger, ct, isElevated ?? AdministrativePrivilege.IsElevated);
            case "admin-machine-tool-delete":
                return await DeleteAsync(ctx, catalog, logger, ct, isElevated ?? AdministrativePrivilege.IsElevated);
            case "admin-machine-tool-grant":
            case "admin-machine-tool-revoke":
                return await ChangeUseGrantAsync(ctx, catalog, logger, ct,
                    isElevated ?? AdministrativePrivilege.IsElevated, allow: action == "admin-machine-tool-grant");
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

    private static bool AuthorizeCatalogMutation(
        string? toolName,
        IToolCatalogProvider? catalog,
        ILogger logger,
        Func<bool> isElevated)
    {
        var requiresAdmin = catalog is not LocalToolCatalogProvider local
            || local.OwnershipMode == ToolCatalogOwnershipMode.MachineAdministrator;

        if (!requiresAdmin || isElevated())
            return true;

        var actor = ProcessActor.Current;
        SecurityEventRuntime.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Warning,
            SecurityEventType.OperationDenied,
            actor,
            actor,
            $"Tool:{toolName ?? "*"}",
            SecurityEventDecision.Denied,
            "Machine tool catalog mutation denied: elevated Administrator or root privileges are required."));
        logger.WriteLine(
            "Machine tool catalog mutation requires an elevated Administrator or root process.",
            ConsoleColor.Red);
        return false;
    }

    private static async Task<int> SetAsync(
        CliContext ctx,
        IToolCatalogProvider? catalog,
        ILogger logger,
        CancellationToken ct,
        Func<bool> isElevated)
    {
        if (catalog is not IWritableToolCatalogProvider writable)
            return LifecycleUnsupported(catalog, logger);

        if (!AuthorizeCatalogMutation(ctx.ToolName?.Trim(), catalog, logger, isElevated))
            return 1;

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

    private static async Task<int> DeleteAsync(
        CliContext ctx,
        IToolCatalogProvider? catalog,
        ILogger logger,
        CancellationToken ct,
        Func<bool> isElevated)
    {
        if (catalog is not IWritableToolCatalogProvider writable)
            return LifecycleUnsupported(catalog, logger);

        if (!AuthorizeCatalogMutation(ctx.ToolName?.Trim(), catalog, logger, isElevated))
            return 1;

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

    private static async Task<int> ChangeUseGrantAsync(
        CliContext ctx,
        IToolCatalogProvider? catalog,
        ILogger logger,
        CancellationToken ct,
        Func<bool> isElevated,
        bool allow)
    {
        if (catalog is not IWritableToolCatalogProvider writable)
            return LifecycleUnsupported(catalog, logger);

        var toolName = ctx.ToolName?.Trim();
        if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(ctx.ToolGrantTenant)
            || string.IsNullOrWhiteSpace(ctx.GrantPrincipalId))
        {
            logger.WriteLine("--name, --tenant, and --principal are required.", ConsoleColor.Red);
            return 1;
        }

        var kind = ctx.GrantPrincipalKind?.Trim().ToUpperInvariant() switch
        {
            "USER" => ToolPrincipalKind.User,
            "GROUP" => ToolPrincipalKind.Group,
            "SERVICE" => ToolPrincipalKind.Service,
            _ => (ToolPrincipalKind?)null
        };
        if (kind is null)
        {
            logger.WriteLine("--principal-kind must be USER, GROUP, or SERVICE.", ConsoleColor.Red);
            return 1;
        }

        try
        {
            ToolAliasValidator.Validate(toolName);
            if (!AuthorizeCatalogMutation(toolName, catalog, logger, isElevated))
                return 1;

            var grant = new ToolUseGrant(ctx.ToolGrantTenant.Trim(), kind.Value, ctx.GrantPrincipalId.Trim());
            if (await writable.SetUseGrantAsync(toolName, grant, allow, ct))
            {
                var actor = ProcessActor.Current;
                var change = allow ? "granted" : "revoked";
                var message = $"Tool use {change} for {grant.Kind}:{grant.PrincipalId}.";
                SecurityEventRuntime.Emit(SecurityEventContract.Create(
                    SecurityEventSeverity.Information, SecurityEventType.CatalogMutation, actor, actor,
                    $"Tool:{toolName}", SecurityEventDecision.Allowed, message) with
                { TenantId = grant.TenantId });
                logger.WriteLine(message);
            }
            else
            {
                logger.WriteLine("The tool's grants are already in the requested state.");
            }

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "Tool grant update failed.", ConsoleColor.Red);
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
