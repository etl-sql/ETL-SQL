import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { pipeline } from 'node:stream/promises';
import { loadVsixToolchain } from './lib/vsix-toolchain.cjs';
import { createUnsignedVsix, inspectVsix, resolveVsixPlatform, vsixTargets } from './lib/vsix-package.mjs';
import { setVsixExecutableModes } from './lib/vsix-executable-modes.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
fs.mkdirSync(path.join(root, 'artifacts'), { recursive: true });
const temporary = fs.mkdtempSync(path.join(root, 'artifacts', 'vsix-check espaces-é-$-'));
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const write = (relative, bytes) => {
    const destination = path.join(temporary, relative);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, bytes);
    return destination;
};
try {
    loadVsixToolchain(root);
    assert.deepEqual(resolveVsixPlatform(undefined, 'darwin', 'x64'), { platform: 'osx-x64', target: 'darwin-x64' });
    assert.deepEqual(resolveVsixPlatform(undefined, 'darwin', 'arm64'), { platform: 'osx-arm64', target: 'darwin-arm64' });
    assert.throws(() => resolveVsixPlatform(undefined, 'linux', 'arm64'), /Unsupported/);
    assert.throws(() => resolveVsixPlatform('win-arm64'), /Unsupported/);
    for (const [platform, target] of Object.entries(vsixTargets)) assert.deepEqual(resolveVsixPlatform(platform), { platform, target });
    const signer = createRequire(path.join(root, 'scripts/vsix/node_modules/@vscode/vsce/package.json'))('@vscode/vsce-sign');
    for (const method of ['generateManifest', 'verify', 'zip']) assert.throws(() => signer[method](), /signing is not available/);
    const lock = JSON.parse(fs.readFileSync(path.join(root, 'scripts/vsix/package-lock.json'), 'utf8'));
    assert.deepEqual(Object.keys(lock.packages).filter(name => name.includes('vsce-sign')), ['node_modules/@vscode/vsce-sign']);
    assert.equal(lock.packages['node_modules/@vscode/vsce-sign'].link, true);
    assert.equal(lock.packages['node_modules/@vscode/vsce-sign'].resolved, 'unsigned-signing');

    const identity = { name: 'etlsql-vsix-check', publisher: 'etl-sql', version: '0.20.1' };
    const manifest = { ...identity, displayName: 'ETL-SQL VSIX check', description: 'Packaging fixture',
        license: 'Apache-2.0', repository: { type: 'git', url: 'https://github.com/etl-sql/ETL-SQL' },
        engines: { vscode: '^1.91.0' }, activationEvents: ['onStartupFinished'], main: './out/extension.js', dependencies: {} };
    const manifestPath = write('extension/package.json', JSON.stringify(manifest));
    const files = {
        'out/extension.js': 'exports.activate = () => {};\n',
        'ui/dist/index.html': '<html><body>fresh UI</body></html>\n',
        'media/report-runtime.js': 'export const fresh = true;\n',
        'LICENSE.md': '# Apache-2.0\n',
        'THIRD-PARTY-NOTICES.md': 'Fixture build dependencies use MIT.\n',
        'bin/ETL-SQL': 'executable mode fixture\n',
    };
    const expectedFiles = {};
    for (const [name, bytes] of Object.entries(files)) {
        write(`extension/${name}`, bytes);
        expectedFiles[name] = digest(bytes);
    }
    write('extension/README.md', '# ETL-SQL packaging fixture\n');
    const assembly = path.dirname(manifestPath);
    const cwdBefore = process.cwd();
    const cancellation = new AbortController();
    cancellation.abort(new Error('Cancelled archive operation.'));
    const cancelledOutput = path.join(temporary, 'cancelled.vsix');
    await assert.rejects(createUnsignedVsix(root, assembly, cancelledOutput, 'linux-x64', { signal: cancellation.signal }), /Cancelled archive operation/);
    assert.equal(fs.existsSync(cancelledOutput), false);
    for (const target of Object.values(vsixTargets)) {
        const output = path.join(temporary, `${identity.name}-${identity.version}-${target}.vsix`);
        const executables = ['bin/ETL-SQL'];
        await createUnsignedVsix(root, assembly, output, target, { executables });
        const inspected = await inspectVsix(root, output, { identity, target, expectedFiles, executables });
        await assert.rejects(inspectVsix(root, output, { identity, target, expectedFiles, signal: cancellation.signal }), /Cancelled archive operation/);
        await assert.rejects(setVsixExecutableModes(output, executables, { signal: cancellation.signal }), /Cancelled archive operation/);
        assert.equal(inspected.target, target);
        assert.equal(inspected.manifest.version, identity.version);
        await assert.rejects(inspectVsix(root, output, { identity: { ...identity, version: '0.21.0' }, target, expectedFiles }), /version/);
        await assert.rejects(inspectVsix(root, output, { identity, target: 'unsupported', expectedFiles }), /target metadata/);
        await assert.rejects(inspectVsix(root, output, { identity, target, expectedFiles: { ...expectedFiles, 'ui/dist/index.html': digest('old UI') } }), /stale.*index.html/);
        await assert.rejects(inspectVsix(root, output, { identity, target, expectedFiles: { 'media/missing.js': digest('missing') } }), /Missing or stale/);
        const accepted = fs.readFileSync(output);
        await assert.rejects(createUnsignedVsix(root, assembly, output, target), /existing packages must be preserved/);
        assert.deepEqual(fs.readFileSync(output), accepted);
        await assert.rejects(setVsixExecutableModes(output, ['bin/missing']), /Unexpected/);
        await assert.rejects(setVsixExecutableModes(output, ['bin/ETL-SQL-LSP']), /Missing executable/);
        assert.deepEqual(fs.readFileSync(output), accepted, 'Invalid mode requests must not change the archive.');
        const truncated = write(`broken-${target}.vsix`, accepted.subarray(0, accepted.length - 64));
        await assert.rejects(inspectVsix(root, truncated, { identity, target, expectedFiles }));
        await assert.rejects(setVsixExecutableModes(truncated, executables), /ZIP/);
    }
    const executable = path.join(assembly, 'bin/ETL-SQL');
    fs.chmodSync(executable, 0o644);
    const nonExecutable = path.join(temporary, 'non-executable.vsix');
    await createUnsignedVsix(root, assembly, nonExecutable, 'linux-x64');
    await assert.rejects(inspectVsix(root, nonExecutable, { identity, target: 'linux-x64', expectedFiles,
        executables: ['bin/ETL-SQL'] }), /executable permissions/);
    const yazl = createRequire(path.join(root, 'scripts/vsix/node_modules/@vscode/vsce/package.json'))('yazl');
    const zip64 = new yazl.ZipFile();
    const zip64File = path.join(temporary, 'forced-zip64.zip');
    zip64.addBuffer(Buffer.from('ZIP64 payload'), 'extension/bin/ETL-SQL', { mode: 0o100644 });
    const zip64Written = pipeline(zip64.outputStream, fs.createWriteStream(zip64File));
    zip64.end({ forceZip64Format: true });
    await zip64Written;
    await setVsixExecutableModes(zip64File, ['bin/ETL-SQL']);
    await new Promise((resolve, reject) => {
        loadVsixToolchain(root).yauzl.open(zip64File, { lazyEntries: true }, (error, zip) => {
            if (error) return reject(error);
            zip.on('error', reject);
            zip.on('entry', entry => {
                assert.equal(entry.versionMadeBy >>> 8, 3);
                assert.equal((entry.externalFileAttributes >>> 16) & 0o177777, 0o100755);
                zip.openReadStream(entry, (streamError, stream) => {
                    if (streamError) return reject(streamError);
                    const chunks = [];
                    stream.on('data', bytes => chunks.push(bytes));
                    stream.on('error', reject);
                    stream.on('end', () => { assert.equal(Buffer.concat(chunks).toString(), 'ZIP64 payload'); zip.readEntry(); });
                });
            });
            zip.on('end', resolve);
            zip.readEntry();
        });
    });
    assert.equal(process.cwd(), cwdBefore);
    const prepublish = { ...manifest, scripts: { 'vscode:prepublish': 'node should-never-run.cjs' } };
    fs.writeFileSync(manifestPath, JSON.stringify(prepublish));
    const unsafeOutput = path.join(temporary, 'prepublish.vsix');
    await assert.rejects(createUnsignedVsix(root, assembly, unsafeOutput, 'linux-x64'), /prepublish script/);
    assert.equal(fs.existsSync(unsafeOutput), false);
    fs.writeFileSync(manifestPath, JSON.stringify(manifest));
    write('extension/out/test/dev.js', 'console.log("test output");\n');
    const devOutput = path.join(temporary, 'dev-output.vsix');
    await createUnsignedVsix(root, assembly, devOutput, 'linux-x64');
    await assert.rejects(inspectVsix(root, devOutput, { identity, target: 'linux-x64', expectedFiles }), /development output/);

    // Exercise preflight failures in a separate toolchain, without modifying the checkout install.
    for (const relative of ['package.json', 'package-lock.json', 'unsigned-signing/package.json'])
        write(`preflight/scripts/vsix/${relative}`, fs.readFileSync(path.join(root, 'scripts/vsix', relative)));
    const fixtureRoot = path.join(temporary, 'preflight');
    assert.throws(() => loadVsixToolchain(fixtureRoot), /Missing or invalid node_modules/);
    const fixtureLock = path.join(fixtureRoot, 'scripts/vsix/package-lock.json');
    const originalLock = JSON.parse(JSON.stringify(lock));
    lock.packages['node_modules/@vscode/vsce-sign'] = { version: '2.1.0', license: 'SEE LICENSE IN LICENSE.txt' };
    fs.writeFileSync(fixtureLock, JSON.stringify(lock));
    assert.throws(() => loadVsixToolchain(fixtureRoot), /unsigned signing replacement is missing/);
    const fixtureManifest = path.join(fixtureRoot, 'scripts/vsix/package.json');
    const changed = JSON.parse(fs.readFileSync(fixtureManifest, 'utf8'));
    changed.engines.node = '24.x';
    fs.writeFileSync(fixtureManifest, JSON.stringify(changed));
    assert.throws(() => loadVsixToolchain(fixtureRoot), /disagree/);
    fs.copyFileSync(path.join(root, 'scripts/vsix/package.json'), fixtureManifest);
    fs.writeFileSync(fixtureLock, JSON.stringify(originalLock));
    const installed = JSON.parse(fs.readFileSync(path.join(root, 'scripts/vsix/node_modules/.package-lock.json'), 'utf8'));
    for (const relative of Object.keys(installed.packages)) {
        if (!relative.startsWith('node_modules/') || relative === 'node_modules/@vscode/vsce-sign') continue;
        write(`preflight/scripts/vsix/${relative}/package.json`, fs.readFileSync(path.join(root, 'scripts/vsix', relative, 'package.json')));
    }
    installed.packages['node_modules/@vscode/vsce'].integrity = 'wrong';
    write('preflight/scripts/vsix/node_modules/.package-lock.json', JSON.stringify(installed));
    assert.throws(() => loadVsixToolchain(fixtureRoot), /restored node_modules\/@vscode\/vsce does not match/);
    installed.packages['node_modules/@vscode/vsce'].integrity = originalLock.packages['node_modules/@vscode/vsce'].integrity;
    write('preflight/scripts/vsix/node_modules/.package-lock.json', JSON.stringify(installed));
    write('preflight/scripts/vsix/node_modules/@vscode/vsce/package.json', JSON.stringify({ version: '3.0.0' }));
    assert.throws(() => loadVsixToolchain(fixtureRoot), /restored node_modules\/@vscode\/vsce does not match/);
    originalLock.packages['node_modules/@vscode/vsce'].license = 'SEE LICENSE IN LICENSE.txt';
    fs.writeFileSync(fixtureLock, JSON.stringify(originalLock));
    assert.throws(() => loadVsixToolchain(fixtureRoot), /Unapproved or unpinned dependency/);
    console.log('VSIX toolchain: four real target archives validated; stale/missing payloads, wrong identity/target, broken archives, unsigned policy, and preflight failures rejected.');
} finally {
    // Only this mkdtemp result is removed. No development outputs or accepted packages are touched.
    fs.rmSync(temporary, { recursive: true, force: true });
}
