using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.ReportPortal
{
    /// <summary>
    /// Live HTTP connection to a remote ETL-SQL Report Portal.
    /// Acquires a JWT on first use and dispatches portal admin statements to the portal REST API.
    /// </summary>
    public sealed class ReportPortalDataSource : IPortalAdminConnection
    {
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private string? _token;
        private DateTime _tokenExpiry = DateTime.MinValue;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string Path => _baseUrl;
        public string ConnectorType => "REPORTPORTAL";
        public Dictionary<string, string>? Options { get; }

        public ReportPortalDataSource(string baseUrl, string username, string password, ILogger logger)
        {
            _baseUrl  = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _logger   = logger;
            _http     = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };
            Options   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"]     = baseUrl,
                ["USER"]     = username,
                ["PASSWORD"] = "********"
            };
        }

        // ── IDataSource (stub — portal connections don't support read/write) ───────

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            throw new NotSupportedException("REPORTPORTAL connections do not support SELECT.");

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            throw new NotSupportedException("REPORTPORTAL connections do not support INSERT.");

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }

        // ── Authentication ────────────────────────────────────────────────────────

        private async Task EnsureAuthenticatedAsync()
        {
            // Re-authenticate if no token or within 5 minutes of expiry
            if (_token is not null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5)) return;

            var req = new { Username = _username, Password = _password };
            var resp = await _http.PostAsJsonAsync("api/auth/login", req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Portal login failed ({(int)resp.StatusCode}): {body}");
            }
            var result = await resp.Content.ReadFromJsonAsync<LoginResponse>(_json)
                ?? throw new ExecutionException("Portal login returned empty response.");
            _token = result.Token;
            _tokenExpiry = result.ExpiresAt.ToUniversalTime();
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        }

        // ── IPortalAdminConnection ─────────────────────────────────────────────────

        public async Task ExecuteAdminStatementAsync(Statement statement, IExecutionContext context)
        {
            await EnsureAuthenticatedAsync();
            switch (statement)
            {
                case CreatePortalUserStatement s:     await CreateUserAsync(s, context); break;
                case AlterPortalUserStatement s:      await AlterUserAsync(s, context); break;
                case DropPortalUserStatement s:       await DropUserAsync(s, context); break;
                case RevokePortalTokensStatement s:   await RevokeTokensAsync(s, context); break;
                case DisconnectPortalUserStatement s: NotYetSupported($"DISCONNECT USER '{s.Username}'", "no portal API endpoint exists"); break;
                case ShowPortalUsersStatement:        await ShowUsersAsync(context); break;
                case ShowActivePortalSessionsStatement: NotYetSupported("SHOW ACTIVE SESSIONS", "no portal API endpoint exists"); break;

                case CreatePortalGroupStatement s:    await CreateGroupAsync(s, context); break;
                case DropPortalGroupStatement s:      await DropGroupAsync(s, context); break;
                case AddUserToPortalGroupStatement s: await AddUserToGroupAsync(s, context); break;

                case CreatePortalFolderStatement s:   await CreateFolderAsync(s, context); break;
                case AlterPortalFolderStatement s:    await AlterFolderAsync(s, context); break;
                case DropPortalFolderStatement s:     await DropFolderAsync(s, context); break;
                case GrantPortalPermissionStatement s: await GrantFolderPermissionAsync(s, context); break;
                case RevokePortalPermissionStatement s: await RevokeFolderPermissionAsync(s, context); break;

                case PublishPortalReportStatement s:  await PublishReportAsync(s, context); break;
                case AlterPortalReportStatement s:    await AlterReportAsync(s, context); break;
                case DropPortalReportStatement s:     await DropReportAsync(s, context); break;
                case ShowPortalReportsStatement s:    await ShowReportsAsync(s, context); break;

                case CreatePortalRefreshJobStatement s: await CreatePortalRefreshJobAsync(s, context); break;
                case DropPortalRefreshJobStatement s:   await DropPortalRefreshJobAsync(s, context); break;
                case RefreshPortalReportStatement s:  await RefreshReportAsync(s, context); break;
                case RebuildPortalSnapshotStatement s: await RebuildSnapshotAsync(s, context); break;
                case DropPortalSnapshotStatement s:   await DropSnapshotAsync(s, context); break;

                case AlterPortalDatasetStatement s:   await AlterDatasetAsync(s, context); break;
                case RefreshPortalDatasetStatement s: await RefreshDatasetAsync(s, context); break;
                case DropPortalDatasetStatement s:    await DropDatasetAsync(s, context); break;
                case GrantPortalDatasetPermissionStatement s: await GrantDatasetPermissionAsync(s, context); break;
                case RevokePortalDatasetPermissionStatement s: await RevokeDatasetPermissionAsync(s, context); break;

                case RestartPortalStatement:  NotYetSupported("RESTART PORTAL",  "no portal API endpoint exists"); break;
                case ShutdownPortalStatement: NotYetSupported("SHUTDOWN PORTAL", "no portal API endpoint exists"); break;

                default:
                    throw new ExecutionException(
                        $"Statement type '{statement.GetType().Name}' is not supported inside a REPORTPORTAL block. " +
                        "Check TODO.md for planned v1.1 statements.");
            }
        }

        // ── Users ─────────────────────────────────────────────────────────────────

        private async Task CreateUserAsync(CreatePortalUserStatement stmt, IExecutionContext context)
        {
            var password = stmt.Password is not null
                ? (await context.EvaluateValue(stmt.Password, new Row()))?.ToString() ?? ""
                : throw new ExecutionException("CREATE USER requires PASSWORD");

            var req = new
            {
                Username  = stmt.Username,
                Email     = stmt.Email,
                Password  = password,
                Role      = stmt.Role ?? "Viewer",
                FirstName = stmt.FirstName,
                LastName  = stmt.LastName
            };
            await CallAsync(HttpMethod.Post, "api/admin/users", req,
                $"User '{stmt.Username}' created.");
        }

        private async Task AlterUserAsync(AlterPortalUserStatement stmt, IExecutionContext context)
        {
            var userId = await LookupUserIdAsync(stmt.Username);
            var password = stmt.NewPassword is not null
                ? (await context.EvaluateValue(stmt.NewPassword, new Row()))?.ToString()
                : null;

            var req = new
            {
                Email     = stmt.NewEmail,
                Role      = stmt.NewRole,
                IsActive  = stmt.SetActive,
                Password  = password
            };
            await CallAsync(HttpMethod.Put, $"api/admin/users/{userId}", req,
                $"User '{stmt.Username}' updated.");
        }

        private async Task DropUserAsync(DropPortalUserStatement stmt, IExecutionContext context)
        {
            var userId = await LookupUserIdAsync(stmt.Username);
            var url = stmt.Cascade ? $"api/admin/users/{userId}?cascade=true" : $"api/admin/users/{userId}";
            await CallAsync(HttpMethod.Delete, url, null,
                $"User '{stmt.Username}' deleted.");
        }

        private async Task RevokeTokensAsync(RevokePortalTokensStatement stmt, IExecutionContext context)
        {
            var userId = await LookupUserIdAsync(stmt.Username);
            await CallAsync(HttpMethod.Post, $"api/admin/users/{userId}/revoke-tokens", null,
                $"Tokens revoked for user '{stmt.Username}'.");
        }

        private async Task ShowUsersAsync(IExecutionContext context)
        {
            var resp = await _http.GetAsync("api/admin/users");
            resp.EnsureSuccessStatusCode();
            var users = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var table = new DataTable();
            table.SetColumns(["Id", "Username", "Email", "FirstName", "LastName", "IsActive", "Roles"]);
            foreach (var u in users)
            {
                var row = new Row(table.Schema);
                row[0] = TryGet(u, "id");
                row[1] = TryGet(u, "username");
                row[2] = TryGet(u, "email");
                row[3] = TryGet(u, "firstName");
                row[4] = TryGet(u, "lastName");
                row[5] = TryGet(u, "isActive");
                row[6] = u.TryGetProperty("roles", out var roles)
                    ? string.Join(", ", roles.EnumerateArray().Select(r => r.GetString()))
                    : null;
                await table.AddRowAsync(row);
            }
            context.LastResultSets.Clear();
            context.LastResultSets.Add(table);
            context.LastResult = table;
        }

        // ── Groups ────────────────────────────────────────────────────────────────

        private async Task CreateGroupAsync(CreatePortalGroupStatement stmt, IExecutionContext context)
        {
            var req = new { Name = stmt.Name, Description = stmt.Description };
            await CallAsync(HttpMethod.Post, "api/admin/groups", req,
                $"Group '{stmt.Name}' created.");
        }

        private async Task DropGroupAsync(DropPortalGroupStatement stmt, IExecutionContext context)
        {
            var groupId = await LookupGroupIdAsync(stmt.Name);
            var url = stmt.Cascade ? $"api/admin/groups/{groupId}?cascade=true" : $"api/admin/groups/{groupId}";
            await CallAsync(HttpMethod.Delete, url, null,
                $"Group '{stmt.Name}' deleted.");
        }

        private async Task AddUserToGroupAsync(AddUserToPortalGroupStatement stmt, IExecutionContext context)
        {
            var groupId = await LookupGroupIdAsync(stmt.GroupName);
            var req = new { Username = stmt.Username };
            await CallAsync(HttpMethod.Post, $"api/admin/groups/{groupId}/members", req,
                $"User '{stmt.Username}' added to group '{stmt.GroupName}'.");
        }

        // ── Folders ───────────────────────────────────────────────────────────────

        private async Task CreateFolderAsync(CreatePortalFolderStatement stmt, IExecutionContext context)
        {
            // Parse /Parent/Child into parent lookup + leaf name
            var path  = stmt.Path.TrimStart('/');
            var slash = path.LastIndexOf('/');
            string name, parentPath;
            if (slash < 0) { name = path; parentPath = ""; }
            else           { name = path[(slash + 1)..]; parentPath = "/" + path[..slash]; }

            int? parentId = null;
            if (!string.IsNullOrEmpty(parentPath))
                parentId = await LookupFolderIdAsync(parentPath);

            var req = new { Name = name, ParentId = parentId };
            await CallAsync(HttpMethod.Post, "api/folders", req,
                $"Folder '{stmt.Path}' created.");
        }

        private async Task AlterFolderAsync(AlterPortalFolderStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.Path);
            int? newParentId = stmt.NewParentPath is not null
                ? await LookupFolderIdAsync(stmt.NewParentPath)
                : null;
            var req = new { Name = stmt.NewName, ParentId = newParentId };
            await CallAsync(HttpMethod.Put, $"api/folders/{folderId}", req,
                $"Folder '{stmt.Path}' updated.");
        }

        private async Task DropFolderAsync(DropPortalFolderStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.Path);
            var url = stmt.Cascade ? $"api/folders/{folderId}?cascade=true" : $"api/folders/{folderId}";
            await CallAsync(HttpMethod.Delete, url, null,
                $"Folder '{stmt.Path}' deleted.");
        }

        private async Task GrantFolderPermissionAsync(GrantPortalPermissionStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.FolderPath);
            var groupId  = await LookupGroupIdAsync(stmt.GroupName);
            var perm     = MapFolderPermission(stmt.Permission);
            var req      = new { GroupId = groupId, Permission = perm };
            await CallAsync(HttpMethod.Post, $"api/folders/{folderId}/acl", req,
                $"Granted {stmt.Permission} on '{stmt.FolderPath}' to group '{stmt.GroupName}'.");
        }

        private async Task RevokeFolderPermissionAsync(RevokePortalPermissionStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.FolderPath);
            var groupId  = await LookupGroupIdAsync(stmt.GroupName);
            await CallAsync(HttpMethod.Delete, $"api/folders/{folderId}/acl/{groupId}", null,
                $"Revoked {stmt.Permission} on '{stmt.FolderPath}' from group '{stmt.GroupName}'.");
        }

        // ── Reports ───────────────────────────────────────────────────────────────

        private async Task PublishReportAsync(PublishPortalReportStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.FolderPath);
            var req = new
            {
                FolderId    = folderId,
                Name        = stmt.ReportName,
                ScriptPath  = stmt.ScriptPath,
                Description = stmt.Description
            };
            await CallAsync(HttpMethod.Post, "api/reports", req,
                $"Report '{stmt.ReportName}' published to '{stmt.FolderPath}'.");
        }

        private async Task AlterReportAsync(AlterPortalReportStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            int? newFolderId = stmt.NewFolder is not null
                ? await LookupFolderIdAsync(stmt.NewFolder)
                : null;
            var req = new { FolderId = newFolderId, Description = stmt.NewDescription };
            await CallAsync(HttpMethod.Put, $"api/reports/{reportId}", req,
                $"Report '{stmt.ReportName}' updated.");
        }

        private async Task DropReportAsync(DropPortalReportStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var url = stmt.Cascade ? $"api/reports/{reportId}?cascade=true" : $"api/reports/{reportId}";
            await CallAsync(HttpMethod.Delete, url, null,
                $"Report '{stmt.ReportName}' deleted.");
        }

        private async Task ShowReportsAsync(ShowPortalReportsStatement stmt, IExecutionContext context)
        {
            string url = "api/admin/reports";
            if (stmt.FolderPath is not null)
            {
                var folderId = await LookupFolderIdAsync(stmt.FolderPath);
                url = $"api/folders/{folderId}/reports";
            }

            var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var reports = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var table = new DataTable();
            table.SetColumns(["Id", "Name", "Description", "FolderPath", "ScriptPath", "UpdatedAt"]);
            foreach (var r in reports)
            {
                var row = new Row(table.Schema);
                row[0] = TryGet(r, "id");
                row[1] = TryGet(r, "name");
                row[2] = TryGet(r, "description");
                row[3] = TryGet(r, "folderPath") ?? TryGet(r, "folder");
                row[4] = TryGet(r, "scriptPath");
                row[5] = TryGet(r, "updatedAt");
                await table.AddRowAsync(row);
            }
            context.LastResultSets.Clear();
            context.LastResultSets.Add(table);
            context.LastResult = table;
        }

        // ── Refresh Jobs ──────────────────────────────────────────────────────────

        private async Task CreatePortalRefreshJobAsync(CreatePortalRefreshJobStatement stmt, IExecutionContext context)
        {
            var req = new
            {
                ReportName        = stmt.ReportName,
                Schedule          = stmt.Schedule,
                OrchestratorAlias = stmt.OrchestratorAlias
            };
            await CallAsync(HttpMethod.Post, "api/subscriptions/refresh-jobs", req,
                $"Refresh job for report '{stmt.ReportName}' created.");
        }

        private async Task DropPortalRefreshJobAsync(DropPortalRefreshJobStatement stmt, IExecutionContext context)
        {
            var encoded = Uri.EscapeDataString(stmt.ReportName);
            await CallAsync(HttpMethod.Delete, $"api/subscriptions/refresh-jobs/{encoded}", null,
                $"Refresh job for report '{stmt.ReportName}' deleted.");
        }

        // ── Refresh & Snapshots ────────────────────────────────────────────────────

        private async Task RefreshReportAsync(RefreshPortalReportStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await CallAsync(HttpMethod.Post, $"api/reports/{reportId}/refresh", null,
                $"Refresh queued for report '{stmt.ReportName}'.");
        }

        private async Task RebuildSnapshotAsync(RebuildPortalSnapshotStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await CallAsync(HttpMethod.Post, $"api/reports/{reportId}/refresh", null,
                $"Snapshot rebuild queued for report '{stmt.ReportName}'.");
        }

        private Task DropSnapshotAsync(DropPortalSnapshotStatement stmt, IExecutionContext context)
        {
            throw new ExecutionException(
                $"DROP SNAPSHOT '{stmt.ReportName}': no portal endpoint exists for this operation. " +
                "Use REBUILD SNAPSHOT to replace the snapshot, or remove it manually via the portal UI.");
        }

        // ── Datasets ──────────────────────────────────────────────────────────────

        private async Task AlterDatasetAsync(AlterPortalDatasetStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            var req = new { AccessLevel = stmt.AccessLevel, Ttl = stmt.Ttl };
            await CallAsync(HttpMethod.Put, $"api/datasets/{datasetId}", req,
                $"Dataset '{stmt.DatasetName}' updated.");
        }

        private async Task RefreshDatasetAsync(RefreshPortalDatasetStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            await CallAsync(HttpMethod.Post, $"api/datasets/{datasetId}/refresh", null,
                $"Refresh queued for dataset '{stmt.DatasetName}'.");
        }

        private async Task DropDatasetAsync(DropPortalDatasetStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            await CallAsync(HttpMethod.Delete, $"api/datasets/{datasetId}", null,
                $"Dataset '{stmt.DatasetName}' deleted.");
        }

        private async Task GrantDatasetPermissionAsync(GrantPortalDatasetPermissionStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            var groupId   = await LookupGroupIdAsync(stmt.GroupName);
            var req = new { GroupId = groupId, Permission = stmt.Permission.ToString() };
            await CallAsync(HttpMethod.Post, $"api/datasets/{datasetId}/acl", req,
                $"Granted {stmt.Permission} on dataset '{stmt.DatasetName}' to group '{stmt.GroupName}'.");
        }

        private async Task RevokeDatasetPermissionAsync(RevokePortalDatasetPermissionStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            var groupId   = await LookupGroupIdAsync(stmt.GroupName);
            await CallAsync(HttpMethod.Delete, $"api/datasets/{datasetId}/acl/{groupId}", null,
                $"Revoked {stmt.Permission} on dataset '{stmt.DatasetName}' from group '{stmt.GroupName}'.");
        }

        // ── Lookup helpers ────────────────────────────────────────────────────────

        private async Task<int> LookupUserIdAsync(string username)
        {
            var resp = await _http.GetAsync("api/admin/users");
            resp.EnsureSuccessStatusCode();
            var users = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var user = users.FirstOrDefault(u =>
                u.TryGetProperty("username", out var v) &&
                v.GetString()?.Equals(username, StringComparison.OrdinalIgnoreCase) == true);
            if (user.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException($"Portal user '{username}' not found.");
            return user.GetProperty("id").GetInt32();
        }

        private async Task<int> LookupGroupIdAsync(string name)
        {
            var resp = await _http.GetAsync("api/admin/groups");
            resp.EnsureSuccessStatusCode();
            var groups = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var group = groups.FirstOrDefault(g =>
                g.TryGetProperty("name", out var v) &&
                v.GetString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
            if (group.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException($"Portal group '{name}' not found.");
            return group.GetProperty("id").GetInt32();
        }

        private async Task<int> LookupFolderIdAsync(string path)
        {
            var resp = await _http.GetAsync("api/folders");
            resp.EnsureSuccessStatusCode();
            // Flatten the tree by traversing it
            var tree = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var match = FindFolderByPath(tree, path.TrimEnd('/'));
            if (match is null)
                throw new ExecutionException($"Portal folder '{path}' not found.");
            return match.Value.GetProperty("id").GetInt32();
        }

        private static JsonElement? FindFolderByPath(IEnumerable<JsonElement> nodes, string targetPath)
        {
            foreach (var node in nodes)
            {
                if (node.TryGetProperty("path", out var p) &&
                    p.GetString()?.Equals(targetPath, StringComparison.OrdinalIgnoreCase) == true)
                    return node;
                if (node.TryGetProperty("children", out var children))
                {
                    var found = FindFolderByPath(children.EnumerateArray(), targetPath);
                    if (found.HasValue) return found;
                }
            }
            return null;
        }

        private async Task<int> LookupReportIdAsync(string name)
        {
            var resp = await _http.GetAsync("api/admin/reports");
            resp.EnsureSuccessStatusCode();
            var reports = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var report = reports.FirstOrDefault(r =>
                r.TryGetProperty("name", out var v) &&
                v.GetString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
            if (report.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException($"Portal report '{name}' not found.");
            return report.GetProperty("id").GetInt32();
        }

        private async Task<int> LookupDatasetIdAsync(string name, string folderPath)
        {
            var resp = await _http.GetAsync("api/datasets");
            resp.EnsureSuccessStatusCode();
            var datasets = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var dataset = datasets.FirstOrDefault(d =>
                d.TryGetProperty("name", out var n) &&
                n.GetString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true &&
                d.TryGetProperty("folderPath", out var fp) &&
                fp.GetString()?.Equals(folderPath, StringComparison.OrdinalIgnoreCase) == true);
            if (dataset.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException($"Portal dataset '{name}' in folder '{folderPath}' not found.");
            return dataset.GetProperty("id").GetInt32();
        }

        // ── HTTP helpers ──────────────────────────────────────────────────────────

        private async Task CallAsync(HttpMethod method, string url, object? body, string successMessage)
        {
            HttpResponseMessage resp;
            if (body is not null)
            {
                var req = new HttpRequestMessage(method, url)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(body, _json),
                        Encoding.UTF8,
                        "application/json")
                };
                resp = await _http.SendAsync(req);
            }
            else
            {
                var req = new HttpRequestMessage(method, url);
                resp = await _http.SendAsync(req);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var bodyText = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Portal API error ({(int)resp.StatusCode} {resp.StatusCode}): {SanitizeBody(bodyText)}");
            }

            _logger.WriteLine(successMessage, ConsoleColor.Green);
        }

        private void NotYetSupported(string statement, string reason)
        {
            _logger.WriteLine(
                $"{statement}: not yet supported — {reason}. See TODO.md v1.1 item.",
                ConsoleColor.Yellow);
        }

        private static string SanitizeBody(string body)
        {
            // Trim large bodies to avoid flooding the log
            if (body.Length > 500) body = body[..500] + "…";
            return body;
        }

        private static object? TryGet(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String  => v.GetString(),
                JsonValueKind.Number  => v.TryGetInt32(out int i) ? (object)i : v.GetDecimal(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => null,
                _                    => v.GetRawText()
            };
        }

        private static int MapFolderPermission(PortalFolderPermission perm) => perm switch
        {
            PortalFolderPermission.Read    => 0,
            PortalFolderPermission.Execute => 1,
            PortalFolderPermission.Manage  => 2,
            _                              => 0
        };

        // ── Internal DTOs ─────────────────────────────────────────────────────────

        private sealed record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);
    }
}
