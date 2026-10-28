namespace ETL_SQL.Portal.BrowserTests;

// Disposable recovery candidate only. Never merge this injected failure into the product.
[Trait("Category", "Browser")]
public sealed class RehearsalShardRecoveryTests
{
    [Fact]
    public void FirstHostedAttempt_FailsOneShardForRecoveryRehearsal()
    {
        var injectedFailure = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            && Environment.GetEnvironmentVariable("GITHUB_HEAD_REF") == "rehearsal/portal-shard-recovery"
            && Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") == "1";

        Assert.False(injectedFailure,
            "Deliberate attempt-1 failure. Preserve all evidence and rerun failed jobs only on this unchanged candidate.");
    }
}
