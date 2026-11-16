using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

internal static class BrowserTestLaunch
{
    private const string FeatureSwitch = "--disable-features=";
    private const string PortFeature = "TcpPortRandomizationWin";
    private static readonly SemaphoreSlim ProbeGate = new(1, 1);
    private static string? originalDisabledFeatures;

    internal static async Task<IBrowser> LaunchAsync(IPlaywright playwright)
        => await playwright.Chromium.LaunchAsync(await OptionsAsync(playwright));

    internal static async Task<BrowserTypeLaunchOptions> OptionsAsync(IPlaywright playwright)
    {
        var options = new BrowserTypeLaunchOptions { Headless = true };
        if (!OperatingSystem.IsWindows()) return options;

        await ProbeGate.WaitAsync();
        try
        {
            if (originalDisabledFeatures is null)
            {
                // Read the pinned driver's effective defaults once. A second --disable-features
                // argument replaces those defaults in Chromium instead of extending them.
                await using var probe = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args = ["--enable-automation"]
                });
                var session = await probe.NewBrowserCDPSessionAsync();
                try
                {
                    var response = await session.SendAsync("Browser.getBrowserCommandLine");
                    var switches = response!.Value.GetProperty("arguments").EnumerateArray()
                        .Select(argument => argument.GetString()!)
                        .Where(argument => argument.StartsWith(FeatureSwitch, StringComparison.Ordinal)).ToArray();
                    if (switches.Length != 1 || switches[0].Length == FeatureSwitch.Length)
                        throw new InvalidOperationException("Cannot preserve the pinned Chromium disabled-feature defaults.");
                    originalDisabledFeatures = switches[0];
                }
                finally { await session.DetachAsync(); }
            }

            // Backport Playwright #43605 for test-owned Windows browsers. Remove when the pinned
            // driver includes the upstream mitigation. No request retry or OS setting changes.
            if (!originalDisabledFeatures[FeatureSwitch.Length..].Split(',').Contains(PortFeature, StringComparer.Ordinal))
            {
                options.IgnoreDefaultArgs = [originalDisabledFeatures];
                options.Args = [$"{originalDisabledFeatures},{PortFeature}"];
            }
            return options;
        }
        finally { ProbeGate.Release(); }
    }
}
