using System.Net;
using System.Net.Http.Headers;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The Portal's cache policy, split by what a response actually is.
///
/// <para>Everything used to be <c>no-store</c>, which is right for documents and API responses and
/// wrong for the Portal's own assets: it meant every page navigation re-downloaded roughly 3.4 MB,
/// about 1.9 MB of it vendored libraries that had not changed since install. It also made the
/// hand-maintained <c>?v=</c> strings on those URLs inert — a browser forbidden to store a response
/// cannot serve a stale one, so there was nothing for a cache-buster to bust.</para>
///
/// <para>Both halves are pinned here because each is a mistake someone could make in good faith:
/// widening <c>no-store</c> back over the assets to be safe, or relaxing the documents to make the
/// app feel faster.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class StaticAssetCachingTests
{
    [Theory]
    [InlineData("/js/api.js")]
    [InlineData("/css/portal.css")]
    public async Task StaticAssets_AreRevalidated_RatherThanRefetchedWhole(string path)
    {
        using var factory = new HostedPortalFactory();
        using var client = factory.CreateClient();

        var first = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var cacheControl = first.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.NoCache, $"{path} must require revalidation.");
        Assert.False(cacheControl.NoStore,
            $"{path} is served no-store, so the browser refetches the whole file on every page "
            + "load. These are the Portal's own scripts and stylesheets and carry no user data.");

        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        // The point of allowing storage: a revalidation costs a 304, not the file.
        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
        conditional.Headers.IfNoneMatch.Add(etag!);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    /// <summary>
    /// Documents and API responses carry catalog contents, identity and report data. They must not
    /// be stored by a browser or an intermediary, however convenient that would be.
    /// </summary>
    [Theory]
    [InlineData("/index.html")]
    [InlineData("/login.html")]
    [InlineData("/api/branding")]
    public async Task DocumentsAndApiResponses_AreNotStored(string path)
    {
        using var factory = new HostedPortalFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);
        var cacheControl = response.Headers.CacheControl;

        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.NoStore,
            $"{path} must be no-store; it can carry catalog, identity or report data.");
    }
}
