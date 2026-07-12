using System.Runtime.CompilerServices;
using System.Collections;
using System.Text.RegularExpressions;
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
    private static readonly object SinkSync = new();
    private static ISecurityEventSink _sink = NullSecurityEventSink.Instance;
    private static readonly AsyncLocal<ISecurityEventSink?> ScopedSink = new();
    private static readonly ConditionalWeakTable<Exception, object> EmittedExceptions = new();

    public static ISecurityEventSink Sink
    {
        get => Volatile.Read(ref _sink);
        set => Volatile.Write(ref _sink, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public static SecurityEventOutbox ConfigureLocalOutbox(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        lock (SinkSync)
        {
            if (_sink is SecurityEventOutbox existing
                && string.Equals(existing.DatabasePath, fullPath, PathComparison()))
                return existing;
            var outbox = new SecurityEventOutbox(new SecurityEventOutboxOptions
            {
                DatabasePath = fullPath
            });
            Sink = outbox;
            return outbox;
        }
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

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

public static partial class SecurityEventSanitizer
{
    public static SecurityEvent Sanitize(SecurityEvent securityEvent)
        => Sanitize(securityEvent, GetSensitiveEnvironmentValues());

    internal static SecurityEvent Sanitize(
        SecurityEvent securityEvent,
        IEnumerable<string> sensitiveEnvironmentValues)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        ArgumentNullException.ThrowIfNull(sensitiveEnvironmentValues);
        var environmentValues = sensitiveEnvironmentValues
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ToArray();
        return securityEvent with
        {
            ActorIdentity = Redact(securityEvent.ActorIdentity, environmentValues),
            EffectiveIdentity = Redact(securityEvent.EffectiveIdentity, environmentValues),
            HostName = RedactNullable(securityEvent.HostName, environmentValues),
            NodeId = RedactNullable(securityEvent.NodeId, environmentValues),
            TenantId = RedactNullable(securityEvent.TenantId, environmentValues),
            ScriptHash = RedactNullable(securityEvent.ScriptHash, environmentValues),
            JobId = RedactNullable(securityEvent.JobId, environmentValues),
            CorrelationId = RedactNullable(securityEvent.CorrelationId, environmentValues),
            PolicyVersion = RedactNullable(securityEvent.PolicyVersion, environmentValues),
            PolicyHash = RedactNullable(securityEvent.PolicyHash, environmentValues),
            SanitizedTarget = SanitizeTarget(securityEvent.SanitizedTarget, environmentValues),
            Reason = SanitizeFreeText(securityEvent.Reason, environmentValues)
        };
    }

    private static string SanitizeTarget(string target, IReadOnlyList<string> environmentValues)
    {
        var redacted = Redact(target, environmentValues);
        if (Path.IsPathRooted(redacted))
            return Path.GetFileName(redacted) is { Length: > 0 } name ? $"<path>/{name}" : "<path>";
        if (redacted.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(redacted, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}";

        if (LooksLikeConnectionString(redacted)) return "<connection-string>";
        return redacted;
    }

    private static string SanitizeFreeText(string value, IReadOnlyList<string> environmentValues)
    {
        var redacted = Redact(value, environmentValues);
        redacted = ConnectionStringPattern().Replace(redacted, "<connection-string>");
        redacted = UrlQueryPattern().Replace(redacted, match => $"{match.Groups[1].Value}?<query-redacted>");
        redacted = WindowsPathPattern().Replace(redacted, "<path>");
        redacted = UnixPathPattern().Replace(redacted, "<path>");
        return redacted;
    }

    private static bool LooksLikeConnectionString(string value) =>
        value.Contains(';') && value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(segment => segment.Contains('=')) >= 2;

    private static string Redact(string value, IReadOnlyList<string> environmentValues)
    {
        var redacted = SecretRedactor.Redact(value) ?? string.Empty;
        foreach (var environmentValue in environmentValues)
            redacted = redacted.Replace(environmentValue, SecretRedactor.Mask, StringComparison.Ordinal);
        return redacted;
    }

    private static string? RedactNullable(string? value, IReadOnlyList<string> environmentValues) =>
        value is null ? null : Redact(value, environmentValues);

    private static IEnumerable<string> GetSensitiveEnvironmentValues()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            var value = entry.Value?.ToString();
            if (SecretRedactor.IsSensitiveKey(key) && !string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    [GeneratedRegex(@"(?:\b[A-Za-z][A-Za-z0-9_ ]*\s*=\s*[^;\r\n]*;){2,}(?:[A-Za-z][A-Za-z0-9_ ]*\s*=\s*[^;\r\n]*)?", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ConnectionStringPattern();

    [GeneratedRegex("""\b(https?://[^\s?'\"]+)\?[^\s'\"]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UrlQueryPattern();

    [GeneratedRegex("""(?:[A-Za-z]:\\|\\\\)[^\r\n'\"]+""", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex("""(?<![:/A-Za-z0-9])/(?:[^/\s'\"]+/)+[^/\s'\",;:]+""", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UnixPathPattern();
}
