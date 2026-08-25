using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class WorkloadIdentityApprovalTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private PortalDbContext db = null!;
    private WorkloadIdentityApprovalService service = null!;
    private readonly WorkloadIdentityBindingConfig binding = new()
    {
        Id = "sensitive-publish",
        Provider = "github",
        ServiceAccountClientId = "sa_publish",
        TenantId = "tenant-a",
        Issuer = "https://token.actions.githubusercontent.com",
        Subject = "repo:etl-sql/ETL-SQL:environment:production",
        Audience = "etl-sql-ci",
        Resource = "/api/orchestrator/jobs",
        Operations = [ServiceAccountScopes.OrchestratorPublish],
        RequireApproval = true
    };

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var config = new PortalConfig
        {
            Jwt = new JwtConfig { Secret = "approval-test-secret-key-1234567890" }
        };
        service = new WorkloadIdentityApprovalService(config,
            new WorkloadIdentityReplayCache(db, TimeProvider.System), TimeProvider.System);
    }

    [Fact]
    public async Task ApprovalIsSignedExactlyBoundAndSingleUse()
    {
        var token = service.Issue(binding, approvedByUserId: 17);
        await service.ValidateAsync(binding, token, CancellationToken.None);

        var replay = await Assert.ThrowsAsync<WorkloadIdentityException>(() =>
            service.ValidateAsync(binding, token, CancellationToken.None));
        Assert.Equal("workload_approval_replay_rejected", replay.Code);

        var second = service.Issue(binding, approvedByUserId: 17);
        var otherResource = binding with { Resource = "/api/orchestrator/jobs/other" };
        var mismatch = await Assert.ThrowsAsync<WorkloadIdentityException>(() =>
            service.ValidateAsync(otherResource, second, CancellationToken.None));
        Assert.Equal("invalid_workload_approval", mismatch.Code);
    }

    [Fact]
    public async Task RequiredApprovalCannotBeOmitted()
    {
        var error = await Assert.ThrowsAsync<WorkloadIdentityException>(() =>
            service.ValidateAsync(binding, null, CancellationToken.None));
        Assert.Equal("workload_approval_required", error.Code);
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }
}
