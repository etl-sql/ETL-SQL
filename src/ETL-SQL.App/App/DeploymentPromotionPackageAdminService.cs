using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App;

internal static class DeploymentPromotionPackageAdminService
{
    internal static async Task<int> RunAsync(CliContext ctx, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var history = Program.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var catalog = Program.ServiceProvider.GetRequiredService<IJobCatalogStore>();
            var lineage = Program.ServiceProvider.GetRequiredService<ILineageCatalogStore>();
            return ctx.Command switch
            {
                "admin-promotion-export" => await ExportAsync(ctx, logger, history, catalog, lineage, ct),
                "admin-promotion-validate" => await ValidateAsync(ctx, logger, history, catalog, ct),
                _ => await ImportAsync(ctx, logger, history, catalog, lineage, ct)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                   or InvalidOperationException or ArgumentException)
        {
            logger.WriteLine($"Promotion {ctx.Command["admin-promotion-".Length..]} failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    private static async Task<int> ExportAsync(CliContext ctx, ILogger logger, IJobHistoryStore history,
        IJobCatalogStore catalog, ILineageCatalogStore lineage, CancellationToken ct)
    {
        var output = Path.GetFullPath(ctx.PromotionOutput
            ?? Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-promotion.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var package = await OrchestratorPromotionPackageService.ExportAsync(
            history, catalog, lineage, Math.Clamp(ctx.PromotionHistoryLimit, 1, 10_000), ct);
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await OrchestratorPromotionPackageService.WriteAsync(package, stream, ct);
        logger.WriteLine($"Orchestrator promotion package: {output}", ConsoleColor.Green);
        logger.WriteLine(
            $"Exported {package.Jobs.Count} job(s), {package.Schedules.Count} schedule(s), " +
            $"{package.QualityHistory.Count} quality run(s), and {package.LineageAndTags.Count} lineage/tag row(s).",
            ConsoleColor.Cyan);
        if (package.RequiredSecretReferences.Count > 0)
            logger.WriteLine($"Provision {package.RequiredSecretReferences.Count} referenced secret name(s) in the target secret provider.", ConsoleColor.Yellow);
        return 0;
    }

    private static async Task<int> ImportAsync(CliContext ctx, ILogger logger, IJobHistoryStore history,
        IJobCatalogStore catalog, ILineageCatalogStore lineage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.PromotionPackage))
            throw new ArgumentException("--package is required for promotion import.");
        var path = Path.GetFullPath(ctx.PromotionPackage);
        var bindings = ParseBindings(ctx.PromotionBindings);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var package = await OrchestratorPromotionPackageService.ReadAsync(stream, ct);
        var result = await OrchestratorPromotionPackageService.ImportAsync(package, history, catalog, lineage, bindings, ct);
        logger.WriteLine(
            $"Imported {result.Jobs} job(s), {result.Schedules} schedule(s), {result.QualityRuns} quality run(s), " +
            $"and {result.LineageEntries} new lineage/tag row(s). Re-import is idempotent.",
            ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> ValidateAsync(CliContext ctx, ILogger logger, IJobHistoryStore history,
        IJobCatalogStore catalog, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.PromotionPackage))
            throw new ArgumentException("--package is required for promotion validation.");
        var path = Path.GetFullPath(ctx.PromotionPackage);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var package = await OrchestratorPromotionPackageService.ReadAsync(stream, ct);
        var validation = await OrchestratorPromotionPackageService.ValidateAsync(
            package, history, catalog, ParseBindings(ctx.PromotionBindings), ct);
        if (!string.IsNullOrWhiteSpace(ctx.PromotionOutput))
        {
            var output = Path.GetFullPath(ctx.PromotionOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using var report = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await System.Text.Json.JsonSerializer.SerializeAsync(report, validation,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }, ct);
        }
        foreach (var finding in validation.Findings)
            logger.WriteLine($"{finding.Severity} {finding.Code} {finding.Resource}: {finding.Message}",
                finding.Severity == "Error" ? ConsoleColor.Red : ConsoleColor.Yellow);
        logger.WriteLine(validation.IsValid ? "Promotion validation passed; no target state changed."
            : "Promotion validation failed; no target state changed.", validation.IsValid ? ConsoleColor.Green : ConsoleColor.Red);
        return validation.IsValid ? 0 : 1;
    }

    internal static IReadOnlyDictionary<string, string> ParseBindings(IEnumerable<string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
                throw new ArgumentException($"Invalid binding '{value}'. Use SOURCE=TARGET.");
            var source = value[..separator].Trim();
            var target = value[(separator + 1)..].Trim();
            if (!result.TryAdd(source, target))
                throw new ArgumentException($"Duplicate binding for '{source}'.");
        }
        return result;
    }
}
