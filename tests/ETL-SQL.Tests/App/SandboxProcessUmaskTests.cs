using System;
using System.Collections.Generic;
using ETL_SQL.App;
using Xunit;

namespace ETL_SQL.Tests.App;

public class SandboxProcessUmaskTests
{
    private static (uint? Applied, List<uint> Calls) Run(string? value)
    {
        var calls = new List<uint>();
        var applied = SandboxProcessUmask.Apply(
            name => name == SandboxProcessUmask.Variable ? value : null,
            mask => { calls.Add(mask); return 0; });
        return (applied, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Unset_LeavesTheInheritedMask(string? value)
    {
        var (applied, calls) = Run(value);
        Assert.Null(applied);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("0000", 0u)]
    [InlineData("000", 0u)]
    [InlineData("022", 18u)]
    [InlineData(" 0777 ", 511u)]
    public void OctalValue_IsApplied(string value, uint expected)
    {
        var (applied, calls) = Run(value);
        Assert.Equal(expected, applied);
        Assert.Equal([expected], calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0o22")]
    [InlineData("8")]
    [InlineData("01000")]
    [InlineData("-1")]
    public void InvalidValue_FailsTheWorker(string value)
    {
        var calls = new List<uint>();
        var ex = Assert.Throws<InvalidOperationException>(() => SandboxProcessUmask.Apply(
            _ => value, mask => { calls.Add(mask); return 0; }));
        Assert.Contains(SandboxProcessUmask.Variable, ex.Message);
        Assert.Empty(calls);
    }
}
