namespace ETL_SQL.Portal.Services;

/// <summary>
/// Decides whether Portal can read a quarantine target's rows, and says why not when it cannot.
/// <para>
/// The row preview in <c>DataQualityController.GetQuarantineRows</c> runs
/// <c>SELECT * FROM {target}</c> inside a <b>fresh in-process ExecutionSession</b>. That session
/// starts with no connections, no temp tables, and no restored session state — nothing the
/// producing run had. So the only targets it can resolve are ones that need no prior state, and
/// today there are none: a connection-qualified target fails to resolve its connection, and a
/// <c>#temp</c> target silently auto-creates as an empty in-memory table, which is worse — a
/// steward reads "no rows" as "nothing was quarantined".
/// </para>
/// <para>
/// Rather than let the steward click into either outcome, the queue marks the target view-only
/// up front and hands them the statement to run against the target themselves. Restoring the
/// producing job's connections (or resolving the target through the shared connection catalog)
/// is the planned fix — see the Portal section of <c>ROADMAP.md</c>. This class is the seam that
/// change plugs into: everything else asks it, so making a target readable means teaching this
/// one method about the new capability.
/// </para>
/// </summary>
public static class QuarantineTargetReadability
{
    public sealed record Verdict(bool Readable, string? Reason);

    private static readonly Verdict Unknown = new(
        false,
        "Portal cannot read this target. The row editor runs inside the Portal process, which has "
        + "no access to the tables or connections the job used.");

    public static Verdict Describe(string? quarantineTarget)
    {
        var target = quarantineTarget?.Trim();
        if (string.IsNullOrEmpty(target)) return Unknown;

        // Session-local scratch tables. The manifest outlives the run; the table does not.
        if (target[0] is '#' or '&')
        {
            return new Verdict(
                false,
                $"'{target}' is a session-local temp table. It stopped existing when the producing "
                + "run ended, so Portal has no rows to show — quarantine into a durable table if "
                + "you need to review rows after the run.");
        }

        // Connection-qualified: the first segment names a connection the producing script created.
        int dot = target.IndexOf('.');
        if (dot > 0)
        {
            return new Verdict(
                false,
                $"Portal cannot open connection '{target[..dot]}'. The row editor runs inside the "
                + "Portal process, which has no access to the connections the job used.");
        }

        return new Verdict(
            false,
            $"Portal cannot resolve '{target}'. The row editor runs inside the Portal process, "
            + "which has no access to the tables or connections the job used.");
    }

    /// <summary>
    /// The statement a steward can run against the target themselves — from the CLI, the VS Code
    /// extension, or any session that does have the connection. Offered in place of the row editor
    /// so a view-only target still leads somewhere.
    /// </summary>
    public static string BuildReviewStatement(string quarantineTarget) =>
        $"SELECT * FROM {quarantineTarget} WHERE {ETL_SQL.Core.Quality.DataQualityColumns.Status} = "
        + $"'{ETL_SQL.Core.Quality.DataQualityColumns.QuarantinedStatus}';";
}
