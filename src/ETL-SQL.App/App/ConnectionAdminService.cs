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

namespace ETL_SQL.App
{
    /// <summary>
    /// Administrative lifecycle for shared connection catalog entries (SHARED:alias): set, list,
    /// verify, disable, delete against the configured catalog provider. Credential fields must be
    /// SECRET: (or ENC:) references — raw credential values are rejected so the catalog never
    /// stores secret material.
    /// </summary>
    internal static class ConnectionAdminService
    {
        internal static async Task<int> RunAsync(CliContext ctx, ILogger logger)
        {
            var configuration = Program.ServiceProvider.GetService<IConfiguration>();
            IConnectionCatalogProvider? catalog;
            try
            {
                catalog = ConnectionCatalogProviderFactory.Create(
                    new ConnectionCatalogOptions
                    {
                        Provider = configuration?["Governance:ConnectionCatalog:Provider"],
                        LocalRoot = configuration?["Governance:ConnectionCatalog:LocalRoot"]
                    });
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Connection catalog configuration error: {SecretRedactor.Redact(ex.Message)}", ConsoleColor.Red);
                return 1;
            }

            ISecretProvider? secrets = null;
            if (ctx.Command == "admin-verify-connection")
            {
                try { secrets = SecretAdminService.CreateProvider(configuration); }
                catch { /* verify still checks the definition; secret resolution is reported as unavailable */ }
            }

            return await ExecuteAsync(ctx.Command["admin-".Length..], ctx, catalog, secrets, logger, CancellationToken.None);
        }

        internal static async Task<int> ExecuteAsync(
            string action,
            CliContext ctx,
            IConnectionCatalogProvider? catalog,
            ISecretProvider? secrets,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (catalog == null)
            {
                logger.WriteLine(
                    "No connection catalog provider is configured. Set Governance:ConnectionCatalog:Provider=Local " +
                    "and Governance:ConnectionCatalog:LocalRoot to manage shared connections from the CLI.",
                    ConsoleColor.Red);
                return 1;
            }

            try
            {
                switch (action)
                {
                    case "list-connections":
                        return await ListAsync(catalog, logger, cancellationToken);
                    case "set-connection":
                        return await SetAsync(ctx, catalog, logger, cancellationToken);
                    case "verify-connection":
                        return await VerifyAsync(ctx.ConnectionAlias, catalog, secrets, logger, cancellationToken);
                    case "disable-connection":
                        return await MutateAsync(ctx.ConnectionAlias, catalog, logger, cancellationToken,
                            (writable, alias, ct) => writable.DisableAsync(alias, ct),
                            alias => $"Shared connection '{alias}' disabled; scripts referencing it now fail until it is re-enabled.");
                    case "enable-connection":
                        return await MutateAsync(ctx.ConnectionAlias, catalog, logger, cancellationToken,
                            (writable, alias, ct) => writable.EnableAsync(alias, ct),
                            alias => $"Shared connection '{alias}' enabled; scripts referencing it resolve again.");
                    case "delete-connection":
                        return await MutateAsync(ctx.ConnectionAlias, catalog, logger, cancellationToken,
                            (writable, alias, ct) => writable.DeleteAsync(alias, ct),
                            alias => $"Shared connection '{alias}' deleted from the catalog.");
                    default:
                        logger.WriteLine($"Unknown connection command '{action}'.", ConsoleColor.Red);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "Connection catalog operation failed.", ConsoleColor.Red);
                return 1;
            }
        }

        private static async Task<int> ListAsync(IConnectionCatalogProvider catalog, ILogger logger, CancellationToken ct)
        {
            if (catalog is not IWritableConnectionCatalogProvider writable)
                return LifecycleUnsupported(catalog, logger);

            var aliases = await writable.ListAsync(ct);
            if (aliases.Count == 0)
            {
                logger.WriteLine("The connection catalog is empty.", ConsoleColor.Yellow);
                return 0;
            }

            foreach (var alias in aliases)
            {
                var status = await writable.GetStatusAsync(alias, ct);
                logger.WriteLine($"{alias}  [{status}]", status == SecretLifecycleStatus.Active ? ConsoleColor.Green : ConsoleColor.Yellow);
            }

            return 0;
        }

