namespace ETL_SQL.Core.Governance;

public sealed class SecurityEventDeliveryUnavailableException(string message)
    : ETL_SQL.Services.SecurityException(message), ISecurityEventEmittedException;

/// <summary>
/// Applies signed fail-closed queue thresholds before top-level script execution. No threshold is
/// active unless it is present in the captured enterprise policy.
/// </summary>
public static class SecurityEventHealthGate
{
    public static void EnsureExecutionAllowed(
        ExecutionPolicySnapshot snapshot,
        SecurityEventOutbox? outbox = null,
        DateTimeOffset? nowUtc = null,
        BootstrapSecurityEventReporter? reporter = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsEnrolled) return;

        var terminalLimit = Threshold(snapshot, "SecurityEvents:FailClosedMaxTerminalFailures");
        var ageLimit = Threshold(snapshot, "SecurityEvents:FailClosedMaxOldestEventSeconds");
        var pendingLimit = Threshold(snapshot, "SecurityEvents:FailClosedMaxPendingEvents");
        var byteLimit = Threshold(snapshot, "SecurityEvents:FailClosedMaxOutboxBytes");
        if (terminalLimit is null && ageLimit is null && pendingLimit is null && byteLimit is null)
            return;

        outbox ??= SecurityEventRuntime.LocalOutbox;
        if (outbox is null)
            throw Denial("Security event fail-closed monitoring is enabled but the durable outbox is unavailable.", reporter);

        SecurityEventOutboxHealth health;
        try
        {
            health = outbox.GetHealth();
        }
        catch
        {
            throw Denial("Security event fail-closed monitoring could not read the durable outbox.", reporter);
        }
        if (terminalLimit is { } failures && health.FailedCount >= failures)
            throw Denial($"Security event terminal delivery failures ({health.FailedCount}) reached the fail-closed limit ({failures}).", reporter);
        if (pendingLimit is { } pending && health.PendingCount >= pending)
            throw Denial($"Security event pending backlog ({health.PendingCount}) reached the fail-closed limit ({pending}).", reporter);
        if (ageLimit is { } age && health.OldestPendingUtc is { } oldest)
        {
            var ageSeconds = Math.Max(0, ((nowUtc ?? DateTimeOffset.UtcNow) - oldest).TotalSeconds);
            if (ageSeconds >= age)
                throw Denial($"Oldest pending security event age ({(long)ageSeconds}s) reached the fail-closed limit ({age}s).", reporter);
        }
        if (byteLimit is { } bytes && health.StoredBytes >= bytes)
            throw Denial($"Security event outbox size ({health.StoredBytes} bytes) reached the fail-closed limit ({bytes} bytes).", reporter);
    }

    private static long? Threshold(ExecutionPolicySnapshot snapshot, string key)
    {
        if (!snapshot.GovernedValues.TryGetValue(key, out var raw)
            || !long.TryParse(raw, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value < 1)
            return null;
        return value;
    }

    private static SecurityEventDeliveryUnavailableException Denial(
        string reason,
        BootstrapSecurityEventReporter? reporter)
    {
        var exception = new SecurityEventDeliveryUnavailableException(reason);
        (reporter ?? BootstrapSecurityEventReporter.CreateDefault(SecurityEventOutboxPaths.Bootstrap()))
            .Report("security-event-health-gate", exception);
        return exception;
    }
}
