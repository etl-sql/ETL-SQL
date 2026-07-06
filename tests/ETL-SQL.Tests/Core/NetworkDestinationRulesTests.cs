using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class NetworkDestinationRulesTests
{
    [Theory]
    [InlineData("2130706433", "127.0.0.1")]        // 32-bit decimal
    [InlineData("0x7f000001", "127.0.0.1")]         // 32-bit hex
    [InlineData("0x7f.0.0.1", "127.0.0.1")]         // dotted hex octet
    [InlineData("0177.0.0.1", "127.0.0.1")]         // dotted octal octet
    [InlineData("[::1]", "::1")]                     // bracketed IPv6
    [InlineData("::ffff:127.0.0.1", "127.0.0.1")]   // IPv4-mapped IPv6
    [InlineData("192.168.001.001", "192.168.1.1")]  // leading-zero octets collapse
    public void Normalize_CanonicalizesObfuscatedLiterals(string input, string expected)
    {
        Assert.Equal(expected, NetworkDestinationRules.Normalize(input));
    }

    [Fact]
    public void Normalize_LeavesDnsNamesUnchanged()
    {
        Assert.Equal("db.corp.internal", NetworkDestinationRules.Normalize("db.corp.internal"));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.5.5")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.10")]
    [InlineData("169.254.169.254")]   // cloud metadata endpoint
    [InlineData("100.64.0.1")]        // carrier-grade NAT
    [InlineData("2130706433")]        // obfuscated loopback
    [InlineData("::1")]
    [InlineData("fe80::1")]           // IPv6 link-local
    [InlineData("fc00::1")]           // IPv6 unique-local
    public void IsRestrictedRange_DetectsInternalAddresses(string host)
    {
        Assert.True(NetworkDestinationRules.IsRestrictedRange(host));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.5")]
    [InlineData("db.corp.internal")]  // DNS name is not a literal range
    [InlineData("172.32.0.1")]        // just outside 172.16/12
    [InlineData("100.128.0.1")]       // just outside 100.64/10
    public void IsRestrictedRange_AllowsPublicAndNames(string host)
    {
        Assert.False(NetworkDestinationRules.IsRestrictedRange(host));
    }
}
