using System.Net.Http.Json;
using System.Security.Claims;
using ETL_SQL.Core.Governance;
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
public class OrchestratorProxyService(
    HttpClient http,
    OrchestratorSettingsService settings,
    ILogger<OrchestratorProxyService> logger,
    // Optional: background work (alert evaluation, subscription delivery) has no HTTP context and
    // therefore no human to attribute, which is a legitimate state rather than a missing dependency.
    IHttpContextAccessor? httpContext = null,
    PortalConfig? portalConfig = null,
    PortalDbContext? portalDb = null)
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

    private async Task<string?> CurrentIdentityAssertionAsync(CancellationToken cancellationToken)
    {
        var secret = portalConfig?.Orchestrator.IdentitySigningSecret;
        if (string.IsNullOrWhiteSpace(secret)) return null;

        var user = httpContext?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return OrchestratorIdentityAssertion.Create(
                new OrchestratorCaller("service", "portal-background", "Portal background service", ["PortalSystem"], []),
                secret);
        }

        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity.Name;
        var serviceAccountId = user.FindFirstValue(TokenService.ServiceAccountIdClaim);
        var isService = string.Equals(
            user.FindFirstValue(TokenService.IdentityTypeClaim),
            TokenService.ServiceIdentityType,
            StringComparison.Ordinal);
        var subjectId = isService ? serviceAccountId : id;
        if (string.IsNullOrWhiteSpace(subjectId)) return null;

        string[] groupIds = [];
        if (!isService && int.TryParse(id, out var userId) && portalDb is not null)
        {
            groupIds = await portalDb.UserGroups.AsNoTracking()
                .Where(membership => membership.UserId == userId)
                .Select(membership => membership.GroupId.ToString())
                .ToArrayAsync(cancellationToken);
        }

        var roles = user.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller(
                isService ? "service" : "user",
                subjectId,
                name ?? subjectId,
                roles,
                groupIds),
            secret);
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
