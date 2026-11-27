import * as assert from 'assert';
import { driveRealPortalOidcConsentFlow } from './st56BrowserFlowHelper';

/**
 * Live Node 22 acceptance runner executed by St56HostedAcceptanceFixtureTests.
 * Loads the REAL compiled VS Code hosted modules (HostedSessionManager, HostedPipelineClient,
 * HostedExecutionController, loopbackPkceAuth) and drives all 8 ST56 acceptance steps +
 * dropped-202 recovery + revoked-session recovery + Stop against a real loopback Kestrel Portal
 * and real Federated Orchestrator.
 */

interface WarningHandler {
    (message: string, ...items: string[]): string | undefined;
}

let nextWarningChoices: Array<string | undefined> = [];
let activePipelineId = 0;
let activeServerOrigin = '';
const statusBarItems: Array<{ text: string; tooltip: string; command: string }> = [];
const infoMessages: string[] = [];
const errorMessages: string[] = [];
const warningMessages: string[] = [];

const vscodeStub = {
    StatusBarAlignment: { Left: 1, Right: 2 },
    ConfigurationTarget: { Global: 1, Workspace: 2, WorkspaceFolder: 3 },
    env: {
        remoteName: undefined as string | undefined,
        openExternal: async () => true
    },
    Uri: {
        file: (p: string) => ({ fsPath: p, toString: () => `file://${p}` }),
        parse: (s: string) => ({ fsPath: s, toString: () => s })
    },
    commands: {
        executeCommand: async () => undefined
    },
    workspace: {
        getConfiguration: () => ({
            get: (key: string) => {
                if (key === 'hosted.pipelineId') {
                    return activePipelineId;
                }
                if (key === 'hosted.defaultServerAlias') {
                    return 'live';
                }
                if (key === 'hosted.pipelineLinks') {
                    return {};
                }
                return undefined;
            },
            inspect: (key: string) => {
                if (key === 'hosted.servers') {
                    return {
                        globalValue: [{ alias: 'live', label: 'Live Portal', origin: activeServerOrigin }]
                    };
                }
                return {};
            },
            update: async () => undefined
        }),
        getWorkspaceFolder: () => undefined
    },
    window: {
        createStatusBarItem: () => {
            const item = {
                text: '',
                tooltip: '',
                command: '',
                show: () => {},
                hide: () => {},
                dispose: () => {}
            };
            statusBarItems.push(item);
            return item;
        },
        showInformationMessage: async (msg: string) => {
            infoMessages.push(msg);
            return undefined;
        },
        showErrorMessage: async (msg: string) => {
            errorMessages.push(msg);
            return undefined;
        },
        showWarningMessage: async (msg: string, ...rest: unknown[]) => {
            warningMessages.push(msg);
            const items = rest.filter((x): x is string => typeof x === 'string');
            if (nextWarningChoices.length > 0) {
                return nextWarningChoices.shift();
            }
            return items[0];
        },
        showQuickPick: async () => undefined,
        showInputBox: async () => undefined
    }
};

// Intercept `require('vscode')` before loading the compiled extension modules.
// eslint-disable-next-line @typescript-eslint/no-require-imports
const moduleLoader = require('module') as {
    _load: (request: string, parent: NodeModule | null, isMain: boolean) => unknown;
};
const originalLoad = moduleLoader._load;
moduleLoader._load = function (request: string, parent: NodeModule | null, isMain: boolean) {
    if (request === 'vscode') {
        return vscodeStub;
    }
    return originalLoad.call(this, request, parent, isMain);
};

