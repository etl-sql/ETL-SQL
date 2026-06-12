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
using ETL_SQL.Core.Data;
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

            // ── Ad-hoc job execution (authenticated — see ApiKeyDenied) ──────────
            app.MapPost("/jobs", (HttpContext ctx, IConfiguration cfg, JobSubmitRequest request, IServiceScopeFactory scopeFactory, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                var jobId = Guid.NewGuid().ToString("N")[..8];
                var cts = new CancellationTokenSource();
                var entry = new JobEntry(jobId, cts);
                _jobs[jobId] = entry;

                logger.LogInformation("Job {JobId} submitted (label={Label})", jobId, request.Label);
                _ = RunJobAsync(entry, request, scopeFactory, logger, cts.Token);

                return Results.Accepted($"/jobs/{jobId}", new { JobId = jobId });
            }).WithName("submitJob");

            app.MapDelete("/jobs/{id}", (string id, HttpContext ctx, IConfiguration cfg, ILogger<Program> logger) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();

                if (!_jobs.TryGetValue(id, out var entry))
                    return Results.NotFound(new { Error = $"Job '{id}' not found." });

                logger.LogInformation("Cancelling job {JobId}", id);
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
                    ErrorMessage = entry.ErrorMessage,
                    ReportManifestJson = includeSensitivePayload ? entry.ReportManifestJson : null
                });
            }).WithName("getJobStatus");

            // ── Scheduled job management ──────────────────────────────────────

            app.MapGet("/api/scheduled-jobs", async (HttpContext ctx, IJobHistoryStore store, IConfiguration cfg) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var jobs = await store.GetAllJobsAsync();
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

                var jobs = await store.GetAllJobsAsync();
                var existing = jobs.FirstOrDefault(j =>
                    j.Name.Equals(Uri.UnescapeDataString(name), StringComparison.OrdinalIgnoreCase));
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

                var jobs = await store.GetAllJobsAsync();
                var unescaped = Uri.UnescapeDataString(name);
                if (!jobs.Any(j => j.Name.Equals(unescaped, StringComparison.OrdinalIgnoreCase)))
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
                var history = await store.GetHistoryAsync(Uri.UnescapeDataString(name), limit);
                return Results.Ok(history);
            }).WithName("getScheduledJobHistory");

            app.MapGet("/api/history", async (HttpContext ctx, IJobHistoryStore store,
                IConfiguration cfg, string? jobName = null, int limit = 100) =>
            {
                if (ApiKeyDenied(ctx, cfg)) return Results.Unauthorized();
                var history = await store.GetHistoryAsync(jobName, limit);
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

                var root = GetScriptRoot(cfg);
                // Prevent path traversal — resolve and verify it stays under root
                var fullPath = Path.GetFullPath(Path.Combine(root, path));
                var fullRoot = Path.GetFullPath(root);
                var separator = Path.DirectorySeparatorChar.ToString();
                var fullRootWithSeparator = fullRoot.EndsWith(separator) ? fullRoot : fullRoot + separator;
                if (!fullPath.StartsWith(fullRootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { Error = "Invalid path." });
                if (!File.Exists(fullPath))
                    return Results.NotFound(new { Error = $"Script '{path}' not found." });

                var content = await File.ReadAllTextAsync(fullPath);
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

        private static bool ApiKeyDenied(HttpContext ctx, IConfiguration cfg)
        {
            ctx.Request.Headers.TryGetValue("X-Orchestrator-Key", out var provided);
            return !ApiKeyAccepted(cfg, provided.ToString());
        }

        private static bool ApiKeyAcceptedForSensitivePayload(HttpContext ctx, IConfiguration cfg)
        {
            ctx.Request.Headers.TryGetValue("X-Orchestrator-Key", out var provided);
            return ApiKeyAccepted(cfg, provided.ToString(), requireConfiguredKey: true);
        }

        internal static bool ApiKeyAccepted(
            IConfiguration cfg,
            string? provided,
            bool requireConfiguredKey = false)
        {
            var configuredKeys = new[] { cfg["Orchestrator:ApiKey"] }
                .Concat((cfg.GetSection("Orchestrator:PreviousApiKeys").Get<string[]>() ?? []).Take(1))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (configuredKeys.Length == 0)
                return !requireConfiguredKey;
            if (string.IsNullOrEmpty(provided))
                return false;

            var providedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            return configuredKeys.Any(key =>
                CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(Encoding.UTF8.GetBytes(key!)),
                    providedDigest));
        }

        private static string GetScriptRoot(IConfiguration cfg) =>
            cfg["Orchestrator:ScriptRoot"] ?? AppDomain.CurrentDomain.BaseDirectory;

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

        // ── Ad-hoc job runner (unchanged) ─────────────────────────────────────

        private static async Task RunJobAsync(JobEntry entry, JobSubmitRequest request,
            IServiceScopeFactory scopeFactory, ILogger logger, CancellationToken ct)
        {
            entry.Status = JobRunStatus.Running;
            var sw = System.Diagnostics.Stopwatch.StartNew();

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
                            logger.LogInformation("Manifest saved to {Path}", manifestPath);
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
                entry.ErrorMessage = ex.Message;
                logger.LogError(ex, "Job {JobId} failed unexpectedly: {Message}. StackTrace: {Stack}",
                    entry.JobId, ex.Message, ex.StackTrace);
            }
            finally
            {
                sw.Stop();
                entry.ExecutionTimeMs = sw.ElapsedMilliseconds;
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

        private sealed class JobEntry(string jobId, CancellationTokenSource cts)
        {
            public string JobId { get; } = jobId;
            public CancellationTokenSource Cts { get; } = cts;
            public JobRunStatus Status { get; set; } = JobRunStatus.Queued;
            public long RowsProcessed { get; set; }
            public long ExecutionTimeMs { get; set; }
            public string? ErrorMessage { get; set; }
            public string? ReportManifestJson { get; set; }
        }
    }
}
