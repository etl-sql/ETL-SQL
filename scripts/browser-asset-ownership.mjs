#!/usr/bin/env node
/**
 * Reconciles the browser ownership inventory with the Git index. Read-only unless `--write-ignore`.
 *
 * Preparation (`node scripts/sync-assets.js`) records every generated output and its owner in
 * `artifacts/browser-assets.json`. This script answers the source-control questions that inventory
 * cannot answer on its own: which outputs would leave the index, which inputs must stay, whether a
 * consumer names an asset nobody owns, and whether the ignore rules still match the inventory.
 *
 * The layout is read from `.gitignore`. Without the managed block the outputs are expected to be
 * tracked; with it, none may be tracked and every one must be ignored. The cutover is therefore one
 * reviewable change: the block plus the index removals.
 *
 * Usage:
 *   node scripts/browser-asset-ownership.mjs                  # removal plan, human readable
 *   node scripts/browser-asset-ownership.mjs --json           # the same plan as JSON
 *   node scripts/browser-asset-ownership.mjs --check          # fail on any ownership problem
 *   node scripts/browser-asset-ownership.mjs --ignore-rules   # print the derived ignore block
 *   node scripts/browser-asset-ownership.mjs --write-ignore   # replace the block in .gitignore
 */
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { browserSourceFiles, browserPreparationFiles, readOwnership } = require('./lib/browser-asset-inputs.cjs');

const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const inventoryName = 'artifacts/browser-assets.json';
const regenerate = 'node scripts/sync-assets.js';
export const ignoreStart = '# >>> generated browser outputs: node scripts/browser-asset-ownership.mjs --write-ignore >>>';
export const ignoreEnd = '# <<< generated browser outputs <<<';

// Each page resolves a root-relative URL against the folder its host serves.
const webRoots = ['src/ETL-SQL.Portal/wwwroot/', 'src/ETL-SQL.ReportPlayer/wwwroot/',
    'src/ETL-SQL.WorkstationEditor/wwwroot/', 'src/etl-sql-vscode/media/'];
const assetRoots = ['src/ETL-SQL.ReportRuntime/Resources/Shared/', ...webRoots];
const assetProjects = ['Reporting', 'ReportRuntime', 'ReportPlayer', 'WorkstationEditor', 'Portal']
    .map(name => `src/ETL-SQL.${name}/ETL-SQL.${name}.csproj`);
const assetReference = /\.(?:js|mjs|css|geojson)$/i;

function git(root, args, input) {
    const result = spawnSync('git', args, { cwd: root, input, maxBuffer: 1 << 28 });
    if (result.error) throw new Error(`Git is required for the ownership check: ${result.error.message}`);
    return result;
}

function gitLines(root, args, input) {
    const result = git(root, args, input);
    if (result.status !== 0) throw new Error(`git ${args.join(' ')} failed: ${result.stderr.toString().trim()}`);
    return result.stdout.toString().split('\0').filter(Boolean);
}

/** Paths the current ignore rules match, whether or not the index still holds them. */
function ignoredPaths(root, paths) {
    if (!paths.length) return new Set();
    const result = git(root, ['check-ignore', '--no-index', '-z', '--stdin'], paths.join('\0'));
    // Exit 1 means "none ignored"; anything above that is a real failure.
    if (result.status > 1) throw new Error(`git check-ignore failed: ${result.stderr.toString().trim()}`);
    return new Set(result.stdout.toString().split('\0').filter(Boolean));
}

/** SHA-256 of each path's staged bytes, so unstaged edits cannot stand in for the proposed change. */
function indexHashes(root, paths) {
    const hashes = new Map();
    if (!paths.length) return hashes;
    const result = git(root, ['cat-file', '--batch'], paths.map(file => `:${file}`).join('\n') + '\n');
    if (result.status !== 0) throw new Error(`git cat-file failed: ${result.stderr.toString().trim()}`);
    const out = result.stdout;
    let offset = 0;
    for (const file of paths) {
        const end = out.indexOf(0x0a, offset);
        const header = out.subarray(offset, end).toString();
        offset = end + 1;
        if (header.endsWith(' missing')) continue;
        const size = Number(header.split(' ')[2]);
        hashes.set(file, createHash('sha256').update(out.subarray(offset, offset + size)).digest('hex'));
        offset += size + 1;
    }
    return hashes;
}

