using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Governance;

public enum SecurityEventSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

public enum SecurityEventType
{
    OverrideAttempt,
    OperationDenied,
    PolicyValidationFailure,
    PolicyAvailabilityFailure,
    EnrollmentChanged,
    ResourceLimitViolation,
    CatalogMutation
}

public enum SecurityEventDecision
{
    Allowed,
    Denied,
    Warning,
    Failed
}

/// <summary>
/// Vendor-neutral event emitted by enterprise security boundaries. This contract is separate from
/// diagnostic logs and transactional governance audit records, but shares correlation identifiers
/// with them when an operation crosses all three concerns.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SecurityEvent
{
    public required int SchemaVersion { get; init; }
    public required Guid EventId { get; init; }
    public required SecurityEventSeverity Severity { get; init; }
    public required SecurityEventType Type { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string ActorIdentity { get; init; }
    public required string EffectiveIdentity { get; init; }
    public string? HostName { get; init; }
    public string? NodeId { get; init; }
    public string? TenantId { get; init; }
    public string? ScriptHash { get; init; }
    public string? JobId { get; init; }
    public string? CorrelationId { get; init; }
    public string? PolicyVersion { get; init; }
    public string? PolicyHash { get; init; }
    public required string SanitizedTarget { get; init; }
    public required SecurityEventDecision Decision { get; init; }
    public required string Reason { get; init; }
}

public static class SecurityEventContract
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static SecurityEvent Create(
        SecurityEventSeverity severity,
        SecurityEventType type,
        string actorIdentity,
        string effectiveIdentity,
        string sanitizedTarget,
        SecurityEventDecision decision,
        string reason,
        DateTimeOffset? timestampUtc = null,
        Guid? eventId = null) =>
        new()
        {
            SchemaVersion = CurrentSchemaVersion,
            EventId = eventId ?? Guid.NewGuid(),
            Severity = severity,
            Type = type,
            TimestampUtc = (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            ActorIdentity = RequireValue(actorIdentity, nameof(actorIdentity)),
            EffectiveIdentity = RequireValue(effectiveIdentity, nameof(effectiveIdentity)),
            SanitizedTarget = RequireValue(sanitizedTarget, nameof(sanitizedTarget)),
            Decision = decision,
            Reason = RequireValue(reason, nameof(reason))
        };

    public static string Serialize(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        Validate(securityEvent);
        return JsonSerializer.Serialize(securityEvent, SerializerOptions);
    }

    public static SecurityEvent Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var securityEvent = JsonSerializer.Deserialize<SecurityEvent>(json, SerializerOptions)
            ?? throw new JsonException("Security event payload was empty.");
        Validate(securityEvent);
        return securityEvent;
    }

    private static void Validate(SecurityEvent securityEvent)
    {
        if (securityEvent.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException($"Unsupported security event schema version '{securityEvent.SchemaVersion}'.");
        if (securityEvent.EventId == Guid.Empty)
            throw new JsonException("Security event ID must not be empty.");
        if (securityEvent.TimestampUtc.Offset != TimeSpan.Zero)
            throw new JsonException("Security event timestamp must be expressed in UTC.");

        RequireValue(securityEvent.ActorIdentity, nameof(securityEvent.ActorIdentity));
        RequireValue(securityEvent.EffectiveIdentity, nameof(securityEvent.EffectiveIdentity));
        RequireValue(securityEvent.SanitizedTarget, nameof(securityEvent.SanitizedTarget));
        RequireValue(securityEvent.Reason, nameof(securityEvent.Reason));
    }

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }
}
