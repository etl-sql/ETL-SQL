import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assetInputs from './lib/browser-asset-inputs.cjs';
import toolchainTools from './lib/browser-toolchain.cjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const marker = '/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.';
const surfaces = [
    ['src/ETL-SQL.ReportRuntime/Resources/TypeScript', 'src/ETL-SQL.ReportRuntime/Resources/Shared'],
    ['src/ETL-SQL.Portal/BrowserSources', 'src/ETL-SQL.Portal/wwwroot/js'],
];
const files = assetInputs.browserFiles;
const key = p => path.resolve(p).replaceAll('\\', '/');

// root/toolchain parameters let regression tests exercise the real compiler in an isolated tree.
export function compileBrowser({ root = repo, check = false, toolchain = repo, plan = false } = {}) {
    const ts = toolchainTools.loadBrowserCompiler(toolchain);
    const ownership = new Map();
    const outputOwners = new Map();
    const groups = surfaces.map(([source, output]) => {
        const sourceRoot = assetInputs.safeAssetPath(root, source), outputRoot = assetInputs.safeAssetPath(root, output);
        const inputs = files(sourceRoot).filter(p => p.endsWith('.ts') && !p.endsWith('.d.ts'));
        for (const input of inputs) {
            const target = path.join(outputRoot, path.relative(sourceRoot, input).replace(/\.ts$/, '.js'));
            const collisionKey = key(target).toLowerCase();
            if (outputOwners.has(collisionKey)) throw new Error(`Browser output collision: ${target}`);
            ownership.set(key(input), target);
            outputOwners.set(collisionKey, input);
        }
        return { sourceRoot, outputRoot, inputs };
    });
    const moduleOwners = new Map(outputOwners);
    const moduleOwnersExact = new Map([...ownership].map(([input, output]) => [key(output), input]));
    for (const input of groups[0].inputs) {
        const relative = path.relative(groups[0].sourceRoot, input).replace(/\.ts$/, '.js');
        const portalHostRoot = relative.replaceAll('\\', '/').startsWith('designer/')
            ? path.join(root, 'src/ETL-SQL.Portal/wwwroot')
            : path.join(root, 'src/ETL-SQL.Portal/wwwroot/js');
        const hostPath = key(path.join(portalHostRoot, relative)).toLowerCase();
        if (moduleOwners.has(hostPath)) throw new Error(`Portal source collides with a shared host asset: ${hostPath}`);
        moduleOwners.set(hostPath, input);
        moduleOwnersExact.set(key(path.join(portalHostRoot, relative)), input);
    }
    const previous = assetInputs.readOwnership(root);
    const orphans = previous.version === 2 ? previous.outputs.filter(owner => owner.kind === 'typescript' &&
        !outputOwners.has(key(path.join(root, owner.path)).toLowerCase())).map(owner => path.join(root, owner.path)) : [];
    const obsoleteModules = new Set(previous.outputs.filter(owner => owner.kind === 'typescript' &&
        !outputOwners.has(key(path.join(root, owner.path)).toLowerCase())).map(owner => key(path.join(root, owner.path)).toLowerCase()));
    for (const owner of previous.outputs) {
        if (owner.kind === 'host-copy' && (obsoleteModules.has(key(path.join(root, owner.source)).toLowerCase()) ||
            !fs.existsSync(path.join(root, owner.source)))) obsoleteModules.add(key(path.join(root, owner.path)).toLowerCase());
    }
    if (check && !plan && orphans.length) throw new Error(`Orphaned TypeScript output: ${orphans.join(', ')}\nRun node scripts/sync-assets.js`);
    if (!ownership.size) {
        if (!plan) assetInputs.prepareOutputs(root, [], { check, partial: true });
        return { outputs: 0, removed: orphans.length, generatedFiles: [], owners: [], plannedOutputs: [], orphans };
    }

    const config = ts.readConfigFile(path.join(root, 'tsconfig.browser-build.json'), ts.sys.readFile);
    if (config.error) throw new Error(ts.flattenDiagnosticMessageText(config.error.messageText, '\n'));
    const converted = ts.convertCompilerOptionsFromJson(config.config.compilerOptions, root);
    if (converted.errors.length) throw new Error('Invalid browser compiler options');
    const expected = new Map();
    const declarations = files(path.join(root, 'types')).filter(p => p.endsWith('.d.ts'));
    for (const group of groups) {
        if (!group.inputs.length) continue;
        const options = { ...converted.options, rootDirs: [group.sourceRoot, group.outputRoot],
            rootDir: root, outDir: path.join(root, 'artifacts/browser-emit') };
        const host = ts.createCompilerHost(options);
        const exists = host.fileExists.bind(host);
        // Hide generated implementations so rootDirs resolves their TypeScript owners instead.
        host.fileExists = p => !outputOwners.has(key(p).toLowerCase()) && !obsoleteModules.has(key(p).toLowerCase()) && exists(p);
        host.resolveModuleNames = (names, containingFile) => names.map(name => {
            // Portal URLs name shared host copies. Resolve those imports to the original typed
            // owner, including on a fresh checkout before any host copy has been generated.
            const deliveredFile = ownership.get(key(containingFile)) ?? containingFile;
            const importedPath = name.startsWith('.') ? key(path.resolve(path.dirname(deliveredFile), name)) : undefined;
            const imported = importedPath ? moduleOwnersExact.get(importedPath) : undefined;
            if (!imported && importedPath && moduleOwners.has(importedPath.toLowerCase())) {
                throw new Error(`Browser import casing does not match its owner: ${name} in ${containingFile}`);
            }
            if (imported) return { resolvedFileName: imported, extension: ts.Extension.Ts };
            // A Portal import can reach a shared owner whose remaining vendor/JavaScript
            // dependencies live under Shared, outside the Portal rootDirs pair.
            const dependencyGroup = groups.find(g => [g.sourceRoot, g.outputRoot]
                .some(dir => key(containingFile).startsWith(key(dir) + '/')));
            const dependencyOptions = dependencyGroup ? { ...options,
                rootDirs: [dependencyGroup.sourceRoot, dependencyGroup.outputRoot] } : options;
            return ts.resolveModuleName(name, containingFile, dependencyOptions, host).resolvedModule;
        });
        const program = ts.createProgram([...group.inputs, ...declarations], options, host);
        const diagnostics = ts.getPreEmitDiagnostics(program);
        if (diagnostics.length) throw new Error(ts.formatDiagnosticsWithColorAndContext(diagnostics, {
            getCurrentDirectory: () => root, getCanonicalFileName: p => p, getNewLine: () => '\n',
        }));
        const result = program.emit(undefined, (_file, content, _bom, _error, sources) => {
            const source = sources?.find(s => ownership.has(key(s.fileName)));
            if (!source) return; // allowJs dependencies are inputs, never outputs we own.
            const target = ownership.get(key(source.fileName));
            const relative = path.relative(root, source.fileName).replaceAll('\\', '/');
            expected.set(target, `${marker}\n * Source: ${relative}\n * Run: node scripts/sync-assets.js\n */\n// @ts-nocheck — generated implementation; check the TypeScript owner.\n${content.replaceAll('\r\n', '\n')}`);
        });
        if (result.emitSkipped) throw new Error('Browser TypeScript emit failed');
    }
    if (expected.size !== ownership.size) throw new Error('Browser compiler did not emit every owned module');
    const relative = file => path.relative(root, file).replaceAll('\\', '/');
    const plannedOutputs = [...ownership].map(([source, output]) => ({
        path: relative(output), kind: 'typescript', source: relative(source), content: expected.get(output),
        // The checked-in v0.20 outputs precede the generated-implementation pragma. Compare the
        // complete fresh emit in that exact earlier format; no source/body difference is accepted.
        bootstrapContents: [expected.get(output).replace('*/\n// @ts-nocheck — generated implementation; check the TypeScript owner.\n', '*/\n')],
    }));
    // Synchronization requests an in-memory emit and preflights it with the bundle and host copies.
    // Standalone compilation uses the same hash-based replacement and cleanup rules.
    if (!plan) assetInputs.prepareOutputs(root, plannedOutputs, { check, partial: true });
    return { outputs: expected.size, removed: orphans.length, generatedFiles: [...expected.keys()],
        owners: [...ownership].map(([source, output]) => ({ source, output })), plannedOutputs, orphans };
}

