namespace ETL_SQL.Core.Reliability;

public enum CanaryJourneyId
{
    ExternalHealth,
    Report,
    Job,
    Gateway,
    Export,
    Notification
}

public enum CanaryFailureKind
{
    None,
    EtlSqlCorrectness,
    EtlSqlAvailability,
    EtlSqlLatency,
    SyntheticDependency,
    IsolationViolation,
    CredentialUnavailable
}

public enum CanaryFaultKind
{
    None,
    EtlSqlCorrectnessFailure,
    EtlSqlAvailabilityRegression,
    EtlSqlLatencyRegression,
    SyntheticDependencyOutage
}

public sealed record CanarySlo(
    decimal AvailabilityPercent,
    TimeSpan MaximumLatency,
    TimeSpan EvaluationWindow)
{
    public void Validate()
    {
        if (AvailabilityPercent <= 0 || AvailabilityPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(AvailabilityPercent));
        if (MaximumLatency <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaximumLatency));
        if (EvaluationWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(EvaluationWindow));
    }
}

public sealed record CanaryJourneyDefinition(
    CanaryJourneyId Id,
    CanarySlo Slo,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> FailureDomains,
    string EtlSqlAlertRoute,
    string DependencyAlertRoute);

public sealed record CanaryIsolationBoundary(
    string TenantId,
    string IdentityId,
    string ResourceNamespace,
    string QuotaPool,
    decimal MaximumMonthlyCostUsd,
    bool CustomerNetworkAccessAllowed,
    bool CustomerCapacityAllowed)
{
    public void Validate()
    {
        RequireSynthetic(TenantId, nameof(TenantId));
        RequireSynthetic(IdentityId, nameof(IdentityId));
        RequireSynthetic(ResourceNamespace, nameof(ResourceNamespace));
        RequireSynthetic(QuotaPool, nameof(QuotaPool));
        if (MaximumMonthlyCostUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumMonthlyCostUsd));
        if (CustomerNetworkAccessAllowed || CustomerCapacityAllowed)
            throw new ArgumentException("Production canaries cannot access customer networks or capacity.");
    }

    private static void RequireSynthetic(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("synthetic-", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Canary isolation identifiers must use the 'synthetic-' prefix.", parameterName);
    }
}

public sealed record CanaryCredential(
    string CredentialId,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    bool Revoked = false);

