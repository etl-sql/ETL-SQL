import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as vscode from 'vscode';
import { getTerminalCommand } from '../terminalCommandBuilder';

// Mock vscode.env.shell dynamically in the tests
vi.mock('vscode', () => {
    return {
        env: {
            shell: ''
        }
    };
});

describe('terminalCommandBuilder', () => {
    beforeEach(() => {
        vi.restoreAllMocks();
        (vscode.env as any).shell = '';
    });

    describe('PowerShell Escaping', () => {
        beforeEach(() => {
            (vscode.env as any).shell = 'C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe';
        });

        it('should format simple arguments without quoting', () => {
            const cmd = getTerminalCommand('etl', ['run', 'script.etlsql']);
            expect(cmd).toBe('& etl run script.etlsql');
        });

        it('should single-quote and escape arguments with spaces or special characters', () => {
            const cmd = getTerminalCommand('etl', ['run', 'my script.etlsql', 'value$1']);
            expect(cmd).toBe("& etl run 'my script.etlsql' 'value$1'");
        });

        it('should double single-quotes inside powerShell arguments', () => {
            const cmd = getTerminalCommand('etl', ['--name', "John's Script"]);
            expect(cmd).toBe("& etl --name 'John''s Script'");
        });

        it('should preserve already quoted arguments', () => {
            const cmd = getTerminalCommand('etl', ['"already quoted"', "'also quoted'"]);
            expect(cmd).toBe('& etl "already quoted" \'also quoted\'');
        });

        it('should escape executable path if it has spaces', () => {
            const cmd = getTerminalCommand('C:\\Program Files\\etl.exe', ['run']);
            expect(cmd).toBe("& 'C:\\Program Files\\etl.exe' run");
        });
    });

    describe('Cmd Escaping', () => {
        beforeEach(() => {
            (vscode.env as any).shell = 'C:\\Windows\\System32\\cmd.exe';
        });

        it('should double-quote arguments with spaces or special characters', () => {
            const cmd = getTerminalCommand('etl', ['run', 'my script.etlsql', 'a&b']);
            expect(cmd).toBe('etl run "my script.etlsql" "a&b"');
        });

        it('should escape double-quotes inside cmd arguments', () => {
            const cmd = getTerminalCommand('etl', ['--message', 'hello "world"']);
            expect(cmd).toBe('etl --message "hello ""world"""');
        });

        it('should escape executable path if it has spaces', () => {
            const cmd = getTerminalCommand('C:\\Program Files\\etl.exe', ['run']);
            expect(cmd).toBe('"C:\\Program Files\\etl.exe" run');
        });
    });

    describe('Unix Escaping (Bash/Zsh)', () => {
        beforeEach(() => {
            (vscode.env as any).shell = '/bin/bash';
        });

        it('should single-quote arguments with shell-specific metacharacters', () => {
            const cmd = getTerminalCommand('etl', ['run', 'my script.etlsql', 'value$1', 'a&b']);
            expect(cmd).toBe("etl run 'my script.etlsql' 'value$1' 'a&b'");
        });

        it("should escape single quotes inside bash arguments using '\\''", () => {
            const cmd = getTerminalCommand('etl', ['--name', "John's Script"]);
            expect(cmd).toBe("etl --name 'John'\\''s Script'");
        });

        it('should escape executable path if it has spaces', () => {
            const cmd = getTerminalCommand('/usr/local/bin/etl run', ['arg']);
            expect(cmd).toBe("'/usr/local/bin/etl run' arg");
        });
    });
});
