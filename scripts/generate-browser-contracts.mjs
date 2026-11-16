import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import assetInputs from './lib/browser-asset-inputs.cjs';
import toolchain from './lib/browser-toolchain.cjs';
import { generatePortalBrowserContract } from './generate-portal-browser-contract.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const outputs = ['types/etlsql-contracts.generated.d.ts',
    'src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-routes.generated.ts',
    'src/ETL-SQL.Portal/wwwroot/js/api-contracts.generated.js'];
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const read = file => fs.existsSync(file) ? fs.readFileSync(file) : null;
const same = (left, right) => left === null ? right === null : right !== null && left.equals(right);
const journalName = 'artifacts/browser-contracts-transaction.json';

function replace(file, content) {
    if (content === null) { if (fs.existsSync(file)) fs.unlinkSync(file); return; }
    fs.mkdirSync(path.dirname(file), { recursive: true });
    const temporary = `${file}.__contract-${process.pid}.tmp`;
    let descriptor;
    try {
        descriptor = fs.openSync(temporary, 'wx');
        fs.writeFileSync(descriptor, content);
        fs.fsyncSync(descriptor);
        fs.closeSync(descriptor);
        descriptor = undefined;
        fs.renameSync(temporary, file);
    } finally {
        if (descriptor !== undefined) fs.closeSync(descriptor);
        if (fs.existsSync(temporary)) fs.unlinkSync(temporary);
    }
}

function restore(root, entries) {
    // Validate the entire set before recovery writes. Neither normal rollback nor crash recovery
    // may overwrite bytes written by another editor or preparation process.
    const validate = entry => {
        const current = read(assetInputs.safeAssetPath(root, entry.path));
        if (!same(current, entry.before) && !same(current, entry.after))
            throw new Error(`Contract recovery cannot overwrite a concurrent edit: ${entry.path}. Review ${journalName}.`);
        return current;
    };
    entries.forEach(validate);
    for (const entry of [...entries].reverse()) {
        if (!same(validate(entry), entry.before)) replace(assetInputs.safeAssetPath(root, entry.path), entry.before);
    }
}

const alive = pid => {
    try { process.kill(pid, 0); return true; }
    catch (error) { if (error.code === 'ESRCH') return false; throw error; }
};

export function recoverBrowserContracts({ root = repo } = {}) {
    root = path.resolve(root);
    const journalPath = assetInputs.safeAssetPath(root, journalName);
    const journal = JSON.parse(fs.readFileSync(journalPath, 'utf8'));
    if (journal.version !== 1 || !['promoting', 'complete'].includes(journal.state) ||
        !Number.isSafeInteger(journal.pid) || journal.pid <= 0 || journal.entries?.length !== outputs.length)
        throw new Error(`Invalid browser contract transaction: ${journalName}.`);
    const decode = value => {
        if (value === null) return null;
        if (typeof value !== 'string' || Buffer.from(value, 'base64').toString('base64') !== value)
            throw new Error(`Invalid browser contract backup: ${journalName}.`);
        return Buffer.from(value, 'base64');
    };
    const entries = journal.entries.map((entry, index) => {
        if (entry.path !== outputs[index] || typeof entry.after !== 'string')
            throw new Error(`Invalid browser contract transaction output: ${journalName}.`);
        return { path: entry.path, before: decode(entry.before), after: decode(entry.after) };
    });
    if (alive(journal.pid)) throw new Error(`Browser contract generator ${journal.pid} is still running. Wait before recovery.`);
    const recoveryPath = assetInputs.safeAssetPath(root, 'artifacts/browser-contracts-recovery.lock');
    const recoveryLock = fs.openSync(recoveryPath, 'wx');
    try {
        fs.writeFileSync(recoveryLock, `${process.pid}\n`);
        const lockPath = assetInputs.safeAssetPath(root, '.browser-assets.lock');
        if (fs.existsSync(lockPath)) {
            const pid = Number(fs.readFileSync(lockPath, 'utf8').trim());
            if (!Number.isSafeInteger(pid) || pid <= 0 || alive(pid))
                throw new Error('Browser asset preparation lock is active or invalid. Preserve it before recovery.');
        } else {
            const lock = fs.openSync(lockPath, 'wx');
            try { fs.writeFileSync(lock, `${process.pid}\n`); } finally { fs.closeSync(lock); }
        }
        if (journal.state === 'complete') {
            for (const entry of entries) if (!same(read(assetInputs.safeAssetPath(root, entry.path)), entry.after))
                throw new Error(`Completed browser contract changed: ${entry.path}. Preserve that edit before recovery.`);
        } else restore(root, entries);
        fs.unlinkSync(journalPath);
        fs.unlinkSync(lockPath);
        return { restored: journal.state === 'promoting' };
    } finally {
        fs.closeSync(recoveryLock);
        fs.unlinkSync(recoveryPath);
    }
}

