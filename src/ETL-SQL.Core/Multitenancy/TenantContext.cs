namespace ETL_SQL.Core.Multitenancy;

/// <summary>Where a tenant context came from. Recorded so evidence can say, not infer.</summary>
public enum TenantContextOrigin
{
    /// <summary>A deployment serving exactly one tenant; identity comes from host configuration.</summary>
    HostFixed,

    /// <summary>A verified credential — an authenticated token claim or service identity.</summary>
    VerifiedCredential,

    /// <summary>Platform automation acting on a tenant it was authorized for out of band.</summary>
    PlatformAuthorization
}

/// <summary>
/// The server-derived tenant scope for a unit of work
/// (<c>docs/architecture/SaaSTenantIsolation.md</c> §6).
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no way to build one of these from a request. Every constructor path names a
/// server-owned origin, so "the caller told us which tenant" is not expressible rather than merely
/// discouraged. That is the whole invariant of this domain: a caller-supplied tenant, alias, gateway,
/// resource, run, object, or storage identifier must not be able to widen scope.
/// </para>
/// <para>
/// This matters even where a tenant has its own deployment. A dedicated boundary makes cross-tenant
/// reach unlikely, not impossible: provisioning, platform automation, and support tooling all still
/// span tenants, and each is a surface that can be handed an identifier.
/// </para>
/// </remarks>
public sealed record TenantContext
{
    private TenantContext(TenantId tenant, TenantContextOrigin origin)
    {
        Tenant = tenant;
        Origin = origin;
    }

    public TenantId Tenant { get; }

    public TenantContextOrigin Origin { get; }

    /// <summary>A single-tenant deployment: the host configuration is the authority.</summary>
    public static TenantContext FromHostConfiguration(string configuredTenantId) =>
        new(TenantId.FromTrustedSource(configuredTenantId), TenantContextOrigin.HostFixed);

    /// <summary>
    /// A verified credential. The caller supplies the credential, never the tenant — the claim is
    /// read only after the credential's signature and issuer have been checked upstream.
    /// </summary>
    public static TenantContext FromVerifiedCredential(string verifiedTenantClaim) =>
        new(TenantId.FromTrustedSource(verifiedTenantClaim), TenantContextOrigin.VerifiedCredential);

    /// <summary>
    /// Platform operation against one tenant, under a live <see cref="PlatformAccessGrant"/>.
    /// </summary>
    /// <remarks>
    /// Takes a grant rather than a reference string so expiry is checked at the point of use, not at
    /// the point of issue. A grant that was valid when it was written and is not valid now must not
    /// produce a usable context, and a caller that only had to pass a string could never be told so.
    /// The resulting context still carries no tenant-user identity: the operator acts as itself.
    /// </remarks>
    public static TenantContext FromPlatformGrant(PlatformAccessGrant grant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);

        if (grant.IsExpiredAt(now))
        {
            throw new UnauthorizedAccessException(
                $"The platform access grant for tenant '{grant.Tenant.Value}' expired at " +
                $"{grant.ExpiresUtc:O}. Expired authorization is not authorization.");
        }

        return new(grant.Tenant, TenantContextOrigin.PlatformAuthorization) { Grant = grant };
    }

    /// <summary>The grant this context rests on, when it came from platform scope.</summary>
    public PlatformAccessGrant? Grant { get; private init; }

    /// <summary>
    /// Confirms a caller-supplied identifier refers to this tenant, and returns it only when it does.
    /// </summary>
    /// <remarks>
    /// The shape matters. A caller may legitimately name a resource it already holds — a run id, an
    /// alias — and the server must be able to accept that name without letting it *select* the
    /// tenant. So the identifier is checked against the context rather than parsed into one, and a
    /// mismatch is refused rather than silently rescoped.
    /// </remarks>
    public string RequireOwned(string callerSuppliedIdentifier, string identifierKind)
    {
        if (string.IsNullOrWhiteSpace(callerSuppliedIdentifier))
            throw new ArgumentException($"A {identifierKind} is required.", nameof(callerSuppliedIdentifier));

        var expectedPrefix = $"{Tenant.Value}/";
        if (!callerSuppliedIdentifier.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"The {identifierKind} '{callerSuppliedIdentifier}' does not belong to tenant " +
                $"'{Tenant.Value}'. A caller-supplied identifier cannot widen scope.");
        }

        return callerSuppliedIdentifier;
    }

    /// <summary>
    /// Treats a caller-supplied tenant id as an assertion against this server-derived context. It
    /// cannot select or replace the context tenant.
    /// </summary>
    public TenantId RequireTenant(string? callerSuppliedTenantId)
    {
        if (!TenantId.TryParse(callerSuppliedTenantId, out var asserted))
            throw new ArgumentException("A canonical tenant id assertion is required.", nameof(callerSuppliedTenantId));
        if (asserted != Tenant)
            throw new UnauthorizedAccessException(
                $"Caller asserted tenant '{asserted.Value}', but server authority is scoped to " +
                $"'{Tenant.Value}'. A caller-supplied tenant cannot widen scope.");
        return Tenant;
    }

    /// <summary>Rechecks time-limited platform authority immediately before a mutation boundary.</summary>
    public void RequireActivePlatformGrant(DateTimeOffset now)
    {
        if (Origin != TenantContextOrigin.PlatformAuthorization || Grant is null)
            throw new UnauthorizedAccessException("This operation requires attributed platform authorization.");
        if (Grant.IsExpiredAt(now))
            throw new UnauthorizedAccessException(
                $"Platform authorization for tenant '{Tenant.Value}' expired at {Grant.ExpiresUtc:O}.");
    }

    /// <summary>
    /// Derives a tenant-scoped key for a logical resource, so two tenants using the same name or the
    /// same numeric id never collide in a shared store, cache, queue, or path.
    /// </summary>
    public string ScopeKey(string logicalId)
    {
        if (string.IsNullOrWhiteSpace(logicalId))
            throw new ArgumentException("A logical id is required.", nameof(logicalId));

        return $"{Tenant.Value}/{logicalId}";
    }

    /// <summary>
    /// The prefix every key for this tenant starts with, for partitioning an enumeration, scan, or
    /// range read over a shared store.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ScopeKey"/> rather than <c>ScopeKey("")</c>, because an empty logical
    /// id is a caller bug and must keep throwing. The shared-surface contract test found this the
    /// first time an implementation tried to enumerate: without it, the obvious workaround is string
    /// concatenation at each call site, and one of those eventually forgets the delimiter — which is
    /// precisely the <c>acme</c> / <c>acme-evil</c> prefix collision.
    /// </remarks>
    public string ScopePrefix => $"{Tenant.Value}/";
}
