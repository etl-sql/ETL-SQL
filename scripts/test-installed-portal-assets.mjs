#!/usr/bin/env node
/**
 * Checks the browser files a running, installed Portal serves.
 *
 * An installed package is accepted by what a browser receives, not by what sits on disk: every file
 * under the install's `wwwroot` must be served with its installed bytes, and development files and
 * missing modules must not be. With --inventory-root the installed bytes must also match that
 * checkout's prepared inventory, so the candidate is checked rather than whatever was installed.
 *
 * Upgrades are checked as a browser with a populated cache would see them. --record saves each
 * asset's validators before the upgrade; --revalidate replays them afterwards. A 304 for a file
 * whose bytes changed is a stale cache, which is the failure this exists to catch.
 *
 * Usage:
 *   node scripts/test-installed-portal-assets.mjs --url http://localhost:5002 --install <dir>
 *     [--inventory-root <checkout>] [--record <cache.json>] [--revalidate <cache.json>] [--out <evidence.json>] [--dump <dir>]
 */
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { packagedAssetProblems } from './verify-packaged-browser-assets.mjs';

const assetRoots = ['js/', 'css/', 'designer/', 'img/', 'maps/'];
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
// The Portal re-encodes an HTML page to stamp a per-request CSP nonce on every <script> and <style>,
// which also drops a UTF-8 byte order mark; the page is otherwise its installed bytes.
const withoutNonces = (relative, bytes) => relative.endsWith('.html')
    ? Buffer.from(bytes.toString('utf8').replace(/^﻿/, '').replace(/ nonce="[^"]*"/g, ''), 'utf8') : bytes;
// Precompressed siblings are negotiated through Accept-Encoding on their original's URL.
const precompressed = (relative, files) => /\.(?:br|gz)$/.test(relative) && files.has(relative.replace(/\.(?:br|gz)$/, ''));

function filesUnder(directory, base = directory) {
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        const full = path.join(directory, entry.name);
        return entry.isDirectory() ? filesUnder(full, base) : [path.relative(base, full).replaceAll('\\', '/')];
    });
}

const urlFor = (base, relative) => `${base}/${relative.split('/').map(encodeURIComponent).join('/')}`;

async function fetchBytes(url, headers = {}) {
    const response = await fetch(url, { headers, redirect: 'manual' });
    return { status: response.status, headers: response.headers, bytes: Buffer.from(await response.arrayBuffer()) };
}

async function inBatches(items, size, work) {
    const results = [];
    for (let i = 0; i < items.length; i += size) results.push(...await Promise.all(items.slice(i, i + size).map(work)));
    return results;
}

