using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Every API path the browser asks for must resolve to a route the Portal actually serves.
///
/// <para>This exists because of how that fails, rather than because of any one bug. The client
/// wraps its calls so a rejected request becomes a caught error, and a caught error renders as
/// "nothing to show" or "temporarily unavailable" — states that read as ordinary quiet rather than
/// as a broken call. A renamed or mistyped route therefore produces no symptom a reviewer would
/// notice, and no symptom a user would report as anything but an empty page.</para>
///
/// <para><b>Scope, stated plainly:</b> this catches a path with no matching route. It would
/// <em>not</em> have caught the quarantine queue polling <c>GET /api/jobs/{id}</c> with an id from
/// a different job namespace, because that route exists — it just never held those ids. Existence
/// is the part a machine can check. Whether the caller is authorized, and whether the response is
/// what the caller expects, are separate questions answered by <c>AuthorizationMatrixTests</c> and
/// <c>BrowserApiContractTests</c>.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class BrowserRouteReachabilityTests
{
    /// <summary>
    /// Paths that are deliberately not Portal routes. Each needs a reason: an entry here is a
    /// claim that a 404 from this path is expected and handled, which is exactly the belief that
    /// was wrong about <c>/api/jobs/{id}</c>.
    /// </summary>
    private static readonly Dictionary<string, string> KnownNonPortalPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // None at present. Add with the reason the caller tolerates a miss.
    };

    [Fact]
    public void EveryApiPathTheBrowserCalls_ResolvesToARoute()
    {
        using var factory = new HostedPortalFactory();
        _ = factory.Services; // force the host to build so endpoints are populated

        var routes = ServerRoutePatterns(factory);
        Assert.NotEmpty(routes);

        var called = ClientApiPaths();
        Assert.NotEmpty(called);

        var unreachable = called
            .Where(call => !KnownNonPortalPaths.ContainsKey(call.Path))
            .Where(call => !routes.Any(route => Matches(route, call.Path)))
            .ToList();

        Assert.True(unreachable.Count == 0,
            $"{unreachable.Count} API path(s) the browser calls have no matching Portal route:\n  "
            + string.Join("\n  ", unreachable.Select(c => $"{c.Path}   (from {c.Source})"))
            + "\n\nThe client turns a rejected request into a caught error, which renders as "
            + "'nothing to show' or 'temporarily unavailable'. A missing route therefore looks "
            + "like quiet rather than breakage.");
    }

    /// <summary>Every route pattern the built host serves, normalised to literal/wildcard segments.</summary>
    private static List<string[]> ServerRoutePatterns(HostedPortalFactory factory)
    {
        var sources = factory.Services.GetServices<EndpointDataSource>();
        var patterns = new List<string[]>();

        foreach (var endpoint in sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var raw = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!raw.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
                && !raw.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                continue;

            patterns.Add(Segments(raw));
        }

        return patterns;
    }

    /// <summary>Every distinct <c>/api/...</c> literal in the Portal's own browser JavaScript.</summary>
    private static List<(string Path, string Source)> ClientApiPaths()
    {
        var jsRoot = Path.Combine(RepoRoot(), "src", "ETL-SQL.Portal", "wwwroot", "js");
        var calls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(jsRoot, "*.js"))
        {
            var name = Path.GetFileName(file);
            // Vendored libraries are not ours and contain unrelated string literals.
            if (name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"[`'""](?<path>/api/[^`'""\s]*)[`'""]"))
            {
                var path = Normalise(match.Groups["path"].Value);
                if (path is null) continue;
                calls.TryAdd(path, name);
            }
        }

        return [.. calls.Select(entry => (entry.Key, entry.Value))];
    }

    /// <summary>
    /// Strips the query string and collapses interpolated segments to a wildcard. A segment that
    /// merely *contains* an interpolation (<c>report-${id}.csv</c>) is still a wildcard: its
    /// literal text is not knowable here.
    /// </summary>
    private static string? Normalise(string raw)
    {
        var path = raw.Split('?')[0].TrimEnd('/');
        if (path.Length <= "/api".Length) return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Contains("${", StringComparison.Ordinal) ? "*" : segment);

        return "/" + string.Join('/', segments);
    }

    private static string[] Segments(string routePattern) =>
        routePattern.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.StartsWith('{') ? "*" : segment)
            .ToArray();

    /// <summary>
    /// A call matches a route when segment counts agree and every literal segment agrees. A
    /// wildcard on either side matches anything: the client cannot know a route constraint, and the
    /// route cannot know what the client interpolated.
    /// </summary>
    private static bool Matches(string[] route, string callPath)
    {
        var call = callPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (route.Length != call.Length) return false;

        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] == "*" || call[i] == "*") continue;
            if (!route[i].Equals(call[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
