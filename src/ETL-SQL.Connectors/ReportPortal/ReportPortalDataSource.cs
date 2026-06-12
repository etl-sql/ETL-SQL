using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _logger = logger;
            _http = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = baseUrl,
                ["USER"] = username,
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
            HttpResponseMessage resp;
            try { resp = await _http.PostAsJsonAsync("api/auth/login", req); }
            catch (HttpRequestException ex) { throw new ExecutionException($"Portal connection error: {ex.Message}", ex); }
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
                case CreatePortalUserStatement s: await CreateUserAsync(s, context); break;
                case AlterPortalUserStatement s: await AlterUserAsync(s, context); break;
                case DropPortalUserStatement s: await DropUserAsync(s, context); break;
                case RevokePortalTokensStatement s: await RevokeTokensAsync(s, context); break;
                case DisconnectPortalUserStatement s: await DisconnectUserAsync(s, context); break;
                case ShowPortalUsersStatement: await ShowUsersAsync(context); break;
                case ShowActivePortalSessionsStatement s: await ShowActiveSessionsAsync(s, context); break;

                case CreatePortalGroupStatement s: await CreateGroupAsync(s, context); break;
                case DropPortalGroupStatement s: await DropGroupAsync(s, context); break;
                case AddUserToPortalGroupStatement s: await AddUserToGroupAsync(s, context); break;

                case CreatePortalSmtpConnectionStatement s: await CreateSmtpConnectionAsync(s, context); break;
                case DropPortalSmtpConnectionStatement s: await DropSmtpConnectionAsync(s, context); break;
                case ShowPortalSmtpConnectionsStatement s: await ShowSmtpConnectionsAsync(s, context); break;

                case CreatePortalFolderStatement s: await CreateFolderAsync(s, context); break;
                case AlterPortalFolderStatement s: await AlterFolderAsync(s, context); break;
                case DropPortalFolderStatement s: await DropFolderAsync(s, context); break;
                case GrantPortalPermissionStatement s: await GrantFolderPermissionAsync(s, context); break;
                case RevokePortalPermissionStatement s: await RevokeFolderPermissionAsync(s, context); break;

                case PublishPortalReportStatement s: await PublishReportAsync(s, context); break;
                case AlterPortalReportStatement s: await AlterReportAsync(s, context); break;
                case DropPortalReportStatement s: await DropReportAsync(s, context); break;
                case ShowPortalReportsStatement s: await ShowReportsAsync(s, context); break;
                case ShowPortalReportStatement s: await ShowReportAsync(s, context); break;
                case FavoritePortalReportStatement s: await FavoriteReportAsync(s, context); break;
                case UnfavoritePortalReportStatement s: await UnfavoriteReportAsync(s, context); break;
                case ValidatePortalReportStatement s: await ValidateReportAsync(s, context); break;
                case ShowPortalReportHistoryStatement s: await ShowReportHistoryAsync(s, context); break;
                case ShowPortalReportDependenciesStatement s: await ShowReportDependenciesAsync(s, context); break;
                case CreatePortalShareLinkStatement s: await CreateShareLinkAsync(s, context); break;
                case ShowPortalShareLinksStatement s: await ShowShareLinksAsync(s, context); break;
                case RevokePortalShareLinkStatement s: await RevokeShareLinkAsync(s, context); break;
                case CreatePortalEmbedTokenStatement s: await CreateEmbedTokenAsync(s, context); break;
                case ShowPortalEmbedTokensStatement s: await ShowEmbedTokensAsync(s, context); break;
                case RevokePortalEmbedTokenStatement s: await RevokeEmbedTokenAsync(s, context); break;
                case CreatePortalSavedViewStatement s: await CreateSavedViewAsync(s, context); break;
                case ShowPortalSavedViewsStatement s: await ShowSavedViewsAsync(s, context); break;
                case DropPortalSavedViewStatement s: await DropSavedViewAsync(s, context); break;
                case CreatePortalAlertStatement s: await CreateAlertAsync(s, context); break;
                case ShowPortalAlertsStatement s: await ShowAlertsAsync(s, context); break;
                case DropPortalAlertStatement s: await DropAlertAsync(s, context); break;
                case ShowPortalFavoritesStatement s: await ShowFavoritesAsync(s, context); break;
                case ShowPortalRecentReportsStatement s: await ShowRecentReportsAsync(s, context); break;
                case SearchPortalCatalogStatement s: await SearchCatalogAsync(s, context); break;
                case ShowEffectivePortalPermissionsStatement s: await ShowEffectivePermissionsAsync(s, context); break;
                case ShowPortalUsageMetricsStatement s: await ShowUsageMetricsAsync(s, context); break;

                case CreatePortalRefreshJobStatement s: await CreatePortalRefreshJobAsync(s, context); break;
                case DropPortalRefreshJobStatement s: await DropPortalRefreshJobAsync(s, context); break;
                case RefreshPortalReportStatement s: await RefreshReportAsync(s, context); break;
                case RebuildPortalSnapshotStatement s: await RebuildSnapshotAsync(s, context); break;
                case DropPortalSnapshotStatement s: await DropSnapshotAsync(s, context); break;
                case CreatePortalSubscriptionStatement s: await CreateSubscriptionAsync(s, context); break;
                case AlterPortalSubscriptionStatement s: await AlterSubscriptionAsync(s, context); break;
                case DropPortalSubscriptionStatement s: await DropSubscriptionAsync(s, context); break;

                case AlterPortalDatasetStatement s: await AlterDatasetAsync(s, context); break;
                case RefreshPortalDatasetStatement s: await RefreshDatasetAsync(s, context); break;
                case DropPortalDatasetStatement s: await DropDatasetAsync(s, context); break;
                case GrantPortalDatasetPermissionStatement s: await GrantDatasetPermissionAsync(s, context); break;
                case RevokePortalDatasetPermissionStatement s: await RevokeDatasetPermissionAsync(s, context); break;

                case RestartPortalStatement: await RestartPortalAsync(context); break;
                case ShutdownPortalStatement: await ShutdownPortalAsync(context); break;

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
                : (stmt.Provider != null && stmt.Provider.Equals("LDAP", StringComparison.OrdinalIgnoreCase))
                    ? null
                    : throw new ExecutionException("CREATE USER requires PASSWORD");

            var req = new
            {
                Username = stmt.Username,
                Email = stmt.Email,
                Password = password,
                Role = stmt.Role ?? "Viewer",
                FirstName = stmt.FirstName,
                LastName = stmt.LastName,
                Provider = stmt.Provider
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
                Email = stmt.NewEmail,
                Role = stmt.NewRole,
                IsActive = stmt.SetActive,
                Password = password
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

        private async Task DisconnectUserAsync(DisconnectPortalUserStatement stmt, IExecutionContext context)
        {
            var userId = await LookupUserIdAsync(stmt.Username);
            await CallAsync(HttpMethod.Post, $"api/admin/users/{userId}/disconnect", null,
                $"Active sessions disconnected for user '{stmt.Username}'.");
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

        private async Task ShowActiveSessionsAsync(ShowActivePortalSessionsStatement stmt, IExecutionContext context) =>
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, "api/admin/sessions", null), stmt.IntoTable, context);

        // ── Groups ────────────────────────────────────────────────────────────────

        private async Task CreateGroupAsync(CreatePortalGroupStatement stmt, IExecutionContext context)
        {
            var req = new
            {
                Name = stmt.Name,
                Description = stmt.Description,
                Provider = stmt.Provider,
                AdGroup = stmt.AdGroup
            };
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

        // ── SMTP connections ──────────────────────────────────────────────────────

        private async Task CreateSmtpConnectionAsync(CreatePortalSmtpConnectionStatement stmt, IExecutionContext context)
        {
            // The password expression is evaluated once and sent over the authenticated channel;
            // the portal stores it encrypted (SmtpPasswordProtector) and never returns it.
            var password = stmt.Password is not null
                ? (await context.EvaluateValue(stmt.Password, new Row()))?.ToString()
                : null;

            var req = new
            {
                Alias = stmt.Alias,
                Host = stmt.Host,
                Port = stmt.Port,
                Username = stmt.Username,
                Password = password,
                FromAddress = stmt.FromAddress,
                UseSsl = stmt.UseSsl
            };
            await CallAsync(HttpMethod.Post, "api/admin/smtp", req,
                $"SMTP connection '{stmt.Alias}' created.");
        }

        private async Task DropSmtpConnectionAsync(DropPortalSmtpConnectionStatement stmt, IExecutionContext context)
        {
            var smtpId = await LookupSmtpConnectionIdAsync(stmt.Alias);
            await CallAsync(HttpMethod.Delete, $"api/admin/smtp/{smtpId}", null,
                $"SMTP connection '{stmt.Alias}' deleted.");
        }

        private async Task ShowSmtpConnectionsAsync(ShowPortalSmtpConnectionsStatement stmt, IExecutionContext context) =>
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, "api/admin/smtp", null), stmt.IntoTable, context);

        private async Task<int> LookupSmtpConnectionIdAsync(string alias)
        {
            var resp = await _http.GetAsync("api/admin/smtp");
            resp.EnsureSuccessStatusCode();
            var connections = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_json) ?? [];
            var match = connections.FirstOrDefault(c =>
                c.TryGetProperty("alias", out var v) &&
                v.GetString()?.Equals(alias, StringComparison.OrdinalIgnoreCase) == true);
            if (match.ValueKind == JsonValueKind.Undefined)
                throw new ExecutionException($"Portal SMTP connection '{alias}' not found.");
            return match.GetProperty("id").GetInt32();
        }

        // ── Folders ───────────────────────────────────────────────────────────────

        private async Task CreateFolderAsync(CreatePortalFolderStatement stmt, IExecutionContext context)
        {
            // Parse /Parent/Child into parent lookup + leaf name
            var path = stmt.Path.TrimStart('/');
            var slash = path.LastIndexOf('/');
            string name, parentPath;
            if (slash < 0) { name = path; parentPath = ""; }
            else { name = path[(slash + 1)..]; parentPath = "/" + path[..slash]; }

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
            var groupId = await LookupGroupIdAsync(stmt.GroupName);
            var perm = MapFolderPermission(stmt.Permission);
            var req = new { GroupId = groupId, Permission = perm };
            await CallAsync(HttpMethod.Post, $"api/folders/{folderId}/acl", req,
                $"Granted {stmt.Permission} on '{stmt.FolderPath}' to group '{stmt.GroupName}'.");
        }

        private async Task RevokeFolderPermissionAsync(RevokePortalPermissionStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.FolderPath);
            var groupId = await LookupGroupIdAsync(stmt.GroupName);
            await CallAsync(HttpMethod.Delete, $"api/folders/{folderId}/acl/{groupId}", null,
                $"Revoked {stmt.Permission} on '{stmt.FolderPath}' from group '{stmt.GroupName}'.");
        }

        // ── Reports ───────────────────────────────────────────────────────────────

        private async Task PublishReportAsync(PublishPortalReportStatement stmt, IExecutionContext context)
        {
            var folderId = await LookupFolderIdAsync(stmt.FolderPath);
            var req = new
            {
                FolderId = folderId,
                Name = stmt.ReportName,
                ScriptPath = stmt.ScriptPath,
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

        private async Task ShowReportAsync(ShowPortalReportStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}", null), stmt.IntoTable, context);
        }

        private async Task FavoriteReportAsync(FavoritePortalReportStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            if (!string.IsNullOrWhiteSpace(stmt.Username))
            {
                var userId = await LookupUserIdAsync(stmt.Username);
                await CallAsync(HttpMethod.Post, $"api/admin/users/{userId}/favorites/{reportId}", null,
                    $"Report '{stmt.ReportName}' favorited for user '{stmt.Username}'.");
                return;
            }

            await CallAsync(HttpMethod.Post, $"api/reports/{reportId}/favorite", null,
                $"Report '{stmt.ReportName}' favorited.");
        }

        private async Task UnfavoriteReportAsync(UnfavoritePortalReportStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            if (!string.IsNullOrWhiteSpace(stmt.Username))
            {
                var userId = await LookupUserIdAsync(stmt.Username);
                await CallAsync(HttpMethod.Delete, $"api/admin/users/{userId}/favorites/{reportId}", null,
                    $"Report '{stmt.ReportName}' unfavorited for user '{stmt.Username}'.");
                return;
            }

            await CallAsync(HttpMethod.Delete, $"api/reports/{reportId}/favorite", null,
                $"Report '{stmt.ReportName}' unfavorited.");
        }

        private async Task ValidateReportAsync(ValidatePortalReportStatement stmt, IExecutionContext context)
        {
            var json = await SendJsonAsync(HttpMethod.Post, "api/reports/validate", new { ScriptPath = stmt.ScriptPath });
            await PublishJsonResultAsync(json, stmt.IntoTable, context);
        }

        private async Task ShowReportHistoryAsync(ShowPortalReportHistoryStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/history", null), stmt.IntoTable, context);
        }

        private async Task ShowReportDependenciesAsync(ShowPortalReportDependenciesStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/dependencies", null), stmt.IntoTable, context);
        }

        private async Task CreateShareLinkAsync(CreatePortalShareLinkStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var json = await SendJsonAsync(HttpMethod.Post, $"api/reports/{reportId}/share-links", new { ExpiresAt = stmt.ExpiresAt });
            await PublishJsonResultAsync(json, stmt.IntoTable, context);
        }

        private async Task ShowShareLinksAsync(ShowPortalShareLinksStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/share-links", null), stmt.IntoTable, context);
        }

        private async Task RevokeShareLinkAsync(RevokePortalShareLinkStatement stmt, IExecutionContext context)
        {
            var (reportId, token) = await LookupReportTokenAsync("share-links", stmt.Token);
            await CallAsync(HttpMethod.Delete, $"api/reports/{reportId}/share-links/{Uri.EscapeDataString(token)}", null,
                $"Share link '{stmt.Token}' revoked.");
        }

        private async Task CreateEmbedTokenAsync(CreatePortalEmbedTokenStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var json = await SendJsonAsync(HttpMethod.Post, $"api/reports/{reportId}/embed-tokens", new { Name = stmt.Name, ExpiresAt = stmt.ExpiresAt });
            await PublishJsonResultAsync(json, stmt.IntoTable, context);
        }

        private async Task ShowEmbedTokensAsync(ShowPortalEmbedTokensStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/embed-tokens", null), stmt.IntoTable, context);
        }

        private async Task RevokeEmbedTokenAsync(RevokePortalEmbedTokenStatement stmt, IExecutionContext context)
        {
            var (reportId, token) = await LookupReportTokenAsync("embed-tokens", stmt.Token);
            await CallAsync(HttpMethod.Delete, $"api/reports/{reportId}/embed-tokens/{Uri.EscapeDataString(token)}", null,
                $"Embed token '{stmt.Token}' revoked.");
        }

        private async Task CreateSavedViewAsync(CreatePortalSavedViewStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var json = await SendJsonAsync(HttpMethod.Post, $"api/reports/{reportId}/saved-views", new
            {
                Name = stmt.Name,
                Parameters = BuildParameterDictionary(stmt.Parameters),
                Filters = (Dictionary<string, string>?)null,
                IsDefault = stmt.IsDefault
            });
            await PublishJsonResultAsync(json, stmt.IntoTable, context);
        }

        private async Task ShowSavedViewsAsync(ShowPortalSavedViewsStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/saved-views", null), stmt.IntoTable, context);
        }

        private async Task DropSavedViewAsync(DropPortalSavedViewStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var viewId = await LookupNamedChildIdAsync($"api/reports/{reportId}/saved-views", stmt.Name, "saved view");
            await CallAsync(HttpMethod.Delete, $"api/reports/{reportId}/saved-views/{viewId}", null,
                $"Saved view '{stmt.Name}' dropped for report '{stmt.ReportName}'.");
        }

        private async Task CreateAlertAsync(CreatePortalAlertStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var json = await SendJsonAsync(HttpMethod.Post, $"api/reports/{reportId}/alerts", new
            {
                Name = stmt.Name,
                VisualName = stmt.VisualName,
                Operator = stmt.Operator,
                Threshold = stmt.Threshold,
                Recipient = stmt.Recipient,
                SmtpAlias = stmt.SmtpAlias
            });
            await PublishJsonResultAsync(json, null, context);
        }

        private async Task ShowAlertsAsync(ShowPortalAlertsStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/alerts", null), stmt.IntoTable, context);
        }

        private async Task DropAlertAsync(DropPortalAlertStatement stmt, IExecutionContext context)
        {
            var reportId = await LookupReportIdAsync(stmt.ReportName);
            var alertId = await LookupNamedChildIdAsync($"api/reports/{reportId}/alerts", stmt.Name, "alert");
            await CallAsync(HttpMethod.Delete, $"api/reports/{reportId}/alerts/{alertId}", null,
                $"Alert '{stmt.Name}' dropped for report '{stmt.ReportName}'.");
        }

        private async Task ShowFavoritesAsync(ShowPortalFavoritesStatement stmt, IExecutionContext context)
        {
            var limit = stmt.Limit ?? 50;
            var url = string.IsNullOrWhiteSpace(stmt.Username)
                ? $"api/catalog/favorites?limit={limit}"
                : $"api/admin/users/{await LookupUserIdAsync(stmt.Username)}/favorites?limit={limit}";
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, url, null), stmt.IntoTable, context);
        }

        private async Task ShowRecentReportsAsync(ShowPortalRecentReportsStatement stmt, IExecutionContext context)
        {
            var limit = stmt.Limit ?? 20;
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/catalog/recent?limit={limit}", null), stmt.IntoTable, context);
        }

        private async Task SearchCatalogAsync(SearchPortalCatalogStatement stmt, IExecutionContext context)
        {
            var limit = stmt.Limit ?? 50;
            var query = Uri.EscapeDataString(stmt.Query);
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/catalog/search?q={query}&limit={limit}", null), stmt.IntoTable, context);
        }

        private async Task ShowEffectivePermissionsAsync(ShowEffectivePortalPermissionsStatement stmt, IExecutionContext context)
        {
            var targetType = stmt.TargetType.ToUpperInvariant();
            string url = targetType switch
            {
                "USER" => $"api/admin/permissions/effective/user/{await LookupUserIdAsync(stmt.Target)}",
                "REPORT" => $"api/admin/permissions/effective/report/{await LookupReportIdAsync(stmt.Target)}",
                "FOLDER" => $"api/admin/permissions/effective/folder/{await LookupFolderIdAsync(stmt.Target)}",
                _ => throw new ExecutionException($"Unsupported effective permissions target type '{stmt.TargetType}'.")
            };
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, url, null), stmt.IntoTable, context);
        }

        private async Task ShowUsageMetricsAsync(ShowPortalUsageMetricsStatement stmt, IExecutionContext context)
        {
            var days = stmt.Days ?? 30;
            await PublishJsonResultAsync(await SendJsonAsync(HttpMethod.Get, $"api/admin/metrics/usage?days={days}", null), stmt.IntoTable, context);
        }

        // ── Refresh Jobs ──────────────────────────────────────────────────────────

        private async Task CreatePortalRefreshJobAsync(CreatePortalRefreshJobStatement stmt, IExecutionContext context)
        {
            var req = new
            {
                ReportName = stmt.ReportName,
                Schedule = stmt.Schedule,
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

        // ── Subscriptions ────────────────────────────────────────────────────────

        private async Task CreateSubscriptionAsync(CreatePortalSubscriptionStatement stmt, IExecutionContext context)
        {
            if (stmt.Format == PortalSubscriptionFormat.Both)
                throw new ExecutionException("Remote CREATE SUBSCRIPTION FORMAT BOTH is not supported by the portal API yet. Create separate PDF and CSV subscriptions.");
            if (stmt.IsGroup)
                throw new ExecutionException("Remote CREATE SUBSCRIPTION DELIVER TO GROUP is not supported by the portal API yet. Create per-recipient subscriptions.");

            var reportId = await LookupReportIdAsync(stmt.ReportPath);
            var req = new
            {
                ReportId = reportId,
                Name = stmt.Name,
                Schedule = stmt.OnRefresh ? "Daily" : stmt.Schedule,
                Format = FormatSubscription(stmt.Format),
                SmtpAlias = stmt.SmtpAlias,
                RecipientEmail = stmt.Recipient,
                AtTime = (string?)null,
                Parameters = BuildParameterDictionary(stmt.Parameters)
            };
            var json = await SendJsonAsync(HttpMethod.Post, "api/subscriptions", req);
            await PublishJsonResultAsync(json, null, context);

            if (stmt.OnRefresh)
                await CallAsync(HttpMethod.Put, $"api/subscriptions/{TryGet(json, "id")}", new { DeliverOnRefresh = true },
                    $"Subscription '{stmt.Name ?? stmt.ReportPath}' set to deliver on refresh.");
        }

        private async Task AlterSubscriptionAsync(AlterPortalSubscriptionStatement stmt, IExecutionContext context)
        {
            if (stmt.NewFormat == PortalSubscriptionFormat.Both)
                throw new ExecutionException("Remote ALTER SUBSCRIPTION FORMAT BOTH is not supported by the portal API yet. Use PDF or CSV.");

            var req = new
            {
                Schedule = stmt.NewSchedule,
                IsActive = stmt.SetActive,
                Format = stmt.NewFormat is null ? null : FormatSubscription(stmt.NewFormat.Value),
                SmtpAlias = stmt.NewSmtpAlias,
                Parameters = stmt.Parameters is null ? null : BuildParameterDictionary(stmt.Parameters)
            };
            var json = await SendJsonAsync(HttpMethod.Put, $"api/subscriptions/{stmt.SubscriptionId}", req);
            await PublishJsonResultAsync(json, null, context);
        }

        private async Task DropSubscriptionAsync(DropPortalSubscriptionStatement stmt, IExecutionContext context)
        {
            await CallAsync(HttpMethod.Delete, $"api/subscriptions/{stmt.SubscriptionId}", null,
                $"Subscription {stmt.SubscriptionId} dropped.");
        }

        // ── Datasets ──────────────────────────────────────────────────────────────

        private async Task AlterDatasetAsync(AlterPortalDatasetStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            var req = new { AccessLevel = stmt.AccessLevel, Ttl = stmt.Ttl };
            // The portal exposes PATCH (not PUT) for dataset metadata updates.
            await CallAsync(HttpMethod.Patch, $"api/datasets/{datasetId}", req,
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
            var groupId = await LookupGroupIdAsync(stmt.GroupName);
            var req = new { GroupId = groupId, Permission = stmt.Permission.ToString() };
            await CallAsync(HttpMethod.Post, $"api/datasets/{datasetId}/acl", req,
                $"Granted {stmt.Permission} on dataset '{stmt.DatasetName}' to group '{stmt.GroupName}'.");
        }

        private async Task RevokeDatasetPermissionAsync(RevokePortalDatasetPermissionStatement stmt, IExecutionContext context)
        {
            var datasetId = await LookupDatasetIdAsync(stmt.DatasetName, stmt.FolderPath);
            var groupId = await LookupGroupIdAsync(stmt.GroupName);
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
            var target = name.Trim();
            var matches = reports.Where(r =>
            {
                var reportName = r.TryGetProperty("name", out var n) ? n.GetString() : null;
                var folderPath = r.TryGetProperty("folderPath", out var fp)
                    ? fp.GetString()
                    : r.TryGetProperty("folder", out var f) ? f.GetString() : null;
                var combined = !string.IsNullOrWhiteSpace(folderPath) && !string.IsNullOrWhiteSpace(reportName)
                    ? (folderPath!.EndsWith('/') ? folderPath + reportName : $"{folderPath}/{reportName}")
                    : reportName;
                return reportName?.Equals(target, StringComparison.OrdinalIgnoreCase) == true
                    || combined?.Equals(target, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
            if (matches.Count == 0)
                throw new ExecutionException($"Portal report '{name}' not found.");
            if (matches.Count > 1)
                throw new ExecutionException($"Portal report name '{name}' is ambiguous. Rename one report or use a unique name.");
            return matches[0].GetProperty("id").GetInt32();
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
            try
            {
                using var req = new HttpRequestMessage(method, url);
                if (body is not null)
                {
                    req.Content = new StringContent(
                        JsonSerializer.Serialize(body, _json),
                        Encoding.UTF8,
                        "application/json");
                }

                await ApplyVersionHeaderAsync(req);
                resp = await _http.SendAsync(req);
            }
            catch (HttpRequestException ex)
            {
                throw new ExecutionException($"Portal connection error: {ex.Message}", ex);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var bodyText = await resp.Content.ReadAsStringAsync();
                throw new ExecutionException($"Portal API error ({(int)resp.StatusCode} {resp.StatusCode}): {SanitizeBody(bodyText)}");
            }

            _logger.WriteLine(successMessage, ConsoleColor.Green);
        }

        private async Task<JsonElement> SendJsonAsync(HttpMethod method, string url, object? body)
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body, _json),
                    Encoding.UTF8,
                    "application/json");
            }

            HttpResponseMessage resp;
            try
            {
                await ApplyVersionHeaderAsync(req);
                resp = await _http.SendAsync(req);
            }
            catch (HttpRequestException ex) { throw new ExecutionException($"Portal connection error: {ex.Message}", ex); }
            using var _ = resp;
            var bodyText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new ExecutionException($"Portal API error ({(int)resp.StatusCode} {resp.StatusCode}): {SanitizeBody(bodyText)}");

            if (string.IsNullOrWhiteSpace(bodyText))
            {
                using var empty = JsonDocument.Parse("{}");
                return empty.RootElement.Clone();
            }

            using var doc = JsonDocument.Parse(bodyText);
            return doc.RootElement.Clone();
        }

        // ── Optimistic concurrency (portal v0.12) ─────────────────────────────────
        // Mutations on versioned portal resources require If-Match with the version from the
        // latest read (428 otherwise; 409 when stale). Scripted statements are single-writer
        // imperative commands, so the connector reads the target's current version immediately
        // before each mutation. URLs that are not versioned mutations are sent unchanged.

        private static readonly Regex[] VersionedRoutes =
        [
            new(@"^(?<r>api/admin/users/\d+)(/reset-password|/revoke-tokens)?$", RegexOptions.Compiled),
            new(@"^(?<r>api/admin/groups/\d+)(/members(/bulk-add|/bulk-remove|/\d+)?)?$", RegexOptions.Compiled),
            new(@"^(?<r>api/datasets/\d+)(/acl(/\d+)?|/move)?$", RegexOptions.Compiled),
            new(@"^(?<r>api/folders/\d+)(/acl(/\d+)?)?$", RegexOptions.Compiled),
            new(@"^(?<r>api/reports/\d+)(/script-content)?$", RegexOptions.Compiled),
            new(@"^(?<r>api/subscriptions/\d+)$", RegexOptions.Compiled),
            new(@"^(?<r>api/admin/smtp/\d+)$", RegexOptions.Compiled)
        ];

        private async Task ApplyVersionHeaderAsync(HttpRequestMessage req)
        {
            if (req.Method == HttpMethod.Get || req.Headers.Contains("If-Match"))
                return;

            var path = (req.RequestUri?.OriginalString ?? "").Split('?')[0].TrimStart('/');
            string? resourceUrl = null;
            foreach (var route in VersionedRoutes)
            {
                var match = route.Match(path);
                if (match.Success)
                {
                    resourceUrl = match.Groups["r"].Value;
                    break;
                }
            }

            if (resourceUrl is null)
                return;

            var version = await TryReadVersionAsync(resourceUrl)
                ?? await TryReadVersionFromListAsync(resourceUrl);
            if (version is not null)
                req.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        }

        private async Task<long?> TryReadVersionAsync(string resourceUrl)
        {
            using var resp = await _http.GetAsync(resourceUrl);
            if (!resp.IsSuccessStatusCode)
                return null;

            var etag = resp.Headers.ETag?.Tag?.Trim('"');
            if (long.TryParse(etag, out var fromEtag) && fromEtag > 0)
                return fromEtag;

            try
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return ReadVersionProperty(doc.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Fallback for resources without a single-item GET (e.g. api/admin/smtp/{id}).</summary>
        private async Task<long?> TryReadVersionFromListAsync(string resourceUrl)
        {
            var lastSlash = resourceUrl.LastIndexOf('/');
            if (lastSlash <= 0 || !long.TryParse(resourceUrl[(lastSlash + 1)..], out var id))
                return null;

            using var resp = await _http.GetAsync(resourceUrl[..lastSlash]);
            if (!resp.IsSuccessStatusCode)
                return null;

            try
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("id", out var itemId)
                        && itemId.ValueKind == JsonValueKind.Number
                        && itemId.GetInt64() == id)
                        return ReadVersionProperty(item);
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }

        private static long? ReadVersionProperty(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.Number
            ? version.GetInt64()
            : null;

        private async Task PublishJsonResultAsync(JsonElement json, string? intoTable, IExecutionContext context)
        {
            var table = await JsonToTableAsync(json);
            context.LastResultSets.Clear();
            context.LastResultSets.Add(table);
            context.LastResult = table;
            context.OnResultSet?.Invoke(table);

            if (intoTable is not null)
            {
                if (!context.Connections.ContainsKey(intoTable))
                    context.Connections[intoTable] = new InMemoryDataSource();
                var destination = await context.ResolveDataSourceAsync(new TableReference(intoTable));
                await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
            }
        }

        private static async Task<DataTable> JsonToTableAsync(JsonElement json)
        {
            var elements = json.ValueKind == JsonValueKind.Array
                ? json.EnumerateArray().ToList()
                : [json];

            var columns = elements
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .SelectMany(e => e.EnumerateObject().Select(p => p.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (columns.Count == 0)
                columns.Add("Value");

            var table = new DataTable();
            table.SetColumns(columns);
            foreach (var element in elements)
            {
                var row = new Row(table.Schema);
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var column in columns)
                    {
                        row[column] = element.TryGetProperty(column, out var value)
                            ? JsonValueToObject(value)
                            : null;
                    }
                }
                else
                {
                    row["Value"] = JsonValueToObject(element);
                }
                await table.AddRowAsync(row);
            }
            return table;
        }

        private static object? JsonValueToObject(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };

        private async Task<(int ReportId, string Token)> LookupReportTokenAsync(string tokenKind, string token)
        {
            var reports = await SendJsonAsync(HttpMethod.Get, "api/admin/reports", null);
            var matches = new List<(int ReportId, string Token)>();
            foreach (var report in reports.EnumerateArray())
            {
                var reportId = report.GetProperty("id").GetInt32();
                var children = await SendJsonAsync(HttpMethod.Get, $"api/reports/{reportId}/{tokenKind}", null);
                foreach (var child in children.EnumerateArray())
                {
                    if (child.TryGetProperty("token", out var tokenProp)
                        && tokenProp.GetString()?.Equals(token, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        matches.Add((reportId, tokenProp.GetString()!));
                    }
                }
            }

            return matches.Count switch
            {
                0 => throw new ExecutionException($"Portal {tokenKind} token '{token}' not found."),
                1 => matches[0],
                _ => throw new ExecutionException($"Portal {tokenKind} token '{token}' is ambiguous.")
            };
        }

        private async Task<int> LookupNamedChildIdAsync(string url, string name, string kind)
        {
            var json = await SendJsonAsync(HttpMethod.Get, url, null);
            var matches = json.EnumerateArray()
                .Where(e => e.TryGetProperty("name", out var n)
                    && n.GetString()?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (matches.Count == 0)
                throw new ExecutionException($"Portal {kind} '{name}' not found.");
            if (matches.Count > 1)
                throw new ExecutionException($"Portal {kind} '{name}' is ambiguous.");
            return matches[0].GetProperty("id").GetInt32();
        }

        private static Dictionary<string, string> BuildParameterDictionary(IReadOnlyList<SubscriptionParameter> parameters) =>
            parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        private static string FormatSubscription(PortalSubscriptionFormat format) => format switch
        {
            PortalSubscriptionFormat.Pdf => "PDF",
            PortalSubscriptionFormat.Csv => "CSV",
            _ => throw new ExecutionException($"Unsupported subscription format '{format}'.")
        };

        private async Task RestartPortalAsync(IExecutionContext context)
        {
            await CallAsync(HttpMethod.Post, "api/admin/service/restart", null,
                "Portal restart requested.");
        }

        private async Task ShutdownPortalAsync(IExecutionContext context)
        {
            await CallAsync(HttpMethod.Post, "api/admin/service/shutdown", null,
                "Portal shutdown requested.");
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
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.TryGetInt32(out int i) ? (object)i : v.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => v.GetRawText()
            };
        }

        private static int MapFolderPermission(PortalFolderPermission perm) => perm switch
        {
            PortalFolderPermission.Read => 0,
            PortalFolderPermission.Execute => 1,
            PortalFolderPermission.Manage => 2,
            _ => 0
        };

        // ── Internal DTOs ─────────────────────────────────────────────────────────

        private sealed record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);
    }
}
