import {
    AuthProvidersResponse,
    DEFAULT_NATIVE_SCOPES,
    ExecutionTarget,
    HostedServerEntry,
    HostedSessionState,
    NATIVE_CLIENT_ID,
    NativeClientTokenResponse,
    StudioSessionDto,
    normalizeTrustedServerOrigin,
    redactSecrets
} from './hostedTypes';
import {
    LoopbackCallbackListener,
    createPkcePair,
    createRandomState,
    startLoopbackCallbackListener
} from './loopbackPkceAuth';

export class HostedSessionExpiredError extends Error {
    constructor(message: string = 'Hosted session expired or was revoked. Sign in again.') {
        super(message);
        this.name = 'HostedSessionExpiredError';
    }
}

export class HostedNetworkError extends Error {
    constructor(message: string, public readonly cause?: unknown) {
        super(message);
        this.name = 'HostedNetworkError';
    }
}

export interface HostedSessionManagerOptions {
    fetchImpl?: typeof fetch;
    openBrowser: (url: string) => Promise<boolean> | Thenable<boolean>;
    now?: () => number;
    log?: (message: string) => void;
    signInTimeoutMs?: number;
    accessTokenRefreshSkewMs?: number;
    startLoopbackListener?: (expectedState: string, timeoutMs: number) => Promise<LoopbackCallbackListener>;
}

/**
 * In-memory-only hosted session and target manager.
 *
 * Always starts on Local (Offline) on every activation. Never persists access or refresh
 * tokens to SecretStorage, globalState, settings, workspace files, or logs.
 */
export class HostedSessionManager {
    private readonly _fetch: typeof fetch;
    private readonly _openBrowser: (url: string) => Promise<boolean> | Thenable<boolean>;
    private readonly _now: () => number;
    private readonly _log: (message: string) => void;
    private readonly _signInTimeoutMs: number;
    private readonly _refreshSkewMs: number;
    private readonly _startLoopback: (expectedState: string, timeoutMs: number) => Promise<LoopbackCallbackListener>;

    private _target: ExecutionTarget = { kind: 'local' };
    private _session: HostedSessionState | null = null;
    private _refreshInFlight: Promise<HostedSessionState> | null = null;
    private _activeLoopback: LoopbackCallbackListener | null = null;
    private _connection = 0;
    private readonly _listeners = new Set<() => void>();

    constructor(options: HostedSessionManagerOptions) {
        this._fetch = options.fetchImpl ?? globalThis.fetch.bind(globalThis);
        this._openBrowser = options.openBrowser;
        this._now = options.now ?? (() => Date.now());
        this._log = options.log ?? (() => {});
        this._signInTimeoutMs = options.signInTimeoutMs ?? 120_000;
        this._refreshSkewMs = options.accessTokenRefreshSkewMs ?? 60_000;
        this._startLoopback = options.startLoopbackListener ?? startLoopbackCallbackListener;
    }

    public onDidChange(listener: () => void): { dispose(): void } {
        this._listeners.add(listener);
        return { dispose: () => this._listeners.delete(listener) };
    }

    public getTarget(): ExecutionTarget {
        return this._target;
    }

    public getSession(): HostedSessionState | null {
        return this._session;
    }

    public isHosted(): boolean {
        return this._target.kind === 'hosted';
    }

    public isSignedIn(): boolean {
        return this._target.kind === 'hosted' && this._session !== null;
    }

    public getActiveSecrets(): string[] {
        if (!this._session) {
            return [];
        }
        return [this._session.token, this._session.refreshToken].filter(Boolean);
    }

    public formatStatusBarLabel(): string {
        if (this._target.kind === 'local') {
            return 'ETL-SQL: Local (Offline)';
        }
        const server = this._target.server;
        const host = this.extractHost(server.origin);
        if (this._session) {
            return `ETL-SQL: Hosted — ${server.label} (${host} / ${this._session.tenantId} · ${this._session.user.username})`;
        }
        return `ETL-SQL: Hosted — ${server.label} (${host} · Sign in required)`;
    }

    public cancelPendingSignIn(): void {
        this._connection++;
        if (this._activeLoopback) {
            this._activeLoopback.cancel('Sign-in cancelled.');
            this._activeLoopback = null;
        }
    }

