using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Services;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ETL_SQL.Portal.Controllers;

[ApiController]
[Route("api/designer")]
[Authorize(Roles = "Admin,Publisher")]
[RequirePortalModule("Designer")]
public class DesignerController : ControllerBase
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> DesignerGates = new();
    private readonly PortalDesignerSchemaService? _schemaService;
    private readonly PortalDesignerRunService? _runService;
    private readonly PortalDesignerPreviewService? _previewService;
    private readonly ReportScriptSaveService? _scriptSave;
    private readonly ILanguageService? _languageService;
    private readonly DesignerAnalysisService _analysisService;
    private readonly DesignerScriptGenerationService _scriptGenerationService;
    private readonly PortalConfig _portalConfig;

    public DesignerController(
        PortalDesignerSchemaService? schemaService = null,
        PortalDesignerRunService? runService = null,
        ReportScriptSaveService? scriptSave = null,
        ILanguageService? languageService = null,
        DesignerAnalysisService? analysisService = null,
        DesignerScriptGenerationService? scriptGenerationService = null,
        PortalConfig? portalConfig = null,
        PortalDesignerPreviewService? previewService = null)
    {
        _schemaService = schemaService;
        _runService = runService;
        _previewService = previewService;
        _scriptSave = scriptSave;
        _languageService = languageService;
        _analysisService = analysisService ?? new DesignerAnalysisService();
        _scriptGenerationService = scriptGenerationService ?? new DesignerScriptGenerationService();
        _portalConfig = portalConfig ?? new PortalConfig();
    }

    // ── POST /api/designer/parse ──────────────────────────────────────────────

    [HttpPost("parse")]
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

    // ── POST /api/designer/run ───────────────────────────────────────────────

    [HttpPost("run")]
    [EnableRateLimiting("designer")]
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
            return Ok(manifest);
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

    // ── POST /api/designer/save ──────────────────────────────────────────────

    [HttpPost("save")]
    [Authorize(Roles = "Admin,Publisher")]
    [EnableRateLimiting("designer")]
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

    // ── POST /api/designer/generate ───────────────────────────────────────────

    [HttpPost("generate")]
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
            var script = _scriptGenerationService.Generate(req.DesignState);
            if (ValidateTextLimit(script, "generated script", MaxGeneratedScriptCharacters) is { } generatedLimit)
                return generatedLimit;
            return Ok(new GenerateDesignerResponse(script));
        }
        finally
        {
            gate?.Release();
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
        if (pageCount + visualCount + datasetCount > MaxGeneratedItems)
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

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult SavedScriptResponse(ReportScriptSaveResult result)
    {
        OptimisticConcurrency.SetETag(Response, result.Version!.Value);
        return Ok(new SaveDesignerResponse(result.Version.Value, result.SourceRevision));
    }

}
