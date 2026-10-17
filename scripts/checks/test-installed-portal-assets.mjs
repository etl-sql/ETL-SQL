import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { checkInstalledPortal } from '../test-installed-portal-assets.mjs';

// A stand-in Portal serving an install folder. `etagFor` lets a case reuse an old validator for new bytes.
// Like the Portal, it stamps a fresh CSP nonce on HTML script tags and never serves a .br file by name.
function serve(install, { etagFor = bytes => `"${createHash('sha256').update(bytes).digest('hex').slice(0, 16)}"`, cacheControl = 'no-cache, must-revalidate', htmlSuffix = '' } = {}) {
    const server = http.createServer((request, response) => {
        const relative = decodeURIComponent(new URL(request.url, 'http://x').pathname.slice(1));
        const file = path.join(install, 'wwwroot', relative);
        if (!relative || relative.endsWith('.br') || !fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404).end(); return; }
        let bytes = fs.readFileSync(file);
        // The validator describes the file, as ASP.NET's does; the nonce is applied to the body after.
        const etag = etagFor(bytes, relative);
        if (relative.endsWith('.html'))
            bytes = Buffer.from(bytes.toString('utf8').replace(/^﻿/, '').replaceAll('<script', `<script nonce="${Math.random().toString(36).slice(2)}"`) + htmlSuffix);
        if (request.headers['if-none-match'] === etag) { response.writeHead(304, { ETag: etag }).end(); return; }
        response.writeHead(200, { ETag: etag, 'Cache-Control': cacheControl }).end(bytes);
    });
    return new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve({ server, url: `http://127.0.0.1:${server.address().port}` })));
}

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'installed-portal-'));
const install = path.join(root, 'install');
const write = (relative, text) => {
    fs.mkdirSync(path.dirname(path.join(install, 'wwwroot', relative)), { recursive: true });
    fs.writeFileSync(path.join(install, 'wwwroot', relative), text);
};
const cache = path.join(root, 'cache.json');

try {
    write('js/app.js', 'export const version = 1;');
    write('css/site.css', 'body{}');
    write('index.html', '<!doctype html><script src="js/app.js"></script>');
    write('js/app.js.br', 'compressed');
    write('login.html', '﻿<!doctype html><script src="js/app.js"></script>');

    let { server, url } = await serve(install);
    const before = await checkInstalledPortal({ url, install, record: cache });
    server.close();
    assert.equal(before.passed, true, before.problems.join('\n'));
    assert.equal(before.served, 4);
    assert.equal(before.precompressedSkipped, 1);
    assert.equal(before.recorded, 4);

    // A page that differs by more than its nonce is still caught.
    ({ server, url } = await serve(install, { htmlSuffix: '<!-- injected -->' }));
    const tampered = await checkInstalledPortal({ url, install });
    server.close();
    assert.ok(tampered.problems.includes('Served bytes differ from the installed file: /index.html'), tampered.problems.join('\n'));

    // The upgrade changes one asset and removes another.
    write('js/app.js', 'export const version = 2;');
    fs.rmSync(path.join(install, 'wwwroot', 'css', 'site.css'));
    ({ server, url } = await serve(install));
    const upgraded = await checkInstalledPortal({ url, install, revalidate: cache });
    server.close();
    assert.equal(upgraded.passed, true, upgraded.problems.join('\n'));
    assert.deepEqual(upgraded.revalidation, { unchanged304: 2, unchanged200: 0, changedRefetched: 1, removed: 1, stale: 0 });

    // A validator that does not change with the bytes leaves a cached browser on the old file.
    const recorded = JSON.parse(fs.readFileSync(cache, 'utf8')).entries;
    const oldTag = Object.fromEntries(recorded.map(entry => [entry.path, entry.etag]));
    ({ server, url } = await serve(install, { etagFor: (bytes, relative) => oldTag[relative] ?? '"new"' }));
    const stale = await checkInstalledPortal({ url, install, revalidate: cache });
    server.close();
    assert.equal(stale.passed, false);
    assert.equal(stale.revalidation.stale, 1);
    assert.ok(stale.problems.some(problem => problem === 'Stale cache: 304 for changed bytes: /js/app.js'), stale.problems.join('\n'));

    // An asset that may be reused without asking the server is refused even before an upgrade.
    ({ server, url } = await serve(install, { cacheControl: 'max-age=31536000' }));
    const cached = await checkInstalledPortal({ url, install });
    server.close();
    assert.ok(cached.problems.some(problem => problem.startsWith('Asset may be reused without revalidation') && problem.endsWith('/js/app.js')),
        cached.problems.join('\n'));

    // Development files beside generated output must not be served.
    write('js/app.ts', 'export const version: number = 2;');
    ({ server, url } = await serve(install));
    const leaked = await checkInstalledPortal({ url, install });
    server.close();
    assert.ok(leaked.problems.includes('Expected 404, got 200: /js/app.ts'), leaked.problems.join('\n'));
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}
console.log('installed Portal assets: served bytes, revalidation, stale 304, cache policy and development-file checks passed.');
