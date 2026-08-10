using ETL_SQL.Core.Common;

namespace ETL_SQL.Core.Multitenancy;

[Flags]
public enum TenantStorageAccess
{
    None = 0,
    Read = 1,
    Write = 2,
    All = Read | Write
}

/// <summary>A server-issued filesystem grant for one tenant run.</summary>
public sealed record TenantStorageGrant
{
    private TenantStorageGrant(string name, string canonicalRoot, TenantStorageAccess access)
    {
        Name = name;
        CanonicalRoot = canonicalRoot;
        Access = access;
    }

    public string Name { get; }
    public string CanonicalRoot { get; }
    public TenantStorageAccess Access { get; }

    internal static TenantStorageGrant FromServerConfiguration(
        string name,
        string root,
        TenantStorageAccess access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (access == TenantStorageAccess.None)
            throw new ArgumentException("A storage grant must allow at least one operation.", nameof(access));

        var canonical = global::ETL_SQL.Services.SecurityService.ResolvePathSymlinks(Path.GetFullPath(root));
        return new TenantStorageGrant(name.Trim(), canonical, access);
    }
}

/// <summary>
/// Immutable, server-derived authority for filesystem and object identifiers used by one tenant
/// execution. Script-selected paths are assertions against this capability; they never select its
/// tenant, run, provider, prefix, or roots.
/// </summary>
public sealed class TenantStorageCapability
{
    private TenantStorageCapability(
        TenantContext tenant,
        string runId,
        IReadOnlyList<TenantStorageGrant> grants)
    {
        Tenant = tenant;
        RunId = runId;
        ObjectPrefix = $"{tenant.Tenant.Value}/{runId}/";
        Grants = grants;
    }

    public TenantContext Tenant { get; }
    public string RunId { get; }
    public string ObjectPrefix { get; }
    public IReadOnlyList<TenantStorageGrant> Grants { get; }

    public static TenantStorageCapability FromServerAuthority(
        TenantContext tenant,
        string runId,
        IEnumerable<(string Name, string Root, TenantStorageAccess Access)> grants)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(grants);
        if (runId.IndexOfAny(['/', '\\']) >= 0 || runId is "." or "..")
            throw new ArgumentException("A storage run identifier must be one canonical segment.", nameof(runId));

        var resolved = grants
            .Select(grant => TenantStorageGrant.FromServerConfiguration(
                grant.Name, grant.Root, grant.Access))
            .ToArray();
        if (resolved.Length == 0)
            throw new ArgumentException("At least one server-owned storage root is required.", nameof(grants));
        if (resolved.Select(grant => grant.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != resolved.Length)
            throw new ArgumentException("Storage grant names must be unique.", nameof(grants));

        return new TenantStorageCapability(tenant, runId.Trim(), resolved);
    }

    public string RequirePath(string canonicalPath, bool? write = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var resolved = global::ETL_SQL.Services.SecurityService.ResolvePathSymlinks(Path.GetFullPath(canonicalPath));
        if (Grants.Any(grant => (write is null
                || grant.Access.HasFlag(write.Value ? TenantStorageAccess.Write : TenantStorageAccess.Read))
            && SafePath.TryResolveWithinRoot(grant.CanonicalRoot, resolved, out _)))
        {
            return resolved;
        }

        throw new global::ETL_SQL.Services.SecurityException(
            $"Tenant storage capability denied {(write is null ? "path" : write.Value ? "write" : "read")} access outside its authorized run roots.");
    }

    public string RequireObjectIdentifier(string callerSuppliedIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerSuppliedIdentifier);
        var normalized = callerSuppliedIdentifier.Replace('\\', '/');
        if (!normalized.StartsWith(ObjectPrefix, StringComparison.Ordinal)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            throw new UnauthorizedAccessException(
                "The object identifier does not belong to the server-issued tenant/run storage prefix.");
        }
        return normalized;
    }

    public bool TryGetGrantRoot(
        string name,
        TenantStorageAccess requiredAccess,
        out string? canonicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var grant = Grants.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
            && candidate.Access.HasFlag(requiredAccess));
        canonicalRoot = grant?.CanonicalRoot;
        return canonicalRoot is not null;
    }

    public string GetGrantRoot(string name, TenantStorageAccess requiredAccess) =>
        TryGetGrantRoot(name, requiredAccess, out var root)
            ? root!
            : throw new global::ETL_SQL.Services.SecurityException(
                $"Tenant storage capability does not grant {requiredAccess} access to '{name}'.");
}

/// <summary>
/// Host-owned factory for run capabilities in a dedicated tenant deployment. Registration of this
/// service is the assertion that the process has fixed tenant authority; caller input is never used
/// to select the tenant or any filesystem root.
/// </summary>
public sealed class TenantStorageHostAuthority
{
    private readonly IReadOnlyList<(string Name, string Root, TenantStorageAccess Access)> _grants;
    private readonly string _scratchRoot;

    private TenantStorageHostAuthority(
        TenantContext tenant,
        string checkpointRoot,
        string scratchRoot,
        IReadOnlyList<(string Name, string Root, TenantStorageAccess Access)> grants)
    {
        Tenant = tenant;
        CheckpointRoot = Path.GetFullPath(checkpointRoot);
        _scratchRoot = Path.GetFullPath(scratchRoot);
        _grants = grants;
    }

    public TenantContext Tenant { get; }
    public string CheckpointRoot { get; }

    public static TenantStorageHostAuthority FromServerConfiguration(
        TenantContext tenant,
        string checkpointRoot,
        string scratchRoot,
        IEnumerable<(string Name, string Root, TenantStorageAccess Access)> grants)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Origin != TenantContextOrigin.HostFixed)
            throw new UnauthorizedAccessException(
                "Dedicated storage host authority must come from immutable host configuration.");
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        ArgumentNullException.ThrowIfNull(grants);

        var configured = grants.ToArray();
        if (configured.Any(grant => string.Equals(grant.Name, "scratch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(grant.Name, "checkpoint", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Scratch and checkpoint grants are created by the host authority.", nameof(grants));
        }

        return FromServerContext(
            tenant,
            checkpointRoot,
            scratchRoot,
            configured);
    }

    public static TenantStorageHostAuthority FromServerContext(
        TenantContext tenant,
        string checkpointRoot,
        string scratchRoot,
        IEnumerable<(string Name, string Root, TenantStorageAccess Access)> grants)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        ArgumentNullException.ThrowIfNull(grants);

        var configured = grants.ToArray();
        if (configured.Any(grant => string.Equals(grant.Name, "scratch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(grant.Name, "checkpoint", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Scratch and checkpoint grants are created by the storage authority.", nameof(grants));
        }

        return new TenantStorageHostAuthority(
            tenant,
            checkpointRoot,
            scratchRoot,
            configured);
    }

    public TenantStorageCapability CreateRunCapability(string runId)
    {
        var runScratchRoot = Path.Combine(_scratchRoot, Tenant.Tenant.Value, runId);
        return TenantStorageCapability.FromServerAuthority(
            Tenant,
            runId,
            _grants.Concat(
            [
                ("scratch", runScratchRoot, TenantStorageAccess.All),
                ("checkpoint", CheckpointRoot, TenantStorageAccess.All)
            ]));
    }
}

/// <summary>Resolves host-fixed storage authority without coupling the engine to host configuration.</summary>
public interface ITenantStorageHostAuthorityProvider
{
    TenantStorageHostAuthority? GetAuthority(TenantContext? persistedContext = null);
}