        private static async Task<int> SetAsync(CliContext ctx, IConnectionCatalogProvider catalog, ILogger logger, CancellationToken ct)
        {
            if (catalog is not IWritableConnectionCatalogProvider writable)
                return LifecycleUnsupported(catalog, logger);
            if (string.IsNullOrWhiteSpace(ctx.ConnectionAlias))
                return MissingArgument("--alias", logger);
            if (string.IsNullOrWhiteSpace(ctx.ConnectionType))
                return MissingArgument("--type", logger);

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ctx.ConnectionOptions ?? Array.Empty<string>())
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    logger.WriteLine($"Option '{pair}' is not in KEY=VALUE form.", ConsoleColor.Red);
                    return 1;
                }

                options[parts[0].Trim()] = parts[1];
            }

            var rawCredential = SharedConnectionValidator.FindRawCredential(options, ctx.ConnectionTarget);
            if (rawCredential != null)
            {
                logger.WriteLine(
                    $"Field '{rawCredential}' holds a raw credential value. The catalog stores references only: " +
                    "store the value with 'etl-sql admin set-secret' and reference it as SECRET:name.",
                    ConsoleColor.Red);
                return 1;
            }

            await writable.StoreAsync(
                new SharedConnectionDefinition(ctx.ConnectionAlias.Trim(), ctx.ConnectionType.Trim(),
                    string.IsNullOrWhiteSpace(ctx.ConnectionTarget) ? null : ctx.ConnectionTarget, options, Disabled: false,
                    ctx.ConnectionSensitiveFields is { Length: > 0 } ? ctx.ConnectionSensitiveFields : null),
                ct);
            // The alias is not secret, but "SHARED:alias" would be masked by the redactor — phrase around it.
            logger.WriteLine(
                $"Shared connection '{ctx.ConnectionAlias.Trim()}' stored ({ctx.ConnectionType.Trim().ToUpperInvariant()}, " +
                $"{options.Count} option(s)). Scripts reference it with the SHARED: prefix and the alias '{ctx.ConnectionAlias.Trim()}'.",
                ConsoleColor.Green);
            return 0;
        }

        private static async Task<int> VerifyAsync(
            string? alias, IConnectionCatalogProvider catalog, ISecretProvider? secrets, ILogger logger, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return MissingArgument("--alias", logger);

            var definition = await catalog.ResolveAsync(alias.Trim(), identity: null, cancellationToken: ct);

            var references = definition.Options.Values
                .Where(value => value.TrimStart().StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Trim()["SECRET:".Length..].Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in references)
            {
                if (secrets == null)
                {
                    logger.WriteLine($"Secret 'SECRET:{name}' could not be checked: no secret provider is configured.", ConsoleColor.Yellow);
                    continue;
                }

                await secrets.ResolveAsync(name, ct);
            }

            logger.WriteLine(
                $"Shared connection '{definition.Alias}' verified: {definition.ConnectorType}, " +
                $"{definition.Options.Count} option(s), {references.Count} secret reference(s) resolvable. Values not shown.",
                ConsoleColor.Green);
            return 0;
        }

        private static async Task<int> MutateAsync(
            string? alias,
            IConnectionCatalogProvider catalog,
            ILogger logger,
            CancellationToken ct,
            Func<IWritableConnectionCatalogProvider, string, CancellationToken, Task> operation,
            Func<string, string> successMessage)
        {
            if (catalog is not IWritableConnectionCatalogProvider writable)
                return LifecycleUnsupported(catalog, logger);
            if (string.IsNullOrWhiteSpace(alias))
                return MissingArgument("--alias", logger);

            await operation(writable, alias.Trim(), ct);
            logger.WriteLine(successMessage(alias.Trim()), ConsoleColor.Green);
            return 0;
        }

        private static int MissingArgument(string option, ILogger logger)
        {
            logger.WriteLine($"{option} is required.", ConsoleColor.Red);
            return 1;
        }

        private static int LifecycleUnsupported(IConnectionCatalogProvider catalog, ILogger logger)
        {
            logger.WriteLine(
                $"Connection catalog provider '{catalog.ProviderName}' does not support lifecycle operations from the CLI.",
                ConsoleColor.Red);
            return 1;
        }
    }
}
