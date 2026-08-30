namespace ETL_SQL.Portal.Models;

using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Reporting.Authoring;

public record ParseDesignerRequest(string Script);

public record ParseDesignerResponse(DesignerStateDto DesignState, string? Error);

public record AnalyzeDesignerRequest(
    string Script,
    string? Dialect = null,
    string? ConnectionRef = null,
    string? DocumentUri = null);

public record AnalyzeDesignerResponse(IReadOnlyList<AnalysisDiagnostic> Diagnostics);

public record ScriptDagRequest(
    string Script,
    string? DocumentUri = null);

public record CompleteDesignerRequest(
    string Script,
    int Line,
    int Column,
    string? ConnectionRef = null,
    string? DocumentUri = null);

public record CompleteDesignerResponse(IReadOnlyList<DesignerCompletionItem> Items);

public record HoverDesignerRequest(
    string? Word,
    string? Script = null,
    int Line = 0,
    int Column = 0,
    string? DocumentUri = null);

public record HoverDesignerResponse(string? Markdown, string? Kind = null);

public record FormatDesignerRequest(
    string Script,
    string? DocumentUri = null);

/// <param name="Diagnostics">Reasons the script could not be formatted; empty on success.</param>
/// <remarks>
/// Shape-compatible with the desktop host's <c>FormatResponse</c> so Studio consumes one contract.
/// </remarks>
public record FormatDesignerResponse(string Script, IReadOnlyList<FormatDesignerDiagnostic> Diagnostics);

public record FormatDesignerDiagnostic(string Message);

public record DesignerCompletionItem(
    string Label,
    string InsertText,
    string Kind,
    string? Detail = null,
    string? Documentation = null,
    int? StartColumn = null,
    int? EndColumn = null);

public record RunDesignerRequest(
    string Script,
    string? Selection = null,
    string? ConnectionRef = null,
    string? DocumentUri = null);

public record RunDesignerResponse(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    bool Capped,
    long ElapsedMs,
    string Message,
    /// <summary>Hierarchical execution-tree snapshot that drives the editor's Pipeline (DAG) tab.</summary>
    object? Pipeline = null,
    bool ByteCapped = false,
    long BytesReturned = 0);

public record DesignerDataPreviewRequest(
    string SourceKind,
    string? Connection = null,
    string? Table = null,
    string? TempTable = null,
    string? Script = null,
    string? DocumentUri = null,
    string? Dataset = null);

public record DesignerDataPreviewResponse(
    string SourceKind,
    string Source,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    bool Capped,
    bool ByteCapped,
    long BytesReturned,
    long ElapsedMs,
    string Message);

public record SaveDesignerRequest(
    int ReportId,
    string ScriptText,
    string? BaseRevision = null);

public record PreviewDesignerRequest(
    string Script,
    string? Page = null);

public record SaveDesignerResponse(
    long Version,
    string? SourceRevision = null);

public record GenerateDesignerRequest(DesignerStateDto DesignState, string? Script = null);

public record GenerateDesignerResponse(string Script);

public record PatchDesignerRequest(string Script, DesignerStateDto DesignState);

public record PatchDesignerResponse(string Script);

public record ApplyDesignerQueryFiltersRequest(
    string Source,
    List<DesignerQueryFilter> Filters,
    bool AsVisualSource = true);

public record ApplyDesignerQueryFiltersResponse(string Source);

public record BuildDesignerOptionSourceRequest(string Source, string Column);

public record DesignerStateDto(
    List<DesignerPageDto> Pages,
    List<DesignerDatasetDto> Datasets,
    DesignerReportStyleDto? ReportStyle = null,
    // Null means "this client does not edit bookmarks", and existing CREATE BOOKMARK statements are
    // left alone. An empty list is an explicit "no bookmarks" and removes them.
    List<DesignerBookmarkDto>? Bookmarks = null,
    // Null preserves declarations for clients that do not edit parameters. Empty removes them.
    List<DesignerParameterDto>? Parameters = null);

public record DesignerParameterDto(
    string Name,
    string DataType,
    string? InitialValue = null,
    bool IsInput = false,
    bool IsOutput = false,
    bool IsRequired = false,
    bool IsSensitive = false);

public record DesignerBookmarkDto(
    string Id,
    string Name,
    string? Title = null,
    string? Page = null,
    bool IsDefault = false,
    List<DesignerBookmarkParameterDto>? Parameters = null,
    List<DesignerBookmarkStateDto>? State = null);

/// <summary><c>Value</c> is the authored source text (<c>'West'</c>, <c>25</c>), never a coerced string.</summary>
public record DesignerBookmarkParameterDto(string Name, string Value);

public record DesignerBookmarkStateDto(string ObjectName, string Property, bool On);

public record DesignerReportStyleDto(
    string? Theme = null,
    string? Accent = null,
    string? Background = null,
    string? Surface = null,
    string? Text = null);

public record DesignerPageDto(
    string Id,
    string Name,
    string Mode,
    List<DesignerVisualDto> Visuals,
    DesignerPageLayoutDto? PrintLayout = null);

public record DesignerPageLayoutDto(
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

public record DesignerVisualDto(
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

public record DesignerDatasetDto(
    string Id,
    string Name,
    string Query,
    string? RefreshInterval = null,
    string? Ttl = null);

public record ScriptContentRequest(string ScriptText, string? BaseRevision = null);

public record ScriptContentResponse(string ScriptText, long Version = 1, string? SourceRevision = null, bool SourceControlEnabled = false);

public record ScriptSourceControlResponse(string? SourceRevision, bool Committed);
