namespace ETL_SQL.Core.Multitenancy;

/// <summary>
/// A time-limited, attributed authorization for platform-scoped access to one tenant
/// (<c>SaaSTenantIsolation.md</c> §4, §7).
/// </summary>
/// <remarks>
/// <para>
/// The product has one <c>Admin</c> role today, which in a host-fixed deployment is the tenant's own
/// administrator. This type introduces the principal the SaaS model needs and the product does not
/// yet have: someone who operates the platform and has, by default, authority over <em>no</em>
/// tenant's data.
/// </para>
/// <para>
/// Note what is deliberately absent. There is no way to express "act as this tenant's user". A
/// platform operator acts as itself, with a grant recorded against it — so an audit record names a
/// person and the authorization that permitted them, rather than showing a tenant user apparently
/// doing something they never did. Impersonation is not restricted here; it is unrepresentable.
/// </para>
/// </remarks>
public sealed record PlatformAccessGrant
{
    private PlatformAccessGrant(
        TenantId tenant, string operatorPrincipal, string authorizationReference,
        string reason, DateTimeOffset expiresUtc)
    {
        Tenant = tenant;
        OperatorPrincipal = operatorPrincipal;
        AuthorizationReference = authorizationReference;
        Reason = reason;
        ExpiresUtc = expiresUtc;
    }

    public TenantId Tenant { get; }

    /// <summary>The platform person or service acting. Never a tenant user.</summary>
    public string OperatorPrincipal { get; }

    /// <summary>The approval this access hangs off — a ticket, a change record, a tenant approval.</summary>
    public string AuthorizationReference { get; }

    public string Reason { get; }

    public DateTimeOffset ExpiresUtc { get; }

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresUtc;

    /// <summary>
    /// Issues a grant. Every field is required: an unattributed, unexplained, or open-ended grant is
    /// the shape that turns platform operation into standing access to customer data.
    /// </summary>
    public static PlatformAccessGrant Issue(
        string tenantId,
        string operatorPrincipal,
        string authorizationReference,
        string reason,
        DateTimeOffset expiresUtc,
        DateTimeOffset now)
    {
        Require(operatorPrincipal, nameof(operatorPrincipal),
            "Platform access must name the operator acting. An audit record naming a service instead " +
            "of a person cannot answer who looked at customer data.");
        Require(authorizationReference, nameof(authorizationReference),
            "Platform access must name the authorization that permitted it. Unattributed platform " +
            "access is the impersonation path this boundary exists to stop.");
        Require(reason, nameof(reason),
            "Platform access must state why. A grant with no reason cannot be reviewed after the fact.");

        if (expiresUtc <= now)
        {
            throw new ArgumentException(
                "A platform access grant must expire in the future. Standing access to a tenant is " +
                "not a grant; it is a second tenant administrator nobody approved.", nameof(expiresUtc));
        }

        return new PlatformAccessGrant(
            TenantId.FromTrustedSource(tenantId), operatorPrincipal.Trim(),
            authorizationReference.Trim(), reason.Trim(), expiresUtc);
    }

    private static void Require(string? value, string parameter, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message, parameter);
    }
}
