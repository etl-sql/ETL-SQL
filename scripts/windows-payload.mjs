import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { lstat, readdir, readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { isDeepStrictEqual } from 'node:util';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const windowsHosts = Object.freeze([
    ['ETL-SQL.App', 'ETL-SQL'], ['ETL-SQL.TUI', 'ETL-SQL-TUI'],
    ['ETL-SQL.LanguageServer', 'ETL-SQL-LSP'], ['ETL-SQL.ReportBuilder.CLI', 'ETL-SQL-Report'],
    ['ETL-SQL.ReportPlayer', 'ETL-SQL-Player'], ['ETL-SQL.WorkstationEditor', 'ETL-SQL-Editor'],
    ['ETL-SQL.Portal', 'ETL-SQL-Portal'], ['ETL-SQL.Orchestrator.Service', 'ETL-SQL-Service'],
].map(([project, executable]) => ({ project: `src/${project}/${project}.csproj`, executable: `${executable}.exe` })));
export const windowsPublishOptions = Object.freeze({ configuration: 'Release', rid: 'win-x64',
    selfContained: true, singleFile: true, readyToRun: true, includeNativeLibrariesForSelfExtract: true });
const manifestName = 'windows-payload.json';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const identity = (sha, version) => {
    if (!/^[a-f0-9]{40}$/.test(sha) || !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version)) throw new Error('Invalid Windows payload identity.');
};
async function directory(file) {
    const info = await lstat(file);
    if (!info.isDirectory() || info.isSymbolicLink()) throw new Error('Windows payload paths must be ordinary directories.');
}
async function inventory(root) {
    await directory(root);
    const bin = path.join(root, 'bin');
    await directory(bin);
    const patterns = (await readFile(new URL('./lib/package-runtime-state.patterns', import.meta.url), 'utf8'))
        .split(/\r?\n/).filter(line => line && !line.startsWith('#'))
        .map(pattern => new RegExp('^' + pattern.split('*').map(part => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('.*') + '$', 'i'));
    const files = [], names = new Set();
    let bytes = 0;
    async function walk(relative = '', depth = 0) {
        if (depth > 32) throw new Error('Windows payload nesting exceeds its limit.');
        for (const name of (await readdir(path.join(bin, relative))).sort()) {
            if (/[\\:\x00-\x1f]/.test(name) || name === '.' || name === '..') throw new Error('Unsafe Windows payload filename.');
            if (!relative && patterns.some(pattern => pattern.test(name))) throw new Error('Runtime state in Windows payload.');
            const nested = relative ? `${relative}/${name}` : name;
            const info = await lstat(path.join(bin, nested));
            if (info.isSymbolicLink()) throw new Error('Linked Windows payload input.');
            if (info.isDirectory()) { await walk(nested, depth + 1); continue; }
            if (!info.isFile() || names.has(nested.toLowerCase()) || files.length >= 10000) throw new Error('Invalid Windows payload inventory.');
            names.add(nested.toLowerCase()); bytes += info.size;
            if (bytes > 8 * 1024 ** 3) throw new Error('Windows payload exceeds its byte limit.');
            const hash = createHash('sha256');
            for await (const chunk of createReadStream(path.join(bin, nested))) hash.update(chunk);
            files.push({ path: nested, bytes: info.size, sha256: hash.digest('hex') });
        }
    }
    await walk();
    for (const name of [...windowsHosts.map(host => host.executable), 'appsettings.json']) {
        if (!files.some(file => file.path === name && file.bytes > 0)) throw new Error(`Missing Windows payload input: ${name}`);
    }
    return files;
}

export async function createWindowsPayload(root, sha, version, dotnetVersion) {
    identity(sha, version);
    if (!/^v22\.\d+\.\d+$/.test(process.version) || !/^10\.\d+\.\d+$/.test(dotnetVersion)) throw new Error('Windows payload requires Node 22 and .NET 10.');
    const value = { schemaVersion: 1, scope: 'WindowsBuildInputsOnly', sourceSha: sha, version,
        options: windowsPublishOptions, hosts: windowsHosts,
        toolchain: { nodeVersion: process.version, dotnetVersion }, files: await inventory(root) };
    const contents = JSON.stringify(value, null, 2) + '\n';
    await writeFile(path.join(root, manifestName), contents);
    return digest(contents);
}

export async function verifyWindowsPayload(root, sha, version, expectedHash) {
    identity(sha, version);
    const file = path.join(root, manifestName), info = await lstat(file);
    if (!info.isFile() || info.isSymbolicLink() || info.size > 8 * 1024 ** 2) throw new Error('Invalid Windows payload manifest.');
    const contents = await readFile(file);
    if (!/^[a-f0-9]{64}$/.test(expectedHash ?? '') || digest(contents) !== expectedHash) throw new Error('Windows payload manifest hash mismatch.');
    const value = JSON.parse(contents);
    if (value.schemaVersion !== 1 || value.scope !== 'WindowsBuildInputsOnly' || value.sourceSha !== sha || value.version !== version ||
        !isDeepStrictEqual(value.options, windowsPublishOptions) || !isDeepStrictEqual(value.hosts, windowsHosts) ||
        !/^v22\.\d+\.\d+$/.test(value.toolchain?.nodeVersion ?? '') || !/^10\.\d+\.\d+$/.test(value.toolchain?.dotnetVersion ?? '') ||
        !isDeepStrictEqual(value.files, await inventory(root))) throw new Error('Windows payload identity, options or files changed.');
    return value;
}

export async function copyWindowsPayload(root, sha, version, expectedHash, destination) {
    if (path.basename(destination) !== 'bin') throw new Error('Windows payload destination must be an empty bin directory.');
    const manifest = await verifyWindowsPayload(root, sha, version, expectedHash);
    await directory(destination);
    if ((await readdir(destination)).length) throw new Error('Windows payload copy requires an empty destination.');
    for (const file of manifest.files) {
        const target = path.join(destination, file.path);
        await mkdir(path.dirname(target), { recursive: true });
        await copyFile(path.join(root, 'bin', file.path), target);
    }
    // Verify the copy and source before packaging; neither partial nor changed input is accepted.
    if (!isDeepStrictEqual(await inventory(path.dirname(destination)), manifest.files)) throw new Error('Copied Windows payload differs.');
    await verifyWindowsPayload(root, sha, version, expectedHash);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        const [mode, root, sha, version, hashOrSdk, destination] = process.argv.slice(2);
        if (mode === 'create' && process.argv.length === 7) console.log(await createWindowsPayload(root, sha, version, hashOrSdk));
        else if (mode === 'verify' && process.argv.length === 7) { await verifyWindowsPayload(root, sha, version, hashOrSdk); console.log(hashOrSdk); }
        else if (mode === 'copy' && process.argv.length === 8) { await copyWindowsPayload(root, sha, version, hashOrSdk, destination); console.log(hashOrSdk); }
        else if (mode === 'projects' && process.argv.length === 3) console.log(JSON.stringify(windowsHosts));
        else throw new Error('Usage: windows-payload.mjs projects | create <root> <sha> <version> <sdk> | verify <root> <sha> <version> <hash> | copy <root> <sha> <version> <hash> <empty-bin>');
    } catch (error) { console.error(error.message); process.exitCode = 1; }
}
