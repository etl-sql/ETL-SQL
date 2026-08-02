using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.App;

/// <summary>Builds stable, counts-only CLI evidence from the engine's per-run quality report.</summary>
public static class QualityRunReporter
{
    public const string SchemaVersion = "1.0";

    public static QualityRunEvidence Create(
        string scriptPath,
        int exitCode,
        string status,
        long rowsProcessed,
        DataQualityReport? report,
        string? error = null) =>
        new(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            Path.GetFullPath(scriptPath),
            status,
            exitCode,
            rowsProcessed,
            report?.RowsValidated ?? 0,
            report?.RowsWarned ?? 0,
            report?.RowsQuarantined ?? 0,
            report?.RowsDryRunAffected ?? 0,
            report?.TotalFailures ?? 0,
            report?.Failures.Select(f => new QualityRuleEvidence(
                f.Column, f.Rule, f.Action.ToString().ToUpperInvariant(), f.Count, f.Owner)).ToArray()
                ?? Array.Empty<QualityRuleEvidence>(),
            report?.ColumnMetrics.ToArray() ?? Array.Empty<DataQualityColumnMetric>(),
            string.IsNullOrWhiteSpace(error) ? null : SecretRedactor.Redact(error));

    public static async Task WriteAsync(
        string outputPath,
        QualityRunEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var stream = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, evidence, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static void WriteSummary(TextWriter writer, QualityRunEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteLine("Data Quality Summary");
        writer.WriteLine($"Status: {evidence.Status}");
        writer.WriteLine($"Rows: processed={evidence.RowsProcessed:N0}, validated={evidence.RowsValidated:N0}, warned={evidence.RowsWarned:N0}, quarantined={evidence.RowsQuarantined:N0}");
        writer.WriteLine($"Rule failures: {evidence.TotalFailures:N0}");
        foreach (var rule in evidence.RuleFailures)
            writer.WriteLine($"- {rule.Column} | {rule.Action} | {rule.Rule} | {rule.Count:N0}" +
                (string.IsNullOrWhiteSpace(rule.Owner) ? string.Empty : $" | owner={rule.Owner}"));
    }
}

public sealed record QualityRunEvidence(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string ScriptPath,
    string Status,
    int ExitCode,
    long RowsProcessed,
    long RowsValidated,
    long RowsWarned,
    long RowsQuarantined,
    long RowsDryRunAffected,
    long TotalFailures,
    IReadOnlyList<QualityRuleEvidence> RuleFailures,
    IReadOnlyList<DataQualityColumnMetric> ColumnMetrics,
    string? Error);

public sealed record QualityRuleEvidence(
    string Column,
    string Rule,
    string Action,
    long Count,
    string? Owner);
