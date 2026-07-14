using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Portal")]
public class PortalModuleHostedServiceTests
{
    [Fact]
    public void Disabled_Reporting_Module_Does_Not_Register_Reporting_Worker_Loops()
    {
        using var factory = CreateFactory(config => config.Modules.Reporting = false);
        using var _ = factory.CreateClient();

        var hostedTypes = HostedTypes(factory);

        Assert.DoesNotContain(typeof(SessionCache), hostedTypes);
        Assert.DoesNotContain(typeof(ExecutionJobService), hostedTypes);
        Assert.DoesNotContain(typeof(OrchestratorPollerService), hostedTypes);
        Assert.DoesNotContain(typeof(DatasetAtRestKeyValidationService), hostedTypes);
        Assert.DoesNotContain(typeof(SnapshotMigrationService), hostedTypes);
        Assert.Contains(typeof(JwtSecretValidationService), hostedTypes);
        Assert.Contains(typeof(AuditRetentionService), hostedTypes);
    }

    [Fact]
    public void Disabled_Scheduling_Module_Does_Not_Register_Orchestrator_Poller()
    {
        using var factory = CreateFactory(config => config.Modules.Scheduling = false);
        using var _ = factory.CreateClient();

        var hostedTypes = HostedTypes(factory);

        Assert.Contains(typeof(SessionCache), hostedTypes);
        Assert.Contains(typeof(ExecutionJobService), hostedTypes);
        Assert.DoesNotContain(typeof(OrchestratorPollerService), hostedTypes);
    }

    [Fact]
    public void Disabled_Operations_Module_Does_Not_Register_Operations_Digest_Workers()
    {
        using var factory = CreateFactory(config => config.Modules.Operations = false);
        using var _ = factory.CreateClient();

        var hostedTypes = HostedTypes(factory);

        Assert.DoesNotContain(typeof(OperationalMetricsDigestService), hostedTypes);
        Assert.DoesNotContain(typeof(FailureDigestAdminService), hostedTypes);
        Assert.DoesNotContain(typeof(BackupReportAdminService), hostedTypes);
        Assert.DoesNotContain(typeof(CapacityReportAdminService), hostedTypes);
        Assert.Contains(typeof(SessionCache), hostedTypes);
        Assert.Contains(typeof(ExecutionJobService), hostedTypes);
    }

    private static HashSet<Type> HostedTypes(PortalWebFactory factory) =>
        factory.Services.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .ToHashSet();

    private static HostedPortalFactory CreateFactory(Action<PortalConfig> customize) =>
        new(settings: settings => Apply(settings, customize), portalConfig: customize);

    private static void Apply(Dictionary<string, string?> settings, Action<PortalConfig> customize)
    {
        var config = new PortalConfig();
        customize(config);
        settings["Portal:Modules:Reporting"] = config.Modules.Reporting.ToString();
        settings["Portal:Modules:Designer"] = config.Modules.Designer.ToString();
        settings["Portal:Modules:ConnectionCatalog"] = config.Modules.ConnectionCatalog.ToString();
        settings["Portal:Modules:SecretStore"] = config.Modules.SecretStore.ToString();
        settings["Portal:Modules:Scheduling"] = config.Modules.Scheduling.ToString();
        settings["Portal:Modules:Operations"] = config.Modules.Operations.ToString();
        settings["Portal:Modules:Documentation"] = config.Modules.Documentation.ToString();
    }
}
