import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import synchronizer from '../sync-assets.js';
import { watchBrowser } from '../compile-browser.mjs';
import assetInputs from '../lib/browser-asset-inputs.cjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const entrypointsOnly = process.argv.includes('--entrypoints-only');
// The wrappers report the real path. A temp directory given as a Windows short name
// (RUNNER~1) or through a symlink (macOS /var) would otherwise never compare equal to it.
const tempBase = fs.realpathSync.native(fs.existsSync(path.join(repo, 'scratch')) ? path.join(repo, 'scratch') : os.tmpdir());
const root = fs.mkdtempSync(path.join(tempBase, 'etlsql-asset-generation espaces-é-$literal-'));
const sharedRel = 'src/ETL-SQL.ReportRuntime/Resources/Shared';
const portalJsRel = 'src/ETL-SQL.Portal/wwwroot/js';
const sourceRel = 'src/ETL-SQL.ReportRuntime/Resources/TypeScript';
const shared = path.join(root, sharedRel);
const source = path.join(root, sourceRel);
const write = (file, content) => {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, content);
};
const generate = (check = false) => synchronizer.syncAssets({ root, toolchain: repo, check, quiet: true });
const verifyBuild = (expectedFailure) => {
    const result = spawnSync('dotnet', ['msbuild', path.join(root, 'ETL-SQL.ReportRuntime.proj'),
        '-nologo', '-target:VerifyPreparedBrowserAssets', '-verbosity:minimal'], { encoding: 'utf8' });
    assert.ifError(result.error);
    if (expectedFailure) {
        assert.notEqual(result.status, 0, 'An unprepared build must fail before consuming assets.');
        assert.match(result.stdout + result.stderr, expectedFailure);
    } else assert.equal(result.status, 0, result.stdout + result.stderr);
};
const files = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(dir, entry.name);
    return entry.isDirectory() ? files(file) : [file];
});
const generatedHosts = [
    'src/ETL-SQL.ReportPlayer/wwwroot',
    'src/ETL-SQL.WorkstationEditor/wwwroot',
    'src/ETL-SQL.Portal/wwwroot/js',
    'src/etl-sql-vscode/media',
].map(rel => path.join(root, rel));

