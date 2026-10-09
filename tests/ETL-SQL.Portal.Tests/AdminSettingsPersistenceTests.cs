using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class AdminSettingsPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"etlsql_settings_{Guid.NewGuid():N}");
    private readonly OrchestratorApiKeyProtector protector = new(new EphemeralDataProtectionProvider());

    public AdminSettingsPersistenceTests() => Directory.CreateDirectory(directory);

    private PortalConfig Config()
    {
        var config = new PortalConfig { DatabasePath = Path.Combine(directory, "portal.db") };
        config.Orchestrator.ApiUrl = "https://configured.example.test";
        config.Orchestrator.ApiKey = "configured-fixture-key";
        return config;
    }

    private async Task<OrchestratorSettingsService> OrchestratorAsync()
    {
        var settings = new OrchestratorSettingsService(Config(), protector);
        await settings.InitializeAsync();
        return settings;
    }

    private async Task<PortalBrandingSettingsService> BrandingAsync()
    {
        var settings = new PortalBrandingSettingsService(Config(), NullLogger<PortalBrandingSettingsService>.Instance);
        await settings.InitializeAsync();
        return settings;
    }

    [Fact]
    public async Task NullKeyKeepsTheConfiguredKeyWhenChangingTheUrl()
    {
        var settings = await OrchestratorAsync();
        await settings.UpdateAsync("https://updated.example.test", null);
        Assert.Equal("configured-fixture-key", settings.ApiKey);
        Assert.Equal("https://updated.example.test", settings.ApiUrl);
        Assert.Equal(settings.ApiKey, (await OrchestratorAsync()).ApiKey);
    }

    [Fact]
    public async Task ExplicitClearOverridesConfigurationAfterRestart()
    {
        var settings = await OrchestratorAsync();
        await settings.UpdateAsync(null, "");
        Assert.Null(settings.ApiKey);
        Assert.Null(settings.ApiUrl);
        var restarted = await OrchestratorAsync();
        Assert.Null(restarted.ApiKey);
        Assert.Null(restarted.ApiUrl);
    }

    [Fact]
    public async Task FailedConnectionPersistenceDoesNotPublishNewRuntimeSettings()
    {
        var settings = await OrchestratorAsync();
        Directory.CreateDirectory(Path.Combine(directory, "portal-orchestrator.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.UpdateAsync("https://updated.example.test", "updated-fixture-key"));
        Assert.Equal("https://configured.example.test", settings.ApiUrl);
        Assert.Equal("configured-fixture-key", settings.ApiKey);
    }

    [Fact]
    public async Task InvalidLogoDoesNotPartiallyChangeBranding()
    {
        var branding = await BrandingAsync();
        await branding.UpdateAsync("Original", "Original footer", "/original.svg");
        await Assert.ThrowsAsync<ArgumentException>(() => branding.UpdateAsync("Changed", "Changed footer", "javascript:invalid"));
        Assert.Equal("Original", branding.DisplayName);
        Assert.Equal("Original footer", branding.FooterText);
        Assert.Equal("/original.svg", branding.LogoUrl);
        Assert.Equal(branding.ToDto(), (await BrandingAsync()).ToDto());
    }

    [Fact]
    public async Task FailedBrandingPersistenceDoesNotPublishNewRuntimeSettings()
    {
        var branding = await BrandingAsync();
        Directory.CreateDirectory(Path.Combine(directory, "portal-branding.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => branding.UpdateAsync("Changed", "Changed footer", "/changed.svg"));
        Assert.Null(branding.DisplayName);
        Assert.Null(branding.FooterText);
        Assert.Equal("/img/logo.png", branding.LogoUrl);
    }

    [Fact]
    public async Task RequestCapturesTheUrlAndKeyTogetherAcrossASettingsChange()
    {
        var settings = await OrchestratorAsync();
        using var captured = settings.CreateRequest(HttpMethod.Get, "health");
        await settings.UpdateAsync("https://updated.example.test", "updated-fixture-key");
        using var current = settings.CreateRequest(HttpMethod.Get, "health");
        Assert.NotNull(captured);
        Assert.NotNull(current);
        Assert.Equal("https://configured.example.test/health", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("configured-fixture-key", Assert.Single(captured.Headers.GetValues("X-Orchestrator-Key")));
        Assert.Equal("https://updated.example.test/health", current.RequestUri!.AbsoluteUri);
        Assert.Equal("updated-fixture-key", Assert.Single(current.Headers.GetValues("X-Orchestrator-Key")));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
