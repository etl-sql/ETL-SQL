using Xunit;

// Collections run one at a time. The lane drives real browsers against real Kestrel hosts, and
// running groups concurrently would multiply the live Chromium and Portal processes at exactly the
// moment memory is already the constraint — the v0.19.0 cascade reproduced under memory pressure.
// Sequential collections also mean at most one group's host is starting at any moment.
//
// The Portals themselves are per group; see PortalBrowserCollections.cs for why and for the
// grouping. DetailSurfaceCollection is separate again, and deliberately has no Portal at all.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
