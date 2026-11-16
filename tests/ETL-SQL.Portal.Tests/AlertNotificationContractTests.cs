using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
[Trait("CompatBreak", "0.22")]
public class AlertNotificationContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false, "recipient")]
    [InlineData(false, "Recipient")]
    [InlineData(false, "smtpAlias")]
    [InlineData(false, "SmtpAlias")]
    [InlineData(true, "recipient")]
    [InlineData(true, "Recipient")]
    [InlineData(true, "smtpAlias")]
    [InlineData(true, "SmtpAlias")]
    public void InlineDeliveryPayloadsAreRejected(bool update, string retiredField)
    {
        var fields = new Dictionary<string, object?>
        {
            ["name"] = "RevenueAlert",
            ["visualName"] = "Revenue",
            ["operator"] = ">",
            ["threshold"] = 100m,
            [retiredField] = null
        };
        var payload = JsonSerializer.Serialize(fields, JsonOptions);
        Assert.Throws<JsonException>(() =>
        {
            if (update)
                _ = JsonSerializer.Deserialize<UpdateReportAlertRequest>(payload, JsonOptions);
            else
                _ = JsonSerializer.Deserialize<CreateReportAlertRequest>(payload, JsonOptions);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlertModelsUseNotificationAttachmentsWithoutInlineDeliveryColumns(bool postgres)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>();
        if (postgres)
            options.UseNpgsql("Host=localhost;Database=design_only;Username=design_only");
        else
            options.UseSqlite("Data Source=:memory:");
        using var db = new PortalDbContext(options.Options);
        var alert = db.Model.FindEntityType(typeof(ReportAlert))!;
        Assert.Null(alert.FindProperty("Recipient"));
        Assert.Null(alert.FindProperty("SmtpAlias"));
        Assert.NotNull(alert.FindNavigation(nameof(ReportAlert.Notifications)));
        Assert.DoesNotContain(typeof(ReportAlertDto).GetProperties(), property => property.Name is "Recipient" or "SmtpAlias");
    }

    [Fact]
    public void CanonicalAlertAndNamedNotificationPayloadsDeserialize()
    {
        var request = JsonSerializer.Deserialize<CreateReportAlertRequest>(
            """{"name":"RevenueAlert","visualName":"Revenue","operator":">","threshold":100}""", JsonOptions);
        Assert.NotNull(request);
        Assert.Equal("RevenueAlert", request.Name);
        Assert.Equal(100m, request.Threshold);
        var notification = JsonSerializer.Deserialize<AlterAlertNotificationRequest>(
            """{"orchestratorAlias":"operations","notificationName":"FinanceOps"}""", JsonOptions);
        Assert.NotNull(notification);
        Assert.Equal("operations", notification.OrchestratorAlias);
        Assert.Equal("FinanceOps", notification.NotificationName);
    }
}
