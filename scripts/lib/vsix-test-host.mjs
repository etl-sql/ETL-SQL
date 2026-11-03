import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import fs from 'node:fs/promises';
import path from 'node:path';

async function hostFile(root, relative) {
    const cache = path.resolve(root, '.vscode-test');
    if (typeof relative !== 'string' || !relative || path.isAbsolute(relative) || /[\r\n]/.test(relative)) throw new Error('Invalid VS Code host path.');
    const executable = path.resolve(cache, relative);
    const contained = (candidate, base = cache) => candidate.startsWith(base + path.sep);
    if (!contained(executable) || (await fs.lstat(cache)).isSymbolicLink() ||
        !contained(await fs.realpath(executable), await fs.realpath(cache)) || !(await fs.stat(executable)).isFile()) {
        throw new Error('VS Code host must be a regular file inside the local test cache.');
    }
    const hash = createHash('sha256');
    for await (const chunk of createReadStream(executable)) hash.update(chunk);
    return { executable, sha256: hash.digest('hex') };
}

export async function prepareVsixTestHost(root, download) {
    const cachePath = path.resolve(root, '.vscode-test');
    await fs.mkdir(cachePath, { recursive: true });
    if ((await fs.lstat(cachePath)).isSymbolicLink()) throw new Error('Linked VS Code cache is not supported.');
    const executable = await download({ cachePath });
    const relative = path.relative(cachePath, executable);
    const host = await hostFile(root, relative);
    return { schemaVersion: 1, platform: process.platform, architecture: process.arch, executable: relative, sha256: host.sha256 };
}

export async function readVsixTestHost(root, receiptPath) {
    // Size-check and read through one handle so the checked file is the read file.
    const handle = await fs.open(receiptPath, 'r');
    let text;
    try {
        if ((await handle.stat()).size > 16384) throw new Error('VS Code host receipt is too large.');
        text = await handle.readFile('utf8');
    } finally { await handle.close(); }
    const receipt = JSON.parse(text);
    if (receipt.schemaVersion !== 1 || receipt.platform !== process.platform || receipt.architecture !== process.arch ||
        !/^[a-f0-9]{64}$/.test(receipt.sha256)) throw new Error('Invalid VS Code host receipt.');
    const host = await hostFile(root, receipt.executable);
    if (host.sha256 !== receipt.sha256) throw new Error('Prepared VS Code host changed.');
    return host.executable;
}
