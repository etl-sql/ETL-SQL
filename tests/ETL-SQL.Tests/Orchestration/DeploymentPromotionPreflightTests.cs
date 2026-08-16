using ETL_SQL.App;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "DeploymentProfile")]
public sealed class DeploymentPromotionPreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"etl-sql-promotion-{Guid.NewGuid():N}");

    public DeploymentPromotionPreflightTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Build_ClassifiesAllSixInventoryClasses_WithoutReadingProtectedMaterial()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pipelines"));
        Directory.CreateDirectory(Path.Combine(_root, "artifacts", "release-evidence", "0.18.0"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "pipelines", "load.etlsql"),
            "CREATE CONNECTION src AS POSTGRES(PASSWORD='SECRET:sales.password'); SELECT * FROM SHARED:warehouse.sales;");
        await File.WriteAllTextAsync(Path.Combine(_root, "report.rptsql"), "SELECT 1 INTO #data;");
        await File.WriteAllTextAsync(Path.Combine(_root, "etlsql-policy.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_root, "portal.db"), "not-a-real-database");
        await File.WriteAllTextAsync(Path.Combine(_root, ".env"), "PASSWORD=never-export-this");
        await File.WriteAllTextAsync(Path.Combine(_root, "artifacts", "release-evidence", "0.18.0", "gate.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_root, "bin", "temporary.dll"), "ephemeral");

        var inventory = await DeploymentPromotionPreflightService.BuildAsync(_root, "Solo", "Team");

        Assert.Equal(DeploymentPromotionPreflightService.SchemaVersion, inventory.SchemaVersion);
        Assert.False(inventory.MutationPerformed);
        Assert.True(inventory.Ready);
        Assert.Equal(3, inventory.PortableArtifacts.Count);
        Assert.Single(inventory.ExportableCatalogState);
        Assert.Contains(inventory.TargetBindings, binding => binding.Kind == "Secret" && binding.Name == "sales.password");
        Assert.Contains(inventory.TargetBindings, binding => binding.Kind == "Connection" && binding.Name == "warehouse.sales");
        var protectedEntry = Assert.Single(inventory.ProtectedMaterial);
        Assert.Equal(".env", protectedEntry.Path);
        Assert.Null(protectedEntry.SizeBytes);
        Assert.Null(protectedEntry.Sha256);
        Assert.Single(inventory.OperationalEvidence);
        Assert.Contains(inventory.EphemeralState, entry => entry.Path == "bin");
        Assert.DoesNotContain("never-export-this", System.Text.Json.JsonSerializer.Serialize(inventory));
    }

    [Fact]
    public async Task Build_FailsReadinessForRawCredential_WithoutDisclosingValue()
    {
        const string rawSecret = "do-not-leak-this";
        await File.WriteAllTextAsync(Path.Combine(_root, "unsafe.etlsql"),
            $"CREATE CONNECTION src AS POSTGRES(PASSWORD='{rawSecret}');");

        var inventory = await DeploymentPromotionPreflightService.BuildAsync(_root, "Solo", "Enterprise");

        Assert.False(inventory.Ready);
        Assert.Contains(inventory.Findings, finding => finding.Code == "DP008" && finding.Severity == "Error");
        Assert.DoesNotContain(rawSecret, System.Text.Json.JsonSerializer.Serialize(inventory));
    }

    [Fact]
    public async Task Build_RejectsBackwardProfileTransition()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "pipeline.etlsql"), "SELECT 1;");

        var inventory = await DeploymentPromotionPreflightService.BuildAsync(_root, "Enterprise", "Team");

        Assert.False(inventory.Ready);
        Assert.Contains(inventory.Findings, finding => finding.Code == "DP001");
    }

    [Fact]
    public async Task Build_BlocksPromotionThatWouldCarryUnownedOrchestratorObjects()
    {
        // Ownership is what lets anyone but an administrator reach an object from Team upward, so a
        // promotion that carried unowned jobs across would hand the new deployment work only an
        // administrator can run — and the symptom arrives much later, as "the schedule fires and
        // nobody can touch it".
        await File.WriteAllTextAsync(Path.Combine(_root, "pipeline.etlsql"), "SELECT 1;");

        var inventory = await DeploymentPromotionPreflightService.BuildAsync(
            _root, "Solo", "Team", default,
            [new("JOB", "nightly_load"), new("SCHEDULE", "hourly")]);

        Assert.False(inventory.Ready);
        var finding = Assert.Single(inventory.Findings, entry => entry.Code == "DP009");
        Assert.Equal("Error", finding.Severity);
        Assert.Contains("nightly_load", finding.Message);
        Assert.Contains("hourly", finding.Message);
        // The remedy is named, because "2 objects have no owner" tells an operator what is wrong and
        // not what to do about it.
        Assert.Contains("admin orchestrator adopt", finding.Message);
    }

    [Fact]
    public async Task Build_DoesNotReportUnownedObjectsWhenTheTargetIsSolo()
    {
        // Solo has no principals, so nothing decides on ownership there and reporting it would be a
        // blocking finding about a property that cannot matter yet.
        await File.WriteAllTextAsync(Path.Combine(_root, "pipeline.etlsql"), "SELECT 1;");

        var inventory = await DeploymentPromotionPreflightService.BuildAsync(
            _root, "Solo", "Solo", default, [new("JOB", "nightly_load")]);

        Assert.DoesNotContain(inventory.Findings, finding => finding.Code == "DP009");
        Assert.True(inventory.Ready);
    }

    [Fact]
    public async Task Build_IsReadyWhenEveryOrchestratorObjectHasAnOwner()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "pipeline.etlsql"), "SELECT 1;");

        var inventory = await DeploymentPromotionPreflightService.BuildAsync(
            _root, "Solo", "Enterprise", default, []);

        Assert.DoesNotContain(inventory.Findings, finding => finding.Code == "DP009");
        Assert.True(inventory.Ready);
    }
}
