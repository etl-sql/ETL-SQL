using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Services;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using DesignerQueryFilterService = ETL_SQL.Reporting.Authoring.DesignerQueryFilterService;
using ScriptDagProjectionService = ETL_SQL.Portal.Services.ScriptDagProjectionService;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/designer")]
[Authorize(Roles = "Admin,Publisher")]
[RequirePortalModule("Designer")]
[RequireStudioCapability(StudioCapabilities.StudioAccess, StudioDeploymentMode.CatalogOnly, StudioDeploymentMode.SourceControlled)]
public class DesignerController : ControllerBase
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> DesignerGates = new();
    private readonly PortalDesignerSchemaService? _schemaService;
    private readonly PortalDesignerRunService? _runService;
    private readonly PortalDesignerPreviewService? _previewService;
    private readonly PortalDesignerDataPreviewService? _dataPreviewService;
    private readonly ReportScriptSaveService? _scriptSave;
    private readonly ILanguageService? _languageService;
    private readonly DesignerAnalysisService _analysisService;
    private readonly DesignerScriptGenerationService _scriptGenerationService;
    private readonly DesignerScriptPatcher _scriptPatcher;
    private readonly PortalConfig _portalConfig;
    private readonly PortalConnectionCatalogService? _connectionCatalog;
    private readonly IMetadataManager? _metadata;
    private readonly DesignerSnapshotService? _snapshots;
    private readonly ScriptDagProjectionService _scriptDag;
    private readonly PipelineTaskAuthoringService _pipelineTasks = new();
    private readonly ScriptScopeService _pipelineScope = new();
    private readonly PipelineRunPlanService _pipelineRunPlans = new();
    private readonly DesignerQueryFilterService _queryFilters = new();
    private readonly LanguageHoverService? _hoverService;

    public DesignerController(
        PortalDesignerSchemaService? schemaService = null,
        PortalDesignerRunService? runService = null,
        ReportScriptSaveService? scriptSave = null,
        ILanguageService? languageService = null,
        DesignerAnalysisService? analysisService = null,
        DesignerScriptGenerationService? scriptGenerationService = null,
        DesignerScriptPatcher? scriptPatcher = null,
        PortalConfig? portalConfig = null,
        PortalDesignerPreviewService? previewService = null,
        PortalConnectionCatalogService? connectionCatalog = null,
        IMetadataManager? metadata = null,
        DesignerSnapshotService? snapshots = null,
        ScriptDagProjectionService? scriptDag = null,
        PortalDesignerDataPreviewService? dataPreviewService = null,
        ILanguageHelpRegistry? languageHelp = null,
        IFunctionRegistry? functionRegistry = null)
    {
        _schemaService = schemaService;
        _runService = runService;
        _previewService = previewService;
        _dataPreviewService = dataPreviewService;
        _scriptSave = scriptSave;
        _languageService = languageService;
        _analysisService = analysisService ?? new DesignerAnalysisService();
        _scriptGenerationService = scriptGenerationService ?? new DesignerScriptGenerationService();
        _scriptPatcher = scriptPatcher ?? new DesignerScriptPatcher(_scriptGenerationService);
        _portalConfig = portalConfig ?? new PortalConfig();
        _connectionCatalog = connectionCatalog;
        _metadata = metadata;
        _snapshots = snapshots;
        _scriptDag = scriptDag ?? new ScriptDagProjectionService();
        _hoverService = languageHelp is not null && functionRegistry is not null
            ? new LanguageHoverService(languageHelp, functionRegistry)
            : null;
    }

    // ── GET /api/session/metadata ─────────────────────────────────────────────
    // Feeds the editor's schema and session explorers. Connections are ACL-filtered so the
    // explorer never reveals a connection the caller cannot use; temp tables come from the
    // metadata the analyze pass registered for this document.

    [HttpGet("/api/session/metadata")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> SessionMetadata([FromQuery] string? documentUri, CancellationToken cancellationToken)
    {
        var connections = _connectionCatalog is null
            ? []
            : await _connectionCatalog.ListUsableAliasesAsync(
                PortalDesignerSchemaService.BuildIdentity(User), cancellationToken);

        var tempTables = new List<object>();
        if (_metadata is not null && !string.IsNullOrWhiteSpace(documentUri))
        {
            foreach (var name in await _metadata.GetTempTablesAsync(documentUri))
            {
                // Temp-table lookups are keyed by document; the connection name is ignored.
                var columns = (await _metadata.GetColumnDetailsAsync(string.Empty, name, documentUri))
                    .Select(c => new { name = c.Name, type = c.DataType })
                    .ToList();
                tempTables.Add(new { name, columns });
            }
        }

        return Ok(new { connections, variables = Array.Empty<object>(), tempTables });
    }

    // ── POST /api/script/dag and /api/designer/dag ───────────────────────────

    [HttpPost("/api/script/dag")]
    [HttpPost("dag")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult ScriptDag([FromBody] ScriptDagRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            return Ok(_scriptDag.Project(req.Script));
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/pipeline-task ─────────────────────────────────────
    // The editable half of the pipeline canvas. The canvas never assembles ETL-SQL itself: it asks
    // for an edit by task label, and this returns either the new script or the reason it was
    // refused. Refusals are ordinary answers here — a duplicate label, a GOTO target, a script that
    // does not parse — so the canvas can say what happened instead of appearing to do nothing.

    [HttpPost("pipeline-task")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult PipelineTask([FromBody] PipelineTaskRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            var script = req.Script ?? string.Empty;
            var result = (req.Op ?? string.Empty).ToLowerInvariant() switch
            {
                "add" => _pipelineTasks.Add(script, new PipelineTaskDraft(
                    req.Id ?? string.Empty,
                    ParseTaskKind(req.Kind),
                    req.Connection,
                    req.Body,
                    req.Source,
                    req.Target,
                    req.Condition,
                    req.Message,
                    req.Recipient,
                    req.Sender,
                    req.Subject,
                    req.After,
                    req.Variable,
                    req.Collection)),
                "update" => _pipelineTasks.Update(script, req.Id ?? string.Empty, req.NewId, req.Connection, req.Body, req.Variable, req.Collection),
                "move" => _pipelineTasks.Move(script, req.Id ?? string.Empty, req.After),
                // `after` names the container to move into; null moves the task back out of the one
                // it is in, which is why this is a separate operation from a reorder.
                "nest" => _pipelineTasks.Nest(script, req.Id ?? string.Empty, req.After),
                // Read as "this task runs after that one": `id` is the dependent, `after` the
                // dependency, the same way the tag reads in the script.
                "connect" => ParseEdgeCondition(req.Edge) is { } edge
                    ? _pipelineTasks.Connect(script, req.After ?? string.Empty, req.Id ?? string.Empty, edge, req.Expression)
                    : PipelineEditResult.Refused(script, $"Unknown edge condition '{req.Edge}'."),
                "disconnect" => _pipelineTasks.Disconnect(script, req.After ?? string.Empty, req.Id ?? string.Empty),
                "remove" => _pipelineTasks.Remove(script, req.Id ?? string.Empty),
                "read" => PipelineEditResult.Ok(script),
                _ => PipelineEditResult.Refused(script, $"Unknown pipeline task operation '{req.Op}'."),
            };

            return Ok(new PipelineTaskResponse(
                result.Applied,
                result.Script,
                result.Error,
                _pipelineTasks.Read(result.Script)
                    .Select(task => new PipelineTaskDto(
                        task.Id, task.Kind.ToString().ToLowerInvariant(), task.Connection, task.Body, task.Line,
                        task.DependsOn
                            .Select(dependency => new PipelineDependencyDto(
                                dependency.Id,
                                dependency.Condition.ToString().ToLowerInvariant(),
                                dependency.Expression))
                            .ToList(),
                        task.Guarded,
                        task.Container,
                        task.Variable,
                        task.Collection))
                    .ToList()));
        }
        finally
        {
            gate?.Release();
        }
    }

    /// <summary>
    /// The task kind a request names. An unknown kind falls back to an execution task rather than
    /// throwing: the palette is the only caller, and a 500 for a typo in a client string would be a
    /// worse answer than the refusal the service already gives for a draft it cannot complete.
    /// </summary>
    private static PipelineTaskKind ParseTaskKind(string? kind) =>
        Enum.TryParse<PipelineTaskKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : PipelineTaskKind.Execution;

    /// <summary>
    /// The edge condition a connect request names, or null when it names one that does not exist.
    ///
    /// <para>Unlike a task kind, this does not fall back. The condition decides which control flow
    /// gets written into the author's file, so a request the server does not understand has to be
    /// refused out loud rather than silently written as plain precedence.</para>
    /// </summary>
    private static PipelineEdgeCondition? ParseEdgeCondition(string? edge) =>
        string.IsNullOrWhiteSpace(edge) ? PipelineEdgeCondition.Always
        : Enum.TryParse<PipelineEdgeCondition>(edge, ignoreCase: true, out var parsed) ? parsed
        : null;

    // ── POST /api/designer/pipeline-scope ────────────────────────────────────
    // What a selected task can see from where it sits. Positional, not script-wide: a variable
    // declared below a task is not one it can read, and a `#temp` created below it does not exist
    // yet, so a flat list of every name in the file would tell the author they can use things that
    // are not there — wrong only at run time, which is the most expensive place to find out.

    [HttpPost("pipeline-scope")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult PipelineScope([FromBody] PipelineScopeRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            var scope = _pipelineScope.At(req.Script, req.Id);
            return Ok(new
            {
                resolved = scope.Resolved,
                error = scope.Error,
                variables = scope.Variables,
                tempTables = scope.TempTables,
            });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/pipeline-run-plan ─────────────────────────────────
    // What running to a selected task would execute, and what that would cost. This route runs
    // nothing: it returns the slice and the writes that slice would perform, so the canvas can show
    // them and get an answer before handing the slice to /api/designer/run like any other script.
    //
    // Splitting the plan from the run is what makes the confirmation trustworthy. A route that both
    // asked and executed would have to be trusted to ask; this one cannot execute at all, and the
    // route that does execute is the ordinary one, still behind the same policy and the same gate.

    [HttpPost("pipeline-run-plan")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult PipelineRunPlan([FromBody] PipelineRunPlanRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            var plan = _pipelineRunPlans.To(req.Script, req.Id);
            return Ok(new
            {
                resolved = plan.Resolved,
                error = plan.Error,
                script = plan.Script,
                included = plan.Included,
                skipped = plan.Skipped,
                effects = plan.Effects.Select(effect => new
                {
                    taskId = effect.TaskId,
                    action = effect.Action,
                    target = effect.Target,
                    line = effect.Line,
                }),
            });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/parse ──────────────────────────────────────────────

    [HttpPost("parse")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult Parse([FromBody] ParseDesignerRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            return Ok(_analysisService.Parse(req.Script, MaxAstStatements));
        }
        catch (DesignerAstLimitExceededException ex)
        {
            return AstLimitExceeded(ex);
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/analyze ───────────────────────────────────────────

    [HttpPost("analyze")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeDesignerRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            return Ok(await _analysisService.AnalyzeAsync(req.Script, req.DocumentUri, MaxAstStatements, HttpContext?.RequestServices));
        }
        catch (DesignerAstLimitExceededException ex)
        {
            return AstLimitExceeded(ex);
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── GET /api/designer/schema ─────────────────────────────────────────────

    [HttpGet("schema")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> Schema([FromQuery] string connection, CancellationToken cancellationToken)
    {
        if (_schemaService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer schema service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            return Ok(await _schemaService.GetSchemaAsync(connection, User, null, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Connection access denied." });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Connection not found." });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/complete ──────────────────────────────────────────

    [HttpPost("complete")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> Complete([FromBody] CompleteDesignerRequest req, CancellationToken cancellationToken)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;
        if (_languageService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer completion service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        string documentUri;
        try
        {
            if (!string.IsNullOrWhiteSpace(req.ConnectionRef))
            {
                if (_schemaService is null)
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer schema service is not configured." });

                try
                {
                    var connectionRef = PortalDesignerSchemaService.NormalizeConnectionRef(req.ConnectionRef!);
                    documentUri = PortalDesignerSchemaService.ResolveDocumentUri(User, connectionRef, req.DocumentUri);
                    await _schemaService.GetSchemaAsync(connectionRef, User, documentUri, cancellationToken);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { error = ex.Message });
                }
                catch (UnauthorizedAccessException)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { error = "Connection access denied." });
                }
                catch (KeyNotFoundException)
                {
                    return NotFound(new { error = "Connection not found." });
                }
            }
            else
            {
                documentUri = PortalDesignerSchemaService.ResolveDocumentUri(User, "adhoc", req.DocumentUri);
            }

            var (scriptBefore, prefix) = GetCompletionPosition(req.Script ?? string.Empty, req.Line, req.Column);
            var suggestions = await _languageService.GetSuggestionsAsync(new SuggestionContext
            {
                Prefix = prefix,
                FullScript = req.Script ?? string.Empty,
                ScriptBefore = scriptBefore,
                DocumentUri = documentUri
            });

            var suggestionList = suggestions.Take(100).ToList();
            var items = new List<DesignerCompletionItem>();

            // Snippets lead: a `$trigger` match is an explicit request for that template, so burying
            // it under keyword suggestions would make the library undiscoverable in the GUI editors.
            items.AddRange(SnippetCompletionSource.GetMatches(scriptBefore, prefix)
                .Select(snippet => new DesignerCompletionItem(
                    snippet.Trigger,
                    snippet.TuiBody,
                    "snippet",
                    snippet.Label,
                    snippet.Description,
                    Math.Max(0, req.Column - prefix.Length),
                    req.Column)));
            if (prefix == "*")
            {
                var columnExpansion = suggestionList
                    .Where(s => string.Equals(s.Type.ToString(), "Column", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Text)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (columnExpansion.Count > 0)
                {
                    items.Add(new DesignerCompletionItem(
                        "Expand * to columns",
                        string.Join(", ", columnExpansion),
                        "snippet",
                        "Column expansion",
                        "Replace * with explicit column names.",
                        Math.Max(0, req.Column - 1),
                        req.Column));
                }
            }

            items.AddRange(suggestionList
                .Take(100)
                .Select(s => new DesignerCompletionItem(
                    s.Text,
                    s.Text,
                    s.Type.ToString().ToLowerInvariant(),
                    s.Type.ToString(),
                    s.Documentation)));

            return Ok(new CompleteDesignerResponse(items));
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/hover ─────────────────────────────────────────────
    // Serves editor hover documentation from the embedded language help corpus. Studio on the
    // desktop reaches the same lookup through the Workstation Editor's /api/hover; both delegate to
    // the shared LanguageHoverService so the two hosts cannot drift apart.

    [HttpPost("hover")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult Hover([FromBody] HoverDesignerRequest req)
    {
        if (_hoverService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Language help is not configured." });

        if (ValidateTextLimit(req.Word, "word", MaxHoverWordCharacters) is { } limitResult)
            return limitResult;

        var (markdown, kind) = _hoverService.Lookup(req.Word);
        return Ok(new HoverDesignerResponse(markdown, kind));
    }

    // ── POST /api/designer/format ────────────────────────────────────────────
    // The Portal has no workspace on disk, so there is no .etlsql-formatter.json to honour here;
    // formatting uses engine defaults. The desktop host keeps its workspace-aware variant.

    [HttpPost("format")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult Format([FromBody] FormatDesignerRequest req)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        var script = req.Script ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
            return Ok(new FormatDesignerResponse(script, []));

        try
        {
            return Ok(new FormatDesignerResponse(SqlFormatter.Format(script, new FormatterOptions()), []));
        }
        catch (Exception ex)
        {
            // Formatting a script that does not parse is an ordinary outcome, not a server fault:
            // hand back the original text plus the reason so the editor can leave the buffer alone.
            return Ok(new FormatDesignerResponse(script, [new FormatDesignerDiagnostic(ex.Message)]));
        }
    }

    // ── POST /api/designer/run ───────────────────────────────────────────────

    [HttpPost("run")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptRun)]
    public async Task<IActionResult> Run([FromBody] RunDesignerRequest req, CancellationToken cancellationToken)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } scriptLimit)
            return scriptLimit;
        if (ValidateTextLimit(req.Selection, "selection", MaxSelectionCharacters) is { } selectionLimit)
            return selectionLimit;
        if (_runService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer run service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            return Ok(await _runService.RunAsync(req, User, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Connection access denied." });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Connection not found." });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { error = "Designer run exceeded the 15 second timeout." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/preview ────────────────────────────────────────────

    [HttpPost("preview")]
    [Authorize(Roles = "Admin,Publisher")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> Preview([FromBody] PreviewDesignerRequest req, CancellationToken cancellationToken)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } scriptLimit)
            return scriptLimit;
        if (_previewService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer preview service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            var manifest = await _previewService.BuildPreviewAsync(req.Script, req.Page, User, cancellationToken);
            return this.BrowserManifest(manifest);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Preview access denied." });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { error = "Preview exceeded the 30 second timeout." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/data-preview ──────────────────────────────────────

    [HttpPost("data-preview")]
    [HttpPost("data-sample")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> DataPreview(
        [FromBody] DesignerDataPreviewRequest req,
        CancellationToken cancellationToken)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } scriptLimit)
            return scriptLimit;
        if (_dataPreviewService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer data-preview service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            return Ok(await _dataPreviewService.PreviewAsync(req, User, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Connection access denied." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = SecretRedactor.Redact(ex.Message) });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout,
                new { error = $"Data preview exceeded the {Math.Max(1, DesignerLimits.MaxDataPreviewSeconds)} second timeout." });
        }
        catch (OperationCanceledException)
        {
            return new StatusCodeResult(499);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/preview/pdf ────────────────────────────────────────

    [HttpPost("preview/pdf")]
    [Authorize(Roles = "Admin,Publisher")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public async Task<IActionResult> PreviewPdf([FromBody] PreviewDesignerRequest req, CancellationToken cancellationToken)
    {
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } scriptLimit)
            return scriptLimit;
        if (_previewService is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer preview service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            var manifest = await _previewService.BuildPreviewAsync(req.Script, req.Page, User, cancellationToken);
            var exporter = new ETL_SQL.Reporting.ReportPdfExporter();
            var pdfBytes = await exporter.ExportAsync(manifest, new ETL_SQL.Reporting.PdfExportOptions
            {
                Mode = ETL_SQL.Reporting.PdfExportMode.Static,
                Host = null
            }, cancellationToken);

            return File(pdfBytes, "application/pdf", "preview.pdf");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Preview access denied." });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { error = "Preview exceeded the 30 second timeout." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"PDF generation failed: {ex.Message}" });
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/save ──────────────────────────────────────────────

    [HttpPost("save")]
    [Authorize(Roles = "Admin,Publisher")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave)]
    public async Task<IActionResult> Save([FromBody] SaveDesignerRequest req, CancellationToken cancellationToken)
    {
        if (ValidateTextLimit(req.ScriptText, "scriptText", MaxScriptCharacters) is { } limitResult)
            return limitResult;
        if (_scriptSave is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Designer save service is not configured." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();

        try
        {
            var expectedVersion = OptimisticConcurrency.ReadExpectedVersion(Request);
            var result = await _scriptSave.SaveAsync(
                req.ReportId,
                req.ScriptText,
                expectedVersion,
                User,
                CurrentUserId,
                req.BaseRevision,
                cancellationToken);

            return result.Status switch
            {
                ReportScriptSaveStatus.Saved => SavedScriptResponse(result),
                ReportScriptSaveStatus.NotFound => NotFound(),
                ReportScriptSaveStatus.Forbidden => Forbid(),
                ReportScriptSaveStatus.MissingVersion => OptimisticConcurrency.MissingVersion(this),
                ReportScriptSaveStatus.Conflict => Conflict(new
                {
                    error = "The resource changed after it was read. Refresh it and retry.",
                    current = new { id = result.Current!.Id, version = result.Current.Version }
                }),
                _ => StatusCode(500, new { error = "Unknown save status." })
            };
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/lease ──────────────────────────────────────────────

    [HttpPost("lease")]
    [Authorize(Roles = "Admin,Publisher")]
    [EnableRateLimiting("designer")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave)]
    public async Task<IActionResult> AcquireLease([FromBody] LeaseDesignerRequest req, CancellationToken cancellationToken)
    {
        var db = HttpContext.RequestServices.GetRequiredService<ETL_SQL.Portal.Data.PortalDbContext>();
        var folderPermissions = HttpContext.RequestServices.GetRequiredService<FolderPermissionService>();
        var catalogScope = HttpContext.RequestServices.GetRequiredService<PortalTenantCatalogScope>();

        var report = await catalogScope.Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == req.ReportId && !r.IsDeleted, cancellationToken);
        if (report is null) return NotFound(new { error = "Report not found." });
        if (!(await folderPermissions.GetEffectiveReportPermissionAsync(report, User))
            .AtLeast(ETL_SQL.Portal.Data.FolderPermission.Author))
            return Forbid();

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(5); // Hard expiry in 5 mins
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var userName = User.Identity?.Name ?? $"user-{userId}";
        var mayForce = req.Force && User.IsInRole("Admin");

        // Lease columns are deliberately updated outside the Report concurrency token. A renewal is
        // collaboration metadata, not report content, and must not make this editor's next If-Match
        // save conflict with itself. The predicate makes acquisition/renewal atomic across nodes.
        var updated = await catalogScope.Reports
            .Where(r => r.Id == req.ReportId && !r.IsDeleted &&
                (mayForce || r.EditSessionUserId == userId || r.EditSessionExpiresAtUtc == null || r.EditSessionExpiresAtUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.EditSessionUserId, userId)
                .SetProperty(r => r.EditSessionUserName, userName)
                .SetProperty(r => r.EditSessionExpiresAtUtc, expiresAt), cancellationToken);
        if (updated == 0)
        {
            var holder = await catalogScope.Reports.AsNoTracking()
                .Where(r => r.Id == req.ReportId)
                .Select(r => new { r.EditSessionUserName, r.EditSessionExpiresAtUtc })
                .SingleAsync(cancellationToken);
            return Conflict(new
            {
                error = "Another user is currently editing this report.",
                owner = holder.EditSessionUserName,
                expiresAt = holder.EditSessionExpiresAtUtc
            });
        }

        await db.ReportScriptDrafts
            .Where(d => d.ReportId == req.ReportId &&
                (d.Status == "draft" || d.Status == "pending" || d.Status == "rejected"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.EditSessionUserId, userId)
                .SetProperty(d => d.EditSessionUserName, userName)
                .SetProperty(d => d.EditSessionExpiresAtUtc, expiresAt), cancellationToken);

        return Ok(new { acquired = true, owner = userName, expiresAt });
    }

    [HttpDelete("lease/{reportId:int}")]
    [Authorize(Roles = "Admin,Publisher")]
    [RequireStudioCapability(StudioCapabilities.ScriptSave)]
    public async Task<IActionResult> ReleaseLease(int reportId, CancellationToken cancellationToken)
    {
        var db = HttpContext.RequestServices.GetRequiredService<ETL_SQL.Portal.Data.PortalDbContext>();
        var catalogScope = HttpContext.RequestServices.GetRequiredService<PortalTenantCatalogScope>();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var report = await catalogScope.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);
        if (report is null)
            return NoContent();

        await catalogScope.Reports.Where(r => r.Id == reportId && r.EditSessionUserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.EditSessionUserId, (int?)null)
                .SetProperty(r => r.EditSessionUserName, (string?)null)
                .SetProperty(r => r.EditSessionExpiresAtUtc, (DateTime?)null), cancellationToken);
        await db.ReportScriptDrafts.Where(d => d.ReportId == reportId && d.EditSessionUserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.EditSessionUserId, (int?)null)
                .SetProperty(d => d.EditSessionUserName, (string?)null)
                .SetProperty(d => d.EditSessionExpiresAtUtc, (DateTime?)null), cancellationToken);
        return NoContent();
    }

    public record LeaseDesignerRequest(int ReportId, bool Force = false);

    // ── POST /api/designer/generate ───────────────────────────────────────────

    [HttpPost("generate")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult Generate([FromBody] GenerateDesignerRequest req)
    {
        if (ValidateDesignerState(req.DesignState) is { } stateLimit)
            return stateLimit;
        if (req.DesignState.Pages == null || req.DesignState.Pages.Count == 0)
            return BadRequest(new { Error = "Design state must contain at least one page." });

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();
        try
        {
            var script = !string.IsNullOrWhiteSpace(req.Script)
                ? _scriptPatcher.Patch(req.Script, req.DesignState)
                : _scriptGenerationService.Generate(req.DesignState);

            if (ValidateTextLimit(script, "generated script", MaxGeneratedScriptCharacters) is { } generatedLimit)
                return generatedLimit;
            return Ok(new GenerateDesignerResponse(script));
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/patch ──────────────────────────────────────────────

    [HttpPost("patch")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult Patch([FromBody] PatchDesignerRequest req)
    {
        if (ValidateDesignerState(req.DesignState) is { } stateLimit)
            return stateLimit;
        if (ValidateTextLimit(req.Script, "script", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        if (!TryEnterDesignerGate(out var gate))
            return DesignerBusy();
        try
        {
            var script = _scriptPatcher.Patch(req.Script, req.DesignState);
            if (ValidateTextLimit(script, "patched script", MaxGeneratedScriptCharacters) is { } generatedLimit)
                return generatedLimit;
            return Ok(new PatchDesignerResponse(script));
        }
        finally
        {
            gate?.Release();
        }
    }

    // ── POST /api/designer/query-filter ───────────────────────────────────────

    [HttpPost("query-filter")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult ApplyQueryFilters([FromBody] ApplyDesignerQueryFiltersRequest req)
    {
        if (ValidateTextLimit(req.Source, "query source", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        try
        {
            return Ok(new ApplyDesignerQueryFiltersResponse(_queryFilters.Apply(req.Source, req.Filters, req.AsVisualSource)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("option-source")]
    [RequireStudioCapability(StudioCapabilities.ScriptPreview)]
    public IActionResult BuildOptionSource([FromBody] BuildDesignerOptionSourceRequest req)
    {
        if (ValidateTextLimit(req.Source, "option source", MaxScriptCharacters) is { } limitResult)
            return limitResult;

        try
        {
            return Ok(new ApplyDesignerQueryFiltersResponse(
                _queryFilters.BuildCategoricalOptionSource(req.Source, req.Column)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

    private PortalDesignerLimitsConfig DesignerLimits => _portalConfig.DesignerLimits ??= new PortalDesignerLimitsConfig();
    private int MaxScriptCharacters => Math.Max(1, DesignerLimits.MaxScriptCharacters);
    private int MaxSelectionCharacters => Math.Max(1, DesignerLimits.MaxSelectionCharacters);
    private int MaxAstStatements => Math.Max(1, DesignerLimits.MaxAstStatements);
    private int MaxGeneratedItems => Math.Max(1, DesignerLimits.MaxGeneratedItems);
    private int MaxGeneratedScriptCharacters => Math.Max(1, DesignerLimits.MaxGeneratedScriptCharacters);

    // A hover token is a single identifier; anything longer is not a word the help corpus can key on.
    private const int MaxHoverWordCharacters = 256;

    private IActionResult? ValidateTextLimit(string? value, string field, int maxCharacters)
    {
        if (value != null && value.Length > maxCharacters)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = $"Designer {field} exceeds the {maxCharacters} character limit." });
        return null;
    }

    private IActionResult AstLimitExceeded(DesignerAstLimitExceededException ex) =>
        StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = ex.Message });

    private IActionResult? ValidateDesignerState(DesignerStateDto? state)
    {
        if (state is null)
            return BadRequest(new { error = "Design state is required." });

        var pageCount = state.Pages?.Count ?? 0;
        var visualCount = state.Pages?.Sum(p => p.Visuals?.Count ?? 0) ?? 0;
        var datasetCount = state.Datasets?.Count ?? 0;
        var parameterCount = state.Parameters?.Count ?? 0;
        if (pageCount + visualCount + datasetCount + parameterCount > MaxGeneratedItems)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = $"Designer state exceeds the {MaxGeneratedItems} generated item limit." });
        }

        return null;
    }

    private bool TryEnterDesignerGate(out SemaphoreSlim? gate)
    {
        var maxConcurrent = DesignerLimits.MaxConcurrentRequests;
        if (maxConcurrent <= 0)
        {
            gate = null;
            return true;
        }

        gate = DesignerGates.GetOrAdd(maxConcurrent, limit => new SemaphoreSlim(limit, limit));
        return gate.Wait(0);
    }

    internal static bool TryAcquireDesignerGateForTest(PortalConfig config, out IDisposable lease)
    {
        var limits = config.DesignerLimits ??= new PortalDesignerLimitsConfig();
        var maxConcurrent = Math.Max(1, limits.MaxConcurrentRequests);
        var gate = DesignerGates.GetOrAdd(maxConcurrent, limit => new SemaphoreSlim(limit, limit));
        if (gate.Wait(0))
        {
            lease = new DesignerGateLease(gate);
            return true;
        }

        lease = new DesignerGateLease(null);
        return false;
    }

    private sealed class DesignerGateLease(SemaphoreSlim? gate) : IDisposable
    {
        public void Dispose() => gate?.Release();
    }

    private IActionResult DesignerBusy() =>
        StatusCode(StatusCodes.Status429TooManyRequests,
            new { error = "Designer is busy; retry shortly." });

    private static (string ScriptBefore, string Prefix) GetCompletionPosition(string script, int line, int column)
    {
        var lines = SplitLines(script);
        if (lines.Count == 0)
            return ("", "");

        var safeLine = Math.Clamp(line, 0, lines.Count - 1);
        var currentLine = lines[safeLine];
        var safeColumn = Math.Clamp(column, 0, currentLine.Length);
        var beforeCursor = currentLine[..safeColumn];
        var match = Regex.Match(beforeCursor, @"([\$&\#@\w\.\*]+)$");
        var prefix = match.Success ? match.Value : string.Empty;
        var scriptBefore = string.Join("\n", lines.Take(safeLine));
        if (safeLine > 0)
            scriptBefore += "\n";
        scriptBefore += beforeCursor;

        return (scriptBefore, prefix);
    }

    // ── GET /api/designer/snapshot/{reportId} ─────────────────────────────────
    // Serves the last compiled .etlsnap so the canvas can lay visuals out against real historical
    // data instead of wireframe placeholders, without touching a production database.
    //
    // A missing snapshot is a normal state, not an error: a report that has never run has none, and
    // an identity-sensitive report never persists one (ExecutionJobService keeps those per-viewer),
    // which is exactly what stops one designer seeing another's row-filtered data.

    [HttpGet("snapshot/{reportId:int}")]
    [RequireStudioCapability(StudioCapabilities.ScriptRead)]
    public async Task<IActionResult> GetDesignerSnapshot(int reportId, CancellationToken cancellationToken)
    {
        if (_snapshots is null) return NotFound(new { error = "Snapshot designing is not available." });

        var result = await _snapshots.LoadForDesignerAsync(reportId, User, cancellationToken);

        return result.Outcome switch
        {
            DesignerSnapshotService.SnapshotOutcome.Ok => Ok(new
            {
                reportName = result.Package!.ReportName,
                builtAt = result.Package.BuiltAt,
                sampleRows = result.Package.SampleRows,
                columns = result.Package.Columns,
                metadata = new
                {
                    isSampled = result.Package.Metadata.IsSampled,
                    rlsEnforced = result.Package.Metadata.RlsEnforced,
                    totalRows = result.Package.Metadata.TotalRows,
                    returnedRows = result.Package.Metadata.ReturnedRows,
                },
            }),
            DesignerSnapshotService.SnapshotOutcome.ReportNotFound => NotFound(),
            DesignerSnapshotService.SnapshotOutcome.Forbidden => Forbid(),
            _ => NotFound(new { error = "No snapshot available." }),
        };
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult SavedScriptResponse(ReportScriptSaveResult result)
    {
        OptimisticConcurrency.SetETag(Response, result.Version!.Value);
        return Ok(new SaveDesignerResponse(result.Version.Value, result.SourceRevision));
    }

}