public sealed record ProductionCanaryPlan(
    string Environment,
    CanaryIsolationBoundary Isolation,
    IReadOnlyList<CanaryJourneyDefinition> Journeys,
    TimeSpan CredentialRotationInterval,
    TimeSpan MaximumCredentialLifetime)
{
    private static readonly CanaryJourneyId[] RequiredOrder =
    [
        CanaryJourneyId.ExternalHealth,
        CanaryJourneyId.Report,
        CanaryJourneyId.Job,
        CanaryJourneyId.Gateway,
        CanaryJourneyId.Export,
        CanaryJourneyId.Notification
    ];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Environment))
            throw new ArgumentException("A hosted environment is required.", nameof(Environment));
        Isolation.Validate();
        if (CredentialRotationInterval <= TimeSpan.Zero || MaximumCredentialLifetime <= TimeSpan.Zero ||
            CredentialRotationInterval >= MaximumCredentialLifetime)
            throw new ArgumentException("Credentials must rotate before their maximum lifetime.");
        if (!Journeys.Select(journey => journey.Id).SequenceEqual(RequiredOrder))
            throw new ArgumentException("Canary journeys must contain the complete ordered journey catalog.", nameof(Journeys));

        foreach (var journey in Journeys)
        {
            journey.Slo.Validate();
            RequireValues(journey.Regions, journey.Id, "region");
            RequireValues(journey.FailureDomains, journey.Id, "failure domain");
            if (string.IsNullOrWhiteSpace(journey.EtlSqlAlertRoute) ||
                string.IsNullOrWhiteSpace(journey.DependencyAlertRoute) ||
                string.Equals(journey.EtlSqlAlertRoute, journey.DependencyAlertRoute, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Journey '{journey.Id}' requires distinct ETL-SQL and dependency alert routes.");
        }
    }

    private static void RequireValues(IReadOnlyList<string> values, CanaryJourneyId id, string label)
    {
        if (values.Count == 0 || values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new ArgumentException($"Journey '{id}' requires unique non-empty {label} values.");
    }
}

public sealed record CanaryProvisioningObservation
{
    public required string TenantId { get; init; }
    public required string IdentityId { get; init; }
    public required string ResourceNamespace { get; init; }
    public required string QuotaPool { get; init; }
    public required decimal EnforcedMonthlyCostLimitUsd { get; init; }
    public required int CustomerResourceGrants { get; init; }
    public required int CustomerNetworkRoutes { get; init; }
    public required bool UsesDedicatedCapacity { get; init; }
}

public interface ICanaryResourceProvisioner
{
    string ProvisionerKind { get; }
    Task<CanaryProvisioningObservation> ProvisionAsync(
        CanaryIsolationBoundary boundary,
        CancellationToken cancellationToken = default);
}

public sealed record CanaryProvisioningEvidence(
    string Schema,
    DateTimeOffset CompletedUtc,
    string ProvisionerKind,
    CanaryIsolationBoundary Expected,
    CanaryProvisioningObservation Observed,
    bool Passed);

public static class CanaryProvisioningCertification
{
    public static async Task<CanaryProvisioningEvidence> ProvisionAndVerifyAsync(
        CanaryIsolationBoundary boundary,
        ICanaryResourceProvisioner provisioner,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(provisioner);
        boundary.Validate();
        var observed = await provisioner.ProvisionAsync(boundary, cancellationToken);
        var passed = string.Equals(observed.TenantId, boundary.TenantId, StringComparison.Ordinal) &&
            string.Equals(observed.IdentityId, boundary.IdentityId, StringComparison.Ordinal) &&
            string.Equals(observed.ResourceNamespace, boundary.ResourceNamespace, StringComparison.Ordinal) &&
            string.Equals(observed.QuotaPool, boundary.QuotaPool, StringComparison.Ordinal) &&
            observed.EnforcedMonthlyCostLimitUsd > 0 &&
            observed.EnforcedMonthlyCostLimitUsd <= boundary.MaximumMonthlyCostUsd &&
            observed.CustomerResourceGrants == 0 && observed.CustomerNetworkRoutes == 0 &&
            observed.UsesDedicatedCapacity;
        return new CanaryProvisioningEvidence(
            "etl-sql.production-canary-provisioning/v1",
            now ?? DateTimeOffset.UtcNow,
            provisioner.ProvisionerKind,
            boundary,
            observed,
            passed);
    }
}

public sealed record CanaryExecutionRequest(
    CanaryJourneyDefinition Journey,
    CanaryIsolationBoundary Isolation,
    CanaryCredential Credential,
    string Region,
    string FailureDomain,
    CanaryFaultKind Fault);

public sealed record CanaryExecutionObservation
{
    public required bool CorrectResult { get; init; }
    public required int SuccessfulSamples { get; init; }
    public required int TotalSamples { get; init; }
    public required TimeSpan Latency { get; init; }
    public required bool SyntheticDependencyHealthy { get; init; }
    public required IReadOnlyList<string> AccessedTenantIds { get; init; }
    public required bool CustomerDataAccessed { get; init; }
    public required bool CustomerSystemsAccessed { get; init; }
    public required bool CustomerCapacityConsumed { get; init; }
    public required decimal AttributedMonthlyCostUsd { get; init; }
    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } =
        new Dictionary<string, string>();
}

