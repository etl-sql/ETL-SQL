# Portal RLS Report Viewing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two ordinary readers open one identity-filtered report in the Portal and each sees, filters, saves a view of, exports and subscribes to their own rows only.

**Architecture:** An identity-sensitive report is served live from the caller's own `DashboardService` session, which now runs under the caller's `ExecutionIdentity`. The existing manifest route, export and viewer switch to that session for these reports. No rows are stored and no snapshot is created.

**Tech Stack:** C# / ASP.NET Core (Portal), TypeScript browser sources compiled by `scripts/sync-assets.js`, xUnit, Playwright.

**Spec:** `docs/architecture/decisions/portal-rls-report-viewing.md`

## Global Constraints

- Read `AGENTS.md` first. No new NuGet or npm dependency. No database migration.
- Node 22 is required for asset scripts: prepend `artifacts\section1\toolchain\node-v22.23.3-win-x64` to `PATH`.
- Never edit `wwwroot/js/*.js`. Edit `src/ETL-SQL.Portal/BrowserSources/*.ts`, then run `node scripts\sync-assets.js` and `node scripts\sync-assets.js -Check`.
- Run one test class at a time with `--filter`. Never run the solution-wide suite.
- `INT` values are `decimal` at runtime: assert with `m` or `Convert.ToDecimal`.
- An identity-sensitive report never gets a `ReportSnapshot` row and never leaves a manifest file.
- The caller's identity is always built from the database, never from the request.
- Read on the folder is enough to view. `POST …/execute` and ordinary Refresh still need Execute.
- User-facing docs name the release (v0.21.0), never ST numbers.
- Commit per task. Stage by explicit file list. End messages with the session's attribution lines.

## Review Focus

1. **Group removed mid-session.** A reader removed from a group must lose those rows on the next request without signing out. Pinned in Task 2 and Task 3.
2. **Unreadable script.** If the report script cannot be read, treat it as identity-sensitive: no snapshot path, live path only. Pinned in Task 1.
3. **Ordinary reports unchanged.** A report with no identity reference keeps its snapshot, first-run gate and snapshot export. Pinned in Task 3 and Task 5.
4. **Identity cannot be resolved.** A deleted or foreign-tenant user gets no identity, so the engine returns no rows. Pinned in Task 1.
5. **Two readers, one process.** Sessions are keyed per user; reader B must never be handed reader A's session. Pinned in Task 2 and Task 8.

---

### Task 1: One identity builder and one sensitivity check

**Files:**
- Create: `src/ETL-SQL.Portal/Services/PortalExecutionIdentityService.cs`
- Create: `src/ETL-SQL.Portal/Services/ReportIdentitySensitivity.cs`
- Modify: `src/ETL-SQL.Portal/Services/ExecutionJobService.cs` (`BuildExecutionIdentityAsync`, `ScriptReferencesIdentityAsync`)
- Modify: `src/ETL-SQL.Portal/Program.cs` (near line 743)
- Test: `tests/ETL-SQL.Portal.Tests/PortalExecutionIdentityServiceTests.cs`

