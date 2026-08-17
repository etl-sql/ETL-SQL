using ETL_SQL.Core.Governance;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Attaches a Portal-signed caller assertion to the ad-hoc job channel's outbound requests.
///
/// <para>The channel carries report execution and data-quality submissions to a remote Orchestrator.
/// It previously sent the shared API key and nothing else, which a federated Orchestrator refuses
/// outright — it requires the key <em>and</em> a signed caller — so every remote report execution
/// answered <c>401</c> in exactly the Team-and-above topology the administration guide prescribes.
/// The admin proxy had carried an assertion since federation shipped; this path was simply
/// missed.</para>
///
/// <para>It also fixes attribution, which is the reason to do it here rather than by widening what
/// the Orchestrator accepts: these runs previously arrived with no principal at all, so nothing they
/// did could be attributed to anyone, and per-object authorization had nobody to authorize.</para>
///
/// <para>A null assertion means the deployment does not federate identity. The header is then
/// omitted rather than sent empty, which is what keeps a Solo host — API key only — working
/// unchanged.</para>
/// </summary>
public sealed class OrchestratorJobChannelIdentityHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<OrchestratorJobChannelIdentityHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A scope per request, deliberately. Message handlers are pooled and outlive any one request
        // — the default handler lifetime is minutes — so resolving the issuer (and the DbContext
        // behind it) once at construction would capture a scope that is disposed long before the
        // second call goes out.
        using var scope = scopeFactory.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<OrchestratorAssertionIssuer>();
        // IHttpContextAccessor is backed by an AsyncLocal, so the ambient request still resolves
        // through a freshly created scope. Background work legitimately has none.
        var user = scope.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.User;

        string? assertion = null;
        try
        {
            assertion = await issuer.IssueForCurrentCallerAsync(user, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never fail the submission here. An Orchestrator that requires an assertion will refuse
            // the call on its own terms, with its own message; turning a signing problem into an
            // exception from the HTTP stack would report it as a transport fault instead.
            logger.LogWarning(ex, "Could not issue an Orchestrator caller assertion for a job submission.");
        }

        if (!string.IsNullOrWhiteSpace(assertion))
        {
            request.Headers.Remove(OrchestratorIdentityAssertion.HeaderName);
            request.Headers.TryAddWithoutValidation(
                OrchestratorIdentityAssertion.HeaderName, assertion);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
