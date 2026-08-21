using System.Collections.Generic;

namespace ETL_SQL.Reporting.Authoring;

/// <summary>
/// Host-neutral report-designer state consumed by the shared script mutation engine.
/// Portal and IDE transport DTOs map into this contract at their boundaries.
/// </summary>
public sealed record DesignerAuthoringState(
    List<DesignerAuthoringPage> Pages,
    List<DesignerAuthoringDataset> Datasets,
    DesignerAuthoringReportStyle? ReportStyle = null);

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
