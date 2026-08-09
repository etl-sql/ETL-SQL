using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Orchestrator.Service
{
    /// <summary>
    /// Startup-time security guard for the Orchestrator HTTP API.
    ///
    /// The ad-hoc job routes let a caller submit arbitrary ETL-SQL for execution, so the service must
    /// never come up unauthenticated on a network-reachable address. When <c>Orchestrator:ApiKey</c>
    /// is empty we permit only a loopback-bound service (developer convenience); any non-loopback bind
    /// without a key fails fast with an actionable message.
    /// </summary>
    public static class OrchestratorStartup
    {
        /// <summary>
        /// Collects the explicitly configured listen URLs from the standard sources
        /// (<c>ASPNETCORE_URLS</c>/<c>--urls</c> → config key "urls", and <c>Kestrel:Endpoints:*:Url</c>).
        /// Returns an empty list when nothing is configured, in which case the host default
        /// (loopback) applies.
        /// </summary>
        public static IReadOnlyList<string> GetConfiguredUrls(IConfiguration configuration)
        {
            var urls = new List<string>();

            var urlsValue = configuration["urls"];
            if (!string.IsNullOrWhiteSpace(urlsValue))
            {
                urls.AddRange(urlsValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            foreach (var endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
            {
                var url = endpoint["Url"];
                if (!string.IsNullOrWhiteSpace(url))
                {
                    urls.AddRange(url.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }

            return urls;
        }

        /// <summary>
        /// True when any configured listen URL binds to a non-loopback address. Kestrel wildcard binds
        /// (<c>0.0.0.0</c>, <c>[::]</c>, <c>+</c>, <c>*</c>) and any concrete hostname/IP that is not
        /// loopback are treated as network-reachable. With no configured URLs, the host's loopback
        /// default applies and this returns false.
        /// </summary>
        public static bool BindsToNonLoopback(IConfiguration configuration)
        {
            foreach (var url in GetConfiguredUrls(configuration))
            {
                if (IsNonLoopbackBinding(url))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsNonLoopbackBinding(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // Kestrel wildcard hosts are not valid System.Uri hosts but always mean "all interfaces".
            if (url.Contains("//+", StringComparison.Ordinal) ||
                url.Contains("//*", StringComparison.Ordinal) ||
                url.Contains("0.0.0.0", StringComparison.Ordinal) ||
                url.Contains("[::]", StringComparison.Ordinal))
            {
                return true;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Unparseable but concrete (e.g. "http://server:5000/" should have parsed); be safe and
                // treat anything we cannot prove is loopback as exposed.
                return true;
            }

            var host = uri.Host;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("::1", StringComparison.Ordinal) ||
                host.Equals("[::1]", StringComparison.Ordinal) ||
                host.StartsWith("127.", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> when no API key is configured but the service
        /// is bound to a non-loopback address. Call this during startup, before serving requests.
        /// </summary>
        public static void ValidateApiKeyBinding(IConfiguration configuration)
        {
            var maxPreviousApiKeys = Math.Max(0, configuration.GetValue<int?>("Orchestrator:MaxPreviousApiKeys") ?? 1);
            var previousApiKeys =
                configuration.GetSection("Orchestrator:PreviousApiKeys").Get<string[]>() ?? [];
            if (previousApiKeys.Length > maxPreviousApiKeys)
            {
                throw new InvalidOperationException(
                    $"Orchestrator:PreviousApiKeys supports at most {maxPreviousApiKeys} temporary previous key(s).");
            }

            var apiKeys = new[] { configuration["Orchestrator:ApiKey"] }
                .Concat(previousApiKeys);
            var hasApiKey = apiKeys.Any(key => !string.IsNullOrWhiteSpace(key));
            if (!hasApiKey && BindsToNonLoopback(configuration))
            {
                throw new InvalidOperationException(
                    "Orchestrator:ApiKey is not configured but the service is bound to a non-loopback address " +
                    $"({string.Join(", ", GetConfiguredUrls(configuration))}). The ad-hoc job API would accept " +
                    "unauthenticated script execution. Set Orchestrator:ApiKey (and the matching " +
                    "Portal:Orchestrator:ApiKey) or bind the service to loopback only.");
            }

            var requiresFederatedIdentity = configuration.GetValue<bool?>("Orchestrator:RequireFederatedIdentity")
                ?? BindsToNonLoopback(configuration);
            if (requiresFederatedIdentity)
            {
                var identitySecret = configuration["Orchestrator:IdentitySigningSecret"];
                try
                {
                    ETL_SQL.Core.Governance.OrchestratorIdentityAssertion.ValidateSecret(identitySecret ?? string.Empty);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException(
                        "Federated Orchestrator identity is required, but Orchestrator:IdentitySigningSecret " +
                        "is missing or shorter than 32 UTF-8 bytes. Configure the matching " +
                        "Portal:Orchestrator:IdentitySigningSecret. Set RequireFederatedIdentity=false only " +
                        "for an isolated legacy deployment.", ex);
                }
            }
        }
    }
}
