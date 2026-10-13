// CodeQL reads its exclusions from .github/workflows/codeql.yml. Generated JavaScript is excluded
// there so findings land on the TypeScript that produced it; this check makes sure the rules hide
// every generated script and never an authored owner. An exclusion wide enough to cover a source
// project once left the whole shared runtime and designer unanalysed without failing anything.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { browserFiles, readOwnership } = require('../lib/browser-asset-inputs.cjs');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workflow = fs.readFileSync(path.join(root, '.github/workflows/codeql.yml'), 'utf8');

const section = /paths-ignore:\r?\n((?:\s+(?:#.*|- .*)\r?\n)+)/.exec(workflow);
assert.ok(section, 'codeql.yml must declare paths-ignore.');
const rules = [...section[1].matchAll(/^\s+- '([^']+)'/gm)].map(match => match[1]);
assert.ok(rules.length > 0, 'codeql.yml paths-ignore has no readable rules.');

const toRegExp = rule => new RegExp('^' + rule.split('**/').map(part => part.split('**').map(piece =>
    piece.split('*').map(text => text.replace(/[.+^${}()|[\]\\?]/g, '\\$&')).join('[^/]*')).join('.*')).join('(?:.*/)?') + '$');
const matchers = rules.map(rule => ({ rule, pattern: toRegExp(rule) }));
const ignoredBy = file => matchers.find(({ pattern }) => pattern.test(file))?.rule;

export function analysisProblems(owners, generatedScripts) {
    const problems = [];
    for (const owner of owners) {
        const rule = ignoredBy(owner);
        if (rule) problems.push(`CodeQL does not analyse an authored browser owner: ${owner} (excluded by '${rule}')`);
    }
    for (const output of generatedScripts)
        if (!ignoredBy(output)) problems.push(`CodeQL analyses a generated script instead of its owner: ${output}`);
    return problems;
}

const ownerRoots = ['src/ETL-SQL.ReportRuntime/Resources/TypeScript', 'src/ETL-SQL.Portal/BrowserSources'];
const owners = ownerRoots.flatMap(dir => browserFiles(path.join(root, dir)))
    .map(file => path.relative(root, file).replaceAll('\\', '/'))
    .filter(file => file.endsWith('.ts') && !file.endsWith('.d.ts'));
assert.ok(owners.length > 100, 'Expected the shared and Portal TypeScript owners to be discoverable.');
assert.ok(fs.existsSync(path.join(root, 'artifacts/browser-assets.json')),
    'Browser ownership inventory is missing. Run node scripts/sync-assets.js first.');
const generated = readOwnership(root).outputs.map(output => output.path).filter(file => file.endsWith('.js'));
assert.ok(generated.length > 300, 'Expected the generated script inventory to be populated.');

// The matcher itself: a project-wide rule must be seen to hide an owner.
assert.equal(toRegExp('**/src/ETL-SQL.ReportRuntime/**').test(owners.find(owner => owner.includes('ReportRuntime'))), true);
assert.equal(toRegExp('**/wwwroot/js/**').test('src/ETL-SQL.Portal/BrowserSources/pages/index.ts'), false);
assert.equal(toRegExp('**/*.bundle.js').test('src/x/report-runtime.bundle.js'), true);

const problems = analysisProblems(owners, generated);
assert.deepEqual(problems, [], problems.join('\n'));
console.log(`source analysis coverage: ${owners.length} owners analysed, ${generated.length} generated scripts excluded`);
