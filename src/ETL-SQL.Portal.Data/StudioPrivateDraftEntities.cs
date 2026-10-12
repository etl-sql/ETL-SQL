namespace ETL_SQL.Portal.Data;

/// <summary>
/// One owner's saved, unpublished Studio work. It is not a catalog document: it has no folder, no
/// grants and no catalog report ID, and nothing lists, serves or executes it. Publishing creates the
/// catalog document and removes the draft.
///
/// <para>Private to its owner inside the owner's tenant. Every query for one carries both, so another
/// principal's draft answers exactly as a missing one does.</para>
/// </summary>
public class StudioPrivateDraft
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "portal-host";
    public int OwnerUserId { get; set; }
    public PortalUser Owner { get; set; } = null!;

    public string Name { get; set; } = "";

    /// <summary>Fixed at creation: it decides which catalog document publishing creates.</summary>
    public CatalogDocumentKind Kind { get; set; } = CatalogDocumentKind.Report;

    public string Content { get; set; } = "";
    public string ContentHash { get; set; } = "";

    /// <summary>Starts at 1 and rises by one per save. A save names the version it started from.</summary>
    public long Version { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The last save. Retention counts from here, not from unsaved edits.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Edits made since the last save and not saved yet, kept so a crash or an expired sign-in loses
    /// nothing. Null when there are none. They are not a version and are never published as one.
    /// </summary>
    public string? UnsavedContent { get; set; }

    /// <summary>The saved version the unsaved edits started from.</summary>
    public long? UnsavedBaseVersion { get; set; }

    public DateTime? UnsavedUpdatedAt { get; set; }

    public ICollection<StudioPrivateDraftRevision> Revisions { get; set; } = [];
}

/// <summary>One saved text of a private draft. Only the newest few are kept.</summary>
public class StudioPrivateDraftRevision
{
    public int Id { get; set; }
    public int DraftId { get; set; }
    public StudioPrivateDraft Draft { get; set; } = null!;
    public long Version { get; set; }
    public string Content { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
