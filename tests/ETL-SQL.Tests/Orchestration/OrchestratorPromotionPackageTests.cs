using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Orchestrator.Storage;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "DeploymentProfile")]
public sealed class OrchestratorPromotionPackageTests : IDisposable
{
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), $"promotion-source-{Guid.NewGuid():N}.db");
    private readonly string _targetPath = Path.Combine(Path.GetTempPath(), $"promotion-target-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_sourcePath)) File.Delete(_sourcePath);
        if (File.Exists(_targetPath)) File.Delete(_targetPath);
    }

    [Fact]
    public async Task ExportImport_PreservesEligibleState_IsSecretSafe_AndIdempotent()
    {
        var source = new SQLiteJobHistoryStore(_sourcePath);
        await source.InitializeAsync();
        await source.SaveScheduleAsync(new ScheduleDefinition("nightly", "0 2 * * *", "UTC", CreatedBy: "owner@example.test"));
        await source.SaveNotificationAsync(new NotificationDefinition(
            "quality-alert", "SHARED:mail", "ops@example.test", Options: "{\"password\":\"SECRET:smtp.password\"}", CreatedBy: "owner@example.test"));
        await source.SaveJobAsync(new JobDefinition(
            "customer-load", "RUN SCRIPT 'pipelines/customer.etlsql';", 1, "HOUR", null, null, null,
            JobType: JobTargetKind.Script, TargetPath: "pipelines/customer.etlsql",
            Options: "{\"classification\":\"confidential\"}", CreatedBy: "owner@example.test"));
        await source.AddJobScheduleAsync("customer-load", "nightly", new DateTime(2026, 8, 3, 2, 0, 0, DateTimeKind.Utc));
        await source.AddJobNotificationAsync("customer-load", "quality-alert", NotificationTrigger.Failure);

        var started = new DateTime(2026, 8, 1, 2, 0, 0, DateTimeKind.Utc);
        var sourceRun = await source.ImportJobHistoryAsync(new JobHistoryEntry(
            41, "customer-load", started, started.AddMinutes(3), "FAILED", "quality gate",
            RowsProcessed: 20, RowsQuarantined: 2, DataQualityFailures: "email:not-null=2"));
        await source.SaveJobDataQualityFailuresAsync(sourceRun,
        [
            new DataQualityRuleFailureMetric("customers", "email", "not-null", "QUARANTINE", 2, "data-owner")
        ]);
        await source.SaveLineageAsync(
        [
            new LineageEntry("warehouse.customers", "MERGE")
            {
                SourceTables = ["stage.customers"],
                Metadata = new Dictionary<string, string> { ["owner"] = "data-owner", ["classification"] = "confidential" },
                SourceFile = "pipelines/customer.etlsql",
                Line = 12
            }
        ], "customer-load", "pipelines/customer.etlsql", started);

        var package = await OrchestratorPromotionPackageService.ExportAsync(source, source, source);
        await using var json = new MemoryStream();
        await OrchestratorPromotionPackageService.WriteAsync(package, json);
        var text = System.Text.Encoding.UTF8.GetString(json.ToArray());
        Assert.Contains("smtp.password", package.RequiredSecretReferences);
        Assert.DoesNotContain("resolved-secret-value", text);
        Assert.Contains("confidential", text);

        json.Position = 0;
        var roundTripped = await OrchestratorPromotionPackageService.ReadAsync(json);
        var target = new SQLiteJobHistoryStore(_targetPath);
        await target.InitializeAsync();
        var bindings = new Dictionary<string, string> { ["SHARED:mail"] = "SHARED:production-mail" };
        await OrchestratorPromotionPackageService.ImportAsync(roundTripped, target, target, target, bindings);
        await OrchestratorPromotionPackageService.ImportAsync(roundTripped, target, target, target, bindings);

        var importedJob = Assert.Single(await target.GetAllJobsAsync());
        Assert.Equal("owner@example.test", importedJob.CreatedBy);
        Assert.Null(importedJob.LastRun);
        Assert.False(importedJob.IsEnabled);
        Assert.Single(await target.GetSchedulesAsync());
        var importedNotification = Assert.Single(await target.GetNotificationsAsync());
        Assert.Equal("SHARED:production-mail", importedNotification.ConnectionName);
        Assert.Single(await target.GetJobSchedulesAsync());
        Assert.Single(await target.GetJobNotificationsAsync());
        Assert.Single(await target.GetHistoryAsync(limit: 100));
        Assert.Single(await target.GetDataQualityFailuresAsync());
        var importedLineage = Assert.Single(await target.GetRecentLineageAsync());
        Assert.Equal("data-owner", importedLineage.Tags["owner"]);

        await target.SaveScheduleAsync(new ScheduleDefinition("nightly", "0 4 * * *", "UTC"));
        var collision = await OrchestratorPromotionPackageService.ValidateAsync(roundTripped, target, target, bindings);
        Assert.False(collision.IsValid);
        Assert.Contains(collision.Findings, finding => finding.Code == "OP003" && finding.Resource == "schedule:nightly");
    }

    [Fact]
    public async Task Export_RefusesRawCredentials()
    {
        var source = new SQLiteJobHistoryStore(_sourcePath);
        await source.InitializeAsync();
        await source.SaveJobAsync(new JobDefinition(
            "unsafe", "CREATE CONNECTION x AS API(API_KEY='raw-value');", 1, "HOUR", null, null, null));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OrchestratorPromotionPackageService.ExportAsync(source, source, source));

        Assert.DoesNotContain("raw-value", error.Message);
    }
}
