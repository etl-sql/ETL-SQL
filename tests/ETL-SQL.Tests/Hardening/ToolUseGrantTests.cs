using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Scheduling;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

[Trait("CompatBreak", "0.24")]
public sealed class ToolUseGrantTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"etlsql-tool-grants-{Guid.NewGuid():N}");
    private const string Alias = "mask_sales";
    private static readonly ToolUseGrant Finance = new("tenant-a", ToolPrincipalKind.Group, "finance-key");

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private LocalToolCatalogProvider Catalog(bool requireGrants = false) =>
        new(root, ToolCatalogOwnershipMode.PersonalWorkstation, validateOwnership: false, requireUseGrants: requireGrants);

    private static readonly ToolDefinition Definition = new(Alias, "CONTAINER",
        new Dictionary<string, string> { ["COMMAND"] = "echo" }, false);

    private static ExecutionIdentity Caller(string tenant = "tenant-a", string group = "finance-key") => new()
    {
        EffectiveUser = "user:alice",
        RealUser = "user:alice",
        TenantId = tenant,
        IsAdmin = true,
        Groups = ["Finance"],
        ToolPrincipal = new(tenant, ToolPrincipalKind.User, "alice", [group], true)
    };

    [Fact]
    public async Task LegacyEntryRequiresTenantBoundGrantOnSharedHost()
    {
        Directory.CreateDirectory(root);
        var legacyEntry = CryptoUtils.ProtectMachine(
            """{"ToolType":"CONTAINER","Options":{"COMMAND":"echo"}}""", Alias);
        await File.WriteAllTextAsync(Path.Combine(root, $"{Alias}.json"), legacyEntry);

        var local = Catalog();
        var shared = Catalog(requireGrants: true);
        Assert.Equal("echo", (await local.ResolveAsync(Alias)).Options["COMMAND"]);
        Assert.Equal("echo", (await shared.GetDefinitionAsync(Alias)).Options["COMMAND"]);
        var missingGrant = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, Caller()));
        Assert.Equal((await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => shared.ResolveAsync(Alias, Caller(group: "other-key")))).Message, missingGrant.Message);

        Assert.True(await shared.SetUseGrantAsync(Alias, Finance, allow: true));
        var upgradedEntry = await File.ReadAllTextAsync(Path.Combine(root, $"{Alias}.json"));
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(CryptoUtils.Unprotect(upgradedEntry, Alias)));
        Assert.False(await shared.SetUseGrantAsync(Alias, Finance, allow: true));
        Assert.Equal("echo", (await shared.ResolveAsync(Alias.ToUpperInvariant(), Caller(group: "FINANCE-KEY"))).Options["COMMAND"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, Caller("tenant-b")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, Caller(group: "Finance")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, Caller() with { ToolPrincipal = null }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, Caller() with { TenantId = "tenant-b" }));
        var preview = ExecutionIdentity.Preview("preview", ["Finance"], ["Admin"], "user:alice", "tenant-a");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, preview));
        Assert.True(await shared.SetUseGrantAsync(Alias, Finance, allow: false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => local.ResolveAsync(Alias));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shared.ResolveAsync(Alias, Caller()));
    }

    [Fact]
    public async Task ScheduledOwnerLabelGrantsNoToolAuthority()
    {
        var catalog = Catalog(requireGrants: true);
        await catalog.StoreAsync(Definition);
        await catalog.SetUseGrantAsync(Alias, Finance, allow: true);
        var caller = Caller();
        var job = new JobDefinition("NightlySales", "", 1, "DAY", null, null, null,
            CreatedBy: caller.EffectiveUser, TenantId: "tenant-a");

        var unattended = SchedulerService.ResolveJobExecutionIdentity(job);
        Assert.NotNull(unattended);
        Assert.Equal(caller.EffectiveUser, unattended.EffectiveUser);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.ResolveAsync(Alias, unattended));

        var authenticated = SchedulerService.ResolveJobExecutionIdentity(job, explicitIdentity: caller);
        Assert.Equal(Alias, (await catalog.ResolveAsync(Alias, authenticated)).Name);
    }

    [Fact]
    public async Task ConcurrentMutationsPreserveGrantsAndDisabledState()
    {
        var first = Catalog(requireGrants: true);
        var second = Catalog(requireGrants: true);
        await first.StoreAsync(Definition);
        await first.DisableAsync(Alias);
        var automation = new ToolUseGrant("tenant-a", ToolPrincipalKind.Service, "automation");
        await Task.WhenAll(first.SetUseGrantAsync(Alias, Finance, allow: true),
            second.SetUseGrantAsync(Alias, automation, allow: true));
        Assert.Empty(await first.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second.ResolveAsync(Alias, Caller()));

        await Task.WhenAll(first.StoreAsync(Definition with { Options = new Dictionary<string, string> { ["COMMAND"] = "updated" } }),
            second.SetUseGrantAsync(Alias, new("tenant-a", ToolPrincipalKind.User, "bob"), allow: true));
        Assert.Equal("updated", (await first.ResolveAsync(Alias, Caller())).Options["COMMAND"]);
        var bob = Caller(group: "other-key") with
        {
            ToolPrincipal = new("tenant-a", ToolPrincipalKind.User, "bob", [], true)
        };
        Assert.Equal(Alias, (await first.ResolveAsync(Alias, bob)).Name);
        var service = Caller(group: "other-key") with
        {
            ToolPrincipal = new("tenant-a", ToolPrincipalKind.Service, "automation", [], true)
        };
        Assert.Equal(Alias, (await first.ResolveAsync(Alias, service)).Name);
        var readOnly = service with { ToolPrincipal = service.ToolPrincipal! with { WithinExecutionScope = false } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.ResolveAsync(Alias, readOnly));
    }
}