export async function watchBrowser({ root = repo, toolchain = repo, intervalMs = 500, onResult = () => {} } = {}) {
    toolchainTools.loadBrowserCompiler(toolchain);
    const { default: synchronizer } = await import('./sync-assets.js');
    const inputSignature = () => assetInputs.browserInputSignature(root);
    const outputSignature = () => surfaces.flatMap(([, output]) => files(path.join(root, output)))
        .sort().map(file => `${file}:${fs.existsSync(file) ? fs.statSync(file).mtimeMs : 'missing'}`).join('|');
    const prepare = async () => {
        try {
            // The CLI is still evaluating this module while starting its watcher. Passing its
            // compiler avoids dynamically importing the unfinished entry module back into sync.
            const result = await synchronizer.syncAssets({ root, toolchain, quiet: true, compiler: compileBrowser });
            onResult({ result });
        } catch (error) { onResult({ error }); }
    };
    let previousInput = inputSignature();
    await prepare();
    let previousOutput = outputSignature();
    let running = null;
    const timer = setInterval(() => {
        if (running) return;
        const nextInput = inputSignature(), nextOutput = outputSignature();
        if (nextInput === previousInput && nextOutput === previousOutput) return;
        previousInput = nextInput;
        running = prepare().then(() => { previousOutput = outputSignature(); }).finally(() => { running = null; });
    }, intervalMs);
    return { async dispose() { clearInterval(timer); await running; } };
}

if (process.argv[1] && key(process.argv[1]) === key(fileURLToPath(import.meta.url))) {
    const run = () => {
        try {
            const result = compileBrowser({ check: process.argv.includes('--check') });
            console.log(`browser compilation: ${result.outputs} module(s) ${process.argv.includes('--check') ? 'verified' : 'generated'}`);
            return true;
        } catch (error) { console.error(error.message); return false; }
    };
    if (process.argv.includes('--watch')) {
        let watcher;
        let stopping = false;
        const stop = async () => {
            stopping = true;
            await watcher?.dispose();
        };
        process.once('SIGINT', stop);
        process.once('SIGTERM', stop);
        watcher = await watchBrowser({ onResult: ({ result, error }) => {
            if (error) console.error(error.message);
            else console.log(`browser assets: ${result.generatedAssets.length} output(s) prepared — reload the sandbox or host`);
        } });
        if (stopping) await watcher.dispose();
    } else if (!run()) process.exitCode = 1;
}
