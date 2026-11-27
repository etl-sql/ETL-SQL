using System.Net.Http.Json;
using System.Security.Claims;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Proxies all Orchestrator management calls through the Orchestrator.Service HTTP API.
/// URL and API key are resolved at call time from OrchestratorSettingsService so they
/// can be changed in the admin UI without restarting the portal.
/// All methods return null / empty collections when the Orchestrator is offline.
/// </summary>
public interface ISharedTenantLifecycleOrchestratorClient
{
    Task<HttpResponseMessage?> ApplySharedTenantLifecycleAsync(
        TenantContext platformTenant,
        SharedTenantLifecycleCommand command,
        CancellationToken cancellationToken = default);
}

public class OrchestratorProxyService(
    HttpClient http,
    OrchestratorSettingsService settings,
    ILogger<OrchestratorProxyService> logger,
    // Optional: background work (alert evaluation, subscription delivery) has no HTTP context and
    // therefore no human to attribute, which is a legitimate state rather than a missing dependency.
    IHttpContextAccessor? httpContext = null,
    PortalConfig? portalConfig = null,
    PortalDbContext? portalDb = null,
    OrchestratorAssertionIssuer? assertionIssuer = null) : ISharedTenantLifecycleOrchestratorClient
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.ApiUrl);

    public async Task<bool> RequiresFederatedIdentityAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(portalConfig?.Orchestrator.IdentitySigningSecret))
            return false;

        var health = await ReadJsonAsync<OrchestratorHealth>("health", "authorization mode", ct);
        return health.Value?.RequiresCallerIdentity == true;
    }

    private sealed record OrchestratorHealth(bool RequiresCallerIdentity);

    public async Task<bool> IsOnlineAsync()
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, "health");
            return resp?.IsSuccessStatusCode ?? false;
        }
        catch { return false; }
    }

    public async Task<OrchestratorMetricsDto?> GetMetricsAsync()
    {
        try
        {
            var raw = await GetJsonAsync<RawMetrics>("metrics");
            if (raw == null) return null;
            return new OrchestratorMetricsDto(
                raw.active_jobs, raw.queued_jobs, raw.max_jobs,
                raw.available_slots, raw.active_processes);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Orchestrator metrics unavailable."); return null; }
    }

    public async Task<OrchestratorStatusDto?> GetStatusAsync()
    {
        try { return await GetJsonAsync<OrchestratorStatusDto>("management/status"); }
        catch (Exception ex) { logger.LogDebug(ex, "Orchestrator status unavailable."); return null; }
    }

    public async Task<List<JobDefinitionDto>> GetJobsAsync(int limit = 1000, int offset = 0)
    {
        try { return await GetJsonAsync<List<JobDefinitionDto>>($"api/scheduled-jobs?limit={Math.Clamp(limit, 1, 1000)}&offset={Math.Max(0, offset)}") ?? []; }
        catch (Exception ex) { logger.LogDebug(ex, "Orchestrator job list unavailable."); return []; }
    }

    public async Task<HttpResponseMessage?> GetJobActivityAsync(
        string filter, string status, string? search, int limit, int offset, CancellationToken ct = default,
        bool requireVerifiedCaller = false)
    {
        try
        {
            var path = $"api/scheduled-jobs/activity?filter={Uri.EscapeDataString(filter)}"
                + $"&status={Uri.EscapeDataString(status)}&search={Uri.EscapeDataString(search ?? "")}&limit={limit}&offset={offset}";
            return await SendAsync(HttpMethod.Get, path, cancellationToken: ct, requireVerifiedCaller: requireVerifiedCaller);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Orchestrator activity unavailable.");
            return null;
        }
    }

    public async Task<HttpResponseMessage?> CreateJobAsync(CreateJobRequest req)
    {
        try { return await SendAsync(HttpMethod.Post, "api/scheduled-jobs", req); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to create job."); return null; }
    }

    public async Task<HttpResponseMessage?> UpdateJobAsync(string name, UpdateJobRequest req, long version)
    {
        try { return await SendAsync(HttpMethod.Put, $"api/scheduled-jobs/{Uri.EscapeDataString(name)}", req, version); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to update job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> SetJobToolApprovalAsync(string name, bool approved, long version)
    {
        try
        {
            return await SendAsync(HttpMethod.Put, $"api/scheduled-jobs/{Uri.EscapeDataString(name)}/tool-approval",
                new { approved }, version);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to set tool approval for job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> DeleteJobAsync(string name, long version)
    {
        try { return await SendAsync(HttpMethod.Delete, $"api/scheduled-jobs/{Uri.EscapeDataString(name)}", version: version); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to delete job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> TriggerJobAsync(
        string name,
        IReadOnlyDictionary<string, string>? variables = null, CancellationToken ct = default,
        bool requireVerifiedCaller = false)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Post,
                $"api/scheduled-jobs/{Uri.EscapeDataString(name)}/trigger",
                new TriggerJobRequest(variables is null
                    ? null
                    : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase)),
                cancellationToken: ct, requireVerifiedCaller: requireVerifiedCaller);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogWarning(ex, "Failed to trigger job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> ResumeRunAsync(long historyId)
    {
        try { return await SendAsync(HttpMethod.Post, $"api/job-runs/{historyId}/resume"); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resume job run {HistoryId}.", historyId);
            return null;
        }
    }

    public async Task<HttpResponseMessage?> KillJobAsync(string name)
    {
        try { return await SendAsync(HttpMethod.Post, $"api/scheduled-jobs/{Uri.EscapeDataString(name)}/kill"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to kill job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    // ── Revision runs ─────────────────────────────────────────────────────────
    //
    // A saved catalog pipeline runs through one Orchestrator job. The Orchestrator decides whether
    // the caller may run or provision it; these carry the caller's assertion and return its answer.

    public async Task<HttpResponseMessage?> ProvisionJobSlotAsync(
        string jobName, string scriptText, string? displayName, string? description,
        CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Post,
                $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/provision",
                new { ScriptText = scriptText, DisplayName = displayName, Description = description },
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to provision run slot for job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> RunJobRevisionAsync(
        string jobName, string scriptText, string? displayName, string? description,
        CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Post,
                $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/runs",
                new { ScriptText = scriptText, ProvisionIfMissing = true, DisplayName = displayName, Description = description },
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to submit a revision run for job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> GetJobRunAsync(
        long runId, CancellationToken ct = default, bool requireVerifiedCaller = false)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/job-runs/{runId}", cancellationToken: ct, requireVerifiedCaller: requireVerifiedCaller); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read job run {RunId}.", runId);
            return null;
        }
    }

    public async Task<HttpResponseMessage?> ScheduleJobRevisionAsync(
        string jobName, string scriptText, string cron, string? timeZone, string? displayName, string? revision,
        CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Put,
                $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/revision-schedule",
                new
                {
                    ScriptText = scriptText,
                    Cron = cron,
                    TimeZone = timeZone,
                    ProvisionIfMissing = true,
                    DisplayName = displayName,
                    Revision = revision
                },
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to schedule a revision of job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> GetJobRevisionScheduleAsync(string jobName, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Get, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/revision-schedule", cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read the schedule of job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> UnscheduleJobRevisionAsync(string jobName, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Delete, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/revision-schedule", cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to remove the schedule of job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> CancelJobRunAsync(
        long runId, CancellationToken ct = default, bool requireVerifiedCaller = false)
    {
        try { return await SendAsync(HttpMethod.Post, $"api/job-runs/{runId}/cancel", cancellationToken: ct, requireVerifiedCaller: requireVerifiedCaller); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to cancel job run {RunId}.", runId);
            return null;
        }
    }

    public async Task<HttpResponseMessage?> GetFleetNodesAndSandboxesAsync(CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, "api/fleet/nodes-sandboxes", cancellationToken: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read Orchestrator fleet nodes and sandbox pools.");
            return null;
        }
    }

    // ── Per-object grants ─────────────────────────────────────────────────────
    //
    // Proxied rather than reimplemented. The Orchestrator owns the grant store and already enforces
    // the decision — tenant, ownership, scope ceiling — so a Portal-side copy would be a second
    // answer to the same question, and the one that drifts. These pass the caller's own signed
    // assertion through, so an administrator who may not manage an object is refused there rather
    // than here, by the same rule that governs every other route.

    public async Task<HttpResponseMessage?> GetObjectGrantsAsync(
        string kind, string name, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Get,
                $"api/authorization/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(name)}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read grants for {Kind} {Name}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(kind), ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> SetObjectGrantAsync(
        string kind, string name, string principalKind, string principalId, string permission,
        CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Put,
                $"api/authorization/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(name)}" +
                $"/{Uri.EscapeDataString(principalKind)}/{Uri.EscapeDataString(principalId)}",
                new { Permission = permission },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set a grant on {Kind} {Name}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(kind), ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> DeleteObjectGrantAsync(
        string kind, string name, string principalKind, string principalId, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Delete,
                $"api/authorization/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(name)}" +
                $"/{Uri.EscapeDataString(principalKind)}/{Uri.EscapeDataString(principalId)}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to revoke a grant on {Kind} {Name}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(kind), ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return null;
        }
    }

    // ── Ownership ─────────────────────────────────────────────────────────────
    //
    // Proxied on the same terms as grants. The Orchestrator decides whether the caller may reassign
    // ownership; the Portal's job is to present the caller it already authenticated, not to form a
    // second opinion about what that caller may do.

    public async Task<HttpResponseMessage?> GetUnownedObjectsAsync(CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, "api/authorization/unowned", cancellationToken: ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to read unowned orchestrator objects."); return null; }
    }

    public async Task<HttpResponseMessage?> SetObjectOwnerAsync(
        string kind, string name, string principalKind, string principalId, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Put,
                $"api/authorization/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(name)}/owner",
                new { PrincipalKind = principalKind, PrincipalId = principalId },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set the owner of {Kind} {Name}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(kind), ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> AdoptUnownedObjectsAsync(
        string principalKind, string principalId, string? kind, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Post,
                "api/authorization/adopt",
                new { PrincipalKind = principalKind, PrincipalId = principalId, Kind = kind },
                cancellationToken: ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to adopt unowned orchestrator objects."); return null; }
    }

    public async Task<HttpResponseMessage?> DispatchNotificationAsync(
        string notificationName,
        OrchestratorNotificationDispatchRequest req,
        CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Post,
                $"api/notifications/{Uri.EscapeDataString(notificationName)}/dispatch",
                req,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to dispatch notification {Name}.",
                ETL_SQL.Core.Common.LogSanitizer.Clean(notificationName));
            return null;
        }
    }

    // ── Schedules ─────────────────────────────────────────────────────────────

    public async Task<ScheduleDefinitionDto?> GetScheduleAsync(string name)
    {
        try { return await GetJsonAsync<ScheduleDefinitionDto>($"api/schedules/{Uri.EscapeDataString(name)}"); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get schedule {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> CreateScheduleAsync(CreateScheduleRequest req)
    {
        try { return await SendAsync(HttpMethod.Post, "api/schedules", req); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to create schedule {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(req.Name)); return null; }
    }

    public async Task<HttpResponseMessage?> UpdateScheduleAsync(string name, UpdateScheduleRequest req)
    {
        try { return await SendAsync(HttpMethod.Put, $"api/schedules/{Uri.EscapeDataString(name)}", req); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to update schedule {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> DeleteScheduleAsync(string name)
    {
        try { return await SendAsync(HttpMethod.Delete, $"api/schedules/{Uri.EscapeDataString(name)}"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to delete schedule {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> AttachJobScheduleAsync(string jobName, string scheduleName)
    {
        try { return await SendAsync(HttpMethod.Post, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/schedules/{Uri.EscapeDataString(scheduleName)}"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to attach schedule {Schedule} to job {Job}.", ETL_SQL.Core.Common.LogSanitizer.Clean(scheduleName), ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return null; }
    }

    public async Task<HttpResponseMessage?> DetachJobScheduleAsync(string jobName, string scheduleName)
    {
        try { return await SendAsync(HttpMethod.Delete, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/schedules/{Uri.EscapeDataString(scheduleName)}"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to detach schedule {Schedule} from job {Job}.", ETL_SQL.Core.Common.LogSanitizer.Clean(scheduleName), ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return null; }
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public async Task<NotificationDefinitionDto?> GetNotificationAsync(string name)
    {
        try { return await GetJsonAsync<NotificationDefinitionDto>($"api/notifications/{Uri.EscapeDataString(name)}"); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get notification {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> CreateNotificationAsync(CreateNotificationRequest req)
    {
        try { return await SendAsync(HttpMethod.Post, "api/notifications", req); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to create notification {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(req.Name)); return null; }
    }

    public async Task<HttpResponseMessage?> UpdateNotificationAsync(string name, UpdateNotificationRequest req)
    {
        try { return await SendAsync(HttpMethod.Put, $"api/notifications/{Uri.EscapeDataString(name)}", req); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to update notification {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> DeleteNotificationAsync(string name)
    {
        try { return await SendAsync(HttpMethod.Delete, $"api/notifications/{Uri.EscapeDataString(name)}"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to delete notification {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> AttachJobNotificationAsync(string jobName, string notificationName, string trigger = "Completion")
    {
        try { return await SendAsync(HttpMethod.Post, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/notifications/{Uri.EscapeDataString(notificationName)}", new { Trigger = trigger }); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to attach notification {Notification} to job {Job}.", ETL_SQL.Core.Common.LogSanitizer.Clean(notificationName), ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return null; }
    }

    public async Task<HttpResponseMessage?> DetachJobNotificationAsync(string jobName, string notificationName, string? trigger = null)
    {
        try
        {
            var path = $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/notifications/{Uri.EscapeDataString(notificationName)}";
            if (!string.IsNullOrWhiteSpace(trigger)) path += $"?trigger={Uri.EscapeDataString(trigger)}";
            return await SendAsync(HttpMethod.Delete, path);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to detach notification {Notification} from job {Job}.", ETL_SQL.Core.Common.LogSanitizer.Clean(notificationName), ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return null; }
    }

    // ── Watermarks & Job State ──────────────────────────────────────────────────

    public async Task<HttpResponseMessage?> SetJobStateAsync(string jobName, string key, string? value)
    {
        try { return await SendAsync(HttpMethod.Put, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/state/{Uri.EscapeDataString(key)}", new { Value = value }); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to set state {Key} on job {Job}.", ETL_SQL.Core.Common.LogSanitizer.Clean(key), ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return null; }
    }

    public async Task<HttpResponseMessage?> DeleteJobStateAsync(string jobName, string key)
    {
        try { return await SendAsync(HttpMethod.Delete, $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/state/{Uri.EscapeDataString(key)}"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to delete state {Key} on job {Job}.", ETL_SQL.Core.Common.LogSanitizer.Clean(key), ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return null; }
    }

    // ── Data Quality & Stewardship & Bundles ────────────────────────────────────

    public async Task<HttpResponseMessage?> GetDataQualityStatusResponseAsync(int limit = 1000, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/data-quality/status?limit={limit}", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get data quality status."); return null; }
    }

    public async Task<HttpResponseMessage?> GetDataQualityFailuresResponseAsync(int limit = 1000, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/data-quality/failures?limit={limit}", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get data quality failures."); return null; }
    }

    public async Task<HttpResponseMessage?> GetStewardshipScoreResponseAsync(int limit = 1000, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/stewardship/score?limit={limit}", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get stewardship score."); return null; }
    }

    public async Task<HttpResponseMessage?> GetStewardshipGapsResponseAsync(int limit = 1000, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/stewardship/gaps?limit={limit}", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get stewardship gaps."); return null; }
    }

    public async Task<HttpResponseMessage?> GetBundlesResponseAsync(CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, "api/bundles", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list bundles."); return null; }
    }

    public async Task<HttpResponseMessage?> GetBundleVersionsResponseAsync(string name, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/bundles/{Uri.EscapeDataString(name)}/versions", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list versions for bundle {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<HttpResponseMessage?> GetBundleFilesResponseAsync(string name, int version, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/bundles/{Uri.EscapeDataString(name)}/versions/{version}/files", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list files for bundle {Name} v{Version}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name), version); return null; }
    }

    public async Task<HttpResponseMessage?> GetBundleDependenciesResponseAsync(string name, int version, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/bundles/{Uri.EscapeDataString(name)}/versions/{version}/dependencies", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list dependencies for bundle {Name} v{Version}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name), version); return null; }
    }

    public async Task<IReadOnlyList<BundleVersionInfo>> GetBundleVersionsAsync(string name, CancellationToken ct = default)
    {
        try { return await GetJsonAsync<List<BundleVersionInfo>>($"api/bundles/{Uri.EscapeDataString(name)}/versions", ct) ?? []; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to list versions for bundle {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return [];
        }
    }

    public async Task<IReadOnlyList<BundleFileInfo>> GetBundleFilesAsync(string name, int version, CancellationToken ct = default)
    {
        try { return await GetJsonAsync<List<BundleFileInfo>>($"api/bundles/{Uri.EscapeDataString(name)}/versions/{version}/files", ct) ?? []; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to list files for bundle {Name} v{Version}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name), version);
            return [];
        }
    }

    public async Task<IReadOnlyList<BundleDependencyInfo>> GetBundleDependenciesAsync(string name, int version, CancellationToken ct = default)
    {
        try { return await GetJsonAsync<List<BundleDependencyInfo>>($"api/bundles/{Uri.EscapeDataString(name)}/versions/{version}/dependencies", ct) ?? []; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to list dependencies for bundle {Name} v{Version}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name), version);
            return [];
        }
    }

    public async Task<OrchestratorScriptsDto?> GetScriptsAsync()
    {
        try { return await GetJsonAsync<OrchestratorScriptsDto>("api/scripts"); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list scripts."); return null; }
    }

    public async Task<string?> GetScriptContentAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var resp = await GetJsonAsync<ScriptContentResponse>($"api/scripts/content?path={Uri.EscapeDataString(path)}", ct);
            return resp?.Content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to get script content for {Path}.", ETL_SQL.Core.Common.LogSanitizer.Clean(path));
            return null;
        }
    }

    public async Task<HttpResponseMessage?> StopServiceAsync()
    {
        try { return await SendAsync(HttpMethod.Post, "management/stop"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to send stop signal."); return null; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Lists shared schedules, keeping a failed or refused read apart from an empty catalog.
    /// </summary>
    public Task<OrchestratorRead<List<ScheduleDefinitionDto>>> ReadSchedulesAsync(
        int limit = 1000, int offset = 0, CancellationToken ct = default) =>
        ReadJsonAsync<List<ScheduleDefinitionDto>>(
            $"api/schedules?limit={Math.Clamp(limit, 1, 1000)}&offset={Math.Max(0, offset)}", "schedules", ct);

    /// <summary>
    /// Lists notification endpoints, keeping a failed or refused read apart from an empty catalog.
    /// </summary>
    public Task<OrchestratorRead<List<NotificationDefinitionDto>>> ReadNotificationsAsync(
        int limit = 1000, int offset = 0, CancellationToken ct = default) =>
        ReadJsonAsync<List<NotificationDefinitionDto>>(
            $"api/notifications?limit={Math.Clamp(limit, 1, 1000)}&offset={Math.Max(0, offset)}", "notifications", ct);

    /// <summary>Reads a job's run history; a failed read is not an empty history.</summary>
    public Task<OrchestratorRead<List<JobHistoryEntryDto>>> ReadHistoryAsync(
        string jobName, int limit = 50, CancellationToken ct = default, bool requireVerifiedCaller = false) =>
        ReadJsonAsync<List<JobHistoryEntryDto>>(
            $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/history?limit={limit}", "job history", ct, requireVerifiedCaller);

    /// <summary>Reads the shared schedules attached to a job.</summary>
    public Task<OrchestratorRead<List<JobScheduleLinkDto>>> ReadJobSchedulesAsync(
        string jobName, CancellationToken ct = default) =>
        ReadJsonAsync<List<JobScheduleLinkDto>>(
            $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/schedules", "job schedules", ct);

    /// <summary>Reads the notification endpoints attached to a job.</summary>
    public Task<OrchestratorRead<List<JobNotificationLinkDto>>> ReadJobNotificationsAsync(
        string jobName, CancellationToken ct = default) =>
        ReadJsonAsync<List<JobNotificationLinkDto>>(
            $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/notifications", "job notifications", ct);

    /// <summary>Reads a job's watermark and incremental state keys.</summary>
    public Task<OrchestratorRead<List<JobStateEntryDto>>> ReadJobStatesAsync(
        string jobName, CancellationToken ct = default) =>
        ReadJsonAsync<List<JobStateEntryDto>>(
            $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/state", "job state", ct);
    // ── Remote history, triage, subscriptions, and governance sync (OR04) ──────

    public async Task<List<JobHistoryEntry>?> GetHistoryAsync(
        string? jobName = null, int limit = 1000, CancellationToken ct = default)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(jobName)
                ? $"api/history?limit={Math.Clamp(limit, 1, 1000)}"
                : $"api/history?jobName={Uri.EscapeDataString(jobName)}&limit={Math.Clamp(limit, 1, 1000)}";
            using var resp = await SendAsync(HttpMethod.Get, path, cancellationToken: ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(ct) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read Orchestrator history over HTTP.");
            return null;
        }
    }

    public async Task<TriageRunDetailDto?> GetTriageRunDetailAsync(long historyId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, $"api/history/{historyId}/detail", cancellationToken: ct);
            if (resp is null || resp.StatusCode == System.Net.HttpStatusCode.NotFound || !resp.IsSuccessStatusCode)
                return null;
            var payload = await resp.Content.ReadFromJsonAsync<RemoteTriageRunDetailPayload>(ct);
            if (payload is null) return null;
            var h = payload.Run;
            var run = new TriageRunDto(
                h.Id, h.JobName, h.StartTime, h.EndTime, h.Status, h.ErrorMessage,
                h.RowsProcessed, h.RowsQuarantined, h.RowsWarned, h.DataQualityFailures,
                h.ScriptHashAtRunTime, h.HashMatched);
            return new TriageRunDetailDto(run, payload.Statements, payload.QualityFailures);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read triage detail for run {RunId}.", historyId);
            return null;
        }
    }

    public async Task<List<JobHistoryEntry>?> GetCompletedHistoryAsync(
        DateTime sinceUtc, DateTime untilUtc, int limit = 1000, int offset = 0, CancellationToken ct = default)
    {
        var path = $"api/history/completions?sinceUtc={Uri.EscapeDataString(sinceUtc.ToUniversalTime().ToString("O"))}"
            + $"&untilUtc={Uri.EscapeDataString(untilUtc.ToUniversalTime().ToString("O"))}"
            + $"&limit={Math.Clamp(limit, 1, 1000)}&offset={Math.Max(0, offset)}";
        using var resp = await SendAsPortalSystemAsync(HttpMethod.Get, path, ct: ct);
        if (resp is null || !resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(ct) ?? [];
    }

    public async Task<bool> SyncSubscriptionJobAsync(
        int subscriptionId, object request, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsPortalSystemAsync(
                HttpMethod.Put, $"api/portal/subscriptions/{subscriptionId}/job", request, ct);
            return resp is { IsSuccessStatusCode: true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to sync subscription {SubscriptionId} job over HTTP.", subscriptionId);
            return false;
        }
    }

    public async Task<bool> DeleteSubscriptionJobAsync(
        int subscriptionId, string jobName, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsPortalSystemAsync(
                HttpMethod.Delete,
                $"api/portal/subscriptions/{subscriptionId}/job?jobName={Uri.EscapeDataString(jobName)}",
                ct: ct);
            return resp is { IsSuccessStatusCode: true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to delete subscription {SubscriptionId} job over HTTP.", subscriptionId);
            return false;
        }
    }

    public async Task<List<JobHistoryEntry>?> GetSubscriptionHistoryAsync(
        int subscriptionId, string jobName, int limit = 50, CancellationToken ct = default)
    {
        var path = $"api/portal/subscriptions/{subscriptionId}/history?jobName={Uri.EscapeDataString(jobName)}&limit={Math.Clamp(limit, 1, 1000)}";
        using var resp = await SendAsPortalSystemAsync(HttpMethod.Get, path, ct: ct);
        if (resp is null || !resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<List<JobHistoryEntry>>(ct) ?? [];
    }

    public async Task<NotificationDefinition?> GetNotificationDefinitionAsync(
        string name, CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAsPortalSystemAsync(
                HttpMethod.Get, $"api/notifications/{Uri.EscapeDataString(name)}", ct: ct);
            if (resp is null || !resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<NotificationDefinition>(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to read notification {Name} over HTTP.", ETL_SQL.Core.Common.LogSanitizer.Clean(name));
            return null;
        }
    }

    public async Task<bool> UpsertRemoteConnectionAsync(string alias, object payload, CancellationToken ct = default)
    {
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Put, $"api/admin/connections/{Uri.EscapeDataString(alias)}", payload, ct);
        return resp is { IsSuccessStatusCode: true };
    }

    public async Task<bool> SetRemoteConnectionEnabledAsync(string alias, bool enabled, CancellationToken ct = default)
    {
        var verb = enabled ? "enable" : "disable";
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Post, $"api/admin/connections/{Uri.EscapeDataString(alias)}/{verb}", ct: ct);
        return resp is { IsSuccessStatusCode: true } || resp?.StatusCode == System.Net.HttpStatusCode.NotFound;
    }

    public async Task<bool> DeleteRemoteConnectionAsync(string alias, CancellationToken ct = default)
    {
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Delete, $"api/admin/connections/{Uri.EscapeDataString(alias)}", ct: ct);
        return resp is { IsSuccessStatusCode: true } || resp?.StatusCode == System.Net.HttpStatusCode.NotFound;
    }

    public async Task<bool> ImportRemoteConnectionsAsync(IEnumerable<object> entries, CancellationToken ct = default)
    {
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Post, "api/admin/connections/import", entries, ct);
        return resp is { IsSuccessStatusCode: true };
    }

    public async Task<bool> UpsertRemoteSecretAsync(string name, string value, CancellationToken ct = default)
    {
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Put, $"api/admin/secrets/{Uri.EscapeDataString(name)}", new { Value = value }, ct);
        return resp is { IsSuccessStatusCode: true };
    }

    public async Task<bool> SetRemoteSecretEnabledAsync(string name, bool enabled, CancellationToken ct = default)
    {
        var verb = enabled ? "enable" : "disable";
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Post, $"api/admin/secrets/{Uri.EscapeDataString(name)}/{verb}", ct: ct);
        return resp is { IsSuccessStatusCode: true } || resp?.StatusCode == System.Net.HttpStatusCode.NotFound;
    }

    public async Task<bool> DeleteRemoteSecretAsync(string name, CancellationToken ct = default)
    {
        using var resp = await SendAsPortalSystemAsync(
            HttpMethod.Delete, $"api/admin/secrets/{Uri.EscapeDataString(name)}", ct: ct);
        return resp is { IsSuccessStatusCode: true } || resp?.StatusCode == System.Net.HttpStatusCode.NotFound;
    }

    private async Task<HttpResponseMessage?> SendAsPortalSystemAsync(
        HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var req = settings.CreateRequest(method, path);
        if (req is null) return null;

        var assertion = (assertionIssuer ?? new OrchestratorAssertionIssuer(portalConfig, portalDb))
            .IssueForBackground();
        if (assertion is not null)
            req.Headers.TryAddWithoutValidation(OrchestratorIdentityAssertion.HeaderName, assertion);
        if (body is not null)
            req.Content = JsonContent.Create(body);

        return await http.SendAsync(req, ct);
    }

    private sealed record RemoteTriageRunDetailPayload(
        JobHistoryEntry Run,
        List<ETL_SQL.Core.Profiling.StatementMetricsPayload> Statements,
        List<JobDataQualityFailure> QualityFailures);

    private async Task<OrchestratorRead<T>> ReadJsonAsync<T>(
        string path, string subject, CancellationToken ct, bool requireVerifiedCaller = false)
        where T : class
    {
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, path, cancellationToken: ct, requireVerifiedCaller: requireVerifiedCaller);
            if (resp is null) return new(StatusCodes.Status503ServiceUnavailable, null);
            if (!resp.IsSuccessStatusCode) return new((int)resp.StatusCode, null);
            return new(StatusCodes.Status200OK, await resp.Content.ReadFromJsonAsync<T>(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Orchestrator {Subject} read failed.", subject);
            return new(StatusCodes.Status503ServiceUnavailable, null);
        }
    }
    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, cancellationToken: ct);
        if (resp == null || !resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(ct);
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        long? version = null,
        CancellationToken cancellationToken = default,
        bool requireVerifiedCaller = false)
    {
        using var req = settings.CreateRequest(method, path);
        if (req is null) return null;

        var identityAssertion = await CurrentIdentityAssertionAsync(cancellationToken);
        if (requireVerifiedCaller)
        {
            if (httpContext?.HttpContext?.User.Identity?.IsAuthenticated != true || identityAssertion is null)
                return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            req.Headers.TryAddWithoutValidation(OrchestratorIdentityAssertion.RequireVerifiedCallerHeader, "true");
        }
        if (identityAssertion is not null)
            req.Headers.TryAddWithoutValidation(OrchestratorIdentityAssertion.HeaderName, identityAssertion);
        if (version.HasValue)
            req.Headers.TryAddWithoutValidation("If-Match", OptimisticConcurrency.ToETag(version.Value));
        if (body is not null)
            req.Content = JsonContent.Create(body);

        return await http.SendAsync(req, cancellationToken);
    }

    /// <summary>
    /// The assertion attached to this call, or null when the deployment does not federate identity.
    /// Delegates to <see cref="OrchestratorAssertionIssuer"/> rather than resolving the principal
    /// here: the exchange endpoint hands the same token to clients that call the Orchestrator
    /// directly, and two resolutions would eventually disagree about who someone is.
    /// </summary>
    private Task<string?> CurrentIdentityAssertionAsync(CancellationToken cancellationToken) =>
        (assertionIssuer ?? new OrchestratorAssertionIssuer(portalConfig, portalDb))
            .IssueForCurrentCallerAsync(httpContext?.HttpContext?.User, cancellationToken);

    /// <summary>
    /// Applies one Shared control-plane lifecycle step as attributed platform automation. This path
    /// never reuses the current tenant user's identity; the target tenant and operator come from a
    /// live signed-policy <see cref="PlatformAccessGrant"/>.
    /// </summary>
    public async Task<HttpResponseMessage?> ApplySharedTenantLifecycleAsync(
        TenantContext platformTenant,
        SharedTenantLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformTenant);
        ArgumentNullException.ThrowIfNull(command);
        platformTenant.RequireActivePlatformGrant(command.NowUtc);
        var grant = platformTenant.Grant!;
        if (!string.Equals(grant.OperatorPrincipal, command.PlatformOperator, StringComparison.Ordinal)
            || !string.Equals(grant.AuthorizationReference, command.AuthorizationReference, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Lifecycle command does not match its platform grant.");

        using var request = settings.CreateRequest(HttpMethod.Post, "api/platform/shared-tenants/lifecycle");
        if (request is null) return null;
        var secret = portalConfig?.Orchestrator.IdentitySigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Shared lifecycle requires Portal-to-Orchestrator identity assertion signing.");

        var assertion = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller(
                "platform", grant.OperatorPrincipal, grant.OperatorPrincipal,
                ["PlatformLifecycle"], [], platformTenant.Tenant.Value),
            secret, command.NowUtc);
        request.Headers.TryAddWithoutValidation(OrchestratorIdentityAssertion.HeaderName, assertion);
        request.Content = JsonContent.Create(new
        {
            command.OperationId,
            Kind = command.Kind.ToString(),
            command.AuthorizationReference,
            command.TargetRelease,
            command.MaxConcurrentJobs,
            command.MaxStorageMb,
            command.MaxReportSessions
        });
        return await http.SendAsync(request, cancellationToken);
    }

    // ── Private raw-deserialization types ─────────────────────────────────────

    private sealed class RawMetrics
    {
        public int active_jobs { get; set; }
        public int queued_jobs { get; set; }
        public int max_jobs { get; set; }
        public int available_slots { get; set; }
        public int active_processes { get; set; }
    }

    private sealed class ScriptContentResponse
    {
        public string? Path { get; set; }
        public string? Content { get; set; }
    }
}

/// <summary>
/// The outcome of a catalog read. <see cref="Value"/> is null unless the service answered with a
/// success status, so an unavailable or refused read cannot be mistaken for an empty catalog.
/// </summary>
public sealed record OrchestratorRead<T>(int StatusCode, T? Value) where T : class;
public sealed record OrchestratorNotificationDispatchRequest(
    string SourceKind,
    string Title,
    string Text,
    string? Trigger = null,
    string? Status = null,
    string? JobName = null,
    string? AlertName = null,
    string? ReportId = null,
    long? HistoryId = null,
    long? RowsProcessed = null,
    string? RecipientOverride = null,
    string? ErrorMessage = null,
    IReadOnlyList<string>? AttachmentPaths = null);
