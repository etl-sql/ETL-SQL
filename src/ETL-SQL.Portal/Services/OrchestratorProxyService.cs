using System.Net.Http.Json;
using System.Security.Claims;
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
    /// <summary>
    /// Retired caller-controlled attribution header. Signed identity assertions now carry both
    /// attribution and authorization identity; retaining this constant only prevents source-level
    /// surprises for integrations while ensuring it is never emitted or trusted.
    /// </summary>
    [Obsolete("Use the signed X-Orchestrator-Identity assertion. This header has no authority and is no longer emitted.")]
    public const string ActorHeader = "X-Orchestrator-Actor";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.ApiUrl);

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

    public async Task<HttpResponseMessage?> DeleteJobAsync(string name, long version)
    {
        try { return await SendAsync(HttpMethod.Delete, $"api/scheduled-jobs/{Uri.EscapeDataString(name)}", version: version); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to delete job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
    }

    public async Task<List<JobHistoryEntryDto>> GetHistoryAsync(string jobName, int limit = 50)
    {
        try
        {
            return await GetJsonAsync<List<JobHistoryEntryDto>>(
                $"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/history?limit={limit}") ?? [];
        }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get history for {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return []; }
    }

    public async Task<HttpResponseMessage?> TriggerJobAsync(
        string name,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        try
        {
            return await SendAsync(
                HttpMethod.Post,
                $"api/scheduled-jobs/{Uri.EscapeDataString(name)}/trigger",
                new TriggerJobRequest(variables is null
                    ? null
                    : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to trigger job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name)); return null; }
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

    public async Task<List<ScheduleDefinitionDto>> GetSchedulesAsync(int limit = 1000, int offset = 0)
    {
        try { return await GetJsonAsync<List<ScheduleDefinitionDto>>($"api/schedules?limit={Math.Clamp(limit, 1, 1000)}&offset={Math.Max(0, offset)}") ?? []; }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list schedules."); return []; }
    }

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

    public async Task<List<JobScheduleLinkDto>> GetJobSchedulesAsync(string jobName)
    {
        try { return await GetJsonAsync<List<JobScheduleLinkDto>>($"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/schedules") ?? []; }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get schedules for job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return []; }
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

    public async Task<List<NotificationDefinitionDto>> GetNotificationsAsync(int limit = 1000, int offset = 0)
    {
        try { return await GetJsonAsync<List<NotificationDefinitionDto>>($"api/notifications?limit={Math.Clamp(limit, 1, 1000)}&offset={Math.Max(0, offset)}") ?? []; }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list notifications."); return []; }
    }

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

    public async Task<List<JobNotificationLinkDto>> GetJobNotificationsAsync(string jobName)
    {
        try { return await GetJsonAsync<List<JobNotificationLinkDto>>($"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/notifications") ?? []; }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get notifications for job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return []; }
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

    public async Task<List<JobStateEntryDto>> GetJobStatesAsync(string jobName)
    {
        try { return await GetJsonAsync<List<JobStateEntryDto>>($"api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/state") ?? []; }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get states for job {Name}.", ETL_SQL.Core.Common.LogSanitizer.Clean(jobName)); return []; }
    }

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

    public async Task<HttpResponseMessage?> GetBundleDependenciesResponseAsync(string name, int version, CancellationToken ct = default)
    {
        try { return await SendAsync(HttpMethod.Get, $"api/bundles/{Uri.EscapeDataString(name)}/versions/{version}/dependencies", cancellationToken: ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list dependencies for bundle {Name} v{Version}.", ETL_SQL.Core.Common.LogSanitizer.Clean(name), version); return null; }
    }

    public async Task<OrchestratorScriptsDto?> GetScriptsAsync()
    {
        try { return await GetJsonAsync<OrchestratorScriptsDto>("api/scripts"); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to list scripts."); return null; }
    }

    public async Task<string?> GetScriptContentAsync(string path)
    {
        try
        {
            var resp = await GetJsonAsync<ScriptContentResponse>($"api/scripts/content?path={Uri.EscapeDataString(path)}");
            return resp?.Content;
        }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get script content for {Path}.", ETL_SQL.Core.Common.LogSanitizer.Clean(path)); return null; }
    }

    public async Task<HttpResponseMessage?> StopServiceAsync()
    {
        try { return await SendAsync(HttpMethod.Post, "management/stop"); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to send stop signal."); return null; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<T?> GetJsonAsync<T>(string path)
    {
        using var resp = await SendAsync(HttpMethod.Get, path);
        if (resp == null || !resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>();
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        long? version = null,
        CancellationToken cancellationToken = default)
    {
        var url = settings.BuildUrl(path);
        if (url is null) return null;

        var req = new HttpRequestMessage(method, url);
        var key = settings.ApiKey;
        if (!string.IsNullOrEmpty(key))
            req.Headers.TryAddWithoutValidation("X-Orchestrator-Key", key);

        var identityAssertion = await CurrentIdentityAssertionAsync(cancellationToken);
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
    private async Task<string?> CurrentIdentityAssertionAsync(CancellationToken cancellationToken)
    {
        var issuer = assertionIssuer ?? new OrchestratorAssertionIssuer(portalConfig, portalDb);
        var user = httpContext?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return issuer.IssueForBackground();
        return (await issuer.IssueForAsync(user, cancellationToken))?.Assertion;
    }

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

        var url = settings.BuildUrl("api/platform/shared-tenants/lifecycle");
        if (url is null) return null;
        var secret = portalConfig?.Orchestrator.IdentitySigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Shared lifecycle requires Portal-to-Orchestrator identity assertion signing.");

        var assertion = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller(
                "platform", grant.OperatorPrincipal, grant.OperatorPrincipal,
                ["PlatformLifecycle"], [], platformTenant.Tenant.Value),
            secret, command.NowUtc);
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrEmpty(settings.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Orchestrator-Key", settings.ApiKey);
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