**Interfaces:**
- Produces:
  - `Task<ExecutionIdentity?> PortalExecutionIdentityService.BuildAsync(int userId, string tenantId, bool isAdministrator, int? impersonatedUserId, CancellationToken ct)`
  - `static Task<bool> ReportIdentitySensitivity.IsSensitiveAsync(string scriptPath, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

```csharp
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class PortalExecutionIdentityServiceTests : IClassFixture<PortalWebFactory>
{
    private readonly PortalWebFactory _factory;
    public PortalExecutionIdentityServiceTests(PortalWebFactory factory) => _factory = factory;

    [Fact]
    public async Task BuildsTheUsersNameGroupsAndRolesFromTheDatabase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = new PortalUser { UserName = $"ident_{suffix}", NormalizedUserName = $"IDENT_{suffix}".ToUpperInvariant(), Email = $"ident_{suffix}@test.local" };
            var group = new Group { Name = $"Region:East {suffix}" };
            db.Users.Add(user); db.Groups.Add(group);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = group.Id });
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var service = _factory.Services.GetRequiredService<PortalExecutionIdentityService>();
        var identity = await service.BuildAsync(userId, "portal-host", isAdministrator: false, impersonatedUserId: null, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal($"ident_{suffix}", identity!.EffectiveUser);
        Assert.Equal(identity.EffectiveUser, identity.RealUser);
        Assert.False(identity.IsAdmin);
        Assert.Contains($"Region:East {suffix}", identity.Groups);
    }

    [Fact]
    public async Task AnUnknownUserHasNoIdentity()
    {
        var service = _factory.Services.GetRequiredService<PortalExecutionIdentityService>();
        Assert.Null(await service.BuildAsync(int.MaxValue, "portal-host", false, null, CancellationToken.None));
    }

    [Fact]
    public async Task AScriptThatCannotBeReadCountsAsIdentitySensitive()
    {
        var missing = Path.Combine(_factory.TempDir, "scripts", $"missing_{Guid.NewGuid():N}.rptsql");
        Assert.True(await ReportIdentitySensitivity.IsSensitiveAsync(missing, CancellationToken.None));
    }

    [Theory]
    [InlineData("SELECT 1 AS A WHERE HAS_GROUP('x');", true)]
    [InlineData("SELECT 1 AS A;", false)]
    public async Task SensitivityFollowsTheScript(string script, bool expected)
    {
        var path = Path.Combine(_factory.TempDir, "scripts", $"sens_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, script);
        Assert.Equal(expected, await ReportIdentitySensitivity.IsSensitiveAsync(path, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run and confirm they fail to compile**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~PortalExecutionIdentityServiceTests`
Expected: build error, `PortalExecutionIdentityService` not found.

- [ ] **Step 3: Create the two services**

`ReportIdentitySensitivity.cs`:

```csharp
namespace ETL_SQL.Portal.Services;

/// <summary>
/// Whether a report's rows depend on who is looking. Such a report is never served from a shared
/// snapshot. A script that cannot be read counts as sensitive, so a read failure cannot turn a
/// per-viewer report into a shared one.
/// </summary>
public static class ReportIdentitySensitivity
{
    public static async Task<bool> IsSensitiveAsync(string scriptPath, CancellationToken ct)
    {
        try
        {
            return ETL_SQL.Core.Governance.RowLevelSecurityScan.ReferencesIdentity(
                await File.ReadAllTextAsync(scriptPath, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return true;
        }
    }
}
```

`PortalExecutionIdentityService.cs`: move the body of `ExecutionJobService.BuildExecutionIdentityAsync` (lines 1310-1368) here unchanged, replacing `job.UserId`, `job.TenantId`, `job.IsAdministrator`, `job.ImpersonatedUserId` with the parameters:

```csharp
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Builds the row-level-security identity of a Portal user from the database: name, roles, groups
/// and whether administrators bypass row filters. Returns null when the user cannot be resolved,
/// so an identity-sensitive report fails closed.
/// </summary>
public sealed class PortalExecutionIdentityService(
    IServiceScopeFactory scopeFactory, PortalConfig config, ILogger<PortalExecutionIdentityService> log)
{
    public async Task<ExecutionIdentity?> BuildAsync(
        int userId, string tenantId, bool isAdministrator, int? impersonatedUserId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var effectiveUserId = impersonatedUserId ?? userId;

            var effective = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == effectiveUserId && u.TenantId == tenantId, ct);
            if (effective is null) return null;

            var roles = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               where ur.UserId == effectiveUserId && r.Name != null
                               select r.Name!).ToListAsync(ct);
            var groups = await (from ug in db.UserGroups
                                join g in db.Groups on ug.GroupId equals g.Id
                                where ug.UserId == effectiveUserId
                                select g.Name).ToListAsync(ct);

            var effectiveName = effective.UserName ?? effectiveUserId.ToString();
            var realName = effectiveName;
            if (impersonatedUserId is not null)
            {
                var actor = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
                realName = actor?.UserName ?? userId.ToString();
            }

            return new ExecutionIdentity
            {
                EffectiveUser = effectiveName,
                EffectiveUserId = effectiveUserId,
                RealUser = realName,
                // The effective identity, not the real actor, decides the administrator bypass.
                IsAdmin = impersonatedUserId is null
                    ? isAdministrator
                    : roles.Contains("Admin", StringComparer.OrdinalIgnoreCase),
                AdminBypassesRowLevelSecurity = config.Security.AdminBypassRowLevelSecurity,
                Groups = groups,
                Roles = roles
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Failed to build execution identity for user {UserId}; identity-sensitive reports will fail closed.", userId);
            return null;
        }
    }
}
```

Register it in `Program.cs` directly above the `SessionCache` registration:

```csharp
builder.Services.AddSingleton<ETL_SQL.Portal.Services.PortalExecutionIdentityService>();
```

- [ ] **Step 4: Make the job service use both**

In `ExecutionJobService`: add a `PortalExecutionIdentityService` constructor dependency (follow how `_sessions` is injected and stored). Replace the body of `BuildExecutionIdentityAsync` with:

```csharp
    => _identities.BuildAsync(job.UserId, job.TenantId, job.IsAdministrator, job.ImpersonatedUserId, ct);
```

Replace the body of `ScriptReferencesIdentityAsync` with `=> ReportIdentitySensitivity.IsSensitiveAsync(scriptPath, ct);`. If any test constructs `ExecutionJobService` by hand, pass `new PortalExecutionIdentityService(scopeFactory, config, NullLogger<PortalExecutionIdentityService>.Instance)`.

- [ ] **Step 5: Run the new tests and the existing RLS tests**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~PortalExecutionIdentityServiceTests|FullyQualifiedName~RowLevelSecurityPortalTests|FullyQualifiedName~SubscriptionDeliverySecurityTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ETL-SQL.Portal/Services/PortalExecutionIdentityService.cs src/ETL-SQL.Portal/Services/ReportIdentitySensitivity.cs src/ETL-SQL.Portal/Services/ExecutionJobService.cs src/ETL-SQL.Portal/Program.cs tests/ETL-SQL.Portal.Tests/PortalExecutionIdentityServiceTests.cs
git commit -m "Build the Portal execution identity in one place"
```

---

### Task 2: Sessions carry the caller's identity

**Files:**
- Modify: `src/ETL-SQL.Portal/Services/SessionCache.cs`
- Test: `tests/ETL-SQL.Portal.Tests/SessionCacheTests.cs`

**Interfaces:**
- Consumes: `ExecutionIdentity` (`ETL_SQL.Core.Governance`).
- Produces:
  - `DashboardService SessionCache.GetOrCreate(int reportId, int userId, string scriptPath, bool isAdministrator = false, string? keyScope = null, ExecutionIdentity? executionIdentity = null)`
  - `Task SessionCache.InvalidateUserAsync(int reportId, int userId, string keyScope = "portal-host")`

- [ ] **Step 1: Write the failing tests** (append inside `SessionCacheTests`)

```csharp
    private static ETL_SQL.Core.Governance.ExecutionIdentity Identity(string user, params string[] groups) => new()
    {
        EffectiveUser = user, EffectiveUserId = 7, RealUser = user, IsAdmin = false, Groups = groups, Roles = ["Viewer"]
    };

    [Fact]
    public void GetOrCreate_SameIdentity_ReusesTheSession_WhateverTheGroupOrder()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var first = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann", "North", "East"));
        var second = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann", "east", "NORTH"));

        Assert.Same(first, second);
    }

    /// <summary>A reader removed from a group must not keep a session that ran with it.</summary>
    [Fact]
    public void GetOrCreate_GroupRemoved_ReplacesTheSession()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");

        var before = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann", "North"));
        var after = cache.GetOrCreate(1, 7, script, executionIdentity: Identity("ann"));

        Assert.NotSame(before, after);
    }

    [Fact]
    public async Task InvalidateUser_RemovesOnlyThatUsersSession()
    {
        using var cache = NewCache();
        var script = Path.Combine(_tempDir, "a.rptsql");
        var ann = cache.GetOrCreate(1, 7, script);
        var bob = cache.GetOrCreate(1, 8, script);

        await cache.InvalidateUserAsync(1, 7);

        Assert.NotSame(ann, cache.GetOrCreate(1, 7, script));
        Assert.Same(bob, cache.GetOrCreate(1, 8, script));
    }
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~SessionCacheTests`
Expected: build error on `executionIdentity` and `InvalidateUserAsync`.

- [ ] **Step 3: Implement**

In `SessionCache.cs`:

```csharp
    private readonly record struct Entry(DashboardService Service, string ScriptPath, string CallerContext, string IdentityKey)
```

Add the parameter and compare the key:

```csharp
    public DashboardService GetOrCreate(
        int reportId, int userId, string scriptPath, bool isAdministrator = false, string? keyScope = null,
        ETL_SQL.Core.Governance.ExecutionIdentity? executionIdentity = null)
    {
        ...
        var identityKey = IdentityKey(executionIdentity);
        ...
                if (existing.ScriptPath == scriptPath && existing.CallerContext == callerContext
                    && existing.IdentityKey == identityKey)
```

Pass `executionIdentity` and `identityKey` through both `CreateEntry` calls, and in `CreateEntry` pass `executionIdentity: executionIdentity` to the `DashboardService` constructor and `identityKey` to `new Entry(...)`. Add:

```csharp
    /// <summary>
    /// What a session's rows were computed for. A session answers only for the identity it ran
    /// under, so a changed group, role or administrator flag is a different session.
    /// </summary>
    private static string IdentityKey(ETL_SQL.Core.Governance.ExecutionIdentity? identity) => identity is null
        ? string.Empty
        : string.Join('\u001f',
            identity.EffectiveUser, identity.RealUser, identity.IsAdmin, identity.AdminBypassesRowLevelSecurity,
            string.Join('\u001e', identity.Groups.Select(g => g.ToUpperInvariant()).Order(StringComparer.Ordinal)),
            string.Join('\u001e', identity.Roles.Select(r => r.ToUpperInvariant()).Order(StringComparer.Ordinal)));

    /// <summary>Removes one user's session for a report, so their next request runs it again.</summary>
    public async Task InvalidateUserAsync(int reportId, int userId, string keyScope = "portal-host")
    {
        if (_sessions.TryRemove(new SessionKey(keyScope, reportId, userId), out var entry))
            await entry.Service.DisposeAsync();
    }
```

Update the class summary to say sessions are also bound to the caller's identity.

- [ ] **Step 4: Run**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~SessionCacheTests|FullyQualifiedName~CrossHostConsistencyTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.Portal/Services/SessionCache.cs tests/ETL-SQL.Portal.Tests/SessionCacheTests.cs
git commit -m "Bind a report session to the identity it ran under"
```

---

### Task 3: The manifest route answers live for identity-sensitive reports

**Files:**
- Modify: `src/ETL-SQL.Portal/Controllers/ExecutionController.cs` (`GetManifest`, `GetOrRebuildSessionAsync`, new `ResetLiveSession`)
- Test: `tests/ETL-SQL.Portal.Tests/RowLevelSecurityLiveViewTests.cs` (new; copy the private helpers `CreatePortalUserAsync`, `LoginWithRequiredChangeAsync`, `CreateFolderAsync`, `CreateReportAsync`, `AuthGet`, `AuthPost`, `GetAdminTokenAsync` from `RowLevelSecurityPortalTests.cs`)

**Interfaces:**
- Consumes: `PortalExecutionIdentityService.BuildAsync`, `ReportIdentitySensitivity.IsSensitiveAsync`, `SessionCache.GetOrCreate(..., executionIdentity:)`, `SessionCache.InvalidateUserAsync`.
- Produces: `GET /api/reports/{id}/manifest` live for sensitive reports; `POST /api/reports/{id}/live/reset` (204).

- [ ] **Step 1: Write the failing tests**

The report used by every test in this class:

```csharp
    private static string Script(string north, string south) => $"""
        CREATE TABLE #sales (SaleID INT, Region VARCHAR(20), Total DECIMAL(10,2));
        INSERT INTO #sales VALUES (1, 'North', 100), (2, 'North', 150), (3, 'South', 80),
          (4, 'South', 60), (5, 'West', 200), (6, 'West', 40);
        CREATE VISUAL Detail AS TABLE (
          SOURCE = (SELECT SaleID, Region, Total FROM #sales
                    WHERE (Region = 'North' AND HAS_GROUP('{north}'))
                       OR (Region = 'South' AND HAS_GROUP('{south}')))
        );
        CREATE PAGE [Main] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = Detail)));
        """;

    /// <summary>The SaleID values the Detail table holds in a manifest response.</summary>
    private static int[] SaleIds(JsonObject manifest)
    {
        var detail = manifest["visuals"]!.AsArray().Single(v => v!["name"]!.GetValue<string>() == "Detail")!;
        var column = detail["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ToList()
            .FindIndex(c => c.Equals("SaleID", StringComparison.OrdinalIgnoreCase));
        return [.. detail["rows"]!.AsArray().Select(r => (int)Convert.ToDecimal(r![column]!.ToString())).Order()];
    }
```

Add a helper `SeedAsync(permission)` that creates a folder, two groups `region_north_<s>` and `region_south_<s>`, two `Viewer` users each in one group, a `FolderAcl` for each group with the given `FolderPermission`, and the report; it returns a record with `ReportId`, `FolderId`, `NorthToken`, `SouthToken`, `NorthUserId` and `NorthGroupId`. Then:

```csharp
    [Fact]
    public async Task EachReaderGetsOnlyTheirRows_WithReadAlone_AndNothingIsStored()
    {
        var seed = await SeedAsync(FolderPermission.Read);

        var north = await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/manifest");
        var south = await AuthGet(seed.SouthToken, $"/api/reports/{seed.ReportId}/manifest");
        Assert.Equal(HttpStatusCode.OK, north.StatusCode);
        Assert.Equal(new[] { 1, 2 }, SaleIds((await north.Content.ReadFromJsonAsync<JsonObject>(_json))!));
        Assert.Equal(new[] { 3, 4 }, SaleIds((await south.Content.ReadFromJsonAsync<JsonObject>(_json))!));

        // Asked again after the other reader, the first still gets only their own.
        var again = await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/manifest");
        Assert.Equal(new[] { 1, 2 }, SaleIds((await again.Content.ReadFromJsonAsync<JsonObject>(_json))!));

        Assert.Equal(HttpStatusCode.NotFound, (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/snapshot")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(0, await db.ReportSnapshots.CountAsync(s => s.ReportId == seed.ReportId));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "VIEW_REPORT_LIVE" && a.ResourceId == seed.ReportId.ToString()));
    }

    [Fact]
    public async Task AReaderRemovedFromTheirGroupGetsNoRowsOnTheNextRequest()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var before = await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/manifest");
        Assert.Equal(new[] { 1, 2 }, SaleIds((await before.Content.ReadFromJsonAsync<JsonObject>(_json))!));

        // A second group keeps their folder grant; only the region membership goes.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var keep = new Group { Name = $"keep_{Guid.NewGuid():N}"[..20] };
            db.Groups.Add(keep);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = seed.NorthUserId, GroupId = keep.Id });
            db.FolderAcls.Add(new FolderAcl { FolderId = seed.FolderId, GroupId = keep.Id, Permission = FolderPermission.Read });
            db.UserGroups.Remove(await db.UserGroups.SingleAsync(ug => ug.UserId == seed.NorthUserId && ug.GroupId == seed.NorthGroupId));
            await db.SaveChangesAsync();
        }

        var after = await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/manifest");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Empty(SaleIds((await after.Content.ReadFromJsonAsync<JsonObject>(_json))!));
    }

    [Fact]
    public async Task AReaderWithNoGrantIsRefused()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var outsiderName = $"outsider_{Guid.NewGuid():N}"[..20];
        await CreatePortalUserAsync(await GetAdminTokenAsync(), outsiderName, "Out@Test1!", "Viewer");
        var outsider = await LoginWithRequiredChangeAsync(outsiderName, "Out@Test1!", "Out@Test2!");

        Assert.Equal(HttpStatusCode.Forbidden, (await AuthGet(outsider, $"/api/reports/{seed.ReportId}/manifest")).StatusCode);
    }

    [Theory]
    [InlineData(true, new[] { 1, 2, 3, 4 })]
    [InlineData(false, new int[0])]
    public async Task AnAdministratorFollowsTheBypassSetting(bool bypass, int[] expected)
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var config = _factory.Services.GetRequiredService<PortalConfig>();
        var original = config.Security.AdminBypassRowLevelSecurity;
        config.Security.AdminBypassRowLevelSecurity = bypass;
        try
        {
            var response = await AuthGet(await GetAdminTokenAsync(), $"/api/reports/{seed.ReportId}/manifest");
            Assert.Equal(expected, SaleIds((await response.Content.ReadFromJsonAsync<JsonObject>(_json))!));
        }
        finally { config.Security.AdminBypassRowLevelSecurity = original; }
    }

    [Fact]
    public async Task ResetDiscardsOnlyTheCallersSession()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/manifest");
        var reset = await AuthPost(seed.NorthToken, $"/api/reports/{seed.ReportId}/live/reset", new { });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var after = await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/manifest");
        Assert.Equal(new[] { 1, 2 }, SaleIds((await after.Content.ReadFromJsonAsync<JsonObject>(_json))!));
    }

    [Fact]
    public async Task AnOrdinaryReportStillHasNoManifestUntilItHasASnapshot()
    {
        var admin = await GetAdminTokenAsync();
        var folderId = await CreateFolderAsync(admin, $"Plain {Guid.NewGuid():N}"[..20]);
        var reportId = await CreateReportAsync(admin, folderId, $"plain_{Guid.NewGuid():N}"[..20],
            "CREATE VISUAL P AS CARD (SOURCE = (SELECT 42 AS Answer), MAPPINGS (VALUE = Answer));");
        Assert.Equal(HttpStatusCode.NotFound, (await AuthGet(admin, $"/api/reports/{reportId}/manifest")).StatusCode);
    }
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~RowLevelSecurityLiveViewTests`
Expected: the first five fail with 404 from `/manifest`; the last passes.

If `SaleIds` throws on the manifest shape, print one manifest body from the parameters route and correct the property names in the helper before going on. The browser tests read `_visualData.columns` and `_visualData.rows[row][column]`, which is the shape assumed here.

- [ ] **Step 3: Implement**

Add `PortalExecutionIdentityService identities` to the `ExecutionController` primary constructor. Then:

```csharp
    [HttpGet("reports/{id:int}/snapshot/manifest")]
    public async Task<IActionResult> GetManifest(int id)
    {
        var live = await TryGetLiveManifestAsync(id);
        if (live is not null) return live;

        var resolved = await ResolveReadableSnapshotKeyAsync(id);
        ...unchanged...
    }

    /// <summary>
    /// An identity-sensitive report has no snapshot to serve: its rows depend on who asks. It is
    /// answered from the caller's own session, run under the caller's identity. Returns null for
    /// any other report, which is served from its snapshot.
    /// </summary>
    private async Task<IActionResult?> TryGetLiveManifestAsync(int id)
    {
        var report = await CatalogScope.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return null;
        if (!PortalPathGuard.TryResolveScript(portalConfig, _datasetScope.TenantId, report.ScriptPath, out var scriptPath))
            return null;
        if (!await ReportIdentitySensitivity.IsSensitiveAsync(scriptPath, HttpContext.RequestAborted)) return null;

        if (await GetEffectiveReportPermissionAsync(report) is null) return Forbid();

        var svc = await GetOrRebuildSessionAsync(id, scriptPath);
        var manifest = await svc.GetManifestAsync(HttpContext.RequestAborted);
        await audit.LogAsync(CurrentUserId, "VIEW_REPORT_LIVE", "Report", id.ToString());
        return this.BrowserManifest(manifest);
    }

    // ── 2.2b  POST /api/reports/{id}/live/reset ──────────────────────────────
    // Discards the caller's own session, so their next view runs the report again.
    [HttpPost("reports/{id:int}/live/reset")]
    public async Task<IActionResult> ResetLiveSession(int id)
    {
        var report = await CatalogScope.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        if (await GetEffectiveReportPermissionAsync(report) is null) return Forbid();
        await sessions.InvalidateUserAsync(id, CurrentUserId, _datasetScope.TenantId);
        return NoContent();
    }
