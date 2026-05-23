import * as cp from 'child_process';
import * as vscode from 'vscode';
import * as path from 'path';
import { ResultsPanel } from './resultsPanel';

export interface EngineMessage {
    type: string;
    status?: string;
    buildId?: string;
    exitCode?: number;
    text?: string;
    level?: string;
    data?: unknown;
    columns?: string[];
    rows?: Record<string, unknown>[];
    metrics?: Record<string, number>;
    mermaid?: string; // Added for lineage
}

interface CommandRequest {
    script: string;
    scriptPath?: string;
    workspaceRoot?: string;
    interactiveMode?: boolean;
    masterPassword?: string;
    onMessage?: (msg: EngineMessage) => void;
    resolve: (val?: void) => void;
    reject: (err?: unknown) => void;
}

export interface ReplLaunchOptions {
    env?: NodeJS.ProcessEnv;
    masterPassword?: string;
}

export class ReplManager {
    private static _instance: ReplManager;
    private _process: cp.ChildProcess | undefined;
    private _isReady: boolean = false;
    private _commandQueue: CommandRequest[] = [];
    private _currentHandler: ((msg: EngineMessage) => void) | undefined;
    private _outputChannel: vscode.OutputChannel | undefined;
    private _currentSessionId: string | undefined;
    private _debugMode: boolean = false;
    private _onVariablesChange: vscode.EventEmitter<unknown[]> = new vscode.EventEmitter<unknown[]>();
    public readonly onVariablesChange: vscode.Event<unknown[]> = this._onVariablesChange.event;
    private _isRunning: boolean = false;
    private _startPromise: Promise<void> | undefined;
    // Incremented on every stop() so close handlers for old processes don't
    // clear commands that were queued for the new process.
    private _generation: number = 0;

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

    public async execute(script: string, exePath: string, args: string[], scriptPath?: string, workspaceRoot?: string, interactiveMode?: boolean, onMessage?: (msg: EngineMessage) => void, launchOptions?: ReplLaunchOptions): Promise<void> {
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

        return new Promise((resolve, reject) => {
            this._commandQueue.push({ script, scriptPath, workspaceRoot, interactiveMode, masterPassword: launchOptions?.masterPassword, onMessage, resolve, reject });
            
            const startExecution = async () => {
                try {
                    if (this._startPromise) {
                        await this._startPromise;
                    } else if (!this._process) {
                        this._currentSessionId = sessionId;
                        this._startPromise = this._start(exePath, args, launchOptions);
                        await this._startPromise;
                        this._startPromise = undefined;
                    }
                    this._processNext();
                } catch {
                    // _start() rejected (process exited before ready).
                    // The close handler already rejected all queued commands.
                    this._startPromise = undefined;
                }
            };

            startExecution();
        });
    }

    public cancel() {
        if (this._process) {
            this._outputChannel?.appendLine('[REPL] Sending cancel request...');
            this._process.stdin?.write(JSON.stringify({ Action: "cancel" }) + "\r\n");
        }
    }

