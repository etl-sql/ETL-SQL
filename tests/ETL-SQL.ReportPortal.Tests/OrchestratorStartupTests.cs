using System;
using System.Collections.Generic;
using ETL_SQL.Orchestrator.Service;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests;

/// <summary>
/// Unit coverage for the startup guard that refuses to run the unauthenticated job API on a
/// network-reachable address.
/// </summary>
public class OrchestratorStartupTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Theory]
    [InlineData("http://0.0.0.0:5001", true)]
    [InlineData("http://+:5001", true)]
    [InlineData("http://*:5001", true)]
    [InlineData("http://[::]:5001", true)]
    [InlineData("http://example.com:5001", true)]
    [InlineData("http://192.168.1.10:5001", true)]
    [InlineData("http://localhost:5001", false)]
    [InlineData("http://127.0.0.1:5001", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("http://[::1]:5001", false)]
    public void IsNonLoopbackBinding_ClassifiesHosts(string url, bool expected)
    {
        Assert.Equal(expected, OrchestratorStartup.IsNonLoopbackBinding(url));
    }

    [Fact]
    public void NoKey_NonLoopbackUrls_Throws()
    {
        var cfg = Config(("Orchestrator:ApiKey", ""), ("urls", "http://0.0.0.0:5001"));
        var ex = Assert.Throws<InvalidOperationException>(() => OrchestratorStartup.ValidateApiKeyBinding(cfg));
        Assert.Contains("Orchestrator:ApiKey", ex.Message);
    }

    [Fact]
    public void NoKey_NonLoopbackKestrelEndpoint_Throws()
    {
        var cfg = Config(
            ("Orchestrator:ApiKey", null),
            ("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5001"));
        Assert.Throws<InvalidOperationException>(() => OrchestratorStartup.ValidateApiKeyBinding(cfg));
    }

    [Fact]
    public void NoKey_LoopbackUrls_DoesNotThrow()
    {
        var cfg = Config(("Orchestrator:ApiKey", ""), ("urls", "http://localhost:5001"));
        OrchestratorStartup.ValidateApiKeyBinding(cfg); // must not throw
    }

    [Fact]
    public void NoKey_NoConfiguredUrls_DoesNotThrow()
    {
        // No explicit binding → host default is loopback, which is safe without a key.
        var cfg = Config(("Orchestrator:ApiKey", ""));
        OrchestratorStartup.ValidateApiKeyBinding(cfg);
    }

    [Fact]
    public void KeyConfigured_NonLoopback_DoesNotThrow()
    {
        var cfg = Config(("Orchestrator:ApiKey", "a-real-key"), ("urls", "http://0.0.0.0:5001"));
        OrchestratorStartup.ValidateApiKeyBinding(cfg);
    }
}
