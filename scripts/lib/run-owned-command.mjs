import path from 'node:path';
import { spawn } from 'node:child_process';
import { nodeCommandEnvironment } from './node-command-environment.mjs';

// Each command owns its process group/console. Cancellation targets that child PID/tree only.
// Node: https://nodejs.org/docs/latest-v22.x/api/child_process.html#optionsdetached
// Windows: https://learn.microsoft.com/windows-server/administration/windows-commands/taskkill
export async function runOwnedCommand(command, args, { cwd, label, capture = false, signal,
    maxOutputBytes = 32 * 1024 * 1024, stdout = process.stderr, stderr = process.stderr }) {
    signal?.throwIfAborted();
    process.stderr.write(`[VSIX] ${label}\n`);
    return await new Promise((resolve, reject) => {
        const child = spawn(command, args, { cwd, env: nodeCommandEnvironment(), detached: true,
            windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
        const chunks = [];
        let size = 0;
        let problem;
        let closed = false;
        let shutdown;
        const stop = () => {
            if (shutdown || closed || !child.pid) return;
            shutdown = new Promise((finish, fail) => {
                if (process.platform === 'win32') {
                    const systemRoot = process.env.SystemRoot ?? process.env.SYSTEMROOT;
                    if (!systemRoot) return fail(new Error('Cannot locate Windows process-tree shutdown.'));
                    const terminator = spawn(path.join(systemRoot, 'System32/taskkill.exe'),
                        ['/pid', String(child.pid), '/t', '/f'], { windowsHide: true, stdio: 'ignore' });
                    terminator.once('error', fail);
                    terminator.once('close', code => code === 0 || closed ? finish() :
                        fail(new Error(`Could not stop owned command tree (${code}).`)));
                } else {
                    try { process.kill(-child.pid, 'SIGKILL'); finish(); }
                    catch (error) { if (error.code === 'ESRCH') finish(); else fail(error); }
                }
            });
            // Retain shutdown failures for close handling without an unhandled rejection.
            shutdown.catch(error => { problem = error; });
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
            if (problem) reject(problem);
            else if (code !== 0) reject(new Error(`${label} failed (${exitSignal ?? code}).`));
            else resolve({ stdout: Buffer.concat(chunks).toString('utf8') });
        });
    });
}