async function verifyGeneration(configurationInputs) {
    const first = await generate();
    assert.ok(first.files > 100, 'Every production host must receive its required assets.');
    const inventoryPath = path.join(root, 'artifacts/browser-assets.json');
    const inventory = JSON.parse(fs.readFileSync(inventoryPath, 'utf8'));
    assert.deepEqual(inventory.outputs.map(output => output.path).sort(), first.generatedAssets,
        'The ownership audit must describe every compiler, bundle, and host output exactly once.');
    for (const output of inventory.outputs) {
        assert.ok(fs.existsSync(path.join(root, output.source)), `Missing owner for ${output.path}`);
        assert.ok(fs.existsSync(path.join(root, output.path)), `Missing output ${output.path}`);
    }
    for (const input of [`${sharedRel}/report-runtime.css`, `${sharedRel}/arrow.min.js`,
        `${sharedRel}/tabulator.min.js`, 'types/etlsql-contracts.generated.d.ts',
        `${sourceRel}/designer/studio-routes.generated.ts`]) {
        assert.equal(first.generatedAssets.includes(input), false, `Tracked input must not become an output: ${input}`);
    }
    assert.ok(first.generatedAssets.includes('src/ETL-SQL.Portal/wwwroot/css/report-runtime.css'));
    for (const host of generatedHosts) assert.ok(fs.existsSync(path.join(host, 'report-runtime.js')));
    assert.ok(fs.existsSync(path.join(shared, 'report-runtime.bundle.js')));
    assert.equal(fs.existsSync(path.join(generatedHosts[0], 'designer/designer.js')), false, 'Player must not ship Studio.');
    assert.equal(fs.existsSync(path.join(generatedHosts[3], 'designer/studio.js')), false, 'VS Code must not ship unloaded Studio code.');
    const snapshot = new Map(files(root).map(file => [file, fs.readFileSync(file)]));
    await generate();
    for (const [file, content] of snapshot) assert.deepEqual(fs.readFileSync(file), content, `Second generation changed ${file}`);
    await generate(true);
    verifyBuild();
    const outputSnapshot = () => new Map([...first.generatedAssets, 'artifacts/browser-assets.json', 'artifacts/browser-assets.props']
        .map(relative => [relative, fs.readFileSync(path.join(root, relative))]));
    const assertPreserved = snapshot => {
        for (const [relative, content] of snapshot)
            assert.deepEqual(fs.readFileSync(path.join(root, relative)), content, `Failed preflight changed ${relative}`);
    };
    // Toolchain failures must happen before acquiring the preparation lock or touching delivered
    // assets. Use a separate fake restore; never corrupt the checkout's working compiler.
    const toolchainFixture = path.join(root, 'toolchain-fixture');
    const toolchainDirectory = path.join(toolchainFixture, 'scripts/typecheck');
    const toolchainSnapshot = outputSnapshot();
    const rejectToolchain = async pattern => {
        await assert.rejects(() => synchronizer.syncAssets({ root, toolchain: toolchainFixture, quiet: true }), pattern);
        await assert.rejects(() => watchBrowser({ root, toolchain: toolchainFixture }), pattern);
        assert.equal(fs.existsSync(path.join(root, '.browser-assets.lock')), false);
        assertPreserved(toolchainSnapshot);
    };
    await rejectToolchain(/Missing or invalid package.json.*npm ci/);
    for (const name of ['package.json', 'package-lock.json'])
        write(path.join(toolchainDirectory, name), fs.readFileSync(path.join(repo, 'scripts/typecheck', name)));
    await rejectToolchain(/Missing or invalid node_modules\/\.package-lock.json.*npm ci/);
    const installedLock = fs.readFileSync(path.join(repo, 'scripts/typecheck/node_modules/.package-lock.json'));
    write(path.join(toolchainDirectory, 'node_modules/.package-lock.json'), installedLock);
    const installedPackage = JSON.parse(fs.readFileSync(path.join(repo, 'scripts/typecheck/node_modules/typescript/package.json')));
    write(path.join(toolchainDirectory, 'node_modules/typescript/package.json'), JSON.stringify({ ...installedPackage, version: '0.0.0' }));
    await rejectToolchain(/restored TypeScript compiler does not match.*npm ci/);
    write(path.join(toolchainDirectory, 'node_modules/typescript/package.json'), JSON.stringify(installedPackage));
    const wrongIntegrity = JSON.parse(installedLock);
    wrongIntegrity.packages['node_modules/typescript'].integrity = 'sha512-wrong';
    write(path.join(toolchainDirectory, 'node_modules/.package-lock.json'), JSON.stringify(wrongIntegrity));
    await rejectToolchain(/restored TypeScript compiler does not match.*npm ci/);
    write(path.join(toolchainDirectory, 'node_modules/.package-lock.json'), installedLock);
    await rejectToolchain(/local TypeScript compiler cannot be loaded.*npm ci/);
    write(path.join(toolchainDirectory, 'node_modules/typescript/lib/typescript.js'), 'module.exports = { version: "0.0.0" };\n');
    await rejectToolchain(/loaded TypeScript compiler does not match.*npm ci/);
    const changedLock = JSON.parse(fs.readFileSync(path.join(toolchainDirectory, 'package-lock.json')));
    changedLock.packages['node_modules/typescript'].version = '0.0.0';
    write(path.join(toolchainDirectory, 'package-lock.json'), JSON.stringify(changedLock));
    await rejectToolchain(/package.json and package-lock.json disagree.*npm ci/);
    fs.rmSync(toolchainFixture, { recursive: true, force: true });
    // Override only the reported runtime version in this child, exercising the production guard
    // portably even when CI has no second Node installation.
    const unsupported = spawnSync(process.execPath, ['-e', `
        Object.defineProperty(process.versions, 'node', { value: '24.0.0' });
        require(process.argv[1]).syncAssets({ root: process.argv[2], toolchain: process.argv[3], quiet: true })
            .catch(error => { console.error(error.message); process.exitCode = 1; });
    `, path.join(repo, 'scripts/sync-assets.js'), root, repo], { encoding: 'utf8' });
    assert.ifError(unsupported.error);
    assert.notEqual(unsupported.status, 0);
    assert.match(unsupported.stderr, /Unsupported Node.js 24.0.0.*npm ci/);
    assert.equal(fs.existsSync(path.join(root, '.browser-assets.lock')), false);
    assertPreserved(toolchainSnapshot);
    console.log('Browser toolchain: unsupported Node, missing/mismatched restore, lock drift and invalid compiler preserve all assets.');
    const freshnessSnapshot = outputSnapshot();
    const freshSignature = assetInputs.browserInputSignature(root);
    const freshnessInputs = [
        'scripts/lib/browser-toolchain.cjs', 'scripts/lib/browser-asset-inputs.cjs',
        'tsconfig.browser-build.json', 'scripts/typecheck/package.json', 'scripts/typecheck/package-lock.json',
        'scripts/typecheck/.npmrc', 'Directory.Build.props', 'Directory.Build.targets',
        ...configurationInputs.filter(input => /(?:\.csproj|\.vscodeignore|vite\.config\.ts|Generator(?:Tests)?\.cs|BrowserContracts.*\.json)$/.test(input)),
        'src/etl-sql-vscode/package.json', 'src/etl-sql-vscode/ui/package.json',
        'scripts/generate-portal-browser-contract.mjs',
        'scripts/generate-browser-contracts.mjs',
        'types/etlsql-contracts.generated.d.ts', 'src/ETL-SQL.Portal/wwwroot/css/portal.css',
        'src/ETL-SQL.Portal/wwwroot/index.html', `${sharedRel}/report-runtime.css`,
    ].filter(input => fs.existsSync(path.join(root, input)));
    for (const relative of [...new Set(freshnessInputs)]) {
        const file = path.join(root, relative), original = fs.readFileSync(file);
        // Whitespace changes fingerprint an input without changing any emitted implementation.
        fs.appendFileSync(file, '\n');
        assert.notEqual(assetInputs.browserInputSignature(root), freshSignature, `Signature missed ${relative}`);
        verifyBuild(/Prepared browser asset is stale/);
        if (['Directory.Build.props', 'src/ETL-SQL.Portal/wwwroot/css/portal.css'].includes(relative))
            await assert.rejects(generate(true), /manifest missing or stale/);
        assertPreserved(freshnessSnapshot);
        fs.writeFileSync(file, original);
    }
    const retainedInputs = ['scripts/lib/freshness-probe.cjs',
        'src/ETL-SQL.Portal/BrowserContracts/freshness-probe.json',
        'src/ETL-SQL.Portal/wwwroot/css/freshness-probe.css'];
    for (const relative of retainedInputs) {
        write(path.join(root, relative), relative.endsWith('.json') ? '{}\n' : '/* retained freshness probe */\n');
        verifyBuild(/Browser sources have not been prepared/);
        await assert.rejects(generate(true), /manifest missing or stale/);
        assertPreserved(freshnessSnapshot);
        fs.unlinkSync(path.join(root, relative));
    }
    write(path.join(root, retainedInputs[0]), 'module.exports = "first";\n');
    await generate();
    const helperSnapshot = outputSnapshot();
    fs.appendFileSync(path.join(root, retainedInputs[0]), '\n// changed transitive helper\n');
    verifyBuild(/Prepared browser asset is stale/);
    await assert.rejects(generate(true), /manifest missing or stale/);
    assertPreserved(helperSnapshot);
    fs.unlinkSync(path.join(root, retainedInputs[0]));
    verifyBuild(/Prepared browser asset is missing/);
    await assert.rejects(generate(true), /manifest missing or stale/);
    assertPreserved(helperSnapshot);
    await generate();
    assertPreserved(freshnessSnapshot);
    const optionalSelector = path.join(root, 'src/etl-sql-vscode/.vscodeignore');
    if (fs.existsSync(optionalSelector)) {
        const selectorBytes = fs.readFileSync(optionalSelector);
        fs.unlinkSync(optionalSelector);
        verifyBuild(/Prepared browser asset is missing/);
        await generate();
        verifyBuild();
        write(optionalSelector, selectorBytes);
        verifyBuild(/Browser sources have not been prepared/);
        await assert.rejects(generate(true), /manifest missing or stale/);
        await generate();
        assertPreserved(freshnessSnapshot);
    }
    verifyBuild();
    console.log('Asset freshness: retained styles/markup, compiler and contract generators, helpers, build/package selectors, dependency metadata, and added/removed inputs reject stale preparation without repair.');
    const beforeBootstrap = outputSnapshot();
    const earlierPragma = '*/\n// @ts-nocheck — generated implementation; check the TypeScript owner.\n';
    for (const relative of first.generatedAssets) {
        const file = path.join(root, relative);
        if (file.endsWith('.js')) write(file, fs.readFileSync(file, 'utf8').replaceAll(earlierPragma, '*/\n'));
    }
    const version1 = { version: 1, outputs: inventory.outputs.map(({ sha256: _hash, ...owner }) => owner) };
    write(inventoryPath, JSON.stringify(version1, null, 2) + '\n');
    const beforeUpgrade = outputSnapshot();
    await assert.rejects(generate(true), /missing or stale/);
    assertPreserved(beforeUpgrade);
    await generate();
    assertPreserved(beforeBootstrap);
    const unverified = path.join(shared, 'unverified-bootstrap.js');
    write(unverified, 'export const retainedUntilReviewed = true;\n');
    version1.outputs.push({ path: `${sharedRel}/unverified-bootstrap.js`, kind: 'typescript',
        source: `${sourceRel}/unverified-bootstrap.ts` });
    write(inventoryPath, JSON.stringify(version1, null, 2) + '\n');
    const unverifiedSnapshot = outputSnapshot();
    await assert.rejects(generate(), /Unverified obsolete browser outputs/);
    assertPreserved(unverifiedSnapshot);
    assert.equal(fs.existsSync(unverified), true);
    assert.equal(fs.existsSync(path.join(generatedHosts[0], 'unverified-bootstrap.js')), false);
    fs.unlinkSync(unverified);
    write(inventoryPath, beforeBootstrap.get('artifacts/browser-assets.json'));
    const utilSource = path.join(source, 'rt-util.ts');
    const utilBefore = fs.readFileSync(utilSource);
    const copiedUtil = path.join(root, portalJsRel, 'rt-util.js');
    const copiedBefore = fs.readFileSync(copiedUtil);
    // A valid change elsewhere must not be delivered before a host-target conflict is found.
    fs.appendFileSync(utilSource, '\n// valid compiler change before host conflict\n');
    write(copiedUtil, '/* GENERATED FILE - DO NOT EDIT. */\nexport const handwritten = true;\n');
    const conflicting = outputSnapshot();
    await assert.rejects(generate(), /retained or locally edited/);
    assertPreserved(conflicting);
    write(utilSource, utilBefore);
    write(copiedUtil, copiedBefore);
    const portalCollision = path.join(root, 'src/ETL-SQL.Portal/BrowserSources/rt-util.ts');
    write(portalCollision, 'export const portalOwner = true;\n');
    const beforePortalCollision = outputSnapshot();
    await assert.rejects(generate(), /collides with a shared host asset/);
    assertPreserved(beforePortalCollision);
    fs.unlinkSync(portalCollision);
    const bundleCollision = path.join(source, 'report-runtime.bundle.ts');
    write(bundleCollision, 'export const competingBundleOwner = true;\n');
    await assert.rejects(generate(), /collision|ownership conflict/);
    assertPreserved(beforePortalCollision);
    fs.unlinkSync(bundleCollision);
    fs.appendFileSync(utilSource, "\nexport const unsupportedBundleDependency = () => import('./rt-state.js');\n");
    await assert.rejects(generate(), /dynamic import/);
    assertPreserved(beforePortalCollision);
    write(utilSource, utilBefore);
    const blockedHost = path.join(generatedHosts[0], 'rt-util.js');
    const blockedBefore = fs.readFileSync(blockedHost);
    fs.unlinkSync(blockedHost);
    fs.mkdirSync(blockedHost);
    fs.appendFileSync(utilSource, '\n// valid emit before blocked host target\n');
    await assert.rejects(generate(), /EISDIR|illegal operation on a directory/);
    for (const [relative, content] of beforePortalCollision) {
        if (path.join(root, relative) !== blockedHost)
            assert.deepEqual(fs.readFileSync(path.join(root, relative)), content, `Blocked host changed ${relative}`);
    }
    fs.rmdirSync(blockedHost);
    write(blockedHost, blockedBefore);
    write(utilSource, utilBefore);
    const playerRoot = generatedHosts[0];
    const detachedPlayer = path.join(root, 'detached-player');
    fs.renameSync(playerRoot, detachedPlayer);
    fs.symlinkSync(detachedPlayer, playerRoot, process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(generate(), /symlink or junction/);
    assertPreserved(beforePortalCollision);
    fs.unlinkSync(playerRoot);
    fs.renameSync(detachedPlayer, playerRoot);
    await generate(true);
    const closureOwner = path.join(source, 'closure-probe.ts');
    const closureConsumer = path.join(shared, 'retained-closure-consumer.js');
    write(closureOwner, 'export const owned = true;\n');
    write(closureConsumer, "import { owned } from './closure-probe.js';\nexport const retained = owned;\n");
    await generate();
    const beforeMissingDependency = outputSnapshot();
    const closureOutput = path.join(shared, 'closure-probe.js');
    const closureBefore = fs.readFileSync(closureOutput);
    fs.unlinkSync(closureOwner);
    await assert.rejects(generate(), /Shared asset import missing: closure-probe\.js/);
    assertPreserved(beforeMissingDependency);
    assert.deepEqual(fs.readFileSync(closureOutput), closureBefore, 'A retained JS dependency must stop obsolete-output cleanup.');
    fs.unlinkSync(closureConsumer);
    await generate();
    assert.equal(fs.existsSync(closureOutput), false);
    const temporaryModules = [path.join(shared, 'rt-util.__test-probe.mjs'),
        path.join(root, portalJsRel, 'portal-nav.__test-probe.mjs')];
    for (const temporary of temporaryModules) write(temporary, 'throw new Error("test-only module");\n');
    await generate(true);
    verifyBuild();
    await generate();
    const preparedManifest = fs.readFileSync(path.join(root, 'artifacts/browser-assets.props'), 'utf8');
    assert.equal(preparedManifest.includes('__test-probe'), false, 'Temporary modules must not become prepared inputs.');
    assert.equal(fs.readFileSync(inventoryPath, 'utf8').includes('__test-probe'), false);
    for (const host of generatedHosts.filter(host => host !== path.join(root, portalJsRel)))
        assert.equal(fs.existsSync(path.join(host, 'rt-util.__test-probe.mjs')), false);
    for (const temporary of temporaryModules) fs.unlinkSync(temporary);
    const lockPath = path.join(root, '.browser-assets.lock');
    write(lockPath, `${process.pid}\n`);
    verifyBuild(/preparation is still running/);
    await assert.rejects(() => generate(), /already running/);
    await assert.rejects(() => generate(true), /already running/);
    fs.unlinkSync(lockPath);
    for (const [file, content] of snapshot) assert.deepEqual(fs.readFileSync(file), content,
        `A concurrent preparation attempt changed ${file}`);
    const preparedSource = path.join(source, 'rt-util.ts');
    const sourceBefore = fs.readFileSync(preparedSource);
    fs.appendFileSync(preparedSource, '\n// changed after preparation\n');
    verifyBuild(/Prepared browser asset is stale/);
    fs.writeFileSync(preparedSource, sourceBefore);
    const newSource = path.join(source, 'new-build-source.ts');
    write(newSource, 'export const newSource = true;\n');
    verifyBuild(/Browser sources have not been prepared/);
    fs.unlinkSync(newSource);
    const newAsset = path.join(shared, 'new-build-asset.css');
    write(newAsset, '.new-asset {}\n');
    verifyBuild(/Browser sources have not been prepared/);
    fs.unlinkSync(newAsset);
    const preparedOutput = path.join(generatedHosts[0], 'report-runtime.js');
    const outputBefore = fs.readFileSync(preparedOutput);
    fs.unlinkSync(preparedOutput);
    verifyBuild(/Prepared browser asset is missing/);
    fs.writeFileSync(preparedOutput, outputBefore);
    verifyBuild();

    const authored = path.join(generatedHosts[2], 'handwritten-probe.js');
    write(authored, 'export const handwritten = true;\n');
    const probe = path.join(source, 'source-probe.ts');
    write(probe, 'export const probe: number = 7;\n');
    await generate();
    for (const host of generatedHosts) assert.ok(fs.existsSync(path.join(host, 'source-probe.js')));
    fs.renameSync(probe, path.join(source, 'renamed-probe.ts'));
    await assert.rejects(generate(true), /Orphaned/);
    await generate();
    for (const host of generatedHosts) {
        assert.equal(fs.existsSync(path.join(host, 'source-probe.js')), false);
        assert.ok(fs.existsSync(path.join(host, 'renamed-probe.js')));
    }
    fs.unlinkSync(path.join(source, 'renamed-probe.ts'));
    await generate();
    for (const host of generatedHosts) assert.equal(fs.existsSync(path.join(host, 'renamed-probe.js')), false);
    assert.equal(fs.readFileSync(authored, 'utf8'), 'export const handwritten = true;\n');

    const runtime = path.join(generatedHosts[0], 'report-runtime.js');
    const beforeFailure = fs.readFileSync(runtime);
    write(probe, 'export const probe: number = "invalid";\n');
    await assert.rejects(generate(), /not assignable/);
    assert.deepEqual(fs.readFileSync(runtime), beforeFailure, 'Compiler errors must stop host writes.');
    fs.unlinkSync(probe);
    const imported = path.join(source, 'designer/import-probe.ts');
    write(imported, "import './missing-module.js';\nexport const probe = true;\n");
    await assert.rejects(generate(), /Cannot find module/);
    fs.unlinkSync(imported);
    fs.unlinkSync(runtime);
    await assert.rejects(generate(true), /missing or stale/);
    await generate();
    assert.deepEqual(fs.readFileSync(runtime), beforeFailure, 'Generation must recover a deleted output.');
    await generate(true);
    const concurrentSource = path.join(source, 'concurrent-probe.ts');
    write(concurrentSource, 'export const concurrent = 1;\n');
    let editedDuringPreparation = false;
    const editObserver = fs.watch(shared, (_event, filename) => {
        if (String(filename) !== 'concurrent-probe.js' || editedDuringPreparation) return;
        editedDuringPreparation = true;
        write(concurrentSource, 'export const concurrent = 2;\n');
    });
    try {
        await assert.rejects(generate(), /sources changed during preparation/);
        assert.equal(editedDuringPreparation, true);
        verifyBuild(/Browser sources have not been prepared|Prepared browser asset is stale/);
    } finally { editObserver.close(); }
    await generate();
    for (const host of generatedHosts)
        assert.match(fs.readFileSync(path.join(host, 'concurrent-probe.js'), 'utf8'), /concurrent = 2/);
    verifyBuild();
    // The manifest hashes files asynchronously. A change after the ownership write must not be
    // recorded as a prepared input while its generated implementation still belongs to an older edit.
    const lateSource = path.join(source, 'late-probe.ts');
    write(lateSource, 'export const late = 1;\n');
    let editedDuringManifest = false;
    const manifestObserver = fs.watch(path.join(root, 'artifacts'), (_event, filename) => {
        if (String(filename) !== 'browser-assets.json' || editedDuringManifest) return;
        editedDuringManifest = true;
        write(lateSource, 'export const late = 2;\n');
    });
    try {
        await assert.rejects(generate(), /sources changed during preparation/);
        assert.equal(editedDuringManifest, true);
        verifyBuild(/Browser sources have not been prepared|Prepared browser asset is stale/);
    } finally { manifestObserver.close(); }
    await generate();
    for (const host of generatedHosts)
        assert.match(fs.readFileSync(path.join(host, 'late-probe.js'), 'utf8'), /late = 2/);
    verifyBuild();
    console.log('Asset preflight: compiler/host/bundle collisions, bundling and host failures, freshness, and hash-based cleanup passed.');
    const watchEvents = [];
    const watchedSource = path.join(source, 'watched-probe.ts');
    let changeAfterPreparation = false;
    // Every watcher result is a full preparation, so the wait is sized from one measured here
    // rather than from a fixed number that a slower runner outgrows as owners are added.
    const measured = Date.now();
    await generate(true);
    const cycleMs = Date.now() - measured;
    const watcher = await watchBrowser({ root, toolchain: repo, intervalMs: 50,
        onResult: event => {
            watchEvents.push(event);
            if (changeAfterPreparation && event.result) {
                changeAfterPreparation = false;
                write(watchedSource, 'export const watched: number = 13;\n');
            }
        } });
    const until = async predicate => {
        const deadline = Date.now() + Math.max(15000, cycleMs * 10);
        while (!predicate()) {
            assert.ok(Date.now() < deadline, 'The browser watcher did not report the expected outcome: ' +
                watchEvents.map(event => event.error?.message ?? 'prepared').join('\n'));
            await new Promise(resolve => setTimeout(resolve, 50));
        }
    };
    try {
        write(watchedSource, 'export const watched: number = 9;\n');
        await until(() => watchEvents.filter(event => event.result).length >= 2);
        for (const host of generatedHosts) assert.match(fs.readFileSync(path.join(host, 'watched-probe.js'), 'utf8'), /watched = 9/);
        verifyBuild();
        write(watchedSource, 'export const watched: number = "invalid";\n');
        await until(() => watchEvents.some(event => event.error));
        assert.match(watchEvents.find(event => event.error).error.message, /not assignable/);
        for (const host of generatedHosts) assert.match(fs.readFileSync(path.join(host, 'watched-probe.js'), 'utf8'), /watched = 9/);
        write(watchedSource, 'export const watched: number = 11;\n');
        await until(() => watchEvents.filter(event => event.result).length >= 3);
        for (const host of generatedHosts) assert.match(fs.readFileSync(path.join(host, 'watched-probe.js'), 'utf8'), /watched = 11/);
        verifyBuild();
        changeAfterPreparation = true;
        write(watchedSource, 'export const watched: number = 12;\n');
        await until(() => watchEvents.filter(event => event.result).length >= 5);
        for (const host of generatedHosts) assert.match(fs.readFileSync(path.join(host, 'watched-probe.js'), 'utf8'), /watched = 13/);
        verifyBuild();
    } finally { await watcher.dispose(); }
}

try {
    for (const rel of [sourceRel, 'src/ETL-SQL.Portal/BrowserSources', 'types']) {
        if (!fs.existsSync(path.join(repo, rel))) continue;
        // Node's native directory-copy fast path skipped this Windows fixture's destination.
        // A filter uses the checked JS copy path; verify the fixture before testing generation.
        fs.cpSync(path.join(repo, rel), path.join(root, rel), { recursive: true, filter: () => true });
        assert.equal(files(path.join(root, rel)).length, files(path.join(repo, rel)).length,
            `Source-only fixture did not copy all owners/declarations: ${rel}`);
    }
    fs.copyFileSync(path.join(repo, 'tsconfig.browser-build.json'), path.join(root, 'tsconfig.browser-build.json'));
    fs.copyFileSync(path.join(repo, 'Directory.Build.targets'), path.join(root, 'Directory.Build.targets'));
    for (const input of ['scripts/compile-browser.mjs', 'scripts/sync-assets.js', 'scripts/lib/browser-asset-inputs.cjs',
        'scripts/lib/browser-toolchain.cjs', 'scripts/typecheck/.npmrc',
        'scripts/typecheck/package.json', 'scripts/typecheck/package-lock.json']) {
        write(path.join(root, input), fs.readFileSync(path.join(repo, input)));
    }
    // Keep this fixture inventory independent of the collector: omitting a selector or contract
    // generator from production must fail the regression instead of silently shrinking coverage.
    const configurationInputs = ['Directory.Build.props', 'scripts/generate-portal-browser-contract.mjs', 'scripts/generate-browser-contracts.mjs',
        'src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj', 'src/ETL-SQL.ReportRuntime/ETL-SQL.ReportRuntime.csproj',
        'src/ETL-SQL.ReportPlayer/ETL-SQL.ReportPlayer.csproj', 'src/ETL-SQL.WorkstationEditor/ETL-SQL.WorkstationEditor.csproj',
        'src/ETL-SQL.Portal/ETL-SQL.Portal.csproj', 'src/etl-sql-vscode/.vscodeignore',
        'src/etl-sql-vscode/package.json', 'src/etl-sql-vscode/ui/package.json', 'src/etl-sql-vscode/ui/vite.config.ts',
        'src/ETL-SQL.Portal/BrowserContracts/critical-api-contracts.json',
        ...['BrowserContractsGenerator', 'BrowserContractsGeneratorTests', 'StudioBrowserContractsGenerator',
            'StudioBrowserContractsGeneratorTests'].map(name => `tests/ETL-SQL.Portal.Tests/${name}.cs`)];
    for (const input of [...configurationInputs, 'src/ETL-SQL.Portal/wwwroot/css/portal.css',
        'src/ETL-SQL.Portal/wwwroot/index.html']) write(path.join(root, input), fs.readFileSync(path.join(repo, input)));
    write(path.join(root, 'ETL-SQL.ReportRuntime.proj'), '<Project><Import Project="Directory.Build.targets" /></Project>');
    verifyBuild(/Browser assets are not prepared/);
    // Copy only authored assets. This models a checkout with no compiler, bundle, or host output.
    for (const file of files(path.join(repo, sharedRel))) {
        const content = fs.readFileSync(file);
        if (file.endsWith('report-runtime.bundle.js') || content.toString('utf8').startsWith('/* GENERATED TYPESCRIPT OUTPUT')) continue;
        write(path.join(shared, path.relative(path.join(repo, sharedRel), file)), content);
    }
    // Authored Portal modules remain tracked during migration, including the separately generated
    // API validator. Shared host copies and TypeScript implementations must be absent.
    for (const file of files(path.join(repo, portalJsRel))) {
        const content = fs.readFileSync(file);
        const text = content.toString('utf8');
        if (text.startsWith('/* GENERATED TYPESCRIPT OUTPUT') || text.includes('Source: src/ETL-SQL.ReportRuntime/Resources/Shared/')) continue;
        write(path.join(root, portalJsRel, path.relative(path.join(repo, portalJsRel), file)), content);
    }
    for (const host of generatedHosts) assert.equal(fs.existsSync(path.join(host, 'report-runtime.js')), false);
    // The focused mode retains the source-only fixture and real preparation used by public wrappers.
    if (!entrypointsOnly) await verifyGeneration(configurationInputs);
    // Exercise the public wrappers with real preparation. Build spies prove that failed preparation
    // cannot reach downstream tools; they also record argument boundaries from an unrelated cwd.
    for (const script of ['build-debug.ps1', 'build-debug.sh', 'sync-assets.ps1', 'check-built-browser-assets.mjs',
        'test-smoke.ps1', 'test-smoke.sh', 'test-lane.ps1', 'test-lane.sh', 'Release.Helpers.ps1', 'TestLane.Helpers.ps1'])
        fs.copyFileSync(path.join(repo, 'scripts', script), path.join(root, 'scripts', script));
    const callsFile = path.join(root, 'entrypoint-calls.jsonl');
    const driver = path.join(root, 'entrypoint-driver.ps1');
    const shim = path.join(root, 'entrypoint-tools');
    const outside = path.join(root, 'outside espaces-é');
    fs.mkdirSync(shim); fs.mkdirSync(outside);
    write(path.join(root, 'tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj'),
        '<Project><ItemGroup><ProjectReference Include="../../ETL-SQL.ReportRuntime.proj" /></ItemGroup></Project>');
    const debug = path.join(root, 'scripts', process.platform === 'win32' ? 'build-debug.ps1' : 'build-debug.sh');
    let shell;
    let realDotnet = '';
    if (process.platform === 'win32') {
        const lookup = spawnSync('pwsh', ['-NoProfile', '-Command', '(Get-Process -Id $PID).Path'], { encoding: 'utf8' });
        assert.equal(lookup.status, 0, lookup.stdout + lookup.stderr);
        shell = lookup.stdout.trim();
        write(driver, `$ErrorActionPreference = 'Stop'
function global:dotnet {
  @{ command = 'dotnet'; args = @($args) } | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ETLSQL_ENTRYPOINT_CALLS
  $global:LASTEXITCODE = [int]$env:ETLSQL_ENTRYPOINT_BUILD_EXIT
  if ($global:LASTEXITCODE -eq 0) { Write-Output 'Passed: 1' }
}
function global:npm {
  @{ command = 'npm'; args = @($args) } | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ETLSQL_ENTRYPOINT_CALLS
  $global:LASTEXITCODE = 0
}
$commandParameters = $env:ETLSQL_ENTRYPOINT_PARAMETERS | ConvertFrom-Json -AsHashtable
& $env:ETLSQL_ENTRYPOINT_SCRIPT @commandParameters
exit $LASTEXITCODE
`);
    } else {
        shell = '/bin/bash';
        const dotnet = spawnSync('/bin/sh', ['-c', 'command -v dotnet'], { encoding: 'utf8' });
        assert.equal(dotnet.status, 0, dotnet.stderr);
        realDotnet = dotnet.stdout.trim();
        // Path discovery must still work when the fixture intentionally removes Node from PATH.
        const dirname = spawnSync('/bin/sh', ['-c', 'command -v dirname'], { encoding: 'utf8' });
        assert.equal(dirname.status, 0, dirname.stderr);
        fs.symlinkSync(dirname.stdout.trim(), path.join(shim, 'dirname'));
        const logger = path.join(shim, 'log.mjs');
        write(logger, `import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
if (process.argv[2] === 'dotnet' && process.argv[3] === 'msbuild' && process.argv.includes('-getItem:ProjectReference')) {
  const result = spawnSync(process.env.ETLSQL_ENTRYPOINT_REAL_DOTNET, process.argv.slice(3), { encoding: 'utf8' });
  process.stdout.write(result.stdout ?? ''); process.stderr.write(result.stderr ?? ''); process.exit(result.status ?? 1);
}
fs.appendFileSync(process.env.ETLSQL_ENTRYPOINT_CALLS, JSON.stringify({command:process.argv[2],args:process.argv.slice(3)})+'\\n');
if (process.argv[2] === 'dotnet' && Number(process.env.ETLSQL_ENTRYPOINT_BUILD_EXIT) === 0) console.log('Passed: 1');
process.exit(process.argv[2] === 'dotnet' ? Number(process.env.ETLSQL_ENTRYPOINT_BUILD_EXIT) : 0);
`);
        for (const command of ['dotnet', 'npm']) {
            const executable = path.join(shim, command);
            write(executable, `#!/bin/sh\n"$ETLSQL_ENTRYPOINT_NODE" "$ETLSQL_ENTRYPOINT_LOGGER" ${command} "$@"\n`);
            fs.chmodSync(executable, 0o755);
        }
    }
    const calls = () => fs.readFileSync(callsFile, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
    const runEntrypoint = ({ missingNode = false, buildExit = 0, script = debug, argv = [], parameters = {} } = {}) => {
        write(callsFile, '');
        const env = { ...process.env, ETLSQL_ENTRYPOINT_CALLS: callsFile,
            ETLSQL_ENTRYPOINT_SCRIPT: script, ETLSQL_ENTRYPOINT_PARAMETERS: JSON.stringify(parameters),
            ETLSQL_ENTRYPOINT_BUILD_EXIT: String(buildExit),
            ETLSQL_ENTRYPOINT_REAL_DOTNET: realDotnet,
            ETLSQL_ENTRYPOINT_NODE: process.execPath, ETLSQL_ENTRYPOINT_LOGGER: path.join(shim, 'log.mjs'),
            PATH: missingNode ? shim : [shim, path.dirname(process.execPath), process.env.PATH].join(path.delimiter) };
        const args = process.platform === 'win32' ? ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', driver] : [script, ...argv];
        const result = spawnSync(shell, args, { cwd: outside, env, encoding: 'utf8' });
        assert.ifError(result.error); return result;
    };
    const testEntries = process.platform === 'win32' ? [
        { script: path.join(root, 'scripts/test-smoke.ps1'), parameters: { Lane: 'core', NoRestore: true } },
        { script: path.join(root, 'scripts/test-lane.ps1'), parameters: { Lane: 'engine', EngineShardCount: 1, NoRestore: true } }
    ] : [
        { script: path.join(root, 'scripts/test-smoke.sh'), argv: ['--lane', 'core', '--no-restore'] },
        { script: path.join(root, 'scripts/test-lane.sh'), argv: ['--lane', 'engine', '--no-restore'] }
    ];
    const noBuildArgument = process.platform === 'win32' ? '-NoBuild' : '--no-build';
    const missingCompiler = runEntrypoint();
    assert.notEqual(missingCompiler.status, 0, missingCompiler.stdout + missingCompiler.stderr);
    assert.match(missingCompiler.stdout + missingCompiler.stderr, /npm ci --prefix scripts\/typecheck/);
    assert.deepEqual(calls(), [], 'Missing compiler must stop before .NET/npm builds.');
    const missingNode = runEntrypoint({ missingNode: true });
    assert.notEqual(missingNode.status, 0, missingNode.stdout + missingNode.stderr);
    assert.match(missingNode.stdout + missingNode.stderr, /node.*(not recognized|not found)/s);
    assert.deepEqual(calls(), [], 'Missing Node must stop before .NET/npm builds.');
    for (const entry of testEntries) {
        for (const absent of [false, true]) {
            const failed = runEntrypoint({ ...entry, missingNode: absent });
            assert.notEqual(failed.status, 0, failed.stdout + failed.stderr);
            assert.match(failed.stdout + failed.stderr, absent ? /node.*(not recognized|not found)/s : /npm ci --prefix scripts\/typecheck/);
            assert.deepEqual(calls(), [], 'Test lanes must stop before .NET when preparation cannot run.');
        }
    }
    fs.cpSync(path.join(repo, 'scripts/typecheck/node_modules'), path.join(root, 'scripts/typecheck/node_modules'),
        { recursive: true, filter: () => true });
    const prepared = runEntrypoint();
    assert.equal(prepared.status, 0, prepared.stdout + prepared.stderr);
    assert.deepEqual(calls(), [
        { command: 'dotnet', args: ['build', path.join(root, 'ETL-SQL.slnx'), '--nologo', '-v', 'minimal'] },
        { command: 'npm', args: ['--prefix', path.join(root, 'src/etl-sql-vscode/ui'), 'run', 'build', '--silent'] },
        { command: 'npm', args: ['--prefix', path.join(root, 'src/etl-sql-vscode'), 'run', 'compile', '--silent'] },
        { command: 'npm', args: ['--prefix', path.join(root, 'src/etl-sql-vscode'), 'run', 'test:unit'] }
    ], 'Checkout paths remain single arguments and package steps do not depend on cwd.');
    const buildFailed = runEntrypoint({ buildExit: 7 });
    assert.notEqual(buildFailed.status, 0, 'A downstream build failure must reach the caller.');
    assert.equal(calls().length, 4, 'A build failure must retain the debug script\'s remaining checks.');
    const preparedMetadata = ['artifacts/browser-assets.json', 'artifacts/browser-assets.props'];
    const metadataSnapshot = () => new Map(preparedMetadata.map(relative =>
        [relative, fs.readFileSync(path.join(root, relative))]));
    const metadataPreserved = before => {
        for (const [relative, bytes] of before)
            assert.deepEqual(fs.readFileSync(path.join(root, relative)), bytes, 'No-build checks must not repair prepared state.');
    };
    // The wrappers' dotnet is a spy, so the real target records the build the way a build would.
    const recordBuild = () => {
        const result = spawnSync('dotnet', ['msbuild', path.join(root, 'ETL-SQL.ReportRuntime.proj'), '-nologo',
            '-target:StampBuiltBrowserAssets', '-p:Configuration=Debug', '-verbosity:minimal'], { encoding: 'utf8' });
        assert.equal(result.status, 0, result.stdout + result.stderr);
        const stamp = fs.readFileSync(path.join(root, 'artifacts/browser-assets-built/ETL-SQL.ReportRuntime.Debug.any.sha256'), 'utf8').trim();
        const manifest = createHash('sha256').update(fs.readFileSync(path.join(root, 'artifacts/browser-assets.props'))).digest('hex');
        assert.equal(stamp.toLowerCase(), manifest, 'A build must record the manifest it consumed.');
    };
    // dotnet publish/run --no-build reach MSBuild without PrepareForBuild; the same record guards them.
    const verifyReuse = expectedFailure => {
        const result = spawnSync('dotnet', ['msbuild', path.join(root, 'ETL-SQL.ReportRuntime.proj'), '-nologo',
            '-target:VerifyBuiltBrowserAssets', '-p:Configuration=Debug', '-p:NoBuild=true', '-verbosity:minimal'], { encoding: 'utf8' });
        if (expectedFailure) {
            assert.notEqual(result.status, 0, 'Reusing binaries from another preparation must fail.');
            assert.match(result.stdout + result.stderr, expectedFailure);
        } else assert.equal(result.status, 0, result.stdout + result.stderr);
        // A test project is guarded through the hosts it references, when the test run skips the build.
        const tests = spawnSync('dotnet', ['msbuild', path.join(root, 'Fixture.Tests.proj'), '-nologo',
            '-target:VerifyBuiltBrowserAssetsForTests', '-p:Configuration=Debug', '-p:VSTestNoBuild=true', '-verbosity:minimal'], { encoding: 'utf8' });
        if (expectedFailure) {
            assert.notEqual(tests.status, 0, 'A no-build test run must not use hosts from another preparation.');
            assert.match(tests.stdout + tests.stderr, /would test a host/);
        } else assert.equal(tests.status, 0, tests.stdout + tests.stderr);
        const building = spawnSync('dotnet', ['msbuild', path.join(root, 'Fixture.Tests.proj'), '-nologo',
            '-target:VerifyBuiltBrowserAssetsForTests', '-p:Configuration=Debug', '-verbosity:minimal'], { encoding: 'utf8' });
        assert.equal(building.status, 0, 'A test run that builds first is checked by the build itself: ' + building.stdout + building.stderr);
    };
    write(path.join(root, 'Fixture.Tests.proj'), '<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>' +
        '<ItemGroup><ProjectReference Include="ETL-SQL.ReportRuntime.proj" /></ItemGroup><Import Project="Directory.Build.targets" /></Project>');
    for (const [index, entry] of testEntries.entries()) {
        const noBuild = process.platform === 'win32' ? { ...entry, parameters: { ...entry.parameters, NoBuild: true } } :
            { ...entry, argv: [...entry.argv, noBuildArgument] };
        const verifyTestCall = () => {
            assert.equal(calls().length, 1);
            assert.equal(calls()[0].command, 'dotnet');
            assert.equal(calls()[0].args[0], 'test');
            assert.equal(calls()[0].args[1], path.join(root, 'tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj'));
            assert.ok(calls()[0].args.includes('--no-restore'));
        };
        // Preparation is current here, but nothing has been built from it: the first lane has no
        // build record at all, the second has one from before the first lane's source was added.
        const unbuilt = runEntrypoint(noBuild);
        assert.notEqual(unbuilt.status, 0, 'No-build test lanes must reject binaries not built from the current preparation.');
        assert.match(unbuilt.stdout + unbuilt.stderr, index === 0 ? /Build once without --no-build/ : /older browser asset preparation/);
        assert.deepEqual(calls(), [], 'Stale binaries must be rejected before .NET runs.');
        verifyReuse(index === 0 ? /no build recorded for the prepared browser assets/ : /built from an older browser asset preparation/);
        recordBuild();
        verifyReuse();
        write(path.join(root, 'artifacts/browser-assets-built/Unrelated.Debug.any.sha256'), 'deliberately-stale-unrelated-host');
        const before = metadataSnapshot();
        const current = runEntrypoint(noBuild);
        assert.equal(current.status, 0, current.stdout + current.stderr);
        verifyTestCall();
        assert.ok(calls()[0].args.includes('--no-build'));
        metadataPreserved(before);
        write(path.join(source, `entrypoint-lane-${index}.ts`), 'export const lane = 1;\n');
        const stale = runEntrypoint(noBuild);
        assert.notEqual(stale.status, 0, 'No-build test lanes must reject added browser owners.');
        assert.deepEqual(calls(), []);
        metadataPreserved(before);
        const repaired = runEntrypoint(entry);
        assert.equal(repaired.status, 0, repaired.stdout + repaired.stderr);
        verifyTestCall();
        assert.equal(calls()[0].args.includes('--no-build'), false);
        const missingOutput = path.join(generatedHosts[0], `entrypoint-lane-${index}.js`);
        assert.ok(missingOutput.startsWith(root + path.sep));
        fs.unlinkSync(missingOutput);
        const missingBefore = metadataSnapshot();
        const missingAsset = runEntrypoint(noBuild);
        assert.notEqual(missingAsset.status, 0, 'No-build test lanes must reject missing host assets.');
        assert.deepEqual(calls(), []);
        metadataPreserved(missingBefore);
        const testFailed = runEntrypoint({ ...entry, buildExit: 7 });
        assert.notEqual(testFailed.status, 0, 'Test failures must reach the caller.');
        verifyTestCall();
        assert.ok(fs.existsSync(missingOutput), 'Ordinary test entry points must prepare missing outputs before .NET.');
    }
    if (process.platform === 'win32') {
        const inventoryFile = path.join(root, 'artifacts/browser-assets.json');
        const manifestFile = path.join(root, 'artifacts/browser-assets.props');
        const before = new Map([inventoryFile, manifestFile].map(file => [file, fs.readFileSync(file)]));
        const runCheck = (missing = false) => spawnSync(shell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-File',
            path.join(root, 'scripts/sync-assets.ps1'), '-Check'], {
            cwd: outside, env: { ...process.env, PATH: missing ? shim :
                [path.dirname(process.execPath), process.env.PATH].join(path.delimiter) }, encoding: 'utf8'
        });
        const check = runCheck();
        assert.equal(check.status, 0, check.stdout + check.stderr);
        for (const [file, content] of before) assert.deepEqual(fs.readFileSync(file), content, 'Wrapper -Check must stay read-only.');
        const missing = runCheck(true);
        assert.notEqual(missing.status, 0, 'Missing Node must not report a successful asset check.');
        assert.match(missing.stdout + missing.stderr, /node.*not recognized/s);
        write(path.join(source, 'entrypoint-stale-probe.ts'), 'export const stale = 1;\n');
        const stale = runCheck();
        assert.notEqual(stale.status, 0, 'The wrapper must propagate canonical verification failure.');
        for (const [file, content] of before) assert.deepEqual(fs.readFileSync(file), content, 'Failed checks must stay read-only.');
    }
    console.log(entrypointsOnly ? 'Public entry points: source-only preparation, read-only no-build checks, argument boundaries, and failure propagation passed.' :
        'Asset generation and public entry points: ownership, absent outputs, closures, freshness, recovery, argument boundaries, and fail-fast builds passed.');
} finally {
    assert.equal(path.dirname(root), path.resolve(tempBase));
    fs.rmSync(root, { recursive: true, force: true });
}
