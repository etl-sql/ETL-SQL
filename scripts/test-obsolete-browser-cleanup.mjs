import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cleanObsoleteBrowserOutputs } from './clean-obsolete-browser-outputs.mjs';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-cleanup-é-'));
const source = 'src/ETL-SQL.Portal/BrowserSources/test.ts';
const output = 'src/ETL-SQL.Portal/wwwroot/js/test.js';
const authored = 'src/ETL-SQL.Portal/wwwroot/js/notes.js';
const bytes = 'export const value = 1;\n';
const write = (file, content) => {
    fs.mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
    fs.writeFileSync(path.join(root, file), content);
};
const git = (...args) => {
    const result = spawnSync('git', ['-C', root, ...args], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
};
const inventory = () => write('artifacts/browser-assets.json', JSON.stringify({ version: 2, outputs: [{
    kind: 'typescript', source, path: output, sha256: createHash('sha256').update(bytes).digest('hex'),
}] }));
try {
    git('init', '-q');
    inventory();
    write(output, bytes);
    write(authored, 'authored work');
    assert.deepEqual(cleanObsoleteBrowserOutputs({ root }), [output]);
    assert.equal(fs.readFileSync(path.join(root, output), 'utf8'), bytes);
    write(output, 'local edit');
    assert.throws(() => cleanObsoleteBrowserOutputs({ root, apply: true }), /locally changed/);
    assert.equal(fs.readFileSync(path.join(root, output), 'utf8'), 'local edit');
    write(output, bytes);
    git('add', '--', output);
    assert.deepEqual(cleanObsoleteBrowserOutputs({ root, apply: true }), []);
    git('rm', '--cached', '--', output);
    assert.deepEqual(cleanObsoleteBrowserOutputs({ root, apply: true }), [output]);
    assert.equal(fs.existsSync(path.join(root, output)), false);
    assert.equal(fs.readFileSync(path.join(root, authored), 'utf8'), 'authored work');
    assert.deepEqual(cleanObsoleteBrowserOutputs({ root, apply: true }), []);
    write('artifacts/browser-assets.json', JSON.stringify({ version: 2, outputs: [{
        kind: 'typescript', source, path: '../escape.js', sha256: 'a'.repeat(64),
    }] }));
    assert.throws(() => cleanObsoleteBrowserOutputs({ root, apply: true }), /Invalid browser ownership path/);
    inventory();
    const actual = path.join(root, 'actual');
    fs.mkdirSync(actual);
    fs.renameSync(path.join(root, 'src/ETL-SQL.Portal/wwwroot/js'), path.join(actual, 'js'));
    fs.symlinkSync(path.join(actual, 'js'), path.join(root, 'src/ETL-SQL.Portal/wwwroot/js'), 'junction');
    assert.throws(() => cleanObsoleteBrowserOutputs({ root, apply: true }), /symlink or junction/);
    console.log('obsolete browser cleanup: preview, preservation, hashes, traversal and junction checks passed');
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}
