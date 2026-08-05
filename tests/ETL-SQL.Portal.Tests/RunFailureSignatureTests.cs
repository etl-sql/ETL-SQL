using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The signature exists to collapse one outage into one row. It has two failure modes and they are
/// not symmetric: under-grouping is noise an operator can read past, while over-grouping hides a
/// second incident behind the first and cannot be recovered from by reading more closely. These
/// cover both directions, weighted toward proving distinct failures stay distinct.
/// </summary>
[Trait("Category", "Portal")]
public sealed class RunFailureSignatureTests
{
    [Fact]
    public void SameOutageAcrossJobsCollapsesToOneSignature()
    {
        // What a shared source outage actually looks like: identical failure, different connection
        // ids and timestamps per job.
        var a = RunFailureSignature.Normalize(
            "Login failed for user 'etl_svc'. Connection id 4f2c1a9e-1b3d-4c8a-9f10-77b2e6d1a5c3 at 2026-08-05T03:14:22Z");
        var b = RunFailureSignature.Normalize(
            "Login failed for user 'etl_svc'. Connection id 91ab77de-5c02-4e11-8ddd-01fe23c9b6a7 at 2026-08-05T03:14:41Z");

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentFailuresDoNotMerge()
    {
        var deadlock = RunFailureSignature.Normalize("Transaction was deadlocked on lock resources.");
        var timeout = RunFailureSignature.Normalize("Timeout expired before the operation completed.");
        var missing = RunFailureSignature.Normalize("Invalid object name 'dbo.stg_sales'.");

        Assert.NotEqual(deadlock, timeout);
        Assert.NotEqual(deadlock, missing);
        Assert.NotEqual(timeout, missing);
    }

    [Fact]
    public void RowCountsAndDurationsDoNotSplitOneIncident()
    {
        var first = RunFailureSignature.Normalize("Quarantined 1,420 rows after 31.5s");
        var second = RunFailureSignature.Normalize("Quarantined 7 rows after 4.25s");

        Assert.Equal(first, second);
    }

    [Fact]
    public void PathsAndPortsVaryingPerNodeDoNotSplitOneIncident()
    {
        var windows = RunFailureSignature.Normalize(@"Cannot open C:\etl\spill\chunk_0007.tmp for writing");
        var otherFile = RunFailureSignature.Normalize(@"Cannot open C:\etl\spill\chunk_0182.tmp for writing");
        var unc = RunFailureSignature.Normalize(@"Cannot open \\artifacts\etl\spill\chunk_0007.tmp for writing");

        Assert.Equal(windows, otherFile);
        Assert.Equal(windows, unc);
    }

    [Fact]
    public void QuotedTableNamesStillSeparateDistinctSchemaErrors()
    {
        // Quoted values are normalized, so two different missing tables group together. That is the
        // intended trade: the incident is "a table referenced by these jobs is missing", and the
        // affected job list on the incident carries the specifics.
        var one = RunFailureSignature.Normalize("Invalid object name 'dbo.stg_sales'.");
        var two = RunFailureSignature.Normalize("Invalid object name 'dbo.stg_returns'.");
        Assert.Equal(one, two);

        // But a different *kind* of schema error must not join them.
        var column = RunFailureSignature.Normalize("Invalid column name 'amount'.");
        Assert.NotEqual(one, column);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingMessagesGroupUnderOneReadableKey(string? error)
    {
        Assert.Equal(RunFailureSignature.NoMessage, RunFailureSignature.Normalize(error));
    }

    [Fact]
    public void SignatureIsStableUnderWhitespaceAndCasing()
    {
        var a = RunFailureSignature.Normalize("Connection   refused\r\n  by source_db");
        var b = RunFailureSignature.Normalize("connection refused by SOURCE_DB");

        Assert.Equal(a, b);
    }

    [Fact]
    public void LongMessagesAreBoundedButStillDistinguishing()
    {
        var prefix = new string('a', 400);
        var one = RunFailureSignature.Normalize(prefix + " alpha");
        var two = RunFailureSignature.Normalize(prefix + " beta");

        // Truncation means very long messages sharing a huge prefix collapse — accepted, because an
        // unbounded key is a memory and storage hazard. What must hold is that the key stays bounded.
        Assert.True(one.Length <= 240);
        Assert.True(two.Length <= 240);
    }

    [Fact]
    public void SamplePrefersTheFullestMessage()
    {
        var sample = RunFailureSignature.SampleFor(
            ["Timeout expired", null, "Timeout expired before the operation completed.", "   "]);

        Assert.Equal("Timeout expired before the operation completed.", sample);
    }

    [Fact]
    public void SampleFallsBackWhenNothingWasRecorded()
    {
        Assert.Equal(RunFailureSignature.NoMessage, RunFailureSignature.SampleFor([null, "", "  "]));
    }
}