```

In `GetOrRebuildSessionAsync`, build the identity and pass it:

```csharp
        var identity = await identities.BuildAsync(
            CurrentUserId, _datasetScope.TenantId, IsAdmin, impersonatedUserId: null, HttpContext.RequestAborted);
        var svc = sessions.GetOrCreate(
            reportId, CurrentUserId, scriptPath,
            isAdministrator: IsAdmin, keyScope: _datasetScope.TenantId, executionIdentity: identity);
```

`GET reports/{id}/snapshot/manifest` and `/manifest` share `GetManifest`; the spec asks that the `snapshot/*` routes keep answering 404 for these reports. Split them: leave `GetManifest` (the `snapshot/manifest` route) as it was, and put the live check only in the alias:

```csharp
    [HttpGet("reports/{id:int}/manifest")]
    public async Task<IActionResult> GetManifestAlias(int id) => await TryGetLiveManifestAsync(id) ?? await GetManifest(id);
```

- [ ] **Step 4: Run**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~RowLevelSecurityLiveViewTests|FullyQualifiedName~RowLevelSecurityPortalTests|FullyQualifiedName~SavedReportViewStateTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.Portal/Controllers/ExecutionController.cs tests/ETL-SQL.Portal.Tests/RowLevelSecurityLiveViewTests.cs
git commit -m "Serve an identity-filtered report live to each reader"
```

---

### Task 4: Tell the viewer which reports are per viewer

**Files:**
- Modify: `src/ETL-SQL.Portal/Models/ReportModels.cs` (`ReportDto`)
- Modify: `src/ETL-SQL.Portal/Controllers/ReportsController.cs` (`ToDto`, the single-report `GET api/reports/{id}` action)
- Test: `tests/ETL-SQL.Portal.Tests/RowLevelSecurityLiveViewTests.cs`

**Interfaces:**
- Produces: `ReportDto.PerViewer` (`bool`, JSON `perViewer`), true only on the single-report read of an identity-sensitive report. List endpoints leave it false: they would have to read every script.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task TheReportSaysItIsPerViewer()
    {
        var seed = await SeedAsync(FolderPermission.Read);
        var report = await (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}")).Content.ReadFromJsonAsync<JsonObject>(_json);
        Assert.True(report!["perViewer"]!.GetValue<bool>());
        Assert.False(report["hasSnapshot"]!.GetValue<bool>());
    }
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~TheReportSaysItIsPerViewer`
Expected: FAIL, `perViewer` is null.

- [ ] **Step 3: Implement**

`ReportDto`: add as the last parameter, after `Version`:

```csharp
    long Version = 1,
    /// <summary>The report's rows depend on who is looking; it is viewed live and has no snapshot.</summary>
    bool PerViewer = false);
```

`ToDto`: add `bool perViewer = false` after `isFavorite` and pass `r.Version, perViewer` to the constructor. In the single-report GET action, before building the DTO:

```csharp
        var perViewer = PortalPathGuard.TryResolveScript(portalConfig, _datasetScope.TenantId, report.ScriptPath, out var scriptPath)
            && await ReportIdentitySensitivity.IsSensitiveAsync(scriptPath, HttpContext.RequestAborted);
```

and pass `perViewer: perViewer`. Use the controller's existing names for the config and tenant scope fields.

If `scripts/generate-browser-contracts.mjs` emits a TypeScript `ReportDto`, run it under Node 22 and commit its output.

- [ ] **Step 4: Run**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~RowLevelSecurityLiveViewTests|FullyQualifiedName~ReportsControllerTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.Portal/Models/ReportModels.cs src/ETL-SQL.Portal/Controllers/ReportsController.cs tests/ETL-SQL.Portal.Tests/RowLevelSecurityLiveViewTests.cs
git commit -m "Mark a report whose rows depend on the viewer"
```

Add any regenerated contract file to the `git add`.

---

### Task 5: Export from the caller's session

**Files:**
- Modify: `src/ETL-SQL.Portal/Controllers/ExportController.cs` (`LoadManifestAsync`)
- Test: `tests/ETL-SQL.Portal.Tests/RowLevelSecurityLiveViewTests.cs`

**Interfaces:**
- Consumes: `SessionCache.GetOrCreate(..., executionIdentity:)`, `PortalExecutionIdentityService.BuildAsync`, `ReportIdentitySensitivity.IsSensitiveAsync`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task EachReadersCsvHoldsOnlyTheirRows()
    {
        var seed = await SeedAsync(FolderPermission.Read);

        var north = await (await AuthGet(seed.NorthToken, $"/api/reports/{seed.ReportId}/export/csv")).Content.ReadAsStringAsync();
        var south = await (await AuthGet(seed.SouthToken, $"/api/reports/{seed.ReportId}/export/csv")).Content.ReadAsStringAsync();

        Assert.Contains("North", north); Assert.DoesNotContain("South", north); Assert.DoesNotContain("West", north);
        Assert.Contains("South", south); Assert.DoesNotContain("North", south); Assert.DoesNotContain("West", south);
    }
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~EachReadersCsvHoldsOnlyTheirRows`
Expected: FAIL, the response is the 404 body "No snapshot available".

- [ ] **Step 3: Implement**

Add `SessionCache sessions` and `PortalExecutionIdentityService identities` to the `ExportController` primary constructor (before the optional parameters). At the top of `LoadManifestAsync`, after the report is loaded:

```csharp
        // A report whose rows depend on the viewer has no snapshot. Export what this caller sees:
        // their own session, under their identity, with their current parameters.
        if (PortalPathGuard.TryResolveScript(portalConfig, _datasetScope.TenantId, report.ScriptPath, out var scriptPath)
            && await ReportIdentitySensitivity.IsSensitiveAsync(scriptPath, HttpContext.RequestAborted))
        {
            var identity = await identities.BuildAsync(
                CurrentUserId, _datasetScope.TenantId, IsAdmin, impersonatedUserId: null, HttpContext.RequestAborted);
            var svc = sessions.GetOrCreate(
                reportId, CurrentUserId, scriptPath,
                isAdministrator: IsAdmin, keyScope: _datasetScope.TenantId, executionIdentity: identity);
            return (await svc.GetManifestAsync(HttpContext.RequestAborted), null, false);
        }
```

- [ ] **Step 4: Run**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~RowLevelSecurityLiveViewTests|FullyQualifiedName~Export"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/ETL-SQL.Portal/Controllers/ExportController.cs tests/ETL-SQL.Portal.Tests/RowLevelSecurityLiveViewTests.cs
git commit -m "Export an identity-filtered report from the reader's own session"
```

---

### Task 6: A run of such a report leaves nothing on disk

**Files:**
- Modify: `src/ETL-SQL.Portal/Services/ExecutionJobService.cs` (lines 508-700)
- Test: `tests/ETL-SQL.Portal.Tests/RowLevelSecurityPortalTests.cs`

- [ ] **Step 1: Confirm nothing reads these results**

Run: `rg -n "ManifestPath" src/ETL-SQL.Portal --glob "*.cs" --glob "*.ts"`
Expected: readers use `ReportSnapshot.ManifestPath` only; `ExecutionJob.ManifestPath` is written and returned in `JobStatusResponse`, never opened. If something does open it for an identity-sensitive job, stop and report before changing anything.

- [ ] **Step 2: Write the failing test** (extend `IdentitySensitiveReport_PersistsNoSharedSnapshot_PlainReportDoes`)

After the two `ExecuteAndAwaitAsync` calls, resolve `SnapshotPackageService` from `_factory.Services` and assert:

```csharp
        var packages = _factory.Services.GetRequiredService<ETL_SQL.Portal.Services.SnapshotPackageService>();
        var sensitiveJob = await LastJobIdAsync(sensitiveId);
        Assert.False(await packages.ExistsAsync(
            ETL_SQL.Portal.Services.SnapshotPackageService.BuildSnapshotKey(sensitiveId, sensitiveJob), CancellationToken.None, "portal-host"),
            "An identity-filtered run left its rows in snapshot storage.");
```

Change `ExecuteAndAwaitAsync` to return the job ID it awaited and use that instead of a `LastJobIdAsync` helper.

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~IdentitySensitiveReport_PersistsNoSharedSnapshot`
Expected: FAIL with the message above.

- [ ] **Step 4: Implement**

Move the `identitySensitive` computation (currently at line 627) up to just after `manifestKey` is defined (line 510). In the in-process branch, guard the save:

```csharp
                // One reader's filtered rows are not written where a snapshot would go.
                if (!identitySensitive)
                    await SaveSnapshotManifestAsync(manifest, manifestKey, job.KeyScope, cts.Token);
```

Apply the same guard to the `SaveSnapshotManifestAsync` call in the remote branch (line 553). If the remote Orchestrator wrote the file itself, delete it there with the snapshot package service's delete method. Set `job.ManifestPath = identitySensitive ? null : manifestPath;` at completion. Leave the later `if (identitySensitive)` log and the snapshot-row skip as they are, minus the now-duplicate computation.

- [ ] **Step 5: Run**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~RowLevelSecurityPortalTests|FullyQualifiedName~ExecutionJob"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ETL-SQL.Portal/Services/ExecutionJobService.cs tests/ETL-SQL.Portal.Tests/RowLevelSecurityPortalTests.cs
git commit -m "Stop writing an identity-filtered run's rows to snapshot storage"
```

---

### Task 7: The viewer opens these reports live

**Files:**
- Modify: `src/ETL-SQL.Portal/BrowserSources/report-viewer-view.ts`
- Modify: `src/ETL-SQL.Portal/BrowserSources/report-viewer.ts`
- Modify: `scripts/test-report-viewer.mjs`
- Generated: `src/ETL-SQL.Portal/wwwroot/js/report-viewer*.js` via `node scripts\sync-assets.js` (check `git status`; some generated files are ignored and must not be force-added)

**Interfaces:**
- Consumes: `ReportDto.perViewer`, `POST /api/reports/{id}/live/reset`.
- Produces: `renderLiveView(report: ViewerReport, canDesign: boolean): string`; `ViewerReport.perViewer: boolean`.

- [ ] **Step 1: Write the failing check** (append to `scripts/test-report-viewer.mjs`, importing `renderLiveView` and `readViewerReport`)

```js
const live = renderLiveView({ id: 7, name: 'Regional', description: null, hasSnapshot: false, perViewer: true }, false);
assert.match(live, /Live · your rows/);
assert.match(live, /id="refreshBtn"/);
assert.match(live, /id="exportBtn"/);
assert.match(live, /id="subBtn"/);
assert.match(live, /id="reportFrame"/);
assert.doesNotMatch(live, /id="designBtn"/);
assert.doesNotMatch(live, /id="execBtn"/);
assert.equal(readViewerReport({ id: 7, name: 'R', hasSnapshot: false, perViewer: true }, 7).perViewer, true);
assert.equal(readViewerReport({ id: 7, name: 'R', hasSnapshot: false }, 7).perViewer, false);
```

- [ ] **Step 2: Run and confirm failure**

Run: `node scripts\test-report-viewer.mjs`
Expected: FAIL, `renderLiveView` is not exported.

- [ ] **Step 3: Implement the view**

In `report-viewer-view.ts`:

```ts
export type ViewerReport = Pick<ReportDto, 'id' | 'name' | 'description' | 'hasSnapshot'> & { perViewer: boolean };
```

In `readViewerReport`, return `perViewer: value.perViewer === true` as well. Add:

```ts
/** A report whose rows depend on the reader: shown live, with nothing built or stored to report on. */
export function renderLiveView(report: ViewerReport, canDesign: boolean): string {
    return frame('Report', report, report.description || 'No description provided.',
        '<div class="viewer-meta"><span class="viewer-meta-pill viewer-meta-ready" title="This report shows each reader their own rows. Nothing is stored.">Live · your rows</span></div>',
        action('refresh', 'Refresh', true) + design(canDesign) + action('structure', 'Structure') + action('dependencies', 'Dependencies')
        + action('history', 'History') + action('export', 'Export') + action('sub', 'Subscribe'),
        '<div class="viewer-body viewer-frame-shell" id="reportFrame"><div class="loading-state"><span class="spinner"></span><span>Rendering report…</span></div></div>');
}
```

- [ ] **Step 4: Implement the viewer**

In `report-viewer.ts`, import `renderLiveView`. Add beside `showSnapshot`:

```ts
    function showLive(current: ViewerReport) {
        main.innerHTML = renderLiveView(current, options.canDesign());
        const frame = root.createElement('iframe');
        frame.title = 'Report viewer';
        frame.style.cssText = 'width:100%;height:100%;border:none;';
        frame.srcdoc = buildReportFrameDocument(root, current.id, false, activePage);
        box('reportFrame').replaceChildren(frame);
    }

    /** Discards this reader's session, then shows the report again. Nothing is built or stored. */
    async function refreshLive(current: ViewerReport) {
        const opened = enter();
        report = current;
        main.innerHTML = '<div class="loading-state"><span class="spinner"></span><span>Loading report…</span></div>';
        try {
            const response = await apiFetch(`/api/reports/${current.id}/live/reset`, { method: 'POST', signal: opened.signal });
            if (response.status !== 204) throw await responseError(response, 'The report could not be refreshed.');
            if (live(opened)) showLive(current);
        } catch (failure) {
            if (live(opened)) main.innerHTML = `<div class="empty-state" role="alert">Failed to refresh report: ${esc(asError(failure).message)}</div>`;
        }
    }
