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
    private readonly object _stateLock = new();

    private ExecutionTarget _target = ExecutionTarget.Local;
    private HostedSession? _session;
    private Task<HostedSession>? _inFlightRefresh;
    private long _selectionVersion;
    private bool _disposed;

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

            return $"Hosted: {_session.Server.Label} ({new Uri(_session.Server.Origin).Authority} / {_session.TenantId}) · {_session.User.Username}";
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

        HostedSession? previous;
        long selection;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            selection = ++_selectionVersion;
            previous = _session;
            _target = ExecutionTarget.Hosted(normalizedServer);
            _session = null;
            _inFlightRefresh = null;
        }
        NotifyStateChanged();

        // Revoke the previous session after retiring it locally.
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
        RequireSelection(selection, ct);
        var pkce = await _authenticator.AuthorizeAsync(origin, ClientId, RequestedScope, browserTimeout, ct);
        var ephemeralSecrets = new[] { pkce.Code, pkce.CodeVerifier };

        // 3. Exchange authorization code + PKCE verifier via /api/auth/client/exchange.
        RequireSelection(selection, ct);
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

        if (!IsValidTokenResponse(tokenResponse))
            throw new InvalidOperationException("Portal returned an invalid native token response.");
        var sessionSecrets = new[] { pkce.Code, pkce.CodeVerifier, tokenResponse.Token, tokenResponse.RefreshToken };

        var session = new HostedSession(
            normalizedServer,
            tokenResponse.SessionId,
            tokenResponse.Token,
            tokenResponse.RefreshToken,
            ToUtc(tokenResponse.ExpiresAt),
            tokenResponse.TenantId,
            tokenResponse.User,
            tokenResponse.Scopes,
            new HostedStudioSessionDto("", [], false, false, false, 0));

        try
        {
            RequireSelection(selection, ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/studio/session");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var studioResp = await _http.SendAsync(req, ct);
            if (!studioResp.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(studioResp, sessionSecrets, ct);
                throw new InvalidOperationException($"Portal Studio session check failed ({(int)studioResp.StatusCode}): {detail}");
            }

            var studioSession = await studioResp.Content.ReadFromJsonAsync<HostedStudioSessionDto>(JsonOptions, ct)
                ?? throw new InvalidOperationException("Portal returned an empty Studio session response.");
            session = session with { StudioSession = studioSession };

            lock (_stateLock)
            {
                RequireSelection(selection, ct);
                _session = session;
            }
        }
        catch
        {
            await BestEffortRevokeAsync(session, TimeSpan.FromSeconds(2), CancellationToken.None);
            throw;
        }

        NotifyStateChanged();
        return session;
    }

    private void RequireSelection(long selection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_selectionVersion != selection)
                throw new HostedSessionExpiredException("Hosted target changed during sign-in.");
        }
    }

    private bool IsValidTokenResponse(HostedClientTokenResponseDto? token) =>
        token is { User.Id: > 0, Scopes: not null } && token.ClientId == ClientId
        && token.Audience == "etl-sql-portal-native-api" && !string.IsNullOrWhiteSpace(token.SessionId)
        && !string.IsNullOrWhiteSpace(token.TenantId) && !string.IsNullOrWhiteSpace(token.Token)
        && !string.IsNullOrWhiteSpace(token.RefreshToken);

    private static DateTimeOffset ToUtc(DateTime value) => new(value.ToUniversalTime(), TimeSpan.Zero);

    public bool IsCurrentSession(HostedSession expected)
    {
        lock (_stateLock) return IsSameSession(_session, expected);
    }

    private static bool IsSameSession(HostedSession? current, HostedSession expected) =>
        current is not null && current.SessionId == expected.SessionId
        && current.Server.Origin == expected.Server.Origin
        && current.TenantId == expected.TenantId
        && current.User.Id == expected.User.Id;

    private HostedSession RequireSession(HostedSession? expected)
    {
        lock (_stateLock)
        {
            if (_session is null || (expected is not null && !IsSameSession(_session, expected)))
                throw new HostedSessionExpiredException("Hosted target or sign-in changed. Sign in again or switch to Local (Offline).");
            return _session;
        }
    }

    /// <summary>
    /// Sends an authorized request to the active hosted Portal with proactive + reactive
    /// single-flight refresh. Never silently falls back to Local on expiry.
    /// </summary>
    public async Task<HttpResponseMessage> AuthorizedSendAsync(
        Func<string, HttpRequestMessage> requestFactory,
        CancellationToken ct = default,
        HostedSession? expectedSession = null)
    {
        var session = RequireSession(expectedSession);
        if (_clock.GetUtcNow() >= session.ExpiresAtUtc - ProactiveRefreshSkew)
            session = await EnsureRefreshedAsync(session, ct);

        var response = await SendForSessionAsync(session, requestFactory, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        var refreshed = await EnsureRefreshedAsync(session, ct);
        var retry = await SendForSessionAsync(refreshed, requestFactory, ct);
        if (retry.StatusCode != HttpStatusCode.Unauthorized)
            return retry;

        retry.Dispose();
        ClearExpiredSession(refreshed);
        throw new HostedSessionExpiredException("Hosted Portal session expired or was revoked. Sign in again or switch to Local (Offline).");
    }

    private async Task<HttpResponseMessage> SendForSessionAsync(
        HostedSession session, Func<string, HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        try
        {
            using var req = requestFactory(session.Server.Origin);
            if (req.RequestUri is not { IsAbsoluteUri: true } uri
                || !string.Equals(uri.GetLeftPart(UriPartial.Authority), session.Server.Origin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Authorized requests must stay on the signed-in Portal origin.");

            Task<HttpResponseMessage> sending;
            lock (_stateLock)
            {
                var current = RequireSession(session);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.Token);
                sending = _http.SendAsync(req, ct);
            }

            var response = await sending;
            if (!IsCurrentSession(session))
            {
                response.Dispose();
                throw new HostedSessionExpiredException("Hosted target or sign-in changed while waiting for Portal.");
            }
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            if (!IsCurrentSession(session))
                throw new HostedSessionExpiredException("Hosted target or sign-in changed while waiting for Portal.");
            throw new HostedNetworkException(
                HostedSecretRedactor.Redact($"Network error communicating with {session.Server.Origin}: {ex.Message}", session.ActiveSecrets), ex);
        }
    }

    public async Task<HostedSession> EnsureRefreshedAsync(HostedSession staleSession, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Task<HostedSession> refresh;
        lock (_stateLock)
        {
            var current = RequireSession(staleSession);
            if (current.Token != staleSession.Token)
                return current;

            if (_inFlightRefresh is null || _inFlightRefresh.IsCompleted)
                _inFlightRefresh = Task.Run(() => PerformRefreshCoreAsync(staleSession, CancellationToken.None));
            refresh = _inFlightRefresh;
        }

        // Cancelling one caller shouldn't cancel a refresh shared by other requests.
        await refresh.WaitAsync(ct);
        return RequireSession(staleSession);
    }

    private async Task<HostedSession> PerformRefreshCoreAsync(HostedSession staleSession, CancellationToken ct)
    {
        RequireSession(staleSession);
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
                ClearExpiredSession(staleSession);
                throw new HostedSessionExpiredException("Hosted Portal session expired or was revoked. Sign in again or switch to Local (Offline).");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(resp, staleSession.ActiveSecrets, ct);
                throw new HostedNetworkException($"Portal session refresh failed ({(int)resp.StatusCode}): {detail}");
            }

            var refreshedDto = await resp.Content.ReadFromJsonAsync<HostedClientTokenResponseDto>(JsonOptions, ct);
            if (refreshedDto is null || !IsValidTokenResponse(refreshedDto)
                || refreshedDto.SessionId != staleSession.SessionId || refreshedDto.TenantId != staleSession.TenantId
                || refreshedDto.User.Id != staleSession.User.Id)
            {
                ClearExpiredSession(staleSession);
                throw new HostedSessionExpiredException("Portal returned a different or invalid native session during refresh.");
            }

            var updated = staleSession with
            {
                Token = refreshedDto.Token,
                RefreshToken = refreshedDto.RefreshToken,
                ExpiresAtUtc = ToUtc(refreshedDto.ExpiresAt),
                User = refreshedDto.User,
                Scopes = refreshedDto.Scopes
            };

            lock (_stateLock)
            {
                RequireSession(staleSession);
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
            ++_selectionVersion;
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

    private void ClearExpiredSession(HostedSession expected)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (IsSameSession(_session, expected) && _session!.Token == expected.Token)
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
        lock (_stateLock) _disposed = true;
        await DisconnectAsync(TimeSpan.FromSeconds(2));
        if (_ownsHttp)
            _http.Dispose();
    }
}
