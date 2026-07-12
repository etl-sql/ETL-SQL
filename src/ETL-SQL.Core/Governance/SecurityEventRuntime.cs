using System.Runtime.CompilerServices;
using System.Threading;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Independent destination for security events. Implementations must treat <see cref="Emit"/> as
/// an append operation and must not use diagnostic logging as the event store.
/// </summary>
public interface ISecurityEventSink
{
    void Emit(SecurityEvent securityEvent);
}

public interface ISecurityEventEmittedException;

public sealed class OperationPolicyDeniedException(OperationPolicyDecision decision)
    : ETL_SQL.Services.SecurityException(decision.Reason), ISecurityEventEmittedException
{
    public OperationPolicyDecision Decision { get; } = decision;
}

public static class SecurityEventRuntime
{
    private static ISecurityEventSink _sink = NullSecurityEventSink.Instance;
    private static readonly AsyncLocal<ISecurityEventSink?> ScopedSink = new();
    private static readonly ConditionalWeakTable<Exception, object> EmittedExceptions = new();

    public static ISecurityEventSink Sink
    {
        get => Volatile.Read(ref _sink);
        set => Volatile.Write(ref _sink, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// Emits a denial after sanitization. Sink failures are isolated from enforcement: policy has
    /// already denied the operation, and monitoring availability cannot change that decision.
    /// </summary>
    public static void EmitPolicyDenial(
        ExecutionPolicySnapshot snapshot,
        OperationPolicyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.IsAllowed) return;

        var eventType = decision.PolicyKey switch
        {
            "EnterprisePolicy:Freshness" => SecurityEventType.PolicyAvailabilityFailure,
            var key when key.Contains("Max", StringComparison.OrdinalIgnoreCase) =>
                SecurityEventType.ResourceLimitViolation,
            _ => SecurityEventType.OperationDenied
        };
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Error,
            eventType,
            snapshot.Actor,
            snapshot.Actor,
            decision.RequestedTarget,
            SecurityEventDecision.Denied,
            decision.Reason) with
        {
            HostName = Environment.MachineName,
            ScriptHash = snapshot.ScriptHash,
            JobId = snapshot.JobId,
            CorrelationId = snapshot.CorrelationId,
            PolicyVersion = snapshot.PolicyVersion,
            PolicyHash = snapshot.PolicyHash
        };

        Emit(securityEvent);
    }

    public static void EmitPolicyLoadFailure(
        EnterpriseEnrollmentDocument enrollment,
        string target,
        string reason,
        bool cacheAvailable,
        string? policyVersion = null)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        var validationFailure = reason.Contains("signature", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("rollback", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("invalid", StringComparison.OrdinalIgnoreCase);
        var identity = string.IsNullOrWhiteSpace(enrollment.ServiceIdentity)
            ? "machine"
            : enrollment.ServiceIdentity;
        var securityEvent = SecurityEventContract.Create(
            cacheAvailable ? SecurityEventSeverity.Warning : SecurityEventSeverity.Error,
            validationFailure
                ? SecurityEventType.PolicyValidationFailure
                : SecurityEventType.PolicyAvailabilityFailure,
            identity,
            identity,
            target,
            cacheAvailable ? SecurityEventDecision.Warning : SecurityEventDecision.Failed,
            reason) with
        {
            HostName = Environment.MachineName,
            NodeId = enrollment.MachineId,
            TenantId = enrollment.Tenant,
            PolicyVersion = policyVersion
        };
        Emit(securityEvent);
    }

    public static void EmitEnrollmentChanged(
        EnterpriseEnrollmentDocument? enrollment,
        string reason)
    {
        var identity = enrollment?.ServiceIdentity;
        if (string.IsNullOrWhiteSpace(identity)) identity = Environment.UserName;
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Information,
            SecurityEventType.EnrollmentChanged,
            identity,
            identity,
            "<enterprise-enrollment>",
            SecurityEventDecision.Allowed,
            reason) with
        {
            HostName = Environment.MachineName,
            NodeId = enrollment?.MachineId,
            TenantId = enrollment?.Tenant
        };
        Emit(securityEvent);
    }

