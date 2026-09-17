// Exercise the real sync command in an isolated tree, including its failure paths.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
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
    for (const name of readdirSync(path.join(repo, relative))) {
        if (name === 'report-runtime.js' || /^rt-.*\.js$/.test(name)) {
            copyFileSync(path.join(repo, relative, name), path.join(shared, name));
            // This isolated fixture tests bundle syntax mutations, not TypeScript ownership.
            const copied = path.join(shared, name);
            writeFileSync(copied, readFileSync(copied, 'utf8').replace('/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.', '/* Bundle test fixture.'));
        }
    }

    fails(/Offline bundle missing/, '--check');
    succeeds();
    const expected = readFileSync(bundle, 'utf8');
    new Script(expected);
    succeeds('--check');
    writeFileSync(bundle, expected + '\n// tampered\n');
    fails(/Offline bundle drifted/, '--check');
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
    console.log('runtime bundle: generation, parse, drift, and rejected-input checks passed');
} finally {
    rmSync(work, { recursive: true, force: true });
}
