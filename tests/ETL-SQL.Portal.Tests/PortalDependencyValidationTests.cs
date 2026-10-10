using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class PortalDependencyValidationTests
{
    [Fact]
    public async Task SoloHost_WithHostedServicesAndDependencyValidation_ServesAuthenticationProviders()
    {
        await using var factory = new ValidatedPortalFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        Assert.False(config.SharedTenancy.Enabled);
        Assert.Null(config.TenantId);
    }

    private sealed class ValidatedPortalFactory : PortalWebFactory
    {
        protected override void ConfigureHostedServices(IServiceCollection services)
        {
            // Keep the production hosted-service pipeline while validating dependency lifetimes.
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            });
        }
    }
}
