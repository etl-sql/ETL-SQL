namespace ETL_SQL.Core.Reliability;

/// <summary>
/// Provider-neutral failures which every distributed deployment claim must exercise. Scenario
/// identity and recovery semantics live in Core so a hosting adapter cannot quietly redefine them.
/// </summary>
public enum FaultScenarioId
{
    ProcessOrWorkerLoss,
    LeaseExpiryAndFencingRace,
    DatabaseDisconnect,
    PartialArtifactOperation,
    StorageOutage,
    NetworkPartition,
    DuplicateDelivery,
    ClockSkew,
    DiskExhaustion
}

public enum FaultInjectionPoint
{
    BeforeAuthorityCheck,
    AfterAuthorityCheck,
    BeforeMutation,
    AfterMutationBeforeCommit,
    AfterCommitBeforeAcknowledgement,
    DuringArtifactWrite,
    DuringArtifactPublish,
    DuringDatabaseOperation,
    DuringNetworkOperation,
    DuringCheckpointPersist
}

public enum FaultRecoveryClaim
{
    SafeFailureAndDeliberateRetry,
    NamedCheckpointResume
}

public sealed record FaultScenarioDefinition(
    FaultScenarioId Id,
    string Trigger,
    FaultInjectionPoint InjectionPoint,
    string ExpectedSafeOutcome);

public static class ProviderNeutralFaultScenarios
{
    public static IReadOnlyList<FaultScenarioDefinition> All { get; } =
    [
        new(FaultScenarioId.ProcessOrWorkerLoss, "Terminate the active worker after mutation begins.",
            FaultInjectionPoint.AfterMutationBeforeCommit, "The attempt loses authority and an incomplete result is never published."),
        new(FaultScenarioId.LeaseExpiryAndFencingRace, "Expire the active lease while a replacement owner acquires a newer fence.",
            FaultInjectionPoint.BeforeMutation, "Only the current fence may mutate shared state."),
        new(FaultScenarioId.DatabaseDisconnect, "Disconnect the durable state provider during an operation.",
            FaultInjectionPoint.DuringDatabaseOperation, "The operation is durably committed once or fails visibly."),
        new(FaultScenarioId.PartialArtifactOperation, "Interrupt an artifact write before atomic publication.",
            FaultInjectionPoint.DuringArtifactPublish, "Readers see the prior artifact or the complete replacement, never a partial artifact."),
        new(FaultScenarioId.StorageOutage, "Make shared artifact storage unavailable.",
            FaultInjectionPoint.DuringArtifactWrite, "The attempt fails visibly without publishing a result."),
        new(FaultScenarioId.NetworkPartition, "Partition an active worker from its authority and durable providers.",
            FaultInjectionPoint.DuringNetworkOperation, "The isolated worker cannot retain or reuse mutation authority."),
        new(FaultScenarioId.DuplicateDelivery, "Deliver the same operation identity more than once.",
            FaultInjectionPoint.AfterCommitBeforeAcknowledgement, "At most one committed result exists for the operation identity."),
        new(FaultScenarioId.ClockSkew, "Move a worker clock across the lease-expiry boundary.",
            FaultInjectionPoint.BeforeAuthorityCheck, "Provider time and fencing decide authority; worker wall-clock skew grants none."),
        new(FaultScenarioId.DiskExhaustion, "Exhaust local working storage during a durable write.",
            FaultInjectionPoint.DuringCheckpointPersist, "No incomplete result or checkpoint is advertised as durable.")
    ];

    public static FaultScenarioDefinition Get(FaultScenarioId id) =>
        All.Single(scenario => scenario.Id == id);
}

public sealed record FaultCheckpointContract(bool IsExplicitlyResumable, string? CheckpointName)
{
    public static FaultCheckpointContract NonResumable { get; } = new(false, null);

    public static FaultCheckpointContract Named(string checkpointName)
    {
        if (string.IsNullOrWhiteSpace(checkpointName))
            throw new ArgumentException("A resumable checkpoint must have a name.", nameof(checkpointName));
        return new(true, checkpointName.Trim());
    }
}

public sealed record FaultRunRequest(
    FaultScenarioDefinition Scenario,
    string Provider,
    string DeploymentProfile,
    int Repetition,
    string OperationId,
    FaultCheckpointContract CheckpointContract);

/// <summary>
/// The adapter observation is deliberately provider-neutral. Provider-specific diagnostics belong in
/// <see cref="Diagnostics"/>; the certification invariants do not.
/// </summary>
public sealed record FaultRunObservation
{
    public required int MaximumConcurrentMutationAuthorities { get; init; }
    public required bool StaleAuthorityRejected { get; init; }
    public required int AcceptedDeliveries { get; init; }
    public required int CommittedResults { get; init; }
    public required int VisiblyFailedDeliveries { get; init; }
    public required bool ResultWasSilentlyLost { get; init; }
    public required FaultRecoveryClaim RecoveryClaim { get; init; }
    public string? ResumedCheckpointName { get; init; }
    public required bool DeliberateRetryEligible { get; init; }
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } =
        new Dictionary<string, string>();
}

public interface IFaultInjectionHook
{
    ValueTask HitAsync(FaultInjectionPoint point, CancellationToken cancellationToken = default);
}

