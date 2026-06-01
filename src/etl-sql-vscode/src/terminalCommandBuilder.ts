import * as vscode from 'vscode';

export function getTerminalCommand(exe: string, args: string[]): string {
    const shellPath = (vscode.env.shell || '').toLowerCase();
    const isPowerShell = shellPath.includes('powershell') || shellPath.includes('pwsh');
    
    let exeStr = exe;
    if (exeStr.includes(' ') && !exeStr.startsWith('"')) {
        exeStr = `"${exeStr}"`;
    }
    
    const argsStr = args.map(arg => {
        if (arg.includes(' ') && !arg.startsWith('"') && !arg.startsWith("'")) {
            return `"${arg}"`;
        }
        return arg;
    }).join(' ');
    
    if (isPowerShell) {
        return `& ${exeStr} ${argsStr}`;
    }
    return `${exeStr} ${argsStr}`;
}
