using ETL_SQL.Portal.Services;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Tests;

public sealed class SupportAccessApprovalServiceTests
{
    private const string Secret = "support-approval-test-secret-at-least-32-characters";
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void CapabilityIsBoundToTenantDisclosureAndExpiry()
    {
        var now = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
        var alpha = Service("tenant-alpha");
        var issued = alpha.Issue("platform:case-42", HashA, "Investigate failed refreshes",
            "tenant-admin", 30, now);

        var approval = alpha.Validate(issued.Capability, HashA, now.AddMinutes(10));
        Assert.Equal("tenant-alpha", approval.TenantId);
        Assert.Equal("platform:case-42", approval.PlatformActor);
        Assert.Equal("Investigate failed refreshes", approval.Purpose);

        Assert.ThrowsAny<SecurityTokenException>(() =>
            alpha.Validate(issued.Capability, HashB, now.AddMinutes(10)));
        Assert.ThrowsAny<SecurityTokenException>(() =>
            Service("tenant-beta").Validate(issued.Capability, HashA, now.AddMinutes(10)));
        Assert.ThrowsAny<SecurityTokenException>(() =>
            alpha.Validate(issued.Capability, HashA, now.AddMinutes(31)));
    }

    [Fact]
    public void CapabilityIsUnavailableWithoutAHostFixedDedicatedTenant()
    {
        var service = Service(null);
        Assert.Throws<InvalidOperationException>(() =>
            service.Issue("platform:case-42", HashA, "Investigate failed refreshes",
                "tenant-admin", 30));
    }

    [Fact]
    public void ApprovalRejectsUnboundedOrWeakInputs()
    {
        var service = Service("tenant-alpha");
        Assert.Throws<ArgumentException>(() =>
            service.Issue("x", HashA, "Investigate failed refreshes", "tenant-admin", 30));
        Assert.Throws<ArgumentException>(() =>
            service.Issue("platform:case-42", "not-a-hash", "Investigate failed refreshes", "tenant-admin", 30));
        Assert.Throws<ArgumentException>(() =>
            service.Issue("platform:case-42", HashA, "short", "tenant-admin", 30));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Issue("platform:case-42", HashA, "Investigate failed refreshes", "tenant-admin", 61));
    }

    private static SupportAccessApprovalService Service(string? tenant)
    {
        var config = new PortalConfig { TenantId = tenant };
        config.Jwt.Secret = Secret;
        return new SupportAccessApprovalService(config);
    }
}
