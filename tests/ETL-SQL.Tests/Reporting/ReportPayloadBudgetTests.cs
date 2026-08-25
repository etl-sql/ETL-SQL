using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Tests.Reporting.Baselines;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// The browser-payload regression gate.
///
/// Phase 8 measured footprint into a report and moved on, so `report-runtime.js` grew back past the
/// size the ECharts retirement had shrunk it to without a single failing check. These tests gate raw
/// and gzip bytes the way the engine allocation budgets are gated: a blessed measurement in
/// `docs/benchmarks/report-payload-budget.json`, a tolerance, and an explicit re-bless path
/// (`scripts/Test-ReportPayloadBudget.ps1 -UpdateBudget`) that lands the new numbers in the diff for
/// review. There is no magic hard ceiling to argue with.
/// </summary>
public sealed class ReportPayloadBudgetTests
{
    /// <summary>Set by the re-bless script; nothing else writes the budget.</summary>
    private const string UpdateVariable = "ETLSQL_REPORT_PAYLOAD_BUDGET_UPDATE";

    private const double TolerancePct = 3;
    private const long ToleranceFloorBytes = 2048;

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public async Task BrowserPayload_StaysWithinTheReviewedBudget()
    {
        var repoRoot = RepoRoot();
        var assets = ReportingBaselineMeasurementHarness.MeasureBundleAssets(repoRoot);
        var fixtures = await ReportingBaselineMeasurementHarness.MeasureRepresentativeFixturesAsync(repoRoot);
        var pageWeights = ReportingBaselineMeasurementHarness.MeasurePageWeights(assets, fixtures);

        var current = ReportPayloadBudget.Measure(
            repoRoot, assets, pageWeights, TolerancePct, ToleranceFloorBytes,
            version: "0.19.0",
            branch: Environment.GetEnvironmentVariable("ETLSQL_REPORT_BASELINE_BRANCH") ?? "unknown");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            current.Save(repoRoot);
            return;
        }

        var blessed = ReportPayloadBudget.Load(repoRoot);
        Assert.True(blessed is not null,
            $"No blessed payload budget at {ReportPayloadBudget.Path(repoRoot)}. " +
            "Establish one with: pwsh -File scripts\\Test-ReportPayloadBudget.ps1 -UpdateBudget");
        Assert.Equal(ReportPayloadBudget.CurrentSchema, blessed!.Schema);

        var regressions = current.RegressionsAgainst(blessed);
        Assert.True(regressions.Count == 0,
            "Browser payload regressed past the reviewed budget:" + Environment.NewLine +
            string.Join(Environment.NewLine, regressions.Select(line => "  - " + line)) + Environment.NewLine +
            "If the growth is intended, re-bless it for review: " +
            "pwsh -File scripts\\Test-ReportPayloadBudget.ps1 -UpdateBudget");
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void PayloadBudget_GatesBothRawAndGzipForTheRuntimeBundle()
    {
        var blessed = ReportPayloadBudget.Load(RepoRoot());
        Assert.NotNull(blessed);

        var runtime = blessed!.Assets.FirstOrDefault(entry =>
            entry.Name.Equals("report-runtime.js", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(runtime);
        Assert.True(runtime!.RawBytes > 0 && runtime.GzipBytes > 0);
        Assert.True(runtime.GzipBytes < runtime.RawBytes);
        Assert.True(blessed.SharedTotal.RawBytes >= runtime.RawBytes);

        // Page weight is the shared assets plus a report's own manifest, so it must exceed either half.
        Assert.True(blessed.PageWeight.RawBytes > blessed.SharedTotal.RawBytes);
        Assert.StartsWith("page-weight:", blessed.PageWeight.Name, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Smoke.Reporting")]
    public void PayloadBudget_FailsOnGrowthPastToleranceAndPassesOnShrink()
    {
        var blessed = new ReportPayloadBudget(
            ReportPayloadBudget.CurrentSchema, "test", DateTime.UtcNow, "test", 3, 2048,
            [new PayloadBudgetEntry("report-runtime.js", 280_000, 70_000)],
            new PayloadBudgetEntry("shared-runtime-total", 1_600_000, 400_000),
            new PayloadBudgetEntry("page-weight:sample", 1_610_000, 403_000));

        var within = blessed with { Assets = [new PayloadBudgetEntry("report-runtime.js", 284_000, 70_500)] };
        Assert.Empty(within.RegressionsAgainst(blessed));

        var shrunk = blessed with { Assets = [new PayloadBudgetEntry("report-runtime.js", 210_000, 52_000)] };
        Assert.Empty(shrunk.RegressionsAgainst(blessed));

        var regressed = blessed with { Assets = [new PayloadBudgetEntry("report-runtime.js", 340_000, 70_000)] };
        var failures = regressed.RegressionsAgainst(blessed);
        Assert.Single(failures);
        Assert.Contains("report-runtime.js raw", failures[0]);
    }

    private static string RepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ETL-SQL.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
                return current;
            current = Path.GetDirectoryName(current);
        }
        return Directory.GetCurrentDirectory();
    }
}