```

In `open`, directly after `report = current;`:

```ts
            if (current.perViewer) { showLive(current); return; }
```

In the click handler, replace the refresh line:

```ts
        if (name === 'refresh' && current.perViewer) void refreshLive(current);
        else if (name === 'refresh' || name === 'retryRun') void run(current, {});
```

- [ ] **Step 5: Build assets and run the checks**

Run: `node scripts\sync-assets.js; node scripts\sync-assets.js -Check; node scripts\test-report-viewer.mjs; node scripts\test-portal-report-run-flow.mjs`
Expected: "Sync Complete.", "Check Complete.", both checks pass.

- [ ] **Step 6: Commit**

```bash
git add src/ETL-SQL.Portal/BrowserSources/report-viewer.ts src/ETL-SQL.Portal/BrowserSources/report-viewer-view.ts scripts/test-report-viewer.mjs
git commit -m "Open an identity-filtered report live in the Portal viewer"
```

Add any tracked generated file `git status` shows as modified.

---

### Task 8: The two-reader browser journey

**Files:**
- Modify: `tests/ETL-SQL.Portal.BrowserTests/PortalReaderJourneyTests.cs` (the uncommitted `TwoReadersOfOneReportEachSeeOnlyTheirOwnRows` and `OpenAndReadAsync`)

**Interfaces:**
- Consumes: everything above. Reuses the file's `CreateUserAsync`, `CreateFolderOwnedByAsync`, `GrantExecuteAsync`, `PublishAsync`, `SignInAsync`, `FrameAsync`, `SaleIdsShownAsync`, `WaitForRegionsAsync`.

- [ ] **Step 1: Change the grant helper to take a permission**

Rename `GrantExecuteAsync` to `GrantAsync(int folderId, Person reader, FolderPermission permission, string? groupName = null)` and pass `FolderPermission.Execute` from the ST18 test.

- [ ] **Step 2: Rewrite the test for the live view**

Replace the report script and the body after publishing with:

```csharp
        // Readers hold Read only. West belongs to neither group: a row no reader may see.
        var northGroup = await GrantAsync(folderId, north, FolderPermission.Read, $"region_north_{suffix}");
        var southGroup = await GrantAsync(folderId, south, FolderPermission.Read, $"region_south_{suffix}");
        var script = $"""
            CREATE TABLE #sales (SaleID INT, Region VARCHAR(20), Total DECIMAL(10,2));
            INSERT INTO #sales VALUES (1, 'North', 100), (2, 'North', 150), (3, 'South', 80),
              (4, 'South', 60), (5, 'West', 200), (6, 'West', 40);
            DECLARE @MinTotal INT = 0;
            SELECT 0 AS Floor INTO #floors UNION ALL SELECT 90 AS Floor;
            CREATE VISUAL FloorPicker AS SLICER (
              SOURCE = #floors,
              MAPPINGS (VALUE = Floor),
              OPTIONS (MODE = SINGLE, LAYOUT = 'DROPDOWN', INCLUDE_ALL = OFF, TITLE = 'Minimum total'),
              ACTIONS (ON_CHANGE = SET_PARAMETER(@MinTotal, value))
            );
            CREATE VISUAL Detail AS TABLE (
              SOURCE = (SELECT SaleID, Region, Total FROM #sales
                        WHERE Total >= @MinTotal
                          AND ((Region = 'North' AND HAS_GROUP('{northGroup}'))
                            OR (Region = 'South' AND HAS_GROUP('{southGroup}'))))
            );
            CREATE PAGE [Main] AS DASHBOARD (
              LAYOUT (STRUCTURE = 'A
                                   B', MAP ('A' = FloorPicker, 'B' = Detail))
            );
            """;
