import * as vscode from 'vscode';

let channel: vscode.OutputChannel | undefined;
const logBuffer: string[] = [];
const MAX_LOGS = 200;

export function setOutputChannel(outputChannel: vscode.OutputChannel) {
    channel = outputChannel;
}

export function redactSecrets(text: string): string {
    return text
        .replace(/(password|passwd|pwd|secret|token|key|connectionstring|connstring|auth)(["']?\s*[:=]\s*["']?)([^"'\s&,;]{3,})/gi, '$1$2[REDACTED]')
        .replace(/(ENC:)[A-Za-z0-9+/=]+/gi, '$1[REDACTED]');
}

export function log(message: string, level: 'info' | 'warn' | 'error' = 'info') {
    const prefix = `[${level.toUpperCase()}]`;
    const formatted = `${prefix} ${message}`;
    if (channel) {
        channel.appendLine(formatted);
    }
    const timestamp = new Date().toISOString().split('T')[1].slice(0, 8);
    const redacted = redactSecrets(formatted);
    logBuffer.push(`[${timestamp}] ${redacted}`);
    if (logBuffer.length > MAX_LOGS) {
        logBuffer.shift();
    }
}

export function logWebview(source: string, message: string, level: 'info' | 'warn' | 'error' = 'info') {
    const formattedMsg = `[Webview:${source}] ${message}`;
    log(formattedMsg, level);
}

export function show() {
    if (channel) {
        channel.show();
    }
}

export function getRecentLogs(): string[] {
    return [...logBuffer];
}

export function clearLogs(): void {
    logBuffer.length = 0;
}
