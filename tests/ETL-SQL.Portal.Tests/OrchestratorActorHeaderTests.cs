using ETL_SQL.Orchestrator.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// The actor header names the human behind a proxied action so a privileged Orchestrator operation
/// is not logged as the shared service key.
///
/// <para>The tests that matter here are the negative ones. The header is caller-controlled — anyone
/// who can reach the Orchestrator can set it to anything — so it must remain a label and never an
/// input to an access decision. If it ever gained authority, a shared secret would become an
/// impersonation vector.</para>
/// </summary>
public sealed class OrchestratorActorHeaderTests
{
    private static IConfiguration Config(string? key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orchestrator:ApiKey"] = key })
            .Build();

    [Fact]
    public void ActorHeaderNamesTheHumanWhenSupplied()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Orchestrator-Actor"] = "42:jsmith";

        Assert.Equal("42:jsmith", JobApiEndpoints.RequestActor(ctx));
    }

    [Fact]
    public void AbsentActorFallsBackToTheService()
    {
        Assert.Equal("service", JobApiEndpoints.RequestActor(new DefaultHttpContext()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankActorFallsBackToTheService(string value)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Orchestrator-Actor"] = value;

        Assert.Equal("service", JobApiEndpoints.RequestActor(ctx));
    }

    /// <summary>
    /// The value reaches a log line, and a header is attacker-controlled, so a newline would let a
    /// caller forge an entirely fake log entry.
    /// </summary>
    [Fact]
    public void ControlCharactersCannotForgeALogLine()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Orchestrator-Actor"] = "42:jsmith\nWARN Job deleted by admin";

        var actor = JobApiEndpoints.RequestActor(ctx);

        Assert.DoesNotContain('\n', actor);
        Assert.DoesNotContain('\r', actor);
    }

    [Fact]
    public void AnAbsurdlyLongActorIsTruncated()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Orchestrator-Actor"] = new string('a', 5000);

        Assert.True(JobApiEndpoints.RequestActor(ctx).Length <= 96);
    }

    // ── The header confers nothing ───────────────────────────────────────────────

    /// <summary>
    /// Authorization is decided by the API key alone. Supplying an actor must not make an
    /// unauthenticated call acceptable, however administrative the claimed identity looks.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public void AnActorHeaderNeverSubstitutesForTheApiKey(string? providedKey)
    {
        var configuration = Config("real-key");

        // Whatever the actor claims to be, the key is what decides.
        Assert.False(JobApiEndpoints.ApiKeyAccepted(configuration, providedKey));
    }

    [Fact]
    public void TheApiKeyStillGrantsAccessWithNoActorAtAll()
    {
        Assert.True(JobApiEndpoints.ApiKeyAccepted(Config("real-key"), "real-key"));
    }
}
