using System.Text.Json;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Governance;

/// <summary>Approval state of a Gateway-local resource registration.</summary>
public enum GatewayResourceState
{
    /// <summary>Discovery suggested this resource. It is inert until an on-premises administrator approves it.</summary>
    Proposed,

    /// <summary>An on-premises administrator approved it; it may be mapped to a tenant alias.</summary>
    Approved,

    /// <summary>Withdrawn. New work fails immediately; it is not published to the tenant catalog.</summary>
    Disabled
}

/// <summary>Operation classes a registered resource may serve. Enumerated, never free-form.</summary>
[Flags]
public enum GatewayOperationClass
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4
}

/// <summary>Bounds every operation against a resource must respect. Absent means the Gateway default, never unbounded.</summary>
public sealed record GatewayResourceLimits(
    int MaxConcurrency = 4,
    long MaxRows = 1_000_000,
    long MaxBytes = 1L << 30,
    int TimeoutSeconds = 300);

/// <summary>
/// A resource registered on the on-premises Gateway (SaaS isolation architecture §11.3 step 3).
///
/// <para>This record lives <b>only</b> on the Gateway. It is the one place the physical target and
/// the local credential reference exist, which is what makes the cloud-side binding safe to be
/// empty. <see cref="ToPublishedMetadata"/> produces the bounded, non-secret projection that is
/// allowed to reach the tenant catalog.</para>
/// </summary>
public sealed record GatewayResource(
    string ResourceId,
    string ConnectorType,
    string LocalTarget,
    string LocalCredentialReference,
    GatewayOperationClass AllowedOperations,
    GatewayResourceLimits Limits,
    GatewayResourceState State = GatewayResourceState.Proposed,
    string? DisplayName = null)
{
    /// <summary>
    /// The projection published to the tenant catalog: identity, connector type, what it can do, and
    /// its bounds. Never the target, never the credential reference. A published record is safe to
    /// store cloud-side precisely because it cannot be dialled.
    /// </summary>
    public GatewayPublishedResource ToPublishedMetadata() => new(
        ResourceId, ConnectorType, AllowedOperations, Limits, State, DisplayName);
}

/// <summary>Bounded non-secret resource metadata as the tenant catalog sees it.</summary>
public sealed record GatewayPublishedResource(
    string ResourceId,
    string ConnectorType,
    GatewayOperationClass AllowedOperations,
    GatewayResourceLimits Limits,
    GatewayResourceState State,
    string? DisplayName);

/// <summary>Thrown when a registry operation is refused. Never contains a target or credential.</summary>
public sealed class GatewayResourceException(string message) : Exception(message);

/// <summary>
/// The Gateway-local resource catalog. Discovery may <see cref="ProposeAsync"/>; only an
/// on-premises administrator may <see cref="ApproveAsync"/>. That split is the point of §11.3 step
/// 3 — "Discovery can propose but never approve a resource" — because discovery walks whatever the
/// network happens to answer, and an automatic approval would let the network choose what the SaaS
/// can reach.
/// </summary>
public sealed class GatewayResourceRegistry
{
    private readonly Dictionary<string, GatewayResource> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly string? _persistencePath;

