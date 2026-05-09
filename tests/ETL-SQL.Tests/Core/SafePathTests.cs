using System.IO;

namespace ETL_SQL.Tests.Core;

public class SafePathTests
{
    [Fact]
    [Trait("Category", "Smoke.Security")]
    public void TryResolveUnderRoot_AllowsRootedChildPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"safe_root_{Guid.NewGuid():N}");
        var child = Path.Combine(root, "reports", "daily.rptsql");

        var allowed = SafePath.TryResolveWithinRoot(root, child, out var resolved);

        Assert.True(allowed);
        Assert.Equal(Path.GetFullPath(child), resolved);
    }

    [Fact]
    [Trait("Category", "Smoke.Security")]
    public void TryResolveUnderRoot_RejectsSiblingPrefixBypass()
    {
        var temp = Path.GetTempPath();
        var root = Path.Combine(temp, $"safe_root_{Guid.NewGuid():N}");
        var sibling = root + "2";
        var child = Path.Combine(sibling, "outside.rptsql");

        var allowed = SafePath.TryResolveWithinRoot(root, child, out _);

        Assert.False(allowed);
    }
}
