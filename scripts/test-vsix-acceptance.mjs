#!/usr/bin/env node
// Exercise packaged VSIX webviews inside real VS Code via @vscode/test-electron.
// Usage: node scripts/test-vsix-acceptance.mjs --vsix <path> --out <dir> [--inventory <path>]
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);

function parseArgs(args) {
    const options = {};
    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--vsix' && args[i + 1]) options.vsix = path.resolve(args[++i]);
        else if (args[i] === '--out' && args[i + 1]) options.out = path.resolve(args[++i]);
        else if (args[i] === '--inventory' && args[i + 1]) options.inventory = path.resolve(args[++i]);
        else if (args[i] === '--fixture' && args[i + 1]) options.fixture = path.resolve(args[++i]);
    }
    return options;
}

const options = parseArgs(process.argv.slice(2));
assert.ok(options.vsix, 'Usage: node scripts/test-vsix-acceptance.mjs --vsix <path> --out <dir>');
assert.ok(options.out, 'Missing required --out directory.');
assert.ok(fs.existsSync(options.vsix), `VSIX file not found: ${options.vsix}`);

const inventoryPath = options.inventory ?? path.join(repository, 'artifacts/browser-assets.json');
assert.ok(fs.existsSync(inventoryPath), `Browser assets inventory not found: ${inventoryPath}`);

fs.mkdirSync(options.out, { recursive: true });
const reportPath = path.join(options.out, 'packaged-webview.json');

// Ensure yauzl is available from scripts/vsix.
const yauzlPath = path.join(repository, 'scripts/vsix/node_modules/yauzl');
if (!fs.existsSync(yauzlPath)) {
    throw new Error('Missing yauzl dependency. Run npm ci --prefix scripts/vsix first.');
}
const yauzl = require(yauzlPath);

// Ensure @vscode/test-electron is available from src/etl-sql-vscode.
const testElectronModule = path.join(repository, 'src/etl-sql-vscode/node_modules/@vscode/test-electron');
if (!fs.existsSync(testElectronModule)) {
    throw new Error('Missing @vscode/test-electron dependency. Run npm ci --prefix src/etl-sql-vscode first.');
}
const { runTests } = require(testElectronModule);

function extractVsix(vsixFile, destDir) {
    return new Promise((resolve, reject) => {
        yauzl.open(vsixFile, { lazyEntries: true, autoClose: true }, (err, zip) => {
            if (err) return reject(err);
            zip.on('error', reject);
            zip.on('close', resolve);
            zip.readEntry();
            zip.on('entry', entry => {
                const name = entry.fileName;
                if (!name.startsWith('extension/')) return zip.readEntry();
                const relativePath = name.slice('extension/'.length);
                if (!relativePath) return zip.readEntry();

                const targetPath = path.join(destDir, relativePath);
                if (name.endsWith('/')) {
                    fs.mkdirSync(targetPath, { recursive: true });
                    return zip.readEntry();
                }

                fs.mkdirSync(path.dirname(targetPath), { recursive: true });
                zip.openReadStream(entry, (streamErr, readStream) => {
                    if (streamErr) return reject(streamErr);
                    const writeStream = fs.createWriteStream(targetPath);
                    readStream.pipe(writeStream);
                    writeStream.on('finish', () => {
                        if (process.platform !== 'win32') {
                            const isExec = ((entry.externalFileAttributes >>> 16) & 0o111) !== 0 ||
                                relativePath.startsWith('bin/');
                            if (isExec) {
                                try { fs.chmodSync(targetPath, 0o755); } catch {}
                            }
                        }
                        zip.readEntry();
                    });
                    writeStream.on('error', reject);
                });
            });
        });
    });
}

function grantWindowsAcl(dirPath) {
    if (process.platform !== 'win32') return;
    try {
        spawnSync('icacls', [dirPath, '/grant', '*S-1-15-2-1:(OI)(CI)(RX)'], {
            windowsHide: true,
            stdio: 'ignore',
        });
    } catch {}
}

const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-vsix-acceptance-'));
const unpackedExtension = path.join(tempDir, 'extension');
fs.mkdirSync(unpackedExtension, { recursive: true });

const fixturePath = options.fixture ?? path.join(tempDir, 'asset-proof.rptsql');
if (!fs.existsSync(fixturePath)) {
    fs.writeFileSync(fixturePath, 'CREATE DATASET &proof AS (SELECT 21 AS Value);\nCREATE VISUAL AssetProof AS TABLE (SOURCE = &proof);\n');
}

console.log(`Extracting ${path.basename(options.vsix)} to temporary directory...`);
await extractVsix(options.vsix, unpackedExtension);

const cachePath = path.join(repository, '.vscode-test');
fs.mkdirSync(cachePath, { recursive: true });
grantWindowsAcl(cachePath);

if (process.platform === 'linux' && !process.env.DISPLAY) {
    throw new Error('Running on headless Linux requires DISPLAY. Run under xvfb-run -a.');
}

try {
    console.log('Launching VS Code webview acceptance suite...');
    const exitCode = await runTests({
        cachePath,
        extensionDevelopmentPath: unpackedExtension,
        extensionTestsPath: path.resolve(repository, 'scripts/fixtures/vsix-webview-acceptance.cjs'),
        extensionTestsEnv: {
            ETLSQL_ACCEPTANCE_REPORT: reportPath,
            ETLSQL_ACCEPTANCE_INVENTORY: inventoryPath,
            ETLSQL_ACCEPTANCE_FIXTURE: fixturePath,
        },
        launchArgs: [
            fixturePath,
            '--disable-gpu',
            '--disable-gpu-sandbox',
            '--disable-updates',
            '--skip-welcome',
            '--skip-release-notes',
            '--disable-workspace-trust',
        ],
    });

    assert.equal(exitCode, 0, `VS Code test host exited with code ${exitCode}`);
    assert.ok(fs.existsSync(reportPath), `Acceptance report not created at ${reportPath}`);

    const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
    assert.equal(report.passed, true, `Acceptance report failed: ${JSON.stringify(report.errors)}`);
    assert.equal(report.errors.length, 0, `Acceptance reported errors: ${report.errors.join('; ')}`);
    assert.ok(report.views?.designer, 'Designer webview verification missing in report.');
    assert.ok(report.views?.preview, 'Preview webview verification missing in report.');

    console.log(`[SUCCESS] VSIX webview acceptance passed for ${path.basename(options.vsix)} (${report.installedAssets} assets, designer & preview webviews verified).`);
} finally {
    try {
        fs.rmSync(tempDir, { recursive: true, force: true });
    } catch {}
}
