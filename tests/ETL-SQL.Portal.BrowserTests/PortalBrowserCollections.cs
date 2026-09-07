namespace ETL_SQL.Portal.BrowserTests;

// The browser lane's Portals.
//
// One shared Portal made a single startup failure the whole assembly's verdict: 178 of 231 tests
// reporting "The server has not been started", naming none of the real conditions and pointing at
// the fixture rather than at whatever actually went wrong. That message has already sent more than
// one investigation down the wrong path — see docs/releases/flaky-test-stability.md.
//
// Each group below gets its own Portal, and therefore its own SQLite databases, script root,
// seeded administrator and forced first-run password change. Two consequences, both wanted:
//
//   - A host that fails to start fails one group, not the assembly. The blast radius is the size
//     of the group, and the surviving groups still report on the product.
//   - No group can depend on state another group seeded, because there is nowhere shared to seed
//     it. Order-dependence between classes stops being possible rather than being discouraged.
//
// This is affordable because `PortalWebFactory` already gives every instance its own
// `portal_test_<guid>` temp directory, so two live hosts share no files, and because a Portal plus
// a Chromium costs a couple of seconds to start. Grouping rather than one-Portal-per-class is the
// balance: it keeps the worst case to one class's tests where that class is big enough to matter
// (SandboxStoryTests), without paying twenty host starts for classes that hold one test each.
//
// xunit keys a collection fixture by its exact type, so each group needs its own subclass and each
// test class asks for that subclass by name. The subclasses add no behaviour on purpose — all of it
// belongs in PortalBrowserFixture, where it stays shared.

/// <summary>Portal for the Studio author journeys — the end-to-end runs that drive the UI.</summary>
public sealed class StudioAuthoringFixture : PortalBrowserFixture;

/// <summary>Portal for the Studio surface and contract checks that are not full journeys.</summary>
public sealed class StudioSurfaceFixture : PortalBrowserFixture;

/// <summary>Portal for the Portal's own journeys: sign-in, roles, accessibility, surface snapshots.</summary>
public sealed class PortalJourneyFixture : PortalBrowserFixture;

/// <summary>Portal for the administrative and dashboard surfaces.</summary>
public sealed class PortalAdminFixture : PortalBrowserFixture;

/// <summary>Portal for the sandbox stories, which are numerous enough to be worth their own.</summary>
public sealed class SandboxStoryFixture : PortalBrowserFixture;

[CollectionDefinition(StudioAuthoringCollection.Name)]
public sealed class StudioAuthoringCollection : ICollectionFixture<StudioAuthoringFixture>
{
    public const string Name = "studio-authoring";
}

[CollectionDefinition(StudioSurfaceCollection.Name)]
public sealed class StudioSurfaceCollection : ICollectionFixture<StudioSurfaceFixture>
{
    public const string Name = "studio-surfaces";
}

[CollectionDefinition(PortalJourneyCollection.Name)]
public sealed class PortalJourneyCollection : ICollectionFixture<PortalJourneyFixture>
{
    public const string Name = "portal-journeys";
}

[CollectionDefinition(PortalAdminCollection.Name)]
public sealed class PortalAdminCollection : ICollectionFixture<PortalAdminFixture>
{
    public const string Name = "portal-admin";
}

[CollectionDefinition(SandboxStoryCollection.Name)]
public sealed class SandboxStoryCollection : ICollectionFixture<SandboxStoryFixture>
{
    public const string Name = "sandbox-stories";
}
