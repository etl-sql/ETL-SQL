import * as vscode from 'vscode';

export function getTerminalCommand(exe: string, args: string[]): string {
    const shellPath = (vscode.env.shell || '').toLowerCase();
    const isPowerShell = shellPath.includes('powershell') || shellPath.includes('pwsh');
    const isCmd = shellPath.includes('cmd.exe') || (process.platform === 'win32' && !shellPath && !isPowerShell);
    
    const escapeArg = (arg: string): string => {
        if (isPowerShell) {
            if ((arg.startsWith('"') && arg.endsWith('"')) || (arg.startsWith("'") && arg.endsWith("'"))) {
                return arg;
            }
            if (/[ $`"'{};&|<>]/g.test(arg)) {
                return `'${arg.replace(/'/g, "''")}'`;
            }
            return arg;
        } else if (isCmd) {
            if ((arg.startsWith('"') && arg.endsWith('"')) || (arg.startsWith("'") && arg.endsWith("'"))) {
                return arg;
            }
            if (/[ "&|<>^]/g.test(arg)) {
                return `"${arg.replace(/"/g, '""')}"`;
            }
            return arg;
        } else {
            // Unix shells (bash/zsh)
            if ((arg.startsWith('"') && arg.endsWith('"')) || (arg.startsWith("'") && arg.endsWith("'"))) {
                return arg;
            }
            if (/[ $`"'{};&|<>\\()]/g.test(arg)) {
                return `'${arg.replace(/'/g, "'\\''")}'`;
            }
            return arg;
        }
    };

    let exeStr = exe;
    if (isPowerShell) {
        if (exeStr.includes(' ') && !exeStr.startsWith('"') && !exeStr.startsWith("'")) {
            exeStr = `'${exeStr.replace(/'/g, "''")}'`;
        }
        const argsStr = args.map(escapeArg).join(' ');
        return `& ${exeStr} ${argsStr}`;
    } else if (isCmd) {
        if (exeStr.includes(' ') && !exeStr.startsWith('"')) {
            exeStr = `"${exeStr}"`;
        }
        const argsStr = args.map(escapeArg).join(' ');
        return `${exeStr} ${argsStr}`;
    } else {
        if (exeStr.includes(' ') && !exeStr.startsWith('"') && !exeStr.startsWith("'")) {
            exeStr = `'${exeStr.replace(/'/g, "'\\''")}'`;
        }
        const argsStr = args.map(escapeArg).join(' ');
        return `${exeStr} ${argsStr}`;
    }
}
