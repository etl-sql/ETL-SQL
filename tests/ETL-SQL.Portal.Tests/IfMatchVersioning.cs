using System.Text.Json;
using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Optimistic-concurrency support for tests (BREAKING_CHANGES v0.12.0): portal mutations on
/// versioned resources require <c>If-Match</c> with the version from the latest read.
/// <see cref="StampAsync"/> resolves the current version of the resource a mutation URL targets —
/// via the single-resource GET's ETag, its JSON <c>version</c> field, or a parent-list search —
/// and stamps the header. Resolution uses a privileged read token so permission-denial tests
/// still reach the endpoint's own authorization checks (the actor's request is otherwise
/// unchanged). URLs that do not address a versioned resource are left alone, as are requests
/// whose resource cannot be read (e.g. negative tests against missing ids, which must still 404).
/// </summary>
internal static partial class IfMatchVersioning
{
    [GeneratedRegex(@"^(?<r>/api/admin/users/\d+)(/reset-password|/revoke-tokens)?$")]
    private static partial Regex UserRoute();

    [GeneratedRegex(@"^(?<r>/api/admin/groups/\d+)(/members(/bulk-add|/bulk-remove|/\d+)?)?$")]
    private static partial Regex GroupRoute();

    // /acl/{groupId} revokes a group grant; /acl/user/{userId} revokes a direct user grant.
    [GeneratedRegex(@"^(?<r>/api/datasets/\d+)(/acl(/user)?(/\d+)?|/move)?$")]
    private static partial Regex DatasetRoute();

    [GeneratedRegex(@"^(?<r>/api/folders/\d+)(/acl(/\d+)?)?$")]
    private static partial Regex FolderRoute();

    [GeneratedRegex(@"^(?<r>/api/reports/\d+)(/script-content)?$")]
    private static partial Regex ReportRoute();

    [GeneratedRegex(@"^(?<r>/api/subscriptions/\d+)$")]
    private static partial Regex SubscriptionRoute();

    // No SMTP-specific route: shared connections are keyed by alias under the governed
    // connection catalog and are not id-versioned mutations.

    /// <summary>
    /// Resolves the targeted resource's current version and adds <c>If-Match</c> to
    /// <paramref name="request"/>. No-op when the URL is not a versioned mutation or the
    /// resource cannot currently be read.
    /// </summary>
    public static async Task StampAsync(HttpClient client, HttpRequestMessage request, string readToken)
    {
        if (request.Method == HttpMethod.Get || request.Headers.Contains("If-Match"))
            return;

        var path = request.RequestUri?.OriginalString.Split('?')[0] ?? "";
        var resourceUrl = ResolveResourceUrl(path);
        if (resourceUrl is null)
            return;

        var version = await TryReadVersionAsync(client, resourceUrl, readToken)
            ?? await TryReadVersionFromParentListAsync(client, resourceUrl, readToken);
        if (version is not null)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
    }

    private static string? ResolveResourceUrl(string path)
    {
        foreach (var route in (ReadOnlySpan<Regex>)
            [UserRoute(), GroupRoute(), DatasetRoute(), FolderRoute(),
             ReportRoute(), SubscriptionRoute()])
        {
            var match = route.Match(path);
            if (match.Success)
                return match.Groups["r"].Value;
        }

        return null;
    }

    private static async Task<long?> TryReadVersionAsync(HttpClient client, string url, string readToken)
    {
        using var read = new HttpRequestMessage(HttpMethod.Get, url);
        read.Headers.Authorization = new("Bearer", readToken);
        using var response = await client.SendAsync(read);
        if (!response.IsSuccessStatusCode)
            return null;

        var etag = response.Headers.ETag?.Tag?.Trim('"');
        if (long.TryParse(etag, out var fromEtag) && fromEtag > 0)
            return fromEtag;

        try
        {
            var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            return ReadVersionProperty(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Fallback for resources without a single-item GET.</summary>
    private static async Task<long?> TryReadVersionFromParentListAsync(
        HttpClient client, string resourceUrl, string readToken)
    {
        var lastSlash = resourceUrl.LastIndexOf('/');
        if (lastSlash <= 0 || !long.TryParse(resourceUrl[(lastSlash + 1)..], out var id))
            return null;

        using var read = new HttpRequestMessage(HttpMethod.Get, resourceUrl[..lastSlash]);
        read.Headers.Authorization = new("Bearer", readToken);
        using var response = await client.SendAsync(read);
        if (!response.IsSuccessStatusCode)
            return null;

        try
        {
            var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            if (body.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in body.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var itemId)
                    && itemId.ValueKind == JsonValueKind.Number
                    && itemId.GetInt64() == id)
                    return ReadVersionProperty(item);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static long? ReadVersionProperty(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.Number
        ? version.GetInt64()
        : null;
}
