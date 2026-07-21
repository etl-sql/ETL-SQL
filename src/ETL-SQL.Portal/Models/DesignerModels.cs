namespace ETL_SQL.Portal.Models;

using ETL_SQL.Analysis.Diagnostics;

public record ParseDesignerRequest(string Script);

public record ParseDesignerResponse(DesignerStateDto DesignState, string? Error);

public record AnalyzeDesignerRequest(
    string Script,
    string? Dialect = null,
    string? ConnectionRef = null,
    string? DocumentUri = null);

public record AnalyzeDesignerResponse(IReadOnlyList<AnalysisDiagnostic> Diagnostics);

public record CompleteDesignerRequest(
    string Script,
    int Line,
    int Column,
    string? ConnectionRef = null,
    string? DocumentUri = null);

public record CompleteDesignerResponse(IReadOnlyList<DesignerCompletionItem> Items);

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
    object? Pipeline = null);

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

public record GenerateDesignerRequest(DesignerStateDto DesignState);

public record GenerateDesignerResponse(string Script);

public record DesignerStateDto(
    List<DesignerPageDto> Pages,
    List<DesignerDatasetDto> Datasets);

public record DesignerPageDto(
    string Id,
    string Name,
    string Mode,
    List<DesignerVisualDto> Visuals);

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
    string Query);

public record ScriptContentRequest(string ScriptText, string? BaseRevision = null);

public record ScriptContentResponse(string ScriptText, long Version = 1, string? SourceRevision = null, bool SourceControlEnabled = false);

public record ScriptSourceControlResponse(string? SourceRevision, bool Committed);
