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
        using var scope = SecurityEventRuntime.UseSinkForScope(new ThrowingSink());
        var snapshot = Snapshot("corr-failure");

        var decision = OperationPolicyDecision.Deny(snapshot, "Filesystem:Write",
            "C:\\private\\payroll.csv", "approved roots", "Outside approved root.");

        Assert.False(decision.IsAllowed);
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