export async function checkInstalledPortal({ url, install, inventoryRoot, record, revalidate, dump }) {
    const base = url.replace(/\/+$/, '');
    const webRoot = path.join(install, 'wwwroot');
    if (!fs.existsSync(webRoot)) throw new Error(`Installed folder has no wwwroot: ${install}`);
    const problems = [];
    const evidence = { url: base, install, served: 0, inventoryChecked: 0 };

    if (inventoryRoot) {
        const result = packagedAssetProblems(install, { root: inventoryRoot });
        evidence.inventoryChecked = result.checked;
        problems.push(...result.problems);
    }

    const installed = filesUnder(webRoot).sort();
    const installedSet = new Set(installed);
    const files = installed.filter(relative => !precompressed(relative, installedSet));
    evidence.precompressedSkipped = installed.length - files.length;
    const served = await inBatches(files, 16, async relative => {
        const response = await fetchBytes(urlFor(base, relative));
        const disk = sha256(withoutNonces(relative, fs.readFileSync(path.join(webRoot, relative))));
        const entry = { path: relative, status: response.status, sha256: sha256(withoutNonces(relative, response.bytes)),
            etag: response.headers.get('etag'), lastModified: response.headers.get('last-modified'),
            cacheControl: response.headers.get('cache-control') };
        if (response.status !== 200) problems.push(`Not served (${response.status}): /${relative}`);
        else if (entry.sha256 !== disk) {
            problems.push(`Served bytes differ from the installed file: /${relative}`);
            if (dump) {
                const target = path.join(dump, relative);
                fs.mkdirSync(path.dirname(target), { recursive: true });
                fs.writeFileSync(target, response.bytes);
            }
        }
        else if (assetRoots.some(root => relative.startsWith(root)) && !/\bno-cache\b/.test(entry.cacheControl ?? ''))
            problems.push(`Asset may be reused without revalidation (Cache-Control: ${entry.cacheControl}): /${relative}`);
        return entry;
    });
    evidence.served = served.filter(entry => entry.status === 200).length;

    // What must not be served: a missing module, and the development files beside generated output.
    const script = files.find(relative => relative.startsWith('js/') && relative.endsWith('.js'));
    const refused = ['js/__missing-module__.js'];
    if (script) refused.push(script.replace(/\.js$/, '.ts'), `${script}.map`);
    // Linux file systems are case-sensitive, so a wrong-case URL must miss there.
    if (script && process.platform === 'linux') refused.push(script.toUpperCase());
    for (const relative of refused) {
        const { status } = await fetchBytes(urlFor(base, relative));
        if (status !== 404) problems.push(`Expected 404, got ${status}: /${relative}`);
    }
    evidence.refused = refused;

    if (record) {
        const cache = served.filter(entry => entry.status === 200 && (entry.etag || entry.lastModified))
            .map(({ path: p, sha256: s, etag, lastModified }) => ({ path: p, sha256: s, etag, lastModified }));
        fs.writeFileSync(record, JSON.stringify({ url: base, recordedAt: new Date().toISOString(), entries: cache }, null, 2));
        evidence.recorded = cache.length;
    }

    if (revalidate) {
        const previous = JSON.parse(fs.readFileSync(revalidate, 'utf8')).entries;
        const current = new Map(served.map(entry => [entry.path, entry]));
        const tally = { unchanged304: 0, unchanged200: 0, changedRefetched: 0, removed: 0, stale: 0 };
        await inBatches(previous, 16, async old => {
            const headers = {};
            if (old.etag) headers['If-None-Match'] = old.etag;
            if (old.lastModified) headers['If-Modified-Since'] = old.lastModified;
            const response = await fetchBytes(urlFor(base, old.path), headers);
            const now = current.get(old.path);
            if (!now) {
                tally.removed++;
                if (response.status !== 404) problems.push(`Removed asset still answers ${response.status}: /${old.path}`);
            } else if (response.status === 304) {
                if (now.sha256 === old.sha256) tally.unchanged304++;
                else { tally.stale++; problems.push(`Stale cache: 304 for changed bytes: /${old.path}`); }
            } else if (response.status === 200) {
                if (sha256(withoutNonces(old.path, response.bytes)) !== now.sha256) problems.push(`Revalidation returned other bytes: /${old.path}`);
                else if (now.sha256 === old.sha256) tally.unchanged200++;
                else tally.changedRefetched++;
            } else problems.push(`Revalidation answered ${response.status}: /${old.path}`);
        });
        evidence.revalidation = tally;
    }

    evidence.problems = problems;
    evidence.passed = problems.length === 0;
    return evidence;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const { values } = parseArgs({ options: {
        url: { type: 'string' }, install: { type: 'string' }, 'inventory-root': { type: 'string' },
        record: { type: 'string' }, revalidate: { type: 'string' }, out: { type: 'string' }, dump: { type: 'string' } } });
    try {
        if (!values.url || !values.install) throw new Error('Usage: --url <base> --install <dir> [--inventory-root <checkout>] [--record <file>] [--revalidate <file>] [--out <file>]');
        const evidence = await checkInstalledPortal({ url: values.url, install: path.resolve(values.install),
            inventoryRoot: values['inventory-root'] && path.resolve(values['inventory-root']),
            record: values.record, revalidate: values.revalidate, dump: values.dump });
        if (values.out) fs.writeFileSync(values.out, JSON.stringify(evidence, null, 2));
        const summary = `installed Portal: ${evidence.served} files served with installed bytes` +
            (evidence.inventoryChecked ? `, ${evidence.inventoryChecked} matched the inventory` : '') +
            (evidence.revalidation ? `, revalidation ${JSON.stringify(evidence.revalidation)}` : '');
        if (!evidence.passed) throw new Error(`${summary}\n${evidence.problems.slice(0, 40).map(p => `  - ${p}`).join('\n')}` +
            (evidence.problems.length > 40 ? `\n  ... and ${evidence.problems.length - 40} more` : ''));
        console.log(summary);
    } catch (error) {
        console.error(error.message);
        process.exit(1);
    }
}
