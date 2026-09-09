import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const marker = '/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.';
const surfaces = [
    ['src/ETL-SQL.ReportRuntime/Resources/TypeScript', 'src/ETL-SQL.ReportRuntime/Resources/Shared'],
    ['src/ETL-SQL.Portal/BrowserSources', 'src/ETL-SQL.Portal/wwwroot/js'],
];
function files(dir) {
    if (!fs.existsSync(dir)) return [];
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
        const p = path.join(dir, e.name);
        if (e.isSymbolicLink()) throw new Error(`Browser source symlink is unsupported: ${p}`);
        return e.isDirectory() ? files(p) : [p];
    });
}
const key = p => path.resolve(p).replaceAll('\\', '/');

// root/toolchain parameters let regression tests exercise the real compiler in an isolated tree.
export function compileBrowser({ root = repo, check = false, toolchain = repo } = {}) {
    const ownership = new Map();
    const outputOwners = new Map();
    const groups = surfaces.map(([source, output]) => {
        const sourceRoot = path.join(root, source), outputRoot = path.join(root, output);
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
    const orphans = groups.flatMap(g => files(g.outputRoot)).filter(p =>
        p.endsWith('.js') && fs.readFileSync(p, 'utf8').startsWith(marker) &&
        !outputOwners.has(key(p).toLowerCase()));
    if (orphans.length) throw new Error(`Orphaned TypeScript output; remove the obsolete output and sync: ${orphans.join(', ')}`);
    if (!ownership.size) return { outputs: 0 };

    const require = createRequire(import.meta.url);
    const compilerPath = path.join(toolchain, 'scripts/typecheck/node_modules/typescript');
    if (!fs.existsSync(compilerPath)) throw new Error('Browser compiler missing. Run npm ci --prefix scripts/typecheck');
    const ts = require(compilerPath);
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
        host.fileExists = p => !outputOwners.has(key(p).toLowerCase()) && exists(p);
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
            expected.set(target, `${marker}\n * Source: ${relative}\n * Run: node scripts/sync-assets.js\n */\n${content.replaceAll('\r\n', '\n')}`);
        });
        if (result.emitSkipped) throw new Error('Browser TypeScript emit failed');
    }
    if (expected.size !== ownership.size) throw new Error('Browser compiler did not emit every owned module');
    const drift = [...expected].filter(([p, content]) => !fs.existsSync(p) || fs.readFileSync(p, 'utf8') !== content);
    if (check && drift.length) throw new Error(`TypeScript output missing or stale: ${drift.map(([p]) => p).join(', ')}\nRun node scripts/sync-assets.js`);
    if (!check) for (const [p, content] of drift) {
        fs.mkdirSync(path.dirname(p), { recursive: true });
        fs.writeFileSync(p, content);
    }
    return { outputs: expected.size };
}

if (process.argv[1] && key(process.argv[1]) === key(fileURLToPath(import.meta.url))) {
    const run = () => {
        try {
            const result = compileBrowser({ check: process.argv.includes('--check') });
            console.log(`browser compilation: ${result.outputs} module(s) ${process.argv.includes('--check') ? 'verified' : 'generated'}`);
            return true;
        } catch (error) { console.error(error.message); return false; }
    };
    if (!run() && !process.argv.includes('--watch')) process.exitCode = 1;
    if (process.argv.includes('--watch')) {
        // Poll source signatures; newly created files are included without restarting the watcher.
        const signature = () => surfaces.flatMap(([s, o]) => [...files(path.join(repo, s)), ...files(path.join(repo, o))])
            .concat(files(path.join(repo, 'types')))
            .concat(path.join(repo, 'tsconfig.browser-build.json'))
            .map(p => `${p}:${fs.statSync(p).mtimeMs}`).join('|');
        let previous = signature();
        setInterval(() => { const next = signature(); if (next !== previous) { previous = next; run(); } }, 500);
    }
}
