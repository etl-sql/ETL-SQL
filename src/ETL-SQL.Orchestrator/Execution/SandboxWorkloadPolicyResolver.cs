using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Execution;

public sealed record SandboxExecutionProfile
{
    public required string PoolId { get; init; }
    public required SandboxIsolationTier IsolationTier { get; init; }
    public required SandboxResourceLimits Limits { get; init; }

    internal void Validate()
    {
        var admission = new ResolvedSandboxAdmissionPolicy
        {
            PoolId = PoolId,
            TenantWeight = 1,
            MaxConcurrentAttempts = 1,
            MaxQueuedAttempts = 1
        };
        admission.Validate();
        ArgumentNullException.ThrowIfNull(Limits);
        Limits.Validate();
    }
}

public sealed record SandboxTenantAdmissionPolicy
{
    public required string DefaultProfile { get; init; }
    public required IReadOnlyCollection<string> AllowedProfiles { get; init; }
    public required int Weight { get; init; }
    public required int MaxConcurrentAttempts { get; init; }
    public required int MaxQueuedAttempts { get; init; }
}

/// <summary>
/// Server-owned workload policy catalog. Scheduler metadata may select an entitled profile by name,
/// but cannot supply physical pools, isolation tiers, resource limits, or admission limits.
/// </summary>
public sealed class SandboxWorkloadPolicyCatalog
{
    public required IReadOnlyDictionary<string, SandboxExecutionProfile> Profiles { get; init; }
    public required IReadOnlyDictionary<string, SandboxTenantAdmissionPolicy> Tenants { get; init; }
}

public sealed record ResolvedSandboxWorkloadPolicy(
    string ProfileName,
    SandboxIsolationTier RequiredIsolationTier,
    SandboxResourceLimits Limits,
    ResolvedSandboxAdmissionPolicy AdmissionPolicy);

public interface ISandboxWorkloadPolicyResolver
{
    ResolvedSandboxWorkloadPolicy Resolve(JobDefinition job, TenantContext tenant);
}

public sealed class SandboxWorkloadPolicyResolver : ISandboxWorkloadPolicyResolver
{
    public const string ProfileOption = "SandboxProfile";

    private readonly IReadOnlyDictionary<string, SandboxExecutionProfile> _profiles;
    private readonly IReadOnlyDictionary<string, SandboxTenantAdmissionPolicy> _tenants;

    public SandboxWorkloadPolicyResolver(SandboxWorkloadPolicyCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalog.Profiles);
        ArgumentNullException.ThrowIfNull(catalog.Tenants);
        if (catalog.Profiles.Count == 0)
            throw new ArgumentException("At least one server-owned sandbox execution profile is required.", nameof(catalog));
        if (catalog.Tenants.Count == 0)
            throw new ArgumentException("At least one tenant sandbox admission policy is required.", nameof(catalog));

        _profiles = CopyUnique(catalog.Profiles, "profile");
        _tenants = CopyUnique(catalog.Tenants, "tenant");
        foreach (var profile in _profiles.Values)
        {
            ArgumentNullException.ThrowIfNull(profile);
            profile.Validate();
        }

        foreach (var (tenantId, policy) in _tenants)
            ValidateTenantPolicy(tenantId, policy);
    }

    public ResolvedSandboxWorkloadPolicy Resolve(JobDefinition job, TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(tenant);
        var tenantId = tenant.Tenant.Value;
        if (string.IsNullOrWhiteSpace(job.TenantId))
            throw new InvalidOperationException(
                "A legacy or unbound scheduled job is not eligible for tenant sandbox execution.");
        tenant.RequireTenant(job.TenantId);
        if (!_tenants.TryGetValue(tenantId, out var tenantPolicy))
            throw new InvalidOperationException("The verified tenant has no sandbox admission policy.");

        var requestedProfile = ReadRequestedProfile(job.Options) ?? tenantPolicy.DefaultProfile;
        if (!tenantPolicy.AllowedProfiles.Contains(requestedProfile, StringComparer.Ordinal))
            throw new InvalidOperationException("The requested sandbox profile is not entitled for the verified tenant.");
        if (!_profiles.TryGetValue(requestedProfile, out var profile))
            throw new InvalidOperationException("The requested sandbox profile is not present in the server policy catalog.");

        var admission = new ResolvedSandboxAdmissionPolicy
        {
            PoolId = profile.PoolId,
            TenantWeight = tenantPolicy.Weight,
            MaxConcurrentAttempts = tenantPolicy.MaxConcurrentAttempts,
            MaxQueuedAttempts = tenantPolicy.MaxQueuedAttempts
        };
        admission.Validate();
        return new ResolvedSandboxWorkloadPolicy(
            requestedProfile, profile.IsolationTier, profile.Limits, admission);
    }

    private static string? ReadRequestedProfile(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return null;

        try
        {
            using var document = JsonDocument.Parse(options);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Scheduler job options must be a JSON object.");

            string? value = null;
            var found = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals(ProfileOption, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found)
                    throw new InvalidOperationException("Scheduler job options contain an ambiguous sandbox profile.");
                if (property.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(property.Value.GetString()))
                    throw new InvalidOperationException("The sandbox profile option must be a nonblank string.");
                found = true;
                value = property.Value.GetString();
            }
            return value;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Scheduler job options are not valid JSON.", ex);
        }
    }

    private void ValidateTenantPolicy(string tenantId, SandboxTenantAdmissionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        TenantId.FromTrustedSource(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.DefaultProfile);
        ArgumentNullException.ThrowIfNull(policy.AllowedProfiles);
        if (policy.AllowedProfiles.Count == 0 || policy.AllowedProfiles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Tenant sandbox policy must allow at least one named profile.");
        if (policy.AllowedProfiles.Distinct(StringComparer.Ordinal).Count() != policy.AllowedProfiles.Count)
            throw new ArgumentException("Tenant sandbox profile entitlements must be unique.");
        if (!policy.AllowedProfiles.Contains(policy.DefaultProfile, StringComparer.Ordinal))
            throw new ArgumentException("The tenant default sandbox profile must be entitled.");
        if (policy.AllowedProfiles.Any(profile => !_profiles.ContainsKey(profile)))
            throw new ArgumentException("Tenant sandbox policy references an unknown execution profile.");

        var admission = new ResolvedSandboxAdmissionPolicy
        {
            PoolId = _profiles[policy.DefaultProfile].PoolId,
            TenantWeight = policy.Weight,
            MaxConcurrentAttempts = policy.MaxConcurrentAttempts,
            MaxQueuedAttempts = policy.MaxQueuedAttempts
        };
        admission.Validate();
    }

    private static IReadOnlyDictionary<string, T> CopyUnique<T>(
        IReadOnlyDictionary<string, T> source,
        string kind)
    {
        var copy = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key) || !copy.TryAdd(key, value))
                throw new ArgumentException($"Sandbox {kind} identifiers must be nonblank and case-sensitive unique.");
        }
        return copy;
    }
}
