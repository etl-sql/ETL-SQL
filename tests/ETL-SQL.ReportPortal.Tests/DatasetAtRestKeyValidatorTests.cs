using System;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Services;
using Xunit;
using Sev = ETL_SQL.ReportPortal.Services.DatasetAtRestKeyValidator.Severity;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// 2i: the portal must fail closed when the at-rest key is missing/weak, unless an operator opts into
/// the dev/standalone MACHINE fallback. These cover the pure validation rules.
/// </summary>
public class DatasetAtRestKeyValidatorTests
{
    private static Sev Validate(string? key, bool allowFallback) =>
        DatasetAtRestKeyValidator.Validate(new DatasetConfig { AtRestKey = key, AllowMachineFallback = allowFallback }).Severity;

    [Fact]
    public void ValidBase64Key_AtLeast32Bytes_IsOk()
    {
        var key = Convert.ToBase64String(new byte[32]);   // 256-bit
        Assert.Equal(Sev.Ok, Validate(key, allowFallback: false));
    }

    [Fact]
    public void EmptyKey_WithFallback_IsWarn()
    {
        Assert.Equal(Sev.Warn, Validate("", allowFallback: true));
        Assert.Equal(Sev.Warn, Validate(null, allowFallback: true));
    }

    [Fact]
    public void EmptyKey_WithoutFallback_IsFatal()
    {
        Assert.Equal(Sev.Fatal, Validate("", allowFallback: false));
        Assert.Equal(Sev.Fatal, Validate(null, allowFallback: false));
    }

    [Fact]
    public void NonBase64Key_IsFatal()
    {
        Assert.Equal(Sev.Fatal, Validate("not valid base64 !!!", allowFallback: false));
    }

    [Fact]
    public void Base64Key_TooShort_IsFatal()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);   // only 128-bit
        Assert.Equal(Sev.Fatal, Validate(shortKey, allowFallback: false));
    }
}
