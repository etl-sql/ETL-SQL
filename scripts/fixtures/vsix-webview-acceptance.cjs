// Run through VS Code --extensionTestsPath; see scripts/README.md for setup.
// Watch mode modifies and restores sources: use a disposable checkout.
const vscode = require('vscode');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const {createHash} = require('node:crypto');
const {spawn} = require('node:child_process');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
exports.run = async function () {
    const result = {startedAt: new Date().toISOString(), passed: false, views: {}, errors: []};
    const reportPath = process.env.ETLSQL_ACCEPTANCE_REPORT;
    try {
        const extension = vscode.extensions.getExtension('etl-sql.etl-sql-vscode');
        assert.ok(extension, 'Packaged extension must be installed.');
        result.extension = extension.extensionPath;
        result.version = extension.packageJSON.version;
        const inventory = JSON.parse(fs.readFileSync(process.env.ETLSQL_ACCEPTANCE_INVENTORY));
        const media = inventory.outputs.filter(output => output.path.startsWith('src/etl-sql-vscode/media/'));
        for (const output of media) {
            const relative = output.path.slice('src/etl-sql-vscode/'.length);
            assert.equal(hash(fs.readFileSync(path.join(extension.extensionPath, relative))), output.sha256, relative);
        }
        result.installedAssets = media.length;
        await extension.activate();
        const loadedModule = name => {
            const expected = path.join(extension.extensionPath, 'out', name).toLowerCase();
            const loaded = Object.keys(require.cache).find(file => file.toLowerCase() === expected);
            assert.ok(loaded, `Activated extension did not load ${name}`);
            return require.cache[loaded].exports;
        };
        const {ReportDesignerPanel} = loadedModule('reportDesignerPanel.js');
        const {ReportPreviewPanel} = loadedModule('reportPreviewPanel.js');
        const lspDeadline = Date.now() + 45000;
        while (!ReportDesignerPanel._lspClient && Date.now() < lspDeadline)
            await new Promise(resolve => setTimeout(resolve, 100));
        assert.ok(ReportDesignerPanel._lspClient, 'The real bundled language server must start.');
        async function verify(klass, method, command, kind, marker) {
            const original = klass.prototype[method];
            let timer, panel;
            const completion = new Promise((resolve, reject) => {
                timer = setTimeout(() => reject(new Error(`${kind} webview acceptance timed out`)), 60000);
                klass.prototype[method] = function (...args) {
                    const html = original.apply(this, args);
                    panel = this._panel;
                    const nonce = /<script nonce="([^"]+)"/.exec(html)?.[1];
                    assert.ok(nonce, 'Use the production CSP nonce.');
                    panel.webview.onDidReceiveMessage(message => {
                        if (message.type === 'acceptance') resolve(message);
                        if (message.type === 'log' && message.level === 'error') result.errors.push(message.message);
                    });
                    const missing = panel.webview.asWebviewUri(vscode.Uri.joinPath(extension.extensionUri, 'media', 'deliberately-missing-module.js')).toString();
                    const observer = `<script nonce="${nonce}">
                      const acceptanceErrors = [];
                      document.addEventListener('securitypolicyviolation', e => acceptanceErrors.push(e.violatedDirective));
                      window.addEventListener('error', e => acceptanceErrors.push(e.message));
                      const acceptanceTimer = setInterval(async () => {
                        const root = document.getElementById('${kind === 'designer' ? 'designerRoot' : 'root'}');
                        const card = root?.querySelector('.etlsql-dsgn-visual-card');
                        const ready = ${kind === 'designer' ? 'card' : "root?.querySelector('td')?.textContent.includes('21')"};
                        if (!ready) return;
                        clearInterval(acceptanceTimer);
                        let missingRejected = false;
                        try { await import(${JSON.stringify(missing)}); } catch { missingRejected = true; }
                        acquireVsCodeApi().postMessage({type:'acceptance', kind:${JSON.stringify(kind)}, text:root.textContent,
                          runtime:!!window.__reportRuntime__, marker:card?.dataset.assetProbe, missingRejected, errors:acceptanceErrors,
                          resources:performance.getEntriesByType('resource').map(r=>({name:r.name,status:r.responseStatus}))});
                      }, 100);
                    </script>`;
                    return html.replace('</head>', observer + '</head>');
                };
            });
            try {
                const document = await vscode.workspace.openTextDocument(process.env.ETLSQL_ACCEPTANCE_FIXTURE);
                await vscode.window.showTextDocument(document);
                await vscode.commands.executeCommand(command, document.uri);
                const view = await completion;
                assert.equal(view.missingRejected, true);
                assert.equal(view.resources.find(resource => resource.name.includes('deliberately-missing-module.js'))?.status, 404);
                assert.deepEqual(view.errors, []);
                if (kind === 'preview') { assert.equal(view.runtime, true); assert.match(view.text, /21/); }
                else assert.match(view.text, /AssetProof/);
                if (marker) assert.equal(view.marker, marker);
                result.views[marker || kind] = view;
            } finally {
                clearTimeout(timer);
                klass.prototype[method] = original;
                panel?.dispose();
            }
        }
        await verify(ReportDesignerPanel, '_getHtml', 'etlsql.openReportDesigner', 'designer');
        await verify(ReportPreviewPanel, '_getReportHtml', 'etlsql.previewReport', 'preview');
        if (process.env.ETLSQL_ACCEPTANCE_WATCH_SOURCE) {
            const root = process.env.ETLSQL_ACCEPTANCE_WATCH_SOURCE;
            const owner = path.join(root, 'src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-canvas-render.ts');
            const added = path.join(path.dirname(owner), 'acceptance-probe.ts');
            const emitted = path.join(extension.extensionPath, 'media/designer/designer-canvas-render.js');
            const emittedAdded = path.join(extension.extensionPath, 'media/designer/acceptance-probe.js');
            const original = fs.readFileSync(owner, 'utf8');
            assert.ok(original.includes('card.className ='));
            assert.equal(fs.existsSync(added), false);
            const watchers = [];
            const logs = {};
            const until = async predicate => {
                const deadline = Date.now() + 45000;
                while (Date.now() < deadline) {
                    if (predicate()) return;
                    await new Promise(resolve => setTimeout(resolve, 100));
                }
                throw new Error('Watcher did not reach the expected compilation state.');
            };
            try {
                for (const command of ['watch', 'watch:assets']) {
                    logs[command] = '';
                    const child = spawn(process.env.ETLSQL_ACCEPTANCE_NODE,
                        [process.env.ETLSQL_ACCEPTANCE_NPM, 'run', command],
                        {cwd:extension.extensionPath, windowsHide:true, env:process.env});
                    watchers.push(child);
                    child.on('error', error => { logs[command] += error.message; });
                    for (const stream of [child.stdout, child.stderr]) stream.on('data', data => { logs[command] += data; });
                }
                await until(() => /Watching for file changes/i.test(logs.watch) && /watch/i.test(logs['watch:assets']));
                fs.writeFileSync(owner, original.replace('card.className =', "card.dataset.assetProbe = 'dependency';\n            card.className ="));
                await until(() => fs.readFileSync(emitted,'utf8').includes("assetProbe = 'dependency'"));
                await verify(ReportDesignerPanel, '_getHtml', 'etlsql.openReportDesigner', 'designer', 'dependency');
                fs.writeFileSync(added, "export const assetProbe = 'new-module';\n");
                fs.writeFileSync(owner, "import { assetProbe } from './acceptance-probe.js';\n" + original.replace('card.className =', 'card.dataset.assetProbe = assetProbe;\n            card.className ='));
                await until(() => fs.existsSync(emittedAdded) && fs.readFileSync(emittedAdded,'utf8').includes('new-module'));
                await verify(ReportDesignerPanel, '_getHtml', 'etlsql.openReportDesigner', 'designer', 'new-module');
                const goodHash = hash(fs.readFileSync(emittedAdded));
                fs.writeFileSync(added, 'export const assetProbe: string = 21;\n');
                await until(() => /not assignable/.test(logs['watch:assets']));
                assert.equal(hash(fs.readFileSync(emittedAdded)), goodHash);
                await verify(ReportDesignerPanel, '_getHtml', 'etlsql.openReportDesigner', 'designer', 'new-module');
                result.compilerFailurePreservedLastGood = true;
                fs.writeFileSync(added, "export const assetProbe = 'recovered';\n");
                await until(() => fs.readFileSync(emittedAdded,'utf8').includes('recovered'));
                await verify(ReportDesignerPanel, '_getHtml', 'etlsql.openReportDesigner', 'designer', 'recovered');
            } finally {
                fs.writeFileSync(owner, original);
                if (fs.existsSync(added)) fs.unlinkSync(added);
                try { await until(() => !fs.existsSync(emittedAdded) && !fs.readFileSync(emitted,'utf8').includes('assetProbe')); }
                finally {
                    for (const child of watchers) {
                        if (process.platform === 'win32')
                            await new Promise(resolve => spawn(path.join(process.env.SystemRoot,'System32/taskkill.exe'), ['/pid',String(child.pid),'/t','/f'],{windowsHide:true}).on('close',resolve));
                        else child.kill('SIGTERM');
                    }
                    result.watchLogs = logs;
                }
            }
        }
        assert.deepEqual(result.errors, []);
        result.passed = true;
    } catch (error) {
        result.error = error.stack;
        throw error;
    } finally {
        result.finishedAt = new Date().toISOString();
        fs.writeFileSync(reportPath, JSON.stringify(result, null, 2));
    }
};
