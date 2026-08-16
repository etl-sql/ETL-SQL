// Parses every inline <script> block in the Portal's pages, and every browser module they import.
//
// Why this exists: the Portal's pages carry most of their behaviour in one large inline module, and a
// single syntax error in it takes the whole page down — no jobs table, no chips, no detail panel,
// nothing. Nothing else in the suite ever parses that code. The extracted modules under wwwroot/js are
// covered by their own unit tests, the C# tests assert over HTTP, and the UI sandbox imports the pure
// modules directly, so an unescaped apostrophe inside a single-quoted string can ship green.
//
// A parse is all this does. It does not execute the module or touch the DOM: `node --check` on a .mjs
// file reports exactly the class of error that silently kills a page, and reports it in a second.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const ROOTS = [
    'src/ETL-SQL.Portal/wwwroot',
    'src/ETL-SQL.ReportPlayer/wwwroot',
];

const SCRIPT_BLOCK = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;

function htmlFiles(root) {
    if (!fs.existsSync(root)) return [];
    return fs.readdirSync(root, { withFileTypes: true })
        .filter(entry => entry.isFile() && entry.name.endsWith('.html'))
        .map(entry => path.join(root, entry.name));
}

/** Inline blocks only — a src= script is a file of its own and is checked as one. */
function inlineBlocks(html) {
    const blocks = [];
    for (const match of html.matchAll(SCRIPT_BLOCK)) {
        const attributes = match[1] || '';
        if (/\bsrc\s*=/i.test(attributes)) continue;
        const body = match[2] || '';
        if (!body.trim()) continue;
        blocks.push({
            isModule: /type\s*=\s*["']module["']/i.test(attributes),
            body,
            // The line the block starts on, so a failure points into the page rather than into a
            // temporary file the reader has never seen.
            line: html.slice(0, match.index).split('\n').length,
        });
    }
    return blocks;
}

const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-inline-'));
const failures = [];
let checked = 0;

function check(code, label, asModule) {
    // An inline classic script is not a module, but parsing it as one is stricter, not looser, and
    // every page here is written to module rules anyway.
    const file = path.join(temp, `check-${checked}.${asModule ? 'mjs' : 'mjs'}`);
    fs.writeFileSync(file, code);
    try {
        execFileSync(process.execPath, ['--check', file], { stdio: ['ignore', 'ignore', 'pipe'] });
    } catch (error) {
        const detail = (error.stderr?.toString() || error.message || '').trim();
        failures.push(`${label}\n${detail.split('\n').slice(0, 6).join('\n')}`);
    }
    checked++;
}

for (const root of ROOTS) {
    for (const file of htmlFiles(root)) {
        const html = fs.readFileSync(file, 'utf8');
        for (const block of inlineBlocks(html)) {
            check(block.body, `${file} (inline script starting at line ${block.line})`, block.isModule);
        }
    }
}

// The browser modules the pages import. Bundled third-party files are skipped: they are vendored
// build output, and a minified bundle's parse errors are not this suite's to find.
for (const root of ROOTS) {
    const jsDir = path.join(root, 'js');
    if (!fs.existsSync(jsDir)) continue;
    for (const entry of fs.readdirSync(jsDir)) {
        if (!entry.endsWith('.js') || entry.endsWith('.min.js')) continue;
        check(fs.readFileSync(path.join(jsDir, entry), 'utf8'), path.join(jsDir, entry), true);
    }
}

fs.rmSync(temp, { recursive: true, force: true });

if (checked === 0) {
    console.error('FAIL: no scripts were checked — the page layout must have moved.');
    process.exit(1);
}

if (failures.length > 0) {
    console.error(`FAIL: ${failures.length} script(s) do not parse:\n\n${failures.join('\n\n')}`);
    process.exit(1);
}

console.log(`portal inline script syntax: ${checked} script(s) parsed`);
