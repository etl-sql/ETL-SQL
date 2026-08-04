using System.Security.Claims;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Filters;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

public sealed record SaveDraftRequest(string ScriptText, string? BaseScriptHash);
public sealed record DecideDraftRequest(string? Reason);

public sealed record DraftDto(
    int Id,
    int ReportId,
    string Status,
    string ScriptHash,
    string? BaseScriptHash,
    string? AuthorUserName,
    string? ApprovedByUserName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? DecidedAtUtc,
    DateTime? PublishedAtUtc,
    long Version,
    IReadOnlyList<DraftDecisionDto> Decisions);

public sealed record DraftDecisionDto(
    string Decision, string? Reason, string? ScriptHash, string? DecidedBy, DateTime DecidedAtUtc);

/// <summary>
/// Draft → review → publish for report scripts.
///
/// <para>Every mutation here takes <c>If-Match</c> carrying the draft's version. The workflow is a
/// conversation between at least two people, so "the thing I am approving is the thing I read" has
/// to be checkable — an approval that silently applied to content edited a second earlier would be
/// worse than no approval, because it would carry a reviewer's name.</para>
/// </summary>
[ApiController]
[Route("api/reports/{reportId:int}/draft")]
[Authorize]
[RequirePortalModule("Designer")]
public sealed class ReportDraftsController(
    ReportDraftWorkflowService workflow,
    ReportScriptSaveService scriptSave,
    PortalConfig portalConfig,
    Microsoft.Extensions.Logging.ILogger<ReportDraftsController> logger) : ControllerBase
{
    private int CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private string? CurrentUserName => User.Identity?.Name;

    private bool WorkflowEnabled => portalConfig.Studio?.RequireApprovalToPublish == true;

    [HttpGet]
    public async Task<IActionResult> Get(int reportId, CancellationToken cancellationToken)
    {
        if (!WorkflowEnabled) return Disabled();
        var draft = await workflow.OpenDraftAsync(reportId, cancellationToken);
        return draft is null ? NoContent() : Ok(ToDto(draft));
    }

    [HttpPut]
    public async Task<IActionResult> Save(
        int reportId, [FromBody] SaveDraftRequest req, CancellationToken cancellationToken)
    {
        if (!WorkflowEnabled) return Disabled();
        if (string.IsNullOrWhiteSpace(req.ScriptText))
            return BadRequest(new { error = "Script text is required." });

        var result = await workflow.SaveDraftAsync(
            reportId, req.ScriptText, User, CurrentUserId, CurrentUserName,
            req.BaseScriptHash, cancellationToken);
        return Respond(result);
    }

    [HttpPost("submit")]
    public async Task<IActionResult> Submit(int reportId, CancellationToken cancellationToken)
    {
        if (!WorkflowEnabled) return Disabled();
        var result = await workflow.SubmitAsync(
            reportId, User, CurrentUserId, CurrentUserName, ExpectedVersion(), cancellationToken);
        return Respond(result);
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve(
        int reportId, [FromBody] DecideDraftRequest? req, CancellationToken cancellationToken)
    {
        if (!WorkflowEnabled) return Disabled();
        var result = await workflow.DecideAsync(
            reportId, approve: true, req?.Reason, User, CurrentUserId, CurrentUserName,
            ExpectedVersion(), cancellationToken);
        return Respond(result);
    }

    [HttpPost("reject")]
    public async Task<IActionResult> Reject(
        int reportId, [FromBody] DecideDraftRequest? req, CancellationToken cancellationToken)
    {
        if (!WorkflowEnabled) return Disabled();
        if (string.IsNullOrWhiteSpace(req?.Reason))
            return BadRequest(new { error = "A reason is required so the author knows what to change." });

        var result = await workflow.DecideAsync(
            reportId, approve: false, req.Reason, User, CurrentUserId, CurrentUserName,
            ExpectedVersion(), cancellationToken);
        return Respond(result);
    }

    /// <summary>
    /// Publishes the approved draft: writes the script through the existing save path, then marks
    /// the draft published.
    /// </summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(int reportId, CancellationToken cancellationToken)
    {
        if (!WorkflowEnabled) return Disabled();

        var gate = await workflow.BeginPublishAsync(reportId, User, CurrentUserId, cancellationToken);
        if (gate.Status != DraftWorkflowStatus.Ok || gate.Draft is null) return Respond(gate);

        // The script write, its backup and its rollback already belong to ReportScriptSaveService.
        // Duplicating them here would give the workflow a second, subtly different way to put a
        // script on disk — and the one that is used less is the one that rots.
        var saved = await scriptSave.SaveAsync(
            reportId, gate.Draft.ScriptText, gate.ReportVersion, User, CurrentUserId,
            baseRevision: null, ct: cancellationToken);

        if (saved.Status != ReportScriptSaveStatus.Saved)
        {
            logger.LogWarning(
                "Publishing draft {DraftId} for report {ReportId} failed at the script write: {Status}",
                gate.Draft.Id, reportId, saved.Status);
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                error = "The approved draft could not be written to the script.",
                status = saved.Status.ToString()
            });
        }

        await workflow.CompletePublishAsync(gate.Draft, CurrentUserId, cancellationToken);
        return Ok(ToDto(gate.Draft));
    }

    private IActionResult Disabled() => NotFound(new
    {
        error = "Draft approval is not enabled on this Portal.",
        setting = "Portal:Studio:RequireApprovalToPublish"
    });

    private long? ExpectedVersion() =>
        OptimisticConcurrency.ReadExpectedVersion(Request);

    private IActionResult Respond(DraftWorkflowResult result) => result.Status switch
    {
        DraftWorkflowStatus.Ok => Ok(ToDto(result.Draft!)),
        DraftWorkflowStatus.NotFound => NotFound(new { error = "No draft is open for this report." }),
        DraftWorkflowStatus.Forbidden => Forbid(),
        DraftWorkflowStatus.MissingVersion =>
            StatusCode(StatusCodes.Status428PreconditionRequired,
                new { error = "If-Match with the draft's version is required." }),
        DraftWorkflowStatus.Conflict =>
            Conflict(new { error = result.Message, current = ToDto(result.Draft!) }),
        DraftWorkflowStatus.InvalidState =>
            Conflict(new { error = result.Message, current = ToDto(result.Draft!) }),
        // 403 rather than 409: this is a refusal, not a race, and the difference matters to whoever
        // reads the log afterwards.
        DraftWorkflowStatus.SelfApproval =>
            StatusCode(StatusCodes.Status403Forbidden, new { error = result.Message }),
        DraftWorkflowStatus.StaleBase =>
            Conflict(new { error = result.Message, current = ToDto(result.Draft!) }),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static DraftDto ToDto(ReportScriptDraft d) => new(
        d.Id, d.ReportId, d.Status, d.ScriptHash, d.BaseScriptHash, d.AuthorUserName,
        d.ApprovedByUserName, d.CreatedAtUtc, d.UpdatedAtUtc, d.SubmittedAtUtc, d.DecidedAtUtc,
        d.PublishedAtUtc, d.Version,
        [.. d.Decisions
            .OrderByDescending(x => x.DecidedAtUtc)
            .Select(x => new DraftDecisionDto(
                x.Decision, x.Reason, x.ScriptHash, x.DecidedByUserName, x.DecidedAtUtc))]);
}
