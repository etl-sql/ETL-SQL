using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.TestSupport;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// P2.3 certification seed: starts real Portal OS processes against one PostgreSQL catalog and shared
/// artifact roots. This intentionally does not use WebApplicationFactory or in-process service proxies.
/// Docker/PostgreSQL availability is required, like <see cref="PortalPostgresProviderTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PortalMultiProcessPostgresTests : IAsyncLifetime
{
    private const string JwtSecret = "multiprocess-test-secret-key-1234567890";
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"portal_multiproc_{Guid.NewGuid():N}");
    private readonly List<PortalProcess> _processes = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return _pg.StartAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var process in _processes)
            await process.DisposeAsync();
        await _pg.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task TwoPortalProcesses_StartAgainstSamePostgresAndSharedStorage()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        using var client = new HttpClient();
        var a = await client.GetFromJsonAsync<HealthzResponse>($"{first.BaseUrl}/healthz");
        var b = await client.GetFromJsonAsync<HealthzResponse>($"{second.BaseUrl}/healthz");

        Assert.Equal("Healthy", a?.Status);
        Assert.Equal("Healthy", b?.Status);
        Assert.Equal("ok", a?.Checks["database"]);
        Assert.Equal("ok", b?.Checks["database"]);
        Assert.Equal("ok", a?.Checks["storage"]);
        Assert.Equal("ok", b?.Checks["storage"]);
        Assert.Equal("ok", a?.Checks["lease"]);
        Assert.Equal("ok", b?.Checks["lease"]);
    }

    [Fact]
    public async Task DatabaseOutage_HealthzFailsClosed()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        _processes.Add(first);

        await first.WaitForHealthzAsync();

        using var client = new HttpClient();
        await _pg.StopAsync();

        var degraded = await WaitForHealthzStatusAsync(
            client,
            first,
            HttpStatusCode.ServiceUnavailable);

        Assert.Equal("Unhealthy", degraded.Body.Status);
        Assert.NotEqual("ok", degraded.Body.Checks["database"]);
    }

    [Fact]
    public async Task DatabaseNetworkPartition_HealthzFailsClosedAndRecovers()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        _processes.Add(first);

        await first.WaitForHealthzAsync();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        await _pg.PauseAsync();
        try
        {
            var degraded = await WaitForHealthzStatusAsync(
                client,
                first,
                HttpStatusCode.ServiceUnavailable);

            Assert.Equal("Unhealthy", degraded.Body.Status);
            Assert.NotEqual("ok", degraded.Body.Checks["database"]);
        }
        finally
        {
            await _pg.UnpauseAsync();
        }

        var recovered = await WaitForHealthzStatusAsync(client, first, HttpStatusCode.OK);
        Assert.Equal("Healthy", recovered.Body.Status);
        Assert.Equal("ok", recovered.Body.Checks["database"]);
    }

    [Fact]
    public async Task CatalogWrites_AreVisibleAcrossProcesses_AndSurviveRestart()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        using var client = new HttpClient();
        var token = await BootstrapAdminTokenAsync(client, first);
        var created = await SendJsonAsync<FolderDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/folders",
            token,
            new { name = $"multiproc-{Guid.NewGuid():N}", parentId = (int?)null },
            HttpStatusCode.Created);

        var foldersFromSecond = await GetJsonAsync<List<FolderDto>>(
            client,
            $"{second.BaseUrl}/api/folders",
            token);
        Assert.Contains(foldersFromSecond, f => f.Id == created.Id && f.Path == created.Path);

        await second.DisposeAsync();
        var restarted = StartPortal(shared, port: FreePort(), nodeName: "node-b-restarted");
        _processes.Add(restarted);
        await restarted.WaitForHealthzAsync();

        var foldersAfterRestart = await GetJsonAsync<List<FolderDto>>(
            client,
            $"{restarted.BaseUrl}/api/folders",
            token);
        Assert.Contains(foldersAfterRestart, f => f.Id == created.Id && f.Path == created.Path);
    }

    [Fact]
    public async Task ConflictingFolderAdministration_ConvergesAcrossProcesses()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        using var client = new HttpClient();
        var token = await BootstrapAdminTokenAsync(client, first);
        var folderName = $"conflict-{Guid.NewGuid():N}";
        var body = new { name = folderName, parentId = (int?)null };

        var results = await Task.WhenAll(
            SendJsonForStatusAsync(client, HttpMethod.Post, $"{first.BaseUrl}/api/folders", token, body),
            SendJsonForStatusAsync(client, HttpMethod.Post, $"{second.BaseUrl}/api/folders", token, body));

        Assert.Contains(HttpStatusCode.Created, results);
        Assert.Contains(HttpStatusCode.Conflict, results);

        var folders = await GetJsonAsync<List<FolderDto>>(client, $"{first.BaseUrl}/api/folders", token);
        Assert.Single(folders, f => f.Path == $"/{folderName}");
    }

    [Fact]
    public async Task PermissionMutationRace_ConvergesAcrossProcesses()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        using var client = new HttpClient();
        var token = await BootstrapAdminTokenAsync(client, first);
        var group = await SendJsonAsync<GroupDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/admin/groups",
            token,
            new { name = $"perm-race-{Guid.NewGuid():N}" },
            HttpStatusCode.Created);
        var folder = await SendJsonAsync<FolderDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/folders",
            token,
            new { name = $"perm-race-{Guid.NewGuid():N}", parentId = (int?)null },
            HttpStatusCode.Created);

        var results = await Task.WhenAll(
            SendJsonForStatusAsync(
                client,
                HttpMethod.Post,
                $"{first.BaseUrl}/api/folders/{folder.Id}/acl",
                token,
                new { groupId = group.Id, permission = FolderPermission.Read },
                ifMatchVersion: folder.Version),
            SendJsonForStatusAsync(
                client,
                HttpMethod.Post,
                $"{second.BaseUrl}/api/folders/{folder.Id}/acl",
                token,
                new { groupId = group.Id, permission = FolderPermission.Manage },
                ifMatchVersion: folder.Version));

        Assert.Contains(HttpStatusCode.OK, results);
        Assert.Contains(HttpStatusCode.Conflict, results);

        var acl = await GetJsonAsync<List<FolderAclDto>>(
            client,
            $"{second.BaseUrl}/api/folders/{folder.Id}/acl",
            token);
        var entry = Assert.Single(acl, value => value.GroupId == group.Id);
        Assert.Contains(entry.Permission, new[] { FolderPermission.Read, FolderPermission.Manage });
    }

    [Fact]
    public async Task SimultaneousRefreshClaims_ConvergeAcrossProcesses()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        var scriptPath = Path.Combine(shared.Scripts, $"claim-race-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(scriptPath, """
            WAITFOR DELAY '00:00:02';
            CREATE VISUAL ClaimRace AS TABLE (
                SOURCE = (SELECT 42 AS Value),
                MAPPINGS (Value = Value)
            );
            """);

        using var client = new HttpClient();
        var token = await BootstrapAdminTokenAsync(client, first);
        var folder = await SendJsonAsync<FolderDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/folders",
            token,
            new { name = $"claim-race-{Guid.NewGuid():N}", parentId = (int?)null },
            HttpStatusCode.Created);
        var report = await SendJsonAsync<ReportDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/reports",
            token,
            new
            {
                folderId = folder.Id,
                name = $"Claim Race {Guid.NewGuid():N}",
                description = "",
                scriptPath
            },
            HttpStatusCode.Created);

        var claims = await Task.WhenAll(
            SendJsonAsync<RefreshDto>(
                client,
                HttpMethod.Post,
                $"{first.BaseUrl}/api/reports/{report.Id}/refresh",
                token,
                new { },
                HttpStatusCode.Accepted),
            SendJsonAsync<RefreshDto>(
                client,
                HttpMethod.Post,
                $"{second.BaseUrl}/api/reports/{report.Id}/refresh",
                token,
                new { },
                HttpStatusCode.Accepted));

        Assert.Equal(claims[0].JobId, claims[1].JobId);

        var job = await GetJsonAsync<JobDto>(client, $"{first.BaseUrl}/api/jobs/{claims[0].JobId}", token);
        Assert.Equal(claims[0].JobId, job.JobId);
    }

    [Fact]
    public async Task ProcessRestart_ReclaimsInterruptedRefreshJobAcrossProcesses()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        _processes.Add(first);

        await first.WaitForHealthzAsync();

        var scriptPath = Path.Combine(shared.Scripts, $"reclaim-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(scriptPath, """
            WAITFOR DELAY '00:00:10';
            CREATE VISUAL ReclaimRace AS TABLE (
                SOURCE = (SELECT 7 AS Value),
                MAPPINGS (Value = Value)
            );
            """);

        using var client = new HttpClient();
        var token = await BootstrapAdminTokenAsync(client, first);
        var folder = await SendJsonAsync<FolderDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/folders",
            token,
            new { name = $"reclaim-{Guid.NewGuid():N}", parentId = (int?)null },
            HttpStatusCode.Created);
        var report = await SendJsonAsync<ReportDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/reports",
            token,
            new
            {
                folderId = folder.Id,
                name = $"Reclaim Race {Guid.NewGuid():N}",
                description = "",
                scriptPath
            },
            HttpStatusCode.Created);

        var refresh = await SendJsonAsync<RefreshDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/reports/{report.Id}/refresh",
            token,
            new { },
            HttpStatusCode.Accepted);

        await first.DisposeAsync();

        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(second);
        await second.WaitForHealthzAsync();

        var reclaimed = await WaitForJobStatusAsync(client, second, token, refresh.JobId, "Cancelled");
        Assert.Contains("interrupted", reclaimed.Error ?? "", StringComparison.OrdinalIgnoreCase);

        var reportState = await GetJsonAsync<ReportDto>(
            client,
            $"{second.BaseUrl}/api/reports/{report.Id}",
            token);
        Assert.Equal("Cancelled", reportState.LastRefreshStatus);
        Assert.Contains("interrupted", reportState.LastRefreshError ?? "", StringComparison.OrdinalIgnoreCase);

        await File.WriteAllTextAsync(scriptPath, """
            CREATE VISUAL Recovered AS TABLE (
                SOURCE = (SELECT 8 AS Value),
                MAPPINGS (Value = Value)
            );
            """);

        var recoveredRefresh = await SendJsonAsync<RefreshDto>(
            client,
            HttpMethod.Post,
            $"{second.BaseUrl}/api/reports/{report.Id}/refresh",
            token,
            new { },
            HttpStatusCode.Accepted);
        Assert.NotEqual(refresh.JobId, recoveredRefresh.JobId);
        Assert.False(recoveredRefresh.AlreadyRunning);
    }

    [Fact]
    public async Task CrossProcessCancellation_CancelsRunningRefresh()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a");
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b");
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        var scriptPath = Path.Combine(shared.Scripts, $"cancel-{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(scriptPath, """
            WAITFOR DELAY '00:00:10';
            CREATE VISUAL CancelRace AS TABLE (
                SOURCE = (SELECT 9 AS Value),
                MAPPINGS (Value = Value)
            );
            """);

        using var client = new HttpClient();
        var token = await BootstrapAdminTokenAsync(client, first);
        var folder = await SendJsonAsync<FolderDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/folders",
            token,
            new { name = $"cancel-{Guid.NewGuid():N}", parentId = (int?)null },
            HttpStatusCode.Created);
        var report = await SendJsonAsync<ReportDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/reports",
            token,
            new
            {
                folderId = folder.Id,
                name = $"Cancel Race {Guid.NewGuid():N}",
                description = "",
                scriptPath
            },
            HttpStatusCode.Created);

        var refresh = await SendJsonAsync<RefreshDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/reports/{report.Id}/refresh",
            token,
            new { },
            HttpStatusCode.Accepted);
        await WaitForJobStatusAsync(client, first, token, refresh.JobId, "Running");

        var cancelStatus = await SendForStatusAsync(
            client,
            HttpMethod.Delete,
            $"{second.BaseUrl}/api/jobs/{refresh.JobId}",
            token);
        Assert.Equal(HttpStatusCode.Accepted, cancelStatus);

        var cancelled = await WaitForJobStatusAsync(client, second, token, refresh.JobId, "Cancelled");
        Assert.Contains("cancelled", cancelled.Error ?? "", StringComparison.OrdinalIgnoreCase);

        var reportState = await GetJsonAsync<ReportDto>(
            client,
            $"{second.BaseUrl}/api/reports/{report.Id}",
            token);
        Assert.Equal("Cancelled", reportState.LastRefreshStatus);
        Assert.Contains("cancelled", reportState.LastRefreshError ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SharedTenants_RemainIsolatedAcrossProcessesAndWorkerQueue()
    {
        var shared = CreateSharedRoots();
        var first = StartPortal(shared, port: FreePort(), nodeName: "node-a", sharedTenancy: true);
        var second = StartPortal(shared, port: FreePort(), nodeName: "node-b", sharedTenancy: true);
        _processes.Add(first);
        _processes.Add(second);

        await first.WaitForHealthzAsync();
        await second.WaitForHealthzAsync();

        var scriptDirectory = Path.Combine(shared.Scripts, "tenant-alpha");
        Directory.CreateDirectory(scriptDirectory);
        const string ScriptName = "cross-node-shared.rptsql";
        await File.WriteAllTextAsync(Path.Combine(scriptDirectory, ScriptName), """
            CREATE VISUAL SharedWorker AS TABLE (
                SOURCE = (SELECT 42 AS Value),
                MAPPINGS (Value = Value)
            );
            """);

        var seeded = await SeedSharedTenantsAsync(ScriptName);
        using var client = new HttpClient();

        foreach (var process in new[] { first, second })
        {
            using var folders = new HttpRequestMessage(
                HttpMethod.Get, $"{process.BaseUrl}/api/folders?tenant=tenant-beta");
            folders.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seeded.AlphaToken);
            folders.Headers.Add("X-Tenant-Id", "tenant-beta");
            using var folderResponse = await client.SendAsync(folders);
            Assert.Equal(HttpStatusCode.OK, folderResponse.StatusCode);
            var body = await folderResponse.Content.ReadAsStringAsync();
            Assert.Contains("Alpha cross-node", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Beta cross-node secret", body, StringComparison.Ordinal);

            Assert.Equal(HttpStatusCode.NotFound, await SendForStatusAsync(
                client, HttpMethod.Get,
                $"{process.BaseUrl}/api/reports/{seeded.BetaReportId}?tenant=tenant-beta",
                seeded.AlphaToken));
            Assert.Equal(HttpStatusCode.NotFound, await SendForStatusAsync(
                client, HttpMethod.Get,
                $"{process.BaseUrl}/api/jobs/{seeded.BetaJobId}?tenant=tenant-beta",
                seeded.AlphaToken));
            Assert.Equal(HttpStatusCode.NotFound, await SendForStatusAsync(
                client, HttpMethod.Delete,
                $"{process.BaseUrl}/api/jobs/{seeded.BetaJobId}?tenant=tenant-beta",
                seeded.AlphaToken));
        }

        var refresh = await SendJsonAsync<RefreshDto>(
            client,
            HttpMethod.Post,
            $"{first.BaseUrl}/api/reports/{seeded.AlphaReportId}/refresh",
            seeded.AlphaToken,
            new { },
            HttpStatusCode.Accepted);
        var completed = await WaitForJobStatusAsync(
            client, second, seeded.AlphaToken, refresh.JobId, "Completed", TimeSpan.FromSeconds(60));
        Assert.Equal(refresh.JobId, completed.JobId);

        await using var verify = CreatePostgresContext();
        var foreignJob = await verify.PortalExecutionJobs.AsNoTracking()
            .SingleAsync(job => job.Id == seeded.BetaJobId);
        Assert.Equal("Running", foreignJob.Status);
        Assert.Equal("tenant-beta", foreignJob.TenantId);
    }

    private SharedRoots CreateSharedRoots()
    {
        var shared = new SharedRoots(
            Scripts: Path.Combine(_root, "scripts"),
            Snapshots: Path.Combine(_root, "snapshots"),
            Maps: Path.Combine(_root, "maps"),
            Datasets: Path.Combine(_root, "datasets"),
            Keys: Path.Combine(_root, "keys"));
        Directory.CreateDirectory(shared.Scripts);
        Directory.CreateDirectory(shared.Snapshots);
        Directory.CreateDirectory(shared.Maps);
        Directory.CreateDirectory(shared.Datasets);
        Directory.CreateDirectory(shared.Keys);
        return shared;
    }

    private PortalProcess StartPortal(
        SharedRoots shared,
        int port,
        string nodeName,
        bool sharedTenancy = false)
    {
        var portalDll = Path.Combine(AppContext.BaseDirectory, $"{typeof(PortalMarker).Assembly.GetName().Name}.dll");
        Assert.True(File.Exists(portalDll), $"Expected Portal assembly at {portalDll}");
        var psi = new ProcessStartInfo("dotnet", $"\"{portalDll}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(portalDll)!
        };
        var env = psi.Environment;
        env["ASPNETCORE_ENVIRONMENT"] = "Testing";
        env["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        env["Portal__Database__Provider"] = "Postgres";
        env["Portal__Database__ConnectionString"] = WithFastFailureTimeouts(_pg.GetConnectionString());
        env["Portal__DatabasePath"] = Path.Combine(_root, $"{nodeName}.db");
        env["Orchestrator__Database__Provider"] = "Postgres";
        env["Orchestrator__Database__ConnectionString"] = WithFastFailureTimeouts(_pg.GetConnectionString());
        env["Orchestrator__DatabasePath"] = Path.Combine(_root, $"{nodeName}.orchestrator.db");
        env["Portal__ScriptRootPath"] = shared.Scripts;
        env["Portal__SnapshotDirectory"] = shared.Snapshots;
        env["Portal__MapRootPath"] = shared.Maps;
        env["Portal__DatasetRootPath"] = shared.Datasets;
        env["Portal__Storage__KeyRingPath"] = shared.Keys;
        env["Portal__Jwt__Secret"] = JwtSecret;
        env["Portal__FirstRun__AdminPassword"] = "Admin@12345!";
        env["Portal__SharedTenancy__Enabled"] = sharedTenancy.ToString();
        if (sharedTenancy)
        {
            env["Portal__KeyManagement__Enabled"] = "true";
            var bindingIndex = 0;
            foreach (var tenant in new[] { "tenant-alpha", "tenant-beta" })
                foreach (var purpose in Enum.GetValues<ETL_SQL.Core.Security.KeyPurpose>())
                {
                    var prefix = $"Portal__KeyManagement__Bindings__{bindingIndex}";
                    var variable = $"ETLSQL_MULTIPROC_KEY_{bindingIndex}";
                    env[$"{prefix}__Scope"] = tenant;
                    env[$"{prefix}__Purpose"] = purpose.ToString();
                    env[$"{prefix}__Version"] = "v1";
                    env[$"{prefix}__KeyId"] = $"{tenant}-{purpose.ToString().ToLowerInvariant()}";
                    env[$"{prefix}__EnvironmentVariable"] = variable;
                    env[$"{prefix}__IsCurrent"] = "true";
                    env[variable] = Convert.ToBase64String(
                        Enumerable.Repeat((byte)(bindingIndex + 1), 32).ToArray());
                    bindingIndex++;
                }
        }
        env["Portal__Dataset__AtRestKey"] = HostedPortalFactory.DefaultAtRestKey;
        env["Cluster__NodeHeartbeatSeconds"] = "2";
        env["Cluster__NodeHeartbeatMinimumSeconds"] = "1";
        env["Cluster__NodeHeartbeatMinimumIntervalSeconds"] = "1";

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Portal process.");
        return new PortalProcess(process, $"http://127.0.0.1:{port}");
    }

    private async Task<SharedTenantSeed> SeedSharedTenantsAsync(string alphaScriptPath)
    {
        await using var db = CreatePostgresContext();
        var admin = await db.Users.SingleAsync(user => user.NormalizedUserName == "ADMIN");
        admin.TenantId = "tenant-alpha";
        admin.MustChangePassword = false;

        var beta = new PortalUser
        {
            UserName = $"beta-{Guid.NewGuid():N}",
            NormalizedUserName = $"BETA-{Guid.NewGuid():N}",
            Email = $"beta-{Guid.NewGuid():N}@test.local",
            NormalizedEmail = $"BETA-{Guid.NewGuid():N}@TEST.LOCAL",
            TenantId = "tenant-beta",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            MustChangePassword = false,
            IsActive = true
        };
        db.Users.Add(beta);
        await db.SaveChangesAsync();

        var alphaFolder = new Folder
        {
            TenantId = "tenant-alpha",
            Name = "Alpha cross-node",
            Path = "/cross-node",
            OwnerId = admin.Id
        };
        var betaFolder = new Folder
        {
            TenantId = "tenant-beta",
            Name = "Beta cross-node secret",
            Path = "/cross-node",
            OwnerId = beta.Id
        };
        db.Folders.AddRange(alphaFolder, betaFolder);
        await db.SaveChangesAsync();

        var alphaReport = new Report
        {
            TenantId = "tenant-alpha",
            FolderId = alphaFolder.Id,
            Name = "Alpha shared worker",
            ScriptPath = alphaScriptPath,
            CreatedBy = admin.Id
        };
        var betaReport = new Report
        {
            TenantId = "tenant-beta",
            FolderId = betaFolder.Id,
            Name = "Beta shared worker secret",
            ScriptPath = "beta-secret.rptsql",
            CreatedBy = beta.Id
        };
        db.Reports.AddRange(alphaReport, betaReport);
        await db.SaveChangesAsync();

        var betaJobId = $"shared-beta-{Guid.NewGuid():N}";
        db.PortalExecutionJobs.Add(new PortalExecutionJob
        {
            Id = betaJobId,
            TenantId = "tenant-beta",
            ReportId = betaReport.Id,
            UserId = beta.Id,
            Status = "Running"
        });
        await db.SaveChangesAsync();

        var roles = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == admin.Id
            select role.Name!).ToListAsync();
        var token = new TokenService(new PortalConfig
        {
            Jwt = new JwtConfig { Secret = JwtSecret },
            SharedTenancy = new SharedTenancyConfig { Enabled = true }
        }).GenerateJwt(
            admin,
            roles,
            tenantContext: TenantContext.FromVerifiedCredential("tenant-alpha"));

        return new SharedTenantSeed(
            token, alphaReport.Id, betaReport.Id, betaJobId);
    }

    private PortalDbContext CreatePostgresContext()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(
                _pg.GetConnectionString(),
                npg => npg.MigrationsAssembly(PortalDatabase.PostgresMigrationsAssembly))
            .Options;
        return new PortalDbContext(options);
    }

    private static string WithFastFailureTimeouts(string connectionString) =>
        connectionString.Contains("Timeout=", StringComparison.OrdinalIgnoreCase)
            ? connectionString
            : connectionString.TrimEnd(';') + ";Timeout=2;Command Timeout=2";

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> BootstrapAdminTokenAsync(HttpClient client, PortalProcess process)
    {
        var initial = await SendJsonAsync<LoginResponse>(
            client,
            HttpMethod.Post,
            $"{process.BaseUrl}/api/auth/login",
            token: null,
            new { username = "admin", password = "Admin@12345!" },
            HttpStatusCode.OK);

        await SendJsonAsync<object>(
            client,
            HttpMethod.Post,
            $"{process.BaseUrl}/api/auth/change-password",
            initial.Token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" },
            HttpStatusCode.NoContent);

        var relogin = await SendJsonAsync<LoginResponse>(
            client,
            HttpMethod.Post,
            $"{process.BaseUrl}/api/auth/login",
            token: null,
            new { username = "admin", password = "Admin@Tests99!" },
            HttpStatusCode.OK);
        return relogin.Token;
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<T> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string url,
        string? token,
        object body,
        HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> SendJsonForStatusAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? token,
        object body,
        long? ifMatchVersion = null)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatchVersion is not null)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion.Value}\"");

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> SendForStatusAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? token)
    {
        using var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<(HttpStatusCode StatusCode, HealthzResponse Body)> WaitForHealthzStatusAsync(
        HttpClient client,
        PortalProcess process,
        HttpStatusCode expectedStatus)
    {
        var observed = await LoadAwareWait.UntilAsync(
            $"Portal process healthz to reach {expectedStatus}",
            async ct =>
            {
                try
                {
                    using var response = await client.GetAsync($"{process.BaseUrl}/healthz", ct);
                    var body = (await response.Content.ReadFromJsonAsync<HealthzResponse>(Json, ct))!;
                    return (StatusCode: (HttpStatusCode?)response.StatusCode, Body: body, Error: (string?)null);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return (StatusCode: (HttpStatusCode?)null, Body: (HealthzResponse?)null,
                        Error: $"{ex.GetType().Name}: {ex.Message}");
                }
            },
            state => state.StatusCode == expectedStatus && state.Body is not null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(250),
            state => $"status={state.StatusCode?.ToString() ?? "<none>"}; error={state.Error ?? "<none>"}");
        return (observed.StatusCode!.Value, observed.Body!);
    }

    private static async Task<JobDto> WaitForJobStatusAsync(
        HttpClient client,
        PortalProcess process,
        string token,
        string jobId,
        string expectedStatus,
        TimeSpan? timeout = null)
    {
        var observed = await LoadAwareWait.UntilAsync(
            $"multi-process job '{jobId}' to reach {expectedStatus}",
            _ => GetJsonAsync<JobDto>(client, $"{process.BaseUrl}/api/jobs/{jobId}", token),
            job => string.Equals(job.Status, expectedStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(job.Status, "Cancelled", StringComparison.OrdinalIgnoreCase),
            timeout ?? TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(250),
            job => $"status={job.Status}; error={job.Error ?? "<none>"}");
        Assert.True(
            string.Equals(observed.Status, expectedStatus, StringComparison.OrdinalIgnoreCase),
            $"Expected job '{jobId}' to reach {expectedStatus}, but reached {observed.Status}: {observed.Error}");
        return observed;
    }

    private sealed record SharedRoots(
        string Scripts,
        string Snapshots,
        string Maps,
        string Datasets,
        string Keys);

    private sealed record HealthzResponse(string Status, Dictionary<string, string> Checks);
    private sealed record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);
    private sealed record FolderDto(int Id, int? ParentId, string Name, string Path, List<FolderDto> Children, long Version);
    private sealed record ReportDto(
        int Id,
        int FolderId,
        string Name,
        string? LastRefreshStatus = null,
        string? LastRefreshError = null);
    private sealed record RefreshDto(string JobId, bool AlreadyRunning);
    private sealed record JobDto(string JobId, string Status, string? Error);
    private sealed record SharedTenantSeed(
        string AlphaToken,
        int AlphaReportId,
        int BetaReportId,
        string BetaJobId);
    private sealed record GroupDto(
        int Id,
        string Name,
        string? Description,
        int MemberCount,
        string Provider,
        string? AdGroup,
        long Version);
    private sealed record FolderAclDto(int GroupId, string GroupName, FolderPermission Permission);

    private sealed class PortalProcess(Process process, string baseUrl) : IAsyncDisposable
    {
        private bool _disposed;
        private readonly Task<string> _standardOutput = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _standardError = process.StandardError.ReadToEndAsync();

        public string BaseUrl { get; } = baseUrl;

        public async Task WaitForHealthzAsync()
        {
            using var client = new HttpClient();
            await LoadAwareWait.UntilAsync(
                $"Portal process at {BaseUrl} to become healthy",
                async ct =>
                {
                    if (process.HasExited)
                        throw new InvalidOperationException(
                            $"Portal process exited with code {process.ExitCode}: {await _standardError.WaitAsync(ct)}");
                    try
                    {
                        using var response = await client.GetAsync($"{BaseUrl}/healthz", ct);
                        var body = await response.Content.ReadAsStringAsync(ct);
                        return $"HTTP {(int)response.StatusCode} {response.StatusCode}: {body}";
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        return $"{ex.GetType().Name}: {ex.Message}";
                    }
                },
                state => state.StartsWith("HTTP 2", StringComparison.Ordinal),
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(500));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await Task.WhenAll(_standardOutput, _standardError);
            process.Dispose();
        }
    }
}