```

`OpenAndReadAsync` no longer presses Run: it signs in, clicks the library card, asserts `#execBtn` is absent and the header contains "Live · your rows", and returns `(await FrameAsync(page), await SaleIdsShownAsync(frame))`. Then:

```csharp
        Assert.Equal(new[] { 1, 2 }, northRows);
        Assert.Equal(new[] { 3, 4 }, southRows);

        // A parameter narrows each reader's own rows and never widens them.
        await northFrame.Locator("[data-visual-name='FloorPicker'] select").SelectOptionAsync("90");
        await WaitForSaleIdsAsync(northFrame, [1, 2]);
        await southFrame.Locator("[data-visual-name='FloorPicker'] select").SelectOptionAsync("90");
        await WaitForNoRowsAsync(southFrame);

        // A saved view, reopened from a fresh page, is still that reader's rows.
        await SaveViewAsync(northFrame, "Large sales");
        await northSession.Page.GotoAsync("/index.html");
        await northSession.Page.Locator($".report-card-link[data-id='{reportId}']").First.ClickAsync();
        northFrame = await FrameAsync(northSession.Page);
        await northFrame.Locator("#etlsql-views-menu-button").ClickAsync();
        await northFrame.Locator(".saved-view-menu-item", new() { HasText = "Large sales" }).ClickAsync();
        await WaitForSaleIdsAsync(northFrame, [1, 2]);

        // The exported file is that reader's rows too.
        var northCsv = await ExportCsvAsync(northSession.Page);
        Assert.Contains("North", northCsv); Assert.DoesNotContain("South", northCsv); Assert.DoesNotContain("West", northCsv);
        var southCsv = await ExportCsvAsync(southSession.Page);
        Assert.DoesNotContain("North", southCsv); Assert.DoesNotContain("West", southCsv);

        // Subscribe is offered from the live view and opens its dialog.
        await northSession.Page.Locator("#subBtn").ClickAsync();
        await Expect(northSession.Page.GetByRole(AriaRole.Dialog).Filter(new() { HasText = "Subscribe" })).ToBeVisibleAsync(new() { Timeout = 15_000 });
```

