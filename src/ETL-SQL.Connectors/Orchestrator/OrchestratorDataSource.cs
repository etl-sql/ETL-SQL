using System;
using System.Collections.Generic;
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
            var resp = await _http.PostAsync("api/scheduled-jobs", content);
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
            var resp = await _http.PostAsync("api/scheduled-jobs", content);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Job '{stmt.JobName}' created in Orchestrator.", ConsoleColor.Green);
        }

        private async Task FetchJobsAsync(ShowJobsStatement stmt, IExecutionContext context)
        {
            var resp = await _http.GetAsync("api/scheduled-jobs");
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

            foreach (var job in jobs)
            {
                var row = new Row();
                row["Name"] = job.Name;
                row["Schedule"] = $"EVERY {job.Interval} {job.Unit}" + (job.AtTime != null ? $" AT {job.AtTime}" : "");
                row["LastRun"] = job.LastRun;
                row["NextRun"] = job.NextRun;
                row["Script"] = job.Script;
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

            var resp = await _http.GetAsync(url);
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
            var resp = await _http.DeleteAsync($"api/scheduled-jobs/{jobName}");
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
            var resp = await _http.PutAsync($"api/scheduled-jobs/{encoded}", content);
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
            var resp = await _http.PutAsync($"api/scheduled-jobs/{encoded}", content);
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
            var resp = await _http.PostAsync($"api/scheduled-jobs/{encoded}/trigger", null);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ExecutionException($"TRIGGER JOB failed: job '{jobName}' not found.");
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            _logger.WriteLine($"Job '{jobName}' triggered for immediate execution.", ConsoleColor.Green);
        }
    }
}
