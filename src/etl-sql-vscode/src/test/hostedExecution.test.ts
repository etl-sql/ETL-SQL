import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import {
    HostedServerEntry,
    normalizeTrustedServerOrigin,
    parseSavedServers,
    redactSecrets
} from '../hosted/hostedTypes';
import {
    LoopbackAuthError,
    createPkcePair,
    createRandomState,
    startLoopbackCallbackListener
} from '../hosted/loopbackPkceAuth';
import {
    HostedNetworkError,
    HostedSessionExpiredError,
    HostedSessionManager
} from '../hosted/hostedSessionManager';
import { HostedExecutionController } from '../hosted/hostedExecutionController';

describe('hostedTypes', () => {
    it('accepts HTTPS origins and explicit loopback HTTP origins, and rejects non-loopback HTTP or malformed URLs', () => {
        expect(normalizeTrustedServerOrigin('https://portal.corp.example')).toEqual({
            ok: true,
            origin: 'https://portal.corp.example',
            host: 'portal.corp.example'
        });
        expect(normalizeTrustedServerOrigin('http://127.0.0.1:5001')).toEqual({
            ok: true,
            origin: 'http://127.0.0.1:5001',
            host: '127.0.0.1:5001'
        });
        expect(normalizeTrustedServerOrigin('http://localhost:5001/')).toEqual({
            ok: true,
            origin: 'http://localhost:5001',
            host: 'localhost:5001'
        });
        expect(normalizeTrustedServerOrigin('http://[::1]:5001')).toEqual({
            ok: true,
            origin: 'http://[::1]:5001',
            host: '[::1]:5001'
        });

        expect(normalizeTrustedServerOrigin('http://portal.corp.example').ok).toBe(false);
        expect(normalizeTrustedServerOrigin('https://user:pass@portal.corp.example').ok).toBe(false);
        expect(normalizeTrustedServerOrigin('https://portal.corp.example/subpath').ok).toBe(false);
        expect(normalizeTrustedServerOrigin('https://portal.corp.example?a=1').ok).toBe(false);
        expect(normalizeTrustedServerOrigin('ftp://portal.corp.example').ok).toBe(false);
    });

    it('filters invalid entries and deduplicates aliases in parseSavedServers', () => {
        const servers = parseSavedServers([
            { alias: 'prod', label: 'Prod', origin: 'https://portal.corp.example', environment: 'Prod' },
            { alias: 'prod', label: 'Duplicate', origin: 'https://other.corp.example' },
            { alias: 'insecure', label: 'Insecure', origin: 'http://portal.corp.example' },
            { alias: 'dev', label: 'Local Dev', origin: 'http://127.0.0.1:5001' }
        ]);
        expect(servers).toHaveLength(2);
        expect(servers.map(s => s.alias)).toEqual(['prod', 'dev']);
    });

    it('redacts secrets from log strings', () => {
        expect(redactSecrets('Bearer eyJsecretTokenValue123 failed', ['eyJsecretTokenValue123'])).toBe(
            'Bearer [REDACTED] failed'
        );
    });
});

