using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App.Admin;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// A command line is visible to every process on the host, lands in shell history, and is captured
/// verbatim by CI logs — so the admin CLI's client secret is only ever taken from the environment or
/// a <c>SECRET:</c> reference. These cover that discipline and the failure messages an operator sees
/// when a runbook is misconfigured, which is when they most need the message to be exact.
/// </summary>
public sealed class PortalAdminCredentialsTests : IDisposable
{
    private readonly string? _url = Environment.GetEnvironmentVariable(PortalAdminCredentials.UrlVariable);
    private readonly string? _id = Environment.GetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable);
    private readonly string? _secret = Environment.GetEnvironmentVariable(PortalAdminCredentials.SecretVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.UrlVariable, _url);
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, _id);
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, _secret);
    }

    [Fact]
    public async Task ResolvesTheSecretFromTheEnvironment()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, "sa_abc");
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, "sas_plain");

        var credentials = await PortalAdminCredentials.ResolveAsync(null, null, CancellationToken.None);

        Assert.Equal("sa_abc", credentials.ClientId);
        Assert.Equal("sas_plain", credentials.ClientSecret);
    }

    [Fact]
    public async Task DereferencesASecretReferenceThroughTheStore()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, "sa_abc");
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, "SECRET:portal-admin");

        var credentials = await PortalAdminCredentials.ResolveAsync(
            new StubSecrets("portal-admin", "sas_from_store"), null, CancellationToken.None);

        Assert.Equal("sas_from_store", credentials.ClientSecret);
    }

    [Fact]
    public async Task MissingSecretSaysWhereToPutItAndThatArgvIsNotAnOption()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, "sa_abc");
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, null);

        var error = await Assert.ThrowsAsync<AdminCliException>(() =>
            PortalAdminCredentials.ResolveAsync(null, null, CancellationToken.None));

        Assert.Equal(AdminExitCode.AuthFailure, error.Code);
        Assert.Contains(PortalAdminCredentials.SecretVariable, error.Message, StringComparison.Ordinal);
        Assert.Contains("never accepted as a command-line argument", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The message must name the secret and never carry its value.</summary>
    [Fact]
    public async Task AnUnresolvableReferenceNamesTheSecretWithoutRevealingAnything()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, "sa_abc");
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, "SECRET:absent");

        var error = await Assert.ThrowsAsync<AdminCliException>(() =>
            PortalAdminCredentials.ResolveAsync(new StubSecrets("other", "value"), null, CancellationToken.None));

        Assert.Equal(AdminExitCode.AuthFailure, error.Code);
        Assert.Contains("absent", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecretReferenceWithNoStoreConfiguredFailsRatherThanUsingItLiterally()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, "sa_abc");
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, "SECRET:portal-admin");

        var error = await Assert.ThrowsAsync<AdminCliException>(() =>
            PortalAdminCredentials.ResolveAsync(null, null, CancellationToken.None));

        Assert.Equal(AdminExitCode.AuthFailure, error.Code);
    }

    /// <summary>A record's generated ToString would print every property, including the secret.</summary>
    [Fact]
    public async Task RenderingCredentialsNeverShowsTheSecret()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.ClientIdVariable, "sa_abc");
        Environment.SetEnvironmentVariable(PortalAdminCredentials.SecretVariable, "sas_topsecret");

        var credentials = await PortalAdminCredentials.ResolveAsync(null, null, CancellationToken.None);

        Assert.DoesNotContain("sas_topsecret", credentials.ToString(), StringComparison.Ordinal);
        Assert.Contains("sa_abc", credentials.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://portal.example.com")]
    [InlineData("")]
    public void ANonHttpUrlIsRejected(string url)
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.UrlVariable, null);

        Assert.Throws<AdminCliException>(() => PortalAdminCredentials.ResolveUrl(url));
    }

    [Fact]
    public void TheUrlFallsBackToTheEnvironmentAndLosesAnyTrailingSlash()
    {
        Environment.SetEnvironmentVariable(PortalAdminCredentials.UrlVariable, "https://portal.example.com/");

        Assert.Equal("https://portal.example.com", PortalAdminCredentials.ResolveUrl(null));
    }

    private sealed class StubSecrets(string name, string value) : ISecretProvider
    {
        public string ProviderName => "stub";

        public Task<SecretResolutionResult> ResolveAsync(string requested, CancellationToken cancellationToken = default) =>
            string.Equals(requested, name, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(new SecretResolutionResult(requested, value, ProviderName))
                : throw new InvalidOperationException($"Secret '{requested}' was not found.");
    }
}
