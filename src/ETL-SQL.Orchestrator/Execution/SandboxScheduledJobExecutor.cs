using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Execution;

public interface ISandboxScheduledJobExecutor
{
    Task<ScriptExecutionResult> ExecuteAsync(
        JobDefinition job,
        long historyId,
        int attempt,
        string sessionId,
        bool resumeFromCheckpoint,
        long queueWaitMs,
        IReadOnlyDictionary<string, string>? variableOverrides,
        CancellationToken cancellationToken);
}

public interface ISandboxTenantContextResolver
{
    TenantContext Resolve(JobDefinition job);
}

public sealed class SandboxTenantContextResolver(string? hostFixedTenantId) : ISandboxTenantContextResolver
{
    private readonly string? _hostFixedTenantId = string.IsNullOrWhiteSpace(hostFixedTenantId)
        ? null
        : TenantId.FromTrustedSource(hostFixedTenantId).Value;

    public TenantContext Resolve(JobDefinition job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(job.TenantId))
            throw new InvalidOperationException("An unbound job cannot enter tenant sandbox execution.");

        var context = _hostFixedTenantId is null
            ? TenantContext.FromVerifiedCredential(job.TenantId)
            : TenantContext.FromHostConfiguration(_hostFixedTenantId);
        context.RequireTenant(job.TenantId);
        return context;
    }
}

public sealed class SandboxScheduledJobExecutorOptions
{
    public required string PolicyVersion { get; init; }
    public required string BindingVersion { get; init; }
}

/// <summary>
/// Scheduler-to-sandbox seam. It converts an immutable tenant-bound job into a content-addressed,
/// server-policy-resolved workload and never exposes runtime, pool, mount, or provider arguments to
/// job metadata.
/// </summary>
public sealed class SandboxScheduledJobExecutor(
    SandboxScheduledJobExecutorOptions options,
    ISandboxTenantContextResolver tenants,
    ISandboxWorkloadPolicyResolver policies,
    IImmutableSandboxArtifactStore artifacts,
    SandboxExecutionCoordinator coordinator,
    ISandboxExecutionProvider provider) : ISandboxScheduledJobExecutor
{
    public async Task<ScriptExecutionResult> ExecuteAsync(
        JobDefinition job,
        long historyId,
        int attempt,
        string sessionId,
        bool resumeFromCheckpoint,
        long queueWaitMs,
        IReadOnlyDictionary<string, string>? variableOverrides,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (attempt <= 0) throw new ArgumentOutOfRangeException(nameof(attempt));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var tenant = tenants.Resolve(job);
        var policy = policies.Resolve(job, tenant);
        var artifact = await artifacts.PutScriptAsync(job.Script, cancellationToken);
        var runId = historyId > 0 ? $"history-{historyId}" : $"run-{Guid.NewGuid():N}";
        var request = new SandboxWorkloadRequest
        {
            Assignment = new SandboxAssignmentIdentity(tenant, runId, $"attempt-{attempt}"),
            ArtifactId = artifact.ArtifactId,
            ArtifactHash = artifact.Hash,
            PolicyVersion = options.PolicyVersion,
            BindingVersion = options.BindingVersion,
            RequiredIsolationTier = policy.RequiredIsolationTier,
            Limits = policy.Limits,
            AdmissionPolicy = policy.AdmissionPolicy,
            SessionId = sessionId,
            ResumeFromCheckpoint = resumeFromCheckpoint,
            CheckpointHandle = resumeFromCheckpoint ? sessionId : null,
            VariableOverrides = variableOverrides ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var outcome = await coordinator.ExecuteAsync(provider, request, cancellationToken);
        if (outcome.Result is not null) return outcome.Result;
        return new ScriptExecutionResult(
            outcome.Status == SandboxTerminalStatus.Succeeded,
            0,
            outcome.SanitizedDiagnostic,
            SessionId: sessionId);
    }
}
