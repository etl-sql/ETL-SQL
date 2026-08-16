using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>How the Orchestrator decides who a caller is.</summary>
    public enum OrchestratorAuthorizationModeKind
    {
        /// <summary>Every request carries a Portal-signed caller assertion, so grants and ownership mean something.</summary>
        Federated,

        /// <summary>
        /// The API key alone is the identity. There are no principals, so there are no grants and no
        /// ownership decisions — the key is a root key over the whole catalog. Solo only.
        /// </summary>
        Legacy
    }

    /// <summary>
    /// The authorization mode this service is running in, and whether anything about the deployment
    /// contradicts it.
    ///
    /// <para>The mode is not configured directly in the common case: <c>RequireFederatedIdentity</c>
    /// defaults to "is the bind address non-loopback", which is a reasonable guess and a silent one.
    /// A shared Orchestrator behind a reverse proxy binds loopback and so guesses <i>legacy</i> —
    /// every operator on the far side of that proxy then shares one root key, and nothing said so.
    /// This type exists to make that visible: it names the mode, and it collects the evidence that the
    /// deployment is shared so startup can say the two do not agree.</para>
    /// </summary>
    public sealed record OrchestratorAuthorizationModeReport(
        OrchestratorAuthorizationModeKind Kind,
        bool ExplicitlyConfigured,
        IReadOnlyList<string> SharedDeploymentSignals)
    {
        public bool IsLegacy => Kind == OrchestratorAuthorizationModeKind.Legacy;

        /// <summary>The mode as it appears on the health endpoint: <c>federated</c> or <c>legacy</c>.</summary>
        public string Name => IsLegacy ? "legacy" : "federated";

        /// <summary>
        /// Legacy mode on a deployment that shows every sign of being shared. This is the state worth
        /// an operator's attention: legacy mode is supported, but only for one person on one box.
        /// </summary>
        public bool RequiresOperatorAttention => IsLegacy && SharedDeploymentSignals.Count > 0;

        /// <summary>
        /// The startup line. Reports the mode either way — an operator should be able to read the mode
        /// out of the log without inferring it from the absence of a warning.
        /// </summary>
        public string Describe()
        {
            if (!IsLegacy)
            {
                return "Orchestrator authorization mode: federated. Callers are identified by a "
                    + "Portal-signed assertion, and per-object grants and ownership are enforced.";
            }

            var opening = "Orchestrator authorization mode: LEGACY (Orchestrator:RequireFederatedIdentity"
                + (ExplicitlyConfigured ? "=false)." : " unset, and the service binds loopback).")
                + " Callers are not identified: the API key alone is a root key over every job, "
                + "schedule, and notification, and per-object grants and ownership do not apply. "
                + "This mode is supported for a Solo deployment only.";

            if (!RequiresOperatorAttention) return opening;

            return opening
                + " This deployment does not look Solo: "
                + string.Join("; ", SharedDeploymentSignals)
                + ". Configure Orchestrator:IdentitySigningSecret and set "
                + "Orchestrator:RequireFederatedIdentity=true, then assign owners to what this host "
                + "already had with 'etl-sql admin orchestrator adopt'.";
        }
    }

    /// <summary>
    /// Resolves — and is the single definition of — the Orchestrator's authorization mode. The startup
    /// guard, the request path, and the health endpoint all ask here, so the three cannot drift into
    /// disagreeing about which mode the service is in.
    /// </summary>
    public static class OrchestratorAuthorizationMode
    {
        /// <summary>
        /// True when a caller assertion is required. Explicit configuration wins; otherwise the bind
        /// address decides, on the reasoning that a service nobody else can reach needs no federation.
        /// </summary>
        public static bool RequiresFederatedIdentity(IConfiguration configuration) =>
            configuration.GetValue<bool?>("Orchestrator:RequireFederatedIdentity")
            ?? OrchestratorStartup.BindsToNonLoopback(configuration);

        /// <summary>Whether the grant and ownership model is inert — see <see cref="OrchestratorAuthorizationModeKind.Legacy"/>.</summary>
        public static bool IsLegacy(IConfiguration configuration) => !RequiresFederatedIdentity(configuration);

        // A reverse proxy is the case the bind address cannot see: the service binds loopback, so the
        // default guesses Solo, while the callers are a whole organization on the other side of the
        // proxy. Configuration says nothing about it, but the requests do — a forwarding header is a
        // remote caller announcing itself. Latched rather than counted: it is evidence about the
        // deployment, and it stays true once observed even if the next request arrives directly.
        private static int _proxiedRequestObserved;

        /// <summary>
        /// Notes that a request arrived through a reverse proxy. Returns true the first time only, so
        /// the caller can log the discovery once rather than on every request.
        /// </summary>
        internal static bool NoteProxiedRequest(HttpContext context)
        {
            if (Volatile.Read(ref _proxiedRequestObserved) != 0) return false;
            if (!context.Request.Headers.ContainsKey("X-Forwarded-For")
                && !context.Request.Headers.ContainsKey("Forwarded")
                && !context.Request.Headers.ContainsKey("X-Forwarded-Host"))
                return false;
            return Interlocked.Exchange(ref _proxiedRequestObserved, 1) == 0;
        }

        internal static bool ProxiedRequestObserved => Volatile.Read(ref _proxiedRequestObserved) != 0;

        /// <summary>Clears the latched proxy observation. Tests only — this is process-wide state.</summary>
        internal static void ResetProxyObservationForTests() => Volatile.Write(ref _proxiedRequestObserved, 0);

        public static OrchestratorAuthorizationModeReport Resolve(IConfiguration configuration)
        {
            var explicitlyConfigured = configuration.GetValue<bool?>("Orchestrator:RequireFederatedIdentity") is not null;
            var kind = RequiresFederatedIdentity(configuration)
                ? OrchestratorAuthorizationModeKind.Federated
                : OrchestratorAuthorizationModeKind.Legacy;

            return new OrchestratorAuthorizationModeReport(
                kind,
                explicitlyConfigured,
                kind == OrchestratorAuthorizationModeKind.Legacy
                    ? CollectSharedDeploymentSignals(configuration)
                    : []);
        }

        /// <summary>
        /// Everything about this deployment that says more than one person reaches it. Each entry is
        /// written to be read aloud in a log line, because that is where an operator meets it.
        /// </summary>
        private static IReadOnlyList<string> CollectSharedDeploymentSignals(IConfiguration configuration)
        {
            var signals = new List<string>();

            if (OrchestratorStartup.BindsToNonLoopback(configuration))
            {
                signals.Add(
                    "it is bound to a non-loopback address ("
                    + string.Join(", ", OrchestratorStartup.GetConfiguredUrls(configuration))
                    + ")");
            }

            if (ProxiedRequestObserved)
            {
                signals.Add("requests are arriving through a reverse proxy (X-Forwarded-For/Forwarded)");
            }

            // A signing secret is configured for exactly one purpose, and legacy mode ignores it. Either
            // a Portal was paired and the mode was then turned off, or the mode was never turned on.
            if (!string.IsNullOrWhiteSpace(configuration["Orchestrator:IdentitySigningSecret"]))
            {
                signals.Add("an identity-signing secret is configured, which only a Portal-paired service needs");
            }

            // A tenant is assigned by a Portal. A Solo host has none, and its objects are unbound.
            if (!string.IsNullOrWhiteSpace(configuration["Orchestrator:TenantId"]))
            {
                signals.Add("a tenant is configured (Orchestrator:TenantId), which a Solo host does not have");
            }

            return signals;
        }
    }
}
