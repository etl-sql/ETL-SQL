import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ts = require('./typecheck/node_modules/typescript');
const designer = path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer');
const palette = path.join(designer, 'studio-pipeline-canvas.ts');
const declarations = path.join(repo, 'types/etlsql-contracts.generated.d.ts');
const fixture = path.join(repo, 'types/studio-contract-probe.ts');
const config = ts.readConfigFile(path.join(repo, 'tsconfig.browser-build.json'), ts.sys.readFile);
assert.equal(config.error, undefined);
const converted = ts.convertCompilerOptionsFromJson(config.config.compilerOptions, repo);
assert.equal(converted.errors.length, 0);
const options = { ...converted.options, noEmit: true };
const normalize = value => path.resolve(value).replaceAll('\\', '/');
function diagnostics(overrides = new Map()) {
    const host = ts.createCompilerHost(options);
    const read = host.readFile;
    host.readFile = name => overrides.get(normalize(name)) ?? read(name);
    const program = ts.createProgram([palette, declarations, ...(overrides.has(normalize(fixture)) ? [fixture] : [])], options, host);
    return ts.getPreEmitDiagnostics(program);
}
assert.deepEqual(diagnostics(), [], 'The real palette must compile against generated server kinds.');
const paletteText = fs.readFileSync(palette, 'utf8');
assert.ok(paletteText.includes("id: 'execution'"));
for (const replacement of ["id: 'not_a_server_kind'", "id: 'waitfor'"]) {
    const errors = diagnostics(new Map([[normalize(palette), paletteText.replace("id: 'execution'", replacement)]]));
    assert.ok(errors.some(error => normalize(error.file?.fileName || '') === normalize(palette)),
        'Unknown kinds and omission of an existing kind must both fail compilation.');
}
const vocabulary = fs.readFileSync(declarations, 'utf8');
const expanded = vocabulary.replace('type PipelineTaskKind =', "type PipelineTaskKind = 'future_server_kind'");
assert.notEqual(expanded, vocabulary);
assert.ok(diagnostics(new Map([[normalize(declarations), expanded]]))
    .some(error => error.code === 2344 && normalize(error.file?.fileName || '') === normalize(palette)),
    'Adding a server kind must require a palette entry.');
const probe = "import { STUDIO_ROUTES } from '../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-contracts.js';\nvoid STUDIO_ROUTES.notARegisteredRoute;\n";
assert.ok(diagnostics(new Map([[normalize(fixture), probe]])).some(error => error.code === 2339),
    'The generated route re-export must reject unknown route keys.');

// This architectural constraint is not a type constraint: a raw fetch URL is still a valid string.
for (const name of fs.readdirSync(designer).filter(name => /^studio(?:-|\.)/.test(name) && name.endsWith('.ts') && name !== 'studio-routes.generated.ts')) {
    const source = ts.createSourceFile(name, fs.readFileSync(path.join(designer, name), 'utf8'), ts.ScriptTarget.Latest, true);
    const visit = node => {
        if (ts.isStringLiteralLike(node) || ts.isTemplateHead(node)) {
            assert.ok(!node.text.startsWith('/api/'), `${name} bypasses the generated route table: ${node.text}`);
        }
        ts.forEachChild(node, visit);
    };
    visit(source);
}
console.log('Studio generated contracts: unknown routes, missing/invalid/new kinds, and route-table ownership checked.');
