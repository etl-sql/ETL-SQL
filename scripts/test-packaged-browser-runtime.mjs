#!/usr/bin/env node
// Exercise release binaries outside the checkout, with Node absent from their PATH.
// Usage: node scripts/test-packaged-browser-runtime.mjs <source> <package-bin> <evidence> <playwright-package>
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { packagedAssetProblems } from './verify-packaged-browser-assets.mjs';

const [sourceArg, packageArg, evidenceArg, playwrightArg] = process.argv.slice(2);
assert.ok(sourceArg && packageArg && evidenceArg && playwrightArg,
    'Expected source checkout, extracted package bin, evidence directory and installed Playwright package.');
const source = fs.realpathSync(sourceArg);
const bin = fs.realpathSync(packageArg);
const evidence = path.resolve(evidenceArg);
const relativePackage = path.relative(source, bin);
assert.ok(relativePackage.startsWith('..' + path.sep) || path.isAbsolute(relativePackage),
    'Run the package outside its source checkout.');
fs.mkdirSync(evidence, { recursive: true });
const { chromium } = createRequire(import.meta.url)(path.resolve(playwrightArg));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const inventory = JSON.parse(fs.readFileSync(path.join(source, 'artifacts/browser-assets.json'), 'utf8'));
const fixture = path.join(evidence, 'asset-proof.rptsql');
const script = "CREATE DATASET &proof AS (SELECT 21 AS Value);\nCREATE VISUAL AssetProof AS TABLE (SOURCE = &proof);\n";
fs.writeFileSync(fixture, script);
const emptyPath = path.join(evidence, 'empty-path');
fs.mkdirSync(emptyPath, { recursive: true });
const hostEnv = Object.fromEntries(Object.entries(process.env).filter(([key]) => key.toLowerCase() !== 'path'));
hostEnv.PATH = emptyPath;
hostEnv.ETLSQL_SECURITY_EVENT_OUTBOX_PATH = path.join(evidence, 'security-events.db');
const suffix = process.platform === 'win32' ? '.exe' : '';
const token = randomUUID();
const report = { startedAt: new Date().toISOString(), source, bin, nodeAbsentFromHostPath: true,
    inventorySha256: sha(fs.readFileSync(path.join(source, 'artifacts/browser-assets.json'))),
    binaries: {}, hosts: {}, browserErrors: [], externalRequests: [], passed: false };
const children = [];
let browser;

function launch(name, args) {
    const executable = path.join(bin, name + suffix);
    report.binaries[name] = sha(fs.readFileSync(executable));
    const child = spawn(executable, args, { cwd: bin, env: hostEnv, windowsHide: true });
    children.push(child);
    let output = '';
    const log = fs.createWriteStream(path.join(evidence, name + '.log'), { flags: 'a' });
    for (const stream of [child.stdout, child.stderr]) stream.on('data', data => { output += data; log.write(data); });
    const done = new Promise((resolve, reject) => {
        child.once('error', reject);
        child.once('close', code => { log.end(); resolve(code); });
    });
    // Startup and CLI completion both have a bound; always terminate timed-out children.
    const timer = setTimeout(() => child.kill(), 90000);
    done.finally(() => clearTimeout(timer)).catch(() => {});
    return { child, done, output: () => output, clearDeadline: () => clearTimeout(timer) };
}

async function host(name, args, pattern) {
    const running = launch(name, args);
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        const match = running.output().match(pattern);
        if (match) {
            running.clearDeadline();
            return new URL(match[1]).origin;
        }
        assert.equal(running.child.exitCode, null, running.output());
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(`${name} did not report its listening URL: ${running.output()}`);
}

async function contextFor(origin) {
    const context = await browser.newContext();
    await context.route('**/*', async route => {
        const url = new URL(route.request().url());
        if (/^https?:$/.test(url.protocol) && url.origin !== origin) {
            report.externalRequests.push(url.href);
            await route.abort();
        } else await route.continue();
    });
    context.on('page', page => {
        page.on('pageerror', error => report.browserErrors.push(error.message));
        page.on('console', message => {
            if (message.type() === 'error' && /Content Security Policy|Refused to (load|execute)/i.test(message.text()))
                report.browserErrors.push(message.text());
        });
    });
    return context;
}

async function checkAssets(context, origin, project) {
    const prefix = `src/ETL-SQL.${project}/wwwroot/`;
    const outputs = inventory.outputs.filter(output => output.path.startsWith(prefix));
    assert.ok(outputs.length > 20, `${project} inventory unexpectedly empty`);
    const checks = [];
    for (const output of outputs) {
        const relative = output.path.slice(prefix.length);
        const url = project === 'WorkstationEditor' && !/^(designer|css)\//.test(relative)
            ? '/runtime/' + relative : '/' + relative;
        const response = await context.request.get(origin + url);
        assert.equal(response.status(), 200, url);
        assert.equal(sha(await response.body()), output.sha256, url);
        const contentType = response.headers()['content-type'];
        if (url.endsWith('.js')) assert.match(contentType, /(?:java|ecma)script/, url);
        if (url.endsWith('.css')) assert.match(contentType, /text\/css/, url);
        if (url.endsWith('.geojson')) assert.match(contentType, /application\/geo\+json/, url);
        checks.push({ url, sha256: output.sha256, contentType });
    }
    const runtimePrefix = project === 'WorkstationEditor' ? '/runtime' : '';
    for (const url of ['/deliberately-missing-module.js', '/rt-util.ts', '/rt-util.js.map'].map(value => runtimePrefix + value)) {
        const response = await context.request.get(origin + url);
        assert.equal(response.status(), 404, url);
    }
    report.hosts[project] = { checked: checks.length, assets: checks, missingModuleStatus: 404 };
}

