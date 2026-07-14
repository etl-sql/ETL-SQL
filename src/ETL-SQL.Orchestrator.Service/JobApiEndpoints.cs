using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Observability;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Channels;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Reporting;
using ETL_SQL.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>
    /// Minimal-API endpoints exposed by the Orchestrator Service over HTTP.
    ///
    /// Ad-hoc execution routes (all require X-Orchestrator-Key header):
    ///   POST   /jobs          — submit a script for ad-hoc execution
    ///   DELETE /jobs/{id}     — cancel a running or queued ad-hoc job
    ///   GET    /jobs/{id}     — get the status of an ad-hoc job
    ///   GET    /health        — liveness probe (always 200 OK, no auth)
    ///   GET    /metrics       — concurrency metrics (no auth)
    ///
    /// Scheduled job management (all require X-Orchestrator-Key header):
    ///   GET    /api/scheduled-jobs              — list all jobs (enabled + disabled)
    ///   POST   /api/scheduled-jobs              — create a job
    ///   PUT    /api/scheduled-jobs/{name}       — update a job (enable/disable/reschedule)
    ///   DELETE /api/scheduled-jobs/{name}       — delete job and its history
    ///   GET    /api/scheduled-jobs/{name}/history — execution history for a job
    ///   POST   /api/scheduled-jobs/{name}/trigger — trigger an immediate out-of-schedule run
    ///   POST   /api/scheduled-jobs/{name}/kill    — cancel the currently running instance
    ///   GET    /api/scripts                     — list .etlsql files under ScriptRoot
    ///   GET    /api/scripts/content             — read script file content (?path=relative)
    ///
    /// Service management (require X-Orchestrator-Key header):
    ///   GET    /management/status  — uptime, version, process info
    ///   POST   /management/stop    — graceful stop (OS supervisor restarts)
    /// </summary>
    public static class JobApiEndpoints
    {
        private static readonly ConcurrentDictionary<string, JobEntry> _jobs = new();
        private static readonly DateTime _startTime = DateTime.UtcNow;

        public static void MapJobApi(this IEndpointRouteBuilder app)
        {
            // ── Health & Metrics (no auth required — liveness/readiness probes) ─
            app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }))
               .WithName("health");

            app.MapGet("/metrics", (SchedulerService scheduler, ChildProcessTracker tracker) =>
            {
                var m = scheduler.GetMetrics();
                return Results.Ok(new
                {
                    active_jobs = m.ActiveJobs,
                    queued_jobs = m.QueuedJobs,
                    max_jobs = m.MaxJobs,
                    available_slots = m.AvailableSlots,
                    active_processes = tracker.ActiveCount
                });
            }).WithName("getMetrics");

            app.MapGet("/metrics/prometheus", async (
                SchedulerService scheduler,
                ChildProcessTracker tracker,
                IJobHistoryStore historyStore,
                IServiceProvider services) =>
            {
                var m = scheduler.GetMetrics();
                return Results.Text(
                    await BuildPrometheusMetricsAsync(
                        m,
                        tracker.ActiveCount,
                        historyStore,
                        services.GetService<IHostMetricsStore>()),
                    "text/plain; version=0.0.4; charset=utf-8");
            }).WithName("getPrometheusMetrics");

            // ── Ad-hoc job execution (authenticated — see ApiKeyDenied) ──────────
            app.MapPost("/jobs", (HttpContext ctx, IConfiguration cfg, JobSubmitRequest request, IServiceScopeFactory scopeFactory, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var jobId = Guid.NewGuid().ToString("N")[..8];
                var cts = new CancellationTokenSource();
                var entry = new JobEntry(jobId, cts, ctx.TraceIdentifier);
                _jobs[jobId] = entry;

                logger.LogInformation("Job {JobId} submitted (label={Label})", jobId, ETL_SQL.Core.Common.LogSanitizer.Clean(request.Label));
                _ = RunJobAsync(entry, request, scopeFactory, logger, cts.Token);

                return Results.Accepted($"/jobs/{jobId}", new { JobId = jobId });
            }).WithName("submitJob");

            app.MapDelete("/jobs/{id}", (string id, HttpContext ctx, IConfiguration cfg, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });

                logger.LogInformation("Cancelling job {JobId}", ETL_SQL.Core.Common.LogSanitizer.Clean(id));
                entry.Cts.Cancel();
                entry.Status = JobRunStatus.Cancelled;
                return Results.Ok(new { JobId = id, Status = "Cancelled" });
            }).WithName("cancelJob");

            app.MapGet("/jobs/{id}", (string id, HttpContext ctx, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });

                var includeSensitivePayload = ApiKeyAcceptedForSensitivePayload(ctx, cfg);
                return Results.Ok(new JobStatusResponse
                {
                    JobId = entry.JobId,
                    Status = entry.Status,
                    RowsProcessed = entry.RowsProcessed,
                    ExecutionTimeMs = entry.ExecutionTimeMs,
                    PeakMemoryBytes = entry.PeakMemoryBytes,
                    CpuTimeSeconds = entry.CpuTimeSeconds,
                    ErrorMessage = entry.ErrorMessage,
                    ReportManifestJson = includeSensitivePayload ? entry.ReportManifestJson : null
                });
            }).WithName("getJobStatus");

            // ── Scheduled job management ──────────────────────────────────────

            app.MapGet("/api/scheduled-jobs", async (HttpContext ctx, IJobHistoryStore store, IConfiguration cfg,
                int limit = 100, int offset = 0) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var jobs = await store.GetJobsPageAsync(limit, offset);
                return Results.Ok(jobs);
            }).WithName("listScheduledJobs");

            app.MapPost("/api/scheduled-jobs", async (HttpContext ctx, CreateScheduledJobRequest req,
                IJobHistoryStore store, IBundleStore bundleStore, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(req.Name))
                    return Results.BadRequest(new { Error = "Name is required." });
                if (string.IsNullOrWhiteSpace(req.ScriptText))
                    return Results.BadRequest(new { Error = "ScriptText is required." });
                if (req.Interval <= 0)
                    return Results.BadRequest(new { Error = "Interval must be positive." });

                var validUnits = new[] { "SECOND", "MINUTE", "HOUR", "DAY", "WEEK", "MONTH" };
                if (!validUnits.Contains((req.Unit ?? "").ToUpperInvariant()))
                    return Results.BadRequest(new { Error = $"Unit must be one of: {string.Join(", ", validUnits)}" });

                var scriptText = await PinBundlePathsAsync(req.ScriptText, bundleStore);
                var job = new JobDefinition(
                    req.Name,
                    scriptText,
                    req.Interval,
                    (req.Unit ?? "HOUR").ToUpperInvariant(),
                    req.AtTime,
                    null, null,
                    true,
                    req.MaxRetries,
                    req.RetryDelaySeconds,
                    null,
                    req.HashPolicy ?? "Warn"
                );

                await store.SaveJobAsync(job);
                return Results.Created($"/api/scheduled-jobs/{Uri.EscapeDataString(req.Name)}", job);
            }).WithName("createScheduledJob");

            app.MapPut("/api/scheduled-jobs/{name}", async (HttpContext ctx, string name,
                UpdateScheduledJobRequest req, IJobHistoryStore store, IBundleStore bundleStore, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var expectedVersion = ReadExpectedVersion(ctx);
                if (expectedVersion is null)
                    return Results.Json(
                        new { Error = "If-Match with the current job version is required." },
                        statusCode: StatusCodes.Status428PreconditionRequired);

                var existing = await store.GetJobAsync(Uri.UnescapeDataString(name));
                if (existing == null)
                    return Results.NotFound(new { Error = $"Job '{name}' not found." });

                var pinnedScript = req.ScriptText != null ? await PinBundlePathsAsync(req.ScriptText, bundleStore) : null;
                var updated = existing with
                {
                    Script = pinnedScript ?? existing.Script,
                    Interval = req.Interval ?? existing.Interval,
                    Unit = req.Unit != null ? req.Unit.ToUpperInvariant() : existing.Unit,
                    AtTime = req.AtTime ?? existing.AtTime,
                    IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                    MaxRetries = req.MaxRetries ?? existing.MaxRetries,
                    RetryDelaySeconds = req.RetryDelaySeconds ?? existing.RetryDelaySeconds,
                    HashPolicy = req.HashPolicy ?? existing.HashPolicy
                };

                if (!await store.TrySaveJobAsync(updated, expectedVersion.Value))
                {
                    var current = await store.GetJobAsync(existing.Name);
                    return Results.Conflict(new
                    {
                        Error = "The job changed after it was read. Refresh it and retry.",
                        Current = current
                    });
                }
                return Results.Ok(await store.GetJobAsync(existing.Name));
            }).WithName("updateScheduledJob");

            app.MapDelete("/api/scheduled-jobs/{name}", async (HttpContext ctx, string name,
                IJobHistoryStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var expectedVersion = ReadExpectedVersion(ctx);
                if (expectedVersion is null)
                    return Results.Json(
                        new { Error = "If-Match with the current job version is required." },
                        statusCode: StatusCodes.Status428PreconditionRequired);

                var unescaped = Uri.UnescapeDataString(name);
                if (await store.GetJobAsync(unescaped) is null)
                    return Results.NotFound(new { Error = $"Job '{name}' not found." });

                if (!await store.TryDeleteJobAsync(unescaped, expectedVersion.Value))
                {
                    var current = await store.GetJobAsync(unescaped);
                    return Results.Conflict(new
                    {
                        Error = "The job changed after it was read. Refresh it and retry.",
                        Current = current
                    });
                }
                return Results.Ok(new { Deleted = unescaped });
            }).WithName("deleteScheduledJob");

            app.MapGet("/api/scheduled-jobs/{name}/history", async (HttpContext ctx, string name,
                IJobHistoryStore store, IConfiguration cfg, int limit = 50) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var history = await store.GetHistoryAsync(Uri.UnescapeDataString(name), Math.Clamp(limit, 1, 1000));
                return Results.Ok(history);
            }).WithName("getScheduledJobHistory");

            app.MapGet("/api/history", async (HttpContext ctx, IJobHistoryStore store,
                IConfiguration cfg, string? jobName = null, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var history = await store.GetHistoryAsync(jobName, Math.Clamp(limit, 1, 1000));
                return Results.Ok(history);
            }).WithName("getAllJobHistory");

            app.MapGet("/api/lineage/history/table/{name}", async (HttpContext ctx, string name,
                ILineageCatalogStore catalog, IConfiguration cfg, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var entries = await catalog.GetHistoryForTableAsync(Uri.UnescapeDataString(name), limit);
                return Results.Ok(entries);
            }).WithName("getLineageHistoryForTable");

            app.MapGet("/api/lineage/history/tag/{key}", async (HttpContext ctx, string key,
                ILineageCatalogStore catalog, IConfiguration cfg, string? value = null, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var entries = await catalog.GetHistoryForTagAsync(Uri.UnescapeDataString(key), value, limit);
                return Results.Ok(entries);
            }).WithName("getLineageHistoryForTag");

            app.MapPost("/api/scheduled-jobs/{name}/trigger", async (HttpContext ctx, string name,
                SchedulerService scheduler, IJobHistoryStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var unescaped = Uri.UnescapeDataString(name);
                var triggered = await scheduler.TriggerJobAsync(unescaped);
                if (!triggered)
                    return Results.NotFound(new { Error = $"Job '{name}' not found." });

                return Results.Accepted(uri: (string?)null, value: new { Message = $"Job '{unescaped}' queued for immediate execution." });
            }).WithName("triggerScheduledJob");

            app.MapPost("/api/scheduled-jobs/{name}/kill", async (HttpContext ctx, string name,
                IJobManager jobManager, IJobHistoryStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var unescaped = Uri.UnescapeDataString(name);
                var history = await store.GetHistoryAsync(unescaped, 10);
                var running = history.FirstOrDefault(h => h.Status == "RUNNING" && h.EndTime == null);
                if (running == null)
                    return Results.NotFound(new { Error = $"No running instance of job '{name}' found." });

                var killed = jobManager.KillJob(running.Id);
                return killed
                    ? Results.Ok(new { Message = $"Job '{unescaped}' (historyId={running.Id}) kill signal sent." })
                    : Results.Problem($"Kill signal for history entry {running.Id} did not match a running job.");
            }).WithName("killScheduledJob");

            // ── Script browser ────────────────────────────────────────────────

            app.MapGet("/api/scripts", (HttpContext ctx, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var root = GetScriptRoot(cfg);
                if (!Directory.Exists(root))
                    return Results.Ok(new { Root = root, Files = Array.Empty<string>() });

                var files = Directory.EnumerateFiles(root, "*.etlsql", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                    .OrderBy(f => f)
                    .ToArray();

                return Results.Ok(new { Root = root, Files = files });
            }).WithName("listScripts");

            app.MapGet("/api/scripts/content", async (HttpContext ctx, IConfiguration cfg, string path) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (string.IsNullOrWhiteSpace(path))
                    return Results.BadRequest(new { Error = "path query parameter is required." });

                var fullRoot = Path.GetFullPath(GetScriptRoot(cfg));
                if (!Directory.Exists(fullRoot))
                    return Results.NotFound(new { Error = $"Script '{path}' not found." });

                // Allowlist: only an actual *.etlsql script under the configured root may be read. The
                // request value is used solely to select a match; the path handed to the reader comes
                // from the directory enumeration, never directly from the request. This both prevents
                // traversal outside the root and reading non-script files.
                var requested = Path.GetFullPath(Path.Combine(fullRoot, path));
                var match = Directory.EnumerateFiles(fullRoot, "*.etlsql", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(Path.GetFullPath(f), requested, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return Results.NotFound(new { Error = $"Script '{path}' not found." });

                var content = await File.ReadAllTextAsync(match);
                return Results.Ok(new { Path = path, Content = content });
            }).WithName("getScriptContent");

            // ── Published bundle lockbox ───────────────────────────────────

            app.MapGet("/api/bundles", async (HttpContext ctx, IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                return Results.Ok(await store.GetBundlesAsync());
            }).WithName("listBundles");

            app.MapPost("/api/bundles", async (HttpContext ctx, PublishBundleApiRequest req,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (req.Bundle == null)
                    return Results.BadRequest(new { Error = "Bundle is required." });
                if (string.IsNullOrWhiteSpace(req.Bundle.BundleName))
                    return Results.BadRequest(new { Error = "BundleName is required." });
                if (string.IsNullOrWhiteSpace(req.Bundle.EntryPath))
                    return Results.BadRequest(new { Error = "EntryPath is required." });
                if (req.Bundle.Files == null || req.Bundle.Files.Count == 0)
                    return Results.BadRequest(new { Error = "At least one file is required." });

                var reencrypted = BundlePublishSupport.ReEncryptRequest(req.Bundle, req.Password, SecurityService.GetMachineKey());
                var version = await store.PublishBundleAsync(reencrypted);
                return Results.Ok(version);
            }).WithName("publishBundle");

            app.MapGet("/api/bundles/{name}/versions", async (HttpContext ctx, string name,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                return Results.Ok(await store.GetVersionsAsync(Uri.UnescapeDataString(name)));
            }).WithName("listBundleVersions");

            app.MapGet("/api/bundles/{name}/versions/{version:int}", async (HttpContext ctx, string name, int version,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var result = await store.GetVersionAsync(Uri.UnescapeDataString(name), version);
                return result == null ? Results.NotFound(new { Error = $"Bundle '{name}' version {version} not found." }) : Results.Ok(result);
            }).WithName("getBundleVersion");

            app.MapGet("/api/bundles/{name}/versions/{version:int}/files", async (HttpContext ctx, string name, int version,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (await store.GetVersionAsync(Uri.UnescapeDataString(name), version) == null)
                    return Results.NotFound(new { Error = $"Bundle '{name}' version {version} not found." });
                return Results.Ok(await store.GetFilesAsync(Uri.UnescapeDataString(name), version));
            }).WithName("listBundleFiles");

            app.MapGet("/api/bundles/{name}/versions/{version:int}/dependencies", async (HttpContext ctx, string name, int version,
                IBundleStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                if (await store.GetVersionAsync(Uri.UnescapeDataString(name), version) == null)
                    return Results.NotFound(new { Error = $"Bundle '{name}' version {version} not found." });
                return Results.Ok(await store.GetDependenciesAsync(Uri.UnescapeDataString(name), version));
            }).WithName("listBundleDependencies");

            // ── Service management ────────────────────────────────────────────

            app.MapGet("/management/status", (HttpContext ctx, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var proc = Process.GetCurrentProcess();
                return Results.Ok(new
                {
                    Status = "Running",
                    UptimeSeconds = (DateTime.UtcNow - _startTime).TotalSeconds,
                    ProcessId = proc.Id,
                    StartedAt = _startTime,
                    Version = typeof(JobApiEndpoints).Assembly.GetName().Version?.ToString() ?? "unknown"
                });
            }).WithName("serviceStatus");

            app.MapPost("/management/stop", (HttpContext ctx, IConfiguration cfg,
                IHostApplicationLifetime lifetime, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                logger.LogWarning("Graceful stop requested via management API.");
                lifetime.StopApplication();
                return Results.Ok(new { Message = "Stop signal sent. Service will shut down and restart if managed by OS supervisor." });
            }).WithName("serviceStop");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? _cachedApiKey;
        private static string[] _cachedPreviousApiKeys = Array.Empty<string>();
        private static byte[]? _cachedApiKeyDigest;
        private static byte[][] _cachedPreviousApiKeyDigests = Array.Empty<byte[]>();
        private static string? _cachedScriptRoot;
        private static string? _cachedConfigFingerprint;
        private static readonly object _configLock = new();
        private static bool _configInitialized = false;

        private static void InitializeCache(IConfiguration cfg)
        {
            var apiKey = cfg["Orchestrator:ApiKey"];
            var previousApiKeys = cfg.GetSection("Orchestrator:PreviousApiKeys").Get<string[]>() ?? Array.Empty<string>();
            var scriptRoot = cfg["Orchestrator:ScriptRoot"] ?? AppDomain.CurrentDomain.BaseDirectory;
            var fingerprint = string.Join('\u001f', [apiKey ?? "", scriptRoot, .. previousApiKeys]);
            if (_configInitialized && string.Equals(_cachedConfigFingerprint, fingerprint, StringComparison.Ordinal))
                return;

            lock (_configLock)
            {
                if (_configInitialized && string.Equals(_cachedConfigFingerprint, fingerprint, StringComparison.Ordinal))
                    return;

                _cachedApiKey = apiKey;
                _cachedPreviousApiKeys = previousApiKeys;
                _cachedScriptRoot = scriptRoot;
                _cachedApiKeyDigest = null;

                if (!string.IsNullOrWhiteSpace(_cachedApiKey))
                {
                    _cachedApiKeyDigest = SHA256.HashData(Encoding.UTF8.GetBytes(_cachedApiKey));
                }

                var digests = new List<byte[]>();
                foreach (var k in _cachedPreviousApiKeys.Take(1).Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    digests.Add(SHA256.HashData(Encoding.UTF8.GetBytes(k)));
                }
                _cachedPreviousApiKeyDigests = digests.ToArray();

                _cachedConfigFingerprint = fingerprint;
                _configInitialized = true;
            }
        }

        private static bool ApiKeyDenied(HttpContext ctx, IConfiguration cfg)
        {
            InitializeCache(cfg);
            ctx.Request.Headers.TryGetValue("X-Orchestrator-Key", out var provided);
            return !ApiKeyAcceptedInternal(provided.ToString());
        }

        private static bool ApiKeyAcceptedForSensitivePayload(HttpContext ctx, IConfiguration cfg)
        {
            InitializeCache(cfg);
            ctx.Request.Headers.TryGetValue("X-Orchestrator-Key", out var provided);
            return ApiKeyAcceptedInternal(provided.ToString(), requireConfiguredKey: true);
        }

        internal static bool ApiKeyAcceptedInternal(
            string? provided,
            bool requireConfiguredKey = false)
        {
            var configuredCount = (_cachedApiKeyDigest != null ? 1 : 0) + _cachedPreviousApiKeyDigests.Length;
            if (configuredCount == 0)
                return !requireConfiguredKey;
            if (string.IsNullOrEmpty(provided))
                return false;

            var providedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(provided));

            if (_cachedApiKeyDigest != null && CryptographicOperations.FixedTimeEquals(_cachedApiKeyDigest, providedDigest))
                return true;

            foreach (var prevDigest in _cachedPreviousApiKeyDigests)
            {
                if (CryptographicOperations.FixedTimeEquals(prevDigest, providedDigest))
                    return true;
            }

            return false;
        }

        internal static bool ApiKeyAccepted(
            IConfiguration cfg,
            string? provided,
            bool requireConfiguredKey = false)
        {
            InitializeCache(cfg);
            return ApiKeyAcceptedInternal(provided, requireConfiguredKey);
        }

        private static string GetScriptRoot(IConfiguration cfg)
        {
            InitializeCache(cfg);
            return _cachedScriptRoot!;
        }

        private static async Task<string> PinBundlePathsAsync(string scriptText, IBundleStore store)
        {
            var parsed = new Parser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
            if (parsed.Statements.Count != 1 || parsed.Statements[0] is not RunScriptStatement run)
                return scriptText;
            if (run.PathExpression is not LiteralExpression lit || lit.Value is not string path)
                return scriptText;
            if (!BundleUri.TryParse(path, out var uri) || uri == null || uri.Version.HasValue)
                return scriptText;
            var latest = await store.GetLatestVersionAsync(uri.BundleName);
            return latest == null
                ? scriptText
                : new RunScriptStatement(new LiteralExpression(uri.ToPinnedString(latest.Version), TokenType.STRING_LITERAL), run.Parameters).ToSql();
        }

        private static async Task<string> BuildPrometheusMetricsAsync(
            JobThrottleMetrics metrics,
            int activeProcesses,
            IJobHistoryStore historyStore,
            IHostMetricsStore? hostMetricsStore)
        {
            var labels = new Dictionary<string, string>
            {
                [ObservabilityConventions.PrometheusLabel(ObservabilityConventions.Tags.Environment)] =
                    Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
                [ObservabilityConventions.PrometheusLabel(ObservabilityConventions.Tags.Node)] = Environment.MachineName,
                [ObservabilityConventions.PrometheusLabel(ObservabilityConventions.Tags.Component)] = "orchestrator"
            };

            var now = DateTime.UtcNow;
            var windowStart = now.AddHours(-1);
            var completed = (await historyStore.GetCompletedHistoryAsync(windowStart, now, limit: 10000))
                .Where(entry => entry.EndTime is not null)
                .ToList();
            var failed = completed.Count(entry =>
                string.Equals(entry.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, "INTERRUPTED", StringComparison.OrdinalIgnoreCase));
            var avgDurationMs = completed.Count == 0
                ? 0
                : completed.Average(entry => Math.Max(0, (entry.EndTime!.Value - entry.StartTime).TotalMilliseconds));
            var rowsProcessed = completed.Sum(entry => Math.Max(0, entry.RowsProcessed));
            var peakMemoryBytes = completed.Count == 0
                ? 0
                : completed.Max(entry => Math.Max(0, entry.PeakMemoryBytes));
            var cpuTimeSeconds = completed.Sum(entry => Math.Max(0, entry.CpuTimeSeconds));

            HostMetricSample? host = null;
            if (hostMetricsStore is not null)
            {
                host = (await hostMetricsStore.GetHostMetricsAsync(Environment.MachineName, windowStart, limit: 1))
                    .FirstOrDefault();
            }

            var sb = new StringBuilder();
            AppendGauge(sb, "etlsql_orchestrator_jobs_active",
                "Currently active Orchestrator jobs.", metrics.ActiveJobs, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_queued",
                "Currently queued Orchestrator jobs.", metrics.QueuedJobs, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_max",
                "Maximum concurrent Orchestrator jobs allowed.", metrics.MaxJobs, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_available_slots",
                "Available Orchestrator execution slots.", metrics.AvailableSlots, labels);
            AppendGauge(sb, "etlsql_orchestrator_processes_active",
                "Currently active child processes.", activeProcesses, labels);
            AppendGauge(sb, "etlsql_orchestrator_database_reachable",
                "Whether the Orchestrator database was reachable while composing this metrics snapshot.", 1, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_completed_1h",
                "Orchestrator jobs completed in the last hour.", completed.Count, labels);
            AppendGauge(sb, "etlsql_orchestrator_jobs_failed_1h",
                "Orchestrator jobs failed or interrupted in the last hour.", failed, labels);
            AppendGauge(sb, "etlsql_orchestrator_job_duration_average_ms_1h",
                "Average Orchestrator job execution duration in milliseconds over the last hour.", avgDurationMs, labels);
            AppendGauge(sb, "etlsql_orchestrator_rows_processed_1h",
                "Rows processed by Orchestrator jobs completed in the last hour.", rowsProcessed, labels);
            AppendGauge(sb, "etlsql_orchestrator_peak_memory_bytes_1h",
                "Maximum peak memory reported by Orchestrator jobs completed in the last hour.", peakMemoryBytes, labels);
            AppendGauge(sb, "etlsql_orchestrator_cpu_time_seconds_1h",
                "Total CPU time reported by Orchestrator jobs completed in the last hour.", cpuTimeSeconds, labels);
            if (host is not null)
            {
                AppendGauge(sb, "etlsql_orchestrator_memory_load_percent",
                    "Latest sampled Orchestrator node memory load percentage.", host.MemoryLoadPercent, labels);
                AppendGauge(sb, "etlsql_orchestrator_process_cpu_percent",
                    "Latest sampled Orchestrator process CPU percentage.", host.ProcessCpuPercent, labels);
                if (host.HostCpuPercent is double hostCpuPercent)
                {
                    AppendGauge(sb, "etlsql_orchestrator_host_cpu_percent",
                        "Latest sampled Orchestrator host CPU percentage.", hostCpuPercent, labels);
                }
                AppendGauge(sb, "etlsql_orchestrator_state_disk_free_bytes",
                    "Latest sampled free bytes for Orchestrator state storage.", host.StateDiskFreeBytes, labels);
                AppendGauge(sb, "etlsql_orchestrator_spill_disk_free_bytes",
                    "Latest sampled free bytes for Orchestrator spill storage.", host.SpillDiskFreeBytes, labels);
            }
            AppendRuntimeGauges(sb, labels);
            return sb.ToString();
        }

        private static void AppendRuntimeGauges(StringBuilder sb, IReadOnlyDictionary<string, string> labels)
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();
            AppendGauge(sb, "etlsql_runtime_process_working_set_bytes",
                "Current process working set in bytes.", process.WorkingSet64, labels);
            AppendGauge(sb, "etlsql_runtime_process_private_memory_bytes",
                "Current process private memory in bytes.", process.PrivateMemorySize64, labels);
            AppendGauge(sb, "etlsql_runtime_gc_heap_bytes",
                "Approximate managed heap bytes reported by GC.GetTotalMemory.", GC.GetTotalMemory(forceFullCollection: false), labels);
            AppendGauge(sb, "etlsql_runtime_gc_collections_gen0_total",
                "Total generation 0 garbage collections since process start.", GC.CollectionCount(0), labels);
            AppendGauge(sb, "etlsql_runtime_gc_collections_gen1_total",
                "Total generation 1 garbage collections since process start.", GC.CollectionCount(1), labels);
            AppendGauge(sb, "etlsql_runtime_gc_collections_gen2_total",
                "Total generation 2 garbage collections since process start.", GC.CollectionCount(2), labels);
        }

        private static void AppendGauge(
            StringBuilder sb,
            string name,
            string help,
            double value,
            IReadOnlyDictionary<string, string> labels)
        {
            sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
            sb.Append("# TYPE ").Append(name).AppendLine(" gauge");
            sb.Append(name).Append(FormatLabels(labels)).Append(' ')
                .AppendLine(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string FormatLabels(IReadOnlyDictionary<string, string> labels) =>
            "{" + string.Join(",", labels.Select(label =>
                $"{label.Key}=\"{EscapeLabelValue(label.Value)}\"")) + "}";

        private static string EscapeLabelValue(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        // ── Ad-hoc job runner (unchanged) ─────────────────────────────────────

        private static async Task RunJobAsync(JobEntry entry, JobSubmitRequest request,
            IServiceScopeFactory scopeFactory, ILogger logger, CancellationToken ct)
        {
            entry.Status = JobRunStatus.Running;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var activity = OrchestratorObservability.StartAdHocJobActivity(entry.JobId, entry.CorrelationId);

            using var scope = scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IScriptExecutor>();

            try
            {
                var result = await executor.ExecuteTextAsync(
                    request.ScriptText,
                    request.SessionId,
                    ct,
                    request.GetLineageJobName(entry.JobId));
                entry.RowsProcessed = result.RowsProcessed;
                entry.PeakMemoryBytes = result.PeakMemoryBytes;
                entry.CpuTimeSeconds = result.CpuTimeSeconds;
                entry.Status = result.Success ? JobRunStatus.Completed : JobRunStatus.Failed;
                entry.ErrorMessage = result.ErrorMessage;

                if (result.Success && request.Metadata != null &&
                    request.Metadata.TryGetValue("IsReport", out var isReport) && isReport == "true")
                {
                    logger.LogInformation("Job {JobId} is a report; building manifest", entry.JobId);
                    if (executor is ScriptExecutorAdapter adapter)
                    {
                        var evaluator = adapter.LastEvaluator;
                        if (evaluator != null)
                        {
                            var builder = new ManifestBuilder(evaluator);
                            var manifest = await builder.BuildAsync("remote_script.rptsql");
                            entry.ReportManifestJson = JsonSerializer.Serialize(manifest);

                            var snapshotDir = "Snapshots";
                            Directory.CreateDirectory(snapshotDir);
                            var reportId = request.Metadata.GetValueOrDefault("ReportId", "unknown");
                            var sessionId = request.SessionId ?? entry.JobId;
                            if (reportId.Contains("..") || reportId.Contains('/') || reportId.Contains('\\') ||
                                sessionId.Contains("..") || sessionId.Contains('/') || sessionId.Contains('\\'))
                            {
                                throw new SecurityException("Invalid character or traversal sequence in ReportId or SessionId.");
                            }
                            var manifestPath = Path.Combine(snapshotDir, $"report_{reportId}_{sessionId}.snapshot.json");

                            var store = new SnapshotStore();
                            await store.SaveAsync(manifest, manifestPath);
                            logger.LogInformation("Manifest saved to {Path}", ETL_SQL.Core.Common.LogSanitizer.Clean(manifestPath));
                        }
                    }
                }

                logger.LogInformation("Job {JobId} {Status} in {ElapsedMs}ms, rows={Rows}",
                    entry.JobId, entry.Status, sw.ElapsedMilliseconds, result.RowsProcessed);
            }
            catch (OperationCanceledException)
            {
                entry.Status = JobRunStatus.Cancelled;
                logger.LogInformation("Job {JobId} was cancelled", entry.JobId);
            }
            catch (Exception ex)
            {
                entry.Status = JobRunStatus.Failed;
                entry.ErrorMessage = SecretRedactor.Redact(ex.Message);
                logger.LogError("Job {JobId} failed unexpectedly: {Message}. StackTrace: {Stack}",
                    entry.JobId, entry.ErrorMessage, SecretRedactor.Redact(ex.StackTrace));
            }
            finally
            {
                sw.Stop();
                entry.ExecutionTimeMs = sw.ElapsedMilliseconds;
                OrchestratorObservability.CompleteAdHocJobActivity(
                    activity,
                    entry.JobId,
                    entry.Status,
                    entry.ExecutionTimeMs,
                    entry.RowsProcessed,
                    entry.PeakMemoryBytes,
                    entry.CpuTimeSeconds);
            }
        }

        // ── Request / response models ─────────────────────────────────────────

        private sealed record CreateScheduledJobRequest(
            string Name,
            string ScriptText,
            int Interval,
            string Unit,
            string? AtTime = null,
            int MaxRetries = 0,
            int RetryDelaySeconds = 30,
            string? HashPolicy = "Warn"
        );

        private sealed record UpdateScheduledJobRequest(
            string? ScriptText = null,
            int? Interval = null,
            string? Unit = null,
            string? AtTime = null,
            bool? IsEnabled = null,
            int? MaxRetries = null,
            int? RetryDelaySeconds = null,
            string? HashPolicy = null
        );

        private static long? ReadExpectedVersion(HttpContext context)
        {
            var value = context.Request.Headers.IfMatch.ToString().Trim().Trim('"');
            return long.TryParse(value, out var version) && version > 0 ? version : null;
        }

        private sealed record PublishBundleApiRequest(
            BundlePublishRequest Bundle,
            string? Password = null
        );

        private sealed class JobEntry(string jobId, CancellationTokenSource cts, string? correlationId)
        {
            public string JobId { get; } = jobId;
            public CancellationTokenSource Cts { get; } = cts;
            public string? CorrelationId { get; } = correlationId;
            public JobRunStatus Status { get; set; } = JobRunStatus.Queued;
            public long RowsProcessed { get; set; }
            public long ExecutionTimeMs { get; set; }
            public long PeakMemoryBytes { get; set; }
            public double CpuTimeSeconds { get; set; }
            public string? ErrorMessage { get; set; }
            public string? ReportManifestJson { get; set; }
        }
    }
}
