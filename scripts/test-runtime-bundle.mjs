// Exercise the real sync command in an isolated tree, including its failure paths.
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Script } from 'node:vm';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const tempBase = existsSync(path.join(repo, 'scratch')) ? path.join(repo, 'scratch') : tmpdir();
const work = mkdtempSync(path.join(tempBase, 'etlsql-runtime-bundle-'));
const relative = 'src/ETL-SQL.ReportRuntime/Resources/Shared';
const shared = path.join(work, relative);
const bundle = path.join(shared, 'report-runtime.bundle.js');
const command = path.join(work, 'scripts/sync-assets.js');
const run = (...args) => spawnSync(process.execPath, [command, ...args], { encoding: 'utf8' });
const succeeds = (...args) => {
    const result = run(...args);
    assert.equal(result.status, 0, result.stderr);
};
const fails = (pattern, ...args) => {
    const result = run(...args);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, pattern);
};

try {
    mkdirSync(shared, { recursive: true });
    mkdirSync(path.dirname(command));
    copyFileSync(path.join(repo, 'scripts/sync-assets.js'), command);
    copyFileSync(path.join(repo, 'scripts/compile-browser.mjs'), path.join(work, 'scripts/compile-browser.mjs'));
    mkdirSync(path.join(work, 'scripts/lib'));
    copyFileSync(path.join(repo, 'scripts/lib/browser-asset-inputs.cjs'), path.join(work, 'scripts/lib/browser-asset-inputs.cjs'));
    copyFileSync(path.join(repo, 'scripts/lib/browser-toolchain.cjs'), path.join(work, 'scripts/lib/browser-toolchain.cjs'));
    cpSync(path.join(repo, 'scripts/typecheck'), path.join(work, 'scripts/typecheck'), { recursive: true, filter: () => true });
    mkdirSync(path.join(shared, 'designer'));
    // The sync validates each host's import closure even when only bundle syntax is under test.
    writeFileSync(path.join(shared, 'designer/designer.js'), 'export const designer = {};\n');
    writeFileSync(path.join(shared, 'designer/connection-wizard.js'), 'export const wizard = {};\n');
    writeFileSync(path.join(shared, 'designer/designer.css'), '/* bundle fixture */\n');
    for (const name of readdirSync(path.join(repo, relative))) {
        if (name === 'report-runtime.js' || /^rt-.*\.js$/.test(name)) {
            copyFileSync(path.join(repo, relative, name), path.join(shared, name));
            // This isolated fixture tests bundle syntax mutations, not TypeScript ownership.
            const copied = path.join(shared, name);
            writeFileSync(copied, readFileSync(copied, 'utf8').replace('/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.', '/* Bundle test fixture.'));
        }
    }

    fails(/missing or stale/, '--check');
    succeeds();
    const expected = readFileSync(bundle, 'utf8');
    new Script(expected);
    succeeds('--check');
    writeFileSync(bundle, expected + '\n// tampered\n');
    fails(/retained or locally edited/, '--check');
    fails(/retained or locally edited/);
    assert.equal(readFileSync(bundle, 'utf8'), expected + '\n// tampered\n', 'Preparation must preserve an output edit until explicitly reconciled.');
    rmSync(bundle); // Remove only this fixture's deliberately edited generated file.
    succeeds();
    assert.equal(readFileSync(bundle, 'utf8'), expected);

    const probe = path.join(shared, 'rt-probe.js');
    writeFileSync(probe, 'export const probe = 1;\n');
    fails(/not listed in RUNTIME_PARTS/);
    rmSync(probe);

    const part = path.join(shared, 'rt-util.js');
    const source = readFileSync(part, 'utf8');
    for (const [extra, error] of [
        ["import { absent } from './rt-absent.js';\n", /not listed in RUNTIME_PARTS/],
        ["import { escHtml as renamed } from './rt-util.js';\n", /cannot inline/],
        ["import { external } from 'external';\n", /cannot inline/],
        ["const later = () => import('./rt-state.js');\n", /dynamic import/],
        ['export { escHtml };\n', /cannot strip/],
        ['const parameters = {};\n', /already been declared/],
    ]) {
        writeFileSync(part, source + '\n' + extra);
        fails(error);
        // A rejected source must not overwrite the last valid bundle.
        assert.equal(readFileSync(bundle, 'utf8'), expected);
    }
    writeFileSync(part, source);
    succeeds('--check');
    // Exercise the CLI, not just the exported watcher: importing an entry module which is still
    // awaiting startup creates an evaluation cycle that an in-process watcher test cannot see.
    const lock = path.join(work, '.browser-assets.lock');
    for (const phase of process.platform === 'win32' ? ['idle'] : ['during-preparation', 'idle']) {
        assert.equal(existsSync(lock), false);
        const child = spawn(process.execPath, [path.join(work, 'scripts/compile-browser.mjs'), '--watch'],
            { stdio: ['ignore', 'pipe', 'pipe'] });
        let output = '', terminal;
        child.stdout.on('data', value => { output += value; });
        child.stderr.on('data', value => { output += value; });
        const exit = new Promise((resolve, reject) => {
            child.once('exit', (code, signal) => { terminal = { code, signal }; resolve(terminal); });
            child.once('error', reject);
        });
        try {
            const deadline = Date.now() + 15000;
            while (phase === 'idle' ? !output.includes('prepared') : !existsSync(lock)) {
                assert.equal(terminal, undefined, output);
                assert.ok(Date.now() < deadline, `Watcher did not reach ${phase}: ${output}`);
                await new Promise(resolve => setTimeout(resolve, 1));
            }
            child.kill('SIGTERM');
            const outcome = await Promise.race([exit, new Promise((_, reject) => {
                const timer = setTimeout(() => reject(new Error(`Watcher did not stop: ${output}`)), 15000);
                timer.unref();
            })]);
            // Windows kill() forcibly ends a process; POSIX delivers the graceful signal handler.
            if (process.platform !== 'win32') assert.equal(outcome.code, 0, output);
            assert.equal(existsSync(lock), false, 'Watcher shutdown must leave no preparation lock.');
        } finally { if (!terminal) { child.kill(); await exit; } }
    }
    succeeds('--check');
    console.log('runtime bundle: generation, parse, drift, and rejected-input checks passed');
} finally {
    rmSync(work, { recursive: true, force: true });
}