    public GatewayResourceRegistry(string? persistencePath = null)
    {
        if (persistencePath is null) return;
        if (!Path.IsPathFullyQualified(persistencePath))
            throw new ArgumentException("The Gateway resource registry path must be absolute.", nameof(persistencePath));
        _persistencePath = Path.GetFullPath(persistencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_persistencePath)!);
        if (!File.Exists(_persistencePath)) return;
        try
        {
            var protectedJson = File.ReadAllText(_persistencePath);
            var resources = JsonSerializer.Deserialize<List<GatewayResource>>(
                CryptoUtils.Unprotect(protectedJson, "gateway-resource-registry")) ?? [];
            foreach (var resource in resources)
                _resources[resource.ResourceId] = resource;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new GatewayResourceException("The protected Gateway resource registry could not be loaded.");
        }
    }

    /// <summary>Records a discovered resource as <see cref="GatewayResourceState.Proposed"/>. Never approves.</summary>
    public Task<GatewayResource> ProposeAsync(GatewayResource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        EnsureWellFormed(resource);

        var proposed = resource with { State = GatewayResourceState.Proposed };
        lock (_gate)
        {
            // Re-proposing an already-approved resource must not silently reset it to Proposed and
            // must not let discovery edit an approved target underneath the administrator.
            if (_resources.TryGetValue(resource.ResourceId, out var existing)
                && existing.State == GatewayResourceState.Approved)
            {
                throw new GatewayResourceException(
                    $"Resource '{resource.ResourceId}' is already approved; discovery cannot redefine an approved resource.");
            }

            _resources[proposed.ResourceId] = proposed;
            PersistLocked();
        }

        return Task.FromResult(proposed);
    }

    /// <summary>
    /// Approves a proposed resource. The approver is the on-premises administrator; a platform
    /// operator has no path to this method by construction — it exists only on the Gateway.
    /// </summary>
    public Task<GatewayResource> ApproveAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_resources.TryGetValue(resourceId, out var existing))
                throw new GatewayResourceException($"Resource '{resourceId}' is not registered.");
            if (existing.State == GatewayResourceState.Disabled)
                throw new GatewayResourceException($"Resource '{resourceId}' is disabled and cannot be approved.");

            var approved = existing with { State = GatewayResourceState.Approved };
            _resources[resourceId] = approved;
            PersistLocked();
            return Task.FromResult(approved);
        }
    }

    /// <summary>Disables a resource. In-flight policy is the caller's concern; new work stops here.</summary>
    public Task DisableAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_resources.TryGetValue(resourceId, out var existing))
                throw new GatewayResourceException($"Resource '{resourceId}' is not registered.");
            _resources[resourceId] = existing with { State = GatewayResourceState.Disabled };
            PersistLocked();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a resource for execution. Only an approved resource resolves; proposed, disabled, and
    /// unknown all fail closed with the same shape of refusal.
    /// </summary>
    public Task<GatewayResource> ResolveForExecutionAsync(
        string resourceId, GatewayOperationClass operation, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_resources.TryGetValue(resourceId, out var existing)
                || existing.State != GatewayResourceState.Approved)
            {
                throw new GatewayResourceException(
                    $"Resource '{resourceId}' is not available for execution on this Gateway.");
            }

            if (operation == GatewayOperationClass.None || (existing.AllowedOperations & operation) != operation)
            {
                throw new GatewayResourceException(
                    $"Resource '{resourceId}' does not permit the requested operation class.");
            }

            return Task.FromResult(existing);
        }
    }

    /// <summary>
    /// The bounded metadata published to the tenant catalog: approved resources only, stripped of
    /// target and credential. A proposed resource is invisible to the tenant, so an administrator
    /// cannot map an alias to something nobody approved.
    /// </summary>
    public Task<IReadOnlyList<GatewayPublishedResource>> PublishAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<GatewayPublishedResource> published = _resources.Values
                .Where(resource => resource.State == GatewayResourceState.Approved)
                .Select(resource => resource.ToPublishedMetadata())
                .OrderBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult(published);
        }
    }

    public Task<IReadOnlyList<GatewayResource>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<GatewayResource>>(
                _resources.Values.OrderBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private void PersistLocked()
    {
        if (_persistencePath is null) return;
        var protectedJson = CryptoUtils.ProtectMachine(
            JsonSerializer.Serialize(_resources.Values.OrderBy(resource => resource.ResourceId)),
            "gateway-resource-registry");
        var temporary = _persistencePath + ".tmp";
        File.WriteAllText(temporary, protectedJson);
        File.Move(temporary, _persistencePath, true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_persistencePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void EnsureWellFormed(GatewayResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.ResourceId))
            throw new GatewayResourceException("A Gateway resource requires a stable resource ID.");
        if (!resource.ResourceId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new GatewayResourceException(
                "A Gateway resource ID may contain only letters, digits, period, underscore, and hyphen.");
        if (string.IsNullOrWhiteSpace(resource.ConnectorType))
            throw new GatewayResourceException("A Gateway resource requires a connector type.");
        if (string.IsNullOrWhiteSpace(resource.LocalTarget))
            throw new GatewayResourceException("A Gateway resource requires a local target.");
        if (resource.AllowedOperations == GatewayOperationClass.None)
            throw new GatewayResourceException(
                "A Gateway resource must permit at least one operation class; a resource that permits nothing is a configuration error, not a deny rule.");
        if (resource.Limits.MaxConcurrency <= 0 || resource.Limits.MaxRows <= 0
            || resource.Limits.MaxBytes <= 0 || resource.Limits.TimeoutSeconds <= 0)
        {
            throw new GatewayResourceException(
                "Gateway resource limits must all be positive; an absent bound would mean unbounded.");
        }
    }
}