Keep the closing no-snapshot assertion and both `PageErrors` checks. Add the helpers:

```csharp
    private static async Task SaveViewAsync(IFrameLocator report, string name)
    {
        await report.Locator("#etlsql-views-menu-button").ClickAsync();
        await report.Locator("#etlsql-views-menu [role='menuitem']", new() { HasText = "Save current view as" }).ClickAsync();
        var dialog = report.Locator(".etlsql-feedback-dialog");
        await dialog.Locator("input").FillAsync(name);
        await dialog.Locator(".etlsql-feedback-btn-primary").ClickAsync();
        await Expect(report.Locator(".etlsql-feedback-toast", new() { HasText = $"Saved '{name}'." })).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private static async Task<string> ExportCsvAsync(IPage page)
    {
        await page.Locator("#exportBtn").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("#exportCsvBtn").ClickAsync());
        var path = await download.PathAsync();
        return await File.ReadAllTextAsync(path!);
    }
```

`WaitForSaleIdsAsync(frame, int[])` and `WaitForNoRowsAsync(frame)` follow `WaitForRegionsAsync` in this file, using `LoadAwareWait.UntilAsync` over `SaleIdsShownAsync`; for no rows, wait for `[data-visual-name='Detail'] tbody tr` filtered to data rows to have count 0. The ST18 test's save-view lines should call `SaveViewAsync` too.

