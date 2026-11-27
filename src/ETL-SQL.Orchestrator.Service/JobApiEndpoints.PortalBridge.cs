using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Profiling;
using ETL_SQL.Engine.Scheduling;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Orchestrator.Service;

/// <summary>
/// HTTP bridge endpoints allowing the Portal to run Triage, OrchestratorPollerService,
/// SubscriptionsController, and governance connection/secret federation over HTTP without
/// opening a local SQLite file via <c>OrchestratorDbLocator</c>.
/// </summary>
public static partial class JobApiEndpoints
{
    private static void MapPortalBridgeEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Triage Run Detail ─────────────────────────────────────────────────
        app.MapGet("/api/history/{historyId:long}/detail", async (
            HttpContext ctx,
            long historyId,
            IJobHistoryStore store,
            OrchestratorObjectAuthorizationService authorization,
            IConfiguration cfg) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var history = await store.GetHistoryEntryAsync(historyId);
            // COMPAT_BREAK: 0.23 - a same-named job cannot authorize another tenant's run.
            if (history is null || (requestTenant is not null
                    && !string.Equals(history.TenantId, requestTenant, StringComparison.Ordinal)))
                return Results.NotFound(new { Error = $"Run '{historyId}' not found." });

            var caller = RequestCaller(ctx);
            // Authorize the durable job identity, not a same-named job in another tenant
            // or one recreated after the original job was deleted.
            var job = history.JobId.IsAssigned
                ? await store.GetJobByIdAsync(history.JobId)
                : await store.GetJobAsync(requestTenant, history.JobName);
            if (job is not null)
            {
                if (!await authorization.CanAsync(
                        caller, OrchestratorObjectKind.Job, job.Id, job.TenantId,
                        OrchestratorObjectPermission.Read, job.CreatedBy, ctx.RequestAborted))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }
            else if (!IsPortalSystemOrAdmin(caller)
                || (requestTenant is not null && !string.Equals(history.TenantId, requestTenant, StringComparison.Ordinal)))
            {
                return Results.NotFound(new { Error = $"Run '{historyId}' not found." });
            }

            var statements = await store.GetJobStatementMetricsAsync(historyId);
            var failures = await store.GetDataQualityFailuresForRunAsync(historyId, 1000);
            return Results.Ok(new TriageRunDetailApiResponse(history, statements, failures));
        }).WithName("getTriageRunDetail");

        // ── Poller Completions Feed ───────────────────────────────────────────
        app.MapGet("/api/history/completions", async (
            HttpContext ctx,
            IJobHistoryStore store,
            IConfiguration cfg,
            DateTime? sinceUtc = null,
            DateTime? untilUtc = null,
            int limit = 1000,
            int offset = 0) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var caller = RequestCaller(ctx);
            if (!IsPortalSystemOrAdmin(caller))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var boundedUntil = (untilUtc ?? DateTime.UtcNow).ToUniversalTime();
            var boundedSince = (sinceUtc ?? boundedUntil.AddMinutes(-5)).ToUniversalTime();
            // COMPAT_BREAK: 0.23 - tenant filtering precedes pagination and excludes legacy rows.
            var entries = (await store.GetCompletedHistoryAsync(
                boundedSince, boundedUntil, Math.Clamp(limit, 1, 1000), Math.Max(0, offset), requestTenant))
                .ToList();
            return Results.Ok(entries);
        }).WithName("getCompletedHistory");

        // ── Portal Subscription Orchestration Bridge ──────────────────────────
        app.MapPut("/api/portal/subscriptions/{subscriptionId:int}/job", async (
            HttpContext ctx,
            int subscriptionId,
            PortalSubscriptionJobSyncRequest req,
            IJobHistoryStore store,
            IJobCatalogStore catalog,
            IConfiguration cfg) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var caller = RequestCaller(ctx);
            if (!IsPortalSystemOrAdmin(caller))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (subscriptionId <= 0 || string.IsNullOrWhiteSpace(req.JobName) || string.IsNullOrWhiteSpace(req.Script))
                return Results.BadRequest(new { Error = "SubscriptionId, JobName, and Script are required." });

            var jobDef = new JobDefinition(
                Name: req.JobName,
                Script: req.Script,
                Interval: Math.Max(1, req.Interval),
                Unit: string.IsNullOrWhiteSpace(req.Unit) ? "DAY" : req.Unit.ToUpperInvariant(),
                AtTime: req.AtTime,
                LastRun: null,
                NextRun: null,
                IsEnabled: req.IsEnabled,
                MaxRetries: Math.Max(0, req.MaxRetries),
                RetryDelaySeconds: Math.Max(1, req.RetryDelaySeconds),
                CreatedBy: caller.PrincipalKey,
                ModifiedBy: ReadActor(ctx),
                TenantId: requestTenant);

            var existing = await store.GetJobAsync(requestTenant, req.JobName);
            if (existing is not null)
            {
                jobDef = existing with
                {
                    Script = req.Script,
                    Interval = jobDef.Interval,
                    Unit = jobDef.Unit,
                    AtTime = req.AtTime,
                    IsEnabled = req.IsEnabled,
                    ModifiedBy = ReadActor(ctx)
                };
            }

            await store.SaveJobAsync(jobDef);
            var saved = await store.GetJobAsync(requestTenant, req.JobName);
            if (saved is null || !saved.Id.IsAssigned)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (!string.IsNullOrWhiteSpace(req.ScheduleName) && !string.IsNullOrWhiteSpace(req.Cron))
            {
                var tz = string.IsNullOrWhiteSpace(req.TimeZone) ? "UTC" : req.TimeZone;
                var schedule = new ScheduleDefinition(
                    Name: req.ScheduleName,
                    Cron: req.Cron,
                    TimeZone: tz,
                    IsEnabled: req.IsEnabled,
                    DisplayName: $"Subscription {subscriptionId}",
                    Description: "Portal subscription trigger schedule",
                    TenantId: requestTenant);
                await catalog.SaveScheduleAsync(schedule);
                var added = await JobScheduleAttachment.AttachAsync(catalog, saved.Id, requestTenant, schedule.Name);
                if (!added && req.RearmExisting)
                {
                    var nextRun = CronSchedule.GetNextOccurrence(schedule.Cron, schedule.TimeZone, DateTimeOffset.UtcNow);
                    var storedSched = await catalog.GetScheduleAsync(requestTenant, schedule.Name);
                    if (storedSched is not null && storedSched.Id.IsAssigned)
                        await catalog.ArmJobScheduleAsync(saved.Id, storedSched.Id, nextRun);
                }
            }

            if (!string.IsNullOrWhiteSpace(req.NotificationName) && !string.IsNullOrWhiteSpace(req.ConnectionName))
            {
                var notification = new NotificationDefinition(
                    Name: req.NotificationName,
                    ConnectionName: req.ConnectionName,
                    Recipient: req.Recipient,
                    IsEnabled: req.IsEnabled,
                    DisplayName: $"Subscription {subscriptionId} notification",
                    Description: "Portal subscription delivery destination",
                    TenantId: requestTenant);
                await catalog.SaveNotificationAsync(notification);
                var storedNotif = await catalog.GetNotificationAsync(requestTenant, notification.Name);
                if (storedNotif is not null && storedNotif.Id.IsAssigned)
                    await catalog.AddJobNotificationAsync(saved.Id, storedNotif.Id, NotificationTrigger.Failure);
            }

            EmitObjectMutationAudit(
                caller, $"JOB:{saved.Name}", existing is null ? "CREATE_JOB" : "ALTER_JOB",
                $"Subscription job '{saved.Name}' synchronized via Portal bridge.", ctx.TraceIdentifier);
            return Results.Ok(new PortalSubscriptionJobSyncResponse(saved.Id, saved.Name));
        }).WithName("syncPortalSubscriptionJob");

        app.MapDelete("/api/portal/subscriptions/{subscriptionId:int}/job", async (
            HttpContext ctx,
            int subscriptionId,
            [FromQuery] string? jobName,
            IJobHistoryStore store,
            IJobCatalogStore catalog,
            IConfiguration cfg) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var caller = RequestCaller(ctx);
            if (!IsPortalSystemOrAdmin(caller))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (!string.IsNullOrWhiteSpace(jobName))
            {
                var job = await store.GetJobAsync(requestTenant, jobName);
                if (job is not null && job.Id.IsAssigned)
                    await store.DeleteJobAsync(job.Id);
            }

            var sched = await catalog.GetScheduleAsync(requestTenant, $"SUBSCHED:{subscriptionId}");
            if (sched is not null && sched.Id.IsAssigned)
                await catalog.DeleteScheduleAsync(sched.Id);

            var notif = await catalog.GetNotificationAsync(requestTenant, $"SUBNOTIFY:{subscriptionId}");
            if (notif is not null && notif.Id.IsAssigned)
                await catalog.DeleteNotificationAsync(notif.Id);

            return Results.NoContent();
        }).WithName("deletePortalSubscriptionJob");

        app.MapGet("/api/portal/subscriptions/{subscriptionId:int}/history", async (
            HttpContext ctx,
            int subscriptionId,
            [FromQuery] string jobName,
            IJobHistoryStore store,
            IConfiguration cfg,
            int limit = 50) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var caller = RequestCaller(ctx);
            if (!IsPortalSystemOrAdmin(caller))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(jobName)
                || !jobName.StartsWith($"SUB:{subscriptionId}:", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { Error = "JobName must match the requested subscription." });
            }

            var rows = (await store.GetHistoryForNameAsync(
                requestTenant, jobName, Math.Clamp(limit, 1, 1000))).ToList();
            return Results.Ok(rows);
        }).WithName("getPortalSubscriptionHistory");

        // ── Remote Secret Store Administration (Federation) ───────────────────
        app.MapGet("/api/admin/secrets", async (
            HttpContext ctx, IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (ConnectionAdministrationDenied(ctx, cfg)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (services.GetService<ISecretProvider>() is not ISecretLifecycleProvider lifecycle)
                return Results.NotFound(new { Error = "No enumerable secret store is configured." });

            var items = await lifecycle.ListAsync(ct);
            return Results.Ok(items.Select(item => new SecretCatalogSummaryResponse(
                item.Name, item.Status == SecretLifecycleStatus.Disabled)).ToList());
        }).WithName("listAdminSecrets");

        app.MapPut("/api/admin/secrets/{name}", async (
            HttpContext ctx, string name, SetAdminSecretRequest req,
            IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (ConnectionAdministrationDenied(ctx, cfg)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (services.GetService<ISecretProvider>() is not IWritableSecretProvider writable)
                return Results.NotFound(new { Error = "No writable secret store is configured." });
            if (string.IsNullOrEmpty(req?.Value))
                return Results.BadRequest(new { Error = "A non-empty secret value is required." });

            var decodedName = Uri.UnescapeDataString(name);
            try
            {
                SecretNameValidator.Validate(decodedName);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }

            var existed = writable is ISecretLifecycleProvider lifecycle
                && await lifecycle.GetStatusAsync(decodedName, ct) != SecretLifecycleStatus.NotFound;
            await writable.StoreAsync(decodedName, req.Value, ct);
            EmitConnectionAudit(RequestActor(ctx), existed ? "SECRET_ROTATE" : "SECRET_SET", decodedName, "Status=active");
            return Results.NoContent();
        }).WithName("setAdminSecret");

        app.MapPost("/api/admin/secrets/{name}/disable", async (
            HttpContext ctx, string name, IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (ConnectionAdministrationDenied(ctx, cfg)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (services.GetService<ISecretProvider>() is not ISecretLifecycleProvider lifecycle)
                return Results.NotFound(new { Error = "No lifecycle secret store is configured." });

            var decodedName = Uri.UnescapeDataString(name);
            if (await lifecycle.GetStatusAsync(decodedName, ct) == SecretLifecycleStatus.NotFound)
                return Results.NotFound(new { Error = $"Secret '{decodedName}' was not found." });

            await lifecycle.DisableAsync(decodedName, ct);
            EmitConnectionAudit(RequestActor(ctx), "SECRET_DISABLE", decodedName, "Status=disabled");
            return Results.NoContent();
        }).WithName("disableAdminSecret");

        app.MapPost("/api/admin/secrets/{name}/enable", async (
            HttpContext ctx, string name, IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (ConnectionAdministrationDenied(ctx, cfg)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (services.GetService<ISecretProvider>() is not ISecretLifecycleProvider lifecycle)
                return Results.NotFound(new { Error = "No lifecycle secret store is configured." });

            var decodedName = Uri.UnescapeDataString(name);
            if (await lifecycle.GetStatusAsync(decodedName, ct) == SecretLifecycleStatus.NotFound)
                return Results.NotFound(new { Error = $"Secret '{decodedName}' was not found." });

            await lifecycle.EnableAsync(decodedName, ct);
            EmitConnectionAudit(RequestActor(ctx), "SECRET_ENABLE", decodedName, "Status=active");
            return Results.NoContent();
        }).WithName("enableAdminSecret");

        app.MapDelete("/api/admin/secrets/{name}", async (
            HttpContext ctx, string name, IServiceProvider services, IConfiguration cfg, CancellationToken ct) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (ConnectionAdministrationDenied(ctx, cfg)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (services.GetService<ISecretProvider>() is not ISecretLifecycleProvider lifecycle)
                return Results.NotFound(new { Error = "No lifecycle secret store is configured." });

            var decodedName = Uri.UnescapeDataString(name);
            if (await lifecycle.GetStatusAsync(decodedName, ct) == SecretLifecycleStatus.NotFound)
                return Results.NotFound(new { Error = $"Secret '{decodedName}' was not found." });

            await lifecycle.DeleteAsync(decodedName, ct);
            EmitConnectionAudit(RequestActor(ctx), "SECRET_DELETE", decodedName, "Status=deleted");
            return Results.NoContent();
        }).WithName("deleteAdminSecret");

        // ── Nodes & Sandbox Fleet Health ──────────────────────────────────────
        app.MapGet("/api/fleet/nodes-sandboxes", async (
            HttpContext ctx,
            IServiceProvider services,
            IConfiguration cfg,
            CancellationToken ct) =>
        {
            if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
            if (!TryResolveRequestTenant(ctx, cfg, out var requestTenant))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (BundleReadDenied(ctx, cfg))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var utcNow = DateTime.UtcNow;
            var caller = RequestCaller(ctx);
            var canViewAllTenants = string.IsNullOrWhiteSpace(requestTenant)
                || IsPortalSystemOrAdmin(caller)
                || caller.IsInRole("FleetReader");

            var nodeStore = services.GetService<INodeRegistryStore>();
            var rawNodes = nodeStore is not null
                ? await nodeStore.GetAllNodesAsync()
                : [];

            var nodes = rawNodes
                .OrderByDescending(n => n.ExpiresAtUtc > utcNow)
                .ThenBy(n => n.Role, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.NodeId, StringComparer.OrdinalIgnoreCase)
                .Select(n => ParseNodeHeartbeat(n, utcNow, includeHostDetails: canViewAllTenants))
                .ToList();

            var registeredOptions = services.GetService<SandboxAdmissionHostOptions>();
            var section = cfg.GetSection("Orchestration:SandboxAdmission");
            var admissionEnabled = registeredOptions?.Enabled ?? section.GetValue("Enabled", false);
            var poolCapacities = registeredOptions?.PoolCapacities
                ?? section.GetSection("PoolCapacities").GetChildren().ToDictionary(
                    child => child.Key,
                    child => int.TryParse(child.Value, out var capacity) ? capacity : 0,
                    StringComparer.Ordinal);
            var ledger = services.GetService<ISandboxAdmissionLedger>();

            var pools = new List<SandboxPoolStateApiResponse>();
            foreach (var (poolId, capacity) in poolCapacities.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var open = ledger is not null
                    ? await ledger.ListOpenAsync(poolId, ct)
                    : [];
                // Free slots are pool-wide: a tenant viewer needs the true figure to know whether a
                // queued run can start, and it names no other tenant. Counts and entries are scoped.
                var occupied = open.Count(e => e.State is SandboxAdmissionState.Active or SandboxAdmissionState.Retained);
                var scopedOpen = canViewAllTenants
                    ? open
                    : open.Where(e => string.Equals(e.TenantId, requestTenant, StringComparison.Ordinal)).ToList();
                var active = scopedOpen.Count(e => e.State == SandboxAdmissionState.Active);
                var retained = scopedOpen.Count(e => e.State == SandboxAdmissionState.Retained);
                var queued = scopedOpen.Count(e => e.State == SandboxAdmissionState.Queued);
                var visibleEntries = scopedOpen
                    .Take(50)
                    .Select(e => new SandboxPoolAdmissionApiResponse(
                        e.AdmissionId,
                        e.PoolId,
                        e.TenantId,
                        e.State.ToString(),
                        e.TenantWeight,
                        canViewAllTenants ? e.LeaseOwner : OpaqueNodeLabel(e.LeaseOwner),
                        e.EnqueuedUtc,
                        e.LeaseExpiresUtc))
                    .ToList();

                pools.Add(new SandboxPoolStateApiResponse(
                    poolId,
                    capacity,
                    queued,
                    active,
                    retained,
                    Math.Max(0, capacity - occupied),
                    visibleEntries));
            }

            return Results.Ok(new OrchestratorFleetSnapshotApiResponse(
                utcNow,
                admissionEnabled,
                nodes,
                pools));
        }).WithName("getFleetNodesAndSandboxes");
    }

    /// <summary>
    /// Node ids and lease owners are <c>machine:pid:guid</c>. Tenant-bound readers get a stable
    /// opaque label instead, so rows stay distinguishable without naming the host or process.
    /// </summary>
    private static string? OpaqueNodeLabel(string? nodeId) =>
        string.IsNullOrEmpty(nodeId)
            ? nodeId
            : "node-" + Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nodeId)))[..12];

    private static FleetNodeHeartbeatApiResponse ParseNodeHeartbeat(
        NodeHeartbeat node,
        DateTime utcNow,
        bool includeHostDetails)
    {
        string? machine = null;
        int? pid = null;
        DateTimeOffset? startedUtc = null;
        FleetNodeCapacityApiResponse? capacity = null;

        if (!string.IsNullOrWhiteSpace(node.Metadata))
        {
            try
            {
                using var doc = JsonDocument.Parse(node.Metadata);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (includeHostDetails && TryGetCaseInsensitive(root, "machine", out var m) && m.ValueKind == JsonValueKind.String)
                        machine = m.GetString();
                    if (includeHostDetails && TryGetCaseInsensitive(root, "pid", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pidVal))
                        pid = pidVal;
                    if (TryGetCaseInsensitive(root, "startedUtc", out var s) && s.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(s.GetString(), out var parsedStarted))
                        startedUtc = parsedStarted;
                    if (includeHostDetails && TryGetCaseInsensitive(root, "capacity", out var cap) && cap.ValueKind == JsonValueKind.Object)
                    {
                        capacity = new FleetNodeCapacityApiResponse(
                            TryGetInt64(cap, "WorkingSetBytes"),
                            TryGetInt64(cap, "GcHeapBytes"),
                            TryGetInt64(cap, "TotalAvailableMemoryBytes"),
                            TryGetDouble(cap, "MemoryLoadPercent"),
                            TryGetDouble(cap, "ProcessCpuPercent"),
                            TryGetDouble(cap, "HostCpuPercent"),
                            TryGetInt32(cap, "ProcessorCount"),
                            TryGetBool(cap, "IsOverloaded"),
                            TryGetNullableInt64(cap, "StateDiskFreeBytes"),
                            TryGetNullableInt64(cap, "SpillDiskFreeBytes"));
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore unparseable node metadata rather than failing the fleet view.
            }
        }

        return new FleetNodeHeartbeatApiResponse(
            includeHostDetails ? node.NodeId : OpaqueNodeLabel(node.NodeId)!,
            node.Role,
            node.ExpiresAtUtc > utcNow,
            node.FirstSeenUtc,
            node.LastHeartbeatUtc,
            node.ExpiresAtUtc,
            machine,
            pid,
            startedUtc,
            capacity);
    }

    private static bool TryGetCaseInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static long TryGetInt64(JsonElement obj, string name) =>
        TryGetCaseInsensitive(obj, name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var val) ? val : 0L;

    private static long? TryGetNullableInt64(JsonElement obj, string name) =>
        TryGetCaseInsensitive(obj, name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var val) ? val : null;

    private static int TryGetInt32(JsonElement obj, string name) =>
        TryGetCaseInsensitive(obj, name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val) ? val : 0;

    private static double TryGetDouble(JsonElement obj, string name) =>
        TryGetCaseInsensitive(obj, name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var val) ? val : 0d;

    private static bool TryGetBool(JsonElement obj, string name) =>
        TryGetCaseInsensitive(obj, name, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False) && prop.GetBoolean();

    private static bool IsPortalSystemOrAdmin(OrchestratorCaller caller) =>
        caller.IsInRole("PortalSystem")
        || IsObjectAdministrator(caller)
        || (caller.SubjectType.Equals("service", StringComparison.OrdinalIgnoreCase)
            && caller.SubjectId.Equals("legacy-api-key", StringComparison.OrdinalIgnoreCase));

    public sealed record TriageRunDetailApiResponse(
        JobHistoryEntry Run,
        IReadOnlyList<StatementMetricsPayload> Statements,
        IReadOnlyList<JobDataQualityFailure> QualityFailures);

    public sealed record PortalSubscriptionJobSyncRequest(
        string JobName,
        string Script,
        int Interval,
        string Unit,
        string? AtTime,
        bool IsEnabled,
        int MaxRetries,
        int RetryDelaySeconds,
        string? ScheduleName,
        string? Cron,
        string? TimeZone,
        bool RearmExisting,
        string? NotificationName,
        string? ConnectionName,
        string? Recipient);

    public sealed record PortalSubscriptionJobSyncResponse(JobId JobId, string JobName);

    public sealed record SetAdminSecretRequest(string Value);

    public sealed record SecretCatalogSummaryResponse(string Name, bool Disabled);

    public sealed record FleetNodeCapacityApiResponse(
        long WorkingSetBytes,
        long GcHeapBytes,
        long TotalAvailableMemoryBytes,
        double MemoryLoadPercent,
        double ProcessCpuPercent,
        double HostCpuPercent,
        int ProcessorCount,
        bool IsOverloaded,
        long? StateDiskFreeBytes,
        long? SpillDiskFreeBytes);

    public sealed record FleetNodeHeartbeatApiResponse(
        string NodeId,
        string Role,
        bool IsLive,
        DateTime FirstSeenUtc,
        DateTime LastHeartbeatUtc,
        DateTime ExpiresAtUtc,
        string? Machine,
        int? Pid,
        DateTimeOffset? StartedUtc,
        FleetNodeCapacityApiResponse? Capacity);

    public sealed record SandboxPoolAdmissionApiResponse(
        string AdmissionId,
        string PoolId,
        string TenantId,
        string State,
        int Weight,
        string? LeaseOwner,
        DateTimeOffset EnqueuedUtc,
        DateTimeOffset? LeaseExpiresUtc);

    public sealed record SandboxPoolStateApiResponse(
        string PoolId,
        int Capacity,
        int QueuedCount,
        int ActiveCount,
        int RetainedCount,
        int AvailableSlots,
        IReadOnlyList<SandboxPoolAdmissionApiResponse> OpenAdmissions);

    public sealed record OrchestratorFleetSnapshotApiResponse(
        DateTime GeneratedAtUtc,
        bool SandboxAdmissionEnabled,
        IReadOnlyList<FleetNodeHeartbeatApiResponse> Nodes,
        IReadOnlyList<SandboxPoolStateApiResponse> SandboxPools);
}
