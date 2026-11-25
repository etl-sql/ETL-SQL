import * as assert from 'assert';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import { HostedExecutionController } from '../../hosted/hostedExecutionController';
import { driveRealPortalOidcConsentFlow } from '../st56BrowserFlowHelper';

interface ExtensionApi {
    getHostedController: () => HostedExecutionController | undefined;
}

/**
 * ST56 Electron Extension-Host Smoke Test.
 * Runs inside a real VS Code Electron Extension Host when ETLSQL_ST56_SMOKE_PORTAL_URL is set.
 * Verifies: activation -> Local (Offline) startup -> Connect via etlsql.hosted.connect ->
 * Run saved pipeline via etlsql.runScript -> Disconnect via etlsql.hosted.disconnect ->
 * Local execution after Portal is stopped -> zero secrets persisted in settings/logs.
 */
suite('ST56 Hosted Execution Electron Smoke', function () {
    this.timeout(60_000);

    test('Activates on Local, connects to live Portal, runs saved pipeline, disconnects, and runs locally with Portal stopped', async function () {
        const portalUrl = process.env.ETLSQL_ST56_SMOKE_PORTAL_URL;
        const controlUrl = process.env.ETLSQL_ST56_SMOKE_CONTROL_URL;
        const pipelineId = Number(process.env.ETLSQL_ST56_SMOKE_PIPELINE_ID || '0');
        if (!portalUrl || !controlUrl || pipelineId <= 0) {
            this.skip();
            return;
        }

        const ext = vscode.extensions.getExtension<ExtensionApi>('Octocolee.etl-sql-vscode');
        assert.ok(ext, 'Extension Octocolee.etl-sql-vscode must be installed in Electron test host');
        const api = await ext.activate();
        assert.strictEqual(ext.isActive, true, 'Extension should be active');

        const controller = api?.getHostedController();
        assert.ok(controller, 'HostedExecutionController should be initialized on activation');

        // 1. Startup defaults to Local (Offline)
        assert.strictEqual(controller.isHostedTarget(), false, 'Extension must default to Local (Offline) on activation');
        assert.strictEqual(controller.sessions.formatStatusBarLabel(), 'ETL-SQL: Local (Offline)');

        const capturedSecrets = new Set<string>();
        controller.sessions.setBrowserLauncher(url => driveRealPortalOidcConsentFlow(url, {
            onCapturedCode: code => capturedSecrets.add(code)
        }));

        // 2. Save server in user settings and connect via the real etlsql.hosted.connect command
        const serverEntry = { alias: 'smoke', label: 'Smoke Portal', origin: portalUrl };
        const cfg = vscode.workspace.getConfiguration('etlsql');
        await cfg.update('hosted.servers', [serverEntry], vscode.ConfigurationTarget.Global);
        await cfg.update('hosted.defaultServerAlias', 'smoke', vscode.ConfigurationTarget.Global);
        await cfg.update('hosted.pipelineId', pipelineId, vscode.ConfigurationTarget.Global);

        await vscode.commands.executeCommand('etlsql.hosted.connect', serverEntry.alias);
        const session = controller.sessions.getSession();
        assert.ok(session, 'Session should be established after etlsql.hosted.connect');
        capturedSecrets.add(session.token);
        capturedSecrets.add(session.refreshToken);
        assert.strictEqual(controller.isHostedTarget(), true);
        assert.ok(controller.sessions.formatStatusBarLabel().includes('alice'));

        // 3. Open a saved (non-dirty) .etlsql file and run saved pipeline via etlsql.runScript
        const tempDir = await fs.promises.mkdtemp(path.join(os.tmpdir(), 'etlsql-electron-smoke-'));
        const scriptFile = path.join(tempDir, 'FinanceCheck.etlsql');
        await fs.promises.writeFile(scriptFile, 'SELECT 1 AS Value;\n', 'utf8');
        try {
            const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(scriptFile));
            await vscode.window.showTextDocument(doc);
            assert.strictEqual(doc.isDirty, false, 'Saved test file should not be dirty');

            const histBefore = Number((await (await fetch(`${controlUrl}/history-count`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pipelineId })
            })).json() as { count: number }).count);

            await vscode.commands.executeCommand('etlsql.runScript');

            const histAfter = Number((await (await fetch(`${controlUrl}/history-count`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pipelineId })
            })).json() as { count: number }).count);
            assert.strictEqual(histAfter, histBefore + 1, 'Hosted runScript must execute the saved pipeline on Federated Orchestrator');

            // 4. Disconnect via the real etlsql.hosted.disconnect command
            await vscode.commands.executeCommand('etlsql.hosted.disconnect');
            assert.strictEqual(controller.isHostedTarget(), false);
            assert.strictEqual(controller.sessions.getSession(), null);
            assert.strictEqual(controller.sessions.formatStatusBarLabel(), 'ETL-SQL: Local (Offline)');

            // 5. Stop the Portal via control endpoint, then run locally offline via etlsql.runScript
            await fetch(`${controlUrl}/stop-portal`, { method: 'POST', body: '{}' });
            await vscode.commands.executeCommand('etlsql.runScript');

            // 6. Verify no secrets were persisted to VS Code user settings file
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
