import path from 'node:path';
import { spawn } from 'node:child_process';
import { nodeCommandEnvironment } from './node-command-environment.mjs';

// Each command owns its process group/console. Cancellation targets that child PID/tree only.
// Node: https://nodejs.org/docs/latest-v22.x/api/child_process.html#optionsdetached
// Windows: https://learn.microsoft.com/windows-server/administration/windows-commands/taskkill
export async function runOwnedCommand(command, args, { cwd, label, capture = false, signal,
    maxOutputBytes = 32 * 1024 * 1024, shutdownTimeoutMs = 5000,
    stdout = process.stderr, stderr = process.stderr }) {
    signal?.throwIfAborted();
    if (!Number.isFinite(shutdownTimeoutMs) || shutdownTimeoutMs < 1) throw new Error('Invalid shutdown timeout.');
    process.stderr.write(`[VSIX] ${label}\n`);
    return await new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd, env: nodeCommandEnvironment(), detached: true,
            windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
        const chunks = [];
        let size = 0;
        let problem;
        let closed = false;
        let shutdown;
        let settled = false;
        const finish = error => {
            if (settled) return;
            settled = true;
            signal?.removeEventListener('abort', abort);
            if (error) reject(error);
            else resolve({ stdout: Buffer.concat(chunks).toString('utf8') });
        };
        const failedShutdown = error => {
            // A denied or stalled tree terminator must not leave the caller waiting for close.
            // Direct-child termination is a fallback, not evidence that descendants were stopped.
            try { child.kill('SIGKILL'); } catch { /* Preserve the shutdown error below. */ }
            child.stdout.destroy();
            child.stderr.destroy();
            child.unref();
            finish(new Error(`${problem?.message ?? label} Owned command shutdown failed: ${error.message}`, { cause: error }));
        };
        const stop = () => {
            if (shutdown || closed || !child.pid) return;
            shutdown = new Promise((finish, fail) => {
                if (process.platform === 'win32') {
                    const systemRoot = process.env.SystemRoot ?? process.env.SYSTEMROOT;
                    if (!systemRoot) return fail(new Error('Cannot locate Windows process-tree shutdown.'));
                    const terminator = spawn(path.join(systemRoot, 'System32/taskkill.exe'),
                        ['/pid', String(child.pid), '/t', '/f'], { windowsHide: true, stdio: 'ignore' });
                    const deadline = setTimeout(() => {
                        terminator.kill('SIGKILL');
                        terminator.unref();
                        fail(new Error(`Owned command tree shutdown exceeded ${shutdownTimeoutMs}ms.`));
                    }, shutdownTimeoutMs);
                    terminator.once('error', error => { clearTimeout(deadline); fail(error); });
                    terminator.once('close', code => {
                        clearTimeout(deadline);
                        if (code === 0 || closed) finish();
                        else fail(new Error(`Could not stop owned command tree (${code}).`));
                    });
                } else {
                    try { process.kill(-child.pid, 'SIGKILL'); finish(); }
                    catch (error) { if (error.code === 'ESRCH') finish(); else fail(error); }
                }
            });
            shutdown.catch(failedShutdown);
        };
        const abort = () => { problem ??= signal.reason ?? new Error(`${label} cancelled.`); stop(); };
        signal?.addEventListener('abort', abort, { once: true });
        if (signal?.aborted) abort();
        child.stdout.on('data', chunk => {
            if (!capture) stdout.write(chunk);
            else {
                size += chunk.length;
                if (size > maxOutputBytes) { problem ??= new Error(`${label}: excessive command output.`); stop(); }
                else chunks.push(chunk);
            }
        });
        child.stderr.on('data', chunk => stderr.write(chunk));
        child.once('error', error => { problem ??= error; });
        child.once('close', async (code, exitSignal) => {
            closed = true;
            signal?.removeEventListener('abort', abort);
            try { await shutdown; } catch (error) { problem = error; }
            if (problem) finish(problem);
            else if (code !== 0) finish(new Error(`${label} failed (${exitSignal ?? code}).`));
            else finish();
        });
    });
}
