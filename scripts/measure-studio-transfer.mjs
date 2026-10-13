import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';
import { createRequire } from 'node:module';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ts = require('./typecheck/node_modules/typescript');
export function resolveStudioAsset(url, assetRoot = root) {
    const resolved = new URL(url, 'https://studio.invalid').pathname;
    const shared = path.join(assetRoot, 'src/ETL-SQL.ReportRuntime/Resources/Shared');
    if (resolved.startsWith('/designer/')) return path.join(shared, resolved.slice(1));
    if (resolved.startsWith('/runtime/')) return path.join(shared, resolved.slice('/runtime/'.length));
    return path.join(assetRoot, 'src/ETL-SQL.Portal/wwwroot', resolved.slice(1));
}

// Use the pinned compiler's AST: comments, quoted examples, and retry URL strings aren't imports.
function moduleImports(source, filename) {
    const result = [];
    const tree = ts.createSourceFile(filename, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS);
    function visit(node) {
        if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier
            && ts.isStringLiteralLike(node.moduleSpecifier)) result.push(node.moduleSpecifier.text);
        if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
            const argument = node.arguments[0];
            if (argument && ts.isStringLiteralLike(argument)) result.push(argument.text);
            // Nonliteral imports must be reviewed. CodeMirror retry is the same measured bundle.
            else {
                const value = argument && ts.isPropertyAccessExpression(argument) && argument.name.text === 'href'
                    ? argument.expression : null;
                const template = value && ts.isNewExpression(value) && value.expression.getText(tree) === 'URL'
                    ? value.arguments?.[0] : null;
                if (!template || !ts.isTemplateExpression(template)
                    || template.head.text !== './codemirror/codemirror-bundle.min.js?retry='
                    || template.templateSpans.length !== 1 || template.templateSpans[0].literal.text !== '') {
                    throw new Error(`Uninventoried dynamic import in ${filename}: ${node.getText(tree)}`);
                }
                result.push('./codemirror/codemirror-bundle.min.js');
            }
        }
        ts.forEachChild(node, visit);
    }
    visit(tree);
    return result;
}

export function measureStudioTransfer(assetRoot = root) {
    const hosts = {};
    for (const [host, filename] of Object.entries({
        portal: 'src/ETL-SQL.Portal/wwwroot/studio.html',
        desktop: 'src/ETL-SQL.WorkstationEditor/StudioShell.cs'
    })) {
        const shell = fs.readFileSync(path.join(assetRoot, filename), 'utf8');
        const pending = [...shell.matchAll(/(?:src|href)="(\/[^"?]+)(?:\?[^\"]*)?"/g)].map(match => match[1]);
        for (const block of shell.matchAll(/<script type="module">([\s\S]*?)<\/script>/g)) {
            for (const specifier of moduleImports(block[1], filename)) {
                if (specifier.startsWith('/')) pending.push(specifier.split('?')[0]);
            }
        }
        const seen = new Set();
        const assets = [];
        while (pending.length) {
            const url = new URL(pending.pop(), 'https://studio.invalid').pathname;
            const file = resolveStudioAsset(url, assetRoot);
            if (seen.has(file)) continue;
            seen.add(file);
            const bytes = fs.readFileSync(file);
            assets.push({ url, rawBytes: bytes.length, gzipBytes: gzipSync(bytes, { level: 9 }).length });
            if (file.endsWith('.js')) {
                for (const specifier of moduleImports(bytes.toString('utf8'), url)) {
                    if (!specifier.startsWith('.') && !specifier.startsWith('/')) throw new Error(`Unresolved import ${specifier} in ${url}`);
                    pending.push(new URL(specifier, `https://studio.invalid${url}`).pathname);
                }
            }
            if (file.endsWith('.css')) {
                for (const match of bytes.toString('utf8').matchAll(/(?:url\(\s*['"]?([^)'"\s]+)|@import\s+['"]([^'"]+))/g)) {
                    const target = match[1] || match[2];
                    if (target.startsWith('data:')) continue;
                    if (/^https?:/.test(target)) throw new Error(`External Studio asset must be inventoried: ${target}`);
                    pending.push(new URL(target, `https://studio.invalid${url}`).pathname);
                }
            }
        }
        assets.sort((left, right) => left.url.localeCompare(right.url));
        hosts[host] = {
            rawBytes: assets.reduce((sum, asset) => sum + asset.rawBytes, 0),
            gzipBytes: assets.reduce((sum, asset) => sum + asset.gzipBytes, 0), assets
        };
    }
    return { schemaVersion: 1, compression: 'gzip level 9, each unique response independently', hosts };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const measurement = measureStudioTransfer();
    const output = process.argv[2];
    if (output) fs.writeFileSync(path.resolve(root, output), `${JSON.stringify(measurement, null, 2)}\n`);
    for (const [host, result] of Object.entries(measurement.hosts)) {
        console.log(`${host}: ${result.assets.length} assets; ${result.rawBytes} raw bytes; ${result.gzipBytes} gzip bytes`);
    }
}
