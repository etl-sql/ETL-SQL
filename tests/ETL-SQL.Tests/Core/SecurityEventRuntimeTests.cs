using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class SecurityEventRuntimeTests
{
    [Fact]
    public void Deny_EmitsCorrelatedSanitizedEventThroughIndependentSink()
    {
        var sink = new RecordingSink();
        using var scope = SecurityEventRuntime.UseSinkForScope(sink);
        var snapshot = Snapshot("corr-17");

        var decision = OperationPolicyDecision.Deny(snapshot, "Connectors:Destination",
            "https://user:password@example.test/data?token=raw-secret",
            "approved hosts", "PASSWORD=raw-secret was denied for SECRET:catalog-key.");

        var securityEvent = Assert.Single(sink.Events);
        Assert.False(decision.IsAllowed);
        Assert.Equal(SecurityEventType.OperationDenied, securityEvent.Type);
        Assert.Equal(SecurityEventDecision.Denied, securityEvent.Decision);
        Assert.Equal("corr-17", securityEvent.CorrelationId);
        Assert.Equal("script-hash", securityEvent.ScriptHash);
        Assert.Equal("https://example.test", securityEvent.SanitizedTarget);
        Assert.DoesNotContain("raw-secret", securityEvent.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("catalog-key", securityEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("PASSWORD=********", securityEvent.Reason, StringComparison.Ordinal);
        Assert.Contains("SECRET:********", securityEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Deny_RemainsDeniedWhenMonitoringSinkFails()
    {
        var droppedBefore = SecurityEventRuntime.GetDiagnostics().DroppedCount;
        using var scope = SecurityEventRuntime.UseSinkForScope(new ThrowingSink());
        var snapshot = Snapshot("corr-failure");

        var decision = OperationPolicyDecision.Deny(snapshot, "Filesystem:Write",
            "C:\\private\\payroll.csv", "approved roots", "Outside approved root.");

        Assert.False(decision.IsAllowed);
        Assert.True(SecurityEventRuntime.GetDiagnostics().DroppedCount > droppedBefore);
    }

    [Fact]
    public void EnforcementBoundary_RemainsBlockedWhenMonitoringSinkFails()
    {
        var droppedBefore = SecurityEventRuntime.GetDiagnostics().DroppedCount;
        using var scope = SecurityEventRuntime.UseSinkForScope(new ThrowingSink());
        var snapshot = Snapshot("corr-boundary") with
        {
            ExecutionMode = ScriptExecutionMode.Remote,
            GovernedValues = new Dictionary<string, string?>
            {
                ["Security:RemoteExecutionMode"] = "Disabled"
            }
        };

        var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
            OperationPolicyBoundary.EnforceRemoteExecutionMode(snapshot));

        Assert.False(denied.Decision.IsAllowed);
        Assert.Equal("corr-boundary", denied.Decision.CorrelationId);
        Assert.Contains("remote execution", denied.Decision.Reason,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(SecurityEventRuntime.GetDiagnostics().DroppedCount > droppedBefore);
    }

    [Fact]
    public void Deny_ClassifiesPolicyAvailabilityAndResourceLimitEvents()
    {
        var sink = new RecordingSink();
        using var scope = SecurityEventRuntime.UseSinkForScope(sink);
        var snapshot = Snapshot("corr-types");

        OperationPolicyDecision.Deny(snapshot, "EnterprisePolicy:Freshness", "<operation>",
            "available policy", "Policy expired.");
        OperationPolicyDecision.Deny(snapshot, "Security:MaxFileOperationsPerScript", "<file-op>",
            "<= 100", "Limit reached.");

        Assert.Collection(sink.Events,
            first => Assert.Equal(SecurityEventType.PolicyAvailabilityFailure, first.Type),
            second => Assert.Equal(SecurityEventType.ResourceLimitViolation, second.Type));
    }

    [Fact]
    public void Sanitizer_RemovesConnectionStringsAndRootedPaths()
    {
        var basis = SecurityEventContract.Create(SecurityEventSeverity.Error,
            SecurityEventType.OperationDenied, "system", "system", "placeholder",
            SecurityEventDecision.Denied, "Denied.");

        var connection = SecurityEventSanitizer.Sanitize(basis with
        {
            SanitizedTarget = "Server=db01;Database=Sales;Password=raw-secret"
        });
        var path = SecurityEventSanitizer.Sanitize(basis with
        {
            SanitizedTarget = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!,
                "sensitive", "payroll.csv")
        });

        Assert.Equal("<connection-string>", connection.SanitizedTarget);
        Assert.Equal("<path>/payroll.csv", path.SanitizedTarget);
    }

    [Fact]
    public void EnrollmentChange_EmitsMachineAndTenantProvenance()
    {
        var sink = new RecordingSink();
        using var scope = SecurityEventRuntime.UseSinkForScope(sink);
        var enrollment = new EnterpriseEnrollmentDocument
        {
            Tenant = "corp-production",
            MachineId = "f6127c5025f74d78ac6b3fe2b8bfbdf8",
            PolicyEndpoint = "https://policy.example.test/etl-sql",
            PolicySigningPublicKey = "not-used-by-emission",
            ServiceIdentity = "service:etl-sql"
        };

        SecurityEventRuntime.EmitEnrollmentChanged(enrollment, "Machine enrollment created.");

        var securityEvent = Assert.Single(sink.Events);
        Assert.Equal(SecurityEventType.EnrollmentChanged, securityEvent.Type);
        Assert.Equal("corp-production", securityEvent.TenantId);
        Assert.Equal(enrollment.MachineId, securityEvent.NodeId);
        Assert.Equal("service:etl-sql", securityEvent.EffectiveIdentity);
    }

    [Fact]
    public void StandaloneOutboxPath_RejectsRelativeOverride()
    {
        var variable = SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable;
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "relative/security-events.db");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(
                SecurityEventOutboxPaths.Standalone);
            Assert.Contains("absolute path", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Emit_RedactsAdversarialPayloadBeforeSinkBoundary()
    {
        const string environmentKey = "ETLSQL_SECURITY_TEST_SECRET";
        const string environmentSecret = "environment-secret-7d21";
        var previous = Environment.GetEnvironmentVariable(environmentKey);
        Environment.SetEnvironmentVariable(environmentKey, environmentSecret);
        try
        {
            var sink = new RecordingSink();
            using var scope = SecurityEventRuntime.UseSinkForScope(sink);
            var securityEvent = SecurityEventContract.Create(
                SecurityEventSeverity.Error,
                SecurityEventType.OperationDenied,
                "user:42",
                "service:runner",
                "https://api.example.test/private/data?token=query-secret",
                SecurityEventDecision.Denied,
                "Request https://api.example.test/v1?token=query-secret failed; " +
                "Server=db01;Database=Sales;Password=connection-secret; " +
                $"environment={environmentSecret}; Windows=C:\\private\\payroll.csv; " +
                "Unix=/srv/private/payroll.csv;\n" +
                "at Connector.Send in C:\\agent\\src\\Connector.cs:line 42") with
            {
                NodeId = $"node-{environmentSecret}"
            };

            SecurityEventRuntime.Emit(securityEvent);

            var emitted = Assert.Single(sink.Events);
            var serialized = SecurityEventContract.Serialize(emitted);
            Assert.Equal("https://api.example.test", emitted.SanitizedTarget);
            Assert.DoesNotContain("query-secret", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("connection-secret", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(environmentSecret, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\private", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("/srv/private", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\agent", serialized, StringComparison.Ordinal);
            Assert.Contains("<connection-string>", emitted.Reason, StringComparison.Ordinal);
            Assert.Contains("?<query-redacted>", emitted.Reason, StringComparison.Ordinal);
            Assert.Contains("<path>", emitted.Reason, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, previous);
        }
    }

    private static ExecutionPolicySnapshot Snapshot(string correlationId) => new()
    {
        IsEnrolled = true,
        IsPolicyAvailable = true,
        PolicyStatus = "Active",
        PolicyVersion = "v4",
        PolicyHash = "policy-hash",
        Actor = "user:42",
        ExecutionMode = ScriptExecutionMode.Batch,
        ScriptHash = "script-hash",
        JobId = "job-9",
        CorrelationId = correlationId,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        GovernedValues = new Dictionary<string, string?>()
    };

    private sealed class RecordingSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private sealed class ThrowingSink : ISecurityEventSink
    {
        public void Emit(SecurityEvent securityEvent) => throw new IOException("Collector unavailable.");
    }
}