- [ ] **Step 3: Run**

Run: `dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter FullyQualifiedName~PortalReaderJourneyTests`
Expected: 2 passed. If the Subscribe dialog has a different accessible name, read `reader-subscriptions.ts` `subscribe()` for the element it opens and assert on that.

- [ ] **Step 4: Run the viewer regressions**

Run: `dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter "FullyQualifiedName~ReportViewerSandboxTests|FullyQualifiedName~ReportLibrarySandboxTests|FullyQualifiedName~StudioInteractionJourneyTests|FullyQualifiedName~ReaderSubscriptionsSandboxTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add tests/ETL-SQL.Portal.BrowserTests/PortalReaderJourneyTests.cs
git commit -m "Prove two readers of one report each see only their own rows (ST19)"
```

---

### Task 9: A delivery carries each recipient's own rows

**Files:**
- Test: `tests/ETL-SQL.Portal.Tests/SubscriptionDeliverySecurityTests.cs`

**Interfaces:**
- Consumes: the existing `RlsReport_DeliversUnderRecipientIdentity_OrFailsForUnknownRecipient` setup and its recording runner (`LastIdentity`).

- [ ] **Step 1: Read the existing test's fixture** (lines 139-230 and the runner class near line 317) to see how a subscription, its recipients and the runner are built.

