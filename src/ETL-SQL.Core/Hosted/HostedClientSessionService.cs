using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Core.Hosted;

/// <summary>
/// Shared .NET hosted client session manager for ETL-SQL TUI (ST57) and Desktop Studio (ST58).
/// Defaults to Local (Offline) on every startup, holds native tokens strictly in process memory,
/// performs loopback PKCE S256 sign-in, single-flight refresh, and single-session logout.
/// Completely independent of TUI/Desktop UI code and separate from the script-level PORTAL connector.
/// </summary>
public sealed class HostedClientSessionService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ProactiveRefreshSkew = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly LoopbackPkceAuthenticator _authenticator;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateLock = new();

    private ExecutionTarget _target = ExecutionTarget.Local;
    private HostedSession? _session;
    private Task<HostedSession>? _inFlightRefresh;

    public string ClientId { get; }
    public string RequestedScope { get; }
    public HostedServerRegistryStore ServerRegistry { get; }

    public event Action? StateChanged;

    public HostedClientSessionService(
        string clientId = HostedClientIds.Tui,
        string requestedScope = HostedClientScopes.TuiDefault,
        HostedServerRegistryStore? serverRegistry = null,
        LoopbackPkceAuthenticator? authenticator = null,
        HttpClient? httpClient = null,
        TimeProvider? clock = null)
    {
        ClientId = string.IsNullOrWhiteSpace(clientId) ? HostedClientIds.Tui : clientId.Trim();
        RequestedScope = string.IsNullOrWhiteSpace(requestedScope) ? HostedClientScopes.TuiDefault : requestedScope.Trim();
        ServerRegistry = serverRegistry ?? new HostedServerRegistryStore();
        _authenticator = authenticator ?? new LoopbackPkceAuthenticator();
        _clock = clock ?? TimeProvider.System;
        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = PolicyBoundHttp.CreateClient(timeout: TimeSpan.FromSeconds(30));
            _ownsHttp = true;
        }
    }

    public ExecutionTarget Target
    {
        get { lock (_stateLock) return _target; }
    }

    public HostedSession? Session
    {
        get { lock (_stateLock) return _session; }
    }

    public bool IsHosted => Target.Kind == ExecutionTargetKind.Hosted;
    public bool IsSignedIn => Session is not null;

    public IReadOnlyList<string> GetActiveSecrets()
    {
        var session = Session;
        return session is null ? [] : session.ActiveSecrets.ToArray();
    }

    public string FormatStatusLabel()
    {
        lock (_stateLock)
        {
            if (_target.Kind != ExecutionTargetKind.Hosted || _target.Server is null)
                return "Local (Offline)";

            if (_session is null)
                return $"Hosted: {_target.Server.Label} (Sign in required)";

            return $"Hosted: {_session.Server.Label} ({_session.TenantId}) · {_session.User.Username}";
        }
    }

    public async Task<HostedSession> SignInAsync(
        HostedServerEntry server,
        TimeSpan? browserTimeout = null,
        CancellationToken ct = default)
    {
        if (!HostedOriginValidator.TryNormalizeTrustedOrigin(server.Origin, out var origin, out _, out var originError))
            throw new InvalidOperationException(originError ?? "Invalid Portal origin.");

        var normalizedServer = server with { Origin = origin };

        // Revoke any existing native session before switching host or account.
        var previous = Session;
        if (previous is not null)
            await BestEffortRevokeAsync(previous, TimeSpan.FromSeconds(3), ct);

        // 1. Probe /api/auth/providers on the trusted origin.
        HostedAuthProvidersDto? providers;
        try
        {
            using var probeResp = await _http.GetAsync($"{origin}/api/auth/providers", ct);
            if (!probeResp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Portal at {origin} returned HTTP {(int)probeResp.StatusCode} from /api/auth/providers.");
            providers = await probeResp.Content.ReadFromJsonAsync<HostedAuthProvidersDto>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new HostedNetworkException($"Could not reach Portal at {origin}: {ex.Message}", ex);
        }

        if (providers is not null && !providers.LocalEnabled && !providers.OidcEnabled && !providers.LdapEnabled)
            throw new InvalidOperationException($"Portal at {origin} has no enabled sign-in providers.");

        // 2. Run RFC 8252 loopback PKCE S256 browser flow.
        var pkce = await _authenticator.AuthorizeAsync(origin, ClientId, RequestedScope, browserTimeout, ct);
        var ephemeralSecrets = new[] { pkce.Code, pkce.CodeVerifier };

        // 3. Exchange authorization code + PKCE verifier via /api/auth/client/exchange.
        HostedClientTokenResponseDto tokenResponse;
        try
        {
            using var exchangeResp = await _http.PostAsJsonAsync(
                $"{origin}/api/auth/client/exchange",
                new
                {
                    code = pkce.Code,
                    client_id = ClientId,
                    redirect_uri = pkce.RedirectUri,
                    code_verifier = pkce.CodeVerifier
                },
                JsonOptions,
                ct);

            if (!exchangeResp.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(exchangeResp, ephemeralSecrets, ct);
                throw new InvalidOperationException($"Portal token exchange failed ({(int)exchangeResp.StatusCode}): {detail}");
            }

            tokenResponse = await exchangeResp.Content.ReadFromJsonAsync<HostedClientTokenResponseDto>(JsonOptions, ct)
                ?? throw new InvalidOperationException("Portal token exchange returned an empty response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException && !ct.IsCancellationRequested)
        {
            throw new HostedNetworkException(
                HostedSecretRedactor.Redact($"Network error during Portal token exchange: {ex.Message}", ephemeralSecrets), ex);
        }

        var sessionSecrets = new[] { pkce.Code, pkce.CodeVerifier, tokenResponse.Token, tokenResponse.RefreshToken };

        // 4. Verify Studio capabilities via /api/studio/session with the new native bearer token.
        HostedStudioSessionDto studioSession;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/studio/session");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.Token);
            using var studioResp = await _http.SendAsync(req, ct);
            if (!studioResp.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(studioResp, sessionSecrets, ct);
                throw new InvalidOperationException($"Portal Studio session check failed ({(int)studioResp.StatusCode}): {detail}");
            }

            studioSession = await studioResp.Content.ReadFromJsonAsync<HostedStudioSessionDto>(JsonOptions, ct)
                ?? throw new InvalidOperationException("Portal returned an empty Studio session response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException && !ct.IsCancellationRequested)
        {
            throw new HostedNetworkException(
                HostedSecretRedactor.Redact($"Network error checking Studio session: {ex.Message}", sessionSecrets), ex);
        }

        var expiresAt = tokenResponse.ExpiresAt.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(tokenResponse.ExpiresAt, TimeSpan.Zero)
            : new DateTimeOffset(tokenResponse.ExpiresAt.ToUniversalTime(), TimeSpan.Zero);

        var session = new HostedSession(
            normalizedServer,
            tokenResponse.SessionId,
            tokenResponse.Token,
            tokenResponse.RefreshToken,
            expiresAt,
            tokenResponse.TenantId,
            tokenResponse.User,
            tokenResponse.Scopes,
            studioSession);

        lock (_stateLock)
        {
            _target = ExecutionTarget.Hosted(normalizedServer);
            _session = session;
            _inFlightRefresh = null;
        }

        NotifyStateChanged();
        return session;
    }

    /// <summary>
    /// Sends an authorized request to the active hosted Portal with proactive + reactive
    /// single-flight refresh. Never silently falls back to Local on expiry.
    /// </summary>
    public async Task<HttpResponseMessage> AuthorizedSendAsync(
        Func<string, HttpRequestMessage> requestFactory,
        CancellationToken ct = default)
    {
        var session = Session;
        if (session is null)
            throw new HostedSessionExpiredException("Hosted Portal session is not signed in. Sign in again or switch to Local (Offline).");

        if (_clock.GetUtcNow() >= session.ExpiresAtUtc - ProactiveRefreshSkew)
            session = await EnsureRefreshedAsync(session, ct);

        HttpResponseMessage response;
        try
        {
            using var req = requestFactory(session.Server.Origin);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            response = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException && !ct.IsCancellationRequested)
        {
            throw new HostedNetworkException(
                HostedSecretRedactor.Redact($"Network error communicating with {session.Server.Origin}: {ex.Message}", session.ActiveSecrets), ex);
        }

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        // Reactive single-flight refresh on 401, then retry once.
        var refreshed = await EnsureRefreshedAsync(session, ct);
        try
        {
            using var retryReq = requestFactory(refreshed.Server.Origin);
            retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.Token);
            var retryResp = await _http.SendAsync(retryReq, ct);
            if (retryResp.StatusCode == HttpStatusCode.Unauthorized)
            {
                retryResp.Dispose();
                ClearExpiredSession(refreshed.SessionId);
                throw new HostedSessionExpiredException("Hosted Portal session expired or was revoked. Sign in again or switch to Local (Offline).");
            }
            return retryResp;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException && !ct.IsCancellationRequested)
        {
            throw new HostedNetworkException(
                HostedSecretRedactor.Redact($"Network error communicating with {refreshed.Server.Origin}: {ex.Message}", refreshed.ActiveSecrets), ex);
        }
    }

    public async Task<HostedSession> EnsureRefreshedAsync(HostedSession staleSession, CancellationToken ct = default)
    {
        Task<HostedSession> refreshTask;
        await _refreshGate.WaitAsync(ct);
        try
        {
            // Another caller may have already refreshed this session.
            var current = Session;
            if (current is not null
                && current.SessionId == staleSession.SessionId
                && current.Token != staleSession.Token
                && _clock.GetUtcNow() < current.ExpiresAtUtc - ProactiveRefreshSkew)
            {
                return current;
            }

            if (_inFlightRefresh is { IsCompleted: false })
            {
                refreshTask = _inFlightRefresh;
            }
            else
            {
                refreshTask = PerformRefreshCoreAsync(staleSession, ct);
                _inFlightRefresh = refreshTask;
            }
        }
        finally
        {
            _refreshGate.Release();
        }

        try
        {
            return await refreshTask;
        }
        finally
        {
            await _refreshGate.WaitAsync(CancellationToken.None);
            try
            {
                if (ReferenceEquals(_inFlightRefresh, refreshTask))
                    _inFlightRefresh = null;
            }
            finally
            {
                _refreshGate.Release();
            }
        }
    }

    private async Task<HostedSession> PerformRefreshCoreAsync(HostedSession staleSession, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync(
                $"{staleSession.Server.Origin}/api/auth/client/refresh",
                new
                {
                    refreshToken = staleSession.RefreshToken,
                    clientId = ClientId
                },
                JsonOptions,
                ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException && !ct.IsCancellationRequested)
        {
            throw new HostedNetworkException(
                HostedSecretRedactor.Redact($"Network error refreshing Portal session: {ex.Message}", staleSession.ActiveSecrets), ex);
        }

        using (resp)
        {
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                ClearExpiredSession(staleSession.SessionId);
                throw new HostedSessionExpiredException("Hosted Portal session expired or was revoked. Sign in again or switch to Local (Offline).");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(resp, staleSession.ActiveSecrets, ct);
                throw new HostedNetworkException($"Portal session refresh failed ({(int)resp.StatusCode}): {detail}");
            }

            var refreshedDto = await resp.Content.ReadFromJsonAsync<HostedClientTokenResponseDto>(JsonOptions, ct);
            if (refreshedDto is null || string.IsNullOrEmpty(refreshedDto.Token))
            {
                ClearExpiredSession(staleSession.SessionId);
                throw new HostedSessionExpiredException("Portal returned an empty refresh response.");
            }

            var expiresAt = refreshedDto.ExpiresAt.Kind == DateTimeKind.Utc
                ? new DateTimeOffset(refreshedDto.ExpiresAt, TimeSpan.Zero)
                : new DateTimeOffset(refreshedDto.ExpiresAt.ToUniversalTime(), TimeSpan.Zero);

            var updated = staleSession with
            {
                SessionId = refreshedDto.SessionId,
                Token = refreshedDto.Token,
                RefreshToken = refreshedDto.RefreshToken,
                ExpiresAtUtc = expiresAt,
                TenantId = refreshedDto.TenantId,
                User = refreshedDto.User,
                Scopes = refreshedDto.Scopes
            };

            lock (_stateLock)
            {
                // Guard against a disconnect or account switch that raced with refresh.
                if (_session is null || _session.SessionId != staleSession.SessionId)
                    throw new HostedSessionExpiredException("Hosted Portal session changed while refreshing.");
                _session = updated;
            }

            NotifyStateChanged();
            return updated;
        }
    }

    /// <summary>
    /// Clears the in-memory session, switches target back to Local (Offline),
    /// and best-effort revokes only this native session via /api/auth/client/logout.
    /// </summary>
    public async Task DisconnectAsync(TimeSpan? bestEffortTimeout = null, CancellationToken ct = default)
    {
        HostedSession? existing;
        lock (_stateLock)
        {
            existing = _session;
            _session = null;
            _target = ExecutionTarget.Local;
            _inFlightRefresh = null;
        }

        NotifyStateChanged();

        if (existing is not null)
            await BestEffortRevokeAsync(existing, bestEffortTimeout ?? TimeSpan.FromSeconds(5), ct);
    }

    private async Task BestEffortRevokeAsync(HostedSession session, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{session.Server.Origin}/api/auth/client/logout")
            {
                Content = JsonContent.Create(new { refreshToken = session.RefreshToken }, options: JsonOptions)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var _ = await _http.SendAsync(req, cts.Token);
        }
        catch
        {
            // Best-effort revocation; local in-memory tokens are already cleared.
        }
    }

    private void ClearExpiredSession(string expectedSessionId)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (_session is not null && _session.SessionId == expectedSessionId)
            {
                _session = null;
                changed = true;
            }
            // Deliberately keep _target as Hosted(server) — never silently fall back to Local.
        }

        if (changed)
            NotifyStateChanged();
    }

    private static async Task<string> ReadErrorDetailAsync(
        HttpResponseMessage resp,
        IEnumerable<string?> secrets,
        CancellationToken ct)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                            return HostedSecretRedactor.Redact(err.GetString(), secrets);
                        if (doc.RootElement.TryGetProperty("detail", out var det) && det.ValueKind == JsonValueKind.String)
                            return HostedSecretRedactor.Redact(det.GetString(), secrets);
                    }
                }
                catch { }
                return HostedSecretRedactor.Redact(body.Length > 200 ? body[..200] : body, secrets);
            }
        }
        catch { }
        return resp.ReasonPhrase ?? $"HTTP {(int)resp.StatusCode}";
    }

    private void NotifyStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(TimeSpan.FromSeconds(2));
        _refreshGate.Dispose();
        if (_ownsHttp)
            _http.Dispose();
    }
}