    public static void EmitOverrideAttempt(
        ExecutionPolicySnapshot snapshot,
        string overrideName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Warning,
            SecurityEventType.OverrideAttempt,
            snapshot.Actor,
            snapshot.Actor,
            overrideName,
            SecurityEventDecision.Warning,
            "A script enabled a local security override.") with
        {
            HostName = Environment.MachineName,
            ScriptHash = snapshot.ScriptHash,
            JobId = snapshot.JobId,
            CorrelationId = snapshot.CorrelationId,
            PolicyVersion = snapshot.PolicyVersion,
            PolicyHash = snapshot.PolicyHash
        };
        Emit(securityEvent);
    }

    public static void EmitNetworkDenial(
        EffectiveEnterprisePolicy policy,
        string host,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Error,
            SecurityEventType.OperationDenied,
            "machine",
            "machine",
            host,
            SecurityEventDecision.Denied,
            reason) with
        {
            HostName = Environment.MachineName,
            PolicyVersion = policy.PolicyVersion
        };
        Emit(securityEvent);
    }

    public static void EmitUnhandledSecurityDenial(
        IExecutionContext context,
        string target,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not ETL_SQL.Services.SecurityException
            || exception is ISecurityEventEmittedException)
            return;
        lock (EmittedExceptions)
        {
            if (EmittedExceptions.TryGetValue(exception, out _)) return;
            EmittedExceptions.Add(exception, new object());
        }

        var snapshot = context.ExecutionPolicy ?? ExecutionPolicySnapshot.Capture(
            EnterprisePolicyRuntime.Current, Environment.UserName,
            context.InteractiveMode ? ScriptExecutionMode.Interactive : ScriptExecutionMode.Batch,
            "unknown");
        var decision = new OperationPolicyDecision(false, "Engine:SecurityGuardrail", target,
            "engine security invariants", exception.Message, snapshot.CorrelationId, snapshot.JobId,
            snapshot.PolicyVersion, snapshot.PolicyHash);
        EmitPolicyDenial(snapshot, decision);
    }

    public static void Emit(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        var sanitized = SecurityEventSanitizer.Sanitize(securityEvent);
        try
        {
            (ScopedSink.Value ?? Sink).Emit(sanitized);
        }
        catch
        {
            // Enforcement and local execution remain independent from optional monitoring sinks.
            // Durable fail-closed behavior belongs to the explicit outbox health gate, not here.
        }
    }

    internal static IDisposable UseSinkForScope(ISecurityEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var previous = ScopedSink.Value;
        ScopedSink.Value = sink;
        return new SinkScope(previous);
    }

    private sealed class SinkScope(ISecurityEventSink? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            ScopedSink.Value = previous;
            _disposed = true;
        }
    }

    private sealed class NullSecurityEventSink : ISecurityEventSink
    {
        public static NullSecurityEventSink Instance { get; } = new();
        public void Emit(SecurityEvent securityEvent) { }
    }
}

public static class SecurityEventSanitizer
{
    public static SecurityEvent Sanitize(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        return securityEvent with
        {
            ActorIdentity = Redact(securityEvent.ActorIdentity),
            EffectiveIdentity = Redact(securityEvent.EffectiveIdentity),
            HostName = RedactNullable(securityEvent.HostName),
            NodeId = RedactNullable(securityEvent.NodeId),
            TenantId = RedactNullable(securityEvent.TenantId),
            ScriptHash = RedactNullable(securityEvent.ScriptHash),
            JobId = RedactNullable(securityEvent.JobId),
            CorrelationId = RedactNullable(securityEvent.CorrelationId),
            PolicyVersion = RedactNullable(securityEvent.PolicyVersion),
            PolicyHash = RedactNullable(securityEvent.PolicyHash),
            SanitizedTarget = SanitizeTarget(securityEvent.SanitizedTarget),
            Reason = Redact(securityEvent.Reason)
        };
    }

    private static string SanitizeTarget(string target)
    {
        var redacted = Redact(target);
        if (Path.IsPathRooted(redacted))
            return Path.GetFileName(redacted) is { Length: > 0 } name ? $"<path>/{name}" : "<path>";
        if (redacted.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(redacted, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}";

        if (LooksLikeConnectionString(redacted)) return "<connection-string>";
        return redacted;
    }

    private static bool LooksLikeConnectionString(string value) =>
        value.Contains(';') && value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(segment => segment.Contains('=')) >= 2;

    private static string Redact(string value) => SecretRedactor.Redact(value) ?? string.Empty;
    private static string? RedactNullable(string? value) => SecretRedactor.Redact(value);
}
