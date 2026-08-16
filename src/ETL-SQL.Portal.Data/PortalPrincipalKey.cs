using System;

namespace ETL_SQL.Portal.Data;

/// <summary>
/// The stable identifier an authorization grant is written against.
///
/// <para>Opaque by design: nothing parses it, and it encodes neither the tenant nor the kind of
/// principal it belongs to. That is deliberate — a key that carried its tenant would tempt a caller
/// into reading the tenant out of it instead of resolving it, which is the same mistake as trusting
/// a caller-supplied identifier. It exists only to be equal to itself.</para>
///
/// <para>Users and groups share one key space so a grant's principal kind is stated by the grant and
/// never inferred from the key's shape. The values are random rather than sequential, so a key can be
/// minted without coordinating with a database and two deployments can never mint the same one.</para>
/// </summary>
public static class PortalPrincipalKey
{
    /// <summary>Mints a key. Called once, when the principal row is created, and never again.</summary>
    public static string New() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// True when <paramref name="value"/> is a key this build could have minted.
    ///
    /// <para>Used to tell "no key yet" from "a key that does not resolve". The first is a row from
    /// before the column existed and is repairable by backfill; the second is an orphaned grant,
    /// which must fail closed and be reported rather than quietly matching nobody.</para>
    /// </summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: 32 } && Guid.TryParseExact(value, "N", out _);
}
