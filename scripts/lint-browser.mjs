/**
 * The browser-side lint gate.
 *
 * Runs ESLint over the canonical browser sources (see eslint.config.mjs at the repository root)
 * and compares the result against a checked-in baseline, exactly as `typecheck-browser.mjs` does
 * for the type gate. It fails on two things, and only these two:
 *
 *   - a finding that is not in the baseline — new code, or an old file edited into a new defect.
 *     This is the point of the gate.
 *   - a baseline entry that no longer reproduces — the finding was fixed and the entry is stale.
 *     Stale entries are how a baseline quietly stops meaning anything, so removing them is part of
 *     fixing a finding, not a chore for later. `--update` does it for you.
 *
 * The baseline is empty, and that is the point: every one of the 115 findings the sources carried
 * when the gate went in has been worked off, so any finding at all is a new one. The mechanism
 * stays because it is what will absorb the next large import without the gate having to be
 * switched off to land it.
 *
 * A finding is identified by file, rule and message — deliberately not by line number, so that
 * editing a file above a known finding does not present it as new.
 *
 * A parse error is never baselineable. A file ESLint cannot parse is a file no rule ran against,
 * so recording it would turn one finding into a silent hole the size of the file.
 *
 * Usage:
 *   node scripts/lint-browser.mjs              check (this is what the pre-push gate runs)
 *   node scripts/lint-browser.mjs --update     rewrite the baseline from the current state
 *   node scripts/lint-browser.mjs --summary    print what is left, by file and by rule
 */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
const baselinePath = path.join(repoRoot, 'browser-lint-baseline.txt');
const eslint = path.join(scriptRoot, 'lint', 'node_modules', 'eslint', 'bin', 'eslint.js');

/**
 * The roots eslint.config.mjs describes. Passed as directories rather than globs because
 * `execFileSync` runs no shell: an unquoted glob would be expanded by POSIX sh on Linux CI and
 * left literal by cmd on Windows, which is how the VS Code extension's config once passed
 * locally and failed CI.
 */
const roots = [
    'src/ETL-SQL.ReportRuntime/Resources/Shared',
    'src/ETL-SQL.Portal/wwwroot/js',
];

const update = process.argv.includes('--update');
const summary = process.argv.includes('--summary');

if (!fs.existsSync(eslint)) {
    console.error('The lint-gate toolchain is not installed.');
    console.error('Run:  npm ci --prefix scripts/lint');
    process.exit(1);
}

let output = '';
try {
    output = execFileSync(
        process.execPath,
        [eslint, '--no-config-lookup', '--config', path.join(repoRoot, 'eslint.config.mjs'), '--format', 'json', ...roots],
        { cwd: repoRoot, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024 },
    );
} catch (error) {
    // ESLint exits 1 when it reports anything, which is the normal case here. It exits 2 when it
    // could not run at all, and then stdout is not JSON.
    output = `${error.stdout ?? ''}`;
    if (!output.trim().startsWith('[')) {
        console.error('ESLint could not run:');
        console.error(`${error.stderr ?? ''}`.trim() || error.message);
        process.exit(1);
    }
}

/** @type {Array<{filePath: string, messages: Array<{ruleId: string | null, message: string, line: number, fatal?: boolean}>}>} */
let results;
try {
    results = JSON.parse(output);
} catch {
    console.error('ESLint produced output that is not JSON. Is the toolchain installed correctly?');
    console.error(output.slice(0, 2000));
    process.exit(1);
}

/** @type {Array<{file: string, rule: string, message: string, line: number, fatal: boolean}>} */
const findings = [];
for (const result of results) {
    const file = path.relative(repoRoot, result.filePath).replace(/\\/g, '/');
    for (const m of result.messages) {
        findings.push({
            file,
            // A null `ruleId` is not always a parse error: ESLint also reports an
            // `eslint-disable` directive that no longer suppresses anything that way, and that
            // finding is ordinary — it means a rule was fixed and the comment outlived it.
            rule: m.ruleId ?? (m.fatal ? 'parse-error' : 'eslint-directive'),
            message: m.message,
            line: m.line ?? 0,
            fatal: Boolean(m.fatal),
        });
    }
}

const fatal = findings.filter(f => f.fatal);
if (fatal.length && !summary) {
    console.error(`\nBrowser lint gate: ${fatal.length} file(s) ESLint could not parse.\n`);
    for (const f of fatal) console.error(`  ${f.file}:${f.line}\n      ${f.message}\n`);
    console.error('A file that does not parse is a file no rule ran against, so this is never');
    console.error('baselined. Fix the syntax — or, if the file is a module being parsed as a');
    console.error('script, move it between the two blocks in eslint.config.mjs.');
    process.exit(1);
}

