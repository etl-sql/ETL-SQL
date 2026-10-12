#!/usr/bin/env node
/**
 * Compares the browser files in a published folder with the prepared ownership inventory.
 *
 * Generated JavaScript is not in Git, so nothing about the checkout proves a package received it.
 * A published folder is correct when every output for the hosts it serves is present with the
 * prepared bytes, and nothing that only belongs in a checkout came along.
 *
 * The release layout publishes Portal, the desktop Player, and the desktop editor into one folder,
 * so all three hosts' outputs are expected under its `wwwroot`.
 *
 * Usage:
 *   node scripts/verify-packaged-browser-assets.mjs <published-folder>
 */
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { readOwnership } = require('./lib/browser-asset-inputs.cjs');

const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const hostRoots = ['src/ETL-SQL.Portal/wwwroot/', 'src/ETL-SQL.ReportPlayer/wwwroot/', 'src/ETL-SQL.WorkstationEditor/wwwroot/'];
const developmentOnly = /\.(?:ts|map)$|(?:^|\/)browser-assets\.(?:json|props)$|\.__test-[^/]+\.mjs$/;

function filesUnder(directory, base = directory) {
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        const full = path.join(directory, entry.name);
        return entry.isDirectory() ? filesUnder(full, base) : [path.relative(base, full).replaceAll('\\', '/')];
    });
}

export function packagedAssetProblems(published, { root = defaultRoot } = {}) {
    const inventory = readOwnership(root);
    if (inventory.version !== 2 || inventory.outputs.length === 0)
        throw new Error('Browser ownership inventory is missing. Run node scripts/sync-assets.js first.');
    const webRoot = path.join(published, 'wwwroot');
    if (!fs.existsSync(webRoot)) return { checked: 0, problems: [`Published folder has no wwwroot: ${published}`] };
    const problems = [];
    let checked = 0;
    for (const output of inventory.outputs) {
        const host = hostRoots.find(prefix => output.path.startsWith(prefix));
        if (!host) continue;
        const relative = output.path.slice(host.length);
        const file = path.join(webRoot, relative);
        checked++;
        if (!fs.existsSync(file)) problems.push(`Missing from the package: wwwroot/${relative} (${output.path})`);
        else if (createHash('sha256').update(fs.readFileSync(file)).digest('hex') !== output.sha256)
            problems.push(`Differs from the prepared file: wwwroot/${relative} (${output.path})`);
    }
    for (const relative of filesUnder(webRoot))
        if (developmentOnly.test(relative)) problems.push(`Development file in the package: wwwroot/${relative}`);
    return { checked, problems };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const published = process.argv[2];
    try {
        if (!published) throw new Error('Usage: node scripts/verify-packaged-browser-assets.mjs <published-folder>');
        const { checked, problems } = packagedAssetProblems(path.resolve(published));
        if (problems.length) throw new Error(`Packaged browser assets do not match the prepared inventory:\n${problems.slice(0, 40).map(problem => `  - ${problem}`).join('\n')}` +
            (problems.length > 40 ? `\n  ... and ${problems.length - 40} more` : ''));
        console.log(`packaged browser assets: ${checked} files match the prepared inventory.`);
    } catch (error) {
        console.error(error.message);
        process.exit(1);
    }
}
