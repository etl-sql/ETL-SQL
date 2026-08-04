using Xunit;

// One Portal host and one Chromium instance are shared across the whole lane; running journeys
// concurrently against the same SQLite databases would trade real coverage for flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

/// <summary>
/// Every browser test class joins this collection so the lane builds <b>one</b> Portal host and
/// <b>one</b> Chromium, not one per class.
///
/// <para>With a per-class fixture, xunit could construct the next class's Portal before disposing
/// the previous one, leaving two hosts briefly alive in the same process. The second then failed
/// to start and every test in that class reported <c>The server has not been started</c> in about
/// a millisecond. A single shared fixture removes the overlap rather than timing around it — and
/// starting Chromium and the Portal once instead of per class is the cheaper arrangement anyway.</para>
/// </summary>
[CollectionDefinition(PortalBrowserCollection.Name)]
public sealed class PortalBrowserCollection : ICollectionFixture<ETL_SQL.Portal.BrowserTests.PortalBrowserFixture>
{
    public const string Name = "portal-browser";
}
