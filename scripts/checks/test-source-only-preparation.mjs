// Failure paths of scripts/verify-source-only-preparation.mjs, with a stand-in for preparation so
// each wrong result can be produced on demand. The real preparation is exercised by running the
// script against the checkout.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { verifySourceOnlyPreparation } from '../verify-source-only-preparation.mjs';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-source-only-'));
const owner = 'src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-util.ts';
const emitted = 'src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';
const copy = 'src/ETL-SQL.ReportPlayer/wwwroot/rt-util.js';
const inventoryPath = 'artifacts/browser-assets.json';
const manifestPath = 'artifacts/browser-assets.props';
const sha = content => createHash('sha256').update(content).digest('hex');
const write = (file, content) => {
    fs.mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
    fs.writeFileSync(path.join(root, file), content);
};
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const good = { [emitted]: 'export const a = 1;\n', [copy]: '// copy\nexport const a = 1;\n' };
const inventory = contents => JSON.stringify({ version: 2, outputs: [
    { path: copy, kind: 'host-copy', source: emitted },
    { path: emitted, kind: 'typescript', source: owner },
].filter(entry => contents[entry.path] !== undefined)
    .map(entry => ({ ...entry, sha256: sha(contents[entry.path]) })) }, null, 2) + '\n';
// Stands in for preparation: writes whatever outputs it is told to, plus matching manifests.
const preparing = contents => async ({ check }) => {
    if (check) return;
    for (const [file, content] of Object.entries(contents)) write(file, content);
    write(inventoryPath, inventory(contents));
    write(manifestPath, '<Project />\n');
};
const reset = () => {
    for (const [file, content] of Object.entries(good)) write(file, content);
    write(inventoryPath, inventory(good));
    write(manifestPath, '<Project />\n');
};
const assertRestored = message => {
    for (const [file, content] of Object.entries(good)) assert.equal(read(file), content, `${message}: ${file}`);
    assert.equal(read(inventoryPath), inventory(good), `${message}: inventory`);
    assert.equal(read(manifestPath), '<Project />\n', `${message}: manifest`);
};
const run = prepare => verifySourceOnlyPreparation({ root, toolchain: root, prepare });

try {
    write(owner, 'export const a: number = 1;\n');
    reset();

    // The outputs really are absent while preparation runs.
    let absentDuringPreparation = false;
    const result = await run(async options => {
        if (!options.check && !absentDuringPreparation)
            absentDuringPreparation = [emitted, copy, inventoryPath, manifestPath].every(file => !fs.existsSync(path.join(root, file)));
        await preparing(good)(options);
    });
    assert.equal(absentDuringPreparation, true, 'Preparation must start from a tree with no outputs or manifests.');
    assert.equal(result.outputs.length, 2);

    // Different bytes, a missing output, and an extra output each fail and put the tree back.
    await assert.rejects(run(preparing({ ...good, [copy]: '// copy\nexport const a = 2;\n' })), /Output differs after source-only preparation: .*ReportPlayer/);
    assertRestored('Changed bytes must be rolled back');
    await assert.rejects(run(preparing({ [emitted]: good[emitted] })), /Output was not regenerated: .*ReportPlayer/);
    assertRestored('A missing output must be rolled back');
    await assert.rejects(run(async options => { await preparing(good)(options); throw new Error('compiler failed'); }), /compiler failed/);
    assertRestored('A failed preparation must be rolled back');

    // A hand-edited output is reported before anything is deleted.
    write(copy, good[copy] + '// local edit\n');
    let called = false;
    await assert.rejects(run(async () => { called = true; }), /missing or differs from the inventory: .*ReportPlayer/);
    assert.equal(called, false, 'An edited output must stop the run before deletion.');
    assert.equal(read(copy), good[copy] + '// local edit\n', 'The local edit must survive.');
    assert.equal(fs.existsSync(path.join(root, emitted)), true);
    reset();

    fs.rmSync(path.join(root, inventoryPath));
    await assert.rejects(run(preparing(good)), /inventory is missing.*sync-assets/);
    console.log('source-only preparation proof checks passed');
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}
