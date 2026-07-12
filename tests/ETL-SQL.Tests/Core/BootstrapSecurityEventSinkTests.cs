using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class BootstrapSecurityEventSinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"bootstrap_events_{Guid.NewGuid():N}");

    [Fact]
    public void Reporter_SanitizesAndContinuesWhenOneSinkFails()
    {
        var recording = new RecordingSink();
        var reporter = new BootstrapSecurityEventReporter([new ThrowingSink(), recording]);

        reporter.Report("policy-startup",
            new InvalidOperationException("PASSWORD=bootstrap-secret at C:\\private\\enrollment.json"));

        var securityEvent = Assert.Single(recording.Events);
        Assert.Equal(SecurityEventSeverity.Critical, securityEvent.Severity);
        Assert.Equal(SecurityEventDecision.Failed, securityEvent.Decision);
        Assert.Equal("policy-startup", securityEvent.SanitizedTarget);
        Assert.DoesNotContain("bootstrap-secret", securityEvent.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", securityEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredFile_WritesSanitizedJsonLinesAndRotatesAtBound()
    {
        var path = Path.Combine(_root, "bootstrap.jsonl");
        var sink = new StructuredFileBootstrapSecurityEventSink(path, maxBytes: 700);
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Critical,
            SecurityEventType.PolicyAvailabilityFailure,
            "machine",
            "machine",
            "https://policy.example.test/envelope?token=query-secret",
            SecurityEventDecision.Failed,
            "Bearer bootstrap-token failed at /etc/etl-sql/enrollment.json");

        sink.Emit(securityEvent);
        var firstLine = Assert.Single(File.ReadAllLines(path));
        var stored = SecurityEventContract.Deserialize(firstLine);
        Assert.Equal("https://policy.example.test", stored.SanitizedTarget);
        Assert.DoesNotContain("bootstrap-token", firstLine, StringComparison.Ordinal);
        Assert.DoesNotContain("/etc/etl-sql", firstLine, StringComparison.Ordinal);

        sink.Emit(securityEvent with { EventId = Guid.NewGuid() });
        sink.Emit(securityEvent with { EventId = Guid.NewGuid() });

        Assert.True(File.Exists(path + ".previous"));
        Assert.All(File.ReadAllLines(path), line => SecurityEventContract.Deserialize(line));
        Assert.All(File.ReadAllLines(path + ".previous"), line => SecurityEventContract.Deserialize(line));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class RecordingSink : IBootstrapSecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }

    private sealed class ThrowingSink : IBootstrapSecurityEventSink
    {
        public void Emit(SecurityEvent securityEvent) => throw new IOException("sink unavailable");
    }
}