- [ ] **Step 2: Write the test**

Add `RlsReport_DeliveredToTwoRecipients_RunsOncePerRecipientUnderEachIdentity`: two Portal users with emails, each in a different region group; one subscription whose `Recipients` holds both emails; the report script `SELECT 1 AS Value INTO #data WHERE HAS_GROUP('<north group>');`. Change the recording runner to keep every identity it was called with (`List<ExecutionIdentity> Identities`), keeping `LastIdentity` as `Identities.LastOrDefault()`. Assert:

```csharp
        Assert.Equal(2, runner.Identities.Count);
        var byUser = runner.Identities.ToDictionary(i => i.EffectiveUser);
        Assert.Contains(northGroupName, byUser[northUserName].Groups);
        Assert.DoesNotContain(northGroupName, byUser[southUserName].Groups);
        Assert.Contains(southGroupName, byUser[southUserName].Groups);
```

This proves each recipient's export is produced by a run under that recipient's identity. Row filtering by identity is the engine's, covered by `RowLevelSecurityIdentityTests`; state that in the test's summary comment.

- [ ] **Step 3: Run**

Run: `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~SubscriptionDeliverySecurityTests`
Expected: all pass. This is existing behavior, so the new test should pass at once. If it fails, delivery does not run per recipient: stop and report, because that is a defect in shipped behavior.

- [ ] **Step 4: Commit**

```bash
git add tests/ETL-SQL.Portal.Tests/SubscriptionDeliverySecurityTests.cs
git commit -m "Prove a delivery runs once per recipient under each identity"
```

---

### Task 10: Documents and ledger

**Files:**
- Modify: `docs/administration/platform/row-level-security.md` (the "No shared snapshot" bullet)
- Modify: `docs/guides/reporting/report-row-level-security.md`
- Modify: `docs/architecture/decisions/portal-rls-report-viewing.md` (status: built, with test names)
- Modify: `docs/architecture/decisions/studio-reader-journey-evidence.md` (ST19 section becomes evidence)
- Modify: `docs/architecture/decisions/studio-beginner-acceptance-assessment.md`, `studio-authoring-completeness.md` (remove "cannot be viewed")
- Modify: `docs/architecture/portal.md` (add `POST /api/reports/{id}/live/reset`, the live `/manifest` behavior, `VIEW_REPORT_LIVE`)
- Modify: `CHANGELOG.md`, `TODO.md`

- [ ] **Step 1: Administration page.** Replace the last sentence of the bullet with: "These reports are shown live: opening one runs it for that reader, under their identity, and nothing is stored. Read on the folder is enough to open one, so for these reports a Read-only reader causes a run. Refresh discards that reader's own session. Export and subscriptions produce each person's own rows."

- [ ] **Step 2: Changelog.** Under `### Fixed`:

```markdown
- Show a row-level-secured report in the Portal. A report that filters rows by the viewer keeps no
  shared snapshot, and the viewer, its filters and export all read only snapshots, so a reader who
  ran one was returned to "Ready for first run". These reports now open live: each reader's rows are
  produced under their own identity and nothing is stored. Filters, saved views, export and
  Subscribe work from that same result. A run of such a report no longer leaves its rows in
  snapshot storage.
```

- [ ] **Step 3: TODO.** Mark ST19 `[x]` with "Done" and the date, the two test class names, the owner decisions (live, Read, Subscribe), and the open point that list endpoints do not carry `perViewer`. Update the §5 row in the open-work table.

- [ ] **Step 4: Run the documentation tests**

Run: `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~ETL_SQL.Tests.Docs`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add docs CHANGELOG.md TODO.md
git commit -m "Record how row-level-secured reports are viewed in the Portal (ST19)"
```

---

## Final verification

- [ ] `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~RowLevelSecurity|FullyQualifiedName~SessionCache|FullyQualifiedName~Subscription|FullyQualifiedName~SavedReportViewState|FullyQualifiedName~PortalExecutionIdentity"`
- [ ] `dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter "FullyQualifiedName~PortalReaderJourneyTests|FullyQualifiedName~ReportViewerSandboxTests|FullyQualifiedName~RoleJourneyTests"`
- [ ] `node scripts\sync-assets.js -Check`
- [ ] Run the `slopwatch` skill over the branch diff (CLAUDE.md quality gate).
- [ ] Check the Review Focus list: each of the five has a passing test named in its task.
