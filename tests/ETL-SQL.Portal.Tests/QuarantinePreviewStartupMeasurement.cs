using System.Diagnostics;
using ETL_SQL.Core;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Measures what a quarantine row preview costs before the row read — the per-request
/// <see cref="ExecutionSession"/> construct, execute and dispose that
/// <c>GET /api/data-quality/quarantine/rows</c> performs every time it is called.
///
/// <para>This exists to answer one question with a number instead of an intuition: is that cost
/// small enough that the steward queue could poll the preview, or refresh a dashboard from it,
/// without a bounded reusable session? Building the reusable path first would have meant
/// optimising something nobody had measured — and a reusable preview session is exactly the kind
/// of shortcut that quietly weakens the parsing, policy, RLS, timeout, row-cap and redaction
/// guarantees the single-shot session gets for free.</para>
///
/// <para><b>Deliberately not a gate.</b> Scale certification on this repository has produced a 56%
/// spread between warmed and cold measurements of the same commit, wide enough to swamp any
/// threshold worth setting. So this reports, and asserts only an order-of-magnitude ceiling that no
/// thermal state can cross. The number that matters is written into the decision record, not
/// enforced here.</para>
///
/// <para>Run explicitly: <c>dotnet test --filter "Category=Performance&amp;FullyQualifiedName~QuarantinePreview"</c>.</para>
/// </summary>
[Trait("Category", "Performance")]
public sealed class QuarantinePreviewStartupMeasurement
{
    private const int WarmupIterations = 5;
    private const int MeasuredIterations = 25;

    /// <summary>
    /// An order of magnitude above anything observed. A session that takes longer than this is not
    /// a thermal artefact, it is a structural change — which is the only thing worth failing on
    /// given the measurement noise this repository has documented.
    /// </summary>
    private const int CeilingMs = 2000;

    [Fact]
    public async Task PreviewSessionStartup_IsMeasuredAndReported()
    {
        using var factory = new HostedPortalFactory();
        var services = factory.Services;
        var engineLogger = services.GetRequiredService<ETL_SQL.Common.ILogger>();

        // A trivial script: this measures the session, not the target read. What a preview costs
        // beyond this depends on the quarantine target's own connector and row count, which is not
        // what a reusable-session optimisation would change.
        const string script = "SELECT 1 AS Probe INTO #probe;";

        for (int i = 0; i < WarmupIterations; i++)
            await RunOnceAsync(services, engineLogger, script);

        var samples = new List<double>(MeasuredIterations);
        for (int i = 0; i < MeasuredIterations; i++)
            samples.Add(await RunOnceAsync(services, engineLogger, script));

        samples.Sort();
        var median = samples[samples.Count / 2];
        var p95 = samples[(int)(samples.Count * 0.95)];
        var min = samples[0];
        var max = samples[^1];

        // Printed rather than only asserted: the point of this test is the number.
        Console.WriteLine(
            $"[quarantine-preview-session] n={MeasuredIterations} "
            + $"min={min:F1}ms median={median:F1}ms p95={p95:F1}ms max={max:F1}ms");

        Assert.True(median < CeilingMs,
            $"Preview session startup median was {median:F1}ms, above the {CeilingMs}ms structural "
            + "ceiling. This is not a thermal artefact at that magnitude — something changed about "
            + "what a session builds.");
    }

    private static async Task<double> RunOnceAsync(
        IServiceProvider services, ETL_SQL.Common.ILogger logger, string script)
    {
        var context = new CliContext
        {
            Command = "run",
            BatchSize = 50,
            IsSilentMode = true,
            SessionId = $"dq-rows-measure-{Guid.NewGuid():N}"
        };

        var stopwatch = Stopwatch.StartNew();
        await using (var session = new ExecutionSession(services, context, logger))
        {
            await session.ExecuteAsync(script, CancellationToken.None, "portal-data-quality-rows");
        }
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }
}
