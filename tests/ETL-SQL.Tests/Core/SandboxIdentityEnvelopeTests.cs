using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class SandboxIdentityEnvelopeTests
{
    private static SandboxIdentityEnvelope Envelope() => new(1, "tenant-a", "run-a", "attempt-a", "sha256:" + new string('a', 64),
        new ExecutionIdentity { EffectiveUser = "learner", RealUser = "user:learner", TenantId = "tenant-a", IsAdmin = false });

    [Fact]
    public void MatchingTrustedAssignmentPreservesTheNonAdminIdentity()
    {
        var envelope = Envelope();
        var identity = envelope.Validate(envelope.TenantId, envelope.RunId, envelope.AttemptId, envelope.ArtifactHash);
        Assert.False(identity.IsAdmin);
        Assert.Equal("learner", identity.EffectiveUser);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("tenant")]
    [InlineData("identityTenant")]
    [InlineData("run")]
    [InlineData("attempt")]
    [InlineData("hash")]
    [InlineData("principal")]
    public void MismatchedOrMalformedEnvelopeFailsClosed(string field)
    {
        var original = Envelope();
        var envelope = field switch
        {
            "version" => original with { Version = 2 },
            "tenant" => original with { TenantId = "tenant-b" },
            "identityTenant" => original with { Identity = original.Identity with { TenantId = "tenant-b" } },
            "run" => original with { RunId = "run-b" },
            "attempt" => original with { AttemptId = "attempt-b" },
            "hash" => original with { ArtifactHash = "sha256:" + new string('b', 64) },
            _ => original with { Identity = original.Identity with { EffectiveUser = "" } }
        };
        Assert.Throws<UnauthorizedAccessException>(() => envelope.Validate(original.TenantId, original.RunId, original.AttemptId, original.ArtifactHash));
    }
}
