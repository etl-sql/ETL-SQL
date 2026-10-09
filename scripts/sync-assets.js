const fs = require('fs').promises;
const path = require('path');
const { Script } = require('vm');
const { createHash } = require('crypto');
const { browserInputSignature, browserFiles, prepareOutputs, safeAssetPath } = require('./lib/browser-asset-inputs.cjs');
const { loadBrowserCompiler } = require('./lib/browser-toolchain.cjs');

const defaultRoot = path.resolve(__dirname, '..');

async function syncAssets({ root = defaultRoot, toolchain = defaultRoot, check = false, quiet = false, compiler } = {}) {
    loadBrowserCompiler(toolchain);
    const lockPath = safeAssetPath(root, '.browser-assets.lock');
    let lock;
    try {
        lock = await fs.open(lockPath, 'wx');
    } catch (error) {
        if (error.code !== 'EEXIST') throw error;
        throw new Error('Browser asset preparation or verification is already running. Wait for it to finish. '
            + 'If that process was interrupted, remove .browser-assets.lock after confirming it has stopped, then rerun node scripts/sync-assets.js.');
    }
    try {
        await lock.writeFile(`${process.pid}\n`);
        return await syncAssetsCore({ root, toolchain, check, quiet, compiler });
    } finally {
        await lock.close();
        await fs.unlink(lockPath);
    }
}

