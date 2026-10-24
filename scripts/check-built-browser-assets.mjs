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
 *   node scripts/check-built-browser-assets.mjs --configuration Release --project tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj
 */
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

function evaluateProject(project, configuration, timeout) {
    const result = spawnSync('dotnet', ['msbuild', project, '-nologo', '-nr:false',
        `-p:Configuration=${configuration}`, '-getProperty:MSBuildProjectName,MSBuildProjectFullPath,RequiresBrowserAssets,TargetFrameworks',
        '-getItem:ProjectReference'], { encoding: 'utf8', windowsHide: true, timeout, maxBuffer: 4 * 1024 * 1024 });
    if (result.error || result.status !== 0) throw new Error('MSBuild could not evaluate the project references. Build without --no-build.');
    try { return JSON.parse(result.stdout.replace(/^\uFEFF/, '')); }
    catch { throw new Error('MSBuild returned invalid project reference metadata.'); }
}

function projectConsumers(root, project, configuration, evaluate) {
    const boundary = fs.realpathSync(root);
    const visited = new Set(), active = new Set(), consumers = new Map();
    const deadline = Date.now() + 60000;
    const key = value => process.platform === 'win32' ? value.toLowerCase() : value;
    function visit(file) {
        const resolved = fs.realpathSync(file);
        if (!key(resolved).startsWith(key(boundary + path.sep))) throw new Error('Project reference is outside the repository.');
        const identity = key(resolved);
        if (active.has(identity)) throw new Error('Project reference cycle prevents asset verification.');
        if (visited.has(identity)) return;
        const remaining = deadline - Date.now();
        if (remaining <= 0) throw new Error('Project reference evaluation exceeded its deadline.');
        active.add(identity);
        const metadata = evaluate(resolved, configuration, remaining);
        const properties = metadata?.Properties;
        const references = metadata?.Items?.ProjectReference;
        if (properties?.MSBuildProjectName !== path.basename(resolved, path.extname(resolved)) ||
            typeof properties.MSBuildProjectFullPath !== 'string' || key(path.resolve(properties.MSBuildProjectFullPath)) !== identity ||
            typeof properties.RequiresBrowserAssets !== 'string' || !Array.isArray(references) ||
            typeof properties.TargetFrameworks !== 'string' || properties.TargetFrameworks) {
            throw new Error('Project reference metadata is incomplete or requires an explicit target framework.');
        }
        if (properties.RequiresBrowserAssets.toLowerCase() === 'true') {
            const name = properties.MSBuildProjectName;
            const existing = consumers.get(name.toLowerCase());
            if (existing && existing.project !== resolved) throw new Error('Asset consumers have ambiguous project names.');
            consumers.set(name.toLowerCase(), { name, project: resolved });
        }
        for (const reference of references) {
            // Configuration is the only global property passed to the metadata query. SDKs may
            // remove unrelated globals (for example WebPublishProfileFile) without changing it.
            const removed = reference?.GlobalPropertiesToRemove ?? '';
            if (typeof reference?.FullPath !== 'string' || !path.isAbsolute(reference.FullPath) ||
                typeof removed !== 'string' || removed.split(/[;,]/).some(name => name.trim().toLowerCase() === 'configuration') ||
                ['AdditionalProperties', 'SetConfiguration', 'SetPlatform', 'SetTargetFramework', 'Targets'].some(property => reference[property])) {
                throw new Error('Project reference overrides prevent scoped asset verification. Build without --no-build.');
            }
            visit(reference.FullPath);
        }
        active.delete(identity);
        visited.add(identity);
    }
    visit(path.resolve(root, project));
    return Array.from(consumers.values()).map(consumer => consumer.name).sort();
}

export function builtAssetProblems({ root = defaultRoot, configuration = 'Debug', project = null, evaluate = evaluateProject } = {}) {
    if (typeof configuration !== 'string' || !/^[a-zA-Z0-9_.-]+$/.test(configuration)) return ['Invalid build configuration.'];
    const manifest = path.join(root, 'artifacts/browser-assets.props');
    if (!fs.existsSync(manifest)) return ['Browser assets are not prepared. Run node scripts/sync-assets.js, then build.'];
    const current = createHash('sha256').update(fs.readFileSync(manifest)).digest('hex');
    const directory = path.join(root, 'artifacts/browser-assets-built');
    // Stamps are named Project.Configuration.Runtime.sha256; a test or run without a runtime
    // identifier uses the "any" build, which is the one a --no-build entry point reuses.
    const suffix = `.${configuration}.any.sha256`.toLowerCase();
    const stamps = fs.existsSync(directory)
        ? fs.readdirSync(directory).filter(name => name.toLowerCase().endsWith(suffix)).sort() : [];
    if (project !== null) {
        try {
            if (typeof project !== 'string' || !project) throw new Error('A project path is required.');
            return projectConsumers(root, project, configuration, evaluate).flatMap(name => {
                const matches = stamps.filter(stamp => stamp.toLowerCase() === `${name}${suffix}`.toLowerCase());
                if (matches.length !== 1) return [`${name} (${configuration}) has no unique build recorded for the prepared browser assets. Build once without --no-build.`];
                return fs.readFileSync(path.join(directory, matches[0]), 'utf8').trim().toLowerCase() === current ? [] :
                    [`${name} (${configuration}) was built from an older browser asset preparation. Rebuild without --no-build.`];
            });
        } catch (error) {
            return [`Cannot verify scoped browser assets: ${error.code ?? error.message}`];
        }
    }
    if (!stamps.length)
        return [`No ${configuration} build has recorded the browser assets it used. Build once without --no-build.`];
    return stamps
        .filter(name => fs.readFileSync(path.join(directory, name), 'utf8').trim().toLowerCase() !== current)
        .map(name => `${name.slice(0, -suffix.length)} (${configuration}) was built from an older browser asset preparation. Rebuild without --no-build.`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const options = {};
    const names = new Map([['--configuration', 'configuration'], ['--project', 'project']]);
    const args = process.argv.slice(2);
    for (let i = 0; i < args.length; i += 2) {
        const name = names.get(args[i]);
        if (!name || !args[i + 1] || args[i + 1].startsWith('--') || Object.hasOwn(options, name)) {
            console.error('Usage: check-built-browser-assets.mjs [--configuration <name>] [--project <project-file>]');
            process.exit(1);
        }
        options[name] = args[i + 1];
    }
    const problems = builtAssetProblems(options);
    if (problems.length) {
        console.error(`Compiled output does not match the prepared browser assets:\n${problems.map(problem => `  - ${problem}`).join('\n')}`);
        process.exit(1);
    }
}
