const fs = require('fs').promises;
const path = require('path');
const { Script } = require('vm');

const repoRoot = path.resolve(__dirname, '..');
const sharedDir = path.join(repoRoot, 'src', 'ETL-SQL.ReportRuntime', 'Resources', 'Shared');

const vsCodeMedia = path.join(repoRoot, 'src', 'etl-sql-vscode', 'media');
const playerWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.ReportPlayer', 'wwwroot');
// Published fallback for the desktop Studio/editor host. Running from a checkout serves
// Resources/Shared directly, so drift here only surfaces in published builds.
const workstationWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.WorkstationEditor', 'wwwroot');
// The Workstation editor serves the canonical Shared folder when running from a checkout,
// but a published install has no repo tree — these copies are what ship.
const editorWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.WorkstationEditor', 'wwwroot');
const portalWwwRoot = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot');
const portalJsDir = path.join(portalWwwRoot, 'js');
const portalCssDir = path.join(portalWwwRoot, 'css');

const checkMode = process.argv.includes('-Check') || process.argv.includes('--check');
const modeLabel = checkMode ? "Check" : "Sync";

console.log("=======================================================");
console.log(` Shared Report Assets ${modeLabel} (Async Node.js)`);
console.log(` Source: ${sharedDir}`);
console.log("=======================================================\n");

const drift = [];
const failures = [];

async function walk(dir) {
    let files = [];
    const list = await fs.readdir(dir, { withFileTypes: true });
    for (const entry of list) {
        const res = path.resolve(dir, entry.name);
        if (entry.isDirectory()) {
            files = files.concat(await walk(res));
        } else {
            files.push(res);
        }
    }
    return files;
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

async function buildRuntimeBundle() {
    const names = await fs.readdir(sharedDir);
    const present = names.filter(n => /^rt-[a-z-]+\.js$/.test(n));
    const unlisted = present.filter(n => !RUNTIME_PARTS.includes(n));
    if (unlisted.length > 0) {
        throw new Error(
            `Runtime part(s) not listed in RUNTIME_PARTS, so they would be missing from every ` +
            `offline snapshot: ${unlisted.join(', ')}`);
    }

    const bodies = [];
    for (const name of RUNTIME_PARTS) {
        const src = await fs.readFile(path.join(sharedDir, name), 'utf8');
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
    if (checkMode) {
        if (!(await existsAsync(bundlePath))) {
            drift.push(`Offline bundle missing ${RUNTIME_BUNDLE}`);
            return;
        }
        const actual = await fs.readFile(bundlePath, 'utf8');
        if (actual !== expected) {
            drift.push(`Offline bundle drifted: ${RUNTIME_BUNDLE}`);
        }
        return;
    }
    await fs.writeFile(bundlePath, expected, 'utf8');
    console.log(`  ${RUNTIME_BUNDLE} OK`);
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
    if (!(await existsAsync(targetDir))) {
        return;
    }

    const targetPath = path.join(targetDir, relativePath);
    const expected = getExpectedContent(filePath, relativePath, fileContent);

    if (checkMode) {
        if (!(await existsAsync(targetPath))) {
            drift.push(`${label} missing ${relativePath}`);
            return;
        }

        const targetContent = await fs.readFile(targetPath, 'utf8');
        if (expected !== targetContent) {
            drift.push(`${label} drifted: ${relativePath}`);
        }
    } else {
        if (await existsAsync(targetPath)) {
            const targetContent = await fs.readFile(targetPath, 'utf8');
            if (targetContent === expected) {
                console.log(`    -> ${label} OK`);
                return;
            }
        }

        const targetParent = path.dirname(targetPath);
        if (!(await existsAsync(targetParent))) {
            await fs.mkdir(targetParent, { recursive: true });
        }

        try {
            await fs.writeFile(targetPath, expected, 'utf8');
            console.log(`    -> ${label} OK`);
        } catch (err) {
            failures.push(`${label} failed to write ${relativePath}: ${err.message}`);
            console.error(`    -> ${label} FAILED`);
        }
    }
}

async function run() {
    if (!(await existsAsync(sharedDir))) {
        console.error(`Shared source directory not found: ${sharedDir}`);
        process.exit(1);
    }

    await syncOrCheckBundle();
    const files = await walk(sharedDir);

    for (const file of files) {
        const verb = checkMode ? "Checking" : "Syncing";
        const relativePath = getAssetRelativePath(file);
        console.log(`  ${verb} ${relativePath}...`);

        const fileContent = await fs.readFile(file, 'utf8');

        // 1. VS Code Media
        await syncOrCheck(file, relativePath, vsCodeMedia, "VS Code", fileContent);

        // 2. ReportPlayer
        await syncOrCheck(file, relativePath, playerWwwRoot, "ReportPlayer", fileContent);

        // 2b. Workstation Editor / desktop Studio
        await syncOrCheck(file, relativePath, workstationWwwRoot, "WorkstationEditor", fileContent);

        // 2b. Workstation editor (published install assets)
        await syncOrCheck(file, relativePath, editorWwwRoot, "Workstation Editor", fileContent);

        // 3. Portal
        if ((await existsAsync(portalJsDir)) && (await existsAsync(portalCssDir))) {
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

    if (checkMode && drift.length > 0) {
        console.error(`\nShared report assets have drifted from src/ETL-SQL.ReportRuntime/Resources/Shared:`);
        for (const item of drift) {
            console.error(`  - ${item}`);
        }
        console.warn(`\nRun node .\\scripts\\sync-assets.js to refresh host copies.`);
        process.exit(1);
    }

    if (!checkMode && failures.length > 0) {
        console.error(`\nShared report asset sync failed:`);
        for (const item of failures) {
            console.error(`  - ${item}`);
        }
        process.exit(1);
    }

    console.log(`\n${modeLabel} Complete.`);
}

run().catch(err => {
    console.error(err);
    process.exit(1);
});
