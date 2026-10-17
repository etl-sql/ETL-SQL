import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { loadVsixToolchain } from './vsix-toolchain.cjs';
import { setVsixExecutableModes } from './vsix-executable-modes.mjs';
import { runOwnedCommand } from './run-owned-command.mjs';

export const vsixTargets = Object.freeze({
    'win-x64': 'win32-x64', 'linux-x64': 'linux-x64',
    'osx-x64': 'darwin-x64', 'osx-arm64': 'darwin-arm64',
});

export function resolveVsixPlatform(requested, host = process.platform, architecture = process.arch) {
    const platform = requested ?? ({ 'win32-x64': 'win-x64', 'linux-x64': 'linux-x64',
        'darwin-x64': 'osx-x64', 'darwin-arm64': 'osx-arm64' })[`${host}-${architecture}`];
    if (!Object.hasOwn(vsixTargets, platform ?? '')) throw new Error(`Unsupported VSIX platform: ${platform ?? `${host}-${architecture}`}.`);
    return { platform, target: vsixTargets[platform] };
}

// VSCE's file list already applies .vscodeignore, so matching that list cannot prove runtime
// entry points survived. For example vscode-languageserver-types requires lib/umd/main.js.
export function dependencyEntryPointProblems(metadata, entries) {
    const problems = [];
    for (const [name, content] of metadata) {
        if (!/^extension\/node_modules\/.*\/package\.json$/.test(name)) continue;
        const manifest = JSON.parse(content);
        if (typeof manifest.main !== 'string') continue;
        const base = path.posix.dirname(name);
        const entry = path.posix.normalize(path.posix.join(base, manifest.main));
        const candidates = [entry, `${entry}.js`, `${entry}.json`, `${entry}.node`,
            `${entry}/index.js`, `${entry}/index.json`, `${entry}/index.node`];
        if ((entry !== base && !entry.startsWith(base + '/')) || !candidates.some(candidate => entries.has(candidate)))
            problems.push(`Missing VSIX dependency entry point: ${name} -> ${manifest.main}.`);
    }
    return problems;
}

export async function createUnsignedVsix(root, assembly, output, target, { executables = [], signal } = {}) {
    signal?.throwIfAborted();
    const tools = loadVsixToolchain(root);
    if (!Object.values(vsixTargets).includes(target)) throw new Error(`Unsupported VSIX target: ${target}.`);
    for (const value of [assembly, output]) if (!path.isAbsolute(value)) throw new Error('VSIX assembly and output paths must be absolute.');
    if (fs.existsSync(output)) throw new Error('Assemble into a new temporary VSIX; existing packages must be preserved.');
    const manifest = JSON.parse(fs.readFileSync(path.join(assembly, 'package.json'), 'utf8'));
    if (manifest.scripts?.['vscode:prepublish']) throw new Error('Build the extension before assembly; the staged manifest must not have a prepublish script.');
    // Call the locked CLI with an argument array, never a shell/npx download fallback.
    // The staging directory has only runtime dependencies, so normal VSCE closure checks still run.
    await runOwnedCommand(process.execPath, [tools.cli, 'package', '--target', target, '--out', output,
        '--no-git-tag-version', '--no-update-package-json'], { cwd: assembly, label: 'VSIX assembly', signal });
    signal?.throwIfAborted();
    if (!fs.statSync(output).isFile()) throw new Error('The packager did not produce a VSIX.');
    await setVsixExecutableModes(output, executables, { signal });
}