function escapeIgnorePath(file) {
    return '/' + file.replace(/[\\*?[\]!#]/g, '\\$&').replace(/ $/, '\\ ');
}

/** The exact ignore block for an inventory: one anchored rule per output, never a pattern. */
export function ignoreBlock(outputs) {
    return [ignoreStart, ...outputs.map(owner => escapeIgnorePath(owner.path)).sort(), ignoreEnd].join('\n') + '\n';
}

function readIgnoreBlock(root) {
    const file = path.join(root, '.gitignore');
    const text = fs.existsSync(file) ? fs.readFileSync(file, 'utf8').replaceAll('\r\n', '\n') : '';
    const start = text.indexOf(ignoreStart), end = text.indexOf(ignoreEnd);
    if ((start < 0) !== (end < 0) || end < start)
        throw new Error('Malformed generated-output block in .gitignore. Restore both marker lines.');
    return { text, block: start < 0 ? null : text.slice(start, end + ignoreEnd.length) + '\n', start, end };
}

function references(text) {
    const found = [];
    const patterns = [/<(?:script|link)\b[^>]*?\b(?:src|href)\s*=\s*["']([^"']+)["']/gi,
        /\bfrom\s*["']([^"']+)["']/g, /\bimport\s*\(\s*["']([^"']+)["']\s*\)/g, /\bimport\s*["']([^"']+)["']/g];
    for (const pattern of patterns) for (const match of text.matchAll(pattern)) found.push(match[1]);
    return found;
}

/** Assets named by pages and project files, which import-closure checks never see. */
function unresolvedConsumers(root, tracked, outputs) {
    const known = new Set([...tracked, ...outputs]);
    const problems = [];
    for (const page of tracked) {
        const webRoot = webRoots.find(prefix => page.startsWith(prefix));
        if (!webRoot || !page.endsWith('.html') || !fs.existsSync(path.join(root, page))) continue;
        for (const reference of references(fs.readFileSync(path.join(root, page), 'utf8'))) {
            const target = reference.split(/[?#]/)[0];
            if (!assetReference.test(target) || /^(?:[a-z]+:)?\/\//i.test(target) || /[{}$<>]/.test(target)) continue;
            const resolved = target.startsWith('/')
                ? webRoot + target.slice(1)
                : path.posix.normalize(path.posix.join(path.posix.dirname(page), target));
            if (!known.has(resolved))
                problems.push(`Consumer names an asset with no tracked input or generated owner: ${page} -> ${reference}`);
        }
    }
    for (const project of assetProjects) {
        if (!tracked.has(project) || !fs.existsSync(path.join(root, project))) continue;
        const xml = fs.readFileSync(path.join(root, project), 'utf8');
        for (const match of xml.matchAll(/<(?:EmbeddedResource|Content|None)\s+Include="([^"]+)"/g)) {
            const include = path.posix.normalize(path.posix.join(path.posix.dirname(project), match[1].replaceAll('\\', '/')));
            if (!assetRoots.some(prefix => include.startsWith(prefix))) continue;
            const pattern = new RegExp('^' + include.split('**/').map(part => part.split('*')
                .map(text => text.replace(/[.+^${}()|[\]\\?]/g, '\\$&')).join('[^/]*')).join('(?:.*/)?') + '$');
            if (![...known].some(file => pattern.test(file)))
                problems.push(`Project includes an asset with no tracked input or generated owner: ${project} -> ${match[1]}`);
        }
    }
    return problems;
}

/**
 * Builds the removal plan and every reason it cannot be applied as it stands.
 * `cutover` reports which layout `.gitignore` declares.
 */
export function ownershipPlan(root = defaultRoot) {
    root = path.resolve(root);
    if (!fs.existsSync(path.join(root, inventoryName)))
        throw new Error(`Browser ownership inventory is missing: ${inventoryName}. Run ${regenerate} first.`);
    const inventory = readOwnership(root);
    if (inventory.version !== 2)
        throw new Error(`Browser ownership inventory has no output hashes. Run ${regenerate} first.`);
    const relative = file => path.relative(root, file).replaceAll('\\', '/');
    const outputs = new Map(inventory.outputs.map(owner => [owner.path, owner]));
    const tracked = new Set(gitLines(root, ['ls-files', '-z']));
    const retained = [...new Set([...browserSourceFiles(root), ...browserPreparationFiles(root)].map(relative))]
        .filter(file => !outputs.has(file)).sort();
    const sources = [...new Set(inventory.outputs.flatMap(owner => [owner.source, ...(owner.inputs ?? [])]))]
        .filter(file => !outputs.has(file));
    const ignored = ignoredPaths(root, [...new Set([...outputs.keys(), ...retained, ...sources])]);
    const staged = indexHashes(root, [...outputs.keys()].filter(file => tracked.has(file)));
    const { block } = readIgnoreBlock(root);
    const cutover = block !== null;
    const problems = [];

    for (const file of new Set([...retained, ...sources])) {
        if (!tracked.has(file)) problems.push(`Required browser input is not tracked: ${file}`);
        if (ignored.has(file)) problems.push(`Required browser input is ignored: ${file}`);
    }
    const modified = [];
    for (const owner of inventory.outputs) {
        const isTracked = tracked.has(owner.path);
        if (isTracked && staged.get(owner.path) !== owner.sha256) modified.push(owner.path);
        if (cutover && isTracked) problems.push(`Generated browser output is tracked: ${owner.path} (owner: ${owner.source})`);
        if (cutover && !ignored.has(owner.path)) problems.push(`Generated browser output is not ignored: ${owner.path}`);
        if (!cutover && !isTracked) problems.push(`Generated browser output left the index before the cutover: ${owner.path}`);
        if (!cutover && ignored.has(owner.path)) problems.push(`Generated browser output is ignored before the cutover: ${owner.path}`);
    }
    for (const file of modified)
        problems.push(`Staged output differs from its prepared bytes: ${file}. Move the change into ${outputs.get(file).source}, then run ${regenerate}.`);
    if (cutover && block !== ignoreBlock(inventory.outputs))
        problems.push('Generated-output ignore rules are stale. Run node scripts/browser-asset-ownership.mjs --write-ignore.');
    problems.push(...unresolvedConsumers(root, tracked, new Set(outputs.keys())));

    return {
        cutover,
        removals: inventory.outputs.filter(owner => tracked.has(owner.path))
            .map(owner => ({ path: owner.path, kind: owner.kind, owner: owner.source, regenerate })),
        outputs: inventory.outputs.length,
        retained,
        modified,
        ignoreRules: ignoreBlock(inventory.outputs),
        problems,
    };
}

/** Replaces the managed block only; every other rule in `.gitignore` is left as written. */
export function writeIgnoreRules(root = defaultRoot) {
    root = path.resolve(root);
    const rules = ignoreBlock(readOwnership(root).outputs);
    const { text, block, start, end } = readIgnoreBlock(root);
    const next = block === null
        ? text + (text === '' || text.endsWith('\n') ? '' : '\n') + rules
        : text.slice(0, start) + rules + text.slice(end + ignoreEnd.length).replace(/^\n/, '');
    if (next !== text) fs.writeFileSync(path.join(root, '.gitignore'), next);
    return next !== text;
}

function report(plan) {
    const kinds = {};
    for (const removal of plan.removals) kinds[removal.kind] = (kinds[removal.kind] ?? 0) + 1;
    const lines = [
        `Layout: ${plan.cutover ? 'generated outputs ignored' : 'generated outputs tracked'}`,
        `Inventoried outputs: ${plan.outputs}`,
        `Proposed index removals: ${plan.removals.length}` +
            (plan.removals.length ? ` (${Object.entries(kinds).map(([kind, count]) => `${count} ${kind}`).join(', ')})` : ''),
        `Inputs that must stay tracked: ${plan.retained.length}`,
        `Regenerate every removal with: ${regenerate}`,
        '',
        ...plan.removals.map(removal => `  remove ${removal.path}  <- ${removal.owner}`),
    ];
    if (plan.problems.length) lines.push('', `Problems (${plan.problems.length}):`, ...plan.problems.map(problem => `  - ${problem}`));
    return lines.join('\n');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const args = process.argv.slice(2);
    try {
        if (args.includes('--write-ignore')) {
            console.log(writeIgnoreRules() ? 'Updated the generated-output block in .gitignore.' : 'Generated-output ignore rules are current.');
        } else {
            const plan = ownershipPlan();
            if (args.includes('--ignore-rules')) process.stdout.write(plan.ignoreRules);
            else if (args.includes('--json')) console.log(JSON.stringify(plan, null, 2));
            else if (args.includes('--check')) {
                if (plan.problems.length) throw new Error(`Browser ownership check failed:\n${plan.problems.map(problem => `  - ${problem}`).join('\n')}`);
                console.log(`Browser ownership check passed: ${plan.outputs} outputs, ${plan.retained.length} retained inputs, ` +
                    `${plan.cutover ? 'none tracked' : 'all tracked'}.`);
            } else console.log(report(plan));
        }
    } catch (error) {
        console.error(error.message);
        process.exit(1);
    }
}
