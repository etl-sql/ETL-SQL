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

/// <summary>
/// One edit to the pipeline canvas, addressed by task label.
/// </summary>
/// <param name="Op">read | add | update | move | nest | connect | disconnect | remove.</param>
/// <param name="Id">The task being edited, or the label of the task being added.</param>
/// <param name="NewId">update only: the new label.</param>
/// <param name="After">add and move: the task this one follows; null means end (add) or start (move).</param>
/// <param name="Edge">
/// connect only: always | onsuccess | onfailure | oncompletion | expression. Anything else is
/// refused rather than quietly downgraded to plain precedence — a canvas that asked for "only on
/// failure" and got "always" would have drawn one pipeline and written another.
/// </param>
/// <param name="Expression">connect only: the author's condition, for an <c>expression</c> edge.</param>
public record PipelineTaskRequest(
    string? Script,
    string? Op,
    string? Id = null,
    string? NewId = null,
    string? Kind = null,
    string? Connection = null,
    string? Body = null,
    string? Source = null,
    string? Target = null,
    string? Condition = null,
    string? Message = null,
    string? Recipient = null,
    string? Sender = null,
    string? Subject = null,
    string? After = null,
    string? Edge = null,
    string? Expression = null,
    string? Variable = null,
    string? Collection = null);

/// <summary>
/// The result of a pipeline edit. <c>Applied</c> false with an <c>Error</c> is an ordinary answer —
/// the script comes back unchanged and the canvas says why, rather than redrawing as if it worked.
/// </summary>
public record PipelineTaskResponse(bool Applied, string Script, string? Error, List<PipelineTaskDto> Tasks);

/// <param name="DependsOn">What this task declares it runs after; several of them is a join.</param>
/// <param name="Guarded">
/// True when the script wraps this task in the <c>BEGIN TRY</c> that records its outcome, because
/// another task's edge asks how it finished.
/// </param>
/// <param name="Container">The container this task sits inside, or null when it is top level.</param>
/// <param name="Variable">Foreach only: the loop variable.</param>
/// <param name="Collection">Foreach only: what it iterates over.</param>
public record PipelineTaskDto(
    string Id, string Kind, string Connection, string Body, int Line,
    List<PipelineDependencyDto> DependsOn, bool Guarded = false,
    string? Container = null, string? Variable = null, string? Collection = null);

/// <param name="Condition">always | onsuccess | onfailure | oncompletion | expression.</param>
public record PipelineDependencyDto(string Id, string Condition, string? Expression = null);

/// <summary>Which task's scope to report. The script is read as the author currently has it.</summary>
public record PipelineScopeRequest(string? Script, string? Id);

/// <summary>
/// Which task to plan a run up to. Planning never executes anything: it returns the slice and what
/// running it would cost, so the canvas can put that in front of the author before anything happens.
/// </summary>
public record PipelineRunPlanRequest(string? Script, string? Id);

public record DesignerStateDto(
    List<DesignerPageDto> Pages,
    List<DesignerDatasetDto> Datasets,
    DesignerReportStyleDto? ReportStyle = null,
    // Null means "this client does not edit bookmarks", and existing CREATE BOOKMARK statements are
    // left alone. An empty list is an explicit "no bookmarks" and removes them.
    List<DesignerBookmarkDto>? Bookmarks = null,
    // Null preserves declarations for clients that do not edit parameters. Empty removes them.
    List<DesignerParameterDto>? Parameters = null,
    // Read-only: reported by parse so a surface can reproduce the script's connection context, and
    // ignored by generate and patch, because no surface edits a connection through design state.
    List<DesignerConnectionDto>? Connections = null);

/// <summary><c>Text</c> is the authored <c>CREATE CONNECTION</c> statement, exactly as written.</summary>
public record DesignerConnectionDto(string Name, string Text);

public record DesignerParameterDto(
    string Name,
    string DataType,
    string? InitialValue = null,
    bool IsInput = false,
    bool IsOutput = false,
    bool IsRequired = false,
    bool IsSensitive = false,
    bool IsBlockScoped = false);

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
    string? ContainerId = null,
    DesignerVisualFormattingDto? Formatting = null);

public record DesignerVisualFormattingDto(
    DesignerTextFormattingDto? Title = null,
    DesignerTextFormattingDto? Subtitle = null,
    Dictionary<string, string>? XAxis = null,
    Dictionary<string, string>? YAxis = null,
    List<string>? Palette = null,
    List<DesignerConditionalFormattingRuleDto>? ConditionalRules = null,
    Dictionary<string, DesignerFieldFormattingDto>? Fields = null);

public record DesignerTextFormattingDto(
    string? Text = null,
    string? Color = null,
    string? Font = null,
    string? Size = null,
    string? Weight = null,
    string? Align = null);

public record DesignerConditionalFormattingRuleDto(
    string Condition,
    string BackgroundColor,
    string? FontColor = null);

public record DesignerFieldFormattingDto(
    string? Format = null,
    string? Align = null,
    string? DisplayName = null,
    bool DataBar = false,
    string? DataBarColor = null,
    string? ColorScaleFrom = null,
    string? ColorScaleTo = null);

public record DesignerDatasetDto(
    string Id,
    string Name,
    string Query,
    string? Ttl = null);

public record ScriptContentRequest(string ScriptText, string? BaseRevision = null);

public record ScriptContentResponse(string ScriptText, long Version = 1, string? SourceRevision = null, bool SourceControlEnabled = false);

public record ScriptSourceControlResponse(string? SourceRevision, bool Committed);
