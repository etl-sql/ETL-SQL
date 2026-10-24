using ETL_SQL.Portal.Services;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(AdminUsersCollection.Name)]
public sealed class AdminSettingsPageTests(AdminUsersFixture fixture)
{
    [Fact]
    public async Task SettingsAndBrandingPersistThroughTheNativePage()
    {
        var settings = fixture.Factory.BrowserServices.GetRequiredService<OrchestratorSettingsService>();
        var branding = fixture.Factory.BrowserServices.GetRequiredService<PortalBrandingSettingsService>();
        var originalUrl = settings.ApiUrl;
        var originalKey = settings.ApiKey;
        var originalBranding = branding.ToDto();
        try
        {
            await settings.UpdateAsync(null, "native-fixture-key");
            await using var session = await fixture.NewSessionAsync();
            var page = session.Page;
            await fixture.SignInAsync(page);
            await page.GotoWithDiagnosticsAsync("/admin.html");
            await page.Locator(".admin-tab[data-tab='settings']").ClickAsync();
            await Expect(page.Locator("#orch-key-status")).ToContainTextAsync("key is set");
            await page.Locator("#orch-url").FillAsync("https://native.example.test");
            await page.Locator("#orchSaveBtn").ClickAsync();
            await Expect(page.Locator("#orch-save-result")).ToHaveTextAsync("Saved.");
            Assert.Equal("native-fixture-key", settings.ApiKey);
            Assert.Equal("https://native.example.test", settings.ApiUrl);
            await page.Locator("#orch-clear-check").CheckAsync();
            await page.Locator("#orchSaveBtn").ClickAsync();
            await Expect(page.Locator("#orch-key-status")).ToContainTextAsync("not set");
            Assert.Null(settings.ApiKey);
            await page.Locator("#orch-url").FillAsync("");
            await page.Locator("#orchSaveBtn").ClickAsync();
            await Expect(page.Locator("#orch-save-result")).ToHaveTextAsync("Saved.");
            await page.Locator("#orchTestBtn").ClickAsync();
            await Expect(page.Locator("#orch-test-result")).ToContainTextAsync("Offline");
            await page.Locator("#brand-name").FillAsync("Native settings portal");
            await page.Locator("#brand-footer").FillAsync("Native persisted footer");
            await page.Locator("#brandSaveBtn").ClickAsync();
            await Expect(page.Locator("#brand-save-result")).ToHaveTextAsync("Saved.");
            await Expect(page.Locator(".portal-brand-name")).ToHaveTextAsync("Native settings portal");
            Assert.Equal("Native settings portal", branding.DisplayName);
            Assert.Equal("Native persisted footer", branding.FooterText);
            await page.ReloadAsync();
            await page.Locator(".admin-tab[data-tab='settings']").ClickAsync();
            await Expect(page.Locator("#brand-name")).ToHaveValueAsync("Native settings portal");
            await Expect(page.Locator("#brand-footer")).ToHaveValueAsync("Native persisted footer");
            await Expect(page.Locator("#orch-key-status")).ToContainTextAsync("not set");
            Assert.Empty(session.PageErrors);
        }
        finally
        {
            await settings.UpdateAsync(originalUrl, originalKey ?? "");
            await branding.UpdateAsync(originalBranding.DisplayName, originalBranding.FooterText, originalBranding.LogoUrl);
        }
    }
}
