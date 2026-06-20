import * as vscode from 'vscode';
import * as os from 'os';
import { getRecentLogs, redactSecrets } from './logger';

export async function reportIssue(context: vscode.ExtensionContext): Promise<void> {
    try {
        // 1. Gather configuration details (redacted)
        const config = vscode.workspace.getConfiguration('etlsql');
        const keys = [
            'executable.path',
            'server.path',
            'portal.url',
            'lint.enable',
            'explain.enable',
            'ai.provider',
            'ai.model',
            'ai.endpoint'
        ];
        const redactedConfig: Record<string, string> = {};
        for (const key of keys) {
            const val = config.get(key);
            if (val !== undefined && val !== null) {
                redactedConfig[key] = redactSecrets(String(val));
            }
        }

        // 2. Gather environment metadata
        const envInfo = {
            os: `${os.platform()} ${os.arch()} ${os.release()}`,
            vscodeVersion: vscode.version,
            extensionVersion: context.extension?.packageJSON?.version || 'unknown',
            nodeVersion: process.version
        };

        // 3. Get recent logs (already redacted at insertion time)
        const logs = getRecentLogs().join('\n');

        // 4. Format Markdown template
        const markdown = `<!-- Please describe the issue/bug you encountered above this line -->

### Environment Details
- **OS Platform/Arch**: ${envInfo.os}
- **VS Code Version**: ${envInfo.vscodeVersion}
- **Extension Version**: ${envInfo.extensionVersion}
- **Node.js Version**: ${envInfo.nodeVersion}

### Active Configuration Overrides
\`\`\`json
${JSON.stringify(redactedConfig, null, 2)}
\`\`\`

<details>
<summary>Recent Extension Logs (Last 200 Entries)</summary>

\`\`\`text
${logs || '(No logs recorded yet)'}
\`\`\`
</details>
`;

        // 5. Copy to clipboard
        await vscode.env.clipboard.writeText(markdown);

        // 6. Redirect to GitHub new issue
        const issueUrl = 'https://github.com/etl-sql/ETL-SQL/issues/new';
        const choice = await vscode.window.showInformationMessage(
            'Redacted diagnostic bundle copied to clipboard! Would you like to open GitHub to paste and submit your issue?',
            'Yes',
            'No'
        );

        if (choice === 'Yes') {
            await vscode.env.openExternal(vscode.Uri.parse(issueUrl));
        }
    } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to generate diagnostic report: ${msg}`);
    }
}
