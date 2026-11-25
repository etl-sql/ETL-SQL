import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as vscode from 'vscode';
import { HostedSessionManager } from '../hosted/hostedSessionManager';
import { HostedPipelineClient } from '../hosted/hostedPipelineClient';
import { HostedJobsTreeProvider, HostedTreeNode } from '../hosted/hostedJobsTreeProvider';
import { HostedJobItemDto, HostedJobRecentRunDto, HostedJobRunDetailDto, resolveTrustedPortalUrl } from '../hosted/hostedTypes';

function jsonResponse(status: number, body: unknown): Response {
    return new Response(JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' }
    });
}

async function createSignedInManager(
    fetchImpl: typeof fetch,
    opts: { origin: string; tenantId: string; userId: number; username: string; sessionId: string }
): Promise<HostedSessionManager> {
    const manager = new HostedSessionManager({
        fetchImpl,
        openBrowser: async () => true,
        startLoopbackListener: async (expectedState) => ({
            port: 49999,
            redirectUri: 'http://127.0.0.1:49999/callback',
            waitForCallback: async () => ({ code: `code-${opts.userId}`, state: expectedState }),
            cancel: () => {}
        })
    });
    await manager.signIn({ alias: 'dev', label: 'Dev Portal', origin: opts.origin });
    return manager;
}

