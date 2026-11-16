using System.Net;
using System.Net.Sockets;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
public sealed class BrowserProcessResourcesTests
{
    [Fact]
    public void Capture_CountsEndpointsOwnedByTheSelectedProcess()
    {
        using var tcpV4 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        tcpV4.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        tcpV4.Listen(1);
        using var udpV4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udpV4.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var snapshot = Assert.Single(BrowserProcessResources.Capture(
            [new(Environment.ProcessId, "test-host")]));

        Assert.Equal(Environment.ProcessId, snapshot.ProcessId);
        Assert.Equal("test-host", snapshot.Role);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(snapshot.HandleCount > 0);
            Assert.True(snapshot.TcpV4Endpoints >= 1);
            Assert.True(snapshot.UdpV4Endpoints >= 1);
            Assert.Empty(snapshot.Unavailable);
        }
        else
        {
            Assert.Null(snapshot.TcpV4Endpoints);
            Assert.Null(snapshot.UdpV4Endpoints);
            Assert.Contains("socket-counts: unsupported-platform", snapshot.Unavailable);
        }
    }

    [Ipv6SocketCountFact]
    public void Capture_CountsIpv6EndpointsWhenAvailable()
    {
        using var tcpV6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        tcpV6.DualMode = false;
        tcpV6.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        tcpV6.Listen(1);
        using var udpV6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        udpV6.DualMode = false;
        udpV6.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));

        var snapshot = Assert.Single(BrowserProcessResources.Capture(
            [new(Environment.ProcessId, "test-host")]));

        Assert.True(snapshot.TcpV6Endpoints >= 1);
        Assert.True(snapshot.UdpV6Endpoints >= 1);
        Assert.Empty(snapshot.Unavailable);
    }

    [Fact]
    public void Capture_AnExitedOrMissingProcess_DoesNotFailDiagnostics()
    {
        var snapshot = Assert.Single(BrowserProcessResources.Capture(
            [new(int.MaxValue, "missing-process")]));

        Assert.Null(snapshot.HandleCount);
        Assert.Contains(snapshot.Unavailable, item => item.StartsWith("handles:", StringComparison.Ordinal));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(0, snapshot.TcpV4Endpoints);
            Assert.Equal(0, snapshot.UdpV4Endpoints);
            Assert.Equal(0, snapshot.TcpV6Endpoints);
            Assert.Equal(0, snapshot.UdpV6Endpoints);
        }
    }
}

/// <summary>
/// Socket counts come from the Windows IP helper tables, and the IPv6 case needs an IPv6 stack. Off
/// Windows or without IPv6 the test shows as skipped instead of passing without asserting anything;
/// the hosted browser lane fails on a skipped result, so it cannot go missing there.
/// </summary>
public sealed class Ipv6SocketCountFactAttribute : FactAttribute
{
    public Ipv6SocketCountFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Socket counts are read only on Windows.";
        else if (!Socket.OSSupportsIPv6) Skip = "This machine has no IPv6 stack.";
    }
}
