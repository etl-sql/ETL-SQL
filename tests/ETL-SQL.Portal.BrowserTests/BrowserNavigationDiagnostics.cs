using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

// A context owns its recorder so popups and additional journey pages share the same history.
internal sealed class BrowserNavigationDiagnostics : IAsyncDisposable
{
    private static readonly ConditionalWeakTable<IBrowserContext, BrowserNavigationDiagnostics> Recorders = new();
    private readonly IBrowserContext context;
    private readonly BrowserTransportFailures failures;
    private readonly string directory;
    private readonly List<Task> pending = [];
    private readonly object gate = new();
    private bool disposed;
    private readonly Func<Task<IReadOnlyList<BrowserProcessIdentity>>> identifyProcesses;

    internal BrowserNavigationDiagnostics(IBrowserContext context, string? directory = null,
        Func<Task<IReadOnlyList<BrowserProcessIdentity>>>? identifyProcesses = null)
    {
        this.context = context;
        this.directory = directory ?? Environment.GetEnvironmentVariable("ETLSQL_BROWSER_DIAGNOSTICS_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "navigation-diagnostics");
        this.identifyProcesses = identifyProcesses ?? IdentifyProcessesAsync;
        failures = new(context);
        Recorders.Add(context, this);
        context.RequestFailed += OnRequestFailed;
    }

    internal static Task CaptureAsync(IPage page, string trigger, string caller, string targetUrl)
        => Recorders.TryGetValue(page.Context, out var recorder)
            ? recorder.CaptureAsync(trigger, page.Url, caller, targetUrl) : Task.CompletedTask;

    private void OnRequestFailed(object? sender, IRequest request)
    {
        if (request.IsNavigationRequest) _ = CaptureAsync("transport-navigation", request.Url);
    }

    internal Task CaptureAsync(string trigger, string url, string? caller = null, string? targetUrl = null)
    {
        lock (gate)
        {
            // Preserve first failures and bound diagnostic work even during a cascade.
            if (disposed || pending.Count >= 8) return Task.CompletedTask;
            var task = CaptureCoreAsync(trigger, url, caller, targetUrl);
            pending.Add(task);
            return task;
        }
    }

    private async Task<IReadOnlyList<BrowserProcessIdentity>> IdentifyProcessesAsync()
    {
        var browser = context.Browser ?? throw new InvalidOperationException("Browser is unavailable.");
        var session = await browser.NewBrowserCDPSessionAsync().WaitAsync(TimeSpan.FromSeconds(1));
        try
        {
            var response = await session.SendAsync("SystemInfo.getProcessInfo").WaitAsync(TimeSpan.FromSeconds(1));
            var result = new List<BrowserProcessIdentity> { new(Environment.ProcessId, "test-host") };
            foreach (var process in response!.Value.GetProperty("processInfo").EnumerateArray().Take(64))
            {
                var number = process.GetProperty("id").GetDouble();
                if (number < 1 || number > int.MaxValue || number != Math.Truncate(number)) continue;
                var role = process.GetProperty("type").GetString();
                // CDP process types are labels, never command lines or process arguments.
                role = role is { Length: > 0 and <= 80 } && role.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
                    ? role : "chromium-other";
                result.Add(new((int)number, role));
            }
            return result;
        }
        finally
        {
            await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    private async Task CaptureCoreAsync(string trigger, string url, string? caller, string? targetUrl)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        string? temporary = null;
        try
        {
            var unavailable = new List<string>();
            IReadOnlyList<BrowserProcessIdentity> identities = [new(Environment.ProcessId, "test-host")];
            try { identities = await identifyProcesses().WaitAsync(TimeSpan.FromSeconds(2), deadline.Token); }
            catch (Exception exception) { unavailable.Add($"browser-processes: {exception.GetType().Name}"); }
            if (identities.Count > 64) unavailable.Add("browser-processes: truncated-to-63-plus-test-host");
            IReadOnlyList<BrowserProcessResourceSnapshot> processes = [];
            try
            {
                processes = await Task.Run(() => BrowserProcessResources.Capture(identities), deadline.Token)
                    .WaitAsync(TimeSpan.FromSeconds(1), deadline.Token);
            }
            catch (Exception exception) { unavailable.Add($"process-resources: {exception.GetType().Name}"); }
            var snapshot = new
            {
                schemaVersion = 1,
                atUtc = DateTimeOffset.UtcNow,
                trigger,
                caller,
                url = BrowserTransportFailures.SanitizeUrl(url),
                targetUrl = targetUrl is null ? null : BrowserTransportFailures.SanitizeUrl(targetUrl),
                requests = failures.Snapshot(),
                processes,
                unavailable,
                identity = new
                {
                    candidate = SafeIdentity("GITHUB_SHA"),
                    run = SafeIdentity("GITHUB_RUN_ID"),
                    attempt = SafeIdentity("GITHUB_RUN_ATTEMPT"),
                    shard = SafeIdentity("ETLSQL_BROWSER_SHARD")
                }
            };
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory,
                $"navigation-{Environment.ProcessId}-{DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.json");
            temporary = destination + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
            File.Move(temporary, destination);
        }
        catch (Exception)
        {
            // Diagnostic collection must preserve the original navigation failure, including
            // when the browser has exited or the artifact directory cannot be written.
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception) { /* Best-effort cleanup of this snapshot's temporary file. */ }
            }
        }
    }

    private static string? SafeIdentity(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is { Length: > 0 and <= 64 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? value : null;
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (gate)
        {
            disposed = true;
            tasks = pending.ToArray();
        }
        context.RequestFailed -= OnRequestFailed;
        failures.Dispose();
        Recorders.Remove(context);
        await Task.WhenAll(tasks);
    }
}

internal static class BrowserNavigationExtensions
{
    internal static async Task<IResponse?> GotoWithDiagnosticsAsync(this IPage page, string url, PageGotoOptions? options = null,
        [CallerMemberName] string caller = "")
    {
        try { return await page.GotoAsync(url, options); }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            await BrowserNavigationDiagnostics.CaptureAsync(page, "goto-failure", caller, url);
            throw;
        }
    }

    internal static async Task WaitForURLWithDiagnosticsAsync(this IPage page, string url, PageWaitForURLOptions? options = null,
        [CallerMemberName] string caller = "")
    {
        try { await page.WaitForURLAsync(url, options); }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            await BrowserNavigationDiagnostics.CaptureAsync(page, "url-wait-failure", caller, url);
            throw;
        }
    }
}
