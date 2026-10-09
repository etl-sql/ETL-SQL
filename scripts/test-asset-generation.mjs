import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import synchronizer from './sync-assets.js';
import { watchBrowser } from './compile-browser.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const tempBase = fs.existsSync(path.join(repo, 'scratch')) ? path.join(repo, 'scratch') : os.tmpdir();
const root = fs.mkdtempSync(path.join(tempBase, 'etlsql-asset-generation espaces-é-'));
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
        'scripts/typecheck/package.json', 'scripts/typecheck/package-lock.json']) {
        write(path.join(root, input), fs.readFileSync(path.join(repo, input)));
    }
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
    const watcher = await watchBrowser({ root, toolchain: repo, intervalMs: 50,
        onResult: event => {
            watchEvents.push(event);
            if (changeAfterPreparation && event.result) {
                changeAfterPreparation = false;
                write(watchedSource, 'export const watched: number = 13;\n');
            }
        } });
    const until = async predicate => {
        const deadline = Date.now() + 15000;
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
    console.log('Asset generation: ownership, absent outputs, host closures, idempotence, build preconditions, concurrency, rename/delete cleanup, watcher recovery, and failure isolation passed.');
} finally {
    assert.equal(path.dirname(root), path.resolve(tempBase));
    fs.rmSync(root, { recursive: true, force: true });
}
