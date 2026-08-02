using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class ConfigurationPromotionValidationTests
{
    [Fact]
    public async Task Validation_IsReadOnly_Idempotent_AndReportsTargetCollision()
    {
        using var factory = new PortalWebFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.Groups.Add(new Group { Name = "finance", Description = "Finance", Provider = "Local" });
        await db.SaveChangesAsync();
        var validator = scope.ServiceProvider.GetRequiredService<ConfigurationPromotionValidationService>();

        var matching = await validator.ValidateAsync("""
            EXECUTE portal BEGIN
                CREATE GROUP 'finance' WITH (DESCRIPTION = 'Finance');
            END;
            """, new Dictionary<string, string> { ["unused-dev"] = "unused-prod" });

        Assert.True(matching.IsValid);
        Assert.Contains(matching.Findings, finding => finding.Code == "PV005" && finding.Severity == "Warning");
        Assert.Equal(1, db.Groups.Count(group => group.Name == "finance"));

        var collision = await validator.ValidateAsync("""
            EXECUTE portal BEGIN
                CREATE GROUP 'finance' WITH (DESCRIPTION = 'Different');
            END;
            """, null);

        Assert.False(collision.IsValid);
        Assert.Contains(collision.Findings, finding => finding.Code == "PV006" && finding.Resource == "group:finance");
        Assert.Equal("Finance", db.Groups.Single(group => group.Name == "finance").Description);
    }

    [Fact]
    public async Task Validation_RejectsRawCredentialsWithoutEchoingThem()
    {
        using var factory = new PortalWebFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ConfigurationPromotionValidationService>();
        const string secret = "never-echo-this";

        var result = await validator.ValidateAsync($"""
            EXECUTE portal BEGIN
                CREATE CONNECTION mail AS SMTP(PASSWORD = '{secret}');
            END;
            """, null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, finding => finding.Code == "PV002");
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(result));
    }
}
