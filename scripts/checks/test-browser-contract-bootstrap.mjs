import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { bootstrapBrowserContracts, recoverBrowserContracts } from '../generate-browser-contracts.mjs';
import synchronizer from '../sync-assets.js';
import { compileBrowser } from '../compile-browser.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const base = path.join(repo, 'artifacts');
fs.mkdirSync(base, { recursive: true });
const root = fs.mkdtempSync(path.join(base, 'contract-bootstrap fixture espaces-é-$literal-'));
const contracts = ['types/etlsql-contracts.generated.d.ts',
    'src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-routes.generated.ts',
    'src/ETL-SQL.Portal/wwwroot/js/api-contracts.generated.js'];
const schema = path.join(root, 'src/ETL-SQL.Portal/BrowserContracts/critical-api-contracts.json');
const write = (file, bytes) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, bytes); };
const validSchema = JSON.stringify({ sample: { kind: 'object', fields: { name: 'string' } } });
const snapshot = () => contracts.map(relative => fs.readFileSync(path.join(root, relative)));
const preserved = before => {
    assert.deepEqual(snapshot(), before);
    assert.equal(fs.existsSync(path.join(root, '.browser-assets.lock')), false);
};
const staged = ({ staging }, invalid = false) => {
    write(path.join(staging, 'outputs', contracts[0]), 'export interface Sample { name: string; }\n');
    write(path.join(staging, 'outputs', contracts[1]), invalid ? 'export const route: number = "bad";\n' :
        'export const route = "/api/sample" as const;\n');
};
const bootstrap = options => bootstrapBrowserContracts({ root, compilerRoot: repo, ...options });
try {
    const resources = bootstrap => {
        const result = spawnSync('dotnet', ['msbuild', path.join(repo, 'src/ETL-SQL.Reporting/ETL-SQL.Reporting.csproj'),
            '-nologo', '-getItem:EmbeddedResource', ...(bootstrap ? ['-p:BootstrapBrowserContracts=true'] : [])], { encoding: 'utf8' });
        assert.ifError(result.error);
        assert.equal(result.status, 0, result.stdout + result.stderr);
        return JSON.parse(result.stdout).Items.EmbeddedResource.map(item => item.LogicalName);
    };
    assert.ok(resources(false).includes('runtime.report-runtime.js'));
    assert.ok(resources(false).includes('runtime.feedback.js'));
    assert.equal(resources(true).includes('runtime.report-runtime.js'), false);
    assert.equal(resources(true).includes('runtime.feedback.js'), false);
    for (const project of ['Reporting', 'Portal']) {
        const publish = spawnSync('dotnet', ['msbuild', path.join(repo, `src/ETL-SQL.${project}/ETL-SQL.${project}.csproj`),
            '-nologo', '-target:RejectBrowserContractBootstrapPublishing', '-p:BootstrapBrowserContracts=true'], { encoding: 'utf8' });
        assert.ifError(publish.error);
        assert.notEqual(publish.status, 0);
        assert.match(publish.stdout + publish.stderr, /bootstrap assemblies cannot be published/);
    }
    write(schema, validSchema);
    for (const [index, relative] of contracts.entries()) write(path.join(root, relative), `old contract ${index}\n`);
    const before = snapshot();
    await assert.rejects(bootstrap({ generate: async context => {
        staged(context); throw new Error('Host database initialization failed');
    } }), /Host database initialization failed/);
    preserved(before);
    await assert.rejects(bootstrap({ generate: async context => staged(context, true) }), /strict validation/);
    preserved(before);
    await assert.rejects(bootstrap({ generate: async () => {} }), /did not produce/);
    preserved(before);
    write(schema, JSON.stringify({ sample: { kind: 'object', fields: { bad: 'missingType' } } }));
    let invoked = false;
    await assert.rejects(bootstrap({ generate: async () => { invoked = true; } }), /Unknown API contract field type/);
    assert.equal(invoked, false);
    preserved(before);
    write(schema, validSchema);
    const addedInput = path.join(root, 'src/Changed.cs');
    await assert.rejects(bootstrap({ generate: async context => {
        staged(context); write(addedInput, 'class Changed {}\n');
    } }), /inputs changed/);
    preserved(before);
    fs.unlinkSync(addedInput);
    await assert.rejects(bootstrap({ generate: async context => staged(context), afterPromotion: async () => {
        throw new Error('Simulated promotion failure');
    } }), /Simulated promotion failure/);
    preserved(before);
    await assert.rejects(bootstrap({ generate: async context => staged(context), afterPromotion: async relative => {
        if (relative === contracts[1]) write(addedInput, 'class ChangedDuringPromotion {}\n');
    } }), /inputs changed during promotion/);
    preserved(before);
    fs.unlinkSync(addedInput);
    await assert.rejects(bootstrap({ generate: async context => {
        staged(context); write(path.join(root, contracts[0]), 'concurrent owner edit\n');
    } }), /Tracked contract changed/);
    assert.equal(fs.readFileSync(path.join(root, contracts[0]), 'utf8'), 'concurrent owner edit\n');
    const current = snapshot();
    const interrupt = () => {
        const result = spawnSync(process.execPath, ['--input-type=module', '-e', `
            import fs from 'node:fs';
            import path from 'node:path';
            import { bootstrapBrowserContracts, recoverBrowserContracts } from ${JSON.stringify(new URL('../generate-browser-contracts.mjs', import.meta.url).href)};
            const [root, compilerRoot] = process.argv.slice(1);
            await bootstrapBrowserContracts({ root, compilerRoot, generate: async ({ staging }) => {
                for (const [relative, bytes] of ${JSON.stringify(contracts.slice(0, 2).map((relative, index) => [relative, index === 0 ? 'export interface Sample { name: string; }\n' : 'export const route = "/api/sample" as const;\n']))}) {
                    const file = path.join(staging, 'outputs', relative);
                    fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, bytes);
                }
            }, afterPromotion: () => {
                try { recoverBrowserContracts({ root }); process.exit(74); }
                catch (error) { if (!/still running/.test(error.message)) throw error; }
                process.exit(73);
            } });`, root, repo], { encoding: 'utf8' });
        assert.ifError(result.error);
        assert.equal(result.status, 73, result.stdout + result.stderr);
        assert.ok(fs.existsSync(path.join(root, 'artifacts/browser-contracts-transaction.json')));
    };
    interrupt();
    // A manually removed process lock must not let a partial contract set pass preparation.
    fs.unlinkSync(path.join(root, '.browser-assets.lock'));
    await assert.rejects(synchronizer.syncAssets({ root, toolchain: repo, quiet: true }), /contracts need recovery/);
    assert.throws(() => compileBrowser({ root, toolchain: repo }), /contracts need recovery/);
    write(path.join(root, 'ETL-SQL.ReportRuntime.proj'), `<Project><Import Project="${path.join(repo, 'Directory.Build.targets')}" /></Project>`);
    const build = spawnSync('dotnet', ['msbuild', path.join(root, 'ETL-SQL.ReportRuntime.proj'), '-nologo',
        '-target:VerifyPreparedBrowserAssets', `-p:BrowserAssetRoot=${root}${path.sep}`, '-verbosity:minimal'], { encoding: 'utf8' });
    assert.ifError(build.error);
    assert.notEqual(build.status, 0);
    assert.match(build.stdout + build.stderr, /contracts need recovery/);
    assert.deepEqual(recoverBrowserContracts({ root }), { restored: true });
    preserved(current);
    interrupt();
    write(path.join(root, contracts[1]), 'edit made after interruption\n');
    await assert.rejects(bootstrap({ generate: async context => staged(context) }), /transaction needs recovery/);
    assert.throws(() => recoverBrowserContracts({ root }), /cannot overwrite a concurrent edit/);
    assert.equal(fs.readFileSync(path.join(root, contracts[1]), 'utf8'), 'edit made after interruption\n');
    assert.ok(fs.existsSync(path.join(root, '.browser-assets.lock')));
    write(path.join(root, contracts[1]), current[1]);
    recoverBrowserContracts({ root });
    preserved(current);
    const ignoredCache = path.join(root, 'src/etl-sql-vscode/.vscode-test');
    const endpointTarget = path.join(root, 'private-test-endpoint');
    fs.mkdirSync(ignoredCache, { recursive: true });
    fs.mkdirSync(endpointTarget);
    const endpoint = path.join(ignoredCache, 'local-endpoint');
    fs.symlinkSync(endpointTarget, endpoint, 'junction');
    try {
        await bootstrap({ generate: async context => {
            staged(context);
            write(path.join(ignoredCache, 'state.json'), '{"cacheChanged":true}');
        } });
    } finally {
        fs.unlinkSync(endpoint);
    }
    for (const relative of contracts) fs.unlinkSync(path.join(root, relative));
    const result = await bootstrap({ generate: async context => staged(context) });
    assert.deepEqual(result.outputs, contracts);
    assert.match(fs.readFileSync(path.join(root, contracts[0]), 'utf8'), /interface Sample/);
    assert.match(fs.readFileSync(path.join(root, contracts[1]), 'utf8'), /api\/sample/);
    assert.match(fs.readFileSync(path.join(root, contracts[2]), 'utf8'), /assertApiContract/);
    assert.equal(fs.existsSync(path.join(root, '.browser-assets.lock')), false);
    console.log('Browser contract bootstrap: staged failures, invalid/missing contracts, concurrent edits, rollback, interruption/recovery, blocked consumers, and absent outputs passed.');
} finally {
    assert.equal(path.dirname(path.resolve(root)), path.resolve(base));
    fs.rmSync(root, { recursive: true, force: true });
}
