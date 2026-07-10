namespace ETL_SQL.Core.Adaptive;

/// <summary>
/// Process-local adaptive execution state machine. Slice A computes and records bounded setpoint
/// advice; execution pipelines opt in by reading their advisor at safe boundaries.
/// </summary>
public sealed class AdaptiveExecutionController
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AdaptiveAdvisor> _advisors = new();
    private readonly List<AdaptiveDecision> _decisions = new();
    private readonly TimeProvider _timeProvider;
    private int _highSamples;
    private int _lowSamples;
    private int _cooldown;
    private int _scaleUpCursor;

    public AdaptiveExecutionController(
        AdaptiveExecutionOptions? options = null,
        long totalGrantBudgetBytes = 0,
        int processorCount = 0,
        TimeProvider? timeProvider = null)
    {
        Options = options ?? new AdaptiveExecutionOptions();
        TotalGrantBudgetBytes = totalGrantBudgetBytes;
        ProcessorCount = processorCount > 0 ? processorCount : Environment.ProcessorCount;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AdaptiveExecutionOptions Options { get; }
    public long TotalGrantBudgetBytes { get; }
    public int ProcessorCount { get; }
    public int ActiveAdvisorCount { get { lock (_gate) return _advisors.Count; } }

    public IReadOnlyList<AdaptiveDecision> Decisions
    {
        get { lock (_gate) return _decisions.ToArray(); }
    }

    public AdaptiveAdvisor CreateAdvisor(AdaptiveExecutionCeilings ceilings)
    {
        var clamped = ceilings.Clamp(Options);
        var advisor = new AdaptiveAdvisor(this, Guid.NewGuid(), clamped, InitialSetpoints(clamped));
        lock (_gate)
        {
            _advisors.Add(advisor.Id, advisor);
            ApplyFairnessLocked();
        }

        return advisor;
    }

    public AdaptiveDecision Observe(ResourceSignals signals)
    {
        signals = signals.Clamp();
        lock (_gate)
        {
            if (_advisors.Count == 0 || !Options.Enabled)
                return RecordLocked(AdaptiveDecisionKind.None, "disabled-or-no-advisors", signals);

            var pressure = Classify(signals);
            if (_cooldown > 0)
            {
                _cooldown--;
                UpdateCounters(pressure);
                return RecordLocked(AdaptiveDecisionKind.None, "cooldown", signals);
            }

            UpdateCounters(pressure);
            if (_highSamples >= Options.ConsecutiveHighSamples)
            {
                foreach (var advisor in _advisors.Values)
                    advisor.Update(ScaleDown(advisor.Snapshot(), EffectiveCeilingsLocked(advisor)));

                _highSamples = 0;
                _lowSamples = 0;
                _cooldown = Options.CooldownSamples;
                return RecordLocked(AdaptiveDecisionKind.ScaleDown, pressure.HighReason, signals);
            }

            if (_lowSamples >= Options.ConsecutiveLowSamples)
            {
                foreach (var advisor in _advisors.Values)
                    advisor.Update(ScaleUp(advisor.Snapshot(), EffectiveCeilingsLocked(advisor)));

                _highSamples = 0;
                _lowSamples = 0;
                _cooldown = Options.CooldownSamples;
                return RecordLocked(AdaptiveDecisionKind.ScaleUp, "idle-capacity", signals);
            }

            return RecordLocked(AdaptiveDecisionKind.None, "deadband-or-waiting", signals);
        }
    }

    internal void Unregister(Guid advisorId)
    {
        lock (_gate)
        {
            _advisors.Remove(advisorId);
            ApplyFairnessLocked();
        }
    }

    private AdaptiveSetpoints InitialSetpoints(AdaptiveExecutionCeilings ceilings) => new(
        Math.Clamp(Math.Min(ceilings.BatchRows, Options.LegacyBatchRows), Options.MinBatchRows, ceilings.BatchRows),
        Math.Clamp(Math.Min(ceilings.WorkerDegree, Options.LegacyWorkerDegree), 1, ceilings.WorkerDegree),
        Math.Clamp(Math.Min(ceilings.PipelineDepth, Options.LegacyPipelineDepth), 0, ceilings.PipelineDepth),
        Math.Clamp(Math.Min(ceilings.SpillWriteConcurrency, Options.LegacySpillWriteConcurrency), 1, ceilings.SpillWriteConcurrency),
        Math.Clamp(Math.Min(ceilings.OperatorGrantRequestMB, Options.LegacyOperatorGrantRequestMB), Options.MinOperatorGrantRequestMB, ceilings.OperatorGrantRequestMB));

    private void ApplyFairnessLocked()
    {
        foreach (var advisor in _advisors.Values)
            advisor.Update(Clamp(advisor.Snapshot(), EffectiveCeilingsLocked(advisor)));
    }

    private AdaptiveExecutionCeilings EffectiveCeilingsLocked(AdaptiveAdvisor advisor)
    {
        var active = Math.Max(1, _advisors.Count);
        var workerShare = Math.Max(1, Math.Min(advisor.ConfiguredCeilings.WorkerDegree, ProcessorCount) / active);
        var grantShare = advisor.ConfiguredCeilings.OperatorGrantRequestMB;
        if (TotalGrantBudgetBytes > 0)
        {
            var grantBudgetMB = (int)Math.Max(1, TotalGrantBudgetBytes / 1024 / 1024);
            grantShare = Math.Min(grantShare, Math.Max(Options.MinOperatorGrantRequestMB, grantBudgetMB / active));
        }

        return new AdaptiveExecutionCeilings(
            advisor.ConfiguredCeilings.BatchRows,
            Math.Max(1, workerShare),
            advisor.ConfiguredCeilings.PipelineDepth,
            advisor.ConfiguredCeilings.SpillWriteConcurrency,
            grantShare).Clamp(Options);
    }

    private AdaptiveSetpoints ScaleDown(AdaptiveSetpoints current, AdaptiveExecutionCeilings ceilings)
    {
        var reduced = current with
        {
            BatchRows = Math.Max(Options.MinBatchRows, current.BatchRows / 2),
            WorkerDegree = Math.Max(1, current.WorkerDegree / 2),
            PipelineDepth = Math.Max(0, current.PipelineDepth / 2),
            SpillWriteConcurrency = Math.Max(1, current.SpillWriteConcurrency / 2),
            OperatorGrantRequestMB = Math.Max(Options.MinOperatorGrantRequestMB, current.OperatorGrantRequestMB / 2)
        };
        return Clamp(reduced, ceilings);
    }

    private AdaptiveSetpoints ScaleUp(AdaptiveSetpoints current, AdaptiveExecutionCeilings ceilings)
    {
        var next = current;
        for (var i = 0; i < 5; i++)
        {
            var cursor = (_scaleUpCursor++ % 5 + 5) % 5;
            next = cursor switch
            {
                0 when current.BatchRows < ceilings.BatchRows =>
                    current with { BatchRows = Math.Min(ceilings.BatchRows, current.BatchRows + Math.Max(1, ceilings.BatchRows / 4)) },
                1 when current.WorkerDegree < ceilings.WorkerDegree =>
                    current with { WorkerDegree = current.WorkerDegree + 1 },
                2 when current.PipelineDepth < ceilings.PipelineDepth =>
                    current with { PipelineDepth = current.PipelineDepth + 1 },
                3 when current.SpillWriteConcurrency < ceilings.SpillWriteConcurrency =>
                    current with { SpillWriteConcurrency = current.SpillWriteConcurrency + 1 },
                4 when current.OperatorGrantRequestMB < ceilings.OperatorGrantRequestMB =>
                    current with { OperatorGrantRequestMB = Math.Min(ceilings.OperatorGrantRequestMB, current.OperatorGrantRequestMB + Math.Max(1, ceilings.OperatorGrantRequestMB / 4)) },
                _ => current
            };
            if (!ReferenceEquals(next, current) && next != current)
                break;
        }

        return Clamp(next, ceilings);
    }

    private AdaptiveSetpoints Clamp(AdaptiveSetpoints setpoints, AdaptiveExecutionCeilings ceilings) => new(
        Math.Clamp(setpoints.BatchRows, Options.MinBatchRows, ceilings.BatchRows),
        Math.Clamp(setpoints.WorkerDegree, 1, ceilings.WorkerDegree),
        Math.Clamp(setpoints.PipelineDepth, 0, ceilings.PipelineDepth),
        Math.Clamp(setpoints.SpillWriteConcurrency, 1, ceilings.SpillWriteConcurrency),
        Math.Clamp(setpoints.OperatorGrantRequestMB, Options.MinOperatorGrantRequestMB, ceilings.OperatorGrantRequestMB));

    private PressureState Classify(ResourceSignals signals)
    {
        if (signals.CpuUtilization >= Options.CpuHigh) return new(true, false, "cpu-high");
        if (signals.MemoryLoad >= Options.MemoryHigh) return new(true, false, "memory-high");
        if (signals.GrantPressure >= Options.GrantHigh) return new(true, false, "grant-high");
        if (signals.SpillWriteLatencyMsPerMB >= Options.SpillWriteLatencyHighMsPerMB) return new(true, false, "spill-latency-high");

        var low = signals.CpuUtilization <= Options.CpuLow
            && signals.MemoryLoad <= Options.MemoryLow
            && signals.GrantPressure <= Options.GrantLow
            && signals.SpillWriteLatencyMsPerMB <= Options.SpillWriteLatencyLowMsPerMB
            && signals.QueueDepth == 0;
        return new(false, low, low ? "low" : "deadband");
    }

    private void UpdateCounters(PressureState pressure)
    {
        if (pressure.IsHigh)
        {
            _highSamples++;
            _lowSamples = 0;
        }
        else if (pressure.IsLow)
        {
            _lowSamples++;
            _highSamples = 0;
        }
        else
        {
            _highSamples = 0;
            _lowSamples = 0;
        }
    }

    private AdaptiveDecision RecordLocked(AdaptiveDecisionKind kind, string reason, ResourceSignals signals)
    {
        var decision = new AdaptiveDecision(kind, reason, signals, _timeProvider.GetUtcNow());
        _decisions.Add(decision);
        return decision;
    }

    private readonly record struct PressureState(bool IsHigh, bool IsLow, string HighReason);
}
