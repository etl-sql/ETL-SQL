using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Connectors.Orchestrator
{
    /// <summary>
    /// Obtains short-lived Orchestrator identity assertions from a Portal on a connection's behalf.
    ///
    /// <para>This is the client half of the exchange shape: the connection authenticates to the
    /// <b>Portal</b>, which is the single control plane for who exists and what they may do, and
    /// receives a token addressed to the Orchestrator. It never presents the Portal's own session
    /// token to the Orchestrator — the two are deliberately audience-separated so neither can be
    /// replayed at the other service.</para>
    ///
    /// <para>Shaped after <c>PortalDataSource.EnsureAuthenticatedAsync</c>, which is the precedent for
    /// a connector that logs in and caches a token. It differs in one respect that matters: a Portal
    /// session lasts long enough to renew five minutes early, while an Orchestrator assertion lives
    /// two minutes by design. Renewing five minutes early would mean renewing on every single call,
    /// so this renews on a fraction of the assertion's own lifetime instead.</para>
    /// </summary>
    internal sealed class OrchestratorAssertionExchange
    {
        /// <summary>
        /// How long before expiry a cached assertion is abandoned. Absolute rather than proportional
        /// so a clock skew or a slow call cannot land a request inside the window it was renewed to
        /// avoid; the assertion's own validation already tolerates 30 seconds of skew.
        /// </summary>
        private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _portal;
        private readonly OrchestratorPortalCredentials _credentials;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private string? _assertion;
        private DateTimeOffset _assertionExpiry = DateTimeOffset.MinValue;

        internal OrchestratorAssertionExchange(HttpClient portal, OrchestratorPortalCredentials credentials)
        {
            _portal = portal;
            _credentials = credentials;
        }

        /// <summary>
        /// The current assertion, exchanging for a new one when there is none or it is about to
        /// expire. Serialized so a burst of statements on one connection performs one exchange
        /// rather than one per statement.
        /// </summary>
        internal async Task<string> CurrentAssertionAsync(CancellationToken cancellationToken = default)
        {
            if (_assertion is not null && DateTimeOffset.UtcNow < _assertionExpiry - RenewBefore)
                return _assertion;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_assertion is not null && DateTimeOffset.UtcNow < _assertionExpiry - RenewBefore)
                    return _assertion;

                var portalToken = await AuthenticateToPortalAsync(cancellationToken);
                var issued = await ExchangeAsync(portalToken, cancellationToken);
                _assertion = issued.Assertion;
                _assertionExpiry = issued.ExpiresAt;
                return _assertion;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<string> AuthenticateToPortalAsync(CancellationToken cancellationToken)
        {
            if (_credentials.IsServiceAccount)
            {
                var response = await PostAsync(
                    "api/auth/service-token",
                    new { clientId = _credentials.ClientId, clientSecret = _credentials.ClientSecret },
                    cancellationToken);
                var token = await ReadAsync<ServiceTokenResponse>(response, "service-account token", cancellationToken);
                return token.AccessToken;
            }

            var loginResponse = await PostAsync(
                "api/auth/login",
                new { username = _credentials.User, password = _credentials.Password },
                cancellationToken);
            var login = await ReadAsync<LoginResponse>(loginResponse, "login", cancellationToken);
            return login.Token;
        }

        private async Task<IssuedAssertion> ExchangeAsync(string portalToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/orchestrator-assertion");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", portalToken);

            HttpResponseMessage response;
            try { response = await _portal.SendAsync(request, cancellationToken); }
            catch (HttpRequestException ex)
            {
                throw new ExecutionException($"Portal connection error during assertion exchange: {ex.Message}", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    // The Portal refuses without saying whether the deployment federates identity or
                    // this principal simply cannot be resolved to one, so neither can be reported —
                    // only that the exchange did not happen, and what to check.
                    throw new ExecutionException(
                        $"The Portal refused to issue an Orchestrator assertion ({(int)response.StatusCode}). " +
                        "Confirm the account may reach the Orchestrator and that the Portal is configured " +
                        "with an Orchestrator identity signing secret.");
                }

                var issued = await response.Content.ReadFromJsonAsync<IssuedAssertion>(Json, cancellationToken)
                    ?? throw new ExecutionException("The Portal returned an empty Orchestrator assertion.");
                if (string.IsNullOrWhiteSpace(issued.Assertion))
                    throw new ExecutionException("The Portal returned an Orchestrator assertion with no token.");
                return issued;
            }
        }

        private async Task<HttpResponseMessage> PostAsync(
            string path, object body, CancellationToken cancellationToken)
        {
            try { return await _portal.PostAsJsonAsync(path, body, Json, cancellationToken); }
            catch (HttpRequestException ex)
            {
                throw new ExecutionException($"Portal connection error: {ex.Message}", ex);
            }
        }

        private static async Task<T> ReadAsync<T>(
            HttpResponseMessage response, string what, CancellationToken cancellationToken)
        {
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    // The body is not echoed: a failed authentication reflects back what was sent, and
                    // what was sent is a credential.
                    throw new ExecutionException(
                        $"Portal {what} failed ({(int)response.StatusCode}).");
                }
                return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                    ?? throw new ExecutionException($"Portal {what} returned an empty response.");
            }
        }

        private sealed record LoginResponse(string Token, string RefreshToken, DateTime ExpiresAt);
        private sealed record ServiceTokenResponse(string AccessToken, string TokenType, int ExpiresIn);
        private sealed record IssuedAssertion(
            string Assertion,
            string HeaderName,
            string Audience,
            DateTimeOffset ExpiresAt,
            IReadOnlyList<string>? Scopes);
    }

    /// <summary>
    /// How a connection proves who it is to the Portal. Both forms exist because an OIDC-federated
    /// user has no Portal password to put in a connection string: they use a service account, whose
    /// client credentials are the Portal's own non-interactive shape.
    /// </summary>
    public sealed record OrchestratorPortalCredentials(
        string PortalHost,
        string? User,
        string? Password,
        string? ClientId,
        string? ClientSecret)
    {
        public bool IsServiceAccount => !string.IsNullOrWhiteSpace(ClientId);

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(PortalHost)
            && ((!string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password))
                || (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)));
    }
}
