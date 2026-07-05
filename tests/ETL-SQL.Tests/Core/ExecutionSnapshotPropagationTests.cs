using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// TODO 3.2 verification: the enforcement snapshot is captured once when a top-level execution
/// begins and the same frozen instance flows to child (RUN SCRIPT / recursive) and forked (parallel
/// branch) contexts. Hosts differ only in how they set <see cref="Evaluator.JobName"/> and
/// <see cref="Evaluator.InteractiveMode"/> before <c>Evaluate</c>, which the captured snapshot's
/// <see cref="ScriptExecutionMode"/> reflects — so proving capture + Fork + recursion here proves it
/// for CLI, TUI, Report Player, Portal, Orchestrator, and scheduled jobs alike (every host runs the
/// same <see cref="Evaluator"/>).
/// </summary>
public sealed class ExecutionSnapshotPropagationTests : IDisposable
{
    public void Dispose() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

    [Fact]
    public async Task Snapshot_IsCapturedAtExecutionBegin_AndFrozenAgainstLaterRuntimeChange()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy("v1"));
        var evaluator = NewEvaluator();

        await evaluator.Evaluate(Parse("DECLARE @x INT = 1;"));

        var snapshot = evaluator.ExecutionPolicy;
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsEnrolled);
        Assert.Equal("v1", snapshot.PolicyVersion);
        Assert.Equal(ScriptExecutionMode.Batch, snapshot.ExecutionMode);

        // A later runtime change does not mutate the already-captured snapshot; freshness surfaces
        // the drift instead, leaving the captured instance frozen for the run's lifetime.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy("v2"));
        Assert.Equal("v1", evaluator.ExecutionPolicy!.PolicyVersion);
        Assert.True(snapshot.GetFreshness(EnterprisePolicyRuntime.Current).CurrentPolicyChanged);
    }

    [Fact]
    public async Task Snapshot_FlowsToForkedParallelBranchContexts()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy("v1"));
        var evaluator = NewEvaluator();
        await evaluator.Evaluate(Parse("DECLARE @x INT = 1;"));

        var fork = evaluator.Fork();

        // A parallel branch (context.Fork()) carries the identical frozen snapshot, so every branch
        // enforces the same organization policy and shares the run's correlation identity.
        Assert.NotNull(fork.ExecutionPolicy);
        Assert.Same(evaluator.ExecutionPolicy, fork.ExecutionPolicy);
        Assert.Equal(evaluator.ExecutionPolicy!.CorrelationId, fork.ExecutionPolicy!.CorrelationId);
    }

    [Fact]
    public async Task Snapshot_IsCapturedOnceAndSurvivesRecursiveDepth()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy("v1"));
        var evaluator = NewEvaluator();
        await evaluator.Evaluate(Parse("DECLARE @x INT = 1;"));
        var captured = evaluator.ExecutionPolicy;

        // RUN SCRIPT / EXECUTE recurse on the same context (capture is guarded by depth == 0), so a
        // nested scope must not re-capture or discard the top-level snapshot mid-run.
        using (evaluator.EnterRecursiveScope())
        {
            Assert.Same(captured, evaluator.ExecutionPolicy);
            EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy("v2"));
            await evaluator.Evaluate(Parse("DECLARE @y INT = 2;"));
            Assert.Same(captured, evaluator.ExecutionPolicy);
        }

        Assert.Equal("v1", evaluator.ExecutionPolicy!.PolicyVersion);
    }

    [Theory]
    [InlineData(false, null, ScriptExecutionMode.Batch)]
    [InlineData(false, "nightly-load", ScriptExecutionMode.Scheduled)]
    [InlineData(true, null, ScriptExecutionMode.Interactive)]
    public async Task Snapshot_ExecutionMode_ReflectsHostInvocation(
        bool interactive, string? jobName, ScriptExecutionMode expected)
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy("v1"));
        var evaluator = NewEvaluator();
        evaluator.InteractiveMode = interactive;
        evaluator.JobName = jobName;

        await evaluator.Evaluate(Parse("DECLARE @x INT = 1;"));

        Assert.Equal(expected, evaluator.ExecutionPolicy!.ExecutionMode);
        Assert.Equal(jobName, evaluator.ExecutionPolicy!.JobId);
    }

    private static Evaluator NewEvaluator() =>
        ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static EffectiveEnterprisePolicy EnrolledPolicy(string version)
    {
        var document = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')] }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", version, "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}
