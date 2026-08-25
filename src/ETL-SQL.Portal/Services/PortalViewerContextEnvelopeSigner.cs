using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Services;

/// <summary>Loads the Portal-to-Gateway signing key only when a context-enabled resource is used.</summary>
public sealed class PortalViewerContextEnvelopeSigner(IConfiguration configuration) : IViewerContextEnvelopeSigner
{
    private HmacViewerContextEnvelopeService? _inner;
    private readonly Lock _gate = new();

    public ViewerContextEnvelope Sign(
        GatewayOperation operation,
        string viewerId,
        string realViewerId,
        string executingCredentialId,
        IReadOnlyDictionary<string, string> claims,
        ViewerContextPolicy policy)
    {
        lock (_gate)
        {
            _inner ??= Create();
            return _inner.Sign(operation, viewerId, realViewerId, executingCredentialId, claims, policy);
        }
    }

    private HmacViewerContextEnvelopeService Create()
    {
        var encodedKey = configuration["Portal:Gateway:ViewerContextHmacKey"]
            ?? Environment.GetEnvironmentVariable("ETLSQL_VIEWER_CONTEXT_HMAC_KEY");
        if (string.IsNullOrWhiteSpace(encodedKey))
            throw new GatewayProtocolException("Portal-to-Gateway viewer context signing is not configured.");
        try
        {
            var keyId = configuration["Portal:Gateway:ViewerContextKeyId"] ?? "portal-gateway-v1";
            return new HmacViewerContextEnvelopeService(keyId, Convert.FromBase64String(encodedKey));
        }
        catch (FormatException)
        {
            throw new GatewayProtocolException("The Portal-to-Gateway viewer context signing key is malformed.");
        }
    }
}