public interface IProductionCanaryExecutor
{
    string ExecutorKind { get; }
    Task<CanaryExecutionObservation> ExecuteAsync(
        CanaryExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CanaryAlert(
    CanaryJourneyId Journey,
    string Region,
    string FailureDomain,
    CanaryFailureKind FailureKind,
    string Route,
    CanaryFaultKind InjectedFault);

public sealed record CanaryAlertDelivery(string AlertId, string Route, bool Delivered);

public interface ICanaryAlertSink
{
    Task<CanaryAlertDelivery> PublishAsync(
        CanaryAlert alert,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Hosts bind each journey to its real external probe. The complete catalog is required at
/// construction so a deployment cannot silently certify only the easy paths.
/// </summary>
public sealed class ConfiguredProductionCanaryExecutor : IProductionCanaryExecutor
{
    private readonly IReadOnlyDictionary<CanaryJourneyId,
        Func<CanaryExecutionRequest, CancellationToken, Task<CanaryExecutionObservation>>> _handlers;

    public ConfiguredProductionCanaryExecutor(
        string executorKind,
        IReadOnlyDictionary<CanaryJourneyId,
            Func<CanaryExecutionRequest, CancellationToken, Task<CanaryExecutionObservation>>> handlers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executorKind);
        ArgumentNullException.ThrowIfNull(handlers);
        var required = Enum.GetValues<CanaryJourneyId>();
        var missing = required.Where(id => !handlers.ContainsKey(id)).ToArray();
        var unknown = handlers.Keys.Where(id => !required.Contains(id)).ToArray();
        if (missing.Length > 0 || unknown.Length > 0 || handlers.Count != required.Length)
            throw new ArgumentException("The executor must bind exactly one handler for every canary journey.", nameof(handlers));
        ExecutorKind = executorKind;
        _handlers = handlers;
    }

    public string ExecutorKind { get; }

    public Task<CanaryExecutionObservation> ExecuteAsync(
        CanaryExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _handlers[request.Journey.Id](request, cancellationToken);
    }
}

public sealed record CanaryJourneyEvidence(
    CanaryExecutionRequest Request,
    CanaryExecutionObservation Observation,
    CanaryFailureKind FailureKind,
    string? AlertRoute,
    bool SloSatisfied,
    bool IsolationSatisfied)
{
    public CanaryAlertDelivery? AlertDelivery { get; init; }

    public bool Passed => IsolationSatisfied && Request.Fault switch
    {
        CanaryFaultKind.None => SloSatisfied && FailureKind == CanaryFailureKind.None &&
            AlertRoute is null && AlertDelivery is null,
        CanaryFaultKind.EtlSqlCorrectnessFailure => FailureKind == CanaryFailureKind.EtlSqlCorrectness &&
            DeliveredTo(Request.Journey.EtlSqlAlertRoute),
        CanaryFaultKind.EtlSqlAvailabilityRegression => FailureKind == CanaryFailureKind.EtlSqlAvailability &&
            DeliveredTo(Request.Journey.EtlSqlAlertRoute),
        CanaryFaultKind.EtlSqlLatencyRegression => FailureKind == CanaryFailureKind.EtlSqlLatency &&
            DeliveredTo(Request.Journey.EtlSqlAlertRoute),
        CanaryFaultKind.SyntheticDependencyOutage => FailureKind == CanaryFailureKind.SyntheticDependency &&
            DeliveredTo(Request.Journey.DependencyAlertRoute),
        _ => false
    };

    private bool DeliveredTo(string route) =>
        AlertRoute == route && AlertDelivery is { Delivered: true, AlertId.Length: > 0 } &&
        AlertDelivery.Route == route;
}

public sealed record ProductionCanaryReport(
    string Schema,
    DateTimeOffset CompletedUtc,
    string ExecutorKind,
    IReadOnlyList<CanaryJourneyEvidence> Runs)
{
    public bool Passed => Runs.Count > 0 && Runs.All(run => run.Passed);
}

public static class ProductionCanaryRunner
{
    public static async Task<ProductionCanaryReport> RunAsync(
        ProductionCanaryPlan plan,
        CanaryCredential credential,
        IProductionCanaryExecutor executor,
        ICanaryAlertSink alertSink,
        IReadOnlyList<CanaryFaultKind>? drills = null,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(alertSink);
        plan.Validate();
        var clock = now ?? DateTimeOffset.UtcNow;
        ValidateCredential(plan, credential, clock);
        var selectedDrills = drills ??
            [CanaryFaultKind.EtlSqlCorrectnessFailure, CanaryFaultKind.EtlSqlAvailabilityRegression,
                CanaryFaultKind.EtlSqlLatencyRegression, CanaryFaultKind.SyntheticDependencyOutage];
        if (selectedDrills.Any(fault => fault == CanaryFaultKind.None))
            throw new ArgumentException("Fault drills cannot contain the normal-run marker.", nameof(drills));

        var evidence = new List<CanaryJourneyEvidence>();
        foreach (var journey in plan.Journeys)
        {
            foreach (var region in journey.Regions)
            {
                foreach (var failureDomain in journey.FailureDomains)
                {
                    evidence.Add(await ExecuteOneAsync(plan, credential, executor, alertSink, journey, region,
                        failureDomain, CanaryFaultKind.None, cancellationToken));
                    foreach (var fault in selectedDrills)
                        evidence.Add(await ExecuteOneAsync(plan, credential, executor, alertSink, journey, region,
                            failureDomain, fault, cancellationToken));
                }
            }
        }

        return new ProductionCanaryReport(
            "etl-sql.production-canary-evidence/v1",
            clock,
            executor.ExecutorKind,
            evidence);
    }

    public static CanaryJourneyEvidence Evaluate(
        CanaryExecutionRequest request,
        CanaryExecutionObservation observation)
    {
        var tenantIsolated = observation.AccessedTenantIds.Count > 0 &&
            observation.AccessedTenantIds.All(tenant =>
                string.Equals(tenant, request.Isolation.TenantId, StringComparison.Ordinal));
        var validSamples = observation.TotalSamples > 0 && observation.SuccessfulSamples >= 0 &&
            observation.SuccessfulSamples <= observation.TotalSamples;
        var availability = validSamples
            ? 100m * observation.SuccessfulSamples / observation.TotalSamples
            : 0m;
        var availabilitySatisfied = validSamples && availability >= request.Journey.Slo.AvailabilityPercent;
        var isolationSatisfied = tenantIsolated && !observation.CustomerDataAccessed &&
            !observation.CustomerSystemsAccessed && !observation.CustomerCapacityConsumed &&
            observation.AttributedMonthlyCostUsd >= 0 &&
            observation.AttributedMonthlyCostUsd <= request.Isolation.MaximumMonthlyCostUsd;
        var sloSatisfied = observation.CorrectResult && availabilitySatisfied &&
            observation.SyntheticDependencyHealthy && observation.Latency <= request.Journey.Slo.MaximumLatency;

        CanaryFailureKind failure;
        if (!isolationSatisfied) failure = CanaryFailureKind.IsolationViolation;
        else if (!observation.SyntheticDependencyHealthy) failure = CanaryFailureKind.SyntheticDependency;
        else if (!observation.CorrectResult) failure = CanaryFailureKind.EtlSqlCorrectness;
        else if (!availabilitySatisfied) failure = CanaryFailureKind.EtlSqlAvailability;
        else if (observation.Latency > request.Journey.Slo.MaximumLatency) failure = CanaryFailureKind.EtlSqlLatency;
        else failure = CanaryFailureKind.None;

        var route = failure switch
        {
            CanaryFailureKind.SyntheticDependency => request.Journey.DependencyAlertRoute,
            CanaryFailureKind.EtlSqlCorrectness or CanaryFailureKind.EtlSqlAvailability or CanaryFailureKind.EtlSqlLatency or
                CanaryFailureKind.IsolationViolation or CanaryFailureKind.CredentialUnavailable =>
                request.Journey.EtlSqlAlertRoute,
            _ => null
        };
        return new CanaryJourneyEvidence(request, observation, failure, route, sloSatisfied, isolationSatisfied);
    }

    private static async Task<CanaryJourneyEvidence> ExecuteOneAsync(
        ProductionCanaryPlan plan,
        CanaryCredential credential,
        IProductionCanaryExecutor executor,
        ICanaryAlertSink alertSink,
        CanaryJourneyDefinition journey,
        string region,
        string failureDomain,
        CanaryFaultKind fault,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new CanaryExecutionRequest(journey, plan.Isolation, credential, region, failureDomain, fault);
        var observation = await executor.ExecuteAsync(request, cancellationToken);
        var evaluated = Evaluate(request, observation);
        if (evaluated.AlertRoute is null) return evaluated;
        var alert = new CanaryAlert(
            journey.Id, region, failureDomain, evaluated.FailureKind, evaluated.AlertRoute, fault);
        var delivery = await alertSink.PublishAsync(alert, cancellationToken);
        return evaluated with { AlertDelivery = delivery };
    }

    private static void ValidateCredential(
        ProductionCanaryPlan plan,
        CanaryCredential credential,
        DateTimeOffset now)
    {
        if (credential.Revoked || credential.IssuedUtc > now || credential.ExpiresUtc <= now)
            throw new InvalidOperationException("The canary credential is unavailable, revoked, or expired.");
        if (credential.ExpiresUtc - credential.IssuedUtc > plan.MaximumCredentialLifetime)
            throw new InvalidOperationException("The canary credential exceeds the maximum lifetime.");
    }
}

public interface ICanaryCredentialAuthority
{
    Task<CanaryCredential> IssueAsync(
        string identityId,
        DateTimeOffset now,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
    Task RevokeAsync(string credentialId, CancellationToken cancellationToken = default);
}

public sealed record CanaryCredentialRotationEvidence(
    CanaryCredential Previous,
    CanaryCredential Current,
    bool PreviousRevoked,
    string Reason,
    DateTimeOffset CompletedUtc);

public sealed record CanaryCredentialLifecycleReport(
    string Schema,
    DateTimeOffset CompletedUtc,
    CanaryCredentialRotationEvidence ScheduledRotation,
    CanaryCredentialRotationEvidence CompromiseResponse)
{
    public bool Passed => ScheduledRotation.PreviousRevoked && CompromiseResponse.PreviousRevoked &&
        ScheduledRotation.Reason == "scheduled" && CompromiseResponse.Reason == "compromise" &&
        ScheduledRotation.Previous.CredentialId != ScheduledRotation.Current.CredentialId &&
        CompromiseResponse.Previous.CredentialId != CompromiseResponse.Current.CredentialId;
}

public sealed class CanaryCredentialLifecycle(ICanaryCredentialAuthority authority)
{
    public async Task<CanaryCredentialRotationEvidence?> RotateIfDueAsync(
        ProductionCanaryPlan plan,
        CanaryCredential current,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        plan.Validate();
        if (!current.Revoked && now - current.IssuedUtc < plan.CredentialRotationInterval)
            return null;
        return await ReplaceAsync(plan, current, now, current.Revoked ? "revoked" : "scheduled", cancellationToken);
    }

    public Task<CanaryCredentialRotationEvidence> RespondToCompromiseAsync(
        ProductionCanaryPlan plan,
        CanaryCredential compromised,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        ReplaceAsync(plan, compromised, now, "compromise", cancellationToken);

    private async Task<CanaryCredentialRotationEvidence> ReplaceAsync(
        ProductionCanaryPlan plan,
        CanaryCredential previous,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken)
    {
        await authority.RevokeAsync(previous.CredentialId, cancellationToken);
        var current = await authority.IssueAsync(
            plan.Isolation.IdentityId, now, plan.MaximumCredentialLifetime, cancellationToken);
        if (string.Equals(previous.CredentialId, current.CredentialId, StringComparison.Ordinal) ||
            current.Revoked || current.IssuedUtc != now || current.ExpiresUtc - current.IssuedUtc > plan.MaximumCredentialLifetime)
            throw new InvalidOperationException("The credential authority returned an invalid replacement credential.");
        return new CanaryCredentialRotationEvidence(previous, current, true, reason, now);
    }
}