try {
    const packageCheck = packagedAssetProblems(bin, { root: source });
    assert.deepEqual(packageCheck.problems, []);
    report.packagedAssets = packageCheck.checked;
    browser = await chromium.launch({ headless: true });

    const playerUrl = await host('ETL-SQL-Player', [fixture, '--port', '0', '--no-browser'], /REPORT_URL=(http:\/\/[^\s]+)/);
    const playerContext = await contextFor(playerUrl);
    const playerPage = await playerContext.newPage();
    await playerPage.goto(playerUrl);
    await playerPage.locator('#root td').filter({ hasText: '21' }).waitFor();
    await checkAssets(playerContext, playerUrl, 'ReportPlayer');
    assert.equal((await playerContext.request.get(playerUrl + '/third-party-notices')).status(), 200);
    await playerPage.screenshot({ path: path.join(evidence, 'player.png'), fullPage: true });
    await playerContext.close();

    const editorUrl = await host('ETL-SQL-Editor', [fixture, '--port', '0', '--token', token], /URL:\s+(http:\/\/[^\s]+)/);
    const editorContext = await contextFor(editorUrl);
    const editorPage = await editorContext.newPage();
    await editorPage.goto(`${editorUrl}/studio/?token=${token}`);
    await editorPage.locator('[data-choose-workflow="dashboard"]').click();
    await editorPage.waitForFunction(() => window.__STUDIO__?.state.editorInstance);
    assert.match(await editorPage.evaluate(() => window.__STUDIO__.state.editorInstance.getValue()), /AssetProof/);
    const preview = await editorContext.request.post(editorUrl + '/api/preview', {
        headers: { 'X-ETLSQL-EDITOR-TOKEN': token }, data: { script }
    });
    assert.equal(preview.status(), 200, await preview.text());
    const manifest = await preview.json();
    assert.ok(manifest.visuals.some(visual => visual.name === 'AssetProof'));
    const previewPage = await editorContext.newPage();
    await previewPage.goto(editorUrl + '/designer-preview.html');
    await previewPage.evaluate(value => window.postMessage({ type: 'reportManifest', manifest: value }, '*'), manifest);
    await previewPage.locator('#root td').filter({ hasText: '21' }).waitFor();
    await checkAssets(editorContext, editorUrl, 'WorkstationEditor');
    await editorPage.screenshot({ path: path.join(evidence, 'editor.png'), fullPage: true });
    await editorContext.close();

    const build = launch('ETL-SQL-Report', ['build', fixture, '--format', 'json']);
    assert.equal(await build.done, 0, build.output());
    assert.match(build.output(), /Snapshot saved to:/);
    const offlinePath = path.join(evidence, 'asset-proof.offline.html');
    const offline = launch('ETL-SQL-Report', ['offline', fixture, '--output', offlinePath]);
    assert.equal(await offline.done, 0, offline.output());
    const html = fs.readFileSync(offlinePath, 'utf8');
    const embedded = ['report-runtime.bundle.js', 'report-runtime.css', 'feedback.js'];
    report.embedded = embedded.map(name => {
        const bytes = fs.readFileSync(path.join(source, 'src/ETL-SQL.ReportRuntime/Resources/Shared', name), 'utf8');
        assert.ok(html.includes(bytes), `Offline export does not contain the prepared ${name}`);
        return { name, sha256: sha(bytes) };
    });
    const offlineContext = await contextFor(null);
    await offlineContext.setOffline(true);
    const offlinePage = await offlineContext.newPage();
    await offlinePage.goto(pathToFileURL(offlinePath).href);
    await offlinePage.locator('#root td').filter({ hasText: '21' }).waitFor();
    assert.equal(await offlinePage.evaluate(() => window.__ETLSNAP__), true);
    await offlinePage.screenshot({ path: path.join(evidence, 'offline.png'), fullPage: true });
    await offlineContext.close();
    assert.deepEqual(report.browserErrors, []);
    assert.deepEqual(report.externalRequests, []);
    report.passed = true;
} catch (error) {
    report.error = error.stack;
    process.exitCode = 1;
} finally {
    await browser?.close();
    for (const child of children) if (child.exitCode === null) child.kill();
    report.finishedAt = new Date().toISOString();
    fs.writeFileSync(path.join(evidence, 'runtime.json'), JSON.stringify(report, null, 2) + '\n');
}
console.log(JSON.stringify({ passed: report.passed, hosts: Object.keys(report.hosts), error: report.error }));