function signature(root) {
    const sourceFiles = directory => {
        if (!fs.existsSync(directory)) return [];
        return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
            const file = path.join(directory, entry.name);
            if (['bin', 'obj', 'artifacts', 'build', 'node_modules', 'dist', 'out', '.vscode-test'].includes(entry.name)) return [];
            if (entry.isSymbolicLink()) throw new Error(`Contract source symlink is unsupported: ${path.relative(root, file)}`);
            if (entry.isDirectory()) return sourceFiles(file);
            if (!/\.(?:cs|csproj|props|targets|json)$/.test(entry.name)) return [];
            return [file];
        });
    };
    const sources = ['src', 'tests/ETL-SQL.Portal.Tests'].flatMap(relative => sourceFiles(assetInputs.safeAssetPath(root, relative)));
    const excluded = new Set(outputs.map(relative => path.resolve(root, relative)));
    const settings = ['Directory.Packages.props', 'NuGet.Config', 'global.json']
        .map(relative => assetInputs.safeAssetPath(root, relative)).filter(file => fs.existsSync(file));
    const files = [...new Set([...sources, ...settings, ...assetInputs.browserSourceFiles(root), ...assetInputs.browserPreparationFiles(root)])]
        .filter(file => !excluded.has(path.resolve(file))).sort();
    return hash(Buffer.from(files.map(file => `${path.relative(root, file)}:${hash(fs.readFileSync(file))}`).join('\n')));
}

async function runGenerators({ root, staging }) {
    const buildDirectory = assetInputs.safeAssetPath(staging, 'dotnet');
    try {
        await new Promise((resolve, reject) => {
            const child = spawn('dotnet', ['test', 'tests/ETL-SQL.Portal.Tests', '--artifacts-path', buildDirectory,
                '-p:RequiresBrowserAssets=false', '-p:BootstrapBrowserContracts=true',
                '--logger', 'trx;LogFileName=browser-contracts.trx', '--results-directory', path.join(staging, 'test-results'),
                '--filter', 'FullyQualifiedName~BrowserContractsGeneratorTests'], {
                cwd: root, stdio: 'inherit', env: { ...process.env, ETLSQL_UPDATE_BROWSER_CONTRACTS: '1',
                    ETLSQL_BROWSER_CONTRACTS_STAGING: path.join(staging, 'outputs') }
            });
            child.once('error', reject);
            child.once('close', (code, signal) => code === 0 ? resolve() :
                reject(new Error(`Browser contract host/DTO generation failed (${signal ?? code}). Tracked contracts were preserved.`)));
        });
    } finally {
        // Keep staged contracts and test diagnostics, not gigabytes of disposable host binaries.
        // Revalidate ancestors immediately before recursive cleanup to reject redirected paths.
        const checked = assetInputs.safeAssetPath(staging, 'dotnet');
        if (checked !== buildDirectory || !checked.startsWith(path.resolve(staging) + path.sep))
            throw new Error('Browser contract build cleanup escaped its staging directory.');
        fs.rmSync(checked, { recursive: true, force: true });
    }
}

