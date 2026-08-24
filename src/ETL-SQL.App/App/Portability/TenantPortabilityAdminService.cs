using ETL_SQL.App.Admin;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Portability;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App.Portability;

/// <summary>
/// The <c>etl-sql admin tenant</c> verbs (TenantPortability.md §16).
/// </summary>
/// <remarks>
/// <c>validate</c> and <c>preflight</c> are the customer-side verbs, and they matter most: someone
/// who has been handed a bundle must be able to check it with the shipped binary and a published
/// key, without an account on the deployment that produced it. Neither reaches the network or mutates
/// anything.
/// </remarks>
internal static class TenantPortabilityAdminService
{
    internal static async Task<int> RunAsync(CliContext ctx, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            return ctx.Command switch
            {
                "admin-tenant-export" => await ExportAsync(ctx, logger, ct),
                "admin-tenant-validate" => await ValidateAsync(ctx, logger, ct),
                "admin-tenant-preflight" => await PreflightAsync(ctx, logger, ct),
                "admin-tenant-import" => await ImportAsync(ctx, logger, ct),
                _ => throw new ArgumentException($"Unknown tenant portability command '{ctx.Command}'.")
            };
        }
        catch (AdminCliException ex)
        {
            logger.WriteLine($"Tenant {ctx.Command["admin-tenant-".Length..]} failed: {ex.Message} " +
                             $"(exit {(int)ex.Code}: {ex.Code})", ConsoleColor.Red);
            return (int)ex.Code;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or InvalidDataException or ArgumentException or InvalidOperationException
                                   or TenantBundleCompositionException)
        {
            logger.WriteLine($"Tenant {ctx.Command["admin-tenant-".Length..]} failed: {ex.Message}", ConsoleColor.Red);
            return (int)TenantPortabilityExitCode.BundleInvalid;
        }
    }

    private static async Task<int> ExportAsync(CliContext ctx, ILogger logger, CancellationToken ct)
    {
        var root = RequireBundle(ctx);
        if (string.IsNullOrWhiteSpace(ctx.TenantExportIdentity))
            throw new ArgumentException("--tenant is required for tenant export.");
        if (string.IsNullOrWhiteSpace(ctx.TenantSourceProfile)
            || !new[] { "Solo", "Team", "Enterprise", "SaaS" }
                .Contains(ctx.TenantSourceProfile, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("--source-profile must be Solo, Team, Enterprise, or SaaS.");
        if (string.IsNullOrWhiteSpace(ctx.TenantSigningKey))
            throw new ArgumentException("--signing-key is required so the exported bundle has verifiable provenance.");
        if (string.Equals(ctx.TenantSourceProfile, "SaaS", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(ctx.TenantRecipientKey))
            throw new ArgumentException("--recipient-key is required for a SaaS-sourced export.");

        var client = await AuthenticatedPortalClientAsync(ctx, ct).ConfigureAwait(false);
        var portal = new PortalAdminConfigurationSource(client, ctx.TenantOrchestratorAlias);
        var package = await ReadOrchestratorPackageAsync(ctx.TenantOrchestratorPackage, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var manifest = await TenantBundleComposer.ComposeAsync(portal,
            new TenantBundleCompositionRequest(
                root,
                $"tenant-{Guid.NewGuid():N}",
                now,
                typeof(TenantPortabilityAdminService).Assembly.GetName().Version?.ToString() ?? "unknown",
                ctx.TenantSourceProfile,
                ctx.TenantExportIdentity,
                $"portal-plan-at-{now:O}",
                [.. (ctx.TenantArtifactFiles ?? []).Select(Path.GetFullPath)],
                string.IsNullOrWhiteSpace(ctx.TenantArtifactRoot) ? null : Path.GetFullPath(ctx.TenantArtifactRoot),
                package,
                NullIfWhiteSpace(ctx.TenantRecipientKey),
                Path.GetFullPath(ctx.TenantSigningKey),
                await ResolveOptionalPassphraseAsync("ETLSQL_TENANT_SIGNING_PASSPHRASE", ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);

        logger.WriteLine($"Tenant bundle exported to '{root}'.", ConsoleColor.Green);
        logger.WriteLine(
            $"{manifest.Components.Count} component(s), {manifest.RequiredBindings.Count} required binding(s), " +
            $"{manifest.Exclusions.Count} explicit exclusion(s).", ConsoleColor.Cyan);
        return (int)TenantPortabilityExitCode.Ok;
    }

    private static async Task<int> ValidateAsync(CliContext ctx, ILogger logger, CancellationToken ct)
    {
        var root = RequireBundle(ctx);
        var result = await Core.Portability.TenantBundleValidator.ValidateAsync(root,
            new Core.Portability.TenantBundleValidator.Options(
                ctx.TenantOperatorKey, ctx.TenantRequireSignature), ct);

        foreach (var finding in result.Findings)
        {
            logger.WriteLine($"[{finding.Severity}] {finding.Code} — {finding.Resource}: {finding.Message}",
                finding.Severity == "Error" ? ConsoleColor.Red : ConsoleColor.Yellow);
        }

        if (!result.IsValid)
        {
            var signature = result.Findings.Any(f => f.Code.StartsWith("bundle.signature", StringComparison.Ordinal));
            logger.WriteLine("Bundle INVALID — do not import it.", ConsoleColor.Red);
            return (int)(signature
                ? TenantPortabilityExitCode.SignatureUnverified
                : TenantPortabilityExitCode.BundleInvalid);
        }

        var manifest = result.Manifest!;
        logger.WriteLine("Bundle valid.", ConsoleColor.Green);
        logger.WriteLine(
            $"Tenant '{manifest.TenantExportIdentity}' from {manifest.SourceProfile} " +
            $"{manifest.SourceProductVersion}, {manifest.Components.Count} component(s), " +
            $"consistency point {manifest.ConsistencyPoint}.", ConsoleColor.Cyan);
        logger.WriteLine(
            manifest.Encryption?.Encrypted == true
                ? "Payloads are encrypted to a tenant recipient key."
                : "Payloads are NOT encrypted; this is recorded in the manifest.",
            manifest.Encryption?.Encrypted == true ? ConsoleColor.Cyan : ConsoleColor.Yellow);

        if (ctx.TenantOperatorKey is null)
        {
            logger.WriteLine(
                "No --operator-key was supplied, so the signature was not verified. Integrity was " +
                "checked; authenticity was not.", ConsoleColor.Yellow);
        }

        return (int)TenantPortabilityExitCode.Ok;
    }

    private static async Task<int> PreflightAsync(CliContext ctx, ILogger logger, CancellationToken ct)
    {
        var root = RequireBundle(ctx);
        var supplied = ParseSuppliedBindings(ctx.TenantBindings);
        var preflight = await TenantPortabilityInspector.PreflightAsync(
            root, ctx.TenantOperatorKey, ctx.TenantRequireSignature, supplied, ct);

        foreach (var finding in preflight.Findings.Where(f => f.Severity == "Error"))
            logger.WriteLine($"[Error] {finding.Code} — {finding.Message}", ConsoleColor.Red);

        if (preflight.ExitCode is TenantPortabilityExitCode.BundleInvalid
            or TenantPortabilityExitCode.SignatureUnverified
            or TenantPortabilityExitCode.NotFound)
        {
            logger.WriteLine("Preflight stopped: the bundle itself is not usable.", ConsoleColor.Red);
            return (int)preflight.ExitCode;
        }

        foreach (var exclusion in preflight.Exclusions)
        {
            logger.WriteLine(
                $"Not portable: {exclusion.ResourceClass} '{exclusion.LogicalId}' — {exclusion.Reason}" +
                (exclusion.Remediation is null ? "" : $" {exclusion.Remediation}"),
                ConsoleColor.Yellow);
        }

        if (preflight.RequiredBindings.Count > 0)
        {
            logger.WriteLine(
                $"{preflight.RequiredBindings.Count} binding(s) the target must supply before import:",
                ConsoleColor.Yellow);
            foreach (var binding in preflight.RequiredBindings)
                logger.WriteLine($"  {binding.BindingClass}: {binding.LogicalId} — {binding.Description}");
            logger.WriteLine(
                "Re-run with --binding <logical-id> for each one the target already provides.",
                ConsoleColor.Cyan);
            return (int)TenantPortabilityExitCode.BindingsRequired;
        }

        logger.WriteLine("Preflight clean: every required binding is supplied.", ConsoleColor.Green);
        return (int)TenantPortabilityExitCode.Ok;
    }

    private static async Task<int> ImportAsync(CliContext ctx, ILogger logger, CancellationToken ct)
    {
        var root = RequireBundle(ctx);
        var bindings = ParseBindingMap(ctx.TenantBindings);
        var collision = (ctx.TenantCollisionPolicy ?? "fail").ToLowerInvariant() switch
        {
            "fail" => TenantImportCollisionPolicy.Fail,
            "proceed" => TenantImportCollisionPolicy.Proceed,
            _ => throw new ArgumentException("--collision must be fail or proceed.")
        };

        var portalUrl = PortalAdminCredentials.ResolveUrl(ctx.PortalUrl);
        var portalUser = Environment.GetEnvironmentVariable("ETLSQL_PORTAL_IMPORT_USERNAME");
        if (string.IsNullOrWhiteSpace(portalUser))
            throw new AdminCliException(AdminExitCode.AuthFailure,
                "No import administrator username. Set ETLSQL_PORTAL_IMPORT_USERNAME.");
        var portalPassword = await ResolveRequiredEnvironmentSecretAsync(
            "ETLSQL_PORTAL_IMPORT_PASSWORD", ct).ConfigureAwait(false);
        var portal = new EnginePortalConfigurationTarget(Program.ServiceProvider,
            () => new PortalDataSource(portalUrl, portalUser, portalPassword, logger));
        var orchestrator = new OrchestratorPackageTarget(
            Program.ServiceProvider.GetRequiredService<IJobHistoryStore>(),
            Program.ServiceProvider.GetRequiredService<IJobCatalogStore>(),
            Program.ServiceProvider.GetRequiredService<ILineageCatalogStore>());
        var result = await TenantBundleImporter.ImportAsync(root,
            new TenantImportOptions(
                bindings,
                NullIfWhiteSpace(ctx.TenantOperatorKey),
                ctx.TenantRequireSignature,
                NullIfWhiteSpace(ctx.TenantRecipientKey),
                await ResolveOptionalPassphraseAsync("ETLSQL_TENANT_RECIPIENT_PASSPHRASE", ct).ConfigureAwait(false),
                collision,
                ctx.TenantDryRun,
                ctx.TenantBaseConsistencyPoint),
            portal, orchestrator, ct).ConfigureAwait(false);

        foreach (var entry in result.Plan)
            logger.WriteLine($"{entry.Action} {entry.Kind}: {entry.Name}",
                entry.Action == "Collision" ? ConsoleColor.Yellow : ConsoleColor.Cyan);

        if (!result.Applied)
        {
            logger.WriteLine(result.RefusalReason ?? "Nothing was applied.",
                result.ExitCode == TenantPortabilityExitCode.Ok ? ConsoleColor.Cyan : ConsoleColor.Red);
            return (int)result.ExitCode;
        }

        logger.WriteLine(
            $"Tenant bundle imported; {result.OrchestratorObjects} Orchestrator object(s) arrived disabled.",
            ConsoleColor.Green);
        return (int)result.ExitCode;
    }

    private static string RequireBundle(CliContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.TenantBundleRoot))
            throw new ArgumentException("--bundle is required and must point at a bundle directory.");
        return Path.GetFullPath(ctx.TenantBundleRoot);
    }

    private static async Task<PortalAdminClient> AuthenticatedPortalClientAsync(
        CliContext ctx, CancellationToken ct)
    {
        var url = PortalAdminCredentials.ResolveUrl(ctx.PortalUrl);
        var credentials = await PortalAdminCredentials.ResolveAsync(
            ResolveSecretProvider(), ctx.PortalClientId, ct).ConfigureAwait(false);
        var client = PortalAdminClient.Create(url);
        await client.AuthenticateAsync(credentials, ct).ConfigureAwait(false);
        return client;
    }

    private static async Task<OrchestratorPromotionPackageService.Package?> ReadOrchestratorPackageAsync(
        string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        await using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, useAsync: true);
        return await OrchestratorPromotionPackageService.ReadAsync(stream, ct).ConfigureAwait(false);
    }

    internal static IReadOnlyDictionary<string, string> ParseBindingMap(IEnumerable<string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
                throw new ArgumentException($"Invalid binding '{value}'. Import requires SOURCE=TARGET.");
            var source = value[..separator].Trim();
            var target = value[(separator + 1)..].Trim();
            if (!result.TryAdd(source, target))
                throw new ArgumentException($"Duplicate binding for '{source}'.");
        }
        return result;
    }

    internal static IReadOnlyList<string> ParseSuppliedBindings(IEnumerable<string>? values) =>
        [.. (values ?? []).Select(value =>
        {
            var separator = value.IndexOf('=');
            return (separator > 0 ? value[..separator] : value).Trim();
        }).Where(value => value.Length > 0)];

    private static async Task<string?> ResolveOptionalPassphraseAsync(string variable, CancellationToken ct)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)) return value;
        var provider = ResolveSecretProvider()
            ?? throw new InvalidOperationException(
                $"{variable} is a SECRET: reference, but no machine secret provider is configured.");
        var name = value["SECRET:".Length..].Trim().Trim('\'', '"');
        if (name.Length == 0) throw new ArgumentException($"{variable} contains an empty SECRET: reference.");
        return (await provider.ResolveAsync(name, ct).ConfigureAwait(false)).Value;
    }

    private static async Task<string> ResolveRequiredEnvironmentSecretAsync(
        string variable, CancellationToken ct)
    {
        var value = await ResolveOptionalPassphraseAsync(variable, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(value))
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"No import administrator password. Set {variable}, optionally to a SECRET:name reference.");
        return value;
    }

    private static ISecretProvider? ResolveSecretProvider()
    {
        try { return Program.ServiceProvider?.GetService(typeof(ISecretProvider)) as ISecretProvider; }
        catch { return null; }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
}
