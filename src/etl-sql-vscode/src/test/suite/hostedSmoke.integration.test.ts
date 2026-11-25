import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { HostedExecutionController } from '../../hosted/hostedExecutionController';
import { HostedJobsTreeProvider, HostedTreeNode } from '../../hosted/hostedJobsTreeProvider';
import { driveRealPortalOidcConsentFlow } from '../st56BrowserFlowHelper';

interface ExtensionApi {
    getHostedController: () => HostedExecutionController | undefined;
    getHostedJobsTree: () => HostedJobsTreeProvider | undefined;
    getHostedJobsView: () => vscode.TreeView<HostedTreeNode> | undefined;
}

async function controlPost<T = Record<string, unknown>>(controlUrl: string, endpoint: string, body?: unknown): Promise<T> {
    const resp = await fetch(`${controlUrl}${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: body ? JSON.stringify(body) : '{}'
    });
    assert.strictEqual(resp.status, 200, `Control ${endpoint} failed with HTTP ${resp.status}`);
    return await resp.json() as T;
}

async function getHistoryCount(controlUrl: string, pipelineId: number): Promise<number> {
    const data = await controlPost<{ count: number }>(controlUrl, '/history-count', { pipelineId });
    return Number(data.count);
}

async function loadAllJobNodes(tree: HostedJobsTreeProvider): Promise<Array<Extract<HostedTreeNode, { kind: 'job' }>>> {
    let nodes = await tree.getChildren();
    while (nodes.length > 0 && nodes[nodes.length - 1].kind === 'more') {
        const moreNode = nodes[nodes.length - 1];
        await vscode.commands.executeCommand('etlsql.hostedJobs.loadMore', moreNode);
        nodes = await tree.getChildren();
    }
    return nodes.filter((node): node is Extract<HostedTreeNode, { kind: 'job' }> => node.kind === 'job');
}

/**
 * ST56 + ST59 Electron Extension-Host Smoke & Sidebar Acceptance Test.
 * Runs inside a real VS Code Electron Extension Host when ETLSQL_ST56_SMOKE_PORTAL_URL is set.
 * Verifies: activation -> Local (Offline) startup -> Hosted Jobs sidebar placeholder ->
 * Connect via etlsql.hosted.connect -> Run saved pipeline via etlsql.runScript ->
 * Load past 50 jobs -> Expand job into recent runs & statement logs ->
 * Run displayed saved revision (stale_revision on v1, v2 execution with dirty buffer untouched) ->
 * Switch to read-only account & refresh (inspect runs/logs, neither Run nor Stop) ->
 * Switch back & refresh -> Open exact run in Portal -> Stop exact selected run ->
 * Disconnect via etlsql.hosted.disconnect -> Local execution after Portal is stopped ->
 * Zero secrets persisted in settings.
 */
suite('ST56 Hosted Execution Electron Smoke', function () {
    this.timeout(120_000);

    test('Activates on Local, exercises Hosted Jobs sidebar end-to-end against loopback Portal + Federated Orchestrator, disconnects, and runs locally with Portal stopped', async function () {
        const portalUrl = process.env.ETLSQL_ST56_SMOKE_PORTAL_URL;
        const controlUrl = process.env.ETLSQL_ST56_SMOKE_CONTROL_URL;
        const pipelineId = Number(process.env.ETLSQL_ST56_SMOKE_PIPELINE_ID || '0');
        const slowPipelineId = Number(process.env.ETLSQL_ST56_SMOKE_SLOW_PIPELINE_ID || '0');
        if (!portalUrl || !controlUrl || pipelineId <= 0 || slowPipelineId <= 0) {
            this.skip();
            return;
        }

        const ext = vscode.extensions.getExtension<ExtensionApi>('Octocolee.etl-sql-vscode');
        assert.ok(ext, 'Extension Octocolee.etl-sql-vscode must be installed in Electron test host');
        const api = await ext.activate();
        assert.strictEqual(ext.isActive, true, 'Extension should be active');

        const controller = api?.getHostedController();
        const tree = api?.getHostedJobsTree();
        const treeView = api?.getHostedJobsView();
        assert.ok(controller, 'HostedExecutionController should be initialized on activation');
        assert.ok(tree, 'HostedJobsTreeProvider should be initialized on activation');
        assert.ok(treeView, 'HostedJobs TreeView should be registered on activation');

        // 1. Startup defaults to Local (Offline) and focuses the actual Hosted Jobs sidebar view
        assert.strictEqual(controller.isHostedTarget(), false, 'Extension must default to Local (Offline) on activation');
        assert.strictEqual(controller.sessions.formatStatusBarLabel(), 'ETL-SQL: Local (Offline)');
        await vscode.commands.executeCommand('workbench.view.extension.etlsql-explorer');
        await vscode.commands.executeCommand('etlsql-hosted-jobs.focus');
        const offlineRoots = await tree.getChildren();
        assert.strictEqual(offlineRoots.length, 1);
        assert.strictEqual(offlineRoots[0].kind, 'placeholder');

        const capturedSecrets = new Set<string>();
        controller.sessions.setBrowserLauncher(url => driveRealPortalOidcConsentFlow(url, {
            onCapturedCode: code => capturedSecrets.add(code)
        }));
        const openedPortalUrls: string[] = [];
        tree.setOpenExternal(uri => {
            openedPortalUrls.push(uri.toString(true));
            return Promise.resolve(true);
        });

        // 2. Save server in user settings and connect as alice via the real etlsql.hosted.connect command
        const serverEntry = { alias: 'smoke', label: 'Smoke Portal', origin: portalUrl };
        const cfg = vscode.workspace.getConfiguration('etlsql');
        await cfg.update('hosted.servers', [serverEntry], vscode.ConfigurationTarget.Global);
        await cfg.update('hosted.defaultServerAlias', 'smoke', vscode.ConfigurationTarget.Global);
        await cfg.update('hosted.pipelineId', pipelineId, vscode.ConfigurationTarget.Global);

        await controlPost(controlUrl, '/set-oidc-user', { user: 'alice' });
        await vscode.commands.executeCommand('etlsql.hosted.connect', serverEntry.alias);
        const session = controller.sessions.getSession();
        assert.ok(session, 'Session should be established after etlsql.hosted.connect');
        capturedSecrets.add(session.token);
        capturedSecrets.add(session.refreshToken);
        assert.strictEqual(controller.isHostedTarget(), true);
        assert.ok(controller.sessions.formatStatusBarLabel().includes('alice'));

        // 3. Open a saved (non-dirty) .etlsql file and run saved pipeline via etlsql.runScript (ST56)
        const tempDir = await fs.promises.mkdtemp(path.join(os.tmpdir(), 'etlsql-electron-smoke-'));
        const scriptFile = path.join(tempDir, 'FinanceCheck.etlsql');
        await fs.promises.writeFile(scriptFile, 'SELECT 1 AS Value;\n', 'utf8');
        try {
            const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(scriptFile));
            const editor = await vscode.window.showTextDocument(doc);
            assert.strictEqual(doc.isDirty, false, 'Saved test file should not be dirty');

            const histBefore = await getHistoryCount(controlUrl, pipelineId);
            await vscode.commands.executeCommand('etlsql.runScript');
            const histAfter = await getHistoryCount(controlUrl, pipelineId);
            assert.strictEqual(histAfter, histBefore + 1, 'Hosted runScript must execute the saved pipeline on Federated Orchestrator');

            // 4. ST59: Load past 50 jobs in the Hosted Jobs sidebar
            const firstPage = await tree.getChildren();
            assert.strictEqual(firstPage.length, 51, 'First page should return 50 jobs plus the Load more jobs node');
            const moreNode = firstPage[50];
            assert.strictEqual(moreNode.kind, 'more');
            const moreTreeItem = tree.getTreeItem(moreNode);
            assert.strictEqual(moreTreeItem.label, 'Load more jobs');
            assert.strictEqual(moreTreeItem.command?.command, 'etlsql.hostedJobs.loadMore');

            await vscode.commands.executeCommand('etlsql.hostedJobs.loadMore', moreNode);
            const allAliceJobs = await tree.getChildren();
            assert.strictEqual(allAliceJobs.length, 53, 'Loading past 50 jobs should yield all 53 authorized jobs for alice');
            assert.ok(allAliceJobs.every(node => node.kind === 'job'));

            const financeNode = allAliceJobs.find(
                (n): n is Extract<HostedTreeNode, { kind: 'job' }> => n.kind === 'job' && n.item.pipelineId === pipelineId
            );
            const slowNode = allAliceJobs.find(
                (n): n is Extract<HostedTreeNode, { kind: 'job' }> => n.kind === 'job' && n.item.pipelineId === slowPipelineId
            );
            assert.ok(financeNode, 'FinanceCheck.etlsql should appear after loading past 50 jobs');
            assert.ok(slowNode, 'SlowWait.etlsql should appear after loading past 50 jobs');
            assert.strictEqual(financeNode.item.pipelineVersion, 1);

            // 5. ST59: Expand a job into recent runs and statement logs
            const financeRuns = await tree.getChildren(financeNode);
            assert.ok(financeRuns.length >= 2, 'FinanceCheck should have seeded and ST56 runs');
            const successRun = financeRuns.find(
                (n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.status === 'SUCCESS'
            );
            assert.ok(successRun, 'FinanceCheck should include a SUCCESS run');
            const statements = await tree.getChildren(successRun);
            assert.ok(
                statements.some(s => s.kind === 'statement' && s.statement.statement.includes('sample_data.Users')),
                'Expanding a run must show statement logs from the Federated Orchestrator'
            );

            // 6. ST59: Run the displayed saved revision (stale_revision on displayed v1, then v2 with dirty local editor untouched)
            await controlPost(controlUrl, '/bump-finance-version', { pipelineId });
            const histBeforeStale = await getHistoryCount(controlUrl, pipelineId);
            await vscode.commands.executeCommand('etlsql.hostedJobs.run', financeNode);
            const histAfterStale = await getHistoryCount(controlUrl, pipelineId);
            assert.strictEqual(histAfterStale, histBeforeStale, 'Running stale displayed v1 node must not silently execute v2');

            const refreshedJobs = await loadAllJobNodes(tree);
            const financeV2Node = refreshedJobs.find(n => n.item.pipelineId === pipelineId);
            assert.ok(financeV2Node, 'FinanceCheck should remain present after refresh');
            assert.strictEqual(financeV2Node.item.pipelineVersion, 2);
            assert.ok(String(tree.getTreeItem(financeV2Node).description).includes('v2'));

            // Make the local editor dirty to prove Hosted Jobs Run executes the displayed saved v2 revision directly
            await editor.edit(eb => eb.insert(new vscode.Position(0, 0), '-- unsaved local edit\n'));
            assert.strictEqual(doc.isDirty, true);

            await vscode.commands.executeCommand('etlsql.hostedJobs.run', financeV2Node);
            const histAfterV2 = await getHistoryCount(controlUrl, pipelineId);
            assert.strictEqual(histAfterV2, histBeforeStale + 1, 'Running displayed v2 node should execute saved v2 revision');
            assert.strictEqual(doc.isDirty, true, 'Dirty local editor buffer must stay untouched');

            const v2Jobs = await loadAllJobNodes(tree);
            const financeAfterV2 = v2Jobs.find(n => n.item.pipelineId === pipelineId)!;
            const v2Runs = await tree.getChildren(financeAfterV2);
            const latestV2Run = v2Runs.find((n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.status === 'SUCCESS');
            assert.ok(latestV2Run);
            const v2Stmts = await tree.getChildren(latestV2Run);
            assert.ok(
                v2Stmts.some(s => s.kind === 'statement' && s.statement.statement.includes('TotalUsers')),
                'Executed run should correspond to saved v2 script'
            );

            // 7. ST59: Start SlowWait, switch to read-only caller bob + refresh, verify inspectable runs/logs and neither Run nor Stop
            const slowNodeForRun = v2Jobs.find(n => n.item.pipelineId === slowPipelineId)!;
            const slowRunPromise = Promise.resolve(vscode.commands.executeCommand('etlsql.hostedJobs.run', slowNodeForRun));
            let slowRunId = 0;
            for (let i = 0; i < 150; i++) {
                const active = controller.getActiveRun();
                if (active && active.pipelineId === slowPipelineId && active.runId > 0) {
                    slowRunId = active.runId;
                    break;
                }
                await new Promise(resolve => setTimeout(resolve, 100));
            }
            assert.ok(slowRunId > 0, 'SlowWait run should attach and report an active runId');

            await controlPost(controlUrl, '/set-oidc-user', { user: 'bob' });
            await vscode.commands.executeCommand('etlsql.hosted.connect', serverEntry.alias);
            const bobSession = controller.sessions.getSession();
            assert.ok(bobSession && bobSession.user.username === 'bob', 'Session should switch to bob');
            capturedSecrets.add(bobSession.token);
            capturedSecrets.add(bobSession.refreshToken);
            await slowRunPromise;
            for (let i = 0; i < 50 && controller.isRunning(); i++) {
                await new Promise(resolve => setTimeout(resolve, 100));
            }

            await vscode.commands.executeCommand('etlsql.hostedJobs.refresh');
            const bobRoots = await tree.getChildren();
            assert.strictEqual(bobRoots.length, 2, 'Read-only bob must see only his 2 authorized jobs (alice BatchJobs cleared)');
            for (const bobNode of bobRoots) {
                assert.strictEqual(bobNode.kind, 'job');
                assert.strictEqual(bobNode.item.canRun, false);
                assert.strictEqual(bobNode.item.canStop, false);
                assert.strictEqual(tree.getTreeItem(bobNode).contextValue, 'hosted-job-readonly');
            }

            const bobFinance = bobRoots.find(
                (n): n is Extract<HostedTreeNode, { kind: 'job' }> => n.kind === 'job' && n.item.pipelineId === pipelineId
            )!;
            const bobSlow = bobRoots.find(
                (n): n is Extract<HostedTreeNode, { kind: 'job' }> => n.kind === 'job' && n.item.pipelineId === slowPipelineId
            )!;
            const bobFinanceRuns = await tree.getChildren(bobFinance);
            const bobSuccessRun = bobFinanceRuns.find(
                (n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.status === 'SUCCESS'
            )!;
            assert.strictEqual(bobSuccessRun.run.canStop, false);
            assert.strictEqual(tree.getTreeItem(bobSuccessRun).contextValue, 'hosted-run-readonly');
            const bobStmts = await tree.getChildren(bobSuccessRun);
            assert.ok(bobStmts.some(s => s.kind === 'statement'), 'Read-only caller must be able to inspect statement logs');

            const bobSlowRuns = await tree.getChildren(bobSlow);
            const bobActiveSlowRun = bobSlowRuns.find(
                (n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.runId === slowRunId
            )!;
            assert.ok(bobActiveSlowRun, 'Read-only bob should see the active SlowWait run');
            assert.strictEqual(bobActiveSlowRun.run.canStop, false);
            assert.strictEqual(tree.getTreeItem(bobActiveSlowRun).contextValue, 'hosted-run-readonly');
            assert.strictEqual(await vscode.commands.executeCommand('etlsql.hostedJobs.run', bobFinance), false);
            assert.strictEqual(await vscode.commands.executeCommand('etlsql.hostedJobs.stop', bobActiveSlowRun), false);

            // 8. ST59: Switch back to alice, refresh, open exact run in Portal, and stop exact selected run
            await controlPost(controlUrl, '/set-oidc-user', { user: 'alice' });
            await vscode.commands.executeCommand('etlsql.hosted.connect', serverEntry.alias);
            const aliceSession2 = controller.sessions.getSession();
            assert.ok(aliceSession2 && aliceSession2.user.username === 'alice', 'Session should switch back to alice');
            capturedSecrets.add(aliceSession2.token);
            capturedSecrets.add(aliceSession2.refreshToken);

            await vscode.commands.executeCommand('etlsql.hostedJobs.refresh');
            const aliceJobsAfterSwitch = await loadAllJobNodes(tree);
            assert.strictEqual(aliceJobsAfterSwitch.length, 53, 'Alice should regain all 53 authorized jobs after switching back and refreshing');
            const aliceSlowNode = aliceJobsAfterSwitch.find(n => n.item.pipelineId === slowPipelineId)!;
            const aliceSlowRuns = await tree.getChildren(aliceSlowNode);
            const activeSlowRunNode = aliceSlowRuns.find(
                (n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.runId === slowRunId
            );
            const earlierTerminalRunNode = aliceSlowRuns.find(
                (n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.runId !== slowRunId && n.run.isTerminal
            );
            assert.ok(activeSlowRunNode, 'Alice should see the active SlowWait run');
            assert.ok(earlierTerminalRunNode, 'Alice should also see the earlier terminal SlowWait run');
            assert.strictEqual(tree.getTreeItem(activeSlowRunNode).contextValue, 'hosted-run-stoppable');
            assert.strictEqual(tree.getTreeItem(earlierTerminalRunNode).contextValue, 'hosted-run-readonly');

            const openedUrl = await vscode.commands.executeCommand<string | null>('etlsql.hostedJobs.openInPortal', activeSlowRunNode);
            const expectedPortalUrl = `${portalUrl}/orchestrator.html?job=PIPELINE%3A${slowPipelineId}&run=${slowRunId}`;
            assert.strictEqual(openedUrl, expectedPortalUrl);
            assert.strictEqual(openedPortalUrls.length, 1, `Expected 1 openExternal URI, got ${JSON.stringify(openedPortalUrls)}`);
            const capturedOpenUri = new URL(openedPortalUrls[0]);
            assert.strictEqual(capturedOpenUri.origin, new URL(portalUrl).origin);
            assert.strictEqual(capturedOpenUri.pathname, '/orchestrator.html');
            assert.strictEqual(capturedOpenUri.searchParams.get('job'), `PIPELINE:${slowPipelineId}`);
            assert.strictEqual(capturedOpenUri.searchParams.get('run'), String(slowRunId));
            const portalPageResp = await fetch(openedUrl!);
            assert.strictEqual(portalPageResp.status, 200, 'Portal deep-link URL must return HTTP 200');

            assert.strictEqual(await vscode.commands.executeCommand('etlsql.hostedJobs.stop', earlierTerminalRunNode), false);
            assert.strictEqual(await vscode.commands.executeCommand('etlsql.hostedJobs.stop', activeSlowRunNode), true);

            let cancelledStatus = '';
            for (let i = 0; i < 60; i++) {
                await vscode.commands.executeCommand('etlsql.hostedJobs.refresh');
                const jobsNow = await loadAllJobNodes(tree);
                const slowNow = jobsNow.find(n => n.item.pipelineId === slowPipelineId)!;
                const runsNow = await tree.getChildren(slowNow);
                const targetRun = runsNow.find(
                    (n): n is Extract<HostedTreeNode, { kind: 'run' }> => n.kind === 'run' && n.run.runId === slowRunId
                );
                if (targetRun?.run.isTerminal) {
                    cancelledStatus = targetRun.run.status;
                    break;
                }
                await new Promise(resolve => setTimeout(resolve, 150));
            }
            assert.strictEqual(cancelledStatus, 'CANCELLED', 'Exact selected run must reach terminal CANCELLED');

            // 9. Disconnect via the real etlsql.hosted.disconnect command
            await vscode.commands.executeCommand('etlsql.hosted.disconnect');
            assert.strictEqual(controller.isHostedTarget(), false);
            assert.strictEqual(controller.sessions.getSession(), null);
            assert.strictEqual(controller.sessions.formatStatusBarLabel(), 'ETL-SQL: Local (Offline)');
            const disconnectedRoots = await tree.getChildren();
            assert.strictEqual(disconnectedRoots.length, 1);
            assert.strictEqual(disconnectedRoots[0].kind, 'placeholder');

            // 10. Stop the Portal via control endpoint, then revert dirty buffer and run locally offline via etlsql.runScript
            await controlPost(controlUrl, '/stop-portal');
            await vscode.commands.executeCommand('workbench.action.files.revert');
            await vscode.commands.executeCommand('etlsql.runScript');

            // 11. Verify no secrets were persisted to VS Code user settings file
            const userSettingsPath = process.env.ETLSQL_ST56_SMOKE_USER_SETTINGS;
            if (userSettingsPath && fs.existsSync(userSettingsPath)) {
                const raw = await fs.promises.readFile(userSettingsPath, 'utf8');
                for (const secret of capturedSecrets) {
                    assert.ok(!raw.includes(secret), `Secret leaked in persisted VS Code settings: ${path.basename(userSettingsPath)}`);
                }
            }
        } finally {
            await fs.promises.rm(tempDir, { recursive: true, force: true }).catch(() => {});
        }
    });
});
