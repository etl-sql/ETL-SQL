import * as cp from 'child_process';
import * as vscode from 'vscode';
import { ResultsPanel } from './resultsPanel';

export class ReplManager {
    private static _instance: ReplManager;
    private _process: cp.ChildProcess | undefined;
    private _isReady: boolean = false;
    private _commandQueue: { script: string, resolve: (val: any) => void, reject: (err: any) => void }[] = [];
    private _currentHandler: ((msg: any) => void) | undefined;
    private _outputChannel: vscode.OutputChannel | undefined;
    private _currentSessionId: string | undefined;
    private _debugMode: boolean = false;
    private _onVariablesChange: vscode.EventEmitter<any[]> = new vscode.EventEmitter<any[]>();
    public readonly onVariablesChange: vscode.Event<any[]> = this._onVariablesChange.event;
    private _isRunning: boolean = false;

    public static getInstance(): ReplManager {
        if (!ReplManager._instance) {
            ReplManager._instance = new ReplManager();
        }
        return ReplManager._instance;
    }

    public setOutputChannel(channel: vscode.OutputChannel) {
        this._outputChannel = channel;
    }

    public setDebugMode(debug: boolean) {
        this._debugMode = debug;
    }

    public async execute(script: string, exePath: string, args: string[]): Promise<void> {
        // Extract sessionId from args for tracking
        let sessionId: string | undefined;
        const sessionIdx = args.indexOf('--session');
        if (sessionIdx !== -1 && sessionIdx + 1 < args.length) {
            sessionId = args[sessionIdx + 1];
        }

        if (this._process && this._currentSessionId !== sessionId) {
            this._outputChannel?.appendLine(`[REPL] Session changed from ${this._currentSessionId} to ${sessionId}. Restarting engine...`);
            this.stop();
        }

        if (!this._process) {
            this._currentSessionId = sessionId;
            await this._start(exePath, args);
        }

        return new Promise((resolve, reject) => {
            this._commandQueue.push({ script, resolve, reject });
            this._processNext();
        });
    }
    private async _start(exePath: string, args: string[]): Promise<void> {
        return new Promise((resolve, reject) => {
            const absoluteExePath = require('path').resolve(exePath);
            const startMsg = `Starting ETL-SQL REPL: "${absoluteExePath}" ui repl ${args.join(' ')}`;
            this._outputChannel?.appendLine(startMsg);

            this._process = cp.spawn(absoluteExePath, ["ui", "repl", ...args], {
                env: { ...process.env, "FORCE_COLOR": "0" }
            });

            // All JSON protocol messages (status, message, results, done) come on stdout.
            let buffer = '';
            this._process.stdout?.on('data', (data) => {
                buffer += data.toString();
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    const trimmed = line.trim();
                    if (!trimmed) continue;
                    try {
                        const msg = JSON.parse(trimmed);
                        this._handleMessage(msg);
                        if (msg.type === 'status' && msg.status === 'ready') {
                            this._outputChannel?.appendLine(`[ENGINE] Ready. Build: ${msg.buildId || 'v1.0'}`);
                            this._isReady = true;
                            resolve();
                            this._processNext();
                        }
                    } catch {
                        // Non-JSON line from engine (startup noise etc.)
                        if (this._debugMode) {
                            this._outputChannel?.appendLine(`[Engine] ${trimmed}`);
                        }
                    }
                }
            });

            // Stderr is raw diagnostics only — log to output channel, never parse as protocol.
            this._process.stderr?.on('data', (data) => {
                const text = data.toString().trimEnd();
                if (text) {
                    this._outputChannel?.appendLine(text);
                }
            });

            this._process.on('close', (code) => {
                this._outputChannel?.appendLine(`REPL process exited (code ${code}).`);
                this._process = undefined;
                this._isReady = false;
                // Reject any in-flight command so the caller's promise doesn't hang.
                if (this._currentHandler) {
                    this._currentHandler({ type: 'done', exitCode: code ?? 1 });
                }
                // Drain the queue — no process to run them.
                for (const cmd of this._commandQueue) {
                    cmd.reject(new Error('REPL process exited unexpectedly'));
                }
                this._commandQueue = [];
            });
        });
    }

    private _processNext() {
        if (!this._isReady || this._commandQueue.length === 0 || this._currentHandler) {
            if (this._commandQueue.length === 0 && !this._currentHandler) {
                this._isRunning = false;
            }
            return;
        }

        this._isRunning = true;
        const cmd = this._commandQueue.shift()!;

        const timeout = setTimeout(() => {
            if (this._currentHandler) {
                this._outputChannel?.appendLine(`[WARN] Command timed out after 60s. Forcing queue reset.`);
                this._currentHandler({ type: 'done', exitCode: 1 });
            }
        }, 60000);

        this._currentHandler = (msg) => {
            if (msg.type === 'done') {
                clearTimeout(timeout);
                this._currentHandler = undefined;
                // Forward done to webview so the spinner stops.
                ResultsPanel.postMessage(msg);
                this._outputChannel?.appendLine(`[PROCESS] Command finished with code ${msg.exitCode}`);
                if (msg.exitCode === 0) cmd.resolve(0);
                else cmd.reject(new Error("Execution failed"));
                this._processNext();
            } else {
                // Forward all other messages (results, message, performance) to the webview.
                ResultsPanel.postMessage(msg);
            }
        };

        this._outputChannel?.appendLine(`[PROCESS] Running script (${cmd.script.length} bytes)...`);
        this._process?.stdin?.write(JSON.stringify({ action: "run", script: cmd.script }) + "\n");
    }

    private _handleMessage(msg: any) {
        // Log text messages to the output channel (once, here).
        if (msg.type === 'message') {
            const prefix = msg.level === 'error' ? '[ERROR] ' : (msg.level === 'warning' ? '[WARN] ' : '');
            this._outputChannel?.appendLine(`${prefix}${msg.text}`);
        }

        if (msg.type === 'variables') {
            this._onVariablesChange.fire(msg.data);
        }

        if (this._currentHandler) {
            this._currentHandler(msg);
        } else if (msg.type === 'message' || msg.type === 'results') {
            // No active command — forward to webview anyway (e.g. async engine messages).
            ResultsPanel.postMessage(msg);
        }
    }

    public stop() {
        this._isRunning = false;
        // Reject any in-flight or queued commands so callers don't hang.
        if (this._currentHandler) {
            this._currentHandler({ type: 'done', exitCode: 1 });
        }
        for (const cmd of this._commandQueue) {
            cmd.reject(new Error('ReplManager stopped'));
        }
        this._commandQueue = [];
        this._currentHandler = undefined;
        this._isReady = false;

        if (this._process) {
            this._process.stdin?.write(JSON.stringify({ action: "exit" }) + "\n");
            this._process.kill();
            this._process = undefined;
        }
    }
    
    public isRunning(): boolean {
        return this._isRunning;
    }
}
