#!/usr/bin/env node
/**
 * Rejects compiled output that was built from an older browser asset preparation.
 *
 * `sync-assets.js -Check` proves the prepared files match their sources. It says nothing about
 * assemblies already on disk: an embedded runtime bundle or a copied static asset stays as it was
 * when the project was last built. Each asset-consuming project therefore records the manifest hash
 * it built against (the StampBuiltBrowserAssets target), and entry points that pass `--no-build`
 * run this check before they test or accept those binaries.
 *
 * Usage:
 *   node scripts/check-built-browser-assets.mjs --configuration Debug
 */
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

export function builtAssetProblems({ root = defaultRoot, configuration = 'Debug' } = {}) {
    const manifest = path.join(root, 'artifacts/browser-assets.props');
    if (!fs.existsSync(manifest)) return ['Browser assets are not prepared. Run node scripts/sync-assets.js, then build.'];
    const current = createHash('sha256').update(fs.readFileSync(manifest)).digest('hex');
    const directory = path.join(root, 'artifacts/browser-assets-built');
    // Stamps are named Project.Configuration.Runtime.sha256; a test or run without a runtime
    // identifier uses the "any" build, which is the one a --no-build entry point reuses.
    const suffix = `.${configuration}.any.sha256`.toLowerCase();
    const stamps = fs.existsSync(directory)
        ? fs.readdirSync(directory).filter(name => name.toLowerCase().endsWith(suffix)).sort() : [];
    if (!stamps.length)
        return [`No ${configuration} build has recorded the browser assets it used. Build once without --no-build.`];
    return stamps
        .filter(name => fs.readFileSync(path.join(directory, name), 'utf8').trim().toLowerCase() !== current)
        .map(name => `${name.slice(0, -suffix.length)} (${configuration}) was built from an older browser asset preparation. Rebuild without --no-build.`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const at = process.argv.indexOf('--configuration');
    const problems = builtAssetProblems({ configuration: at > 0 ? process.argv[at + 1] : 'Debug' });
    if (problems.length) {
        console.error(`Compiled output does not match the prepared browser assets:\n${problems.map(problem => `  - ${problem}`).join('\n')}`);
        process.exit(1);
    }
}
