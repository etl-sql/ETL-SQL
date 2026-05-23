using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;

namespace ETL_SQL.Connectors.Orchestrator
{
    /// <summary>
    /// Live HTTP connection to a remote ETL-SQL Orchestrator service.
    /// Dispatches orchestrator admin statements to the Orchestrator REST API.
    /// </summary>
    public sealed class OrchestratorDataSource : IPortalAdminConnection
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly ILogger _logger;
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string Path => _baseUrl;
        public string ConnectorType => "ORCHESTRATOR";
        public Dictionary<string, string>? Options { get; }

        public OrchestratorDataSource(string baseUrl, string apiKey, ILogger logger)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey  = apiKey;
            _logger  = logger;
            _http    = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };
            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Add("X-Orchestrator-Key", apiKey);
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = baseUrl,
                ["API_KEY"] = "********"
            };
        }

        // ── IDataSource (stub) ────────────────────────────────────────────────────

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            throw new NotSupportedException("ORCHESTRATOR connections do not support SELECT.");

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            throw new NotSupportedException("ORCHESTRATOR connections do not support INSERT.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }

        // ── IPortalAdminConnection ────────────────────────────────────────────────

        public async Task ExecuteAdminStatementAsync(Statement statement, IExecutionContext context)
        {
            switch (statement)
            {
                case CreatePortalRefreshJobStatement s: await CreateRefreshJobAsync(s, context); break;
                case DropPortalRefreshJobStatement s:   await DropJobAsync(s, context); break;
                case CreateJobStatement s:              await CreateScriptJobAsync(s, context); break;
                case AlterJobStatement s:              await AlterScriptJobAsync(s, context); break;
                case EnableJobStatement s:             await EnableDisableJobAsync(s.Name, enable: true, context); break;
                case DisableJobStatement s:            await EnableDisableJobAsync(s.Name, enable: false, context); break;
                case TriggerJobStatement s:            await TriggerJobAsync(s.Name, context); break;
                case PublishBundleStatement s:         await PublishBundleAsync(s, context); break;
                case ValidateBundleStatement s:        await ValidateBundleAsync(s, context); break;
                case ExportScriptStatement s:          await ExportScriptAsync(s, context); break;
                case ShowPublishedBundlesStatement s:  await FetchPublishedBundlesAsync(s, context); break;
                case ShowBundleVersionsStatement s:    await FetchBundleVersionsAsync(s, context); break;
                case ShowBundleFilesStatement s:       await FetchBundleFilesAsync(s, context); break;
                case ShowBundleDependenciesStatement s: await FetchBundleDependenciesAsync(s, context); break;
                case ShowJobsStatement s:               await FetchJobsAsync(s, context); break;
                case ShowJobHistoryStatement s:         await FetchJobHistoryAsync(s, context); break;
                default:
                    throw new ExecutionException(
                        $"Statement type '{statement.GetType().Name}' is not supported inside an ORCHESTRATOR block.");
            }
        }

        private async Task CreateRefreshJobAsync(CreatePortalRefreshJobStatement stmt, IExecutionContext context)
        {
            var (interval, unit, atTime) = ParseCronToSchedule(stmt.Schedule);
            var req = new
            {
                Name       = $"REFRESH:{stmt.ReportName}",
                ScriptText = $"-- Auto-generated refresh job for report '{stmt.ReportName}'",
                Interval   = interval,
                Unit       = unit,
                AtTime     = atTime
            };
            var content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            var resp = await SendHttpAsync(() => _http.PostAsync("api/scheduled-jobs", content));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Refresh job for '{stmt.ReportName}' created in Orchestrator.", ConsoleColor.Green);
        }

        // Converts common cron expressions to the Orchestrator's Interval/Unit/AtTime model.
        // Handles the forms produced by ETL-SQL's EVERY...AT scheduling syntax.
        // Falls back to EVERY 1 HOUR for unrecognized patterns.
        private static (int Interval, string Unit, string? AtTime) ParseCronToSchedule(string? cron)
        {
            if (string.IsNullOrWhiteSpace(cron)) return (1, "HOUR", null);
            var parts = cron.Trim().Split(' ');
            if (parts.Length < 5) return (1, "HOUR", null);

            var minute = parts[0]; var hour = parts[1];
            var dom = parts[2]; var month = parts[3]; var dow = parts[4];

            // Every N minutes: */N * * * *
            if (minute.StartsWith("*/") && hour == "*" && dom == "*" && month == "*" && dow == "*"
                && int.TryParse(minute[2..], out var everyMin))
                return (everyMin, "MINUTE", null);

            // Every N hours at fixed minute: M */N * * *  or  0 */N * * *
            if (hour.StartsWith("*/") && dom == "*" && month == "*" && dow == "*"
                && int.TryParse(hour[2..], out var everyHr) && int.TryParse(minute, out var atMin))
                return (everyHr, "HOUR", atMin == 0 ? null : $"00:{atMin:D2}");

            // Daily at HH:MM: M H * * *
            if (dom == "*" && month == "*" && dow == "*"
                && int.TryParse(minute, out var dMin) && int.TryParse(hour, out var dHr))
                return (1, "DAY", $"{dHr:D2}:{dMin:D2}");

            return (1, "HOUR", null);
        }

        private async Task CreateScriptJobAsync(CreateJobStatement stmt, IExecutionContext context)
        {
            // Serialize the full script body as a SQL statement (e.g. "RUN SCRIPT '/path';")
            // so the scheduler can re-parse and execute it correctly. The old code stored just
            // the bare path string, causing "Unexpected token SLASH" at runtime when the path
            // was fed to the ETL-SQL parser as if it were a SQL script.
            string scriptText = stmt.Script.ToSql();

            var schedule = stmt.Schedule;
            var req = new
            {
                Name       = stmt.JobName,
                ScriptText = scriptText,
                Interval   = schedule?.Interval ?? 1,
                Unit       = (schedule?.Unit ?? "HOUR").ToUpperInvariant(),
                AtTime     = schedule?.AtTime
            };
            var content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            var resp = await SendHttpAsync(() => _http.PostAsync("api/scheduled-jobs", content));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Job '{stmt.JobName}' created in Orchestrator.", ConsoleColor.Green);
        }

        private async Task FetchJobsAsync(ShowJobsStatement stmt, IExecutionContext context)
        {
            var resp = await SendHttpAsync(() => _http.GetAsync("api/scheduled-jobs"));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }

            var jobs = await resp.Content.ReadFromJsonAsync<JobDefinition[]>(_json) ?? [];

            var table = new DataTable();
            table.AddColumn("Name");
            table.AddColumn("Schedule");
            table.AddColumn("LastRun");
            table.AddColumn("NextRun");
            table.AddColumn("Script");
            table.AddColumn("Enable");

            foreach (var job in jobs)
            {
                var row = new Row();
                row["Name"] = job.Name;
                row["Schedule"] = $"EVERY {job.Interval} {job.Unit}" + (job.AtTime != null ? $" AT {job.AtTime}" : "");
                row["LastRun"] = job.LastRun;
                row["NextRun"] = job.NextRun;
                row["Script"] = job.Script;
                row["Enable"] = job.IsEnabled ? 1 : 0;
                await table.AddRowAsync(row);
            }

            await WriteResultAsync(table, stmt.IntoTable, context);
            if (stmt.IntoTable == null && table.Rows.Count == 0)
                context.Log("0 rows returned.", ConsoleColor.Cyan);
        }

        private async Task FetchJobHistoryAsync(ShowJobHistoryStatement stmt, IExecutionContext context)
        {
            var url = stmt.JobName != null
                ? $"api/history?jobName={Uri.EscapeDataString(stmt.JobName)}"
                : "api/history";

            var resp = await SendHttpAsync(() => _http.GetAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }

            var history = await resp.Content.ReadFromJsonAsync<JobHistoryEntry[]>(_json) ?? [];

            var table = new DataTable();
            table.AddColumn("Id");
            table.AddColumn("JobName");
            table.AddColumn("StartTime");
            table.AddColumn("EndTime");
            table.AddColumn("Status");
            table.AddColumn("RowsProcessed");
            table.AddColumn("PeakRAM_MB");
            table.AddColumn("CPUTime_s");
            table.AddColumn("ErrorMessage");

            foreach (var entry in history)
            {
                var row = new Row();
                row["Id"] = entry.Id;
                row["JobName"] = entry.JobName;
                row["StartTime"] = entry.StartTime;
                row["EndTime"] = entry.EndTime;
                row["Status"] = entry.Status;
                row["RowsProcessed"] = entry.RowsProcessed;
                row["PeakRAM_MB"] = entry.PeakMemoryBytes / (1024.0 * 1024.0);
                row["CPUTime_s"] = entry.CpuTimeSeconds;
                row["ErrorMessage"] = entry.ErrorMessage;
                await table.AddRowAsync(row);
            }

            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task PublishBundleAsync(PublishBundleStatement stmt, IExecutionContext context)
        {
            var source = (await context.EvaluateValue(stmt.SourcePath, new Row()))?.ToString()
                ?? throw new ExecutionException("PUBLISH BUNDLE source path evaluated to null.");
            var password = stmt.PasswordMode == BundleSecretMode.Prompt
                ? PasswordPrompt.ReadPassword($"Publish password for bundle '{stmt.BundleName}': ")
                : stmt.Password;

            var preflight = await BundlePublishSupport.PreflightAsync(
                stmt.BundleName,
                context.ResolvePath(source),
                stmt.EntryPath,
                password,
                SecurityService.GetMachineKey(),
                rewriteSecrets: false);

            var request = new PublishBundleApiRequest(
                new BundlePublishRequest(
                    stmt.BundleName,
                    preflight.EntryPath,
                    preflight.Files,
                    preflight.Dependencies,
                    preflight.ContentHash,
                    stmt.EncryptionMode.ToUpperInvariant(),
                    stmt.KeyFile,
                    Environment.UserName,
                    stmt.Description),
                password);

            var resp = await SendHttpAsync(() => _http.PostAsJsonAsync("api/bundles", request, _json));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            var version = await resp.Content.ReadFromJsonAsync<BundleVersionInfo>(_json);
            _logger.WriteLine($"Published bundle '{stmt.BundleName}' version {version?.Version ?? 0} to Orchestrator.", ConsoleColor.Green);
        }

        private async Task ValidateBundleAsync(ValidateBundleStatement stmt, IExecutionContext context)
        {
            var source = (await context.EvaluateValue(stmt.SourcePath, new Row()))?.ToString()
                ?? throw new ExecutionException("VALIDATE BUNDLE source path evaluated to null.");
            var password = stmt.PasswordMode == BundleSecretMode.Prompt
                ? PasswordPrompt.ReadPassword($"Publish password for bundle '{stmt.BundleName}': ")
                : stmt.Password;
            var preflight = await BundlePublishSupport.PreflightAsync(
                stmt.BundleName,
                context.ResolvePath(source),
                stmt.EntryPath,
                password,
                SecurityService.GetMachineKey(),
                rewriteSecrets: false);
            _logger.WriteLine($"Bundle '{stmt.BundleName}' validated for remote publish: {preflight.Files.Count} file(s), {preflight.Dependencies.Count} dependency edge(s).", ConsoleColor.Green);
        }

        private async Task ExportScriptAsync(ExportScriptStatement stmt, IExecutionContext context)
        {
            var source = (await context.EvaluateValue(stmt.SourcePath, new Row()))?.ToString()
                ?? throw new ExecutionException("EXPORT SCRIPT source evaluated to null.");
            var target = (await context.EvaluateValue(stmt.TargetPath, new Row()))?.ToString()
                ?? throw new ExecutionException("EXPORT SCRIPT target evaluated to null.");

            if (!BundleUri.TryParse(source, out var uri) || uri == null)
                throw new ExecutionException("EXPORT SCRIPT source must be an orch://bundle@version/path.etlsql path.");
            var version = uri.Version ?? await FetchLatestBundleVersionNumberAsync(uri.BundleName);
            var files = await GetBundleFilesAsync(uri.BundleName, version);
            var targetDir = context.ResolvePath(target);
            System.IO.Directory.CreateDirectory(targetDir);
            foreach (var file in files)
            {
                var outPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(targetDir, file.VirtualPath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                if (!outPath.StartsWith(System.IO.Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                    throw new ExecutionException($"EXPORT SCRIPT refused path outside target directory: {file.VirtualPath}");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
                await File.WriteAllTextAsync(outPath, file.Content);
            }
            _logger.WriteLine($"Exported remote bundle '{uri.BundleName}' version {version} to {targetDir}. Re-enter any secrets before running recovered scripts.", ConsoleColor.Green);
        }

        private async Task FetchPublishedBundlesAsync(ShowPublishedBundlesStatement stmt, IExecutionContext context)
        {
            var bundles = await GetJsonAsync<BundleVersionInfo[]>("api/bundles") ?? [];
            await WriteBundleVersionsAsync(bundles, stmt.IntoTable, context);
        }

        private async Task FetchBundleVersionsAsync(ShowBundleVersionsStatement stmt, IExecutionContext context)
        {
            var versions = await GetJsonAsync<BundleVersionInfo[]>($"api/bundles/{Uri.EscapeDataString(stmt.BundleName)}/versions") ?? [];
            await WriteBundleVersionsAsync(versions, stmt.IntoTable, context);
        }

        private async Task FetchBundleFilesAsync(ShowBundleFilesStatement stmt, IExecutionContext context)
        {
            var files = await GetBundleFilesAsync(stmt.BundleName, stmt.Version);
            var table = new DataTable();
            table.AddColumn("BundleName");
            table.AddColumn("Version");
            table.AddColumn("VirtualPath");
            table.AddColumn("ContentHash");
            table.AddColumn("SizeBytes");
            table.AddColumn("ContentType");
            foreach (var file in files)
            {
                var row = new Row();
                row["BundleName"] = file.BundleName;
                row["Version"] = file.Version;
                row["VirtualPath"] = file.VirtualPath;
                row["ContentHash"] = file.ContentHash;
                row["SizeBytes"] = file.SizeBytes;
                row["ContentType"] = file.ContentType;
                await table.AddRowAsync(row);
            }
            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task FetchBundleDependenciesAsync(ShowBundleDependenciesStatement stmt, IExecutionContext context)
        {
            var deps = await GetJsonAsync<BundleDependencyInfo[]>($"api/bundles/{Uri.EscapeDataString(stmt.BundleName)}/versions/{stmt.Version}/dependencies") ?? [];
            var table = new DataTable();
            table.AddColumn("BundleName");
            table.AddColumn("Version");
            table.AddColumn("FromPath");
            table.AddColumn("ToPath");
            foreach (var dep in deps)
            {
                var row = new Row();
                row["BundleName"] = dep.BundleName;
                row["Version"] = dep.Version;
                row["FromPath"] = dep.FromPath;
                row["ToPath"] = dep.ToPath;
                await table.AddRowAsync(row);
            }
            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task<int> FetchLatestBundleVersionNumberAsync(string bundleName)
        {
            var versions = await GetJsonAsync<BundleVersionInfo[]>($"api/bundles/{Uri.EscapeDataString(bundleName)}/versions") ?? [];
            var latest = versions.OrderByDescending(v => v.Version).FirstOrDefault()
                ?? throw new ExecutionException($"Bundle '{bundleName}' was not found.");
            return latest.Version;
        }

        private async Task<BundleFileInfo[]> GetBundleFilesAsync(string bundleName, int version)
            => await GetJsonAsync<BundleFileInfo[]>($"api/bundles/{Uri.EscapeDataString(bundleName)}/versions/{version}/files") ?? [];

        private async Task<T?> GetJsonAsync<T>(string url)
        {
            var resp = await SendHttpAsync(() => _http.GetAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            return await resp.Content.ReadFromJsonAsync<T>(_json);
        }

        private static async Task WriteBundleVersionsAsync(IEnumerable<BundleVersionInfo> versions, string? intoTable, IExecutionContext context)
        {
            var table = new DataTable();
            table.AddColumn("BundleName");
            table.AddColumn("Version");
            table.AddColumn("EntryPath");
            table.AddColumn("ContentHash");
            table.AddColumn("PublishedAt");
            table.AddColumn("Publisher");
            table.AddColumn("Description");
            foreach (var version in versions)
            {
                var row = new Row();
                row["BundleName"] = version.BundleName;
                row["Version"] = version.Version;
                row["EntryPath"] = version.EntryPath;
                row["ContentHash"] = version.ContentHash;
                row["PublishedAt"] = version.PublishedAt;
                row["Publisher"] = version.Publisher;
                row["Description"] = version.Description;
                await table.AddRowAsync(row);
            }
            await WriteResultAsync(table, intoTable, context);
        }

        private static async Task WriteResultAsync(DataTable table, string? intoTable, IExecutionContext context)
        {
            if (intoTable != null)
            {
                if (!context.Connections.ContainsKey(intoTable))
                    context.Connections[intoTable] = new InMemoryDataSource();
                var destination = await context.ResolveDataSourceAsync(new TableReference(intoTable));
                await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
            }
            else
            {
                context.LastResult = table;
                context.LastResultSets.Add(table);
                context.OnResultSet?.Invoke(table);
            }
        }

        private async Task DropJobAsync(DropPortalRefreshJobStatement stmt, IExecutionContext context)
        {
            var jobName = Uri.EscapeDataString($"REFRESH:{stmt.ReportName}");
            var resp = await SendHttpAsync(() => _http.DeleteAsync($"api/scheduled-jobs/{jobName}"));
            if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Refresh job for '{stmt.ReportName}' removed from Orchestrator.", ConsoleColor.Green);
        }

        private async Task AlterScriptJobAsync(AlterJobStatement stmt, IExecutionContext context)
        {
            // Build a partial update — only set fields that the ALTER statement changed.
            var req = new
            {
                ScriptText        = stmt.Script   != null ? (string?)stmt.Script.ToSql()     : null,
                Interval          = stmt.Schedule != null ? (int?)stmt.Schedule.Interval      : null,
                Unit              = stmt.Schedule != null ? (string?)stmt.Schedule.Unit.ToUpperInvariant() : null,
                AtTime            = stmt.Schedule != null ? stmt.Schedule.AtTime               : null
            };
            var encoded = Uri.EscapeDataString(stmt.JobName);
            var content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            var resp = await SendHttpAsync(() => _http.PutAsync($"api/scheduled-jobs/{encoded}", content));
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ExecutionException($"ALTER JOB failed: job '{stmt.JobName}' not found in Orchestrator. Use CREATE JOB to create it.");
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Job '{stmt.JobName}' altered in Orchestrator.", ConsoleColor.Green);
        }

        private async Task EnableDisableJobAsync(string jobName, bool enable, IExecutionContext context)
        {
            var req = new { IsEnabled = (bool?)enable };
            var encoded = Uri.EscapeDataString(jobName);
            var content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            var resp = await SendHttpAsync(() => _http.PutAsync($"api/scheduled-jobs/{encoded}", content));
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ExecutionException($"{(enable ? "ENABLE" : "DISABLE")} JOB failed: job '{jobName}' not found.");
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Job '{jobName}' {(enable ? "enabled" : "disabled")} in Orchestrator.", ConsoleColor.Green);
        }

        private async Task TriggerJobAsync(string jobName, IExecutionContext context)
        {
            var encoded = Uri.EscapeDataString(jobName);
            var resp = await SendHttpAsync(() => _http.PostAsync($"api/scheduled-jobs/{encoded}/trigger", null));
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ExecutionException($"TRIGGER JOB failed: job '{jobName}' not found.");
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Job '{jobName}' triggered for immediate execution.", ConsoleColor.Green);
        }

        private async Task<HttpResponseMessage> SendHttpAsync(Func<Task<HttpResponseMessage>> send)
        {
            try { return await send(); }
            catch (HttpRequestException ex)
            { throw new ExecutionException($"Orchestrator connection error: {ex.Message}", ex); }
        }

        private sealed record PublishBundleApiRequest(BundlePublishRequest Bundle, string? Password = null);
    }
}
