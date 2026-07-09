using ETL_SQL.Core.Adaptive;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class AdaptiveExecutionControllerTests
{
    private static AdaptiveExecutionOptions Options() => new()
    {
        Enabled = true,
        ConsecutiveHighSamples = 2,
        ConsecutiveLowSamples = 8,
        CooldownSamples = 4,
        MinBatchRows = 1000,
        MinOperatorGrantRequestMB = 64,
        LegacyBatchRows = 10000,
        LegacyWorkerDegree = 4,
        LegacyPipelineDepth = 1,
        LegacySpillWriteConcurrency = 1,
        LegacyOperatorGrantRequestMB = 256
    };

    private static AdaptiveExecutionCeilings Ceilings() => new(
        BatchRows: 40000,
        WorkerDegree: 8,
        PipelineDepth: 2,
        SpillWriteConcurrency: 2,
        OperatorGrantRequestMB: 1024);

    [Fact]
    public void DisabledController_DoesNotChangeSetpoints()
    {
        var controller = new AdaptiveExecutionController(Options() with { Enabled = false });
        using var advisor = controller.CreateAdvisor(Ceilings());
        var before = advisor.Snapshot();

        controller.Observe(new ResourceSignals(1, 1, 1));
        controller.Observe(new ResourceSignals(1, 1, 1));

        Assert.Equal(before, advisor.Snapshot());
        Assert.All(controller.Decisions, d => Assert.Equal(AdaptiveDecisionKind.None, d.Kind));
    }

    [Fact]
    public void HighPressure_ScalesDownAfterConsecutiveSamples()
    {
        var controller = new AdaptiveExecutionController(Options());
        using var advisor = controller.CreateAdvisor(Ceilings());

        var first = controller.Observe(new ResourceSignals(CpuUtilization: 0.95, MemoryLoad: 0.2, GrantPressure: 0.2));
        var second = controller.Observe(new ResourceSignals(CpuUtilization: 0.95, MemoryLoad: 0.2, GrantPressure: 0.2));

        Assert.Equal(AdaptiveDecisionKind.None, first.Kind);
        Assert.Equal(AdaptiveDecisionKind.ScaleDown, second.Kind);
        Assert.Equal("cpu-high", second.Reason);
        Assert.Equal(5000, advisor.Snapshot().BatchRows);
        Assert.Equal(2, advisor.Snapshot().WorkerDegree);
        Assert.Equal(128, advisor.Snapshot().OperatorGrantRequestMB);
    }

    [Fact]
    public void ScaleDown_StopsAtFloors()
    {
        var controller = new AdaptiveExecutionController(Options() with { CooldownSamples = 0 });
        using var advisor = controller.CreateAdvisor(new AdaptiveExecutionCeilings(1000, 1, 0, 1, 64));

        for (var i = 0; i < 8; i++)
            controller.Observe(new ResourceSignals(CpuUtilization: 0.95, MemoryLoad: 0.2, GrantPressure: 0.2));

        Assert.Equal(new AdaptiveSetpoints(1000, 1, 0, 1, 64), advisor.Snapshot());
    }

    [Fact]
    public void IdleCapacity_ScalesUpSlowlyWithinCeilings()
    {
        var controller = new AdaptiveExecutionController(Options());
        using var advisor = controller.CreateAdvisor(Ceilings());

        for (var i = 0; i < 7; i++)
            Assert.Equal(AdaptiveDecisionKind.None, controller.Observe(ResourceSignals.Idle).Kind);

        var decision = controller.Observe(ResourceSignals.Idle);

        Assert.Equal(AdaptiveDecisionKind.ScaleUp, decision.Kind);
        Assert.Equal(20000, advisor.Snapshot().BatchRows);
        Assert.Equal(4, advisor.Snapshot().WorkerDegree);
        Assert.True(advisor.Snapshot().BatchRows <= advisor.ConfiguredCeilings.BatchRows);
    }

    [Fact]
    public void Cooldown_PreventsImmediateRepeatedChanges()
    {
        var controller = new AdaptiveExecutionController(Options());
        using var advisor = controller.CreateAdvisor(Ceilings());

        controller.Observe(new ResourceSignals(0.95, 0.2, 0.2));
        controller.Observe(new ResourceSignals(0.95, 0.2, 0.2));
        var afterScaleDown = advisor.Snapshot();

        for (var i = 0; i < 4; i++)
            Assert.Equal(AdaptiveDecisionKind.None, controller.Observe(new ResourceSignals(0.95, 0.2, 0.2)).Kind);

        Assert.Equal(afterScaleDown, advisor.Snapshot());
    }

    [Fact]
    public void Deadband_DoesNotAccumulateTowardScaleUpOrDown()
    {
        var controller = new AdaptiveExecutionController(Options());
        using var advisor = controller.CreateAdvisor(Ceilings());
        var initial = advisor.Snapshot();

        for (var i = 0; i < 20; i++)
            controller.Observe(new ResourceSignals(0.70, 0.60, 0.60));

        Assert.Equal(initial, advisor.Snapshot());
    }

    [Fact]
    public void Fairness_CapsWorkerAndGrantCeilingsAcrossConcurrentAdvisors()
    {
        var controller = new AdaptiveExecutionController(
            Options(),
            totalGrantBudgetBytes: 512L * 1024 * 1024,
            processorCount: 8);

        using var first = controller.CreateAdvisor(Ceilings());
        Assert.Equal(4, first.Snapshot().WorkerDegree);
        Assert.Equal(256, first.Snapshot().OperatorGrantRequestMB);

        using var second = controller.CreateAdvisor(Ceilings());

        Assert.Equal(4, first.Snapshot().WorkerDegree);
        Assert.Equal(4, second.Snapshot().WorkerDegree);
        Assert.Equal(256, first.Snapshot().OperatorGrantRequestMB);
        Assert.Equal(256, second.Snapshot().OperatorGrantRequestMB);

        for (var i = 0; i < 8; i++)
            controller.Observe(ResourceSignals.Idle);

        Assert.Equal(4, first.Snapshot().WorkerDegree);
        Assert.Equal(4, second.Snapshot().WorkerDegree);
        Assert.True(first.Snapshot().OperatorGrantRequestMB <= 256);
        Assert.True(second.Snapshot().OperatorGrantRequestMB <= 256);
    }

    [Fact]
    public void DisposingAdvisor_RecomputesFairShareForRemainingAdvisor()
    {
        var controller = new AdaptiveExecutionController(Options(), processorCount: 8);
        using var first = controller.CreateAdvisor(Ceilings());
        var second = controller.CreateAdvisor(Ceilings());

        second.Dispose();

        for (var i = 0; i < 20; i++)
            controller.Observe(ResourceSignals.Idle);

        Assert.True(first.Snapshot().WorkerDegree > 4);
        Assert.Equal(1, controller.ActiveAdvisorCount);
    }
}
