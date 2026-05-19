import * as assert from 'assert';
import * as vscode from 'vscode';

suite('Extension Integration Test Suite', () => {
	vscode.window.showInformationMessage('Starting integration tests...');

	test('Extension should activate successfully', async () => {
		const extension = vscode.extensions.getExtension('etl-sql.etl-sql-vscode');
		assert.ok(extension, 'Extension was not found');
		await extension!.activate();
		assert.strictEqual(extension!.isActive, true, 'Extension should be active');
	});

	test('Command etlsql.runScript is registered', async () => {
		const commands = await vscode.commands.getCommands(true);
		assert.ok(commands.includes('etlsql.runScript'), 'Command runScript not registered');
	});

	test('Can run a simple "SELECT 1" without throwing', async () => {
		// 1. Create a virtual ETL-SQL document
		const doc = await vscode.workspace.openTextDocument({
			language: 'etlsql',
			content: 'SELECT 1;'
		});
		await vscode.window.showTextDocument(doc);
		
		// 2. Trigger the command
		// Note: We're not asserting the results yet, just that the extension doesn't crash
		// during the handshake. This is the primary fragility point.
		try {
			await vscode.commands.executeCommand('etlsql.runScript');
			// Allow some time for ReplManager to spawn
			await new Promise(r => setTimeout(r, 2000));
		} catch (err) {
			assert.fail(`Execution failed with error: ${err}`);
		}
	});

    test('Connections view should be visible', async () => {
        const view = await vscode.commands.executeCommand('workbench.view.extension.etlsql-explorer');
        assert.ok(view !== undefined);
    });

    test('Results panel should be registered and toggleable', async () => {
        // workbench.view.extension.etlsql-panel is the ID we set in package.json
        try {
            await vscode.commands.executeCommand('workbench.view.extension.etlsql-panel');
            // Successful execution of the command implies the view container exists
        } catch (err) {
            assert.fail('Results panel view container not found');
        }
    });

    test('Can trigger results clearing via command', async () => {
        // This is a smoke test for the command itself
        try {
            await vscode.commands.executeCommand('etlsql.runScript');
            // We expect this to call ResultsPanel.postMessage({ type: 'clear' }) internally
        } catch (err) {
            assert.fail('Failed to trigger runScript command');
        }
    });
});
