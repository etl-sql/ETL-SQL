using Xunit;

// One Portal host and one Chromium instance are shared through the class fixture; running journeys
// concurrently against the same SQLite databases would trade real coverage for flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
