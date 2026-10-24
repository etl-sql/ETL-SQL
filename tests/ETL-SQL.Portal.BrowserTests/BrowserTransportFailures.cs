using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

internal sealed record BrowserTransportFailure(
    DateTimeOffset AtUtc,
    string Method,
    string ResourceType,
    bool IsNavigation,
    string Url,
    string FailureCode);

internal sealed record BrowserTransportFailureHistory(
    IReadOnlyList<BrowserTransportFailure> Requests,
    int Dropped,
    bool NavigationFailed);

// HTTP error responses retain their existing, separate BrowserSession assertions.
internal sealed partial class BrowserTransportFailures : IDisposable
{
    private const int MaximumRecords = 100;
    private readonly IBrowserContext context;
    private readonly object gate = new();
    private readonly Queue<BrowserTransportFailure> requests = new();
    private int dropped;
    private bool navigationFailed;

    internal BrowserTransportFailures(IBrowserContext context)
    {
        this.context = context;
        // Context-level recording also covers extra pages opened by a journey.
        context.RequestFailed += OnRequestFailed;
    }

    private void OnRequestFailed(object? sender, IRequest request)
        => Record(request.Method, request.ResourceType, request.IsNavigationRequest,
            request.Url, request.Failure);

    internal void Record(string method, string resourceType, bool isNavigation, string url, string? failure)
    {
        // Failure text is provider-controlled. Preserve only the Chromium error code, never a
        // raw exception/message that might repeat a URL, credentials or request contents.
        var match = FailureCodePattern().Match(failure ?? string.Empty);
        var record = new BrowserTransportFailure(DateTimeOffset.UtcNow,
            Limit(method, 24), Limit(resourceType, 32), isNavigation,
            SanitizeUrl(url), match.Success ? match.Value : "unavailable");
        lock (gate)
        {
            navigationFailed |= isNavigation;
            if (requests.Count == MaximumRecords)
            {
                requests.Dequeue();
                if (dropped < int.MaxValue) dropped++;
            }
            requests.Enqueue(record);
        }
    }

    internal BrowserTransportFailureHistory Snapshot()
    {
        lock (gate)
            return new(requests.ToArray(), dropped, navigationFailed);
    }

    internal static string SanitizeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "[invalid-url]";
        if (uri.Scheme is not ("http" or "https")) return "[non-http-url]";
        var sanitized = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return Limit(sanitized.Uri.GetLeftPart(UriPartial.Path), 2048);
    }

    private static string Limit(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum];

    public void Dispose() => context.RequestFailed -= OnRequestFailed;

    [GeneratedRegex(@"\bnet::ERR_[A-Z0-9_]{1,80}\b", RegexOptions.CultureInvariant)]
    private static partial Regex FailureCodePattern();
}