// The .NET bootstrap artifacts stay isolated: they cannot satisfy a later ordinary host build.
// Tests write only to staging; no tracked declaration or route changes before the complete run passes.
export async function bootstrapBrowserContracts({ root = repo, compilerRoot = root, generate = runGenerators,
    afterPromotion } = {}) {
    root = path.resolve(root);
    const ts = toolchain.loadBrowserCompiler(compilerRoot);
    const journalPath = assetInputs.safeAssetPath(root, journalName);
    if (fs.existsSync(journalPath)) throw new Error('A browser contract transaction needs recovery. Run node scripts/generate-browser-contracts.mjs --recover.');
    const lockPath = assetInputs.safeAssetPath(root, '.browser-assets.lock');
    const lock = fs.openSync(lockPath, 'wx');
    let preserveLock = false;
    let staging;
    try {
        fs.writeFileSync(lock, `${process.pid}\n`);
        const artifactRoot = assetInputs.safeAssetPath(root, 'artifacts');
        fs.mkdirSync(artifactRoot, { recursive: true });
        staging = fs.mkdtempSync(path.join(artifactRoot, 'browser-contracts-'));
        const before = signature(root);
        const plannedApi = generatePortalBrowserContract(root);
        const originals = new Map(outputs.map(relative => [relative, read(assetInputs.safeAssetPath(root, relative))]));
        await generate({ root, staging });
        const planned = new Map(outputs.slice(0, 2).map(relative => {
            const file = assetInputs.safeAssetPath(staging, `outputs/${relative}`);
            if (!fs.existsSync(file)) throw new Error(`Contract generator did not produce ${relative}. Tracked contracts were preserved.`);
            return [relative, fs.readFileSync(file)];
        }));
        planned.set(outputs[2], Buffer.from(plannedApi));
        const program = ts.createProgram(outputs.slice(0, 2).map(relative => path.join(staging, 'outputs', relative)), {
            noEmit: true, strict: true, target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.ES2020,
            types: [], skipLibCheck: false
        });
        const errors = ts.getPreEmitDiagnostics(program).filter(diagnostic => diagnostic.category === ts.DiagnosticCategory.Error);
        if (errors.length) throw new Error('Generated browser contracts failed strict validation: ' +
            errors.map(error => ts.flattenDiagnosticMessageText(error.messageText, '\n')).join('\n'));
        if (before !== signature(root)) throw new Error('Contract inputs changed during bootstrap. Tracked contracts were preserved.');
        for (const [relative, bytes] of originals) {
            const current = read(assetInputs.safeAssetPath(root, relative));
            if ((current && hash(current)) !== (bytes && hash(bytes)))
                throw new Error(`Tracked contract changed during bootstrap: ${relative}. Preserve that edit before retrying.`);
        }
        const entries = [...planned].map(([relative, after]) => ({ path: relative, before: originals.get(relative), after }));
        const journal = { version: 1, state: 'promoting', pid: process.pid, entries: entries.map(entry => ({
            path: entry.path, before: entry.before?.toString('base64') ?? null, after: entry.after.toString('base64')
        })) };
        replace(journalPath, Buffer.from(JSON.stringify(journal)));
        try {
            for (const [relative, content] of planned) {
                const file = assetInputs.safeAssetPath(root, relative);
                if (!same(read(file), originals.get(relative)))
                    throw new Error(`Tracked contract changed during promotion: ${relative}. Preserve that edit before retrying.`);
                replace(file, content);
                await afterPromotion?.(relative);
            }
            if (before !== signature(root)) throw new Error('Contract inputs changed during promotion.');
            for (const entry of entries) if (!same(read(assetInputs.safeAssetPath(root, entry.path)), entry.after))
                throw new Error(`Tracked contract changed during promotion: ${entry.path}.`);
            replace(journalPath, Buffer.from(JSON.stringify({ ...journal, state: 'complete' })));
            fs.unlinkSync(journalPath);
        } catch (error) {
            try {
                restore(root, entries);
                fs.unlinkSync(journalPath);
            } catch (rollbackError) {
                preserveLock = true;
                throw new Error(`Contract rollback failed: ${rollbackError.message} Keep the preparation lock and review ${staging}.`, { cause: error });
            }
            throw error;
        }
        return { staging, outputs: [...planned.keys()] };
    } finally {
        fs.closeSync(lock);
        if (!preserveLock) fs.unlinkSync(lockPath);
    }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        if (process.argv.includes('--recover')) {
            const result = recoverBrowserContracts();
            console.log(result.restored ? 'Restored the previous browser contracts. Rerun bootstrap before preparation.' : 'Verified the completed browser contract set. Rerun preparation.');
        } else {
            const result = await bootstrapBrowserContracts();
            console.log(`Generated ${result.outputs.length} browser contracts. Run node scripts/sync-assets.js, then its -Check mode.`);
        }
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
