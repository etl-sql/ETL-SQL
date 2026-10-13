import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { Writable } from 'node:stream';
import { setTimeout as delay } from 'node:timers/promises';
import { runOwnedCommand } from '../lib/run-owned-command.mjs';
import { createHash } from 'node:crypto';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const temporary = fs.mkdtempSync(path.join(root, 'artifacts', 'owned-command espaces-é-$-'));
const quiet = new Writable({ write(_bytes, _encoding, done) { done(); } });
const environmentSignature = () => createHash('sha256').update(JSON.stringify(Object.keys(process.env).sort()
    .map(key => [key, process.env[key]]))).digest('hex');
const until = async predicate => {
    const deadline = Date.now() + 15000;
    while (!predicate()) { assert.ok(Date.now() < deadline, 'Owned process did not reach its expected state.'); await delay(25); }
};
const alive = pid => {
    try {
        process.kill(pid, 0);
        if (process.platform === 'linux' && /\) Z /.test(fs.readFileSync(`/proc/${pid}/stat`, 'utf8'))) return false;
        return true;
    } catch (error) { if (['ESRCH', 'ENOENT'].includes(error.code)) return false; throw error; }
};
let peer;
try {
    const environment = environmentSignature();
    const options = { cwd: temporary, label: 'process fixture', capture: true, stdout: quiet, stderr: quiet };
    const result = await runOwnedCommand(process.execPath, ['-e', 'process.stdout.write(JSON.stringify(process.argv.slice(1)))', 'é $ literal'], options);
    assert.deepEqual(JSON.parse(result.stdout), ['é $ literal']);
    await assert.rejects(runOwnedCommand(process.execPath, ['-e', 'process.exit(7)'], options), /failed.*7/);
    await assert.rejects(runOwnedCommand(path.join(temporary, 'missing-executable'), [], options), /ENOENT/);
    const preCancelled = new AbortController();
    preCancelled.abort(new Error('Already cancelled.'));
    await assert.rejects(runOwnedCommand(process.execPath, ['-e', 'throw new Error("must not start")'], { ...options, signal: preCancelled.signal }), /Already cancelled/);
    await assert.rejects(runOwnedCommand(process.execPath, ['-e', 'process.stdout.write("too much");setInterval(()=>{},1000)'],
        { ...options, maxOutputBytes: 2 }), /excessive command output/);
    const childScript = path.join(temporary, 'child.mjs');
    const heartbeat = path.join(temporary, 'heartbeat.txt');
    const pids = path.join(temporary, 'pids.json');
    fs.writeFileSync(childScript, `import fs from 'node:fs';
setInterval(() => fs.writeFileSync(process.argv[2], String(Date.now())), 25);
`);
    const parentScript = path.join(temporary, 'parent.mjs');
    fs.writeFileSync(parentScript, `import fs from 'node:fs';
import { spawn } from 'node:child_process';
const child = spawn(process.execPath, [process.argv[2], process.argv[3]], { stdio: 'ignore' });
fs.writeFileSync(process.argv[4], JSON.stringify([process.pid, child.pid]));
setInterval(() => {}, 1000);
`);
    peer = spawn(process.execPath, ['-e', 'setInterval(()=>{},1000)'], { windowsHide: true, stdio: 'ignore' });
    const controller = new AbortController();
    const cancelled = runOwnedCommand(process.execPath, [parentScript, childScript, heartbeat, pids], { ...options, signal: controller.signal });
    const rejected = assert.rejects(cancelled, /Fixture cancellation/);
    await until(() => fs.existsSync(pids) && fs.existsSync(heartbeat));
    const ownedPids = JSON.parse(fs.readFileSync(pids, 'utf8'));
    controller.abort(new Error('Fixture cancellation.'));
    await rejected;
    await until(() => ownedPids.every(pid => !alive(pid)));
    assert.equal(alive(peer.pid), true, 'An unrelated peer must survive cancellation.');
    const stoppedHeartbeat = fs.readFileSync(heartbeat, 'utf8');
    await delay(100);
    assert.equal(fs.readFileSync(heartbeat, 'utf8'), stoppedHeartbeat);
    assert.equal(environmentSignature(), environment, 'Child toolchain selection must preserve the caller environment.');
    console.log('Owned commands: success/failure, missing tools, bounded output, cancellation, descendant shutdown, and unrelated-peer preservation passed.');
} finally {
    if (peer && peer.exitCode === null) {
        const closed = new Promise(resolve => peer.once('close', resolve));
        peer.kill();
        await closed;
    }
    assert.equal(path.dirname(temporary), path.join(root, 'artifacts'));
    assert.equal(fs.lstatSync(temporary).isSymbolicLink(), false);
    fs.rmSync(temporary, { recursive: true, force: true });
}
