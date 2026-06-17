import * as vscode from 'vscode';

let channel: vscode.OutputChannel | undefined;

export function setOutputChannel(outputChannel: vscode.OutputChannel) {
    channel = outputChannel;
}

export function log(message: string, level: 'info' | 'warn' | 'error' = 'info') {
    if (channel) {
        const prefix = `[${level.toUpperCase()}]`;
        channel.appendLine(`${prefix} ${message}`);
    }
}

export function logWebview(source: string, message: string, level: 'info' | 'warn' | 'error' = 'info') {
    if (channel) {
        const prefix = `[Webview:${source}] [${level.toUpperCase()}]`;
        channel.appendLine(`${prefix} ${message}`);
    }
}

export function show() {
    if (channel) {
        channel.show();
    }
}