describe('HostedJobsTreeProvider (ST59)', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('shows offline placeholder on Local (Offline) with zero network calls', async () => {
        const fetchMock = vi.fn<typeof fetch>();
        const sessions = new HostedSessionManager({
            fetchImpl: fetchMock,
            openBrowser: async () => true
        });
        const tree = new HostedJobsTreeProvider({ sessions });

        const roots = await tree.getChildren();
        expect(roots).toHaveLength(1);
        expect(roots[0].kind).toBe('placeholder');
        expect(fetchMock).not.toHaveBeenCalled();
        tree.dispose();
    });

    it('binds Open in Portal strictly to the connected trusted origin', () => {
        expect(resolveTrustedPortalUrl('https://portal.corp.example', '/orchestrator.html?job=Nightly')).toBe(
            'https://portal.corp.example/orchestrator.html?job=Nightly'
        );
        expect(resolveTrustedPortalUrl('https://portal.corp.example', 'https://evil.example/phish')).toBeNull();
        expect(resolveTrustedPortalUrl('https://portal.corp.example', '//evil.example/phish')).toBeNull();
        expect(resolveTrustedPortalUrl('https://portal.corp.example', '/\\evil.example/phish')).toBeNull();
    });

    it('renders jobs, saved pipelines, recent runs, statement logs, and cancels the exact selected run', async () => {
        const cancelledUrls: string[] = [];
        const requestedOffsets: string[] = [];
        const runSavedPipeline = vi.fn(async () => {});
        const jobs: HostedJobItemDto[] = [
            {
                kind: 'pipeline',
                jobName: 'PIPELINE:42',
                displayName: 'FinanceCheck.etlsql',
                pipelineId: 42,
                pipelineVersion: 3,
                folderPath: '/Finance',
                isEnabled: true,
                lastStatus: 'RUNNING',
                lastRunId: 501,
                activeRunId: 501,
                nextScheduledRunAt: '2026-11-26T02:00:00Z',
                canRun: true,
                canStop: true,
                portalPath: '/studio.html?reportId=42'
            },
            {
                kind: 'job',
                jobName: 'ReadOnlySummary',
                displayName: 'Read Only Summary',
                isEnabled: true,
                lastStatus: 'SUCCESS',
                lastRunId: 400,
                activeRunId: null,
                nextScheduledRunAt: null,
                canRun: false,
                canStop: false,
                portalPath: '/orchestrator.html?job=ReadOnlySummary'
            }
        ];
        const runs: HostedJobRecentRunDto[] = [
            {
                runId: 501,
                jobName: 'PIPELINE:42',
                status: 'RUNNING',
                isTerminal: false,
                startTime: '2026-11-25T10:00:00Z',
                rowsProcessed: 12,
                canStop: true,
                portalPath: '/orchestrator.html?job=PIPELINE%3A42&run=501'
            },
            {
                runId: 500,
                jobName: 'PIPELINE:42',
                status: 'SUCCESS',
                isTerminal: true,
                startTime: '2026-11-25T09:00:00Z',
                endTime: '2026-11-25T09:00:02Z',
                durationMs: 2000,
                rowsProcessed: 40,
                canStop: false,
                portalPath: '/orchestrator.html?job=PIPELINE%3A42&run=500'
            }
        ];
        const detail: HostedJobRunDetailDto = {
            runId: 500,
            jobName: 'PIPELINE:42',
            status: 'SUCCESS',
            isTerminal: true,
            startTime: '2026-11-25T09:00:00Z',
            endTime: '2026-11-25T09:00:02Z',
            rowsProcessed: 40,
            logTruncated: false,
            statements: [
                { statement: 'SELECT UserID INTO #users FROM sample_data.Users', durationMs: 120, rowsProcessed: 40, failed: false }
            ]
        };

        const fetchImpl: typeof fetch = async (input, init) => {
            const url = new URL(String(input));
            const method = init?.method ?? 'GET';
            if (url.pathname === '/api/auth/providers') {
                return jsonResponse(200, { local: false, oidcEnabled: true });
            }
            if (url.pathname === '/api/auth/client/exchange') {
                return jsonResponse(200, {
                    token: 'tok-alice',
                    refreshToken: 'ref-alice',
                    expiresAt: new Date(Date.now() + 3600_000).toISOString(),
                    sessionId: 'sess-alice',
                    clientId: 'etlsql-vscode',
                    audience: 'etl-sql-portal-native-api',
                    tenantId: 'tenant-alpha',
                    scopes: ['studio.read', 'studio.run'],
                    user: { id: 1, username: 'alice', roles: ['Publisher'] }
                });
            }
            if (url.pathname === '/api/studio/session') {
                return jsonResponse(200, { mode: 'CatalogOnly', capabilities: ['StudioAccess', 'ScriptRun'], sourceControlEnabled: false, draftRecovery: false });
            }
            if (url.pathname === '/api/studio/hosted-jobs' && method === 'GET') {
                const offset = Number(url.searchParams.get('offset'));
                requestedOffsets.push(String(offset));
                return jsonResponse(200, {
                    items: offset === 0
                        ? [jobs[0], ...Array.from({ length: 49 }, (_, index) => ({
                            ...jobs[1], jobName: `Job-${index}`
                        }))]
                        : [jobs[1]],
                    total: 51, offset, limit: 50, hasMore: offset === 0, orchestratorOnline: true
                });
            }
            if (url.pathname === '/api/studio/hosted-jobs/PIPELINE%3A42/runs' && method === 'GET') {
                return jsonResponse(200, runs);
            }
            if (url.pathname === '/api/studio/hosted-jobs/PIPELINE%3A42/runs/500' && method === 'GET') {
                return jsonResponse(200, detail);
            }
            if (url.pathname === '/api/studio/hosted-jobs/PIPELINE%3A42/runs/501/cancel' && method === 'POST') {
                cancelledUrls.push(url.pathname);
                return jsonResponse(202, { jobName: 'PIPELINE:42', runId: 501, message: 'Cancellation requested for run #501.' });
            }
            return jsonResponse(404, { error: 'not_found' });
        };

        const sessions = await createSignedInManager(fetchImpl, {
            origin: 'https://portal-a.example',
            tenantId: 'tenant-alpha',
            userId: 1,
            username: 'alice',
            sessionId: 'sess-alice'
        });
        const openExternal = vi.fn(async () => true);
        const tree = new HostedJobsTreeProvider({
            sessions,
            client: new HostedPipelineClient(sessions),
            openExternal,
            runSavedPipeline
        });

        const rootNodes = await tree.getChildren();
        expect(rootNodes).toHaveLength(51);
        const pipelineNode = rootNodes[0];
        const more = rootNodes[50];
        expect(tree.getTreeItem(more).command?.command).toBe('etlsql.hostedJobs.loadMore');
        await tree.loadMore(more);
        const allNodes = await tree.getChildren();
        expect(allNodes).toHaveLength(51);
        expect(allNodes[50]).toMatchObject({ kind: 'job', item: { jobName: 'ReadOnlySummary' } });
        const readOnlyNode = allNodes[50];
        await tree.loadMore(more);
        expect(requestedOffsets).toEqual(['0', '50']);
        expect(tree.getTreeItem(pipelineNode).contextValue).toBe('hosted-job-run-stop');
        expect(tree.getTreeItem(readOnlyNode).contextValue).toBe('hosted-job-readonly');

        const runNodes = await tree.getChildren(pipelineNode);
        expect(runNodes).toHaveLength(2);
        expect(tree.getTreeItem(runNodes[0]).contextValue).toBe('hosted-run-stoppable');
        expect(tree.getTreeItem(runNodes[1]).contextValue).toBe('hosted-run-readonly');

        const stmtNodes = await tree.getChildren(runNodes[1]);
        expect(stmtNodes).toHaveLength(1);
        expect(stmtNodes[0].kind).toBe('statement');

        // Open in Portal binds strictly to connected origin.
        const opened = await tree.openInPortal(runNodes[0]);
        expect(opened).toBe('https://portal-a.example/orchestrator.html?job=PIPELINE%3A42&run=501');
        expect(openExternal).toHaveBeenCalledTimes(1);

        expect(await tree.runNode(readOnlyNode)).toBe(false);
        expect(await tree.stopNode(runNodes[1])).toBe(false);
        expect(await tree.runNode(pipelineNode)).toBe(true);
        expect(runSavedPipeline).toHaveBeenCalledExactlyOnceWith(42, 3, 'FinanceCheck.etlsql');

        // Stop targets exact selected run #501.
        const stopped = await tree.stopNode(runNodes[0]);
        expect(stopped).toBe(true);
        expect(cancelledUrls).toEqual(['/api/studio/hosted-jobs/PIPELINE%3A42/runs/501/cancel']);
        tree.dispose();
    });

    it('clears the tree on target/account switch and discards late responses from the previous session', async () => {
        let resolveSlowPortalA!: (value: Response) => void;
        const slowPortalAPromise = new Promise<Response>(resolve => {
            resolveSlowPortalA = resolve;
        });

        const fetchImpl: typeof fetch = async (input) => {
            const url = new URL(String(input));
            if (url.pathname === '/api/auth/providers') {
                return jsonResponse(200, { local: false, oidcEnabled: true });
            }
            if (url.pathname === '/api/auth/client/logout') {
                return new Response(null, { status: 204 });
            }
            if (url.pathname === '/api/auth/client/exchange') {
                const isPortalA = url.origin === 'https://portal-a.example';
                return jsonResponse(200, {
                    token: isPortalA ? 'tok-a' : 'tok-b',
                    refreshToken: isPortalA ? 'ref-a' : 'ref-b',
                    expiresAt: new Date(Date.now() + 3600_000).toISOString(),
                    sessionId: isPortalA ? 'sess-a' : 'sess-b',
                    clientId: 'etlsql-vscode',
                    audience: 'etl-sql-portal-native-api',
                    tenantId: isPortalA ? 'tenant-alpha' : 'tenant-beta',
                    scopes: ['studio.read', 'studio.run'],
                    user: { id: isPortalA ? 1 : 2, username: isPortalA ? 'alice' : 'bob', roles: ['Publisher'] }
                });
            }
            if (url.pathname === '/api/studio/session') {
                return jsonResponse(200, { mode: 'CatalogOnly', capabilities: ['StudioAccess', 'ScriptRun'], sourceControlEnabled: false, draftRecovery: false });
            }
            if (url.pathname === '/api/studio/hosted-jobs') {
                if (url.origin === 'https://portal-a.example') {
                    return slowPortalAPromise;
                }
                return jsonResponse(200, {
                    items: [{
                        kind: 'job',
                        jobName: 'BetaJob',
                        displayName: 'Beta Job',
                        isEnabled: true,
                        lastStatus: 'SUCCESS',
                        canRun: true,
                        canStop: false,
                        portalPath: '/orchestrator.html?job=BetaJob'
                    }],
                    total: 1,
                    offset: 0,
                    limit: 50,
                    hasMore: false,
                    orchestratorOnline: true
                });
            }
            return jsonResponse(404, { error: 'not_found' });
        };

        const sessions = await createSignedInManager(fetchImpl, {
            origin: 'https://portal-a.example',
            tenantId: 'tenant-alpha',
            userId: 1,
            username: 'alice',
            sessionId: 'sess-a'
        });
        const tree = new HostedJobsTreeProvider({ sessions });

        // Kick off slow load on Portal A.
        const staleLoadPromise = tree.getChildren();

        // Switch target to Portal B (different host, tenant, and user) before Portal A responds.
        await sessions.signIn({ alias: 'b', label: 'Portal B', origin: 'https://portal-b.example' });
        expect(tree.getCachedJobs()).toHaveLength(0);

        // Now resolve Portal A's late response -> MUST be discarded!
        resolveSlowPortalA(jsonResponse(200, {
            items: [{
                kind: 'job',
                jobName: 'AlphaSecretJob',
                displayName: 'Alpha Secret Job',
                isEnabled: true,
                lastStatus: 'SUCCESS',
                canRun: true,
                canStop: false,
                portalPath: '/orchestrator.html?job=AlphaSecretJob'
            }],
            total: 1,
            offset: 0,
            limit: 50,
            hasMore: false,
            orchestratorOnline: true
        }));
        const staleResult = await staleLoadPromise;
        expect(staleResult).toEqual([]);
        expect(tree.getCachedJobs()).toHaveLength(0);

        // Loading on Portal B shows ONLY Portal B's job.
        const betaNodes = await tree.getChildren();
        expect(betaNodes).toHaveLength(1);
        expect((betaNodes[0] as Extract<HostedTreeNode, { kind: 'job' }>).item.jobName).toBe('BetaJob');
        expect(vscode.window.showErrorMessage).not.toHaveBeenCalled();
        tree.dispose();
    });
});