async function syncAssetsCore({ root, toolchain, check, quiet, compiler }) {
    const repoRoot = path.resolve(root);
    const sharedDir = path.join(repoRoot, 'src', 'ETL-SQL.ReportRuntime', 'Resources', 'Shared');

    const vsCodeMedia = path.join(repoRoot, 'src', 'etl-sql-vscode', 'media');
    const playerWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.ReportPlayer', 'wwwroot');
    // Published fallback for the desktop Studio/editor host. Running from a checkout serves
    // Resources/Shared directly, so drift here only surfaces in published builds.
    const workstationWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.WorkstationEditor', 'wwwroot');
    const portalWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot');
    const portalJsDir = path.join(portalWwwRoot, 'js');
    const portalCssDir = path.join(portalWwwRoot, 'css');

    const checkMode = check;
    const modeLabel = checkMode ? "Check" : "Sync";
    const log = quiet ? () => {} : console.log;
    const expectedTargets = new Set();
    let compilerOutputs = [];
    const plannedOutputs = [];
    const virtual = new Map();
    const legacyVirtual = new Map();
    const relative = file => path.relative(repoRoot, file).replaceAll('\\', '/');
    const readVirtual = (file, contents = virtual) => {
        const content = contents.get(path.resolve(file));
        if (content === undefined) throw new Error(`Shared asset import missing: ${relative(file)}`);
        return content;
    };

    log("=======================================================");
    log(` Shared Report Assets ${modeLabel} (Async Node.js)`);
    log(` Source: ${sharedDir}`);
    log("=======================================================\n");

    const drift = [];

    async function walk(dir) {
        return browserFiles(dir);
    }

    function getAssetRelativePath(filePath) {
        return path.relative(sharedDir, filePath);
    }

    function getExpectedContent(filePath, relativePath, content) {
        const ext = path.extname(filePath).toLowerCase();
        if (ext === '.js' || ext === '.css') {
            const normalizedRel = relativePath.replace(/\\/g, '/');
            if (normalizedRel.startsWith('designer/codemirror/')) {
                return content;
            }
            const sourcePath = `src/ETL-SQL.ReportRuntime/Resources/Shared/${normalizedRel}`;
            const banner = `/* GENERATED FILE - DO NOT EDIT.
 * Source: ${sourcePath}
 * Edit the canonical source, then run: node .\\scripts\\sync-assets.js
 */\n\n`;
            // A host copy is byte-identical to the canonical file, so the browser type gate must not
            // check it a second time under a second path — every finding would be reported twice and
            // the copy is not where anyone would fix it. `@ts-nocheck` has to be its own `//` comment
            // (TypeScript does not read the pragma out of a block comment) and has to come before the
            // first statement, so it leads the banner. JS only; CSS has no pragma.
            const pragma = ext === '.js' ? '// @ts-nocheck — generated copy; check the canonical source.\n' : '';
            return pragma + banner + content;
        }
        return content;
    }

    // ── Offline-snapshot bundle ──────────────────────────────────────────────────
    //
    // OfflineSnapshotViewer.cs inlines the report runtime into a single <script> tag in a one-file
    // .etlsnap. A single file has no siblings to import, so the ES-module parts are concatenated here
    // into report-runtime.bundle.js, which is what ETL-SQL.Reporting embeds.
    //
    // The transform is line-based, which is why the parts obey a constrained export style (see
    // docs/superpowers/specs/2026-09-07-browser-file-split-design.md). Anything it cannot handle is an
    // error rather than a silent omission: a part that vanished from a snapshot would fail only for a
    // user opening an .etlsnap, which is the furthest possible place from this script.
    const RUNTIME_PARTS = [
        'rt-util.js',
        'rt-state.js',
        'rt-theme.js',
        'rt-transport.js',
        'rt-data.js',
        'rt-detail.js',
        'rt-charts.js',
        'rt-table.js',
        'rt-matrix.js',
        'rt-controls-date.js',
        'rt-controls-input.js',
        'rt-visual.js',
        'rt-layout.js',
        'rt-actions.js',
        'rt-views.js',
        'rt-chrome.js',
        'report-runtime.js',
    ];
    const RUNTIME_BUNDLE = 'report-runtime.bundle.js';

    const INTRA_IMPORT = /^import\s*\{\s*([\w$]+(?:\s*,\s*[\w$]+)*\s*,?)\s*\}\s*from\s*'\.\/((?:rt-[a-z-]+|report-runtime)\.js)';\s*$/;
    const ANY_IMPORT = /^\s*import\b/;
    const EXPORT_DECL = /^export\s+(?=(?:async\s+)?function\b|const\b|let\b|class\b)/;
    const ANY_EXPORT = /^\s*export\b/;

    async function buildRuntimeBundle(contents = virtual) {
        const names = [...contents.keys()].filter(file => path.dirname(file) === sharedDir).map(file => path.basename(file));
        const present = names.filter(n => /^rt-[a-z-]+\.js$/.test(n));
        const unlisted = present.filter(n => !RUNTIME_PARTS.includes(n));
        if (unlisted.length > 0) {
            throw new Error(
                `Runtime part(s) not listed in RUNTIME_PARTS, so they would be missing from every ` +
                `offline snapshot: ${unlisted.join(', ')}`);
        }

        const bodies = [];
        for (const name of RUNTIME_PARTS) {
            const src = readVirtual(path.join(sharedDir, name), contents).toString('utf8');
            const out = [];
            const lines = src.split('\n');
            for (let i = 0; i < lines.length; i++) {
                const line = lines[i];
                if (/\bimport\s*\(/.test(line)) {
                    throw new Error(`${name}:${i + 1} uses a dynamic import the bundle cannot inline.`);
                }
                const imported = INTRA_IMPORT.exec(line);
                if (imported) {
                    if (!RUNTIME_PARTS.includes(imported[2])) {
                        throw new Error(`${name}:${i + 1} imports a module not listed in RUNTIME_PARTS: ${imported[2]}`);
                    }
                    continue;
                }
                if (ANY_IMPORT.test(line)) {
                    throw new Error(`${name}:${i + 1} imports something the bundle cannot inline: ${line.trim()}`);
                }
                if (EXPORT_DECL.test(line)) {
                    out.push(line.replace(EXPORT_DECL, ''));
                    continue;
                }
                if (ANY_EXPORT.test(line)) {
                    throw new Error(`${name}:${i + 1} uses an export form the bundle cannot strip: ${line.trim()}`);
                }
                out.push(line);
            }
            bodies.push(`// ─── ${name} ───\n${out.join('\n')}`);
        }

        const banner = `// @ts-nocheck — generated bundle; check the canonical parts.
/* GENERATED FILE - DO NOT EDIT.
 * Built from the rt-*.js parts by: node .\\scripts\\sync-assets.js
 * Consumed by src/ETL-SQL.Reporting for single-file .etlsnap snapshots.
 */\n\n`;
        const bundle = `${banner}(function () {\n    'use strict';\n\n${bodies.join('\n\n')}\n})();\n`;
        // Reject syntax errors and duplicate declarations before any host copy is written.
        new Script(bundle, { filename: RUNTIME_BUNDLE });
        return bundle;
    }

    async function syncOrCheckBundle() {
        const bundlePath = path.join(sharedDir, RUNTIME_BUNDLE);
        const expected = await buildRuntimeBundle();
        const earlierFormat = await buildRuntimeBundle(legacyVirtual);
        plannedOutputs.push({ path: relative(bundlePath), kind: 'offline-bundle', source: 'scripts/sync-assets.js',
            inputs: RUNTIME_PARTS.map(part => relative(path.join(sharedDir, part))), content: expected,
            bootstrapContents: [earlierFormat] });
        virtual.set(bundlePath, Buffer.from(expected));
        legacyVirtual.set(bundlePath, Buffer.from(earlierFormat));
        log(`  ${RUNTIME_BUNDLE} OK`);
    }

    // ── What each host actually loads ────────────────────────────────────────────
    //
    // Every host used to receive the whole Shared tree. ReportPlayer never loads anything under
    // designer/, and the VS Code extension loads the report designer and the connection wizard, not
    // Studio — yet both shipped all of Studio. Unloaded files still cost a sync, a drift check, and
    // surface in a webview's resource roots, and they read as supported when nothing mounts them.
    //
    // VS Code's designer set is the import closure of the files its webviews name, walked here on every
    // run, so moving a module between files cannot silently drop one the designer still needs.
    const VSCODE_DESIGNER_ENTRIES = ['designer/designer.js', 'designer/connection-wizard.js', 'designer/designer.css'];
    const MODULE_SPECIFIER = /(?:import|export)\s[^'"]*?from\s*['"](\.[^'"]+)['"]|import\s*\(\s*['"](\.[^'"]+)['"]\s*\)|import\s*['"](\.[^'"]+)['"]/g;

    async function importClosure(entries) {
        const seen = new Set();
        const stack = [...entries];
        while (stack.length) {
            const rel = stack.pop();
            if (seen.has(rel)) continue;
            seen.add(rel);
            const file = path.join(sharedDir, rel);
            if (!virtual.has(file)) throw new Error(`Shared asset import missing: ${rel}`);
            if (!rel.endsWith('.js')) continue;
            const text = readVirtual(file).toString('utf8');
            for (const match of text.matchAll(MODULE_SPECIFIER)) {
                const target = match[1] || match[2] || match[3];
                stack.push(path.relative(sharedDir, path.resolve(path.dirname(file), target)).replace(/\\/g, '/'));
            }
        }
        return seen;
    }

    /** Whether a Shared file belongs in a host, by its path relative to Shared. */
    function hostWants(label, normalizedRel, vsCodeDesigner) {
        if (!normalizedRel.startsWith('designer/')) return true;
        if (label === 'ReportPlayer') return false;
        if (label === 'VS Code') return vsCodeDesigner.has(normalizedRel);
        return true;
    }

    async function existsAsync(p) {
        try {
            await fs.access(p);
            return true;
        } catch {
            return false;
        }
    }

    async function syncOrCheck(filePath, relativePath, targetDir, label, fileContent) {
        const targetPath = path.join(targetDir, relativePath);
        expectedTargets.add(path.resolve(targetPath));
        plannedOutputs.push({ path: relative(targetPath), kind: 'host-copy', source: relative(filePath),
            content: getExpectedContent(filePath, relativePath, fileContent),
            bootstrapContents: [getExpectedContent(filePath, relativePath, readVirtual(filePath, legacyVirtual))] });
        log(`    -> ${label} planned`);
    }

    async function writeOrCheckBuildManifest(preparedInputs) {
        const inputs = new Set(expectedTargets);
        inputs.add(path.join(repoRoot, 'artifacts/browser-assets.json'));
        const sourceGroups = [
            ['src/ETL-SQL.ReportRuntime/Resources/TypeScript', 'src/ETL-SQL.ReportRuntime/Resources/Shared'],
            ['src/ETL-SQL.Portal/BrowserSources', 'src/ETL-SQL.Portal/wwwroot/js'],
        ];
        for (const [sourceRel, outputRel] of sourceGroups) {
            const sourceDir = path.join(repoRoot, sourceRel);
            for (const input of await walk(sourceDir)) {
                inputs.add(input);
                if (input.endsWith('.ts') && !input.endsWith('.d.ts'))
                    inputs.add(path.join(repoRoot, outputRel, path.relative(sourceDir, input).replace(/\.ts$/, '.js')));
            }
        }
        for (const input of await walk(sharedDir)) inputs.add(input);
        for (const input of await walk(portalJsDir)) inputs.add(input);
        for (const input of await walk(path.join(repoRoot, 'types'))) inputs.add(input);
        for (const rel of ['scripts/compile-browser.mjs', 'scripts/sync-assets.js', 'tsconfig.browser-build.json',
            'scripts/lib/browser-asset-inputs.cjs',
            'scripts/lib/browser-toolchain.cjs', 'scripts/typecheck/.npmrc',
            'scripts/typecheck/package.json', 'scripts/typecheck/package-lock.json']) {
            const input = path.join(repoRoot, rel);
            if (await existsAsync(input)) inputs.add(input);
        }
        const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
        const entries = [];
        for (const input of [...inputs].sort()) {
            // Missing outputs have already been diagnosed by the sync check. Do not obscure that
            // diagnosis by trying to hash a file which does not exist.
            if (!(await existsAsync(input))) continue;
            const relative = path.relative(repoRoot, input).replaceAll('\\', '/');
            const hash = createHash('sha256').update(await fs.readFile(input)).digest('hex').toUpperCase();
            entries.push(`    <_BrowserPreparedAsset Include="$(BrowserAssetRoot)${xml(relative)}"><ExpectedHash>${hash}</ExpectedHash></_BrowserPreparedAsset>`);
        }
        const content = `<Project>\n  <!-- Generated by scripts/sync-assets.js; local build inputs and outputs. -->\n  <ItemGroup>\n${entries.join('\n')}\n  </ItemGroup>\n</Project>\n`;
        const manifest = path.join(repoRoot, 'artifacts/browser-assets.props');
        const actual = await existsAsync(manifest) ? await fs.readFile(manifest, 'utf8') : null;
        if (browserInputSignature(repoRoot) !== preparedInputs)
            throw new Error('Browser sources changed during preparation. Rerun node scripts/sync-assets.js.');
        if (checkMode && actual !== content) drift.push('Browser build manifest missing or stale: artifacts/browser-assets.props');
        else if (!checkMode && actual !== content) {
            safeAssetPath(repoRoot, 'artifacts/browser-assets.props');
            await fs.mkdir(path.dirname(manifest), { recursive: true });
            await fs.writeFile(manifest, content, 'utf8');
        }
    }

    function generatedAssets() {
        return [...new Set([...compilerOutputs, path.join(sharedDir, RUNTIME_BUNDLE), ...expectedTargets])]
            .map(file => path.relative(repoRoot, file).replaceAll('\\', '/')).sort();
    }

    function generatedJavaScript() {
        return [...new Set([...compilerOutputs, path.join(sharedDir, RUNTIME_BUNDLE),
            ...[...expectedTargets].filter(file => file.endsWith('.js'))])]
            .map(file => path.relative(repoRoot, file).replaceAll('\\', '/')).sort();
    }

    async function run() {
        const preparedInputs = browserInputSignature(repoRoot);
        const compileBrowser = compiler ?? (await import('./compile-browser.mjs')).compileBrowser;
        const compilation = compileBrowser({ root: repoRoot, toolchain, check: checkMode, plan: true });
        plannedOutputs.push(...compilation.plannedOutputs);
        const obsolete = new Set(compilation.orphans.map(file => path.resolve(file)));
        for (const file of await walk(sharedDir)) {
            if (obsolete.has(path.resolve(file))) continue;
            safeAssetPath(repoRoot, relative(file));
            const content = await fs.readFile(file);
            virtual.set(path.resolve(file), content);
            legacyVirtual.set(path.resolve(file), content);
        }
        for (const output of compilation.plannedOutputs) {
            const file = path.resolve(repoRoot, output.path);
            if (file.startsWith(sharedDir + path.sep)) {
                virtual.set(file, Buffer.from(output.content));
                legacyVirtual.set(file, Buffer.from(output.bootstrapContents[0]));
            }
        }
        compilerOutputs = compilation.generatedFiles;
        if (!(await existsAsync(sharedDir))) {
            console.error(`Shared source directory not found: ${sharedDir}`);
            throw new Error(`Shared source directory not found: ${sharedDir}`);
        }

        await syncOrCheckBundle();
        const files = [...virtual.keys()].sort();
        // Retained JavaScript may still import a migrated owner. Validate those dependencies
        // against the planned tree so cleanup cannot leave an authored module pointing at an orphan.
        await importClosure(files.filter(file => file.endsWith('.js') && file !== path.join(sharedDir, RUNTIME_BUNDLE))
            .map(getAssetRelativePath));
        const vsCodeDesigner = await importClosure(VSCODE_DESIGNER_ENTRIES);

        for (const file of files) {
            const verb = checkMode ? "Checking" : "Syncing";
            const relativePath = getAssetRelativePath(file);
            const normalizedRel = relativePath.replace(/\\/g, '/');
            log(`  ${verb} ${relativePath}...`);

            const fileContent = readVirtual(file);

            // 1. VS Code Media — the designer and connection wizard, not Studio
            if (hostWants("VS Code", normalizedRel, vsCodeDesigner))
                await syncOrCheck(file, relativePath, vsCodeMedia, "VS Code", fileContent);

            // 2. ReportPlayer — the report runtime only
            if (hostWants("ReportPlayer", normalizedRel, vsCodeDesigner))
                await syncOrCheck(file, relativePath, playerWwwRoot, "ReportPlayer", fileContent);

            // 2b. Workstation Editor / desktop Studio
            await syncOrCheck(file, relativePath, workstationWwwRoot, "WorkstationEditor", fileContent);

            // 3. Portal
            {
                const normalizedRel = relativePath.replace(/\\/g, '/');
                if (normalizedRel.startsWith('maps/')) {
                    await syncOrCheck(file, relativePath, portalWwwRoot, "Portal (Maps)", fileContent);
                } else if (normalizedRel.startsWith('designer/')) {
                    await syncOrCheck(file, relativePath, portalWwwRoot, "Portal (Designer)", fileContent);
                } else {
                    const ext = path.extname(file).toLowerCase();
                    if (ext === '.js') {
                        await syncOrCheck(file, relativePath, portalJsDir, "Portal (JS)", fileContent);
                    } else if (ext === '.css') {
                        await syncOrCheck(file, relativePath, portalCssDir, "Portal (CSS)", fileContent);
                    } else {
                        await syncOrCheck(file, relativePath, portalJsDir, "Portal (Misc)", fileContent);
                    }
                }
            }
        }

        if (browserInputSignature(repoRoot) !== preparedInputs)
            throw new Error('Browser sources changed during preparation. Rerun node scripts/sync-assets.js.');
        // Compiler emits, offline bundle, import closures, retained-file conflicts, and cleanup
        // permissions are validated together before the first delivered byte changes.
        prepareOutputs(repoRoot, plannedOutputs, { check: checkMode });
        if (browserInputSignature(repoRoot) !== preparedInputs)
            throw new Error('Browser sources changed during preparation. Rerun node scripts/sync-assets.js.');
        await writeOrCheckBuildManifest(preparedInputs);

        if (checkMode && drift.length > 0) {
            console.error(`\nShared report assets have drifted from src/ETL-SQL.ReportRuntime/Resources/Shared:`);
            for (const item of drift) {
                console.error(`  - ${item}`);
            }
            console.warn(`\nRun node .\\scripts\\sync-assets.js to refresh host copies.`);
            throw new Error('Shared report asset verification failed.');
        }

        log(`\n${modeLabel} Complete.`);
    }

    await run();
    return { files: expectedTargets.size, generatedJavaScript: generatedJavaScript(), generatedAssets: generatedAssets() };
}

module.exports = { syncAssets };

if (require.main === module) syncAssets({ check: process.argv.includes('-Check') || process.argv.includes('--check') }).catch(err => {
    console.error(err);
    process.exit(1);
});
