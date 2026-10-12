#!/usr/bin/env node
// Run from a newer checkout against an older checkout after a revision switch. Preview by default.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { readOwnership, safeAssetPath } = require('./lib/browser-asset-inputs.cjs');
const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');

export function cleanObsoleteBrowserOutputs({ root = defaultRoot, inventoryRoot = root, apply = false } = {}) {
    root = path.resolve(root);
    if (!fs.existsSync(path.join(inventoryRoot, 'artifacts/browser-assets.json')))
        throw new Error('Save the newer checkout ownership inventory before switching revisions.');
    const inventory = readOwnership(inventoryRoot);
    if (inventory.version !== 2) throw new Error('Cleanup requires a version-2 inventory with output hashes.');
    const trackedResult = spawnSync('git', ['-C', root, 'ls-files', '-z'], { encoding: 'utf8' });
    if (trackedResult.status !== 0) throw new Error('Cleanup requires a Git checkout.');
    const tracked = new Set(trackedResult.stdout.split('\0').filter(Boolean).map(file => file.toLowerCase()));
    const removals = [];
    for (const output of inventory.outputs) {
        if (tracked.has(output.path.toLowerCase())) continue;
        const file = safeAssetPath(root, output.path);
        if (!fs.existsSync(file)) continue;
        if (!fs.statSync(file).isFile() || hash(file) !== output.sha256)
            throw new Error(`Preserve locally changed output before cleanup: ${output.path}`);
        removals.push(output);
    }
    // Validate the entire plan before removing anything. Unlisted and tracked files are untouched.
    if (apply) for (const output of removals) {
        const file = safeAssetPath(root, output.path);
        if (hash(file) !== output.sha256) throw new Error(`Output changed during cleanup: ${output.path}`);
        fs.unlinkSync(file);
    }
    return removals.map(output => output.path);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        const args = process.argv.slice(2);
        let root = defaultRoot, inventoryRoot;
        let apply = false;
        while (args.length) {
            const option = args.shift();
            if (option === '--apply') apply = true;
            else if ((option === '--root' || option === '--inventory-root') && args[0] && !args[0].startsWith('--')) {
                const value = path.resolve(args.shift());
                if (option === '--root') root = value;
                else inventoryRoot = value;
            } else throw new Error('Usage: clean-obsolete-browser-outputs.mjs [--root checkout] [--inventory-root saved-checkout] [--apply]');
        }
        const files = cleanObsoleteBrowserOutputs({ root, inventoryRoot: inventoryRoot ?? root, apply });
        console.log(`${apply ? 'Removed' : 'Would remove'} ${files.length} verified untracked outputs.`);
        for (const file of files) console.log(file);
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
