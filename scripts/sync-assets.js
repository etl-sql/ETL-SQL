const fs = require('fs').promises;
const path = require('path');

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
