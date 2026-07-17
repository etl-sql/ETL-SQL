using ETL_SQL.Portal;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

public class PortalModuleRegistryTests
{
    [Fact]
    public void Defaults_Enable_All_Modules()
    {
        var registry = new PortalModuleRegistry(new PortalConfig());

        Assert.All(registry.All, module => Assert.True(module.Enabled));
        Assert.True(registry.IsEnabled("Reporting"));
        Assert.True(registry.IsEnabled("reports"));
        Assert.True(registry.IsEnabled("ConnectionCatalog"));
        Assert.True(registry.IsEnabled("secret-store"));
        Assert.False(registry.IsEnabled("unknown"));
    }

    [Fact]
    public void Disabled_Modules_Are_Reported_And_Aliased()
    {
        var registry = new PortalModuleRegistry(new PortalConfig
        {
            Modules = new PortalModuleConfig
            {
                Reporting = false,
                Designer = false,
                ConnectionCatalog = false,
                SecretStore = true,
                Scheduling = true,
                Operations = true,
                Documentation = true
            }
        });

        Assert.False(registry.IsEnabled("Reporting"));
        Assert.False(registry.IsEnabled("datasets"));
        Assert.False(registry.IsEnabled("designer"));
        Assert.False(registry.IsEnabled("connections"));
        Assert.True(registry.IsEnabled("secrets"));
        Assert.Contains(registry.All, module => module.Name == "Reporting" && !module.Enabled);
    }

    [Fact]
    public void Portal_Host_Registers_Module_Registry_From_Config()
    {
        using var factory = new ModuleConfigFactory();

        var registry = factory.Services.GetRequiredService<PortalModuleRegistry>();

        Assert.False(registry.IsEnabled("Reporting"));
        Assert.False(registry.IsEnabled("Designer"));
        Assert.True(registry.IsEnabled("SecretStore"));
    }

    private sealed class ModuleConfigFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Modules.Reporting = false;
            config.Modules.Designer = false;
        }
    }
}
