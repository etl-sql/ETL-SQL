using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
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

        /// <summary>
        /// Present when the connection was given Portal credentials, absent when it was given only an
        /// API key. Null is the Solo posture — a single shared key and no identity — which a
        /// federated Orchestrator refuses.
        /// </summary>
        private readonly OrchestratorAssertionExchange? _exchange;
        private readonly HttpClient? _portalHttp;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string Path => _baseUrl;
        public string ConnectorType => "ORCHESTRATOR";
        public Dictionary<string, string>? Options { get; }

        public OrchestratorDataSource(
            string baseUrl,
            string apiKey,
            ILogger logger,
            OrchestratorPortalCredentials? portalCredentials = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _logger = logger;
            _http = PolicyBoundHttp.CreateClient(baseAddress: new Uri(_baseUrl + "/"));
            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Add("X-Orchestrator-Key", apiKey);
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = baseUrl,
                ["API_KEY"] = "********"
            };

            if (portalCredentials is { IsComplete: true })
            {
                // Policy-bound like the Orchestrator client: the Portal is a second network
                // destination this connection reaches, and it carries the credential.
                _portalHttp = PolicyBoundHttp.CreateClient(
                    baseAddress: new Uri(portalCredentials.PortalHost.TrimEnd('/') + "/"));
                _exchange = new OrchestratorAssertionExchange(_portalHttp, portalCredentials);
                Options["PORTAL_HOST"] = portalCredentials.PortalHost;
                if (portalCredentials.IsServiceAccount) Options["CLIENT_ID"] = portalCredentials.ClientId!;
                else Options["USER"] = portalCredentials.User!;
            }
        }

        /// <summary>
        /// Test/DI constructor for in-memory transport handlers. Production clients use the
        /// policy-bound constructor above so redirects, proxies, and socket connects remain governed.
        /// </summary>
        public OrchestratorDataSource(HttpClient http, string apiKey, ILogger logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _baseUrl = http.BaseAddress?.ToString().TrimEnd('/') ?? "";
            _apiKey = apiKey;
            _logger = logger;
            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Add("X-Orchestrator-Key", apiKey);
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = _baseUrl,
                ["API_KEY"] = "********"
            };
        }

        // ── IDataSource (stub) ────────────────────────────────────────────────────

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ORCHESTRATOR connections do not support SELECT.");

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ORCHESTRATOR connections do not support INSERT.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => new OrchestratorCatalogDataSource(_http, tableName);
        public ValueTask DisposeAsync()
        {
            _http.Dispose();
            _portalHttp?.Dispose();
            return ValueTask.CompletedTask;
        }

        // ── IPortalAdminConnection ────────────────────────────────────────────────

        public async Task ExecuteAdminStatementAsync(Statement statement, IExecutionContext context)
        {
            switch (statement)
            {
                case CreatePortalRefreshJobStatement s: await CreateRefreshJobAsync(s, context); break;
                case DropPortalRefreshJobStatement s: await DropJobAsync(s, context); break;
                case CreateJobStatement s: await CreateScriptJobAsync(s, context); break;
                case AlterJobStatement s: await AlterScriptJobAsync(s, context); break;
                case EnableJobStatement s: await EnableDisableJobAsync(s.Name, enable: true, context); break;
                case DisableJobStatement s: await EnableDisableJobAsync(s.Name, enable: false, context); break;
                case TriggerJobStatement s: await TriggerJobAsync(s.Name, context); break;
                case PublishBundleStatement s: await PublishBundleAsync(s, context); break;
                case ValidateBundleStatement s: await ValidateBundleAsync(s, context); break;
                case ExportScriptStatement s: await ExportScriptAsync(s, context); break;
                case CreateConnectionStatement s: await CreateSharedConnectionAsync(s, context); break;
                case AlterConnectionStatement s: await AlterSharedConnectionAsync(s, context); break;
                case TestConnectionStatement s: await TestSharedConnectionAsync(s, context); break;
                case DropConnectionStatement s: await DropSharedConnectionAsync(s, context); break;
                case ShowConnectionsStatement s: await ShowSharedConnectionsAsync(s, context); break;
                case ShowConnectionConfigStatement s: await ShowSharedConnectionConfigAsync(s, context); break;
                case ShowPublishedBundlesStatement s: await FetchPublishedBundlesAsync(s, context); break;
                case ShowBundleVersionsStatement s: await FetchBundleVersionsAsync(s, context); break;
                case ShowBundleFilesStatement s: await FetchBundleFilesAsync(s, context); break;
                case ShowBundleDependenciesStatement s: await FetchBundleDependenciesAsync(s, context); break;
                case ShowJobsStatement s: await FetchJobsAsync(s, context); break;
                case ShowJobHistoryStatement s: await FetchJobHistoryAsync(s, context); break;
                case ShowLineageHistoryForTableStatement s: await FetchLineageHistoryForTableAsync(s, context); break;
                case ShowLineageHistoryForTagStatement s: await FetchLineageHistoryForTagAsync(s, context); break;
                case ShowLineageHistoryForMissingTagsStatement s: await FetchLineageHistoryForMissingTagsAsync(s, context); break;
                case ShowProtectedDataStatement s: await FetchProtectedDataAsync(s, context); break;
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
                Name = $"REFRESH:{stmt.ReportName}",
                ScriptText = $"-- Auto-generated refresh job for report '{stmt.ReportName}'",
                Interval = interval,
                Unit = unit,
                AtTime = atTime
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
            var req = new
            {
                Name = stmt.JobName,
                JobType = stmt.TargetKind.ToString(),
                TargetPath = stmt.TargetPath,
                stmt.MaxRetries,
                stmt.RetryDelaySeconds,
                stmt.Metadata.DisplayName,
                stmt.Metadata.Description,
                stmt.Metadata.Options,
                Mode = stmt.Mode.ToString(),
                HashPolicy = context.ScriptHashPolicy
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

        private async Task CreateSharedConnectionAsync(CreateConnectionStatement stmt, IExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(stmt.ConnectionType))
                throw new ExecutionException(
                    $"CREATE CONNECTION {stmt.ConnectionName} on an Orchestrator requires an implementation type: " +
                    $"CREATE CONNECTION {stmt.ConnectionName} AS <connector>(...).");

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in stmt.Options ?? [])
            {
                var value = (await context.EvaluateValue(option.Value, new Row()))?.ToString();
                if (value is not null) options[option.Key] = value;
            }

            var target = stmt.TargetExpression is null
                ? null
                : (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString();

            if (context.IsWhatIf)
            {
                _logger.WriteLine(
                    $"WHAT IF: would register shared connection '{stmt.ConnectionName}' in Orchestrator ({stmt.ConnectionType}).",
                    ConsoleColor.Yellow);
                return;
            }

            var request = new { ConnectorType = stmt.ConnectionType, Target = target, Options = options };
            var content = new StringContent(JsonSerializer.Serialize(request, _json), Encoding.UTF8, "application/json");
            var resp = await SendHttpAsync(() =>
                _http.PutAsync($"api/admin/connections/{Uri.EscapeDataString(stmt.ConnectionName)}", content));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {SanitizeBody(body)}");
            }

            _logger.WriteLine(
                $"Shared connection '{stmt.ConnectionName}' registered in Orchestrator ({stmt.ConnectionType}).",
                ConsoleColor.Green);
        }

        private async Task AlterSharedConnectionAsync(AlterConnectionStatement stmt, IExecutionContext context)
        {
            var existing = await GetJsonAsync<ConnectionCatalogEntryDto>(
                $"api/admin/connections/{Uri.EscapeDataString(stmt.ConnectionName)}")
                ?? throw new ExecutionException($"Shared connection '{stmt.ConnectionName}' was not found in Orchestrator.");

            var options = new Dictionary<string, string>(
                existing.Options ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var option in stmt.Options ?? [])
            {
                var value = (await context.EvaluateValue(option.Value, new Row()))?.ToString();
                if (value is not null) options[option.Key] = value;
            }

            var connectorType = stmt.ConnectionType ?? existing.ConnectorType;
            if (string.IsNullOrWhiteSpace(connectorType))
                throw new ExecutionException(
                    $"ALTER CONNECTION {stmt.ConnectionName} requires an implementation type because the catalog entry has no active definition.");

            var target = stmt.TargetExpression is null
                ? existing.Target
                : (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString();

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: would alter shared connection '{stmt.ConnectionName}' in Orchestrator.", ConsoleColor.Yellow);
                return;
            }

            var request = new
            {
                ConnectorType = connectorType,
                Target = target,
                Options = options,
                SensitiveFields = existing.SensitiveFields
            };
            var content = new StringContent(JsonSerializer.Serialize(request, _json), Encoding.UTF8, "application/json");
            var resp = await SendHttpAsync(() =>
                _http.PutAsync($"api/admin/connections/{Uri.EscapeDataString(stmt.ConnectionName)}", content));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {SanitizeBody(body)}");
            }

            _logger.WriteLine($"Shared connection '{stmt.ConnectionName}' altered in Orchestrator.", ConsoleColor.Green);
        }

        private async Task TestSharedConnectionAsync(TestConnectionStatement stmt, IExecutionContext context)
        {
            var resp = await SendHttpAsync(() =>
                _http.PostAsync($"api/admin/connections/{Uri.EscapeDataString(stmt.ConnectionName)}/test", null));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {SanitizeBody(body)}");
            }

            var response = await resp.Content.ReadFromJsonAsync<ConnectionTestResponseDto>(_json)
                ?? throw new ExecutionException($"TEST CONNECTION '{stmt.ConnectionName}' returned no response.");

            var table = new DataTable();
            table.AddColumn("Layer");
            table.AddColumn("Status");
            table.AddColumn("Detail");
            table.AddColumn("Remedy");

            foreach (var step in response.Steps ?? [])
            {
                var row = new Row();
                row["Layer"] = step.Layer;
                row["Status"] = step.Status?.ToUpperInvariant();
                row["Detail"] = step.Detail;
                row["Remedy"] = step.Remedy ?? string.Empty;
                await table.AddRowAsync(row);
            }

            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task DropSharedConnectionAsync(DropConnectionStatement stmt, IExecutionContext context)
        {
            var url = $"api/admin/connections/{Uri.EscapeDataString(stmt.ConnectionName)}";

            if (stmt.IfExists)
            {
                using var probe = await SendHttpAsync(() => _http.GetAsync(url));
                if (probe.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.WriteLine(
                        $"Shared connection '{stmt.ConnectionName}' does not exist in Orchestrator — skipped.",
                        ConsoleColor.DarkGray);
                    return;
                }
            }

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: would delete shared connection '{stmt.ConnectionName}' from Orchestrator.", ConsoleColor.Yellow);
                return;
            }

            var resp = await SendHttpAsync(() => _http.DeleteAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {SanitizeBody(body)}");
            }

            _logger.WriteLine($"Shared connection '{stmt.ConnectionName}' deleted from Orchestrator.", ConsoleColor.Green);
        }

        private async Task ShowSharedConnectionsAsync(ShowConnectionsStatement stmt, IExecutionContext context)
        {
            var entries = await GetJsonAsync<ConnectionCatalogEntryDto[]>("api/admin/connections") ?? [];
            var table = new DataTable();
            table.AddColumn("Alias");
            table.AddColumn("ConnectorType");
            table.AddColumn("Target");
            table.AddColumn("Options");
            table.AddColumn("Status");
            table.AddColumn("SensitiveFields");

            foreach (var entry in entries)
            {
                var row = new Row();
                row["Alias"] = entry.Alias;
                row["ConnectorType"] = entry.ConnectorType;
                row["Target"] = entry.Target;
                row["Options"] = JsonSerializer.Serialize(entry.Options ?? [], _json);
                row["Status"] = entry.Status;
                row["SensitiveFields"] = entry.SensitiveFields is null
                    ? null
                    : string.Join(", ", entry.SensitiveFields);
                await table.AddRowAsync(row);
            }

            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task ShowSharedConnectionConfigAsync(ShowConnectionConfigStatement stmt, IExecutionContext context)
        {
            var entry = await GetJsonAsync<ConnectionCatalogEntryDto>(
                $"api/admin/connections/{Uri.EscapeDataString(stmt.ConnectionName)}")
                ?? throw new ExecutionException($"Shared connection '{stmt.ConnectionName}' was not found in Orchestrator.");

            var table = new DataTable();
            table.AddColumn("Option");
            table.AddColumn("Value");

            async Task AddAsync(string option, object? value)
            {
                var row = new Row();
                row["Option"] = option;
                row["Value"] = value?.ToString() ?? string.Empty;
                await table.AddRowAsync(row);
            }

            await AddAsync("Alias", entry.Alias);
            await AddAsync("ConnectorType", entry.ConnectorType);
            await AddAsync("Target", entry.Target);
            await AddAsync("Status", entry.Status);
            if (entry.SensitiveFields is not null)
                await AddAsync("SensitiveFields", string.Join(", ", entry.SensitiveFields));

            foreach (var option in (entry.Options ?? []).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                await AddAsync(option.Key, option.Value);

            await WriteResultAsync(table, stmt.IntoTable, context);
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

        private async Task FetchLineageHistoryForTableAsync(ShowLineageHistoryForTableStatement stmt, IExecutionContext context)
        {
            var url = $"api/lineage/history/table/{Uri.EscapeDataString(stmt.TableName)}?limit={stmt.Limit ?? 100}";
            var resp = await SendHttpAsync(() => _http.GetAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            var entries = await resp.Content.ReadFromJsonAsync<LineageHistoryEntryDto[]>(_json) ?? [];
            var table = await BuildLineageHistoryTableAsync(entries);
            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task FetchLineageHistoryForTagAsync(ShowLineageHistoryForTagStatement stmt, IExecutionContext context)
        {
            var url = stmt.TagValue != null
                ? $"api/lineage/history/tag/{Uri.EscapeDataString(stmt.TagKey)}?value={Uri.EscapeDataString(stmt.TagValue)}&limit={stmt.Limit ?? 100}"
                : $"api/lineage/history/tag/{Uri.EscapeDataString(stmt.TagKey)}?limit={stmt.Limit ?? 100}";
            var resp = await SendHttpAsync(() => _http.GetAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            var entries = await resp.Content.ReadFromJsonAsync<LineageHistoryEntryDto[]>(_json) ?? [];
            var table = await BuildLineageHistoryTableAsync(entries);
            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task FetchLineageHistoryForMissingTagsAsync(ShowLineageHistoryForMissingTagsStatement stmt, IExecutionContext context)
        {
            var url = $"api/lineage/history/missing-tags?limit={stmt.Limit ?? 100}";
            var resp = await SendHttpAsync(() => _http.GetAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            var entries = await resp.Content.ReadFromJsonAsync<LineageMissingMetadataEntryDto[]>(_json) ?? [];
            var table = await BuildMissingMetadataTableAsync(entries);
            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private async Task FetchProtectedDataAsync(ShowProtectedDataStatement stmt, IExecutionContext context)
        {
            var path = stmt.Suggestions ? "api/lineage/history/protected-data/suggestions" : "api/lineage/history/protected-data";
            var url = $"{path}?limit={stmt.Limit ?? 100}";
            var resp = await SendHttpAsync(() => _http.GetAsync(url));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {body}");
            }
            var table = stmt.Suggestions
                ? await BuildProtectedDataSuggestionsTableAsync(
                    await resp.Content.ReadFromJsonAsync<ProtectedDataSuggestionEntry[]>(_json) ?? [])
                : await BuildProtectedDataTableAsync(
                    await resp.Content.ReadFromJsonAsync<ProtectedLineageHistoryEntry[]>(_json) ?? []);
            await WriteResultAsync(table, stmt.IntoTable, context);
        }

        private static async Task<DataTable> BuildMissingMetadataTableAsync(LineageMissingMetadataEntryDto[] entries)
        {
            var table = new DataTable();
            table.AddColumn("TargetTable");
            table.AddColumn("TargetColumn");
            table.AddColumn("MissingTags");
            table.AddColumn("PresentTags");
            table.AddColumn("RunAt");
            table.AddColumn("JobName");
            table.AddColumn("ScriptPath");
            foreach (var e in entries)
            {
                var row = new Row();
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["MissingTags"] = string.Join(", ", e.MissingTags.Select(t => "@" + t));
                row["PresentTags"] = JsonSerializer.Serialize(e.PresentTags);
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["ScriptPath"] = e.ScriptPath;
                await table.AddRowAsync(row);
            }
            return table;
        }

        private static async Task<DataTable> BuildProtectedDataTableAsync(ProtectedLineageHistoryEntry[] entries)
        {
            var table = new DataTable();
            table.AddColumn("Id");
            table.AddColumn("RunAt");
            table.AddColumn("JobName");
            table.AddColumn("TargetTable");
            table.AddColumn("TargetColumn");
            table.AddColumn("SourceTables");
            table.AddColumn("Operation");
            table.AddColumn("ProtectionTags");
            table.AddColumn("ProtectionReason");
            table.AddColumn("Owner");
            table.AddColumn("Steward");
            table.AddColumn("Contact");
            table.AddColumn("Domain");
            table.AddColumn("Classification");
            table.AddColumn("Quality");
            table.AddColumn("Tags");
            table.AddColumn("SourceFile");
            table.AddColumn("Line");
            foreach (var e in entries)
            {
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["Operation"] = e.Operation;
                row["ProtectionTags"] = string.Join(", ", e.ProtectionTags);
                row["ProtectionReason"] = e.ProtectionReason;
                row["Owner"] = e.Owner;
                row["Steward"] = e.Steward;
                row["Contact"] = e.Contact;
                row["Domain"] = e.Domain;
                row["Classification"] = e.Classification;
                row["Quality"] = e.Quality;
                row["Tags"] = JsonSerializer.Serialize(e.Tags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = e.Line;
                await table.AddRowAsync(row);
            }
            return table;
        }

        private static async Task<DataTable> BuildProtectedDataSuggestionsTableAsync(ProtectedDataSuggestionEntry[] entries)
        {
            var table = new DataTable();
            table.AddColumn("Id");
            table.AddColumn("RunAt");
            table.AddColumn("JobName");
            table.AddColumn("TargetTable");
            table.AddColumn("TargetColumn");
            table.AddColumn("SourceTables");
            table.AddColumn("SourceColumns");
            table.AddColumn("SuggestedTag");
            table.AddColumn("SuggestedValue");
            table.AddColumn("Confidence");
            table.AddColumn("EvidenceKind");
            table.AddColumn("Evidence");
            table.AddColumn("Reason");
            table.AddColumn("ExistingTags");
            table.AddColumn("SourceFile");
            table.AddColumn("Line");
            foreach (var e in entries)
            {
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["SourceColumns"] = string.Join(", ", e.SourceColumns);
                row["SuggestedTag"] = e.SuggestedTag;
                row["SuggestedValue"] = e.SuggestedValue;
                row["Confidence"] = e.Confidence;
                row["EvidenceKind"] = e.EvidenceKind;
                row["Evidence"] = e.Evidence;
                row["Reason"] = e.Reason;
                row["ExistingTags"] = JsonSerializer.Serialize(e.ExistingTags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = e.Line;
                await table.AddRowAsync(row);
            }
            return table;
        }

        private static async Task<DataTable> BuildLineageHistoryTableAsync(LineageHistoryEntryDto[] entries)
        {
            var table = new DataTable();
            table.AddColumn("Id");
            table.AddColumn("RunAt");
            table.AddColumn("JobName");
            table.AddColumn("TargetTable");
            table.AddColumn("TargetColumn");
            table.AddColumn("SourceTables");
            table.AddColumn("Operation");
            table.AddColumn("Tags");
            table.AddColumn("SourceFile");
            table.AddColumn("Line");
            foreach (var e in entries)
            {
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["Operation"] = e.Operation;
                row["Tags"] = JsonSerializer.Serialize(e.Tags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = e.Line;
                await table.AddRowAsync(row);
            }
            return table;
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
                throw new ExecutionException($"Orchestrator API error ({(int)resp.StatusCode}): {SanitizeBody(body)}");
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
                stmt.TargetPath,
                stmt.MaxRetries,
                stmt.RetryDelaySeconds,
                stmt.Metadata.DisplayName,
                stmt.Metadata.Description,
                stmt.Metadata.Options,
                HashPolicy = context.ScriptHashPolicy
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
            await EnsureIdentityAsync();
            try { return await send(); }
            catch (HttpRequestException ex)
            { throw new ExecutionException($"Orchestrator connection error: {ex.Message}", ex); }
        }

        /// <summary>
        /// Refreshes the signed identity assertion this connection presents, when it has one.
        ///
        /// <para>Applied here rather than at each call site because every request in this class funnels
        /// through <see cref="SendHttpAsync"/>: a request that forgot the header would not fail
        /// visibly, it would simply be refused as anonymous, and one such call site would be enough.
        /// A connection without Portal credentials attaches nothing and keeps the API-key posture —
        /// which a federated Orchestrator will then refuse, as it should.</para>
        ///
        /// <para>The header is set on the client rather than the message because the callers hand in
        /// prepared sends. A connection executes its statements one at a time, so there is no window
        /// in which two requests disagree about the current assertion.</para>
        /// </summary>
        private async Task EnsureIdentityAsync()
        {
            if (_exchange is null) return;

            var assertion = await _exchange.CurrentAssertionAsync();
            _http.DefaultRequestHeaders.Remove(OrchestratorIdentityAssertion.HeaderName);
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                OrchestratorIdentityAssertion.HeaderName, assertion);
        }

        private static string SanitizeBody(string body)
        {
            var redacted = SecretRedactor.Redact(body) ?? string.Empty;
            return redacted.Length > 500 ? redacted[..500] + "..." : redacted;
        }

        private sealed record PublishBundleApiRequest(BundlePublishRequest Bundle, string? Password = null);

        private sealed record LineageHistoryEntryDto(
            long Id, DateTime RunAt, string? JobName, string? ScriptPath,
            string TargetTable, string? TargetColumn,
            string[] SourceTables, string Operation,
            Dictionary<string, string> Tags, string? SourceFile, int Line);

        private sealed record LineageMissingMetadataEntryDto(
            string TargetTable,
            string? TargetColumn,
            string[] MissingTags,
            Dictionary<string, string> PresentTags,
            DateTime RunAt,
            string? JobName,
            string? ScriptPath);

        private sealed record ConnectionCatalogEntryDto(
            string Alias,
            string ConnectorType,
            string? Target,
            Dictionary<string, string>? Options,
            string Status,
            string[]? SensitiveFields);

        private sealed record ConnectionTestResponseDto(
            string Alias,
            bool Succeeded,
            ConnectionDiagnosticStepDto[]? Steps);

        private sealed record ConnectionDiagnosticStepDto(
            string Layer,
            string? Status,
            string Detail,
            string? Remedy);
    }

    internal sealed class OrchestratorCatalogDataSource(HttpClient http, string tableName) : IDataSource
    {
        private sealed record OrchestratorEffectivePermissionDto(
            string PrincipalKey,
            string ActorIdentity,
            string Role,
            string GroupId,
            string Scope,
            bool CanCreate,
            bool CanMutate,
            bool CanExecute,
            string Source);

        private sealed record OrchestratorCapabilityDto(
            string Name,
            long SizeBytes,
            string MountedPath,
            bool IsAvailable,
            DateTime LastModifiedUtc);

        private sealed record OrchestratorTenantContextDto(
            string TenantId,
            string RunId,
            bool IsSandboxed,
            int StorageGrantsCount,
            string CapabilityRoot);

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
        private readonly string _tableName = tableName.Trim();
        public string Path => _tableName;
        public string ConnectorType => "ORCHESTRATOR";
        public Dictionary<string, string>? Options => null;
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var normalized = _tableName.Trim().ToLowerInvariant();
            var endpoint = normalized switch
            {
                "eng.data_quality_status" => "api/data-quality/status",
                "eng.data_quality_failures" => "api/data-quality/failures",
                "eng.stewardship_score" => "api/stewardship/score",
                "eng.stewardship_gaps" => "api/stewardship/gaps",
                "eng.effective_permissions" or "eng.permissions" => "api/effective-permissions",
                "eng.capabilities" => "api/capabilities",
                "eng.tenant_context" => "api/tenant-context",
                _ => throw new NotSupportedException($"ORCHESTRATOR SELECT does not expose '{_tableName}'.")
            };
            using var response = await http.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new ExecutionException($"Orchestrator API error ({(int)response.StatusCode}): {SecretRedactor.Redact(await response.Content.ReadAsStringAsync(cancellationToken))}");

            if (normalized.EndsWith("effective_permissions", StringComparison.Ordinal) || normalized.EndsWith("permissions", StringComparison.Ordinal))
            {
                var perms = await response.Content.ReadFromJsonAsync<OrchestratorEffectivePermissionDto[]>(Json, cancellationToken) ?? [];
                var columns = new[] { "principal_key", "actor_identity", "role", "group_id", "scope", "can_create", "can_mutate", "can_execute", "source" };
                var rows = perms.Select(p => new Row
                {
                    ["principal_key"] = p.PrincipalKey,
                    ["actor_identity"] = p.ActorIdentity,
                    ["role"] = p.Role,
                    ["group_id"] = p.GroupId,
                    ["scope"] = p.Scope,
                    ["can_create"] = p.CanCreate,
                    ["can_mutate"] = p.CanMutate,
                    ["can_execute"] = p.CanExecute,
                    ["source"] = p.Source
                }).ToList();
                yield return await BuildAsync(columns, rows);
            }
            else if (normalized.EndsWith("capabilities", StringComparison.Ordinal))
            {
                var caps = await response.Content.ReadFromJsonAsync<OrchestratorCapabilityDto[]>(Json, cancellationToken) ?? [];
                var columns = new[] { "name", "size_bytes", "mounted_path", "is_available", "last_modified_utc" };
                var rows = caps.Select(c => new Row
                {
                    ["name"] = c.Name,
                    ["size_bytes"] = c.SizeBytes,
                    ["mounted_path"] = c.MountedPath,
                    ["is_available"] = c.IsAvailable,
                    ["last_modified_utc"] = c.LastModifiedUtc
                }).ToList();
                yield return await BuildAsync(columns, rows);
            }
            else if (normalized.EndsWith("tenant_context", StringComparison.Ordinal))
            {
                var tc = await response.Content.ReadFromJsonAsync<OrchestratorTenantContextDto>(Json, cancellationToken);
                var columns = new[] { "tenant_id", "run_id", "is_sandboxed", "storage_grants_count", "capability_root" };
                var rows = tc != null ? new List<Row>
                {
                    new()
                    {
                        ["tenant_id"] = tc.TenantId,
                        ["run_id"] = tc.RunId,
                        ["is_sandboxed"] = tc.IsSandboxed,
                        ["storage_grants_count"] = tc.StorageGrantsCount,
                        ["capability_root"] = tc.CapabilityRoot
                    }
                } : [];
                yield return await BuildAsync(columns, rows);
            }
            else if (normalized.EndsWith("data_quality_status", StringComparison.Ordinal))
            {
                var statuses = await response.Content.ReadFromJsonAsync<JobDataQualityStatus[]>(Json, cancellationToken) ?? [];
                var columns = new[] { "run_id", "job_name", "start_time", "end_time", "status", "rows_processed", "rows_warned", "rows_quarantined", "warn_percent", "quarantine_percent", "failed_rule_count", "freshest_value_utc", "freshness_state", "error_summary", "source" };
                var rows = statuses.Select(s =>
                {
                    var denominator = s.RowsProcessed <= 0 ? 0d : s.RowsProcessed;
                    return new Row
                    {
                        ["run_id"] = s.RunId,
                        ["job_name"] = s.JobName,
                        ["start_time"] = s.StartTime,
                        ["end_time"] = s.EndTime,
                        ["status"] = s.Status,
                        ["rows_processed"] = s.RowsProcessed,
                        ["rows_warned"] = s.RowsWarned,
                        ["rows_quarantined"] = s.RowsQuarantined,
                        ["warn_percent"] = denominator == 0 ? 0d : s.RowsWarned * 100d / denominator,
                        ["quarantine_percent"] = denominator == 0 ? 0d : s.RowsQuarantined * 100d / denominator,
                        ["failed_rule_count"] = s.FailedRuleCount,
                        ["freshest_value_utc"] = s.FreshestValueUtc,
                        ["freshness_state"] = s.FreshnessState,
                        ["error_summary"] = s.ErrorSummary,
                        ["source"] = "REMOTE_ORCHESTRATOR"
                    };
                }).ToList();
                yield return await BuildAsync(columns, rows);
            }
            else if (normalized.EndsWith("data_quality_failures", StringComparison.Ordinal))
            {
                var failures = await response.Content.ReadFromJsonAsync<JobDataQualityFailure[]>(Json, cancellationToken) ?? [];
                var columns = new[] { "run_id", "job_name", "start_time", "end_time", "status", "target_table", "column_name", "rule", "action", "failure_count", "owner", "source" };
                var rows = failures.Select(f => new Row
                {
                    ["run_id"] = f.RunId.ToString(),
                    ["job_name"] = f.JobName,
                    ["start_time"] = f.StartTime,
                    ["end_time"] = f.EndTime,
                    ["status"] = f.Status,
                    ["target_table"] = f.TargetTable,
                    ["column_name"] = f.ColumnName,
                    ["rule"] = f.Rule,
                    ["action"] = f.Action,
                    ["failure_count"] = f.FailureCount,
                    ["owner"] = f.Owner,
                    ["source"] = "REMOTE_ORCHESTRATOR"
                }).ToList();
                yield return await BuildAsync(columns, rows);
            }
            else if (normalized.EndsWith("stewardship_score", StringComparison.Ordinal))
            {
                var scores = await response.Content.ReadFromJsonAsync<StewardshipScore[]>(Json, cancellationToken) ?? [];
                var columns = new[] { "scope_type", "scope_name", "component", "numerator", "denominator", "percentage", "asset_count", "column_count", "weight", "evaluated_at_utc", "definition_version" };
                var rows = scores.Select(s => new Row
                {
                    ["scope_type"] = s.ScopeType,
                    ["scope_name"] = s.ScopeName,
                    ["component"] = s.Component,
                    ["numerator"] = s.Numerator,
                    ["denominator"] = s.Denominator,
                    ["percentage"] = s.Percentage,
                    ["asset_count"] = s.AssetCount,
                    ["column_count"] = s.ColumnCount,
                    ["weight"] = s.Weight,
                    ["evaluated_at_utc"] = s.EvaluatedAtUtc,
                    ["definition_version"] = s.DefinitionVersion
                }).ToList();
                yield return await BuildAsync(columns, rows);
            }
            else if (normalized.EndsWith("stewardship_gaps", StringComparison.Ordinal))
            {
                var gaps = await response.Content.ReadFromJsonAsync<StewardshipGap[]>(Json, cancellationToken) ?? [];
                var columns = new[] { "scope_type", "scope_name", "component", "target_table", "target_column", "requirement", "source_file", "line", "evaluated_at_utc", "definition_version" };
                var rows = gaps.Select(g => new Row
                {
                    ["scope_type"] = g.ScopeType,
                    ["scope_name"] = g.ScopeName,
                    ["component"] = g.Component,
                    ["target_table"] = g.TargetTable,
                    ["target_column"] = g.TargetColumn,
                    ["requirement"] = g.Requirement,
                    ["source_file"] = g.SourceFile,
                    ["line"] = g.Line,
                    ["evaluated_at_utc"] = g.EvaluatedAtUtc,
                    ["definition_version"] = g.DefinitionVersion
                }).ToList();
                yield return await BuildAsync(columns, rows);
            }
        }

        private static async Task<DataTable> BuildAsync(IEnumerable<string> columns, IEnumerable<Row> rows)
        {
            var table = new DataTable();
            table.SetColumns(columns);
            foreach (var row in rows) await table.AddRowAsync(row);
            return table;
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => throw new NotSupportedException("ORCHESTRATOR catalog tables are read-only.");
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken) => throw new NotSupportedException("ORCHESTRATOR catalog tables are read-only.");
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string name) => new OrchestratorCatalogDataSource(http, name);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
