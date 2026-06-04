using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests
{
    [Trait("Category", "Portal")]
    [Trait("CompatBreak", "0.10")]
    public class UserPermissionIntegrationTests : IClassFixture<PortalWebFactory>
    {
        private readonly HttpClient _client;
        private readonly PortalWebFactory _factory;
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
        private static readonly SemaphoreSlim _dbLock = new(1, 1);
        private static bool _dbInitialized = false;

        private static string? _adminToken;
        private static readonly SemaphoreSlim _tokenLock = new(1, 1);

        public UserPermissionIntegrationTests(PortalWebFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();

            // Seed DB once
            InitializeDatabaseAsync().GetAwaiter().GetResult();
        }

        private async Task InitializeDatabaseAsync()
        {
            await _dbLock.WaitAsync();
            try
            {
                if (_dbInitialized) return;

                using (var scope = _factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
                    var registry = scope.ServiceProvider.GetRequiredService<IDatasetRegistry>();

                    // Apply any pending migrations
                    db.Database.Migrate();

                    if (await db.Folders.AnyAsync())
                    {
                        _dbInitialized = true;
                        return;
                    }

                    // 1. Seed Groups
                    var gFinanceReaders = new Group { Name = "Finance_Readers", Description = "Finance Readers Group" };
                    var gFinancePublishers = new Group { Name = "Finance_Publishers", Description = "Finance Publishers Group" };
                    var gOperationsReaders = new Group { Name = "Operations_Readers", Description = "Operations Readers Group" };
                    var gManagers = new Group { Name = "Managers", Description = "Managers Group" };
                    var gOutsiders = new Group { Name = "Outsider_Group", Description = "Outsiders Group" };
                    db.Groups.AddRange(gFinanceReaders, gFinancePublishers, gOperationsReaders, gManagers, gOutsiders);
                    await db.SaveChangesAsync();

                    // 2. Seed Folders
                    var folderFinance = new Folder { Name = "Finance", Path = "/Finance", OwnerId = 1 };
                    db.Folders.Add(folderFinance);
                    await db.SaveChangesAsync();

                    var folderInvoices = new Folder { Name = "Invoices", Path = "/Finance/Invoices", ParentId = folderFinance.Id, OwnerId = 1 };
                    db.Folders.Add(folderInvoices);
                    await db.SaveChangesAsync();

                    var folderOperations = new Folder { Name = "Operations", Path = "/Operations", OwnerId = 1 };
                    db.Folders.Add(folderOperations);
                    await db.SaveChangesAsync();

                    var folderLogs = new Folder { Name = "Logs", Path = "/Operations/Logs", ParentId = folderOperations.Id, OwnerId = 1 };
                    db.Folders.Add(folderLogs);
                    await db.SaveChangesAsync();

                    // 3. Seed Folder ACLs
                    db.FolderAcls.AddRange(
                        new FolderAcl { FolderId = folderFinance.Id, GroupId = gFinanceReaders.Id, Permission = FolderPermission.Read },
                        new FolderAcl { FolderId = folderInvoices.Id, GroupId = gFinanceReaders.Id, Permission = FolderPermission.Read },

                        new FolderAcl { FolderId = folderFinance.Id, GroupId = gFinancePublishers.Id, Permission = FolderPermission.Execute },
                        new FolderAcl { FolderId = folderInvoices.Id, GroupId = gFinancePublishers.Id, Permission = FolderPermission.Manage },

                        new FolderAcl { FolderId = folderOperations.Id, GroupId = gOperationsReaders.Id, Permission = FolderPermission.Read },
                        new FolderAcl { FolderId = folderLogs.Id, GroupId = gOperationsReaders.Id, Permission = FolderPermission.Read },

                        new FolderAcl { FolderId = folderFinance.Id, GroupId = gManagers.Id, Permission = FolderPermission.Execute },
                        new FolderAcl { FolderId = folderInvoices.Id, GroupId = gManagers.Id, Permission = FolderPermission.Read },
                        new FolderAcl { FolderId = folderOperations.Id, GroupId = gManagers.Id, Permission = FolderPermission.Read },
                        new FolderAcl { FolderId = folderLogs.Id, GroupId = gManagers.Id, Permission = FolderPermission.Execute }
                    );
                    await db.SaveChangesAsync();

                    // Helper to create user
                    async Task<PortalUser> CreateUserAsync(string username, string role, List<Group> groups, bool isActive = true, bool mustChangePassword = false)
                    {
                        var user = new PortalUser
                        {
                            UserName = username,
                            Email = $"{username}@test.local",
                            FirstName = username,
                            LastName = "Test",
                            IsActive = isActive,
                            MustChangePassword = mustChangePassword,
                            Provider = "Local",
                            CreatedAt = DateTime.UtcNow
                        };
                        var result = await userManager.CreateAsync(user, "Password@1234!");
                        if (!result.Succeeded)
                            throw new Exception($"Failed to create user {username}: " + string.Join(", ", result.Errors.Select(e => e.Description)));

                        await userManager.AddToRoleAsync(user, role);

                        foreach (var group in groups)
                        {
                            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
                        }
                        await db.SaveChangesAsync();
                        return user;
                    }

                    // 4. Seed Users
                    var uAdmin = await CreateUserAsync("admin_user", "Admin", []);
                    var uFinPub = await CreateUserAsync("finance_pub", "Publisher", [gFinancePublishers]);
                    var uFinRead = await CreateUserAsync("finance_read", "Viewer", [gFinanceReaders]);
                    var uOpsRead = await CreateUserAsync("ops_read", "Viewer", [gOperationsReaders]);
                    var uManager = await CreateUserAsync("manager_user", "Viewer", [gManagers]);
                    var uOutsider = await CreateUserAsync("outsider_user", "Viewer", [gOutsiders]);
                    var uNoGroup = await CreateUserAsync("no_group_user", "Viewer", []);
                    var uInactive = await CreateUserAsync("inactive_user", "Viewer", [gFinanceReaders], isActive: false);
                    var uMcp = await CreateUserAsync("mcp_user", "Viewer", [gFinanceReaders], mustChangePassword: true);
                    var uRevoked = await CreateUserAsync("revoked_user", "Viewer", [gFinanceReaders]);

                    // 5. Seed Reports
                    var scriptDir = Path.Combine(_factory.TempDir, "scripts");
                    Directory.CreateDirectory(scriptDir);

                    string CreateScriptFile(string name, string header)
                    {
                        var path = Path.Combine(scriptDir, name);
                        File.WriteAllText(path, header + "\nSELECT 1;");
                        return path;
                    }

                    var pRevenue = CreateScriptFile("RevenueReport.rptsql", "/* @owner: Finance; @contact: finance@test.local */");
                    var pInvoice = CreateScriptFile("InvoiceDetails.rptsql", "/* @owner: Finance; @contact: finance@test.local */");
                    var pSysLogs = CreateScriptFile("SystemLogs.rptsql", "/* @owner: Ops; @contact: ops@test.local */");
                    var pErrAudit = CreateScriptFile("ErrorAudit.rptsql", "/* @owner: Ops; @contact: ops@test.local */");

                    var rRevenue = new Report
                    {
                        FolderId = folderFinance.Id,
                        Name = "RevenueReport",
                        Description = "Monthly revenue report",
                        ScriptPath = pRevenue,
                        ScriptLastModified = DateTime.UtcNow,
                        CreatedBy = uAdmin.Id,
                        Owner = "Finance"
                    };
                    var rInvoices = new Report
                    {
                        FolderId = folderInvoices.Id,
                        Name = "InvoiceDetails",
                        Description = "Detailed invoice listing",
                        ScriptPath = pInvoice,
                        ScriptLastModified = DateTime.UtcNow,
                        CreatedBy = uAdmin.Id,
                        Owner = "Finance"
                    };
                    var rSysLogs = new Report
                    {
                        FolderId = folderOperations.Id,
                        Name = "SystemLogs",
                        Description = "System log viewer",
                        ScriptPath = pSysLogs,
                        ScriptLastModified = DateTime.UtcNow,
                        CreatedBy = uAdmin.Id,
                        Owner = "Ops"
                    };
                    var rErrorAudit = new Report
                    {
                        FolderId = folderLogs.Id,
                        Name = "ErrorAudit",
                        Description = "Error log audits",
                        ScriptPath = pErrAudit,
                        ScriptLastModified = DateTime.UtcNow,
                        CreatedBy = uAdmin.Id,
                        Owner = "Ops"
                    };

                    db.Reports.AddRange(rRevenue, rInvoices, rSysLogs, rErrorAudit);
                    await db.SaveChangesAsync();

                    var snapshotPath = Path.Combine(_factory.TempDir, "snapshots", "RevenueReport.snapshot.json");
                    await File.WriteAllTextAsync(snapshotPath, "{}");
                    db.ReportSnapshots.Add(new ReportSnapshot
                    {
                        ReportId = rRevenue.Id,
                        ManifestPath = snapshotPath,
                        BuiltAt = DateTime.UtcNow,
                        BuiltBy = uAdmin.Id
                    });
                    await db.SaveChangesAsync();

                    // 6. Seed Datasets using IDatasetRegistry
                    var datasetDir = Path.Combine(_factory.TempDir, "datasets");
                    Directory.CreateDirectory(datasetDir);

                    await registry.RegisterOrUpdate(new DatasetMetadata
                    {
                        Name = "PublicDataset",
                        FolderPath = "/Finance",
                        ParquetFilePath = Path.Combine(datasetDir, "PublicDataset.parquet"),
                        SourceQuery = "SELECT * FROM public_source",
                        AccessLevel = DatasetAccessLevel.Public,
                        RowCount = 100,
                        ColumnSchema = "[]",
                        LastRefresh = DateTime.UtcNow
                    });

                    await registry.RegisterOrUpdate(new DatasetMetadata
                    {
                        Name = "FinancePrivateDataset",
                        FolderPath = "/Finance",
                        ParquetFilePath = Path.Combine(datasetDir, "FinancePrivateDataset.parquet"),
                        SourceQuery = "SELECT * FROM finance_secret",
                        AccessLevel = DatasetAccessLevel.Private,
                        RowCount = 50,
                        ColumnSchema = "[]",
                        LastRefresh = DateTime.UtcNow,
                        OwningReportId = rRevenue.Id
                    });

                    await registry.RegisterOrUpdate(new DatasetMetadata
                    {
                        Name = "OpsPrivateDataset",
                        FolderPath = "/Operations",
                        ParquetFilePath = Path.Combine(datasetDir, "OpsPrivateDataset.parquet"),
                        SourceQuery = "SELECT * FROM ops_secret",
                        AccessLevel = DatasetAccessLevel.Private,
                        RowCount = 200,
                        ColumnSchema = "[]",
                        LastRefresh = DateTime.UtcNow,
                        OwningReportId = rSysLogs.Id
                    });

                    // Set dataset ACLs in EF
                    var dbFinPrivate = await db.Datasets.SingleAsync(d => d.Name == "FinancePrivateDataset");
                    var dbOpsPrivate = await db.Datasets.SingleAsync(d => d.Name == "OpsPrivateDataset");

                    db.DatasetAcls.AddRange(
                        new DatasetAcl { DatasetId = dbFinPrivate.Id, GroupId = gFinanceReaders.Id, Permission = DatasetPermission.Viewer },
                        new DatasetAcl { DatasetId = dbFinPrivate.Id, GroupId = gFinancePublishers.Id, Permission = DatasetPermission.Editor },
                        new DatasetAcl { DatasetId = dbOpsPrivate.Id, GroupId = gOperationsReaders.Id, Permission = DatasetPermission.Viewer },
                        new DatasetAcl { DatasetId = dbOpsPrivate.Id, GroupId = gManagers.Id, Permission = DatasetPermission.Editor }
                    );
                    await db.SaveChangesAsync();

                    // 7. Seed SMTP Connection
                    db.SmtpConnections.Add(new SmtpConnection
                    {
                        Alias = "default-smtp",
                        Host = "localhost",
                        Port = 25,
                        Username = "smtp-user",
                        EncryptedPassword = "encrypted-pwd-dummy",
                        FromAddress = "portal@test.local",
                        UseSsl = false
                    });

                    // 8. Seed Saved View
                    db.SavedReportViews.Add(new SavedReportView
                    {
                        ReportId = rRevenue.Id,
                        UserId = uFinRead.Id,
                        Name = "Default Finance View",
                        ParametersJson = "{}",
                        FiltersJson = "{}",
                        IsDefault = true
                    });

                    // 9. Seed Subscription
                    db.Subscriptions.Add(new Subscription
                    {
                        ReportId = rRevenue.Id,
                        UserId = uFinRead.Id,
                        Schedule = "0 9 * * *",
                        Format = SubscriptionFormat.PDF,
                        SmtpAlias = "default-smtp",
                        Recipients = "finance_read@test.local",
                        IsActive = true
                    });

                    // 10. Seed Alert
                    db.ReportAlerts.Add(new ReportAlert
                    {
                        ReportId = rRevenue.Id,
                        OwnerId = uFinRead.Id,
                        Name = "Revenue Alert",
                        VisualName = "Card1",
                        Operator = ">=",
                        Threshold = 10000,
                        Recipient = "finance_read@test.local",
                        IsActive = true
                    });

                    // 11. Seed Share Link
                    db.ReportShareLinks.Add(new ReportShareLink
                    {
                        ReportId = rRevenue.Id,
                        CreatedBy = uFinPub.Id,
                        Token = "fin-share-token-xyz123",
                        ExpiresAt = DateTime.UtcNow.AddDays(7)
                    });

                    // 12. Seed Embed Token
                    db.ReportEmbedTokens.Add(new ReportEmbedToken
                    {
                        ReportId = rRevenue.Id,
                        CreatedBy = uFinPub.Id,
                        Name = "Intranet Embed",
                        Token = "fin-embed-token-abc987",
                        ExpiresAt = DateTime.UtcNow.AddDays(30)
                    });

                    await db.SaveChangesAsync();
                }
                _dbInitialized = true;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private async Task<string> GetAdminTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                if (_adminToken is not null) return _adminToken;

                // SeedFirstRunAsync creates user "admin" with password "Admin@12345!"
                var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
                {
                    username = "admin",
                    password = "Admin@12345!"
                });
                loginRes.EnsureSuccessStatusCode();
                var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
                var token = body!["token"]!.GetValue<string>();

                // Must change password
                using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
                cpReq.Headers.Authorization = new("Bearer", token);
                cpReq.Content = JsonContent.Create(new
                {
                    currentPassword = "Admin@12345!",
                    newPassword     = "Admin@Tests99!"
                });
                var cpRes = await _client.SendAsync(cpReq);
                cpRes.EnsureSuccessStatusCode();

                // Get fresh token
                var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
                {
                    username = "admin",
                    password = "Admin@Tests99!"
                });
                reloginRes.EnsureSuccessStatusCode();
                var reloginBody = await reloginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
                _adminToken = reloginBody!["token"]!.GetValue<string>();

                return _adminToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task<string> GetUserTokenAsync(string username, string password = "Password@1234!")
        {
            var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username,
                password
            });
            loginRes.EnsureSuccessStatusCode();
            var body = await loginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            return body!["token"]!.GetValue<string>();
        }

        private List<string> FlattenFolderPaths(JsonArray tree)
        {
            var paths = new List<string>();
            void Traverse(JsonObject node)
            {
                paths.Add(node["path"]!.GetValue<string>());
                var children = node["children"]?.AsArray();
                if (children is not null)
                {
                    foreach (var child in children)
                    {
                        Traverse(child!.AsObject());
                    }
                }
            }
            foreach (var root in tree)
            {
                Traverse(root!.AsObject());
            }
            return paths;
        }

        private Task<HttpResponseMessage> AuthGet(string token, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new("Bearer", token);
            return _client.SendAsync(req);
        }

        private Task<HttpResponseMessage> AuthPost(string token, string url, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new("Bearer", token);
            req.Content = JsonContent.Create(body);
            return _client.SendAsync(req);
        }

        private Task<HttpResponseMessage> AuthPut(string token, string url, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Authorization = new("Bearer", token);
            req.Content = JsonContent.Create(body);
            return _client.SendAsync(req);
        }

        private Task<HttpResponseMessage> AuthDelete(string token, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Authorization = new("Bearer", token);
            return _client.SendAsync(req);
        }

        // ── Test Cases ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Setup_VerifyDataIsSeeded()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

            Assert.Equal(4, await db.Folders.CountAsync());
            Assert.Equal(5, await db.Groups.CountAsync());
            Assert.Equal(4, await db.Reports.CountAsync());
            Assert.Equal(3, await db.Datasets.CountAsync());
        }

        [Fact]
        public async Task Auth_VerifyEdgeCases()
        {
            // 1. Deactivated user login returns 401
            var inactiveRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "inactive_user",
                password = "Password@1234!"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, inactiveRes.StatusCode);

            // 2. Must-change-password user gets 403 Forbidden with redirect on regular APIs
            var mcpLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "mcp_user",
                password = "Password@1234!"
            });
            Assert.Equal(HttpStatusCode.OK, mcpLoginRes.StatusCode);
            var mcpBody = await mcpLoginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var mcpToken = mcpBody!["token"]!.GetValue<string>();

            var apiRes = await AuthGet(mcpToken, "/api/folders");
            Assert.Equal(HttpStatusCode.Forbidden, apiRes.StatusCode);
            var blockBody = await apiRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            Assert.NotNull(blockBody!["redirect"]);

            // 3. Revoked refresh tokens stop working
            var revokedLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "revoked_user",
                password = "Password@1234!"
            });
            Assert.Equal(HttpStatusCode.OK, revokedLoginRes.StatusCode);
            var revokedBody = await revokedLoginRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var refreshToken = revokedBody!["refreshToken"]!.GetValue<string>();

            // Revoke via admin
            var adminToken = await GetAdminTokenAsync();
            int revokedUserId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var u = await db.Users.SingleAsync(x => x.UserName == "revoked_user");
                revokedUserId = u.Id;
            }

            var revokeRes = await AuthPost(adminToken, $"/api/admin/users/{revokedUserId}/revoke-tokens", new { });
            Assert.Equal(HttpStatusCode.NoContent, revokeRes.StatusCode);

            // Verify refresh fails
            var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshRes.StatusCode);
        }

        [Fact]
        public async Task EffectivePermissions_VerifyFolderAndReportPermissions()
        {
            var adminToken = await GetAdminTokenAsync();

            int uAdminId, uFinPubId, uFinReadId, uOpsReadId, uManagerId, uOutsiderId, uNoGroupId;
            int fFinanceId, fInvoicesId, fOperationsId, fLogsId;

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                uAdminId = (await db.Users.SingleAsync(u => u.UserName == "admin_user")).Id;
                uFinPubId = (await db.Users.SingleAsync(u => u.UserName == "finance_pub")).Id;
                uFinReadId = (await db.Users.SingleAsync(u => u.UserName == "finance_read")).Id;
                uOpsReadId = (await db.Users.SingleAsync(u => u.UserName == "ops_read")).Id;
                uManagerId = (await db.Users.SingleAsync(u => u.UserName == "manager_user")).Id;
                uOutsiderId = (await db.Users.SingleAsync(u => u.UserName == "outsider_user")).Id;
                uNoGroupId = (await db.Users.SingleAsync(u => u.UserName == "no_group_user")).Id;

                fFinanceId = (await db.Folders.SingleAsync(f => f.Path == "/Finance")).Id;
                fInvoicesId = (await db.Folders.SingleAsync(f => f.Path == "/Finance/Invoices")).Id;
                fOperationsId = (await db.Folders.SingleAsync(f => f.Path == "/Operations")).Id;
                fLogsId = (await db.Folders.SingleAsync(f => f.Path == "/Operations/Logs")).Id;
            }

            // Assertions for User Effective Permissions
            async Task AssertFolderPermission(int userId, int folderId, string? expectedPermission)
            {
                var res = await AuthGet(adminToken, $"/api/admin/permissions/effective/user/{userId}");
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                var dto = await res.Content.ReadFromJsonAsync<EffectiveUserPermissionsDto>(_json);
                Assert.NotNull(dto);

                var folderEntry = dto.Folders.FirstOrDefault(f => f.ResourceId == folderId);
                if (expectedPermission is null)
                {
                    Assert.Null(folderEntry);
                }
                else
                {
                    Assert.NotNull(folderEntry);
                    Assert.Equal(expectedPermission, folderEntry.Permission);
                }
            }

            // admin_user (has no group, so effective group-resolved permission is null, though they can manage as Admin)
            await AssertFolderPermission(uAdminId, fFinanceId, null);
            await AssertFolderPermission(uAdminId, fInvoicesId, null);
            await AssertFolderPermission(uAdminId, fOperationsId, null);
            await AssertFolderPermission(uAdminId, fLogsId, null);

            // finance_pub
            await AssertFolderPermission(uFinPubId, fFinanceId, "Execute");
            await AssertFolderPermission(uFinPubId, fInvoicesId, "Manage");
            await AssertFolderPermission(uFinPubId, fOperationsId, null);

            // finance_read
            await AssertFolderPermission(uFinReadId, fFinanceId, "Read");
            await AssertFolderPermission(uFinReadId, fInvoicesId, "Read");
            await AssertFolderPermission(uFinReadId, fOperationsId, null);

            // ops_read
            await AssertFolderPermission(uOpsReadId, fFinanceId, null);
            await AssertFolderPermission(uOpsReadId, fOperationsId, "Read");
            await AssertFolderPermission(uOpsReadId, fLogsId, "Read");

            // manager_user (Execute / Finance, Read / Invoices, Read / Operations, Execute / Logs)
            await AssertFolderPermission(uManagerId, fFinanceId, "Execute");
            await AssertFolderPermission(uManagerId, fInvoicesId, "Read");
            await AssertFolderPermission(uManagerId, fOperationsId, "Read");
            await AssertFolderPermission(uManagerId, fLogsId, "Execute");

            // outsider_user
            await AssertFolderPermission(uOutsiderId, fFinanceId, null);
            await AssertFolderPermission(uOutsiderId, fOperationsId, null);

            // no_group_user
            await AssertFolderPermission(uNoGroupId, fFinanceId, null);
            await AssertFolderPermission(uNoGroupId, fOperationsId, null);

            // Test Folder Effective Permissions for all users
            var folderRes = await AuthGet(adminToken, $"/api/admin/permissions/effective/folder/{fFinanceId}");
            Assert.Equal(HttpStatusCode.OK, folderRes.StatusCode);
            var folderUsers = await folderRes.Content.ReadFromJsonAsync<List<EffectivePrincipalPermissionDto>>(_json);
            Assert.NotNull(folderUsers);

            var managerPermission = folderUsers.Single(u => u.UserId == uManagerId);
            Assert.Equal("Execute", managerPermission.Permission);
            Assert.Contains("GROUP Managers", managerPermission.Sources);
        }

        [Fact]
        public async Task FolderAccess_VerifyFilteringAndDirectFetch()
        {
            var tFinRead = await GetUserTokenAsync("finance_read");
            var tOpsRead = await GetUserTokenAsync("ops_read");
            var tOutsider = await GetUserTokenAsync("outsider_user");

            int fFinanceId, fOperationsId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                fFinanceId = (await db.Folders.SingleAsync(f => f.Path == "/Finance")).Id;
                fOperationsId = (await db.Folders.SingleAsync(f => f.Path == "/Operations")).Id;
            }

            // 1. List folders
            var resFinList = await AuthGet(tFinRead, "/api/folders");
            Assert.Equal(HttpStatusCode.OK, resFinList.StatusCode);
            var listFin = await resFinList.Content.ReadFromJsonAsync<JsonArray>(_json);
            var pathsFin = FlattenFolderPaths(listFin!);
            // Finance reader should see /Finance and /Finance/Invoices
            Assert.Contains("/Finance", pathsFin);
            Assert.Contains("/Finance/Invoices", pathsFin);
            Assert.DoesNotContain("/Operations", pathsFin);

            var resOpsList = await AuthGet(tOpsRead, "/api/folders");
            Assert.Equal(HttpStatusCode.OK, resOpsList.StatusCode);
            var listOps = await resOpsList.Content.ReadFromJsonAsync<JsonArray>(_json);
            var pathsOps = FlattenFolderPaths(listOps!);
            // Operations reader should see /Operations and /Operations/Logs
            Assert.Contains("/Operations", pathsOps);
            Assert.Contains("/Operations/Logs", pathsOps);
            Assert.DoesNotContain("/Finance", pathsOps);

            var resOutsiderList = await AuthGet(tOutsider, "/api/folders");
            Assert.Equal(HttpStatusCode.OK, resOutsiderList.StatusCode);
            var listOutsider = await resOutsiderList.Content.ReadFromJsonAsync<JsonArray>(_json);
            var pathsOutsider = FlattenFolderPaths(listOutsider!);
            Assert.Empty(pathsOutsider);

            // 2. Direct folder detail fetch
            // finance_read fetching /Finance -> OK
            var resFinDetail = await AuthGet(tFinRead, $"/api/folders/{fFinanceId}");
            Assert.Equal(HttpStatusCode.OK, resFinDetail.StatusCode);

            // finance_read fetching /Operations -> 403 Forbidden
            var resFinOpsDetail = await AuthGet(tFinRead, $"/api/folders/{fOperationsId}");
            Assert.Equal(HttpStatusCode.Forbidden, resFinOpsDetail.StatusCode);
        }

        [Fact]
        public async Task ReportAccess_VerifyDirectFetchAndExecute()
        {
            var tFinRead = await GetUserTokenAsync("finance_read");
            var tFinPub = await GetUserTokenAsync("finance_pub");
            var tOpsRead = await GetUserTokenAsync("ops_read");

            int rRevenueId, rSysLogsId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                rRevenueId = (await db.Reports.SingleAsync(r => r.Name == "RevenueReport")).Id;
                rSysLogsId = (await db.Reports.SingleAsync(r => r.Name == "SystemLogs")).Id;
            }

            // 1. Direct GET Details
            // finance_read has Read on Finance -> can view RevenueReport
            var resFinReport = await AuthGet(tFinRead, $"/api/reports/{rRevenueId}");
            Assert.Equal(HttpStatusCode.OK, resFinReport.StatusCode);

            // finance_read has no access to Operations -> cannot view SystemLogs (Forbid)
            var resFinOpsReport = await AuthGet(tFinRead, $"/api/reports/{rSysLogsId}");
            Assert.Equal(HttpStatusCode.Forbidden, resFinOpsReport.StatusCode);

            // 2. Execution / Refresh
            // finance_read has only Read on Finance -> cannot Execute (returns Forbid)
            var resFinReadExec = await AuthPost(tFinRead, $"/api/reports/{rRevenueId}/refresh", new { });
            Assert.Equal(HttpStatusCode.Forbidden, resFinReadExec.StatusCode);

            // finance_pub has Execute on Finance -> can Execute (returns Accepted or OK depending on controller, let's check: 202 Accepted)
            var resFinPubExec = await AuthPost(tFinPub, $"/api/reports/{rRevenueId}/refresh", new { });
            Assert.True(resFinPubExec.StatusCode == HttpStatusCode.Accepted || resFinPubExec.StatusCode == HttpStatusCode.OK);

            // 3. Metadata updates
            // finance_pub has Execute/Manage on /Finance but Reports edit requires Manage.
            // Let's verify: finance_pub has Manage on Invoices -> can edit InvoiceDetails report metadata.
            // finance_pub has only Execute on /Finance -> cannot edit RevenueReport metadata.
            var resFinPubEditRevenue = await AuthPut(tFinPub, $"/api/reports/{rRevenueId}", new { description = "Updated by Pub" });
            Assert.Equal(HttpStatusCode.Forbidden, resFinPubEditRevenue.StatusCode);
        }

        [Fact]
        public async Task ReportWorkflows_VerifyVisibilityAndCreationPermissions()
        {
            var tFinRead = await GetUserTokenAsync("finance_read");
            var tFinPub = await GetUserTokenAsync("finance_pub");
            var tOutsider = await GetUserTokenAsync("outsider_user");

            int fFinanceId, fInvoicesId, rRevenueId, rInvoiceId, rSysLogsId;
            string invoiceScriptPath;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                fFinanceId = (await db.Folders.SingleAsync(f => f.Path == "/Finance")).Id;
                fInvoicesId = (await db.Folders.SingleAsync(f => f.Path == "/Finance/Invoices")).Id;
                rRevenueId = (await db.Reports.SingleAsync(r => r.Name == "RevenueReport")).Id;
                var invoice = await db.Reports.SingleAsync(r => r.Name == "InvoiceDetails");
                rInvoiceId = invoice.Id;
                invoiceScriptPath = invoice.ScriptPath;
                rSysLogsId = (await db.Reports.SingleAsync(r => r.Name == "SystemLogs")).Id;
            }

            var visibleList = await AuthGet(tFinRead, $"/api/folders/{fFinanceId}/reports");
            Assert.Equal(HttpStatusCode.OK, visibleList.StatusCode);
            var visibleReports = await visibleList.Content.ReadFromJsonAsync<JsonArray>(_json);
            Assert.Contains(visibleReports!, r => r!["name"]!.GetValue<string>() == "RevenueReport");

            var hiddenList = await AuthGet(tFinRead, "/api/catalog/search?q=SystemLogs");
            Assert.Equal(HttpStatusCode.OK, hiddenList.StatusCode);
            var hiddenSearchResults = await hiddenList.Content.ReadFromJsonAsync<JsonArray>(_json);
            Assert.Empty(hiddenSearchResults!);

            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(tFinRead, $"/api/reports/{rSysLogsId}/snapshot")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(tFinRead, $"/api/reports/{rSysLogsId}/export/csv")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await AuthGet(tFinRead, $"/api/reports/{rRevenueId}/snapshot")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthPost(tFinRead, $"/api/reports/{rRevenueId}/favorite", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(tFinRead, $"/api/reports/{rSysLogsId}/favorite", new { })).StatusCode);

            var savedView = await AuthPost(tFinRead, $"/api/reports/{rRevenueId}/saved-views", new
            {
                name = "Finance workflow view",
                parameters = new Dictionary<string, string>(),
                filters = new Dictionary<string, string>(),
                isDefault = false
            });
            Assert.Equal(HttpStatusCode.Created, savedView.StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(tFinRead, $"/api/reports/{rRevenueId}/alerts", new
            {
                name = "Read-only alert",
                visualName = "Card1",
                @operator = ">=",
                threshold = 1,
                recipient = "finance_read@test.local"
            })).StatusCode);

            Assert.Equal(HttpStatusCode.Created, (await AuthPost(tFinPub, $"/api/reports/{rRevenueId}/alerts", new
            {
                name = "Publisher alert",
                visualName = "Card1",
                @operator = ">=",
                threshold = 1,
                recipient = "finance_pub@test.local"
            })).StatusCode);

            Assert.Equal(HttpStatusCode.Created, (await AuthPost(tFinRead, "/api/subscriptions", new
            {
                reportId = rRevenueId,
                schedule = "Daily",
                format = "Link",
                recipientEmail = "finance_read@test.local",
                atTime = "08:00"
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(tOutsider, "/api/subscriptions", new
            {
                reportId = rRevenueId,
                schedule = "Daily",
                format = "Link",
                recipientEmail = "outsider_user@test.local",
                atTime = "08:00"
            })).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(tFinRead, "/api/reports", new
            {
                folderId = fInvoicesId,
                name = "Viewer publish attempt",
                scriptPath = invoiceScriptPath
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthPost(tFinPub, "/api/folders", new
            {
                name = "PublisherRoot",
                parentId = (int?)null
            })).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await AuthPut(tFinPub, $"/api/reports/{rInvoiceId}", new
            {
                description = "Updated by authorized publisher"
            })).StatusCode);
        }

        [Fact]
        public async Task DatasetAccess_VerifyPermissionsAndAcls()
        {
            var tFinRead = await GetUserTokenAsync("finance_read");
            var tFinPub = await GetUserTokenAsync("finance_pub");
            var tManager = await GetUserTokenAsync("manager_user");

            int dsFinPrivateId, dsOpsPrivateId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                dsFinPrivateId = (await db.Datasets.SingleAsync(d => d.Name == "FinancePrivateDataset")).Id;
                dsOpsPrivateId = (await db.Datasets.SingleAsync(d => d.Name == "OpsPrivateDataset")).Id;
            }

            // 1. List datasets
            // finance_read has Viewer ACL on FinancePrivateDataset -> should see it
            var resFinList = await AuthGet(tFinRead, "/api/datasets");
            Assert.Equal(HttpStatusCode.OK, resFinList.StatusCode);
            var listFin = await resFinList.Content.ReadFromJsonAsync<JsonArray>(_json);
            Assert.Contains(listFin!, d => d!["name"]!.GetValue<string>() == "FinancePrivateDataset");
            Assert.DoesNotContain(listFin!, d => d!["name"]!.GetValue<string>() == "OpsPrivateDataset");

            // manager_user has Editor ACL on OpsPrivateDataset, but no ACL on FinancePrivateDataset (even though they can see Finance reports)
            var resManagerList = await AuthGet(tManager, "/api/datasets");
            Assert.Equal(HttpStatusCode.OK, resManagerList.StatusCode);
            var listManager = await resManagerList.Content.ReadFromJsonAsync<JsonArray>(_json);
            Assert.Contains(listManager!, d => d!["name"]!.GetValue<string>() == "OpsPrivateDataset");
            Assert.DoesNotContain(listManager!, d => d!["name"]!.GetValue<string>() == "FinancePrivateDataset");

            // 2. Direct dataset details
            // manager_user accessing FinancePrivateDataset directly -> 403 Forbidden
            var resManagerDirect = await AuthGet(tManager, $"/api/datasets/{dsFinPrivateId}");
            Assert.Equal(HttpStatusCode.Forbidden, resManagerDirect.StatusCode);

            // finance_pub has Editor on FinancePrivateDataset -> OK
            var resFinPubDirect = await AuthGet(tFinPub, $"/api/datasets/{dsFinPrivateId}");
            Assert.Equal(HttpStatusCode.OK, resFinPubDirect.StatusCode);

            // 3. ACL management
            // finance_pub is Editor, not Owner -> cannot manage ACL (returns Forbid)
            var resFinPubGrant = await AuthPost(tFinPub, $"/api/datasets/{dsFinPrivateId}/acl", new { GroupId = 1, Permission = "Viewer" });
            Assert.Equal(HttpStatusCode.Forbidden, resFinPubGrant.StatusCode);

            // admin is Owner/Admin -> can manage ACL
            var adminToken = await GetAdminTokenAsync();
            var resAdminGrant = await AuthPost(adminToken, $"/api/datasets/{dsFinPrivateId}/acl", new { GroupId = 1, Permission = "Viewer" });
            Assert.Equal(HttpStatusCode.OK, resAdminGrant.StatusCode);
        }

        [Fact]
        public async Task AdminOnlySurfaces_VerifyForbiddenForNonAdmins()
        {
            var tFinPub = await GetUserTokenAsync("finance_pub");

            // 1. User CRUD -> 403 Forbidden
            var resUsers = await AuthGet(tFinPub, "/api/admin/users");
            Assert.Equal(HttpStatusCode.Forbidden, resUsers.StatusCode);

            // 2. Group CRUD -> 403 Forbidden
            var resGroups = await AuthGet(tFinPub, "/api/admin/groups");
            Assert.Equal(HttpStatusCode.Forbidden, resGroups.StatusCode);

            // 3. SMTP Administration -> 403 Forbidden
            var resSmtp = await AuthGet(tFinPub, "/api/admin/smtp");
            Assert.Equal(HttpStatusCode.Forbidden, resSmtp.StatusCode);

            // 4. Usage Metrics -> 403 Forbidden
            var resMetrics = await AuthGet(tFinPub, "/api/admin/metrics/usage");
            Assert.Equal(HttpStatusCode.Forbidden, resMetrics.StatusCode);

            // 5. Audit Log -> 403 Forbidden
            var resAudit = await AuthGet(tFinPub, "/api/admin/audit");
            Assert.Equal(HttpStatusCode.Forbidden, resAudit.StatusCode);

            // 6. Orchestrator settings/service -> 403 Forbidden
            var resRestart = await AuthPost(tFinPub, "/api/admin/service/restart", new { });
            Assert.Equal(HttpStatusCode.Forbidden, resRestart.StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(tFinPub, "/api/admin/audit/export/csv")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(tFinPub, "/api/orchestrator/status")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(tFinPub, "/api/admin/permissions/effective/user/1")).StatusCode);
        }

        [Fact]
        public async Task AuditLog_VerifyExpectations()
        {
            var adminToken = await GetAdminTokenAsync();

            // 1. Create a user via Admin API to generate CREATE_USER audit log
            var createUserRes = await AuthPost(adminToken, "/api/admin/users", new
            {
                username = $"audit_user_{Guid.NewGuid():N}"[..20],
                email = "audit@test.local",
                password = "Password@1234!",
                role = "Viewer",
                firstName = "Audit",
                lastName = "User"
            });
            Assert.Equal(HttpStatusCode.Created, createUserRes.StatusCode);
            var createdUser = await createUserRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var createdUserId = createdUser!["id"]!.GetValue<int>();

            var createGroupRes = await AuthPost(adminToken, "/api/admin/groups", new
            {
                name = $"Audit Group {Guid.NewGuid():N}"[..24],
                description = "Temporary audit verification group"
            });
            Assert.Equal(HttpStatusCode.Created, createGroupRes.StatusCode);
            var createdGroup = await createGroupRes.Content.ReadFromJsonAsync<JsonObject>(_json);
            var groupId = createdGroup!["id"]!.GetValue<int>();

            // 2. Grant folder permission via Admin API to generate GRANT_PERMISSION audit log
            int folderId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                folderId = (await db.Folders.FirstAsync()).Id;
            }

            var grantRes = await AuthPost(adminToken, $"/api/folders/{folderId}/acl", new
            {
                GroupId = groupId,
                Permission = 0
            });
            Assert.True(grantRes.StatusCode == HttpStatusCode.NoContent || grantRes.StatusCode == HttpStatusCode.OK);

            Assert.Equal(HttpStatusCode.NoContent, (await AuthPost(adminToken, $"/api/admin/groups/{groupId}/members", new
            {
                userId = createdUserId
            })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthDelete(adminToken, $"/api/admin/groups/{groupId}/members/{createdUserId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthPost(adminToken, $"/api/admin/users/{createdUserId}/revoke-tokens", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthDelete(adminToken, $"/api/folders/{folderId}/acl/{groupId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthDelete(adminToken, $"/api/admin/users/{createdUserId}?cascade=true")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthDelete(adminToken, $"/api/admin/groups/{groupId}")).StatusCode);

            // 3. Fetch audit logs and verify
            var resAudit = await AuthGet(adminToken, "/api/admin/audit?pageSize=200");
            Assert.Equal(HttpStatusCode.OK, resAudit.StatusCode);

            var body = await resAudit.Content.ReadFromJsonAsync<PagedResult<AuditLogDto>>(_json);
            Assert.NotNull(body);

            var actions = body.Items.Select(x => x.Action).ToList();

            // Verify CREATE_USER is logged
            Assert.Contains("CREATE_USER", actions);
            Assert.Contains("CREATE_GROUP", actions);

            // Verify GRANT_PERMISSION is logged
            Assert.Contains("GRANT_PERMISSION", actions);
            Assert.Contains("ADD_USER_TO_GROUP", actions);
            Assert.Contains("REMOVE_USER_FROM_GROUP", actions);
            Assert.Contains("REVOKE_TOKENS", actions);
            Assert.Contains("REVOKE_PERMISSION", actions);
            Assert.Contains("DELETE_USER", actions);
            Assert.Contains("DELETE_GROUP", actions);
        }
    }
}