describe('loopbackPkceAuth', () => {
    it('generates RFC 7636 S256 PKCE pairs and validates callback success, denial, mismatch, cancel, and timeout', async () => {
        const pkce = createPkcePair();
        expect(pkce.verifier.length).toBeGreaterThanOrEqual(43);
        expect(pkce.challenge).toMatch(/^[A-Za-z0-9_-]{43}$/);

        // 1. Success path
        const state1 = createRandomState();
        const listener1 = await startLoopbackCallbackListener(state1, 5_000);
        expect(listener1.redirectUri).toMatch(/^http:\/\/127\.0\.0\.1:\d+\/callback$/);
        const wait1 = listener1.waitForCallback();
        const cbResp1 = await fetch(`${listener1.redirectUri}?code=one-time-code&state=${encodeURIComponent(state1)}`);
        expect(cbResp1.status).toBe(200);
        expect(await cbResp1.text()).toContain('Signed in to ETL-SQL');
        expect(await wait1).toEqual({ code: 'one-time-code', state: state1 });

        // 2. Denial path
        const state2 = createRandomState();
        const listener2 = await startLoopbackCallbackListener(state2, 5_000);
        const expect2 = expect(listener2.waitForCallback()).rejects.toMatchObject({ code: 'access_denied' });
        await fetch(`${listener2.redirectUri}?error=access_denied&state=${encodeURIComponent(state2)}`);
        await expect2;

        // 3. State mismatch path
        const state3 = createRandomState();
        const listener3 = await startLoopbackCallbackListener(state3, 5_000);
        const expect3 = expect(listener3.waitForCallback()).rejects.toBeInstanceOf(LoopbackAuthError);
        const cbResp3 = await fetch(`${listener3.redirectUri}?code=abc&state=wrong-state-value`);
        expect(cbResp3.status).toBe(400);
        await expect3;

        // 4. Cancellation path
        const listener4 = await startLoopbackCallbackListener(createRandomState(), 5_000);
        const wait4 = listener4.waitForCallback();
        listener4.cancel('User aborted');
        await expect(wait4).rejects.toMatchObject({ code: 'cancelled' });

        // 5. Timeout path
        const listener5 = await startLoopbackCallbackListener(createRandomState(), 40);
        await expect(listener5.waitForCallback()).rejects.toMatchObject({ code: 'timeout' });
    });
});

describe('HostedSessionManager', () => {
    const server: HostedServerEntry = { alias: 'dev', label: 'Dev', origin: 'http://127.0.0.1:5001' };

    it('defaults to Local (Offline) on activation and shares one in-flight refresh across concurrent callers', async () => {
        let nowMs = 1_000_000;
        let refreshCalls = 0;

        const fetchMock = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
            const url = String(input);
            if (url.endsWith('/api/auth/providers')) {
                return jsonResp({ local: false, oidcEnabled: true, oidcLoginUrl: '/api/auth/oidc/login' });
            }
            if (url.endsWith('/api/auth/client/exchange')) {
                return jsonResp({
                    token: 'jwt-initial-secret-1111',
                    refreshToken: 'refresh-initial-secret-1111',
                    expiresAt: new Date(nowMs + 30_000).toISOString(), // within 60s skew -> triggers refresh
                    sessionId: 'sess-1',
                    clientId: 'etlsql-vscode',
                    user: { id: 7, username: 'alice', email: 'alice@example.com', roles: ['Publisher'] },
                    tenantId: 'tenant-alpha',
                    audience: 'etl-sql-portal-native-api',
                    scopes: ['studio.read', 'studio.run', 'studio.save']
                });
            }
            if (url.endsWith('/api/studio/session')) {
                return jsonResp({
                    mode: 'CatalogOnly',
                    capabilities: ['StudioAccess', 'ScriptRead', 'ScriptRun', 'ScriptSave'],
                    sourceControlEnabled: false,
                    draftRecovery: true
                });
            }
            if (url.endsWith('/api/auth/client/refresh')) {
                refreshCalls++;
                await new Promise(resolve => setTimeout(resolve, 30));
                return jsonResp({
                    token: 'jwt-refreshed-secret-2222',
                    refreshToken: 'refresh-rotated-secret-2222',
                    expiresAt: new Date(nowMs + 3_600_000).toISOString(),
                    sessionId: 'sess-1',
                    clientId: 'etlsql-vscode',
                    user: { id: 7, username: 'alice', email: 'alice@example.com', roles: ['Publisher'] },
                    tenantId: 'tenant-alpha',
                    audience: 'etl-sql-portal-native-api',
                    scopes: ['studio.read', 'studio.run', 'studio.save']
                });
            }
            if (url.endsWith('/api/auth/client/logout')) {
                expect(init?.method).toBe('POST');
                return new Response(null, { status: 204 });
            }
            return new Response(null, { status: 404 });
        });

        const logs: string[] = [];
        const manager = new HostedSessionManager({
            fetchImpl: fetchMock as unknown as typeof fetch,
            now: () => nowMs,
            log: msg => logs.push(msg),
            openBrowser: async url => {
                const parsed = new URL(url);
                const redirectUri = parsed.searchParams.get('redirect_uri')!;
                const state = parsed.searchParams.get('state')!;
                void fetch(`${redirectUri}?code=code-123&state=${encodeURIComponent(state)}`);
                return true;
            }
        });

        // Defaults to Local (Offline) on startup.
        expect(manager.getTarget()).toEqual({ kind: 'local' });
        expect(manager.formatStatusBarLabel()).toBe('ETL-SQL: Local (Offline)');

        await manager.signIn(server);
        expect(manager.isSignedIn()).toBe(true);
        expect(manager.formatStatusBarLabel()).toContain('tenant-alpha');
        expect(manager.formatStatusBarLabel()).toContain('alice');

        // 5 concurrent ensureValidAccessToken calls trigger exactly 1 refresh!
        const tokens = await Promise.all([
            manager.ensureValidAccessToken(),
            manager.ensureValidAccessToken(),
            manager.ensureValidAccessToken(),
            manager.ensureValidAccessToken(),
            manager.ensureValidAccessToken()
        ]);
        expect(refreshCalls).toBe(1);
        expect(tokens).toEqual(Array(5).fill('jwt-refreshed-secret-2222'));

        // Disconnect clears in-memory session and returns to Local (Offline).
        await manager.disconnect();
        expect(manager.getTarget()).toEqual({ kind: 'local' });
        expect(manager.getSession()).toBeNull();
        expect(manager.formatStatusBarLabel()).toBe('ETL-SQL: Local (Offline)');

        // Zero secrets ever appeared in logs.
        const joinedLogs = logs.join('\n');
        expect(joinedLogs).not.toContain('jwt-initial-secret-1111');
        expect(joinedLogs).not.toContain('refresh-initial-secret-1111');
        expect(joinedLogs).not.toContain('jwt-refreshed-secret-2222');
        expect(joinedLogs).not.toContain('refresh-rotated-secret-2222');
    });
});

