using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using Xunit;

namespace ETL_SQL.Tests.CliCommands;

public sealed class SaasTenantDeletionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"etlsql_tenant_deletion_{Guid.NewGuid():N}");

    public SaasTenantDeletionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task AuthorizedDeletionRemovesBoundaryAndPersistsExternalCompletionRecord()
    {
        var tenantRoot = await SeedBoundaryAsync("tenant-alpha");
        var receiptRoot = Path.Combine(_root, "receipts");
        var now = DateTimeOffset.UtcNow;
        var authority = Authority("tenant-alpha", now.AddHours(-1), now);

        var receipt = await SaasTenantDeletionService.DeleteAsync(new CliContext
        {
            SaasTenantId = "tenant-alpha",
            SaasDeletionTenantRoot = tenantRoot,
            SaasDeletionReceiptRoot = receiptRoot,
            SaasDeletionExecute = true
        }, authority, now);

        Assert.False(Directory.Exists(tenantRoot));
        Assert.Equal("Completed", receipt.Status);
        Assert.Equal("tenant-alpha", receipt.TenantId);
        Assert.True(receipt.LegalHoldCleared);
        Assert.Equal(2, receipt.FileCount);
        Assert.True(receipt.TotalBytes > 0);
        Assert.Equal(64, receipt.BoundaryDigestSha256.Length);
        Assert.False(receipt.TenantUserImpersonation);
        var receiptPath = Assert.Single(Directory.GetFiles(receiptRoot, "tenant-deletion-*.json"));
        var persisted = JsonSerializer.Deserialize<SaasTenantDeletionService.DeletionReceipt>(
            await File.ReadAllTextAsync(receiptPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Completed", persisted!.Status);
        Assert.NotNull(persisted.CompletedUtc);
    }

    [Fact]
    public async Task TenantMismatchOrReceiptInsideBoundaryCannotDelete()
    {
        var tenantRoot = await SeedBoundaryAsync("tenant-alpha");
        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            SaasTenantDeletionService.DeleteAsync(new CliContext
            {
                SaasTenantId = "tenant-beta",
                SaasDeletionTenantRoot = tenantRoot,
                SaasDeletionReceiptRoot = Path.Combine(_root, "receipts")
            }, Authority("tenant-alpha", now.AddHours(-1), now), now));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SaasTenantDeletionService.DeleteAsync(new CliContext
            {
                SaasTenantId = "tenant-alpha",
                SaasDeletionTenantRoot = tenantRoot,
                SaasDeletionReceiptRoot = Path.Combine(tenantRoot, "receipts")
            }, Authority("tenant-alpha", now.AddHours(-1), now), now));
        Assert.True(Directory.Exists(tenantRoot));
    }

    [Fact]
    public async Task PreflightInventoriesButDoesNotMutateBoundaryOrReceiptRoot()
    {
        var tenantRoot = await SeedBoundaryAsync("tenant-alpha");
        var receiptRoot = Path.Combine(_root, "not-created-by-preflight");
        var now = DateTimeOffset.UtcNow;

        var receipt = await SaasTenantDeletionService.DeleteAsync(new CliContext
        {
            SaasTenantId = "tenant-alpha",
            SaasDeletionTenantRoot = tenantRoot,
            SaasDeletionReceiptRoot = receiptRoot
        }, Authority("tenant-alpha", now.AddHours(-1), now), now, execute: false);

        Assert.Equal("Preflight", receipt.Status);
        Assert.Equal(2, receipt.FileCount);
        Assert.True(Directory.Exists(tenantRoot));
        Assert.False(Directory.Exists(receiptRoot));
    }

    [Fact]
    public void SignedPolicyEnforcesRetentionLegalHoldExpiryAndCliMismatch()
    {
        var now = DateTimeOffset.UtcNow;
        var valid = Policy("tenant-alpha", now.AddMinutes(-1), legalHoldCleared: true, now.AddMinutes(10));
        var authority = SaasTenantDeletionService.ResolveAuthorizedContext(
            new CliContext { SaasTenantId = "tenant-alpha" }, valid, now);
        Assert.Equal("tenant-alpha", authority.TenantContext.Tenant.Value);

        Assert.Throws<UnauthorizedAccessException>(() =>
            SaasTenantDeletionService.ResolveAuthorizedContext(
                new CliContext { SaasTenantId = "tenant-beta" }, valid, now));
        Assert.Throws<UnauthorizedAccessException>(() =>
            SaasTenantDeletionService.ResolveAuthorizedContext(
                new CliContext { SaasTenantId = "tenant-alpha" },
                Policy("tenant-alpha", now.AddMinutes(1), true, now.AddMinutes(10)), now));
        Assert.Throws<UnauthorizedAccessException>(() =>
            SaasTenantDeletionService.ResolveAuthorizedContext(
                new CliContext { SaasTenantId = "tenant-alpha" },
                Policy("tenant-alpha", now.AddMinutes(-1), false, now.AddMinutes(10)), now));
        Assert.Throws<ArgumentException>(() =>
            SaasTenantDeletionService.ResolveAuthorizedContext(
                new CliContext { SaasTenantId = "tenant-alpha" },
                Policy("tenant-alpha", now.AddMinutes(-2), true, now.AddMinutes(-1)), now));
    }

    private async Task<string> SeedBoundaryAsync(string tenant)
    {
        var boundary = Path.Combine(_root, tenant);
        Directory.CreateDirectory(Path.Combine(boundary, "config"));
        await File.WriteAllTextAsync(Path.Combine(boundary, "tenant-manifest.json"),
            $$"""{ "tenantId": "{{tenant}}" }""");
        await File.WriteAllTextAsync(Path.Combine(boundary, "config", "appsettings.tenant.json"),
            $$"""{ "SaasTenant": { "TenantId": "{{tenant}}" } }""");
        return boundary;
    }

    private static SaasTenantDeletionService.DeletionAuthority Authority(
        string tenant,
        DateTimeOffset retainedUntil,
        DateTimeOffset now) =>
        new(
            TenantContext.FromPlatformGrant(
                PlatformAccessGrant.Issue(
                    tenant, "privacy-operator@platform.test", "privacy-42",
                    "approved tenant erasure", now.AddMinutes(10), now), now),
            retainedUntil,
            LegalHoldCleared: true);

    private static EffectiveEnterprisePolicy Policy(
        string tenant,
        DateTimeOffset retainedUntil,
        bool legalHoldCleared,
        DateTimeOffset expires) =>
        new(
            true, true, "Current", "42", "Live", DateTimeOffset.UtcNow.AddMinutes(-1),
            expires, DateTimeOffset.UtcNow,
            new OrganizationPolicyDocument
            {
                SaasDeletion = new SaasDeletionAuthorizationPolicySection
                {
                    Enabled = true,
                    TenantId = tenant,
                    OperatorPrincipal = "privacy-operator@platform.test",
                    AuthorizationReference = "privacy-42",
                    Reason = "approved tenant erasure",
                    RetentionUntilUtc = retainedUntil,
                    LegalHoldCleared = legalHoldCleared,
                    ExpiresUtc = expires
                }
            },
            new Dictionary<string, string?>());
}
