#!/usr/bin/env node
/**
 * Pre-commit: runs the browser ownership check when the staged change touches browser assets.
 *
 * The check reads the index, so it judges the commit being made and not the working tree around
 * it. Commits that touch nothing here are left alone: an unrelated commit should not fail because
 * someone else's new, still-untracked page is sitting in the same checkout.
 */
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const assetPaths = [
    /^src\/ETL-SQL\.ReportRuntime\/Resources\/(?:TypeScript|Shared)\//,
    /^src\/ETL-SQL\.Portal\/(?:BrowserSources|wwwroot|BrowserContracts)\//,
    /^src\/ETL-SQL\.(?:ReportPlayer|WorkstationEditor)\/wwwroot\//,
    /^src\/etl-sql-vscode\/media\//,
    /^types\//,
    /^scripts\/(?:sync-assets\.js|compile-browser\.mjs|browser-asset-ownership\.mjs|lib\/)/,
    /^(?:\.gitignore|tsconfig\.browser-build\.json|Directory\.Build\.targets)$/,
];

export function touchesBrowserAssets(paths) {
    return paths.some(file => assetPaths.some(pattern => pattern.test(file)));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const staged = spawnSync('git', ['diff', '--cached', '--name-only', '-z'], { cwd: root, encoding: 'utf8' });
    if (staged.status !== 0) { console.error(staged.stderr); process.exit(1); }
    if (touchesBrowserAssets(staged.stdout.split('\0').filter(Boolean))) {
        if (!fs.existsSync(path.join(root, 'artifacts/browser-assets.json'))) {
            console.error('This commit changes browser assets, but they are not prepared. Run node scripts/sync-assets.js first.');
            process.exit(1);
        }
        const check = spawnSync(process.execPath, [path.join(root, 'scripts/browser-asset-ownership.mjs'), '--check'],
            { cwd: root, stdio: ['ignore', 'ignore', 'inherit'] });
        process.exit(check.status ?? 1);
    }
}