describe('HostedExecutionController', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('blocks Run Selection while Hosted and handles dirty buffer save conflict without losing buffer', async () => {
        const results: unknown[] = [];
        const sessionManager = createSignedInMockManager({
            fetchImpl: vi.fn(async (input: string | URL | Request) => {
                const url = String(input);
                if (url.endsWith('/api/studio/reports/42/summary')) {
                    return jsonResp({
                        id: 42,
                        folderId: 1,
                        folderPath: '/Finance',
                        name: 'FinanceCheck.etlsql',
                        updatedAt: new Date().toISOString(),
                        version: 3,
                        kind: 'Pipeline'
                    });
                }
                if (url.endsWith('/api/designer/save')) {
                    return jsonResp({
                        error: 'The resource changed after it was read. Refresh it and retry.',
                        current: { id: 42, version: 4 }
                    }, 409);
                }
                return new Response(null, { status: 404 });
            }) as unknown as typeof fetch
        });

        await sessionManager.signIn({ alias: 'dev', label: 'Dev', origin: 'http://127.0.0.1:5001' });

        vi.mocked(vscode.workspace.getConfiguration).mockReturnValue({
            get: vi.fn((key: string) => key === 'hosted.pipelineId' ? 42 : undefined),
            inspect: vi.fn(() => ({ globalValue: [] })),
            update: vi.fn()
        } as unknown as vscode.WorkspaceConfiguration);

        vi.mocked(vscode.window.showWarningMessage).mockResolvedValue('Save to Portal & Run' as never);

        const controller = new HostedExecutionController({
            sessions: sessionManager,
            postResults: msg => results.push(msg),
            sleep: async () => {}
        });

        controller.handleRunSelectionUnavailable();
        expect(vscode.window.showInformationMessage).toHaveBeenCalledWith(
            expect.stringContaining('Run Selection is unavailable for hosted execution')
        );

        const editor = {
            document: {
                uri: vscode.Uri.file('/workspace/FinanceCheck.etlsql'),
                fileName: '/workspace/FinanceCheck.etlsql',
                isDirty: true,
                getText: () => 'SELECT 1;'
            }
        } as unknown as vscode.TextEditor;

        await controller.runHostedScript(editor);
        expect(vscode.window.showErrorMessage).toHaveBeenCalledWith(
            expect.stringContaining('Save Conflict')
        );
        expect(controller.getActiveRun()).toBeNull();
        expect(editor.document.getText()).toBe('SELECT 1;');
    });

    it('keeps submission_outcome_unknown unconfirmed and verifies host/tenant/user before resuming after re-sign-in', async () => {
        const results: unknown[] = [];
        let byRequestCalls = 0;

        const fetchMock = vi.fn(async (input: string | URL | Request) => {
            const url = String(input);
            if (url.endsWith('/api/studio/pipelines/42/runs')) {
                throw new TypeError('socket hang up');
            }
            if (url.includes('/runs/by-request/')) {
                byRequestCalls++;
                return jsonResp({
                    code: 'submission_outcome_unknown',
                    error: 'Portal could not confirm whether the Orchestrator accepted submission.'
                }, 409);
            }
            return new Response(null, { status: 404 });
        });

        const sessionManager = createSignedInMockManager({
            fetchImpl: fetchMock as unknown as typeof fetch
        });
        await sessionManager.signIn({ alias: 'dev', label: 'Dev', origin: 'http://127.0.0.1:5001' });

        // First prompt: Check Status Again; second prompt: Cancel (leaves pending unconfirmed)
        vi.mocked(vscode.window.showWarningMessage)
            .mockResolvedValueOnce('Check Status Again' as never)
            .mockResolvedValueOnce('Cancel' as never);

        const controller = new HostedExecutionController({
            sessions: sessionManager,
            postResults: msg => results.push(msg),
            sleep: async () => {}
        });

        const pending = {
            idempotencyKey: '11111111-1111-4111-8111-111111111111',
            origin: 'http://127.0.0.1:5001',
            tenantId: 'tenant-alpha',
            userId: 7,
            username: 'alice',
            pipelineId: 42,
            version: 1,
            jobName: 'PIPELINE:42',
            status: 'unconfirmed' as const,
            createdAtMs: Date.now()
        };

        await controller.submitWithRecovery(pending);
        expect(byRequestCalls).toBe(2);
        expect(controller.getPendingSubmission()?.status).toBe('unknown');
        expect(results.some(r => JSON.stringify(r).includes('Submission unconfirmed'))).toBe(true);

        // Attempting recovery when signed in as a different user refuses automatically.
        const differentUserPending = { ...pending, userId: 999, username: 'bob' };
        await controller.recoverPendingSubmission(differentUserPending);
        expect(vscode.window.showWarningMessage).toHaveBeenLastCalledWith(
            expect.stringContaining('does not match original host, tenant, and user')
        );
    });
});

