import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { createVsixCancellationFixture } from '../fixtures/vsix-cli-cancellation.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const temporary = fs.mkdtempSync(path.join(repository, 'artifacts', 'vsix-signal espaces-é-$-'));
const records = [];
const until = async predicate => {
    const deadline = Date.now() + 60000;
    while (!predicate()) { assert.ok(Date.now() < deadline, 'Public packaging did not reach its expected signal boundary.'); await delay(25); }
};
const alive = pid => {
    try {
        process.kill(pid, 0);
        if (process.platform === 'linux' && /\) Z /.test(fs.readFileSync(`/proc/${pid}/stat`, 'utf8'))) return false;
        return true;
    } catch (error) { if (['ESRCH', 'ENOENT'].includes(error.code)) return false; throw error; }
};
try {
    // Windows process.kill() force-terminates rather than delivering console Ctrl+C. The native
    // console check uses this same fixture through the terminal harness; do not claim it here.
    const consoleCheck = process.platform === 'win32' && process.argv.includes('--console');
    if (process.platform === 'win32' && !consoleCheck) {
        console.log('Public CLI signal delivery requires the Windows console harness; owned-tree cancellation is tested separately.');
    } else for (const signal of consoleCheck ? ['SIGINT'] : ['SIGINT', 'SIGTERM']) {
        const fixture = createVsixCancellationFixture(repository, path.join(temporary, signal));
        const previous = fs.readFileSync(fixture.accepted);
        let stdout = '';
        let stderr = '';
        const child = spawn(process.execPath, [path.join(fixture.root, 'scripts/package-vsix.mjs'), 'build',
            '--platform', 'win-x64', '--out-dir', fixture.outputDirectory], { cwd: temporary, stdio: ['ignore', 'pipe', 'pipe'],
            // Isolate the dependency-free fixture from a slow or damaged developer npm cache.
            env: { ...process.env, npm_config_cache: path.join(fixture.root, 'artifacts/npm-cache'), npm_config_offline: 'true' } });
        child.stdout.on('data', bytes => { stdout += bytes; });
        child.stderr.on('data', bytes => { stderr += bytes; });
        let result;
        const closed = new Promise((resolve, reject) => { child.on('error', reject); child.on('close', (code, exitSignal) => {
            result = { code, exitSignal }; resolve(result);
        }); });
        const observeInterrupt = () => {};
        if (consoleCheck) process.on('SIGINT', observeInterrupt);
        try {
            try { await until(() => {
                assert.equal(result, undefined, 'Public packaging exited before the npm lifecycle started.');
                return fs.existsSync(fixture.pids) && fs.existsSync(fixture.heartbeat);
            }); }
            catch (error) { throw new Error(`${error.message}\n${stderr}`, { cause: error }); }
            const ownedPids = JSON.parse(fs.readFileSync(fixture.pids, 'utf8'));
            const lockToken = fs.readFileSync(fixture.lock, 'utf8');
            assert.match(lockToken, new RegExp(`^${child.pid}:`));
            if (consoleCheck) console.log('Windows console fixture ready for Ctrl+C.');
            else child.kill(signal);
            await closed;
            assert.equal(result.exitSignal, null, 'Coordinator must handle the signal and exit after cleanup.');
            assert.equal(result.code, signal === 'SIGINT' ? 130 : 143);
            assert.match(stderr, /VSIX packaging cancelled/);
            assert.equal(stdout, '', 'Interrupted packaging must not report a completed package.');
            await until(() => ownedPids.every(pid => !alive(pid)));
            assert.equal(fs.existsSync(fixture.lock), false);
            assert.deepEqual(fs.readFileSync(fixture.accepted), previous);
            assert.deepEqual(fs.readdirSync(fixture.outputDirectory), [path.basename(fixture.accepted)]);
            records.push({ signal, ...result, ownedPids, descendantsStopped: true, acceptedPackagePreserved: true,
                stageRemoved: true, lockRemoved: true });
            console.log(`${signal}: exit ${result.code}, npm descendants stopped, accepted package preserved, stage and lock removed.`);
        } finally {
            if (consoleCheck) process.removeListener('SIGINT', observeInterrupt);
            if (child.exitCode === null && child.signalCode === null) { child.kill('SIGTERM'); await closed; }
        }
    }
    if (records.length) fs.writeFileSync(path.join(repository, 'artifacts/vsix-cli-cancellation-tests.json'),
        JSON.stringify({ platform: process.platform, node: process.version, console: consoleCheck, records }, null, 2));
} finally {
    assert.equal(path.dirname(temporary), path.join(repository, 'artifacts'));
    assert.equal(fs.lstatSync(temporary).isSymbolicLink(), false);
    fs.rmSync(temporary, { recursive: true, force: true });
}