    public rollback() {
        if (this._process) {
            this._outputChannel?.appendLine('[REPL] Sending rollback request...');
            this._process.stdin?.write(JSON.stringify({ Action: "run", Script: "ROLLBACK;" }) + "\r\n");
        }
    }
    private async _start(exePath: string, args: string[], launchOptions?: ReplLaunchOptions): Promise<void> {
        return new Promise<void>((resolve, reject) => {
            const absoluteExePath = path.resolve(exePath);
            const startMsg = `Starting ETL-SQL REPL: "${absoluteExePath}" ui repl ${this._redactArgs(args).join(' ')}`;
            this._outputChannel?.appendLine(startMsg);

            // Snapshot the generation at spawn time. If stop() is called before
            // this process closes, _generation increments and the close handler
            // knows not to touch commands that belong to the newer session.
            const myGeneration = this._generation;

            const child = cp.spawn(absoluteExePath, ["ui", "repl", ...args], {
                env: { ...process.env, ...launchOptions?.env, "FORCE_COLOR": "0" }
            });
            this._process = child;

            let becameReady = false;

            // All JSON protocol messages (status, message, results, done) come on stdout.
            let buffer = '';
            child.stdout?.on('data', (data) => {
                buffer += data.toString();
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    const trimmed = line.trim();
                    if (!trimmed) {
                        continue;
                    }
                    try {
                        const msg = JSON.parse(trimmed);
                        this._handleMessage(msg);
                        if (msg.type === 'status' && msg.status === 'ready') {
                            this._outputChannel?.appendLine(`[ENGINE] Ready. Build: ${msg.buildId || 'v1.0'}`);
                            becameReady = true;
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
            child.stderr?.on('data', (data) => {
                const text = data.toString().trimEnd();
                if (text) {
                    this._outputChannel?.appendLine(text);
                }
            });

            child.on('close', (code) => {
                this._outputChannel?.appendLine(`REPL process exited (code ${code}).`);

                // If stop() was called and a new session has since started, don't
                // touch state or commands that belong to the newer process.
                if (this._generation !== myGeneration) {
                    return;
                }

                this._startPromise = undefined;
                this._process = undefined;
                this._isReady = false;

                // If the process exited before ever becoming ready, reject the
                // start promise so startExecution() doesn't hang indefinitely.
                if (!becameReady) {
                    reject(new Error(`REPL process exited with code ${code ?? 1} before becoming ready`));
                }

                // Reject any in-flight command so the caller's promise doesn't hang.
                if (this._currentHandler) {
                    const handler = this._currentHandler;
                    this._currentHandler = undefined;
                    handler({ type: 'done', exitCode: code ?? 1 });
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
            if (cmd.onMessage) {
                cmd.onMessage(msg);
            }
            if (msg.type === 'done') {
                clearTimeout(timeout);
                this._currentHandler = undefined;
                // Forward done to webview so the spinner stops.
                ResultsPanel.postMessage(msg);
                this._outputChannel?.appendLine(`[PROCESS] Command finished with code ${msg.exitCode}`);
                if (msg.exitCode === 0) {
                    cmd.resolve();
                } else {
                    cmd.reject(new Error("Execution failed"));
                }
                this._processNext();
            } else {
                // Forward all other messages (results, message, performance) to the webview.
                ResultsPanel.postMessage(msg);
            }
        };

        this._outputChannel?.appendLine(`[PROCESS] Running script (${cmd.script.length} bytes)...`);
        const command = { 
            Action: "run", 
            Script: cmd.script, 
            ScriptPath: cmd.scriptPath, 
            WorkspaceRoot: cmd.workspaceRoot,
            InteractiveMode: cmd.interactiveMode,
            MasterPassword: cmd.masterPassword
        };
        const payload = JSON.stringify(command);
        const loggedPayload = JSON.stringify({
            ...command,
            MasterPassword: command.MasterPassword ? '********' : undefined
        });
        this._outputChannel?.appendLine(`[REPL] STDIN write: ${loggedPayload}`);
        const ok = this._process?.stdin?.write(payload + "\r\n", 'utf8');
        this._outputChannel?.appendLine(`[REPL] STDIN ok: ${ok}`);
    }

    private _handleMessage(msg: EngineMessage) {
        if (msg.type === 'pong') {
            this._outputChannel?.appendLine(`[REPL] Heartbeat: PONG received.`);
            return;
        }
        if (msg.type === 'status' && msg.status === 'ready') {
            this._outputChannel?.appendLine(`[REPL] Engine ready. Sending ping...`);
            this._process?.stdin?.write(JSON.stringify({ Action: "ping" }) + "\r\n", 'utf8');
        }
        // Log text messages to the output channel (once, here).
        if (msg.type === 'message') {
            const prefix = msg.level === 'error' ? '[ERROR] ' : (msg.level === 'warning' ? '[WARN] ' : '');
            this._outputChannel?.appendLine(`${prefix}${msg.text}`);
        }

        if (msg.type === 'variables') {
            this._onVariablesChange.fire(msg.data as unknown[]);
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
        this._startPromise = undefined;

        this._generation++;
        const p = this._process;
        this._process = undefined;

        if (p) {
            p.stdin?.write(JSON.stringify({ Action: "exit" }) + "\r\n");
            p.kill();
        }
    }
    
    public isRunning(): boolean {
        return this._isRunning;
    }

    public warmup(exePath: string, args: string[], launchOptions?: ReplLaunchOptions): void {
        if (this._process) {
            return;
        }
        const sessionIdx = args.indexOf('--session');
        if (sessionIdx !== -1 && sessionIdx + 1 < args.length) {
            this._currentSessionId = args[sessionIdx + 1];
        }
        this._start(exePath, args, launchOptions).catch(() => {
            // Warmup failures are silent; execute() will retry when the user runs.
            this._process = undefined;
            this._isReady = false;
            this._currentSessionId = undefined;
        });
    }

    private _redactArgs(args: string[]): string[] {
        const redacted = [...args];
        const passIdx = redacted.indexOf('--pass');
        if (passIdx !== -1 && passIdx + 1 < redacted.length) {
            redacted[passIdx + 1] = '********';
        }
        return redacted;
    }
}
