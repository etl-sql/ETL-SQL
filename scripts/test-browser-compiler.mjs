import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { compileBrowser } from './compile-browser.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-browser-compiler-'));
const source = path.join(root, 'src/ETL-SQL.ReportRuntime/Resources/TypeScript');
const output = path.join(root, 'src/ETL-SQL.ReportRuntime/Resources/Shared');
const write = (p, s) => { fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, s); };
const compile = check => compileBrowser({ root, toolchain: repo, check });
try {
    fs.copyFileSync(path.join(repo, 'tsconfig.browser-build.json'), path.join(root, 'tsconfig.browser-build.json'));
    write(path.join(source, 'leaf.ts'), 'export const amount: number = 7;\n');
    write(path.join(output, 'legacy.js'), "import { amount } from './leaf.js';\nexport function twice() { return amount * 2; }\n");
    write(path.join(source, 'entry.ts'), "import { twice } from './legacy.js';\nexport const result: number = twice();\n");
    write(path.join(source, 'nested/consumer.ts'), "import { amount } from '../leaf.js';\nexport const nested: number = amount;\n");
    assert.throws(() => compile(true), /missing or stale/);
    compile(false);
    const leaf = path.join(output, 'leaf.js');
    const expected = fs.readFileSync(leaf, 'utf8');
    const legacy = fs.readFileSync(path.join(output, 'legacy.js'), 'utf8');
    compile(true);
    assert.match(fs.readFileSync(path.join(output, 'nested/consumer.js'), 'utf8'), /from '\.\.\/leaf\.js'/);
    compile(false);
    assert.equal(fs.readFileSync(leaf, 'utf8'), expected);
    assert.equal(fs.readFileSync(path.join(output, 'legacy.js'), 'utf8'), legacy);
    // Generated output must not shadow the source when checking migrated imports.
    write(leaf, expected + '\ninvalid generated syntax !!!');
    assert.throws(() => compile(true), /missing or stale/);
    compile(false);
    // An invalid source must not write even other valid changed modules.
    write(path.join(source, 'leaf.ts'), 'export const amount: number = 8;\n');
    write(path.join(source, 'entry.ts'), "import { amount } from './leaf.js';\nexport const result: string = amount;\n");
    assert.throws(() => compile(false), /not assignable/);
    assert.equal(fs.readFileSync(leaf, 'utf8'), expected);
    write(path.join(source, 'entry.ts'), 'export enum Unsupported { One }\n');
    assert.throws(() => compile(false), /erasableSyntaxOnly/);
    assert.equal(fs.readFileSync(leaf, 'utf8'), expected);
    fs.rmSync(path.join(source, 'leaf.ts'));
    assert.throws(() => compile(true), /Orphaned/);
    assert.throws(() => compile(false), /Orphaned/);
    console.log('browser compiler: mixed imports, drift, idempotence, orphan and failed-compile checks passed');
} finally { fs.rmSync(root, { recursive: true, force: true }); }

const utilPath = path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js');
const util = await import(`data:text/javascript;base64,${Buffer.from(fs.readFileSync(utilPath, 'utf8')).toString('base64')}`);
assert.equal(util.safeUrl(' javascript:alert(1) '), '#');
assert.equal(util.safeUrl('/reports/one'), '/reports/one');
assert.equal(util.escHtml('<>&"\''), '&lt;&gt;&amp;&quot;&#39;');
assert.equal(util.escHtml(null), '');
assert.equal(util.interpolateUrlTemplate({ url: '/?q={Name}&x={Other}', urlParams: ['name'] }, ['a&b'], ['NAME']), '/?q=a%26b&x=');
assert.deepEqual(util.parseMultiParameter('["a",2]'), ['a', '2']);
assert.deepEqual(util.parseMultiParameter(' a, ,b '), ['a', 'b']);
assert.equal(util.getOption({ TiTlE: 'hello' }, 'TITLE'), 'hello');
assert.equal(util.formatValue(12.5, 'N1'), '12.5');
assert.equal(util.formatValue('12.5', 'N1'), '12.5');
assert.equal(util.formatValue('bad', 'N1'), 'bad');
assert.equal(util.toCssLength(12), '12px');
assert.equal(util.interpolateColor('#000', '#fff', 0.5), 'rgb(128,128,128)');
assert.match(util.simpleMarkdown('# Heading\n\n```sql\n<x>\n```'), /&lt;x&gt;/);
assert.equal(util.renderInlineMarkdown('<img> **safe**'), '&lt;img&gt; <strong>safe</strong>');
console.log('runtime utility pilot: coercion, URL, escaping, formatting and markdown checks passed');

const designerPath = path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-util.js');
const designer = await import(`data:text/javascript;base64,${Buffer.from(fs.readFileSync(designerPath, 'utf8')).toString('base64')}`);
assert.equal(designer.escapeHtml('<>&"\''), '&lt;&gt;&amp;&quot;\'');
assert.equal(designer.esc('<>&"\''), '&lt;>&amp;&quot;\'');
for (const escape of [designer.escapeHtml, designer.esc]) {
    assert.equal(escape(null), '');
    assert.equal(escape(undefined), '');
    assert.equal(escape(0), '0');
    assert.equal(escape(false), 'false');
}
console.log('designer utility: existing escaping and coercion behavior preserved');
