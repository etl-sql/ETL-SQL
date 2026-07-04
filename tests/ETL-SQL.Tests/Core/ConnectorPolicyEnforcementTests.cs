using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// End-to-end proof that CREATE/ALTER CONNECTION routes through <see cref="ConnectorPolicyAuthorizer"/>
/// and enforces enterprise connector-type and destination-host allowlists before a connection is
/// created, rejects URL-embedded credentials regardless of policy, and leaves unenrolled
/// (standalone) execution unrestricted by organization policy.
/// </summary>
public sealed class ConnectorPolicyEnforcementTests : IDisposable
{
    public void Dispose() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

    [Fact]
    public async Task ConnectorType_EnterpriseAllowlistDeniesDisallowedType()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedTypes: ["SQLITE"]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = 'db.example.com', DATABASE = 'd');"));
        Assert.Contains("connector type", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DestinationHost_EnterpriseAllowlistDeniesUnlistedHost()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["db.corp.internal"]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = 'evil.example.com', DATABASE = 'd');"));
        Assert.Contains("authorized host list", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DestinationHost_WildcardAllowlistStillDeniesObfuscatedInternalAddress()
    {
        // "*" allows any public host, but must never grant access to an internal range — even
        // when the loopback address is obfuscated as a 32-bit decimal literal.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["*"]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = '2130706433', DATABASE = 'd');"));
        Assert.Contains("internal address", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DestinationHost_ExplicitlyListedInternalAddressIsPermitted()
    {
        // When an operator explicitly lists the internal address, it passes the authorizer
        // (the connection may still fail later, but not on a policy denial).
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["127.0.0.1"]));

        var error = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = '127.0.0.1', DATABASE = 'd');"));
        Assert.IsNotType<ConnectorPolicyDeniedException>(Unwrap(error));
    }

    [Fact]
    public async Task EmbeddedUrlCredentials_RejectedRegardlessOfPolicy()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION api AS REST('https://user:s3cret@api.example.com/v1');"));
        Assert.Contains("Credentials embedded", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Standalone_Unenrolled_ConnectorTypeAndHostUnrestricted()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        // A disallowed-by-nothing POSTGRES connection to an arbitrary host passes the authorizer;
        // it may still fail later on an actual connection attempt, but never on a policy denial.
        var error = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION pg AS POSTGRES(HOST = 'db.example.com', DATABASE = 'd');"));
        Assert.IsNotType<ConnectorPolicyDeniedException>(Unwrap(error));
    }

    [Fact]
    public async Task RestoredSavedConnection_DeniedByCurrentPolicyIsDropped()
    {
        // A connection saved under a looser policy must not be reusable once policy tightens:
        // restoring a POSTGRES connection while only SQLITE is permitted drops it (no exception).
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedTypes: ["SQLITE"]));
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var state = new SessionState
        {
            SessionId = "conn-policy-restore",
            Connections = new List<ETL_SQL.Core.Data.ConnectionInfo>
            {
                new() { Name = "pg", Type = "POSTGRES", ConnectionString = "Host=db.example.com;Database=d" }
            }
        };

        await evaluator.LoadSessionState(state);

        Assert.False(evaluator.Connections.ContainsKey("pg"),
            "A saved connection denied by current policy must not be restored.");
    }

    [Fact]
    public void EnforceEnterpriseHost_DeniesObfuscatedInternalHostForDynamicRequests()
    {
        // The static entry point used by REST redirect/pagination/template requests applies the
        // same wildcard-safe internal-range denial as connection creation.
        var policy = EnrolledPolicy(allowedHosts: ["*"]);
        var context = new Moq.Mock<IExecutionContext>();
        context.SetupGet(c => c.ExecutionPolicy).Returns(ExecutionPolicySnapshot.Capture(
            policy, "operator", ScriptExecutionMode.Batch, "hash"));

        Assert.Throws<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context.Object, "2130706433"));

        // A public host under "*" is permitted.
        var ex = Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context.Object, "api.example.com"));
        Assert.Null(ex);
    }

    private static Exception? Unwrap(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is ConnectorPolicyDeniedException) return ex;
            ex = ex.InnerException;
        }
        return null;
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledPolicy(
        string[]? allowedTypes = null,
        string[]? allowedHosts = null)
    {
        var document = new OrganizationPolicyDocument
        {
            Connectors = new ConnectorPolicySection { AllowedTypes = allowedTypes ?? [] },
            RemoteExecution = new RemoteExecutionPolicySection
            {
                Mode = allowedHosts is { Length: > 0 }
                    ? RemoteExecutionMode.AllowedHosts
                    : RemoteExecutionMode.Disabled,
                AllowedHosts = allowedHosts ?? []
            }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}