public sealed class FaultInjectedException(FaultInjectionPoint point)
    : Exception($"The deterministic fault at '{point}' was activated.")
{
    public FaultInjectionPoint Point { get; } = point;
}

/// <summary>
/// A deterministic, occurrence-addressed local hook. It contains no timing sleeps and is safe to use
/// in focused tests and provider adapters.
/// </summary>
public sealed class DeterministicFaultInjectionHook(
    FaultInjectionPoint target,
    int occurrence = 1) : IFaultInjectionHook
{
    private int _hits;

    public int HitCount => Volatile.Read(ref _hits);
    public bool Activated { get; private set; }

    public ValueTask HitAsync(FaultInjectionPoint point, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (point != target) return ValueTask.CompletedTask;

        var hit = Interlocked.Increment(ref _hits);
        if (hit == occurrence)
        {
            Activated = true;
            throw new FaultInjectedException(point);
        }

        return ValueTask.CompletedTask;
    }
}

public interface IFaultScenarioAdapter
{
    string AdapterKind { get; }
    Task<FaultRunObservation> ExecuteAsync(
        FaultRunRequest request,
        IFaultInjectionHook hook,
        CancellationToken cancellationToken = default);
}

public sealed class LocalFaultScenarioAdapter(
    Func<FaultRunRequest, IFaultInjectionHook, CancellationToken, Task<FaultRunObservation>> execute)
    : IFaultScenarioAdapter
{
    public string AdapterKind => "local";

    public Task<FaultRunObservation> ExecuteAsync(
        FaultRunRequest request,
        IFaultInjectionHook hook,
        CancellationToken cancellationToken = default) => execute(request, hook, cancellationToken);
}

public sealed record FaultInvariantEvidence(
    bool NoSplitBrainMutation,
    bool NoStaleAuthorityReuse,
    bool NoSilentLoss,
    bool NoDuplicateCommittedResult,
    bool RecoveryClaimMatchesCheckpointContract)
{
    public bool Passed => NoSplitBrainMutation && NoStaleAuthorityReuse && NoSilentLoss &&
                          NoDuplicateCommittedResult && RecoveryClaimMatchesCheckpointContract;
}

public sealed record FaultScenarioEvidence(
    FaultRunRequest Request,
    string AdapterKind,
    FaultRunObservation Observation,
    FaultInvariantEvidence Invariants,
    bool FaultActivated);

public sealed record FaultCertificationReport(
    string Schema,
    DateTimeOffset CompletedUtc,
    int Repetitions,
    IReadOnlyList<FaultScenarioEvidence> Runs)
{
    public bool Passed => Runs.Count > 0 && Runs.All(run => run.Invariants.Passed && run.FaultActivated);
}

public static class ProviderNeutralFaultCertificationRunner
{
    public static async Task<FaultCertificationReport> RunAsync(
        IFaultScenarioAdapter adapter,
        string provider,
        string deploymentProfile,
        int repetitions,
        Func<FaultScenarioDefinition, FaultCheckpointContract> checkpointContract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentProfile);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repetitions);
        ArgumentNullException.ThrowIfNull(checkpointContract);

        var evidence = new List<FaultScenarioEvidence>(ProviderNeutralFaultScenarios.All.Count * repetitions);
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            foreach (var scenario in ProviderNeutralFaultScenarios.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = new FaultRunRequest(
                    scenario,
                    provider,
                    deploymentProfile,
                    repetition,
                    $"{deploymentProfile}-{scenario.Id}-{repetition}",
                    checkpointContract(scenario));
                var hook = new DeterministicFaultInjectionHook(scenario.InjectionPoint);
                var observation = await adapter.ExecuteAsync(request, hook, cancellationToken);
                evidence.Add(new FaultScenarioEvidence(
                    request,
                    adapter.AdapterKind,
                    observation,
                    Verify(request, observation),
                    hook.Activated));
            }
        }

        return new FaultCertificationReport(
            "etl-sql.provider-neutral-fault-certification/v1",
            DateTimeOffset.UtcNow,
            repetitions,
            evidence);
    }

    public static FaultInvariantEvidence Verify(FaultRunRequest request, FaultRunObservation observation)
    {
        var recoveryMatches = observation.RecoveryClaim switch
        {
            FaultRecoveryClaim.NamedCheckpointResume =>
                request.CheckpointContract.IsExplicitlyResumable &&
                !string.IsNullOrWhiteSpace(request.CheckpointContract.CheckpointName) &&
                string.Equals(request.CheckpointContract.CheckpointName,
                    observation.ResumedCheckpointName, StringComparison.Ordinal),
            FaultRecoveryClaim.SafeFailureAndDeliberateRetry =>
                observation.DeliberateRetryEligible &&
                string.IsNullOrWhiteSpace(observation.ResumedCheckpointName),
            _ => false
        };

        return new FaultInvariantEvidence(
            observation.MaximumConcurrentMutationAuthorities is >= 0 and <= 1,
            observation.StaleAuthorityRejected,
            observation.AcceptedDeliveries > 0 && observation.CommittedResults >= 0 &&
                observation.VisiblyFailedDeliveries >= 0 && !observation.ResultWasSilentlyLost &&
                observation.AcceptedDeliveries == observation.CommittedResults + observation.VisiblyFailedDeliveries,
            observation.CommittedResults is >= 0 and <= 1,
            recoveryMatches);
    }
}
