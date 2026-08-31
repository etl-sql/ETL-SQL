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
    List<DesignerAuthoringBookmark>? Bookmarks = null,
    List<DesignerAuthoringParameter>? Parameters = null);

/// <summary>A report parameter declaration carried as authored source text for lossless patching.</summary>
public sealed record DesignerAuthoringParameter(
    string Name,
    string DataType,
    string? InitialValue = null,
    bool IsInput = false,
    bool IsOutput = false,
    bool IsRequired = false,
    bool IsSensitive = false);

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
    List<DesignerAuthoringVisual> Visuals,
    DesignerAuthoringPageLayout? PrintLayout = null);

public sealed record DesignerAuthoringPageLayout(
    string? PageSize = null,
    string? Orientation = null,
    decimal? MarginTop = null,
    decimal? MarginRight = null,
    decimal? MarginBottom = null,
    decimal? MarginLeft = null,
    string? Units = null,
    string? Overflow = null,
    decimal? CustomWidth = null,
    decimal? CustomHeight = null);

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

/// <summary>
/// A named, cached query. <c>Ttl</c> is how long the cached rows stay valid, carried as the authored
/// duration text ('2h') rather than a parsed span — a designer that round-trips it must not rewrite
/// '120m' into '2h'. Null means the clause is absent, which is not the same as a zero duration.
///
/// <para>There is deliberately no refresh interval here. <c>CREATE DATASET ... REFRESH EVERY</c> is
/// retired and the parser rejects it; scheduled refresh is expressed with <c>CREATE SCHEDULE</c> and
/// <c>CREATE JOB ... FOR REPORT</c>. Emitting it produced a script that would not parse, which the
/// patcher then refused wholesale — so the designer wrote nothing at all.</para>
/// </summary>
public sealed record DesignerAuthoringDataset(
    string Id,
    string Name,
    string Query,
    string? Ttl = null);
