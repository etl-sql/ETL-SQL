import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { EventEmitter } from 'events';

// ── Fake child process ─────────────────────────────────────────────────────────
// A controllable stand-in for cp.ChildProcess. Tests call emitLine() to simulate
// what the engine writes to stdout.

class FakeChildProcess extends EventEmitter {
    stdin = { write: vi.fn(), end: vi.fn() };
    stdout = new EventEmitter();
    stderr = new EventEmitter();
    kill = vi.fn();

    /** Simulate engine writing a single JSON line to stdout. */
    emitLine(obj: object) {
        this.stdout.emit('data', JSON.stringify(obj) + '\n');
    }

    /** Simulate engine writing multiple JSON objects in one data chunk. */
    emitLines(...objs: object[]) {
        this.stdout.emit('data', objs.map(o => JSON.stringify(o)).join('\n') + '\n');
    }

    /** Simulate JSON split across two data chunks (tests buffer accumulation). */
    emitChunked(obj: object) {
        const line = JSON.stringify(obj) + '\n';
        const mid = Math.floor(line.length / 2);
        this.stdout.emit('data', line.slice(0, mid));
        this.stdout.emit('data', line.slice(mid));
    }

    close(code = 0) {
        this.emit('close', code);
    }
}

// ── child_process mock ─────────────────────────────────────────────────────────
// vi.mock() is hoisted to the top of the file by Vitest, so it runs before imports.

let fakeProcess: FakeChildProcess;

vi.mock('child_process', () => ({
    spawn: vi.fn(() => {
        fakeProcess = new FakeChildProcess();
        return fakeProcess;
    })
}));

// ── Static imports (after mocks so they pick up the mocked child_process) ──────

import * as cp from 'child_process';
import { ReplManager } from '../ReplManager';
import { ResultsPanel } from '../resultsPanel';

// ── Test helpers ──────────────────────────────────────────────────────────────

const FAKE_EXE = 'ETL-SQL.exe';
const SESSION = 'test-session';

function makeOutputChannel() {
    return { appendLine: vi.fn(), append: vi.fn() } as any;
}

/**
 * Starts the REPL for a script and completes the ready handshake.
 * Returns the pending execPromise plus helpers to drive the fake engine.
 *
 * We deliberately do NOT await execPromise here — tests do that themselves
 * so they can interleave engine events with the await.
 *
 * Timing note: _start() is an `async` function returning a Promise, so
 * execute()'s continuation needs 2 microtask hops after resolve() is called:
 *   hop 1 — inner Promise → _start()'s async wrapper Promise
 *   hop 2 — _start()'s async wrapper → execute()'s await _start() continuation
 * We therefore need 2 awaits after emitting 'ready'.
 */
async function startRepl(script = 'SELECT 1;', sessionId = SESSION) {
    const mgr = ReplManager.getInstance();
    const outputChannel = makeOutputChannel();
    mgr.setOutputChannel(outputChannel);

    const args = ['--verbose', '--session', sessionId];
    const execPromise = mgr.execute(script, FAKE_EXE, args);

    await Promise.resolve(); // let _start() spawn the process and attach listeners
    fakeProcess.emitLine({ type: 'status', status: 'ready' }); // resolves _start()'s inner Promise
    await Promise.resolve(); // hop 1: inner → _start()'s async wrapper resolves
    await Promise.resolve(); // hop 2: execute()'s continuation runs, _processNext sets _currentHandler

    return { mgr, execPromise, outputChannel };
}

// ── Suite ──────────────────────────────────────────────────────────────────────

