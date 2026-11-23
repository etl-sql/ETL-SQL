import assert from 'node:assert/strict';
import fs from 'node:fs';
import {
    encryptClientPassword,
    validatePathSecurity,
    createConnectionWizard,
} from '../../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/connection-wizard.js';

// ── 1. validatePathSecurity Zero-Trust Guardrails ───────────────────────────
// Valid paths
assert.equal(validatePathSecurity('data/sales.csv'), null);
assert.equal(validatePathSecurity('input/reports/2026/q1.xlsx'), null);
assert.equal(validatePathSecurity('feed.json'), null);

// Falsy or non-string inputs return null (no violation)
assert.equal(validatePathSecurity(''), null);
assert.equal(validatePathSecurity('   '), null);
assert.equal(validatePathSecurity(null), null);
assert.equal(validatePathSecurity(undefined), null);
assert.equal(validatePathSecurity(123), null);

// Guardrail: script files forbidden
for (const scriptPath of ['query.sql', 'etl.etlsql', 'report.rptsql', 'DATA/SCRIPT.SQL']) {
    const violation = validatePathSecurity(scriptPath);
    assert.ok(violation, `Expected script guardrail violation for ${scriptPath}`);
    assert.ok(violation.includes('script files is forbidden'));
}

// Guardrail: directory traversal forbidden
for (const traversalPath of ['../secret.csv', 'data/../../etc', 'a/..']) {
    const violation = validatePathSecurity(traversalPath);
    assert.ok(violation, `Expected traversal guardrail violation for ${traversalPath}`);
    assert.ok(violation.includes('directory traversal'));
}

// Guardrail: absolute system paths forbidden
for (const absPath of ['/var/log/app.log', '\\\\server\\share\\file.csv', 'C:\\data\\sales.csv', 'D:/files/input.csv']) {
    const violation = validatePathSecurity(absPath);
    assert.ok(violation, `Expected absolute path guardrail violation for ${absPath}`);
    assert.ok(violation.includes('Absolute system paths'));
}

// Guardrail: system directories forbidden
for (const sysPath of ['windows/system32/cmd.exe', 'etc/passwd', 'root/key', 'bin/sh', '.git/config', '.ssh/id_rsa', 'tmp/file.csv']) {
    const violation = validatePathSecurity(sysPath);
    assert.ok(violation, `Expected system directory guardrail violation for ${sysPath}`);
    assert.ok(violation.includes('system directories'));
}

// ── 2. encryptClientPassword WebCrypto AES-GCM (v2) ───────────────────────────
// Empty / missing credentials return null
assert.equal(await encryptClientPassword(null, 'passphrase'), null);
assert.equal(await encryptClientPassword('password', null), null);
assert.equal(await encryptClientPassword('', 'passphrase'), null);
assert.equal(await encryptClientPassword('password', ''), null);

// Node environment WebCrypto compatibility
if (globalThis.crypto?.subtle) {
    globalThis.window = { crypto: globalThis.crypto };
    const encrypted = await encryptClientPassword('MySuperSecretDbPassword!2026', 'MyClientPassphrase');
    assert.ok(encrypted, 'Client encryption returned a token');
    assert.ok(encrypted.startsWith('ENC:'), 'Token has ENC: prefix');
    assert.ok(encrypted.length > 30, 'Token has substantial length for v2 payload');
}

// ── 3. createConnectionWizard export & mounting contract ────────────────────
assert.equal(typeof createConnectionWizard, 'function');

const appendedElements = [];
const mockHost = {
    appendChild(el) { appendedElements.push(el); }
};

const mockDoc = {
    body: mockHost,
    createElement(tag) {
        return {
            tagName: tag.toUpperCase(),
            setAttribute() {},
            getAttribute() { return null; },
            appendChild() {},
            remove() {},
            querySelector() { return null; },
            querySelectorAll() { return []; },
            style: {},
            classList: { add() {}, remove() {}, toggle() {} },
            addEventListener() {},
            dispatchEvent() {},
            innerHTML: ''
        };
    }
};

globalThis.document = mockDoc;

const wizard = createConnectionWizard({
    host: mockHost,
    initialConnector: 'POSTGRES'
});

assert.ok(wizard);
assert.equal(typeof wizard.open, 'function');
assert.equal(typeof wizard.close, 'function');
assert.equal(typeof wizard.getGeneratedSql, 'function');
assert.equal(typeof wizard.validatePathSecurity, 'function');

const generatedSql = wizard.getGeneratedSql();
assert.ok(generatedSql.includes('CREATE CONNECTION'), 'Generated SQL has CREATE CONNECTION clause');
assert.ok(generatedSql.includes('POSTGRES'), 'Generated SQL uses specified connector');

// ── 4. Responsive narrow-panel layout & spacing contract ────────────────────
const overlayEl = appendedElements[0];
assert.ok(overlayEl && overlayEl.innerHTML, 'Wizard rendered modal markup');
assert.ok(overlayEl.innerHTML.includes('etlsql-cw-cat-list'), 'Categories wrapped in etlsql-cw-cat-list for narrow pill layout');
assert.ok(overlayEl.innerHTML.includes('etlsql-cw-connector-group'), 'Connector picker uses etlsql-cw-connector-group class');
const previewIdx = overlayEl.innerHTML.indexOf('class="etlsql-cw-preview-pane"');
const footerIdx = overlayEl.innerHTML.indexOf('class="etlsql-cw-footer-actions"');
const bodyCloseIdx = overlayEl.innerHTML.lastIndexOf('</div>', footerIdx);
assert.ok(previewIdx > 0 && footerIdx > previewIdx && bodyCloseIdx > previewIdx,
    'Footer actions render after etlsql-cw-body so Insert/Cancel stay pinned');

const designerCss = fs.readFileSync(
    new URL('../../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css', import.meta.url),
    'utf8'
);
assert.match(designerCss, /\.etlsql-cw-sql-box\s*\{[^}]*flex:\s*0 0 auto;[^}]*min-height:\s*\d+px;/s,
    'SQL preview box must not shrink to a single line');
assert.match(designerCss, /@media\s*\(max-width:\s*960px\)\s*\{[^@]*\.etlsql-cw-body\s*\{[^}]*display:\s*flex;[^}]*flex-direction:\s*column;/s,
    'Narrow viewport must stack etlsql-cw-body as a single scroll column');
assert.match(designerCss, /@media\s*\(max-width:\s*960px\)\s*\{[^@]*\.etlsql-cw-cat-list\s*\{[^}]*flex-direction:\s*row;[^}]*flex-wrap:\s*wrap;/s,
    'Narrow viewport must render categories as a wrapping pill row');
assert.match(designerCss, /@media\s*\(max-width:\s*960px\)\s*\{[^@]*\.etlsql-cw-type-list\s*\{[^}]*display:\s*grid;[^}]*max-height:\s*\d+px;/s,
    'Narrow viewport must show connector list in a bounded grid');

wizard.close();

console.log('OK test-connection-wizard.mjs: all checks passed.');
