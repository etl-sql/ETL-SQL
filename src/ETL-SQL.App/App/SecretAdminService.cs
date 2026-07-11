using System;
using System.Net.Http;
using System.Text;
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
    /// Administrative lifecycle commands for named secrets: set, verify, rotate, disable, and
    /// delete against the configured secret provider (Governance:Secrets). Mutations require a
    /// provider with lifecycle support (OsSecretStore). Secret values are never echoed or logged.
    /// </summary>
    internal static class SecretAdminService
    {
        internal static async Task<int> RunAsync(CliContext ctx, ILogger logger)
        {
            ISecretProvider provider;
            try
            {
                provider = CreateProvider(Program.ServiceProvider.GetService<IConfiguration>());
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Secret provider configuration error: {SecretRedactor.Redact(ex.Message)}", ConsoleColor.Red);
                return 1;
            }

            var action = ctx.Command["admin-".Length..];
            string? value = null;
            if (action is "set-secret" or "rotate-secret")
            {
                value = AcquireValue(ctx, logger);
                if (string.IsNullOrEmpty(value))
                {
                    logger.WriteLine("A non-empty secret value is required.", ConsoleColor.Red);
                    return 1;
                }
            }

            return await ExecuteAsync(action, ctx.SecretName, value, provider, logger, CancellationToken.None);
        }

        internal static async Task<int> ExecuteAsync(
            string action,
            string? name,
            string? value,
            ISecretProvider provider,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                logger.WriteLine("--name is required.", ConsoleColor.Red);
                return 1;
            }

            try
            {
                switch (action)
                {
                    case "verify-secret":
                    {
                        var result = await provider.ResolveAsync(name, cancellationToken);
                        logger.WriteLine($"Secret '{name}' resolved from provider '{result.Provider}'. Value not shown.", ConsoleColor.Green);
                        return 0;
                    }
                    case "set-secret":
                    case "rotate-secret":
                    {
                        if (provider is not IWritableSecretProvider writable)
                        {
                            logger.WriteLine(
                                $"Provider '{provider.ProviderName}' does not support writes. " +
                                "Configure Governance:Secrets:Provider=OsSecretStore (with OsStoreRoot) to manage secrets from the CLI.",
                                ConsoleColor.Red);
                            return 1;
                        }

                        if (action == "rotate-secret"
                            && provider is ISecretLifecycleProvider lifecycleForRotate
                            && await lifecycleForRotate.GetStatusAsync(name, cancellationToken) == SecretLifecycleStatus.NotFound)
                        {
                            logger.WriteLine($"Secret '{name}' does not exist; use set-secret to create it.", ConsoleColor.Red);
                            return 1;
                        }

                        await writable.StoreAsync(name, value!, cancellationToken);
                        logger.WriteLine($"Secret '{name}' stored in provider '{writable.ProviderName}' (machine scope).", ConsoleColor.Green);
                        return 0;
                    }
                    case "disable-secret":
                    {
                        if (provider is not ISecretLifecycleProvider lifecycle)
                            return LifecycleUnsupported(provider, logger);

                        await lifecycle.DisableAsync(name, cancellationToken);
                        logger.WriteLine($"Secret '{name}' disabled. Resolution now fails until it is re-enabled.", ConsoleColor.Green);
                        return 0;
                    }
                    case "enable-secret":
                    {
                        if (provider is not ISecretLifecycleProvider lifecycle)
                            return LifecycleUnsupported(provider, logger);

                        await lifecycle.EnableAsync(name, cancellationToken);
                        logger.WriteLine($"Secret '{name}' enabled; the previously stored value resolves again.", ConsoleColor.Green);
                        return 0;
                    }
                    case "delete-secret":
                    {
                        if (provider is not ISecretLifecycleProvider lifecycle)
                            return LifecycleUnsupported(provider, logger);

                        await lifecycle.DeleteAsync(name, cancellationToken);
                        logger.WriteLine($"Secret '{name}' deleted from provider '{lifecycle.ProviderName}'.", ConsoleColor.Green);
                        return 0;
                    }
                    default:
                        logger.WriteLine($"Unknown secret command '{action}'.", ConsoleColor.Red);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "Secret operation failed.", ConsoleColor.Red);
                return 1;
            }
        }

        private static int LifecycleUnsupported(ISecretProvider provider, ILogger logger)
        {
            logger.WriteLine(
                $"Provider '{provider.ProviderName}' does not support lifecycle operations. " +
                "Configure Governance:Secrets:Provider=OsSecretStore (with OsStoreRoot) to manage secrets from the CLI.",
                ConsoleColor.Red);
            return 1;
        }

        internal static ISecretProvider CreateProvider(IConfiguration? configuration)
        {
            var options = new SecretProviderOptions
            {
                Provider = configuration?["Governance:Secrets:Provider"]
                    ?? configuration?["Secrets:Provider"]
                    ?? "Environment",
                EnvironmentPrefix = configuration?["Governance:Secrets:EnvironmentPrefix"]
                    ?? configuration?["Secrets:EnvironmentPrefix"],
                OsStoreRoot = configuration?["Governance:Secrets:OsStoreRoot"]
                    ?? configuration?["Secrets:OsStoreRoot"],
                VaultEndpoint = configuration?["Governance:Secrets:VaultEndpoint"]
                    ?? configuration?["Secrets:VaultEndpoint"],
                VaultBearerToken = configuration?["Governance:Secrets:VaultBearerToken"]
                    ?? configuration?["Secrets:VaultBearerToken"]
            };
            return new SecretProviderFactory(new HttpClient()).Create(options);
        }

        private static string? AcquireValue(CliContext ctx, ILogger logger)
        {
            if (!string.IsNullOrEmpty(ctx.SecretValue))
            {
                logger.WriteLine(
                    "Warning: --value can persist in shell history; prefer the masked prompt or piping the value via stdin.",
                    ConsoleColor.Yellow);
                return ctx.SecretValue;
            }

            if (Console.IsInputRedirected)
                return Console.In.ReadLine()?.TrimEnd('\r', '\n');

            var first = PromptMasked("Secret value (input hidden): ");
            if (string.IsNullOrEmpty(first))
                return first;

            var second = PromptMasked("Confirm secret value: ");
            if (!string.Equals(first, second, StringComparison.Ordinal))
            {
                logger.WriteLine("Values did not match.", ConsoleColor.Red);
                return null;
            }

            return first;
        }

        private static string PromptMasked(string prompt)
        {
            Console.Write(prompt);
            var buffer = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buffer.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0) buffer.Length--;
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                    buffer.Append(key.KeyChar);
            }
        }
    }
}
