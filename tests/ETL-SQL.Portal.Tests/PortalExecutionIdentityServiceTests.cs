using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The identity a row-level-secured report runs under, and the check that decides whether a report
/// is one. Both are shared by the report job, the live viewer session and export, so a difference
/// between them would show different rows on different paths.
/// </summary>
[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.21.0")]
public sealed class PortalExecutionIdentityServiceTests : IClassFixture<PortalWebFactory>
{
    private readonly PortalWebFactory _factory;

    public PortalExecutionIdentityServiceTests(PortalWebFactory factory)
    {
        _factory = factory;
        factory.CreateClient().Dispose();
    }

    [Fact]
    public async Task BuildsTheUsersNameGroupsAndRolesFromTheDatabase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = new PortalUser
            {
                UserName = $"ident_{suffix}",
                NormalizedUserName = $"IDENT_{suffix}".ToUpperInvariant(),
                Email = $"ident_{suffix}@test.local"
            };
            var group = new Group { Name = $"Region:East {suffix}" };
            db.Users.Add(user);
            db.Groups.Add(group);
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
    [InlineData("RUN SCRIPT 'child.etlsql';", true)]
    [InlineData("RUN /* dependency */ SCRIPT @path;", true)]
    [InlineData("SELECT 'RUN SCRIPT child' AS Note;", false)]
    public async Task SensitivityFollowsTheScript(string script, bool expected)
    {
        var path = Path.Combine(_factory.TempDir, "scripts", $"sens_{Guid.NewGuid():N}.rptsql");
        await File.WriteAllTextAsync(path, script);
        Assert.Equal(expected, await ReportIdentitySensitivity.IsSensitiveAsync(path, CancellationToken.None));
    }
}