function createSignedInMockManager(overrides: { fetchImpl: typeof fetch }): HostedSessionManager {
    const wrappedFetch = async (input: string | URL | Request, init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith('/api/auth/providers')) {
            return jsonResp({ local: false, oidcEnabled: true, oidcLoginUrl: '/api/auth/oidc/login' });
        }
        if (url.endsWith('/api/auth/client/exchange')) {
            return jsonResp({
                token: 'jwt-test-token-abcdef1234',
                refreshToken: 'refresh-test-token-abcdef1234',
                expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
                sessionId: 'sess-mock',
                clientId: 'etlsql-vscode',
                user: { id: 7, username: 'alice', email: 'alice@example.com', roles: ['Publisher'] },
                tenantId: 'tenant-alpha',
                audience: 'etl-sql-portal-native-api',
                scopes: ['studio.read', 'studio.run', 'studio.save']
            });
        }
        if (url.endsWith('/api/studio/session')) {
            return jsonResp({
                mode: 'CatalogOnly',
                capabilities: ['StudioAccess', 'ScriptRead', 'ScriptRun', 'ScriptSave'],
                sourceControlEnabled: false,
                draftRecovery: true
            });
        }
        if (url.endsWith('/api/auth/client/logout')) {
            return new Response(null, { status: 204 });
        }
        return overrides.fetchImpl(input, init);
    };

    return new HostedSessionManager({
        fetchImpl: wrappedFetch as unknown as typeof fetch,
        openBrowser: async url => {
            const parsed = new URL(url);
            const redirectUri = parsed.searchParams.get('redirect_uri')!;
            const state = parsed.searchParams.get('state')!;
            void fetch(`${redirectUri}?code=mock-code&state=${encodeURIComponent(state)}`);
            return true;
        }
    });
}

function jsonResp(body: unknown, status: number = 200): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' }
    });
}