/** Line numbers move; the finding is the same one. Identity is file + rule + message. */
const key = f => `${f.file}\t${f.rule}\t${f.message}`;

if (summary) {
    const byFile = new Map();
    const byRule = new Map();
    for (const f of findings) {
        byFile.set(f.file, (byFile.get(f.file) ?? 0) + 1);
        byRule.set(f.rule, (byRule.get(f.rule) ?? 0) + 1);
    }
    const rank = m => [...m.entries()].sort((a, b) => b[1] - a[1]);
    console.log(`${findings.length} finding(s)\n`);
    console.log('By file:');
    for (const [file, count] of rank(byFile)) console.log(`  ${String(count).padStart(4)}  ${file}`);
    console.log('\nBy rule:');
    for (const [rule, count] of rank(byRule)) console.log(`  ${String(count).padStart(4)}  ${rule}`);
    process.exit(0);
}

const header = `# Browser lint-gate baseline — findings that already existed when the gate went in.
#
# Generated by: node scripts/lint-browser.mjs --update
#
# One line per finding: <count> TAB <file> TAB <rule> TAB <message>. No line numbers, so editing a
# file above a known finding does not make it look new. Repeated identical findings in one file
# appear once with a count.
#
# This file may shrink and must never grow. Adding a line to it by hand is how a gate stops being
# one; fix the finding, then run --update to drop its entry.
`;

function serialize(list) {
    const counts = new Map();
    for (const f of list) counts.set(key(f), (counts.get(key(f)) ?? 0) + 1);
    return header + [...counts.entries()]
        .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
        .map(([k, count]) => `${count}\t${k}`)
        .join('\n') + '\n';
}

if (update) {
    fs.writeFileSync(baselinePath, serialize(findings));
    console.log(`Baseline updated: ${findings.length} finding(s) recorded in browser-lint-baseline.txt`);
    process.exit(0);
}

if (!fs.existsSync(baselinePath)) {
    console.error('browser-lint-baseline.txt is missing. Create it with:');
    console.error('  node scripts/lint-browser.mjs --update');
    process.exit(1);
}

/** @type {Map<string, number>} */
const baseline = new Map();
for (const raw of fs.readFileSync(baselinePath, 'utf8').split(/\r?\n/)) {
    if (!raw.trim() || raw.startsWith('#')) continue;
    const tab = raw.indexOf('\t');
    baseline.set(raw.slice(tab + 1), Number(raw.slice(0, tab)));
}

/** @type {Map<string, number>} */
const current = new Map();
for (const f of findings) current.set(key(f), (current.get(key(f)) ?? 0) + 1);

const added = [];
for (const [k, count] of current) {
    const allowed = baseline.get(k) ?? 0;
    if (count > allowed) added.push({ k, count, allowed });
}
const stale = [];
for (const [k, allowed] of baseline) {
    const count = current.get(k) ?? 0;
    if (count < allowed) stale.push({ k, count, allowed });
}

const describe = k => {
    const [file, rule, message] = k.split('\t');
    return `${file}\n      ${rule}: ${message}`;
};

if (added.length) {
    console.error(`\nBrowser lint gate: ${added.length} new finding(s).\n`);
    for (const { k, count, allowed } of added) {
        console.error(`  [${count > allowed + 1 || allowed > 0 ? `${allowed} -> ${count}` : 'new'}] ${describe(k)}\n`);
    }
    console.error('Fix them, or — if the finding is genuinely not fixable here — disable the rule');
    console.error('on the line with a comment saying why. `--update` is for absorbing a large');
    console.error('import, not for making one finding go away.');
}

if (stale.length) {
    console.error(`\nBrowser lint gate: ${stale.length} baseline entr(y/ies) no longer reproduce.\n`);
    for (const { k, count, allowed } of stale) {
        console.error(`  [${allowed} -> ${count}] ${describe(k)}\n`);
    }
    console.error('Good news — these are fixed. Run `node scripts/lint-browser.mjs --update`');
    console.error('so the baseline stops claiming them.');
}

if (added.length || stale.length) process.exit(1);

console.log(findings.length === 0
    ? 'browser lint gate: clean — 0 findings'
    : `browser lint gate: ${findings.length} known finding(s), no new ones`);