    public async signIn(
        server: HostedServerEntry,
        options?: { timeoutMs?: number; scopes?: readonly string[] }
    ): Promise<HostedSessionState> {
        const validated = normalizeTrustedServerOrigin(server.origin);
        if (!validated.ok) {
            throw new Error(validated.error);
        }
        const normalizedServer: HostedServerEntry = { ...server, origin: validated.origin };

        this.cancelPendingSignIn();
        const connection = this._connection;
        const previous = this._session;
        this._session = null;
        this._refreshInFlight = null;
        this._target = { kind: 'hosted', server: normalizedServer };
        this.notifyChanged();
        if (previous) {
            await this.revokeSession(previous.server.origin, previous, 3_000);
            this.requireConnection(connection);
        }

        // 1. Discover auth providers on the target Portal.
        const providers = await this.fetchProviders(normalizedServer.origin);
        this.requireConnection(connection);
        this.safeLog(
            `[Hosted] Discovered auth providers at ${normalizedServer.origin}: local=${providers.local}, oidc=${providers.oidcEnabled}`
        );

        // 2. Generate PKCE S256 + state and bind ephemeral loopback listener.
        const pkce = createPkcePair();
        const state = createRandomState();
        const timeoutMs = options?.timeoutMs ?? this._signInTimeoutMs;
        const scopes = options?.scopes ?? DEFAULT_NATIVE_SCOPES;

        const listener = await this._startLoopback(state, timeoutMs);
        const callbackPromise = listener.waitForCallback();
        void callbackPromise.catch(() => {});
        if (connection !== this._connection) {
            listener.cancel();
            this.requireConnection(connection);
        }
        this._activeLoopback = listener;

        const authorizeUrl = new URL('/api/auth/client/authorize', normalizedServer.origin);
        authorizeUrl.searchParams.set('client_id', NATIVE_CLIENT_ID);
        authorizeUrl.searchParams.set('redirect_uri', listener.redirectUri);
        authorizeUrl.searchParams.set('code_challenge', pkce.challenge);
        authorizeUrl.searchParams.set('code_challenge_method', 'S256');
        authorizeUrl.searchParams.set('state', state);
        authorizeUrl.searchParams.set('scope', scopes.join(' '));

        let code: string;
        try {
            const opened = await this._openBrowser(authorizeUrl.toString());
            if (!opened) {
                listener.cancel('Could not launch system browser for sign-in.');
            }
            const callback = await callbackPromise;
            this.requireConnection(connection);
            code = callback.code;
        } finally {
            listener.cancel();
            if (this._activeLoopback === listener) {
                this._activeLoopback = null;
            }
        }

        // 3. Exchange one-time authorization code + PKCE verifier for native session credentials.
        const exchangeResp = await this.rawFetch(`${normalizedServer.origin}/api/auth/client/exchange`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                code,
                code_verifier: pkce.verifier,
                redirect_uri: listener.redirectUri,
                client_id: NATIVE_CLIENT_ID
            })
        });

        if (!exchangeResp.ok) {
            throw new Error(`Sign-in code exchange failed (${exchangeResp.status}).`);
        }

        const tokenResponse = await this.readNativeCredentials(exchangeResp);
        let studioSession: StudioSessionDto;
        try {
            this.requireConnection(connection);
            studioSession = await this.fetchStudioSession(normalizedServer.origin, tokenResponse.token);
            this.requireConnection(connection);
        } catch (error) {
            await this.revokeSession(normalizedServer.origin, tokenResponse, 3_000);
            throw error;
        }

        const sessionState: HostedSessionState = {
            server: normalizedServer,
            token: tokenResponse.token,
            refreshToken: tokenResponse.refreshToken,
            expiresAtMs: Date.parse(tokenResponse.expiresAt),
            sessionId: tokenResponse.sessionId,
            clientId: tokenResponse.clientId || NATIVE_CLIENT_ID,
            user: tokenResponse.user,
            tenantId: tokenResponse.tenantId,
            audience: tokenResponse.audience,
            scopes: tokenResponse.scopes ?? [],
            studioSession
        };

        this._target = { kind: 'hosted', server: normalizedServer };
        this._session = sessionState;
        this._refreshInFlight = null;
        this.safeLog(
            `[Hosted] Signed in to ${normalizedServer.origin} (tenant=${sessionState.tenantId}, user=${sessionState.user.username}, mode=${studioSession.mode})`
        );
        this.notifyChanged();
        return sessionState;
    }

    public async ensureValidAccessToken(): Promise<string> {
        const session = this._session;
        if (!session) {
            throw new HostedSessionExpiredError();
        }

        if (Number.isFinite(session.expiresAtMs) && session.expiresAtMs - this._now() >= this._refreshSkewMs) {
            return session.token;
        }

        const refreshed = await this.refreshSingleFlight();
        return refreshed.token;
    }

    public async refreshSingleFlight(): Promise<HostedSessionState> {
        if (this._refreshInFlight) {
            return this._refreshInFlight;
        }

        const current = this._session;
        if (!current) {
            throw new HostedSessionExpiredError();
        }

        const promise = this.performRefresh(current, this._connection);
        this._refreshInFlight = promise;
        try {
            return await promise;
        } finally {
            if (this._refreshInFlight === promise) {
                this._refreshInFlight = null;
            }
        }
    }

    public async authorizedFetch(path: string, init?: RequestInit): Promise<Response> {
        const session = this._session;
        if (!session) {
            throw new HostedSessionExpiredError();
        }

        const connection = this._connection;
        const token = await this.ensureValidAccessToken();
        this.requireConnection(connection);
        const url = `${session.server.origin}${path.startsWith('/') ? path : `/${path}`}`;

        const firstResp = await this.rawFetch(url, this.withBearer(init, token));
        this.requireConnection(connection);
        if (firstResp.status !== 401) {
            return firstResp;
        }

        // Another request may already have rotated the rejected token.
        const current = this._session;
        const refreshed = current && current.token !== token ? current : await this.refreshSingleFlight();
        this.requireConnection(connection);
        const retryResp = await this.rawFetch(url, this.withBearer(init, refreshed.token));
        this.requireConnection(connection);
        if (retryResp.status === 401) {
            if (this._session === refreshed) {
                this.clearSessionKeepTarget('Hosted session was rejected after refresh.');
            }
            throw new HostedSessionExpiredError();
        }
        return retryResp;
    }

    /**
     * Detaches the hosted session from the UI, returns the active target to Local (Offline),
     * and revokes ONLY this native client session via POST /api/auth/client/logout.
     */
    public async disconnect(options?: { bestEffortTimeoutMs?: number }): Promise<void> {
        this.cancelPendingSignIn();
        const snapshot = this._session;
        this._session = null;
        this._refreshInFlight = null;
        this._target = { kind: 'local' };
        this.notifyChanged();

        if (snapshot) {
            await this.revokeSession(snapshot.server.origin, snapshot, options?.bestEffortTimeoutMs ?? 5_000);
        }
    }

    private async revokeSession(origin: string, snapshot: Pick<HostedSessionState, 'token' | 'refreshToken'>, timeoutMs: number): Promise<void> {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeoutMs);
        timer.unref?.();

        try {
            const response = await this._fetch(`${origin}/api/auth/client/logout`, {
                redirect: 'error',
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${snapshot.token}`
                },
                body: JSON.stringify({ refreshToken: snapshot.refreshToken }),
                signal: controller.signal
            });
            this.safeLog(`[Hosted] Client logout at ${origin} returned HTTP ${response.status}.`);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            this.safeLog(`[Hosted] Best-effort logout to ${origin} did not complete: ${redactSecrets(msg, [snapshot.token, snapshot.refreshToken])}`);
        } finally {
            clearTimeout(timer);
        }
    }

    public async switchToLocal(): Promise<void> {
        await this.disconnect();
    }

    private async performRefresh(current: HostedSessionState, connection: number): Promise<HostedSessionState> {
        const resp = await this.rawFetch(`${current.server.origin}/api/auth/client/refresh`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                refreshToken: current.refreshToken,
                clientId: current.clientId
            })
        });

        this.requireConnection(connection);
        if (resp.status === 401 || resp.status === 403) {
            this.clearSessionKeepTarget('Hosted refresh token expired or revoked.');
            throw new HostedSessionExpiredError();
        }

        if (!resp.ok) {
            throw new HostedNetworkError(`Token refresh failed (${resp.status}).`);
        }

        const refreshed = await this.readNativeCredentials(resp);
        this.requireConnection(connection);
        if (refreshed.sessionId !== current.sessionId || refreshed.tenantId !== current.tenantId
            || refreshed.user.id !== current.user.id) {
            await this.revokeSession(current.server.origin, refreshed, 3_000);
            this.requireConnection(connection);
            this.clearSessionKeepTarget('Portal returned a different session during refresh.');
            throw new HostedSessionExpiredError();
        }

        const nextState: HostedSessionState = {
            server: current.server,
            token: refreshed.token,
            refreshToken: refreshed.refreshToken,
            expiresAtMs: Date.parse(refreshed.expiresAt),
            sessionId: refreshed.sessionId,
            clientId: refreshed.clientId || current.clientId,
            user: refreshed.user,
            tenantId: refreshed.tenantId,
            audience: refreshed.audience,
            scopes: refreshed.scopes ?? current.scopes,
            studioSession: current.studioSession
        };

        this._session = nextState;
        this.notifyChanged();
        return nextState;
    }

    private clearSessionKeepTarget(reason: string): void {
        this._session = null;
        this._refreshInFlight = null;
        this.safeLog(`[Hosted] ${reason}`);
        this.notifyChanged();
    }

    private async fetchProviders(origin: string): Promise<AuthProvidersResponse> {
        const resp = await this.rawFetch(`${origin}/api/auth/providers`, { method: 'GET' });
        if (!resp.ok) {
            throw new Error(`Could not reach Portal auth providers (${resp.status}).`);
        }
        return await resp.json() as AuthProvidersResponse;
    }

    private async fetchStudioSession(origin: string, accessToken: string): Promise<StudioSessionDto> {
        const resp = await this.rawFetch(`${origin}/api/studio/session`, {
            method: 'GET',
            headers: { Authorization: `Bearer ${accessToken}` }
        });
        if (resp.status === 401) {
            throw new HostedSessionExpiredError();
        }
        if (!resp.ok) {
            throw new Error(`Could not read Studio session capabilities (${resp.status}).`);
        }
        return await resp.json() as StudioSessionDto;
    }

    private async rawFetch(url: string, init?: RequestInit): Promise<Response> {
        try {
            const timeout = AbortSignal.timeout(30_000);
            const signal = init?.signal ? AbortSignal.any([init.signal, timeout]) : timeout;
            return await this._fetch(url, { ...init, signal, redirect: 'error' });
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            throw new HostedNetworkError(redactSecrets(msg, this.getActiveSecrets()));
        }
    }

    private withBearer(init: RequestInit | undefined, token: string): RequestInit {
        const headers = new Headers(init?.headers);
        headers.set('Authorization', `Bearer ${token}`);
        return { ...init, headers };
    }

    private requireConnection(connection: number): void {
        if (connection !== this._connection) {
            throw new Error('Hosted target or sign-in changed. Start the action again on the selected target.');
        }
    }

    private async readNativeCredentials(response: Response): Promise<NativeClientTokenResponse> {
        const credentials = await response.json().catch(() => null) as Partial<NativeClientTokenResponse> | null;
        if (!credentials || typeof credentials.token !== 'string' || !credentials.token
            || typeof credentials.refreshToken !== 'string' || !credentials.refreshToken
            || typeof credentials.sessionId !== 'string' || !credentials.sessionId
            || credentials.clientId !== NATIVE_CLIENT_ID || credentials.audience !== 'etl-sql-portal-native-api'
            || typeof credentials.tenantId !== 'string' || !credentials.tenantId
            || typeof credentials.expiresAt !== 'string' || !Number.isFinite(Date.parse(credentials.expiresAt))
            || !credentials.user || !Number.isSafeInteger(credentials.user.id) || credentials.user.id <= 0
            || typeof credentials.user.username !== 'string' || !credentials.user.username
            || !Array.isArray(credentials.scopes) || credentials.scopes.some(scope => typeof scope !== 'string')) {
            throw new Error('Portal returned an invalid native session response.');
        }
        return credentials as NativeClientTokenResponse;
    }

    private safeLog(message: string): void {
        this._log(redactSecrets(message, this.getActiveSecrets()));
    }

    private notifyChanged(): void {
        for (const listener of this._listeners) {
            try {
                listener();
            } catch {
                // ignore listener errors
            }
        }
    }

    private extractHost(origin: string): string {
        try {
            return new URL(origin).host;
        } catch {
            return origin;
        }
    }
}
