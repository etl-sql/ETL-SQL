using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class ProcessActorTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("DOMAIN\\service-account")]
    public void OsSuppliedUserNameIsUsedVerbatim(string osUserName)
    {
        Assert.Equal(osUserName, ProcessActor.Resolve(osUserName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnnamedAccountIsAttributedRatherThanRejected(string? osUserName)
    {
        // A sandbox runs tenant code as a numeric uid with no passwd entry. Actor is a required
        // audit field, so an unnamed account must still produce a usable, obviously-unattributed
        // identity instead of aborting the run.
        Assert.Equal(ProcessActor.Unmapped, ProcessActor.Resolve(osUserName));
    }

    [Fact]
    public void CurrentActorIsNeverBlank()
    {
        Assert.False(string.IsNullOrWhiteSpace(ProcessActor.Current));
    }
}