// Read the actual archive, including binary entry hashes, without buffering a whole VSIX.
export async function inspectVsix(root, file, { identity, target, expectedFiles, executables = [], signal }) {
    signal?.throwIfAborted();
    const tools = loadVsixToolchain(root);
    const hashes = new Map();
    const metadata = new Map();
    const seen = new Set();
    const executableEntries = new Set(executables.map(name => `extension/${name}`));
    await new Promise((resolve, reject) => {
        tools.yauzl.open(file, { lazyEntries: true, strictFileNames: true, validateEntrySizes: true, autoClose: false }, (error, zip) => {
            if (error) return reject(error);
            let settled = false;
            let activeStream;
            let problem;
            const cleanup = () => signal?.removeEventListener('abort', abort);
            const complete = error => {
                if (settled) return;
                settled = true;
                problem = error;
                cleanup();
                activeStream?.destroy();
                zip.close();
            };
            const fail = error => complete(error);
            const abort = () => fail(signal.reason ?? new Error('VSIX inspection cancelled.'));
            signal?.addEventListener('abort', abort, { once: true });
            zip.on('error', error => { if (settled) reject(error); else fail(error); });
            // Finish after yauzl has released its file descriptor, including on cancellation.
            zip.once('close', () => problem ? reject(problem) : resolve());
            zip.on('end', () => complete());
            zip.on('entry', entry => {
                if (settled) return;
                const name = entry.fileName;
                const segments = name.replace(/\/$/, '').split('/');
                const key = name.replace(/\/$/, '').toLowerCase();
                if (segments.some(segment => !segment || segment === '.' || segment === '..' || segment.includes(':')) ||
                    name.includes('\\') || seen.has(key)) return fail(new Error(`Unsafe or duplicate VSIX entry: ${name}.`));
                seen.add(key);
                const unixType = (entry.externalFileAttributes >>> 16) & 0xf000;
                if (unixType === 0xa000) return fail(new Error(`VSIX contains a symbolic link: ${name}.`));
                if (executableEntries.has(name) && ((entry.versionMadeBy >>> 8) !== 3 || unixType !== 0x8000 ||
                    ((entry.externalFileAttributes >>> 16) & 0o111) !== 0o111))
                    return fail(new Error(`VSIX executable permissions are missing: ${name}.`));
                if (name.endsWith('/')) return zip.readEntry();
                if (name !== '[Content_Types].xml' && name !== 'extension.vsixmanifest' && !name.startsWith('extension/'))
                    return fail(new Error(`Unexpected VSIX entry: ${name}.`));
                if (/^extension\/(coverage|logs|out\/test|bin\/runtimes|runtimes)\//.test(name) ||
                    /(?:^|\/)test_output\.txt$|\.__test-|\.map$/.test(name))
                    return fail(new Error(`VSIX contains development output: ${name}.`));
                const isMetadata = name === 'extension/package.json' || name === 'extension.vsixmanifest' || name === '[Content_Types].xml' ||
                    /^extension\/node_modules\/.*\/package\.json$/.test(name);
                if (isMetadata && entry.uncompressedSize > 1024 * 1024) return fail(new Error(`Oversized VSIX metadata: ${name}.`));
                zip.openReadStream(entry, (streamError, stream) => {
                    if (streamError) return fail(streamError);
                    if (settled) { stream.destroy(); return; }
                    activeStream = stream;
                    const hash = createHash('sha256');
                    const chunks = [];
                    stream.on('data', chunk => { hash.update(chunk); if (isMetadata) chunks.push(chunk); });
                    stream.on('error', fail);
                    stream.on('end', () => {
                        if (settled) return;
                        activeStream = undefined;
                        hashes.set(name, hash.digest('hex'));
                        if (isMetadata) metadata.set(name, Buffer.concat(chunks).toString('utf8'));
                        zip.readEntry();
                    });
                });
            });
            if (signal?.aborted) { abort(); return; }
            zip.readEntry();
        });
    });
    signal?.throwIfAborted();
    for (const name of ['extension/package.json', 'extension.vsixmanifest', '[Content_Types].xml'])
        if (!metadata.has(name)) throw new Error(`Missing VSIX metadata: ${name}.`);
    const manifest = JSON.parse(metadata.get('extension/package.json'));
    const dependencyProblems = dependencyEntryPointProblems(metadata, hashes);
    if (dependencyProblems.length) throw new Error(dependencyProblems.join('\n'));
    for (const field of ['name', 'publisher', 'version'])
        if (manifest[field] !== identity[field]) throw new Error(`Packaged extension ${field} does not match ${identity[field]}.`);
    const xmlText = metadata.get('extension.vsixmanifest');
    if (/<!DOCTYPE|<!ENTITY/i.test(xmlText)) throw new Error('VSIX metadata must not declare XML entities.');
    const xml = await tools.parseXml(xmlText);
    signal?.throwIfAborted();
    const actual = xml.PackageManifest?.Metadata?.[0]?.Identity?.[0]?.$;
    if (actual?.Id !== identity.name || actual.Publisher !== identity.publisher ||
        actual.Version !== identity.version || actual.TargetPlatform !== target)
        throw new Error('Actual VSIX identity/version/target metadata does not match the requested package.');
    const main = manifest.main?.replace(/^\.\//, '');
    if (typeof main !== 'string' || !hashes.has(`extension/${main}`)) throw new Error('VSIX extension entry point is missing.');
    for (const [name, hash] of Object.entries(expectedFiles)) {
        if (hashes.get(`extension/${name}`) !== hash) throw new Error(`Missing or stale VSIX payload: ${name}.`);
    }
    for (const name of executableEntries) if (!hashes.has(name)) throw new Error(`Missing VSIX executable: ${name}.`);
    return { manifest, target: actual.TargetPlatform, hashes: Object.fromEntries(hashes) };
}
