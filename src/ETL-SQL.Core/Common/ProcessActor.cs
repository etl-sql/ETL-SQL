namespace ETL_SQL.Core.Common;

/// <summary>
/// The OS-reported identity that audit and policy capture attribute an operation to.
/// </summary>
/// <remarks>
/// A hardened sandbox runs tenant code as a numeric uid with no <c>passwd</c> entry, where
/// <see cref="Environment.UserName"/> is legitimately empty. Actor is a required field, so an empty
/// user name would otherwise abort the whole run. Substituting an obviously-unattributed marker
/// keeps the audit record honest without making an unmapped uid unable to execute.
/// </remarks>
public static class ProcessActor
{
    /// <summary>Recorded when the operating system cannot name the account the process runs as.</summary>
    public const string Unmapped = "unmapped-os-user";

    public static string Current => Resolve(Environment.UserName);

    internal static string Resolve(string? osUserName) =>
        string.IsNullOrWhiteSpace(osUserName) ? Unmapped : osUserName;
}
