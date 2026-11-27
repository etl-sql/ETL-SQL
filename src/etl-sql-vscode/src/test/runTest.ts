import * as path from 'path';
import { runTests } from '@vscode/test-electron';

async function main() {
	try {
		// The folder containing the Extension Manifest package.json
		// Passed to `--extensionDevelopmentPath`
		const extensionDevelopmentPath = path.resolve(__dirname, '../../');

		// The path to test runner
		// Passed to --extensionTestsPath
		const extensionTestsPath = path.resolve(__dirname, './suite/index');

		const launchArgs: string[] = [];
		if (process.env.ETLSQL_VSCODE_USER_DATA_DIR) {
			launchArgs.push('--user-data-dir', process.env.ETLSQL_VSCODE_USER_DATA_DIR);
		}
		if (process.env.ETLSQL_VSCODE_TEST_WORKSPACE) {
			launchArgs.push(process.env.ETLSQL_VSCODE_TEST_WORKSPACE);
		}

		// Download VS Code, unzip it and run the integration test
		await runTests({
			extensionDevelopmentPath,
			extensionTestsPath,
			vscodeExecutablePath: process.env.ETLSQL_VSCODE_EXECUTABLE,
			launchArgs: launchArgs.length > 0 ? launchArgs : undefined,
		});
	} catch {
		console.error('Failed to run tests');
		process.exit(1);
	}
}

main();
