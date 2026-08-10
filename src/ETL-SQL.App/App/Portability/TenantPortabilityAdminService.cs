using ETL_SQL.Common;
using ETL_SQL.Core;

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
                "admin-tenant-validate" => await ValidateAsync(ctx, logger, ct),
                _ => await PreflightAsync(ctx, logger, ct)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or InvalidDataException or ArgumentException)
        {
            logger.WriteLine($"Tenant {ctx.Command["admin-tenant-".Length..]} failed: {ex.Message}", ConsoleColor.Red);
            return (int)TenantPortabilityExitCode.BundleInvalid;
        }
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
        var supplied = ctx.TenantBindings ?? [];
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

    private static string RequireBundle(CliContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.TenantBundleRoot))
            throw new ArgumentException("--bundle is required and must point at a bundle directory.");
        return Path.GetFullPath(ctx.TenantBundleRoot);
    }
}
