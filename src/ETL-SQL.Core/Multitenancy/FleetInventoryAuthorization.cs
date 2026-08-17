namespace ETL_SQL.Core.Multitenancy;

/// <summary>
/// A time-limited, attributed authorization to enumerate the *operational* state of the tenant fleet:
/// which tenants exist, what release each runs, and their capacity assignment.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a <see cref="PlatformAccessGrant"/> with the tenant left off. A grant
/// authorizes acting on one tenant; this authorizes reading control-plane metadata about many and
/// acting on none. Keeping them separate is what stops "I needed to list the fleet" from becoming a
/// way to obtain tenant-scoped authority in bulk — every mutation still needs its own grant naming
/// its own tenant.
/// </para>
/// <para>
/// What it can read is the control plane's own record. It carries no path to tenant data, scripts,
/// results, or credentials, and there is no way to widen it into one.
/// </para>
/// </remarks>
public sealed record FleetInventoryAuthorization
{
    private FleetInventoryAuthorization(
        string operatorPrincipal, string authorizationReference, string reason, DateTimeOffset expiresUtc)
    {
        OperatorPrincipal = operatorPrincipal;
        AuthorizationReference = authorizationReference;
        Reason = reason;
        ExpiresUtc = expiresUtc;
    }

    /// <summary>The platform person or service acting. Never a tenant user.</summary>
    public string OperatorPrincipal { get; }

    /// <summary>The approval this enumeration hangs off — a change record or rollout ticket.</summary>
    public string AuthorizationReference { get; }

    public string Reason { get; }

    public DateTimeOffset ExpiresUtc { get; }

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresUtc;

    /// <summary>
    /// Issues an authorization. Every field is required for the same reason a platform grant requires
    /// them: an unattributed, unexplained, or open-ended one is standing visibility into the customer
    /// population rather than an authorization.
    /// </summary>
    public static FleetInventoryAuthorization Issue(
        string operatorPrincipal,
        string authorizationReference,
        string reason,
        DateTimeOffset expiresUtc,
        DateTimeOffset now)
    {
        Require(operatorPrincipal, nameof(operatorPrincipal),
            "Fleet inventory must name the operator acting.");
        Require(authorizationReference, nameof(authorizationReference),
            "Fleet inventory must name the authorization that permitted it.");
        Require(reason, nameof(reason),
            "Fleet inventory must state why, or it cannot be reviewed after the fact.");
        if (expiresUtc <= now)
            throw new ArgumentException(
                "A fleet inventory authorization must expire in the future.", nameof(expiresUtc));

        return new FleetInventoryAuthorization(
            operatorPrincipal.Trim(), authorizationReference.Trim(), reason.Trim(), expiresUtc);
    }

    private static void Require(string? value, string parameter, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message, parameter);
    }
}
