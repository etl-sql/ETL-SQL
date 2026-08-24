using System.Collections.Generic;

namespace ETL_SQL.Reporting.Authoring;

/// <summary>
/// Host-neutral report-designer state consumed by the shared script mutation engine.
/// Portal and IDE transport DTOs map into this contract at their boundaries.
/// </summary>
public sealed record DesignerAuthoringState(
    List<DesignerAuthoringPage> Pages,
    List<DesignerAuthoringDataset> Datasets,
    DesignerAuthoringReportStyle? ReportStyle = null,
    List<DesignerAuthoringBookmark>? Bookmarks = null);

/// <summary>
/// One author bookmark as the designer edits it. Values are carried as the authored source text
/// (<c>'West'</c>, <c>25</c>, <c>TRUE</c>) rather than as strings, so a number never round-trips into a
/// quoted string — the same typed contract the parser and formatter hold.
/// </summary>
public sealed record DesignerAuthoringBookmark(
    string Id,
    string Name,
    string? Title = null,
    string? Page = null,
    bool IsDefault = false,
    List<DesignerAuthoringBookmarkParameter>? Parameters = null,
    List<DesignerAuthoringBookmarkState>? State = null);

/// <summary><c>@Name = &lt;value expression&gt;</c> inside a bookmark's PARAMETERS clause.</summary>
public sealed record DesignerAuthoringBookmarkParameter(string Name, string Value);

/// <summary><c>ObjectName.VISIBLE|COLLAPSED = ON|OFF</c> inside a bookmark's STATE clause.</summary>
public sealed record DesignerAuthoringBookmarkState(string ObjectName, string Property, bool On);

public sealed record DesignerAuthoringReportStyle(
    string? Theme = null,
    string? Accent = null,
    string? Background = null,
    string? Surface = null,
    string? Text = null);

public sealed record DesignerAuthoringPage(
    string Id,
    string Name,
    string Mode,
    List<DesignerAuthoringVisual> Visuals);

public sealed record DesignerAuthoringVisual(
    string Id,
    string Name,
    string Type,
    int GridCol,
    int GridRow,
    int GridColSpan,
    int GridRowSpan,
    string? Title,
    string? Dataset,
    Dictionary<string, string> Mappings,
    Dictionary<string, string> Options,
    string? ContainerId = null);

public sealed record DesignerAuthoringDataset(
    string Id,
    string Name,
    string Query);
