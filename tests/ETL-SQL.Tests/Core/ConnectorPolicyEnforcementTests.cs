using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.ReportPortal;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
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
    public async Task ConnectorAlias_IsAuthorizedByCanonicalType()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedTypes: ["SQLITE"]));

        var error = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION aliasdb AS SQLITE3(DATABASE = ':memory:', TABLE = 'sqlite_master');"));

        Assert.Null(error);
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
        EnterprisePolicyRuntime.SetCurrent(policy);
        var context = new Moq.Mock<IExecutionContext>();
        context.SetupGet(c => c.ExecutionPolicy).Returns(ExecutionPolicySnapshot.Capture(
            policy, "operator", ScriptExecutionMode.Batch, "hash"));
        context.SetupGet(c => c.SecurityService).Returns(
            new ETL_SQL.Services.SecurityService(ETL_SQL.Common.NullLogger.Instance));

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context.Object, "2130706433"));

        // A public host under "*" is permitted.
        var ex = Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context.Object, "api.example.com"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DestinationScheme_EnterpriseAllowlistDeniesDisallowedScheme()
    {
        // Only https is permitted; a REST connection over http is denied at creation.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["*"], allowedSchemes: ["https"]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION api AS REST('http://api.example.com/v1');"));
        Assert.Contains("schemes", denied.ToString(), StringComparison.OrdinalIgnoreCase);

        // The same host over https passes the authorizer.
        var ok = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION api2 AS REST('https://api.example.com/v1');"));
        Assert.IsNotType<ConnectorPolicyDeniedException>(Unwrap(ok));
    }

    [Fact]
    public async Task DestinationPort_EnterpriseAllowlistDeniesDisallowedPort()
    {
        // 443 is permitted; an explicit :8080 on an allowed host is denied.
        EnterprisePolicyRuntime.SetCurrent(
            EnrolledPolicy(allowedHosts: ["*"], allowedPorts: [443]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync(
            "CREATE CONNECTION api AS REST('https://api.example.com:8080/v1');"));
        Assert.Contains("port", denied.ToString(), StringComparison.OrdinalIgnoreCase);

        // The scheme-default 443 (no explicit port) is allowed.
        var ok = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION api2 AS REST('https://api.example.com/v1');"));
        Assert.IsNotType<ConnectorPolicyDeniedException>(Unwrap(ok));
    }

    [Fact]
    public void EnforceEnterpriseUrl_AppliesSchemeAndPortRulesOnDynamicRequests()
    {
        // The dynamic REST path (redirects/pagination) enforces scheme and port, so a redirect to a
        // denied port on an allowed host is blocked even when the host allowlist is a wildcard.
        var policy = EnrolledPolicy(allowedHosts: ["*"], allowedSchemes: ["https"], allowedPorts: [443]);
        EnterprisePolicyRuntime.SetCurrent(policy);
        var context = new Moq.Mock<IExecutionContext>();
        context.SetupGet(c => c.ExecutionPolicy).Returns(ExecutionPolicySnapshot.Capture(
            policy, "operator", ScriptExecutionMode.Batch, "hash"));
        context.SetupGet(c => c.SecurityService).Returns(
            new ETL_SQL.Services.SecurityService(ETL_SQL.Common.NullLogger.Instance));

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context.Object, new Uri("https://api.example.com:8080/v1")));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context.Object, new Uri("http://api.example.com/v1")));

        var ok = Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceEnterpriseUrl(context.Object, new Uri("https://api.example.com/v1")));
        Assert.Null(ok);
    }

    [Fact]
    public async Task Standalone_Unenrolled_SchemeAndPortUnrestricted()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var error = await Record.ExceptionAsync(() => ExecuteAsync(
            "CREATE CONNECTION api AS REST('http://api.example.com:8080/v1');"));
        Assert.IsNotType<ConnectorPolicyDeniedException>(Unwrap(error));
    }

    [Fact]
    public void EnforceResolvedAddress_DeniesRebindToInternalIp_UnderHostAllowlist()
    {
        var eventSink = new RecordingSecurityEventSink();
        using var eventScope = SecurityEventRuntime.UseSinkForScope(eventSink);
        // DNS-rebinding defense: a name that passed the name-based allowlist ("*") must still be
        // denied at connect time if it resolved to a loopback/internal address.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["*"]));

        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("api.example.com", System.Net.IPAddress.Loopback));
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress(
                "api.example.com", System.Net.IPAddress.Parse("169.254.169.254"))); // link-local metadata
        Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress(
                "api.example.com", System.Net.IPAddress.Parse("10.0.0.5")));         // private range

        // A public resolved address is permitted under "*".
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("api.example.com", System.Net.IPAddress.Parse("93.184.216.34"))));

        Assert.Equal(3, eventSink.Events.Count);
        Assert.All(eventSink.Events, securityEvent =>
        {
            Assert.Equal(SecurityEventType.OperationDenied, securityEvent.Type);
            Assert.Equal("api.example.com", securityEvent.SanitizedTarget);
            Assert.DoesNotContain("127.0.0.1", securityEvent.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("169.254.169.254", securityEvent.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("10.0.0.5", securityEvent.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EnforceResolvedAddress_AllowsExplicitlyListedInternalIp()
    {
        // When an operator lists the internal address explicitly, the resolved connection is allowed.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedHosts: ["127.0.0.1"]));
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("localhost", System.Net.IPAddress.Loopback)));
    }

    [Fact]
    public void EnforceResolvedAddress_NoOpWhenStandaloneOrNoAllowlist()
    {
        // Standalone: no organization restriction applies at connect time.
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("localhost", System.Net.IPAddress.Loopback)));

        // Enrolled but with no host allowlist configured: parity with the name-based check (no-op).
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy());
        Assert.Null(Record.Exception(() =>
            ConnectorPolicyAuthorizer.EnforceResolvedAddress("localhost", System.Net.IPAddress.Loopback)));
    }

    [Fact]
    public void PolicyBoundHttp_CreateHandler_DisablesRedirectsAndAmbientProxy()
    {
        using var handler = PolicyBoundHttp.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void ProductionHttpClients_UsePolicyBoundFactory()
    {
        var srcRoot = Path.Combine(FindRepositoryRoot(), "src");
        var forbidden = new[]
        {
            "new HttpClient(",
            "new HttpClient {",
            "new HttpClient()",
            "new HttpClientHandler(",
            "new SocketsHttpHandler("
        };
        var allowedFile = Path.GetFullPath(Path.Combine(srcRoot, "ETL-SQL.Core", "Governance",
            "PolicyBoundHttp.cs"));

        var offenders = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFullPath(file), allowedFile, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { file, line, lineNumber = index + 1 }))
            .Where(hit => forbidden.Any(pattern => hit.line.Contains(pattern, StringComparison.Ordinal)))
            .Select(hit => $"{Path.GetRelativePath(srcRoot, hit.file)}:{hit.lineNumber}: {hit.line.Trim()}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task ReportPortal_RedirectToDeniedHost_IsNotFollowed()
    {
        var handler = new RedirectingHandler("http://169.254.169.254/latest/meta-data/");
        using var http = PolicyBoundHttp.CreateClient(handler, baseAddress: new Uri("https://portal.example.com/"));
        await using var portal = new ReportPortalDataSource(http, "admin", "pass", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            portal.ExecuteAdminStatementAsync(new ShowPortalUsersStatement(), SystemExecutionContext.Instance));

        Assert.Contains("302", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        Assert.Equal("portal.example.com", handler.Requests[0].Host);
    }

    [Fact]
    public async Task Orchestrator_RedirectToDeniedHost_IsNotFollowed()
    {
        var handler = new RedirectingHandler("http://169.254.169.254/latest/meta-data/");
        using var http = PolicyBoundHttp.CreateClient(handler, baseAddress: new Uri("https://orchestrator.example.com/"));
        await using var orchestrator = new OrchestratorDataSource(http, "key", NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            orchestrator.ExecuteAdminStatementAsync(new ShowJobsStatement(), SystemExecutionContext.Instance));

        Assert.Contains("302", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
        Assert.Equal("orchestrator.example.com", handler.Requests[0].Host);
    }

    [Fact]
    public async Task SharePoint_RedirectToDeniedHost_IsNotFollowed()
    {
        var handler = new RedirectingHandler("http://169.254.169.254/latest/meta-data/");
        await using var sharePoint = new SharePointConnector(SystemExecutionContext.Instance,
            "https://company.sharepoint.com/sites/Finance", null, handler);

        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            sharePoint.GetVersionAsync(SystemExecutionContext.Instance, "https://company.sharepoint.com/sites/Finance"));

        Assert.Contains("SharePoint site connection failed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
        Assert.Equal("company.sharepoint.com", handler.Requests[0].Host);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }

    private sealed class RedirectingHandler(string location) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Redirect)
            {
                RequestMessage = request,
                Headers = { Location = new Uri(location) },
                Content = new StringContent("")
            });
        }
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledPolicy(
        string[]? allowedTypes = null,
        string[]? allowedHosts = null,
        string[]? allowedSchemes = null,
        int[]? allowedPorts = null)
    {
        var document = new OrganizationPolicyDocument
        {
            Connectors = new ConnectorPolicySection { AllowedTypes = allowedTypes ?? [] },
            Network = new NetworkPolicySection
            {
                AllowedSchemes = allowedSchemes ?? [],
                AllowedPorts = allowedPorts ?? []
            },
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

    private sealed class RecordingSecurityEventSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }
}
