import test from 'node:test';
import assert from 'node:assert/strict';

let onNotify = () => {};
globalThis.ETLSQLFeedback = { notify: (message, opts) => onNotify(message, opts), confirm: async () => true, prompt: async () => null };
const { createStudioFileCommands } = await import('../../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-file-commands.js');

function deferred() {
    let resolve;
    const promise = new Promise(complete => { resolve = complete; });
    return { promise, resolve };
}

function fixture(onSave) {
    const doc = { path: 'item.etlsql', name: 'item.etlsql', content: 'SELECT 1;', isDirty: true, version: 1 };
    let active = doc;
    const removed = [];
    const commands = createStudioFileCommands({
        getActiveDoc: () => active,
        state: { editorInstance: { getValue: () => active.content } },
        opts: { onSave },
        leaseLifecycle: { removeDraft: document => removed.push(document) },
        renderTabs() {},
    });
    return { doc, removed, commands, activate: document => { active = document; } };
}

test('pending save preserves newer text and its draft while applying version metadata', async () => {
    const pending = deferred();
    let submitted;
    const { doc, removed, commands } = fixture(content => { submitted = content; return pending.promise; });
    const save = commands.handleSave();
    doc.content = 'SELECT 2;';
    pending.resolve({ content: 'SELECT 1;', version: 2 });
    assert.equal(await save, true);
    assert.equal(submitted, 'SELECT 1;');
    assert.equal(doc.content, 'SELECT 2;');
    assert.equal(doc.isDirty, true);
    assert.equal(doc.version, 2);
    assert.deepEqual(removed, []);
});

test('an unchanged successful save clears dirty state and removes its draft', async () => {
    const { doc, removed, commands } = fixture(async () => ({ version: 2 }));
    assert.equal(await commands.handleSave(), true);
    assert.equal(doc.isDirty, false);
    assert.deepEqual(removed, [doc]);
});

test('switching tabs does not apply save completion to the newly active document', async () => {
    const pending = deferred();
    const { doc, removed, commands, activate } = fixture(() => pending.promise);
    const save = commands.handleSave();
    const other = { path: 'other.etlsql', content: 'SELECT 3;', isDirty: true };
    activate(other);
    pending.resolve({ version: 2 });
    await save;
    assert.equal(doc.isDirty, false);
    assert.equal(other.isDirty, true);
    assert.equal(other.content, 'SELECT 3;');
    assert.deepEqual(removed, [doc]);
});

test('multiple saves are serialized and the later save receives the current version', async () => {
    const first = deferred();
    const second = deferred();
    const secondStarted = deferred();
    const submissions = [];
    const { doc, removed, commands } = fixture((content, path, document) => {
        submissions.push({ content, version: document.version });
        if (submissions.length === 1) return first.promise;
        secondStarted.resolve();
        return second.promise;
    });
    const firstSave = commands.handleSave();
    doc.content = 'SELECT 2;';
    const secondSave = commands.handleSave();
    assert.equal(submissions.length, 1);
    first.resolve({ version: 2 });
    await firstSave;
    await secondStarted.promise;
    assert.deepEqual(submissions, [{ content: 'SELECT 1;', version: 1 }, { content: 'SELECT 2;', version: 2 }]);
    assert.equal(doc.isDirty, true);
    second.resolve({ version: 3 });
    await secondSave;
    assert.equal(doc.content, 'SELECT 2;');
    assert.equal(doc.isDirty, false);
    assert.equal(doc.version, 3);
    assert.deepEqual(removed, [doc]);
});

test('save-time server sealing resets editor undo history and saves sealed text', async () => {
    const doc = {
        path: 'item.etlsql',
        name: 'item.etlsql',
        content: "USE PASSWORD = 'secret';\nCREATE CONNECTION db AS MSSQL(PASSWORD = 'pw');",
        isDirty: true,
        version: 1,
        editorState: { old: true },
    };
    const sealed = "USE PASSWORD PROMPT;\nCREATE CONNECTION db AS MSSQL(PASSWORD = 'ENC:sealed');";
    const resetCalls = [];
    const savedPayloads = [];
    const commands = createStudioFileCommands({
        apiBase: 'http://localhost',
        authFetch: async () => new Response(JSON.stringify({
            sealedScript: sealed,
            requiresPassword: false,
            wasSealed: true,
            allowsPlaintextSecrets: false,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } }),
        getActiveDoc: () => doc,
        state: {
            editorInstance: {
                getValue: () => doc.content,
                setValue: () => { throw new Error('Should use resetDocument to clear undo stack'); },
                resetDocument: text => resetCalls.push(text),
                getState: () => ({ reset: true }),
            },
        },
        opts: {
            onSave: async content => {
                savedPayloads.push(content);
                return { version: 2 };
            },
        },
        leaseLifecycle: { removeDraft() {} },
        renderTabs() {},
    });

    assert.equal(await commands.handleSave(), true);
    assert.deepEqual(resetCalls, [sealed]);
    assert.equal(doc.content, sealed);
    assert.deepEqual(doc.editorState, { reset: true });
    assert.deepEqual(savedPayloads, [sealed]);
});

test('save-time policy honors server ALLOW_PLAINTEXT_SECRETS without rewriting', async () => {
    const warnings = [];
    const prevNotify = onNotify;
    onNotify = (message, opts) => warnings.push({ message, opts });
    try {
        const raw = "SET ALLOW_PLAINTEXT_SECRETS = ON;\nCREATE CONNECTION db AS MSSQL(PASSWORD = 'pw');";
        const doc = { path: 'item.etlsql', name: 'item.etlsql', content: raw, isDirty: true, version: 1 };
        const savedPayloads = [];
        const commands = createStudioFileCommands({
            apiBase: 'http://localhost',
            authFetch: async () => new Response(JSON.stringify({
                sealedScript: raw,
                requiresPassword: false,
                wasSealed: false,
                allowsPlaintextSecrets: true,
            }), { status: 200, headers: { 'Content-Type': 'application/json' } }),
            getActiveDoc: () => doc,
            state: { editorInstance: { getValue: () => doc.content, resetDocument() {} } },
            opts: {
                onSave: async content => {
                    savedPayloads.push(content);
                    return { version: 2 };
                },
            },
            leaseLifecycle: { removeDraft() {} },
            renderTabs() {},
        });

        assert.equal(await commands.handleSave(), true);
        assert.deepEqual(savedPayloads, [raw]);
        assert.ok(warnings.some(w => w.opts?.title === 'Plaintext Secrets Allowed'));
    } finally {
        onNotify = prevNotify;
    }
});
