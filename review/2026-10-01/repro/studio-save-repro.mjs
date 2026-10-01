import test from 'node:test';
import assert from 'node:assert/strict';

globalThis.ETLSQLFeedback = { notify() {}, confirm: async () => true, prompt: async () => null };
const { createStudioFileCommands } = await import('../../../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-file-commands.js');

test('editing while Save is pending must preserve the dirty flag and recovery draft', async () => {
    const doc = { path: 'review.etlsql', name: 'review.etlsql', content: 'SELECT 1;', isDirty: true };
    let editorText = doc.content;
    let resolveSave;
    let savedText;
    let draftRemoved = false;
    const pendingSave = new Promise(resolve => { resolveSave = resolve; });
    const commands = createStudioFileCommands({
        getActiveDoc: () => doc,
        state: { editorInstance: { getValue: () => editorText } },
        opts: { onSave: async content => { savedText = content; return pendingSave; } },
        leaseLifecycle: { removeDraft: () => { draftRemoved = true; } },
        renderTabs() {},
    });
    const save = commands.handleSave();
    editorText = 'SELECT 2;';
    doc.content = editorText;
    doc.isDirty = true;
    resolveSave({ sourceRevision: 'saved-revision' });
    await save;
    assert.equal(savedText, 'SELECT 1;');
    assert.equal(doc.content, 'SELECT 2;');
    assert.deepEqual({ dirty: doc.isDirty, draftRemoved }, { dirty: true, draftRemoved: false },
        'the newer edit was never saved and its recovery draft must survive');
});
