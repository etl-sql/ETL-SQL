using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETL_SQL.Core.Governance;

/// <summary>Frame kinds in the versioned Gateway operation model. One model over either transport.</summary>
public enum GatewayFrameKind
{
    /// <summary>Gateway → cloud, first frame: who this session is.</summary>
    Hello,

    /// <summary>Cloud → Gateway: session accepted.</summary>
    HelloAck,

    /// <summary>Cloud → Gateway: fresh nonce that the enrolled workload key must sign.</summary>
    Challenge,

    /// <summary>Gateway → cloud: proof of possession for the public key presented in Hello.</summary>
    Authenticate,

    /// <summary>Cloud → Gateway: a typed operation to execute.</summary>
    Operation,

    /// <summary>Gateway → cloud: a bounded batch of rows.</summary>
    RowBatch,

    /// <summary>Gateway → cloud: terminal success, with the recorded outcome.</summary>
    Complete,

    /// <summary>Gateway → cloud: terminal refusal or failure. Carries no target and no credential.</summary>
    Fault
}

/// <summary>
/// One frame of the Gateway operation protocol.
///
/// <para>What this record does <b>not</b> have is the security property: there is no host, port,
/// scheme, path, command, or connection-string field anywhere in it. The cloud side can name a
/// registered resource ID and an operation class and nothing else, so a compromised cloud side
/// cannot ask the Gateway to reach an arbitrary destination — the protocol simply cannot express the
/// request. That is what makes this a typed operation channel rather than a tunnel (§11.1).</para>
/// </summary>
public sealed record GatewayFrame
{
    /// <summary>Wire version. A Gateway refuses a version it does not implement rather than guessing.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public GatewayFrameKind Kind { get; init; }

    public string? TenantId { get; init; }
    public string? GatewayId { get; init; }
    public string? NodeId { get; init; }
    public string? WorkloadPublicKeyThumbprint { get; init; }
    public string? WorkloadPublicKey { get; init; }
    public string? Challenge { get; init; }
    public string? Signature { get; init; }
    public IReadOnlyList<GatewayPublishedResource>? PublishedResources { get; init; }

    public string? OperationId { get; init; }
    public string? CorrelationId { get; init; }
    public string? ResourceId { get; init; }
    public GatewayOperationClass OperationClass { get; init; }
    public GatewayOperationEffect Effect { get; init; }
    public GatewayOperationBounds? Bounds { get; init; }

    /// <summary>Bounded connector-specific request text, e.g. a parameterised query. Never a destination.</summary>
    public string? Request { get; init; }
    public IReadOnlyList<string>? Parameters { get; init; }
    public ViewerContextEnvelope? ViewerContext { get; init; }

    public IReadOnlyList<IReadOnlyList<string?>>? Rows { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }

    public GatewayOutcomeState OutcomeState { get; init; }
    public long RowsProduced { get; init; }
    public string? Reason { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static GatewayFrame Deserialize(string payload)
    {
        var frame = JsonSerializer.Deserialize<GatewayFrame>(payload, Json)
            ?? throw new GatewayProtocolException("A Gateway frame could not be decoded.");
        if (frame.Version != CurrentVersion)
            throw new GatewayProtocolException(
                $"Unsupported Gateway protocol version {frame.Version}; this Gateway implements version {CurrentVersion}.");
        return frame;
    }

    public static GatewayFrame Fault(string? operationId, string reason) => new()
    {
        Kind = GatewayFrameKind.Fault,
        OperationId = operationId,
        OutcomeState = GatewayOutcomeState.Failed,
        Reason = reason
    };
}
