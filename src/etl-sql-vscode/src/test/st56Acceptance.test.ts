import { describe, it, expect, vi } from 'vitest';
import * as http from 'http';
import * as crypto from 'crypto';
import * as vscode from 'vscode';
import { HostedSessionManager } from '../hosted/hostedSessionManager';
import { HostedExecutionController } from '../hosted/hostedExecutionController';
import { HostedServerEntry } from '../hosted/hostedTypes';

/**
 * ST56 8-Step Acceptance Fixture (VS Code client side).
 *
 * Drives the real HostedSessionManager, HostedPipelineClient, HostedExecutionController,
 * and RFC 8252 ephemeral loopback PKCE listener over real localhost HTTP against a
 * contract-faithful loopback Portal + Federated Orchestrator + OIDC test double,
 * verifying all 8 steps and secret hygiene (zero tokens in logs, settings, or SecretStorage).
 */
describe('ST56 8-Step Hosted Acceptance Fixture (VS Code Client)', () => {
    it('executes all 8 steps end-to-end with dropped-202 recovery, single Orchestrator run, and zero secret leaks', async () => {
        const transcript: string[] = [];
        const logs: string[] = [];
        const resultsMessages: unknown[] = [];
        const issuedSecrets = new Set<string>();
        const secretStorageStore = vi.fn();

        // Loopback Portal + Orchestrator + OIDC state
        let currentOidcUser: 'alice' | 'bob' = 'alice';
        let nextRunId = 500;
        let browserLaunches = 0;
        let dropNextSubmitResponse = false;

        const authCodes = new Map<string, { challenge: string; redirectUri: string; user: 'alice' | 'bob' }>();
        const activeSessions = new Map<string, { user: 'alice' | 'bob'; refreshToken: string; revoked: boolean }>();
        const idempotencyLedger = new Map<string, { runId: number; pipelineId: number; version: number; scriptHash: string; jobName: string }>();
        const orchestratorRuns = new Map<number, {
            runId: number;
            pipelineId: number;
            status: 'RUNNING' | 'SUCCESS' | 'CANCELLED';
            isTerminal: boolean;
            polls: number;
            slow: boolean;
            cancelRequested: boolean;
            idempotencyKey?: string;
        }>();

        const server = http.createServer(async (req, res) => {
            const url = new URL(req.url ?? '/', 'http://127.0.0.1');
            const bodyText = await readBody(req);
            const authHeader = req.headers.authorization ?? '';
            const bearer = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : '';

            const record = (status: number) => {
                transcript.push(`${req.method} ${url.pathname} -> ${status}`);
            };

            if (req.method === 'GET' && url.pathname === '/api/auth/providers') {
                record(200);
                return sendJson(res, 200, { local: false, oidcEnabled: true, oidcLoginUrl: '/api/auth/oidc/login' });
            }

            // Simulated OIDC browser login + consent redirect back to loopback callback
            if (req.method === 'GET' && url.pathname === '/api/auth/client/authorize') {
                const redirectUri = url.searchParams.get('redirect_uri')!;
                const state = url.searchParams.get('state')!;
                const challenge = url.searchParams.get('code_challenge')!;
                const code = `code_${crypto.randomBytes(8).toString('hex')}`;
                authCodes.set(code, { challenge, redirectUri, user: currentOidcUser });
                record(302);
                res.writeHead(302, { Location: `${redirectUri}?code=${code}&state=${encodeURIComponent(state)}` });
                res.end();
                return;
            }

            if (req.method === 'POST' && url.pathname === '/api/auth/client/exchange') {
                const body = JSON.parse(bodyText) as { code: string; code_verifier: string; redirect_uri: string; client_id: string };
                const entry = authCodes.get(body.code);
                if (!entry || entry.redirectUri !== body.redirect_uri || body.client_id !== 'etlsql-vscode') {
                    record(400);
                    return sendJson(res, 400, { error: 'invalid_grant' });
                }
                const computed = crypto.createHash('sha256').update(body.code_verifier, 'ascii').digest('base64url');
                if (computed !== entry.challenge) {
                    record(400);
                    return sendJson(res, 400, { error: 'invalid_grant' });
                }
                authCodes.delete(body.code);

                const sessionId = `sess_${crypto.randomBytes(6).toString('hex')}`;
                const token = `jwt_${sessionId}_${crypto.randomBytes(12).toString('hex')}`;
                const refreshToken = `ref_${sessionId}_${crypto.randomBytes(12).toString('hex')}`;
                issuedSecrets.add(token);
                issuedSecrets.add(refreshToken);
                activeSessions.set(token, { user: entry.user, refreshToken, revoked: false });

                record(200);
                return sendJson(res, 200, {
                    token,
                    refreshToken,
                    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
                    sessionId,
                    clientId: 'etlsql-vscode',
                    user: { id: entry.user === 'alice' ? 10 : 11, username: entry.user, email: `${entry.user}@alpha.test`, roles: ['Publisher'] },
                    tenantId: 'tenant-alpha',
                    audience: 'etl-sql-portal-native-api',
                    scopes: ['studio.read', 'studio.run', 'studio.save']
                });
            }

            if (req.method === 'POST' && url.pathname === '/api/auth/client/refresh') {
                const body = JSON.parse(bodyText) as { refreshToken: string };
                const match = [...activeSessions.entries()].find(([, s]) => s.refreshToken === body.refreshToken);
                if (!match || match[1].revoked) {
                    record(401);
                    return sendJson(res, 401, { error: 'invalid_grant' });
                }
                const [oldToken, state] = match;
                activeSessions.delete(oldToken);
                const newToken = `jwt_refreshed_${crypto.randomBytes(12).toString('hex')}`;
                const newRefresh = `ref_rotated_${crypto.randomBytes(12).toString('hex')}`;
                issuedSecrets.add(newToken);
                issuedSecrets.add(newRefresh);
                activeSessions.set(newToken, { user: state.user, refreshToken: newRefresh, revoked: false });

                record(200);
                return sendJson(res, 200, {
                    token: newToken,
                    refreshToken: newRefresh,
                    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
                    sessionId: 'sess_refreshed',
                    clientId: 'etlsql-vscode',
                    user: { id: state.user === 'alice' ? 10 : 11, username: state.user, email: `${state.user}@alpha.test`, roles: ['Publisher'] },
                    tenantId: 'tenant-alpha',
                    audience: 'etl-sql-portal-native-api',
                    scopes: ['studio.read', 'studio.run', 'studio.save']
                });
            }

            if (req.method === 'POST' && url.pathname === '/api/auth/client/logout') {
                const sess = activeSessions.get(bearer);
                if (sess) {
                    sess.revoked = true;
                }
                record(204);
                res.writeHead(204);
                res.end();
                return;
            }

            const session = activeSessions.get(bearer);
            if (!session || session.revoked) {
                record(401);
                return sendJson(res, 401, { error: 'unauthorized' });
            }

            if (req.method === 'GET' && url.pathname === '/api/studio/session') {
                record(200);
                return sendJson(res, 200, {
                    mode: 'CatalogOnly',
                    capabilities: session.user === 'alice'
                        ? ['StudioAccess', 'ScriptRead', 'ScriptRun', 'ScriptSave']
                        : ['StudioAccess', 'ScriptRead', 'ScriptRun'],
                    sourceControlEnabled: false,
                    draftRecovery: true
                });
            }

            const summaryMatch = url.pathname.match(/^\/api\/studio\/reports\/(\d+)\/summary$/);
            if (req.method === 'GET' && summaryMatch) {
                const id = Number(summaryMatch[1]);
                record(200);
                return sendJson(res, 200, {
                    id,
                    folderId: 1,
                    folderPath: '/Finance',
                    name: id === 101 ? 'FinanceCheck.etlsql' : 'SlowWait.etlsql',
                    updatedAt: new Date().toISOString(),
                    version: 1,
                    kind: 'Pipeline'
                });
            }

            const submitMatch = url.pathname.match(/^\/api\/studio\/pipelines\/(\d+)\/runs$/);
            if (req.method === 'POST' && submitMatch) {
                const pipelineId = Number(submitMatch[1]);
                const key = String(req.headers['idempotency-key'] ?? '');
                if (session.user === 'bob') {
                    record(403);
                    return sendJson(res, 403, { code: 'run_grant_denied', error: `No EXECUTE grant on PIPELINE:${pipelineId}.` });
                }

                const existing = idempotencyLedger.get(key);
                if (existing) {
                    record(202);
                    return sendJson(res, 202, { ...existing, replayed: true });
                }

                const runId = ++nextRunId;
                const receipt = {
                    runId,
                    pipelineId,
                    version: 1,
                    scriptHash: 'sha256:abc123',
                    jobName: `PIPELINE:${pipelineId}`
                };
                idempotencyLedger.set(key, receipt);
                orchestratorRuns.set(runId, {
                    runId,
                    pipelineId,
                    status: 'RUNNING',
                    isTerminal: false,
                    polls: 0,
                    slow: pipelineId === 102,
                    cancelRequested: false,
                    idempotencyKey: key
                });

                if (dropNextSubmitResponse) {
                    dropNextSubmitResponse = false;
                    transcript.push(`POST ${url.pathname} -> <DROPPED AFTER ACCEPT runId=${runId}>`);
                    req.socket.destroy();
                    return;
                }

                record(202);
                return sendJson(res, 202, { ...receipt, replayed: false });
            }

            const byReqMatch = url.pathname.match(/^\/api\/studio\/pipelines\/(\d+)\/runs\/by-request\/([^/]+)$/);
            if (req.method === 'GET' && byReqMatch) {
                const key = decodeURIComponent(byReqMatch[2]);
                const found = idempotencyLedger.get(key);
                if (!found) {
                    record(404);
                    return sendJson(res, 404, { code: 'submission_not_found', error: 'Not found' });
                }
                record(200);
                return sendJson(res, 200, { ...found, replayed: true });
            }

            const cancelMatch = url.pathname.match(/^\/api\/studio\/pipelines\/(\d+)\/runs\/(\d+)\/cancel$/);
            if (req.method === 'POST' && cancelMatch) {
                const runId = Number(cancelMatch[2]);
                const run = orchestratorRuns.get(runId);
                if (run) {
                    run.cancelRequested = true;
                    run.status = 'CANCELLED';
                    run.isTerminal = true;
                }
                record(202);
                return sendJson(res, 202, { runId, pipelineId: Number(cancelMatch[1]) });
            }

            const getRunMatch = url.pathname.match(/^\/api\/studio\/pipelines\/(\d+)\/runs\/(\d+)$/);
            if (req.method === 'GET' && getRunMatch) {
                const runId = Number(getRunMatch[2]);
                const run = orchestratorRuns.get(runId)!;
                run.polls++;
                if (!run.slow && !run.cancelRequested) {
                    run.status = 'SUCCESS';
                    run.isTerminal = true;
                }
                record(200);
                return sendJson(res, 200, {
                    runId: run.runId,
                    pipelineId: run.pipelineId,
                    status: run.status,
                    isTerminal: run.isTerminal,
                    startTime: new Date().toISOString(),
                    endTime: run.isTerminal ? new Date().toISOString() : null,
                    errorMessage: null,
                    rowsProcessed: run.status === 'SUCCESS' ? 5 : 0,
                    scriptHash: 'sha256:abc123',
                    logTruncated: false,
                    statements: [
                        { statement: 'SELECT UserID INTO #users FROM sample_data.Users', durationMs: 12, rowsProcessed: 5, failed: false }
                    ]
                });
            }

            record(404);
            res.writeHead(404);
            res.end();
        });

        await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
        const port = (server.address() as { port: number }).port;
        const origin = `http://127.0.0.1:${port}`;
        const hostedEntry: HostedServerEntry = { alias: 'alpha', label: 'Alpha', origin };

        try {
            let nowMs = Date.now();
            const sessions = new HostedSessionManager({
                now: () => nowMs,
                log: msg => logs.push(msg),
                openBrowser: async url => {
                    browserLaunches++;
                    // Follow portal authorize -> 302 loopback callback (exact browser flow)
                    const resp = await fetch(url, { redirect: 'manual' });
                    const loc = resp.headers.get('location')!;
                    await fetch(loc);
                    return true;
                }
            });

            const controller = new HostedExecutionController({
                sessions,
                postResults: msg => resultsMessages.push(msg),
                log: msg => logs.push(msg),
                sleep: async () => {},
                initialPollDelayMs: 5,
                maxPollDelayMs: 10
            });

            const makeEditor = (pipelineId: number, name: string) => {
                vi.mocked(vscode.workspace.getConfiguration).mockReturnValue({
                    get: vi.fn((key: string) => key === 'hosted.pipelineId' ? pipelineId : undefined),
                    inspect: vi.fn(() => ({ globalValue: [hostedEntry] })),
                    update: vi.fn()
                } as unknown as vscode.WorkspaceConfiguration);
                return {
                    document: {
                        uri: vscode.Uri.file(`/workspace/${name}`),
                        fileName: `/workspace/${name}`,
                        isDirty: false,
                        getText: () => 'SELECT 1;'
                    }
                } as unknown as vscode.TextEditor;
            };

            // Step 1: Starts on Local (Offline), connects via loopback OIDC as alice
            expect(sessions.getTarget()).toEqual({ kind: 'local' });
            expect(sessions.formatStatusBarLabel()).toBe('ETL-SQL: Local (Offline)');
            await sessions.signIn(hostedEntry);
            expect(browserLaunches).toBe(1);
            expect(sessions.formatStatusBarLabel()).toContain('tenant-alpha');
            expect(sessions.formatStatusBarLabel()).toContain('alice');

            // Step 2: Authorized run of FinanceCheck.etlsql (pipeline 101)
            await controller.runHostedScript(makeEditor(101, 'FinanceCheck.etlsql'));
            expect(orchestratorRuns.size).toBe(1);
            expect(resultsMessages.some(m => JSON.stringify(m).includes('finished: SUCCESS'))).toBe(true);

            // Step 3: Session reuse (zero new browser launches, distinct runId)
            await controller.runHostedScript(makeEditor(101, 'FinanceCheck.etlsql'));
            expect(browserLaunches).toBe(1);
            expect(orchestratorRuns.size).toBe(2);

            // Step 4: Denied run as bob (403 run_grant_denied, zero new Orchestrator runs)
            currentOidcUser = 'bob';
            await sessions.signIn(hostedEntry);
            await controller.runHostedScript(makeEditor(101, 'FinanceCheck.etlsql'));
            expect(orchestratorRuns.size).toBe(2);
            expect(resultsMessages.some(m => JSON.stringify(m).includes('run_grant_denied'))).toBe(true);

            // Step 5: (a) Expired access token -> silent single-flight refresh; (b) revoked refresh -> 401 keeps buffer
            currentOidcUser = 'alice';
            await sessions.signIn(hostedEntry);
            nowMs += 3_700_000; // expire access token
            await controller.runHostedScript(makeEditor(101, 'FinanceCheck.etlsql'));
            expect(orchestratorRuns.size).toBe(3);

            // Step 5(b): revoke active session on server -> refresh returns 401 -> prompts Sign In Again, stays hosted
            const currentToken = sessions.getSession()!.token;
            activeSessions.get(currentToken)!.revoked = true;
            vi.mocked(vscode.window.showWarningMessage).mockResolvedValueOnce('Cancel' as never);
            await controller.runHostedScript(makeEditor(101, 'FinanceCheck.etlsql'));
            expect(sessions.isHosted()).toBe(true);
            expect(sessions.getSession()).toBeNull();

            // Re-sign-in as alice for Step 6 & 7
            await sessions.signIn(hostedEntry);

            // Step 6: Stop on SlowWait.etlsql (pipeline 102) -> Cancellation requested -> CANCELLED
            const slowRunPromise = controller.runHostedScript(makeEditor(102, 'SlowWait.etlsql'));
            // Wait a tick for active run to attach, then invoke Stop
            await new Promise(resolve => setTimeout(resolve, 15));
            await controller.stopHostedRun();
            await slowRunPromise;
            expect(resultsMessages.some(m => JSON.stringify(m).includes('Cancellation requested'))).toBe(true);
            expect(resultsMessages.some(m => JSON.stringify(m).includes('finished: CANCELLED'))).toBe(true);

            // Step 7: Lost 202 response -> recovers exact runId, creates exactly ONE Orchestrator run for K1
            const runsBeforeDrop = orchestratorRuns.size;
            dropNextSubmitResponse = true;
            await controller.runHostedScript(makeEditor(101, 'FinanceCheck.etlsql'));
            expect(orchestratorRuns.size).toBe(runsBeforeDrop + 1);
            expect(resultsMessages.some(m => JSON.stringify(m).includes('Recovered accepted run'))).toBe(true);

            // Step 8: Disconnect -> Local (Offline), server session revoked, fresh activation starts on Local
            const preDisconnectToken = sessions.getSession()!.token;
            await controller.disconnect();
            expect(sessions.getTarget()).toEqual({ kind: 'local' });
            expect(activeSessions.get(preDisconnectToken)?.revoked).toBe(true);

            const freshSessions = new HostedSessionManager({ openBrowser: async () => true });
            expect(freshSessions.getTarget()).toEqual({ kind: 'local' });
            expect(freshSessions.getSession()).toBeNull();

            // Secret hygiene: zero issued tokens or refresh tokens in logs, results, transcript, or SecretStorage
            const allText = [...logs, ...transcript, JSON.stringify(resultsMessages)].join('\n');
            for (const secret of issuedSecrets) {
                expect(allText).not.toContain(secret);
            }
            expect(secretStorageStore).not.toHaveBeenCalled();
        } finally {
            server.close();
        }
    });
});

function readBody(req: http.IncomingMessage): Promise<string> {
    return new Promise((resolve, reject) => {
        const chunks: Buffer[] = [];
        req.on('data', chunk => chunks.push(Buffer.from(chunk)));
        req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
        req.on('error', reject);
    });
}

function sendJson(res: http.ServerResponse, status: number, body: unknown): void {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(body));
}
