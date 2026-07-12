using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class SecurityEventHealthGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"security_gate_{Guid.NewGuid():N}");
    private readonly BootstrapSecurityEventReporter _reporter = new([]);

    [Theory]
    [InlineData("SecurityEvents:FailClosedMaxPendingEvents", 1, "pending backlog")]
    [InlineData("SecurityEvents:FailClosedMaxOldestEventSeconds", 10, "Oldest pending")]
    [InlineData("SecurityEvents:FailClosedMaxOutboxBytes", 1, "outbox size")]
    public void EnsureExecutionAllowed_BlocksWhenConfiguredBacklogThresholdIsReached(
        string key,
        long threshold,
        string expectedReason)
    {
        var outbox = CreateOutbox();
        outbox.Emit(Event());
        var snapshot = Snapshot(new Dictionary<string, string?>
        {
            [key] = threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        var error = Assert.Throws<SecurityEventDeliveryUnavailableException>(() =>
            SecurityEventHealthGate.EnsureExecutionAllowed(snapshot, outbox,
                DateTimeOffset.UtcNow.AddSeconds(20), _reporter));

        Assert.Contains(expectedReason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureExecutionAllowed_BlocksAtTerminalFailureThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        var outbox = CreateOutbox(maxAttempts: 1);
        var securityEvent = Event();
        outbox.Emit(securityEvent);
        Assert.Single(outbox.ClaimBatch(1, now, TimeSpan.FromMinutes(1)));
        outbox.MarkDeliveryFailed([securityEvent.EventId], "collector unavailable", now);
        var snapshot = Snapshot(new Dictionary<string, string?>
        {
            ["SecurityEvents:FailClosedMaxTerminalFailures"] = "1"
        });

        Assert.Throws<SecurityEventDeliveryUnavailableException>(() =>
            SecurityEventHealthGate.EnsureExecutionAllowed(snapshot, outbox, now, _reporter));
    }

    [Fact]
    public void EnsureExecutionAllowed_RemainsOpenWithoutSignedThresholdsOrEnrollment()
    {
        var outbox = CreateOutbox();
        outbox.Emit(Event());

        SecurityEventHealthGate.EnsureExecutionAllowed(Snapshot(
                new Dictionary<string, string?>()), outbox,
            DateTimeOffset.UtcNow.AddDays(1), _reporter);
        SecurityEventHealthGate.EnsureExecutionAllowed(
            Snapshot(new Dictionary<string, string?>
            {
                ["SecurityEvents:FailClosedMaxPendingEvents"] = "1"
            }) with
            { IsEnrolled = false }, outbox, DateTimeOffset.UtcNow, _reporter);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SecurityEventOutbox CreateOutbox(int maxAttempts = 3) => new(
        new SecurityEventOutboxOptions
        {
            DatabasePath = Path.Combine(_root, "events.db"),
            MaxDeliveryAttempts = maxAttempts
        }, jitter: () => 0.5);

    private static ExecutionPolicySnapshot Snapshot(IReadOnlyDictionary<string, string?> values) => new()
    {
        IsEnrolled = true,
        IsPolicyAvailable = true,
        PolicyStatus = "Live",
        Actor = "operator",
        ExecutionMode = ScriptExecutionMode.Batch,
        ScriptHash = "hash",
        CorrelationId = "correlation",
        CapturedAtUtc = DateTimeOffset.UtcNow,
        GovernedValues = values
    };

    private static SecurityEvent Event() => SecurityEventContract.Create(
        SecurityEventSeverity.Error, SecurityEventType.OperationDenied,
        "operator", "operator", "<target>", SecurityEventDecision.Denied, "Denied.");
}
