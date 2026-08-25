using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Tests.Reporting.Baselines;

/// <summary>One gated figure: a name plus its blessed raw and gzip bytes.</summary>
public sealed record PayloadBudgetEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rawBytes")] long RawBytes,
    [property: JsonPropertyName("gzipBytes")] long GzipBytes);

/// <summary>
/// The reviewed browser-payload budget.
///
/// This is a blessed measurement, not a hand-picked ceiling. `report-runtime.js` grew from 217,299
/// bytes just after the ECharts retirement to 280,472 bytes with nobody noticing, because footprint
/// was observed in a report rather than gated. The budget closes that: growth past tolerance fails
/// the build, and the only way past it is an explicit, reviewed re-bless that lands in the diff.
/// </summary>
public sealed record ReportPayloadBudget(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("blessedUtc")] DateTime BlessedUtc,
    [property: JsonPropertyName("blessedOnBranch")] string BlessedOnBranch,
    [property: JsonPropertyName("tolerancePct")] double TolerancePct,
    [property: JsonPropertyName("toleranceFloorBytes")] long ToleranceFloorBytes,
    [property: JsonPropertyName("assets")] IReadOnlyList<PayloadBudgetEntry> Assets,
    [property: JsonPropertyName("sharedTotal")] PayloadBudgetEntry SharedTotal,
    [property: JsonPropertyName("pageWeight")] PayloadBudgetEntry PageWeight)
{
    public const string CurrentSchema = "etlsql.report-payload-budget/1";

    /// <summary>Assets gated individually. The rest are covered by the shared total.</summary>
    public static readonly string[] GatedAssets = ["report-runtime.js", "report-runtime.css"];

    /// <summary>Repo-relative location of the blessed budget.</summary>
    public static string Path(string repoRoot) =>
        System.IO.Path.Combine(repoRoot, "docs", "benchmarks", "report-payload-budget.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static ReportPayloadBudget? Load(string repoRoot)
    {
        var path = Path(repoRoot);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ReportPayloadBudget>(File.ReadAllText(path), Json)
            : null;
    }

    public void Save(string repoRoot)
    {
        var path = Path(repoRoot);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json) + Environment.NewLine);
    }

    /// <summary>Measures the current tree into a budget shaped exactly like the blessed one.</summary>
    public static ReportPayloadBudget Measure(
        string repoRoot,
        IReadOnlyList<BundleAssetMeasurement> assets,
        IReadOnlyList<PageWeightMeasurement> pageWeights,
        double tolerancePct,
        long toleranceFloorBytes,
        string version,
        string branch)
    {
        var gated = GatedAssets
            .Select(name => assets.FirstOrDefault(asset =>
                asset.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Gated asset '{name}' is not present under Resources/Shared."))
            .Select(asset => new PayloadBudgetEntry(asset.RelativePath, asset.RawBytes, asset.GzipBytes))
            .ToList();

        // The heaviest representative report is the gated page weight: a budget set on the lightest
        // one would pass while the worst page regressed.
        var heaviest = pageWeights.OrderByDescending(weight => weight.TotalRawBytes).FirstOrDefault();

        return new ReportPayloadBudget(
            CurrentSchema,
            version,
            DateTime.UtcNow,
            branch,
            tolerancePct,
            toleranceFloorBytes,
            gated,
            new PayloadBudgetEntry("shared-runtime-total", assets.Sum(a => a.RawBytes), assets.Sum(a => a.GzipBytes)),
            heaviest is null
                ? new PayloadBudgetEntry("page-weight-heaviest", 0, 0)
                : new PayloadBudgetEntry($"page-weight:{heaviest.FixtureName}", heaviest.TotalRawBytes, heaviest.TotalGzipBytes));
    }

    /// <summary>Every figure in this budget that exceeds the blessed one past tolerance.</summary>
    public IReadOnlyList<string> RegressionsAgainst(ReportPayloadBudget blessed)
    {
        var failures = new List<string>();
        foreach (var entry in Assets)
        {
            var baseline = blessed.Assets.FirstOrDefault(item =>
                item.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            if (baseline is null)
            {
                failures.Add($"{entry.Name}: not present in the blessed budget — re-bless to add it.");
                continue;
            }
            Compare(failures, blessed, entry.Name, baseline, entry);
        }

        Compare(failures, blessed, "shared runtime total", blessed.SharedTotal, SharedTotal);

        // A page-weight budget blessed on a different fixture is not comparable.
        if (!blessed.PageWeight.Name.Equals(PageWeight.Name, StringComparison.OrdinalIgnoreCase))
            failures.Add($"page weight: blessed on '{blessed.PageWeight.Name}' but the heaviest fixture is now '{PageWeight.Name}' — re-bless.");
        else
            Compare(failures, blessed, "page weight", blessed.PageWeight, PageWeight);

        return failures;
    }

    private static void Compare(
        ICollection<string> failures,
        ReportPayloadBudget blessed,
        string label,
        PayloadBudgetEntry baseline,
        PayloadBudgetEntry current)
    {
        Check(failures, blessed, $"{label} raw", baseline.RawBytes, current.RawBytes);
        Check(failures, blessed, $"{label} gzip", baseline.GzipBytes, current.GzipBytes);
    }

    private static void Check(
        ICollection<string> failures,
        ReportPayloadBudget blessed,
        string label,
        long baseline,
        long current)
    {
        var limit = (long)Math.Ceiling(baseline * (1 + blessed.TolerancePct / 100d)) + blessed.ToleranceFloorBytes;
        if (current > limit)
        {
            failures.Add(
                $"{label}: {current:N0} B exceeds the budget of {baseline:N0} B " +
                $"(+{blessed.TolerancePct}% +{blessed.ToleranceFloorBytes:N0} B = {limit:N0} B), " +
                $"a {(double)(current - baseline) / Math.Max(1, baseline):P1} regression.");
        }
    }
}
