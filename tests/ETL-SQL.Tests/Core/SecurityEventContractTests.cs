using System.Text.Json;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

public sealed class SecurityEventContractTests
{
    [Fact]
    public void Serialize_UsesStableVersionedJsonShapeAndRoundTrips()
    {
        var eventId = Guid.Parse("8a3cc9b9-d498-45c5-a26c-307f14ea88c5");
        var timestamp = new DateTimeOffset(2026, 7, 12, 15, 30, 0, TimeSpan.Zero);
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Error,
            SecurityEventType.OperationDenied,
            "user:42",
            "service-account:report-runner",
            "<path>/payroll.csv",
            SecurityEventDecision.Denied,
            "Enterprise policy denied write access.",
            timestamp,
            eventId) with
        {
            HostName = "etl-host-01",
            NodeId = "portal-a",
            TenantId = "acme",
            ScriptHash = "sha256:script",
            JobId = "job-17",
            CorrelationId = "corr-99",
            PolicyVersion = "v12",
            PolicyHash = "sha256:policy"
        };

        var json = SecurityEventContract.Serialize(securityEvent);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(SecurityEventContract.CurrentSchemaVersion,
            root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(eventId, root.GetProperty("eventId").GetGuid());
        Assert.Equal("error", root.GetProperty("severity").GetString());
        Assert.Equal("operationDenied", root.GetProperty("type").GetString());
        Assert.Equal("denied", root.GetProperty("decision").GetString());
        Assert.Equal("2026-07-12T15:30:00+00:00", root.GetProperty("timestampUtc").GetString());
        Assert.Equal("corr-99", root.GetProperty("correlationId").GetString());
        Assert.Equal(securityEvent, SecurityEventContract.Deserialize(json));
    }

    [Fact]
    public void Deserialize_RejectsUnknownMembersAndUnsupportedVersions()
    {
        var valid = SecurityEventContract.Create(
            SecurityEventSeverity.Warning,
            SecurityEventType.OverrideAttempt,
            "user:7",
            "user:7",
            "<setting>",
            SecurityEventDecision.Warning,
            "A locked setting override was attempted.",
            DateTimeOffset.UnixEpoch,
            Guid.Parse("5b23a38a-68a7-4c40-a31d-574c0e8ca94e"));
        var json = SecurityEventContract.Serialize(valid);

        Assert.Throws<JsonException>(() => SecurityEventContract.Deserialize(
            json.Replace("{", "{\"unexpected\":true,", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => SecurityEventContract.Deserialize(
            json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
    }

    [Fact]
    public void Create_RequiresSecurityBoundaryFieldsAndNormalizesTimestampToUtc()
    {
        Assert.Throws<ArgumentException>(() => SecurityEventContract.Create(
            SecurityEventSeverity.Error,
            SecurityEventType.PolicyValidationFailure,
            " ",
            "service:portal",
            "<policy>",
            SecurityEventDecision.Failed,
            "Signature validation failed."));

        var localTimestamp = new DateTimeOffset(2026, 7, 12, 10, 30, 0, TimeSpan.FromHours(-5));
        var securityEvent = SecurityEventContract.Create(
            SecurityEventSeverity.Error,
            SecurityEventType.PolicyValidationFailure,
            "system",
            "service:portal",
            "<policy>",
            SecurityEventDecision.Failed,
            "Signature validation failed.",
            localTimestamp);

        Assert.Equal(TimeSpan.Zero, securityEvent.TimestampUtc.Offset);
        Assert.Equal(15, securityEvent.TimestampUtc.Hour);
    }
}
