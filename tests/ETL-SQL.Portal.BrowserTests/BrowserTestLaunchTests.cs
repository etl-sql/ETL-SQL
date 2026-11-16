using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
public sealed class BrowserTestLaunchTests
{
    [Fact]
    public async Task Launch_PreservesDriverFeaturesAndAddsWindowsPortMitigation()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var original = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--enable-automation"]
        });
        var originalArguments = await ArgumentsAsync(original);
        var originalFeatures = DisabledFeatures(originalArguments);
        var options = await BrowserTestLaunch.OptionsAsync(playwright);
        // Reveal the effective command line for both arms; the fixture itself needs no automation flag.
        options.Args = (options.Args ?? []).Append("--enable-automation");
        await using var candidate = await playwright.Chromium.LaunchAsync(options);
        var candidateArguments = await ArgumentsAsync(candidate);
        var candidateFeatures = DisabledFeatures(candidateArguments);
        Assert.All(originalFeatures, feature => Assert.Contains(feature, candidateFeatures));
        if (OperatingSystem.IsWindows())
            Assert.Equal(1, candidateFeatures.Count(feature => feature == "TcpPortRandomizationWin"));
        else
            Assert.Equal(originalFeatures.Order(), candidateFeatures.Order());
        static IEnumerable<string> OtherArguments(IEnumerable<string> arguments) => arguments
            .Where(argument => !argument.StartsWith("--disable-features=", StringComparison.Ordinal)
                && !argument.StartsWith("--user-data-dir=", StringComparison.Ordinal)).Order();
        Assert.Equal(OtherArguments(originalArguments), OtherArguments(candidateArguments));
    }

    private static string[] DisabledFeatures(IEnumerable<string> arguments)
        => Assert.Single(arguments, value => value.StartsWith("--disable-features=", StringComparison.Ordinal))
            ["--disable-features=".Length..].Split(',');

    private static async Task<string[]> ArgumentsAsync(IBrowser browser)
    {
        var session = await browser.NewBrowserCDPSessionAsync();
        try
        {
            var response = await session.SendAsync("Browser.getBrowserCommandLine");
            return response!.Value.GetProperty("arguments").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
        }
        finally { await session.DetachAsync(); }
    }
}
