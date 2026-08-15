using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Data;

/// <summary>
/// The surrogate identity of an orchestrator job.
///
/// <para>This exists as a type rather than a <c>string</c> for one reason: a job has both a
/// <em>name</em> and an <em>id</em>, both are text, and passing the name where the id belongs is a
/// silent fault, not a loud one. The write matches zero rows, the read returns nothing, and nothing
/// throws — a lease is never taken, a run is never recorded, a watermark comes back empty and an
/// incremental load quietly reprocesses from the beginning. Moving from name-addressed to
/// id-addressed storage produced 97 such faults in one pass, every one of which the compiler had
/// accepted. With a distinct type it is a compile error instead.</para>
///
/// <para>The value is the store-assigned GUID ("N" format). It is opaque: nothing outside the store
/// parses it, and callers obtain one from a definition, a link, or a name lookup rather than
/// constructing it.</para>
/// </summary>
[JsonConverter(typeof(JobIdJsonConverter))]
public readonly record struct JobId(string Value)
{
    /// <summary>An object that has not been persisted, so has no identity yet.</summary>
    public static JobId None => default;

    /// <summary>False for <see cref="None"/> — a definition the store has not written yet.</summary>
    public bool IsAssigned => !string.IsNullOrWhiteSpace(Value);

    /// <summary>Mints a new identity. Only a store does this, on first insert.</summary>
    public static JobId New() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Rehydrates an identity read back from storage or a transport payload. Blank means the object
    /// has no identity yet, which is a legitimate state and not an error.
    /// </summary>
    public static JobId From(string? value) =>
        string.IsNullOrWhiteSpace(value) ? None : new JobId(value);

    /// <summary>
    /// The value, for a caller that must have one — parameter binding, mostly. Throws rather than
    /// binding null, so an unassigned identity surfaces at the call that misused it instead of as a
    /// statement that silently affects no rows.
    /// </summary>
    public string Require([System.Runtime.CompilerServices.CallerMemberName] string? caller = null) =>
        IsAssigned
            ? Value
            : throw new InvalidOperationException(
                $"{caller ?? "This operation"} requires a persisted job identity, but the job has none. " +
                "Resolve the job by name first (GetJobAsync) and pass its Id.");

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Surrogate identity of a schedule. See <see cref="JobId"/> for why this is a type.</summary>
[JsonConverter(typeof(ScheduleIdJsonConverter))]
public readonly record struct ScheduleId(string Value)
{
    public static ScheduleId None => default;
    public bool IsAssigned => !string.IsNullOrWhiteSpace(Value);
    public static ScheduleId New() => new(Guid.NewGuid().ToString("N"));
    public static ScheduleId From(string? value) =>
        string.IsNullOrWhiteSpace(value) ? None : new ScheduleId(value);

    public string Require([System.Runtime.CompilerServices.CallerMemberName] string? caller = null) =>
        IsAssigned
            ? Value
            : throw new InvalidOperationException(
                $"{caller ?? "This operation"} requires a persisted schedule identity, but the schedule has none. " +
                "Resolve the schedule by name first (GetScheduleAsync) and pass its Id.");

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Surrogate identity of a notification. See <see cref="JobId"/> for why this is a type.</summary>
[JsonConverter(typeof(NotificationIdJsonConverter))]
public readonly record struct NotificationId(string Value)
{
    public static NotificationId None => default;
    public bool IsAssigned => !string.IsNullOrWhiteSpace(Value);
    public static NotificationId New() => new(Guid.NewGuid().ToString("N"));
    public static NotificationId From(string? value) =>
        string.IsNullOrWhiteSpace(value) ? None : new NotificationId(value);

    public string Require([System.Runtime.CompilerServices.CallerMemberName] string? caller = null) =>
        IsAssigned
            ? Value
            : throw new InvalidOperationException(
                $"{caller ?? "This operation"} requires a persisted notification identity, but the notification has none. " +
                "Resolve the notification by name first (GetNotificationAsync) and pass its Id.");

    public override string ToString() => Value ?? string.Empty;
}

// The wire format stays a bare string: these identities travel through the Orchestrator API,
// promotion packages, and backup manifests, and none of those should gain a nested object because
// the engine tightened a type. An unassigned identity is written as null, matching what it replaced.

internal sealed class JobIdJsonConverter : JsonConverter<JobId>
{
    public override JobId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JobId.From(reader.TokenType == JsonTokenType.Null ? null : reader.GetString());

    public override void Write(Utf8JsonWriter writer, JobId value, JsonSerializerOptions options)
    {
        if (value.IsAssigned) writer.WriteStringValue(value.Value);
        else writer.WriteNullValue();
    }

    public override JobId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JobId.From(reader.GetString());

    public override void WriteAsPropertyName(Utf8JsonWriter writer, JobId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString());
}

internal sealed class ScheduleIdJsonConverter : JsonConverter<ScheduleId>
{
    public override ScheduleId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ScheduleId.From(reader.TokenType == JsonTokenType.Null ? null : reader.GetString());

    public override void Write(Utf8JsonWriter writer, ScheduleId value, JsonSerializerOptions options)
    {
        if (value.IsAssigned) writer.WriteStringValue(value.Value);
        else writer.WriteNullValue();
    }

    public override ScheduleId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ScheduleId.From(reader.GetString());

    public override void WriteAsPropertyName(Utf8JsonWriter writer, ScheduleId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString());
}

internal sealed class NotificationIdJsonConverter : JsonConverter<NotificationId>
{
    public override NotificationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationId.From(reader.TokenType == JsonTokenType.Null ? null : reader.GetString());

    public override void Write(Utf8JsonWriter writer, NotificationId value, JsonSerializerOptions options)
    {
        if (value.IsAssigned) writer.WriteStringValue(value.Value);
        else writer.WriteNullValue();
    }

    public override NotificationId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationId.From(reader.GetString());

    public override void WriteAsPropertyName(Utf8JsonWriter writer, NotificationId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString());
}
