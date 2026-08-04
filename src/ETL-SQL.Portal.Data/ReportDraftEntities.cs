namespace ETL_SQL.Portal.Data;

/// <summary>
/// A proposed change to a report's script, held separately from the live one until it is approved
/// and published.
///
/// <para>Without a draft there is nowhere for an unapproved change to exist: saving writes straight
/// over the running report, so "save" and "publish" are the same act and review can only ever happen
/// after the fact. The draft is what makes the gap between authoring and publishing representable.</para>
///
/// <para>The script text lives here rather than in the artifact store on purpose. A draft is not yet
/// a script — nothing should execute it, nothing should serve it, and it must not appear in a
/// directory listing beside real scripts. Keeping it in the database keeps that distinction
/// structural instead of relying on everyone remembering a naming convention.</para>
/// </summary>
public class ReportScriptDraft : IVersionedEntity
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public Report? Report { get; set; }

    public string ScriptText { get; set; } = "";

    /// <summary>Hash of <see cref="ScriptText"/>, so an approval can name exactly what it approved.</summary>
    public string ScriptHash { get; set; } = "";

    /// <summary>
    /// The live script's hash when editing began. If the live script has moved on since, publishing
    /// this draft would silently discard whatever changed in between.
    /// </summary>
    public string? BaseScriptHash { get; set; }

    /// <summary><c>draft</c>, <c>pending</c>, <c>approved</c>, <c>rejected</c>, <c>published</c>, or <c>superseded</c>.</summary>
    public string Status { get; set; } = DraftStatus;

    public const string DraftStatus = "draft";
    public const string PendingStatus = "pending";
    public const string ApprovedStatus = "approved";
    public const string RejectedStatus = "rejected";
    public const string PublishedStatus = "published";
    public const string SupersededStatus = "superseded";

    public int AuthorUserId { get; set; }
    public string? AuthorUserName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>
    /// Who approved it. Recorded on the draft as well as in the decision trail because the question
    /// "who let this into production" must be answerable from the thing that went to production.
    /// </summary>
    public int? ApprovedByUserId { get; set; }
    public string? ApprovedByUserName { get; set; }

    public long Version { get; set; } = 1;

    public ICollection<ReportScriptDraftDecision> Decisions { get; set; } = [];
}

/// <summary>
/// One reviewer's decision on a draft, kept as an append-only trail.
///
/// <para>Rows are added rather than edited when a decision is superseded: a reviewer who approved
/// and later changed their mind is a different history from one who only ever rejected, and the
/// distinction is exactly what a post-incident review is looking for.</para>
/// </summary>
public class ReportScriptDraftDecision
{
    public int Id { get; set; }
    public int DraftId { get; set; }
    public ReportScriptDraft? Draft { get; set; }

    /// <summary><c>submit</c>, <c>approve</c>, <c>reject</c>, or <c>withdraw</c>.</summary>
    public string Decision { get; set; } = "";

    public string? Reason { get; set; }

    /// <summary>The script hash this decision was made against — an approval names its content.</summary>
    public string? ScriptHash { get; set; }

    public int DecidedByUserId { get; set; }
    public string? DecidedByUserName { get; set; }
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
}
