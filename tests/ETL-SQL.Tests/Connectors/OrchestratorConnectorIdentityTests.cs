using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Core.Common.Exceptions;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// How the ORCHESTRATOR connector proves who it is.
///
/// <para>These are the connector half of the exchange shape: a connection authenticates to the
/// <b>Portal</b> and presents the resulting assertion to the Orchestrator. What is worth pinning down
/// here is how a connection string is *read* — which credential form it selects, and when it is
/// refused outright — because a misread credential does not fail loudly, it arrives at the
/// Orchestrator as an anonymous or wrong principal and is denied, which reads as a permissions
/// problem rather than a connection one. That the Orchestrator refuses an unsigned request is proven
/// where it belongs, in <c>OrchestratorJobApiAuthTests</c>.</para>
/// </summary>
public sealed class OrchestratorConnectorIdentityTests
{
    [Fact]
    public void PortalHostWithoutACredentialIsRefusedAtCreation()
    {
        var connector = new OrchestratorConnector(new Mock<ILogger>().Object);

        // Refused when the connection is created rather than on first use: a connection that names a
        // Portal but cannot authenticate to it will fail every statement, and saying so at CREATE
        // CONNECTION points at the line that is actually wrong.
        var error = Assert.Throws<ExecutionException>(() => connector.CreateDataSource(
            null!, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = "http://orchestrator:5100",
                ["PORTAL_HOST"] = "https://portal"
            }));

        Assert.Contains("CLIENT_ID", error.Message, StringComparison.Ordinal);
        Assert.Contains("USER", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordAloneStaysTheApiKeyAndDoesNotBecomeAPortalPassword()
    {
        var connector = new OrchestratorConnector(new Mock<ILogger>().Object);

        // PASSWORD is overloaded and USER is what disambiguates it. Without USER this is the shared
        // API key, which is what existing Solo scripts mean by it; reading it as a Portal password
        // would send a shared key to a login endpoint, and vice versa.
        var source = connector.CreateDataSource(
            null!, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = "http://orchestrator:5100",
                ["PASSWORD"] = "shared-api-key"
            });

        Assert.NotNull(source.Options);
        Assert.False(source.Options!.ContainsKey("PORTAL_HOST"));
        Assert.False(source.Options.ContainsKey("USER"));
    }

    [Fact]
    public void ACredentialIsNeverEchoedInTheConnectionsVisibleOptions()
    {
        var connector = new OrchestratorConnector(new Mock<ILogger>().Object);

        var source = connector.CreateDataSource(
            null!, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = "http://orchestrator:5100",
                ["PORTAL_HOST"] = "https://portal",
                ["CLIENT_ID"] = "sa_runner",
                ["CLIENT_SECRET"] = "sas_do-not-disclose"
            });

        // Options are surfaced by SHOW CONNECTIONS and land in diagnostics.
        Assert.DoesNotContain(
            "sas_do-not-disclose",
            string.Join('|', source.Options!.Select(option => $"{option.Key}={option.Value}")),
            StringComparison.Ordinal);
    }

    [Theory]
    // Complete: a Portal, and one of the two credential forms.
    [InlineData("https://portal", "alice", "pw", null, null, true)]
    [InlineData("https://portal", null, null, "sa_runner", "sas_x", true)]
    // Incomplete: half a credential is no credential.
    [InlineData("https://portal", "alice", null, null, null, false)]
    [InlineData("https://portal", null, null, "sa_runner", null, false)]
    [InlineData("https://portal", null, null, null, null, false)]
    // No Portal to exchange with, whatever else is supplied.
    [InlineData("", "alice", "pw", null, null, false)]
    public void ACredentialIsCompleteOnlyAsAWholePair(
        string portalHost, string? user, string? password,
        string? clientId, string? clientSecret, bool expected)
    {
        var credentials = new OrchestratorPortalCredentials(
            portalHost, user, password, clientId, clientSecret);

        Assert.Equal(expected, credentials.IsComplete);
    }

    [Fact]
    public void ClientCredentialsSelectTheServiceAccountExchange()
    {
        // The two forms reach different Portal endpoints — a service account cannot log in, and a
        // person has no client secret — so which one a connection is using has to be unambiguous.
        Assert.True(new OrchestratorPortalCredentials("https://p", null, null, "sa_x", "sas_y").IsServiceAccount);
        Assert.False(new OrchestratorPortalCredentials("https://p", "alice", "pw", null, null).IsServiceAccount);
    }
}
