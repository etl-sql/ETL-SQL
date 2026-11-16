import * as vscode from 'vscode';

export function getTerminalCommand(exe: string, args: string[]): string {
    const shellPath = (vscode.env.shell || '').toLowerCase();
    const isPowerShell = shellPath.includes('powershell') || shellPath.includes('pwsh');
    const isCmd = shellPath.includes('cmd.exe') || (process.platform === 'win32' && !shellPath && !isPowerShell);
    
    // Callers pass raw values and every argument is quoted here. An argument that merely looks
    // quoted is quoted again: passing it through let a file named "$(whoami).rptsql" run in the shell.
    const escapeArg = (arg: string): string => {
        if (isPowerShell) {
            if (/[\s$`"'{}();,&|<>@]/.test(arg)) {
                return `'${arg.replace(/'/g, "''")}'`;
            }
            return arg;
        } else if (isCmd) {
            if (/[\s"&|<>^()]/.test(arg)) {
                return `"${arg.replace(/"/g, '""')}"`;
            }
            return arg;
        } else {
            // Unix shells (bash/zsh): nothing but the quote itself is special inside single quotes.
            if (/[\s$`"'{};&|<>\\()]/.test(arg)) {
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
