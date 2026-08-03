using ETL_SQL.Core.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Decides whether Portal can read a quarantine target's rows, and says why not when it cannot.
///
/// <para>
/// The row preview runs <c>SELECT * FROM {target}</c> inside a fresh in-process
/// <c>ExecutionSession</c> that restores nothing from the producing run. So a target is readable
/// only when the Portal can open its connection through a <b>governed</b> path: the shared
/// connection catalog, resolved as <c>SHARED:alias</c>, where policy, secret resolution, and
/// redaction all apply unchanged. Everything else stays view-only and keeps its existing reason,
/// including a session-local temp table, which stopped existing when the run ended — a steward must
/// never read "no rows" as "nothing was quarantined".
/// </para>
///
/// <para><b>Authorization decision.</b> A readable target requires <em>both</em> data-quality steward
/// access (which gates the feature) and the caller's own grant on the shared connection (which gates
/// the data). Steward access alone is deliberately not enough: quarantined rows are raw source rows,
/// carrying whatever the source carried, and the connection ACL exists precisely to say who may read
/// that. Letting one capability stand in for a grant is the same failure as treating authorship as
/// permission — an authority that accumulates implicitly and cannot be revoked where it was granted.
/// The cost is a steward who must also be granted the connection; the alternative cost is a role
/// that silently reads every connection that has ever produced a capture.</para>
/// </summary>
public static class QuarantineTargetReadability
{
    /// <param name="ConnectionAlias">Set only when readable, and only from the manifest.</param>
    public sealed record Verdict(
        bool Readable, string? Reason, string? ConnectionAlias = null, string? ConnectorType = null);

    private static readonly Verdict Unknown = new(
        false,
        "Portal cannot read this target. The row editor runs inside the Portal process, which has "
        + "no access to the tables or connections the job used.");

    /// <param name="previewEnabled">
    /// <c>Portal:DataQuality:AllowConnectionPreview</c>. Default off, so upgrading does not silently
    /// start opening production connections from the web tier.
    /// </param>
    /// <param name="callerUsableAliases">
    /// Shared-connection aliases this caller may use. Null means "not resolved", which is treated as
    /// no access rather than full access.
    /// </param>
    public static Verdict Describe(
        string? quarantineTarget,
        QuarantineReplayManifest? manifest,
        bool previewEnabled,
        IReadOnlyCollection<string>? callerUsableAliases)
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

        int dot = target.IndexOf('.');
        if (dot <= 0)
        {
            return new Verdict(
                false,
                $"Portal cannot resolve '{target}'. The row editor runs inside the Portal process, "
                + "which has no access to the tables or connections the job used.");
        }

        var alias = target[..dot];

        // A manifest written before provenance existed says nothing about its target, and absent is
        // not the same as catalog-backed. Classifying it view-only keeps old captures safe.
        // The connector type is required too, not merely nice to have: reopening the alias means
        // emitting a typed CREATE CONNECTION, and there is no way to write one without it. Partial
        // provenance is unknown provenance.
        if (manifest?.TargetIsCatalogBacked is not true
            || string.IsNullOrWhiteSpace(manifest.TargetConnectionAlias)
            || string.IsNullOrWhiteSpace(manifest.TargetConnectorType))
        {
            return new Verdict(
                false,
                $"Portal cannot open connection '{alias}'. This capture has no record of a governed "
                + "shared connection behind its target, so there is no path the Portal can reopen it "
                + "through.");
        }

        // The alias is the manifest's, never the request's: a caller must not be able to name a
        // connection and have the Portal open it. When the two disagree the capture contradicts
        // itself, and both readings are wrong — opening the recorded alias reads a connection the
        // target does not name, and trusting the target's prefix lets a string choose the
        // connection. An inconsistent record is not evidence, so it is refused.
        if (!manifest.TargetConnectionAlias!.Equals(alias, StringComparison.OrdinalIgnoreCase))
        {
            return new Verdict(
                false,
                $"This capture is inconsistent: its target names '{alias}' but its recorded "
                + $"connection is '{manifest.TargetConnectionAlias}'. Portal will not guess which "
                + "one to open. Re-run the job to write a fresh capture.");
        }

        alias = manifest.TargetConnectionAlias!;

        // The alias and connector type are interpolated into a CREATE CONNECTION statement, so they
        // are checked as identifiers before they get there. They reach us from job state — durable,
        // engine-written, but still a stored blob — and a value that only happens to be safe because
        // of where it came from is one refactor away from not being.
        if (!IsIdentifier(alias) || !IsIdentifier(manifest.TargetConnectorType!))
        {
            return new Verdict(
                false,
                $"This capture records a connection Portal will not open: '{alias}' or its connector "
                + "type is not a plain identifier.");
        }

        if (!previewEnabled)
        {
            return new Verdict(
                false,
                $"Connection preview is disabled. '{alias}' is catalog-backed and could be read, but "
                + "Portal:DataQuality:AllowConnectionPreview is off.");
        }

        if (callerUsableAliases is null
            || !callerUsableAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
        {
            // Steward access gates the feature; the connection grant gates the data. A missing
            // alias, a disabled entry, and an ungranted one deliberately share this one wording:
            // the catalog does not disclose the existence of connections a caller cannot use, and
            // separating the cases here would leak exactly that.
            return new Verdict(
                false,
                $"Portal cannot open shared connection '{alias}' for you — it is not a usable entry "
                + "in the shared connection catalog, or you have no grant on it. Quarantined rows "
                + "are raw source rows, so reading them needs the same authority as using the "
                + "connection they came from, not only data-quality steward access.");
        }

        return new Verdict(true, null, alias, manifest.TargetConnectorType);
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0
        && value.Length <= 128
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>
    /// The statement a steward can run against the target themselves — from the CLI, the VS Code
    /// extension, or any session that does have the connection. Offered in place of the row editor
    /// so a view-only target still leads somewhere.
    /// </summary>
    public static string BuildReviewStatement(string quarantineTarget) =>
        $"SELECT * FROM {quarantineTarget} WHERE {ETL_SQL.Core.Quality.DataQualityColumns.Status} = "
        + $"'{ETL_SQL.Core.Quality.DataQualityColumns.QuarantinedStatus}';";
}