describe('ReplManager', () => {
    let mgr: ReplManager;

    beforeEach(() => {
        vi.clearAllMocks();
        mgr = ReplManager.getInstance();
        // Reset singleton state left by previous tests.
        mgr.stop();
    });

    afterEach(() => {
        mgr.stop();
    });

    // ── Process lifecycle ──────────────────────────────────────────────────────

    it('spawns process with "repl" as first arg', async () => {
        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        expect(cp.spawn).toHaveBeenCalledOnce();
        const spawnArgs = (cp.spawn as ReturnType<typeof vi.fn>).mock.calls[0][1];
        expect(spawnArgs[0]).toBe('repl');
    });

    it('passes session flag in spawn args', async () => {
        const { execPromise } = await startRepl('SELECT 1;', 'session-xyz');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const spawnArgs = (cp.spawn as ReturnType<typeof vi.fn>).mock.calls[0][1] as string[];
        const idx = spawnArgs.indexOf('--session');
        expect(idx).toBeGreaterThan(-1);
        expect(spawnArgs[idx + 1]).toBe('session-xyz');
    });

    it('does not spawn a second process for the same session', async () => {
        const { execPromise } = await startRepl('SELECT 1;', 'shared');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        // Second call — same session, process already alive.
        const exec2 = mgr.execute('SELECT 2;', FAKE_EXE, ['--session', 'shared']);
        await Promise.resolve();
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await exec2;

        expect(cp.spawn).toHaveBeenCalledOnce();
    });

    it('restarts process when session ID changes', async () => {
        const { execPromise } = await startRepl('SELECT 1;', 'session-a');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        // Different session — should restart (same 2-hop timing as startRepl).
        const exec2 = mgr.execute('SELECT 2;', FAKE_EXE, ['--session', 'session-b']);
        await Promise.resolve(); // let new _start() spawn and attach listeners
        fakeProcess.emitLine({ type: 'status', status: 'ready' });
        await Promise.resolve(); // hop 1
        await Promise.resolve(); // hop 2 — execute continuation runs, _currentHandler set
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await exec2;

        expect(cp.spawn).toHaveBeenCalledTimes(2);
    });

    it('sends run command to stdin after ready', async () => {
        const { execPromise } = await startRepl('SELECT 42;');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const written = fakeProcess.stdin.write.mock.calls.map((c: any) => JSON.parse(c[0]));
        const runCmd = written.find((w: any) => w.action === 'run');
        expect(runCmd).toBeDefined();
        expect(runCmd!.script).toBe('SELECT 42;');
    });

    // ── Message routing ────────────────────────────────────────────────────────

    it('forwards result messages to ResultsPanel', async () => {
        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'results', columns: ['id'], rows: [[1]] });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        expect(spy).toHaveBeenCalledWith(expect.objectContaining({ type: 'results' }));
        spy.mockRestore();
    });

    it('writes info messages to output channel', async () => {
        const { execPromise, outputChannel } = await startRepl();
        fakeProcess.emitLine({ type: 'message', level: 'info', text: 'Connection created.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.includes('Connection created.'))).toBe(true);
    });

    it('prefixes error messages in output channel with [ERROR]', async () => {
        const { execPromise, outputChannel } = await startRepl();
        fakeProcess.emitLine({ type: 'message', level: 'error', text: 'Something broke.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 1 });
        await execPromise.catch(() => {});

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.startsWith('[ERROR]') && l.includes('Something broke.'))).toBe(true);
    });

    it('logs each message exactly once to the output channel', async () => {
        // Previously: _handleMessage AND _currentHandler both logged — tripling output.
        // Fix: logging only in _handleMessage; _currentHandler only forwards to ResultsPanel.
        const { execPromise, outputChannel } = await startRepl();
        fakeProcess.emitLine({ type: 'message', level: 'info', text: 'Connection m created.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        const matches = logged.filter(l => l.includes('Connection m created.'));
        expect(matches).toHaveLength(1);
    });

    it('resolves when exitCode is 0', async () => {
        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await expect(execPromise).resolves.not.toThrow();
    });

    it('rejects when exitCode is non-zero', async () => {
        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'done', exitCode: 1 });
        await expect(execPromise).rejects.toThrow();
    });

    // ── Buffer handling ────────────────────────────────────────────────────────

    it('handles JSON split across multiple data chunks', async () => {
        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl();
        fakeProcess.emitChunked({ type: 'results', columns: ['x'], rows: [[42]] });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        expect(spy).toHaveBeenCalledWith(expect.objectContaining({ type: 'results' }));
        spy.mockRestore();
    });

    it('handles multiple JSON objects in a single data chunk', async () => {
        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl();
        fakeProcess.emitLines(
            { type: 'message', level: 'info', text: 'Step 1' },
            { type: 'message', level: 'info', text: 'Step 2' },
            { type: 'done', exitCode: 0 }
        );
        await execPromise;

        spy.mockRestore();
    });

    it('ignores non-JSON lines on stdout and routes them to the output channel', async () => {
        const { execPromise, outputChannel, mgr } = await startRepl();
        mgr.setDebugMode(true);
        fakeProcess.stdout.emit('data', 'Build info: debug\n');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.includes('[Engine]') && l.includes('Build info: debug'))).toBe(true);
    });

    it('routes stderr lines to output channel without parsing as JSON', async () => {
        const { execPromise, outputChannel } = await startRepl();
        fakeProcess.stderr.emit('data', '[PROC_START] ETL-SQL Engine process identified.\n');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.includes('[PROC_START]'))).toBe(true);
    });

    // ── Queue ──────────────────────────────────────────────────────────────────

    it('queues a second script and runs it after the first completes', async () => {
        const { execPromise } = await startRepl('SELECT 1;');

        // Queue second before first finishes.
        const exec2 = mgr.execute('SELECT 2;', FAKE_EXE, ['--session', SESSION]);
        await Promise.resolve();

        // Finish first.
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        // Now second should be in flight.
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await exec2;

        const written = fakeProcess.stdin.write.mock.calls.map((c: any) => JSON.parse(c[0]));
        const runs = written.filter((w: any) => w.action === 'run');
        expect(runs).toHaveLength(2);
        expect(runs[0].script).toBe('SELECT 1;');
        expect(runs[1].script).toBe('SELECT 2;');
    });

    // ── Stop / cleanup ─────────────────────────────────────────────────────────

    it('sends exit command and calls kill() on stop()', async () => {
        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        mgr.stop();

        const written = fakeProcess.stdin.write.mock.calls.map((c: any) => JSON.parse(c[0]));
        expect(written.some((w: any) => w.action === 'exit')).toBe(true);
        expect(fakeProcess.kill).toHaveBeenCalled();
    });

    it('does not throw if stop() is called when no process is running', () => {
        // mgr.stop() was already called in beforeEach — calling again should be safe.
        expect(() => mgr.stop()).not.toThrow();
    });

    // ── Regression tests ───────────────────────────────────────────────────────
    // These tests reproduce past failures so they cannot silently regress.

    it('[regression] REPL signals ready on stdout immediately — no startup hang', async () => {
        // Previously: ReplUi was loading session state (with Task.WhenAny + Task.Delay)
        // before emitting "ready". Task.Delay deadlocked under System.CommandLine's
        // sync context, causing the REPL to hang forever and never send "ready".
        // Fix: ready is now emitted at startup with no async work beforehand.

        const outputChannel = makeOutputChannel();
        mgr.setOutputChannel(outputChannel);

        // Start execute() — it calls _start() and awaits the ready signal.
        const execPromise = mgr.execute('SELECT 1;', FAKE_EXE, ['--session', SESSION]);
        await Promise.resolve(); // let _start() spawn + attach listeners

        // Engine sends ready immediately on stdout (no session loading delay).
        fakeProcess.emitLine({ type: 'status', status: 'ready' });
        await Promise.resolve(); // hop 1
        await Promise.resolve(); // hop 2 — _processNext runs

        // Engine processes the run command and sends done.
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });

        // Must resolve — if it hangs, this will time out.
        await expect(execPromise).resolves.not.toThrow();
    });

    it('[regression] all JSON protocol messages are on stdout — done resolves execPromise', async () => {
        // Previously: ExecuteScript wrote results/done to Console.Error (stderr).
        // ReplManager reads protocol from stdout only, so done was never received
        // and execPromise never resolved, leaving the spinner running forever.
        // Fix: all protocol messages (message, results, done) are now on stdout.

        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl('SELECT * FROM m.Users;');

        // Engine sends results then done — both on stdout.
        fakeProcess.emitLine({ type: 'results', isFirst: true, columns: ['id', 'name'], rows: [{ id: 1, name: 'Alice' }] });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });

        await expect(execPromise).resolves.not.toThrow();
        expect(spy).toHaveBeenCalledWith(expect.objectContaining({ type: 'results' }));
        spy.mockRestore();
    });

    it('[regression] done is forwarded to ResultsPanel so the webview spinner stops', async () => {
        // Previously: _currentHandler handled 'done' internally (resolve/reject) but never
        // forwarded it to ResultsPanel. The webview's onExecutionDone() was never called,
        // leaving the spinner running forever even after the script finished.

        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        expect(spy).toHaveBeenCalledWith(expect.objectContaining({ type: 'done', exitCode: 0 }));
        spy.mockRestore();
    });

    it('[regression] results packet includes isFirst:true so webview creates the grid', async () => {
        // Previously: results packet had no isFirst field. renderResults() checked if(data.isFirst)
        // to create the Tabulator grid container. Without it, the else branch ran addData() on a
        // non-existent table — results were silently dropped and nothing rendered.

        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl();
        fakeProcess.emitLine({ type: 'results', isFirst: true, columns: ['x'], rows: [{ x: 1 }] });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const resultsCalls = spy.mock.calls.map((c: any) => c[0]).filter((m: any) => m.type === 'results');
        expect(resultsCalls.length).toBeGreaterThan(0);
        expect(resultsCalls[0].isFirst).toBe(true);
        spy.mockRestore();
    });

    it('[regression] run command uses lowercase JSON keys matching C# case-insensitive deserializer', async () => {
        // Previously: ReplCommand deserialized with case-sensitive System.Text.Json defaults.
        // { "action": "run", "script": "..." } — lowercase keys — left Script=null because
        // the C# property was named "Script" (capital S). Parser got empty source → 0 statements → no results.
        // Fix: PropertyNameCaseInsensitive = true on deserialization.
        // This test verifies ReplManager sends lowercase keys that match the protocol.

        const { execPromise } = await startRepl('SELECT 1;');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const written = fakeProcess.stdin.write.mock.calls.map((c: any) => JSON.parse(c[0]));
        const runCmd = written.find((w: any) => w.action === 'run');
        expect(runCmd).toBeDefined();
        // Keys must be lowercase so C# case-insensitive deserialization can map them.
        expect(Object.keys(runCmd)).toContain('action');
        expect(Object.keys(runCmd)).toContain('script');
        expect(runCmd.action).toBe('run');
        expect(runCmd.script).toBe('SELECT 1;');
    });

    it('[regression] stderr diagnostic lines do not block protocol processing', async () => {
        // Previously: stderr was parsed as JSON protocol. Now stderr is raw diagnostics only.
        // A large burst of stderr output (e.g. .NET startup noise) must not block
        // stdout protocol messages from being processed.

        const { execPromise } = await startRepl();

        // Simulate .NET startup writing a lot to stderr simultaneously.
        fakeProcess.stderr.emit('data', '[PROC_START] Engine starting.\n[DI_READY] DI complete.\n');

        // stdout protocol continues to work normally.
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await expect(execPromise).resolves.not.toThrow();
    });

    // ── Warning messages ───────────────────────────────────────────────────────

    it('prefixes warning messages in output channel with [WARN]', async () => {
        const { execPromise, outputChannel } = await startRepl();
        fakeProcess.emitLine({ type: 'message', level: 'warning', text: 'Implicit conversion detected.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.startsWith('[WARN]') && l.includes('Implicit conversion detected.'))).toBe(true);
    });

    it('info messages have no prefix in output channel', async () => {
        const { execPromise, outputChannel } = await startRepl();
        fakeProcess.emitLine({ type: 'message', level: 'info', text: 'Table created.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        const match = logged.find(l => l.includes('Table created.'));
        expect(match).toBeDefined();
        expect(match).not.toMatch(/^\[ERROR\]|\[WARN\]/);
    });

    // ── b5/b6: engine execution messages ──────────────────────────────────────
    // These verify that messages emitted by the C# engine during SQL execution
    // (e.g. "Table created.", "3 row(s) affected.") flow through to the output channel.

    it('[b5] "Table created." message from engine appears in output channel', async () => {
        const { execPromise, outputChannel } = await startRepl('CREATE TABLE #tmp (id INT);');
        fakeProcess.emitLine({ type: 'message', level: 'info', text: 'Table created.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.includes('Table created.'))).toBe(true);
    });

    it('[b6] "N row(s) affected." message from engine appears in output channel', async () => {
        const { execPromise, outputChannel } = await startRepl('INSERT INTO t VALUES (1);');
        fakeProcess.emitLine({ type: 'message', level: 'info', text: '1 row(s) affected.' });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const logged: string[] = outputChannel.appendLine.mock.calls.map((c: any) => c[0]);
        expect(logged.some(l => l.includes('row(s) affected.'))).toBe(true);
    });

    // ── Multiple result sets ───────────────────────────────────────────────────

    it('forwards all result set packets to ResultsPanel', async () => {
        const spy = vi.spyOn(ResultsPanel, 'postMessage').mockImplementation(() => {});

        const { execPromise } = await startRepl('SELECT 1; SELECT 2;');
        fakeProcess.emitLine({ type: 'results', isFirst: true,  columns: ['a'], rows: [{ a: 1 }] });
        fakeProcess.emitLine({ type: 'results', isFirst: false, columns: ['b'], rows: [{ b: 2 }] });
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        const resultsCalls = spy.mock.calls.map((c: any) => c[0]).filter((m: any) => m.type === 'results');
        expect(resultsCalls).toHaveLength(2);
        expect(resultsCalls[0].isFirst).toBe(true);
        expect(resultsCalls[1].isFirst).toBe(false);
        spy.mockRestore();
    });

    // ── Process crash recovery ─────────────────────────────────────────────────

    it('rejects in-flight execute() promise when process closes unexpectedly', async () => {
        // If the engine crashes mid-execution the close event fires without a "done" packet.
        // Previously: _currentHandler was never called, leaving execute() hung forever.
        // Fix: close handler now calls _currentHandler with a synthetic done(exitCode=1).

        const { execPromise } = await startRepl('SELECT SLEEP(100);');

        // Simulate abrupt crash — no "done" packet first.
        fakeProcess.close(1);

        await expect(execPromise).rejects.toThrow();
    });

    it('rejects queued commands when process closes unexpectedly', async () => {
        const { execPromise } = await startRepl('SELECT 1;');

        // Queue a second command.
        const exec2 = mgr.execute('SELECT 2;', FAKE_EXE, ['--session', SESSION]);
        await Promise.resolve();

        // Process crashes — both should reject.
        fakeProcess.close(1);

        await expect(execPromise).rejects.toThrow();
        await expect(exec2).rejects.toThrow();
    });

    // ── execute() after stop() ─────────────────────────────────────────────────

    it('starts a fresh process when execute() is called after stop()', async () => {
        const { execPromise } = await startRepl('SELECT 1;');
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await execPromise;

        mgr.stop();

        // New execute after stop — should spawn a new process.
        const exec2 = mgr.execute('SELECT 2;', FAKE_EXE, ['--session', SESSION]);
        await Promise.resolve();
        fakeProcess.emitLine({ type: 'status', status: 'ready' });
        await Promise.resolve();
        await Promise.resolve();
        fakeProcess.emitLine({ type: 'done', exitCode: 0 });
        await exec2;

        expect(cp.spawn).toHaveBeenCalledTimes(2);
    });
});
