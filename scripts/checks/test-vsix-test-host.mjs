import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { prepareVsixTestHost, readVsixTestHost } from '../lib/vsix-test-host.mjs';

const root = await fs.mkdtemp(path.join(os.tmpdir(), 'vsix-test-host-'));
try {
    const binary = path.join(root, '.vscode-test/host/code');
    await fs.mkdir(path.dirname(binary), { recursive: true });
    await fs.writeFile(binary, 'synthetic host');
    let calls = 0;
    const receipt = await prepareVsixTestHost(root, async options => {
        calls++; assert.equal(options.cachePath, path.join(root, '.vscode-test')); return binary;
    });
    const file = path.join(root, 'host.json');
    await fs.writeFile(file, JSON.stringify(receipt));
    assert.equal(await readVsixTestHost(root, file), binary);
    assert.equal(calls, 1, 'Receipt consumption must not resolve or download VS Code.');
    await fs.writeFile(binary, 'changed host');
    await assert.rejects(readVsixTestHost(root, file), /host changed/);
    for (const patch of [{ executable: '../../outside' }, { platform: 'wrong' }, { architecture: 'wrong' }, { sha256: 'invalid' }]) {
        await fs.writeFile(file, JSON.stringify({ ...receipt, ...patch }));
        await assert.rejects(readVsixTestHost(root, file));
    }
    await assert.rejects(prepareVsixTestHost(root, async () => { throw new Error('download unavailable'); }), /download unavailable/);
    await assert.rejects(prepareVsixTestHost(root, async () => path.join(root, 'outside')), /regular file/);
    await fs.writeFile(file, 'x'.repeat(16385));
    await assert.rejects(readVsixTestHost(root, file), /too large/);
    await fs.rename(path.join(root, '.vscode-test'), path.join(root, 'saved-cache'));
    await fs.symlink(path.join(root, 'saved-cache'), path.join(root, '.vscode-test'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(prepareVsixTestHost(root, async () => { throw new Error('Must not download through a linked cache.'); }), /Linked VS Code cache/);
    await fs.unlink(path.join(root, '.vscode-test'));
    const workflow = await fs.readFile(new URL('../../.github/workflows/native-packages.yml', import.meta.url), 'utf8');
    assert.ok(workflow.indexOf('prepare-vsix-acceptance.mjs') < workflow.indexOf('- name: Build native ZIP and VSIX'));
    assert.equal((workflow.match(/test-vsix-acceptance\.mjs --vsix \$vsix --out \$outDir --host-receipt/g) ?? []).length, 2);
    console.log('VS Code host: early download failure, offline receipt consumption, hash/platform/path rejection and workflow ordering passed.');
} finally {
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    await fs.rm(root, { recursive: true, force: true });
}