async function controlPost(controlUrl: string, path: string, body?: unknown): Promise<Record<string, unknown>> {
    const resp = await fetch(`${controlUrl}${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: body ? JSON.stringify(body) : '{}'
    });
    assert.strictEqual(resp.status, 200, `Control ${path} failed with ${resp.status}`);
    return await resp.json() as Record<string, unknown>;
}

async function main() {
    const portalOrigin = process.env.ETLSQL_LIVE_PORTAL_URL;
    const controlUrl = process.env.ETLSQL_LIVE_CONTROL_URL;
    const financePipelineId = Number(process.env.ETLSQL_LIVE_FINANCE_PIPELINE_ID);
    const slowPipelineId = Number(process.env.ETLSQL_LIVE_SLOW_PIPELINE_ID);

    assert.ok(portalOrigin, 'ETLSQL_LIVE_PORTAL_URL is required');
    assert.ok(controlUrl, 'ETLSQL_LIVE_CONTROL_URL is required');
    assert.ok(financePipelineId > 0, 'ETLSQL_LIVE_FINANCE_PIPELINE_ID is required');
    assert.ok(slowPipelineId > 0, 'ETLSQL_LIVE_SLOW_PIPELINE_ID is required');
    activeServerOrigin = portalOrigin;

    const { HostedSessionManager } = await import('../hosted/hostedSessionManager');
    const { HostedExecutionController } = await import('../hosted/hostedExecutionController');

    const logs: string[] = [];
    const results: unknown[] = [];
    const trace: string[] = [];
    const sensitiveValues = new Set<string>();
    let browserLaunches = 0;
    let nowMs = Date.now();

    // Wrap fetch only to record a sanitized trace & capture PKCE verifiers/tokens for hygiene checks.
    const tracedFetch: typeof fetch = async (input, init) => {
        const urlStr = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
        const method = init?.method ?? 'GET';
        if (typeof init?.body === 'string' && urlStr.includes('/api/auth/client/exchange')) {
            try {
                const parsed = JSON.parse(init.body) as { code_verifier?: string; code?: string };
                if (parsed.code_verifier) {
                    sensitiveValues.add(parsed.code_verifier);
                }
                if (parsed.code) {
                    sensitiveValues.add(parsed.code);
                }
            } catch {
                // ignore
            }
        }
        const parsedUrl = new URL(urlStr);
        try {
            const resp = await fetch(input, init);
            trace.push(`${method} ${parsedUrl.pathname} -> ${resp.status}`);
            return resp;
        } catch (err) {
            trace.push(`${method} ${parsedUrl.pathname} -> <NETWORK_ERROR>`);
            throw err;
        }
    };

    const hostedServer = { alias: 'live', label: 'Live Portal', origin: portalOrigin };
    const sessions = new HostedSessionManager({
        fetchImpl: tracedFetch,
        now: () => nowMs,
        log: msg => logs.push(msg),
        openBrowser: async url => {
            browserLaunches++;
            return await driveRealPortalOidcConsentFlow(url, {
                onCapturedCode: code => sensitiveValues.add(code)
            });
        }
    });

    const controller = new HostedExecutionController({
        sessions,
        postResults: msg => results.push(msg),
        log: msg => logs.push(msg),
        initialPollDelayMs: 100,
        maxPollDelayMs: 300
    });

    const captureActiveTokens = () => {
        const s = sessions.getSession();
        if (s) {
            sensitiveValues.add(s.token);
            sensitiveValues.add(s.refreshToken);
        }
    };

    const makeEditor = (pipelineId: number, name: string, isDirty: boolean = false, text: string = 'SELECT 1;') => {
        activePipelineId = pipelineId;
        return {
            document: {
                uri: vscodeStub.Uri.file(`C:/workspace/${name}`),
                fileName: `C:/workspace/${name}`,
                isDirty,
                getText: () => text
            }
        } as unknown as import('vscode').TextEditor;
    };

    // ── Step 1: Defaults to Local (Offline), connects via real OIDC + consent + loopback PKCE as alice ──
    assert.strictEqual(sessions.getTarget().kind, 'local');
    assert.strictEqual(statusBarItems[0].text, 'ETL-SQL: Local (Offline)');

    await controlPost(controlUrl, '/set-oidc-user', { user: 'alice' });
    const signedIn = await controller.connectToServer(hostedServer);
    assert.strictEqual(signedIn, true);
    assert.strictEqual(browserLaunches, 1);
    captureActiveTokens();
    assert.ok(statusBarItems[0].text.includes('alice'));

    // ── Step 2: Authorized run of FinanceCheck.etlsql as alice ──
    await controller.runHostedScript(makeEditor(financePipelineId, 'FinanceCheck.etlsql'));
    assert.ok(results.some(r => JSON.stringify(r).includes('finished: SUCCESS')), 'Step 2 should reach SUCCESS');

    // ── Step 3: Session reuse (0 extra browser launches, second distinct run) ──
    await controller.runHostedScript(makeEditor(financePipelineId, 'FinanceCheck.etlsql'));
    assert.strictEqual(browserLaunches, 1, 'Step 3 must reuse in-memory session with zero browser launches');

    // ── Step 4: Denied run as bob (403 run_grant_denied, 0 Orchestrator runs) ──
    const histBeforeBob = Number((await controlPost(controlUrl, '/history-count', { pipelineId: financePipelineId })).count);
    await controlPost(controlUrl, '/set-oidc-user', { user: 'bob' });
    assert.strictEqual(await controller.connectToServer(hostedServer), true);
    captureActiveTokens();
    await controller.runHostedScript(makeEditor(financePipelineId, 'FinanceCheck.etlsql'));
    const runnable = await controller.client.findRunnablePipelines('Finance');
    // The search matches folder paths too, so both pipelines in /Finance are visible.
    assert.deepEqual(runnable.items.map(item => [item.id, item.canAuthor]), [[financePipelineId, false], [slowPipelineId, false]],
        `Bob should see runnable pipelines in /Finance and none in /FinancePrivate. Found: ${JSON.stringify(runnable.items)}`);
    const authorSummary = await sessions.authorizedFetch(`/api/studio/reports/${financePipelineId}/summary`);
    assert.strictEqual(authorSummary.status, 404, 'Execute-only Bob must not see authoring summary');
    const histAfterBob = Number((await controlPost(controlUrl, '/history-count', { pipelineId: financePipelineId })).count);
    assert.strictEqual(histAfterBob, histBeforeBob, 'Bob must not trigger Orchestrator execution');
    assert.ok(
        results.some(r => JSON.stringify(r).includes('run_grant_denied')),
        `Step 4 should report run_grant_denied. errors=${JSON.stringify(errorMessages)} trace=${JSON.stringify(trace.slice(-6))}`
    );

    // ── Step 5: Expired access token -> silent single-flight refresh as alice ──
    await controlPost(controlUrl, '/set-oidc-user', { user: 'alice' });
    assert.strictEqual(await controller.connectToServer(hostedServer), true);
    captureActiveTokens();
    const browserLaunchesBeforeRefresh = browserLaunches;
    nowMs += 3_700_000; // expire access token locally -> triggers single-flight /api/auth/client/refresh
    await controller.runHostedScript(makeEditor(financePipelineId, 'FinanceCheck.etlsql'));
    captureActiveTokens();
    assert.strictEqual(browserLaunches, browserLaunchesBeforeRefresh, 'Silent refresh must not open browser');

    // ── Step 6: Stop through extension command on SlowWait.etlsql -> Cancellation requested -> CANCELLED ──
    const slowRunPromise = controller.runHostedScript(makeEditor(slowPipelineId, 'SlowWait.etlsql'));
    // Wait until active run is attached (max 5s)
    for (let i = 0; i < 100 && !controller.getActiveRun(); i++) {
        await new Promise(r => setTimeout(r, 50));
    }
    assert.ok(controller.getActiveRun(), 'SlowWait run should attach before Stop');
    await controller.stopHostedRun();
    await slowRunPromise;
    assert.ok(results.some(r => JSON.stringify(r).includes('Cancellation requested for run')), 'Step 6 must show Cancellation requested');
    assert.ok(results.some(r => JSON.stringify(r).includes('finished: CANCELLED')), 'Step 6 must wait for terminal CANCELLED');

    // ── Step 7a: Drop 202 response after Portal accepts -> recover exact runId, exactly ONE Orchestrator run ──
    const histBeforeDrop1 = Number((await controlPost(controlUrl, '/history-count', { pipelineId: financePipelineId })).count);
    await controlPost(controlUrl, '/arm-drop', { revokeSessionAfterDrop: false });
    await controller.runHostedScript(makeEditor(financePipelineId, 'FinanceCheck.etlsql'));
    const histAfterDrop1 = Number((await controlPost(controlUrl, '/history-count', { pipelineId: financePipelineId })).count);
    assert.strictEqual(histAfterDrop1, histBeforeDrop1 + 1, 'Dropped 202 recovery must create exactly ONE Orchestrator run');
    assert.ok(results.some(r => JSON.stringify(r).includes('Recovered accepted run')), 'Step 7a must report recovered run');

    // ── Step 7b: Revoke native session during recovery -> preserve dirty buffer + pending key -> resume only on same user ──
    const histBeforeDrop2 = Number((await controlPost(controlUrl, '/history-count', { pipelineId: financePipelineId })).count);
    const dirtyContent = '-- unsaved local edit preserved across expiry\nSELECT 1;';
    const dirtyEditor = makeEditor(financePipelineId, 'FinanceCheck.etlsql', true, dirtyContent);
    await controlPost(controlUrl, '/arm-drop', { revokeSessionAfterDrop: true });
    // Prompts: (1) dirty editor -> Run Last Saved Revision, (2) session expired -> Cancel (keeps pending key!),
    // then sign in as bob (wrong account -> refuses to resume alice's key), then sign back in as alice -> Check Status Again!
    nextWarningChoices = ['Run Last Saved Revision', 'Cancel'];
    await controller.runHostedScript(dirtyEditor);
    assert.strictEqual(dirtyEditor.document.getText(), dirtyContent, 'Dirty editor buffer must be preserved');
    assert.strictEqual(sessions.isHosted(), true, 'Must stay on hosted target (no silent fallback to Local)');
    assert.strictEqual(sessions.getSession(), null, 'Revoked session must be cleared');

    // Sign in as bob first -> must NOT resume alice's pending submission
    await controlPost(controlUrl, '/set-oidc-user', { user: 'bob' });
    assert.strictEqual(await controller.connectToServer(hostedServer), true);
    captureActiveTokens();
    assert.strictEqual(controller.getPendingSubmission(), null, 'Alice pending submission must not be exposed to bob');

    // Sign back in as alice (same host, tenant, account) -> alice's pending key is preserved and resumes!
    await controlPost(controlUrl, '/set-oidc-user', { user: 'alice' });
    assert.strictEqual(await controller.connectToServer(hostedServer), true);
    captureActiveTokens();
    const pendingForAlice = controller.getPendingSubmission();
    assert.ok(pendingForAlice, 'Alice pending submission key must still be preserved after re-sign-in');
    await controller.recoverPendingSubmission(pendingForAlice!);
    const histAfterDrop2 = Number((await controlPost(controlUrl, '/history-count', { pipelineId: financePipelineId })).count);
    assert.strictEqual(histAfterDrop2, histBeforeDrop2 + 1, 'Revoked-session recovery must create exactly ONE Orchestrator run');

    // ── Step 8: Disconnect -> Local (Offline) + revokes alice session on server ──
    await controller.disconnect();
    assert.strictEqual(sessions.getTarget().kind, 'local');
    assert.strictEqual(statusBarItems[0].text, 'ETL-SQL: Local (Offline)');

    // Fresh startup defaults to Local (Offline)
    const freshSessions = new HostedSessionManager({ openBrowser: async () => true });
    assert.strictEqual(freshSessions.getTarget().kind, 'local');
    assert.strictEqual(freshSessions.getSession(), null);

    // ── Secret Hygiene Check: zero tokens, refresh tokens, auth codes, or PKCE verifiers in logs/results/trace ──
    assert.ok(sensitiveValues.size >= 8, `Expected captured secrets to audit, got ${sensitiveValues.size}`);
    const combinedOutput = [
        ...logs,
        ...trace,
        ...infoMessages,
        ...warningMessages,
        ...errorMessages,
        JSON.stringify(results)
    ].join('\n');
    for (const secret of sensitiveValues) {
        assert.ok(!combinedOutput.includes(secret), `Secret leaked in output!`);
    }

    console.log('ST56_LIVE_TRACE_BEGIN');
    for (const line of trace) {
        console.log(line);
    }
    console.log('ST56_LIVE_TRACE_END');
    console.log(`ST56_LIVE_SECRETS_CHECKED=${sensitiveValues.size}`);
}

main().catch(err => {
    console.error(err);
    process.exit(1);
});
