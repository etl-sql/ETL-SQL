import assert from 'node:assert/strict';
import { createStudioPipelineView } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-pipeline-view.js';
import { createStudioDocumentContext, createStudioState } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-state.js';

function element() {
    return {
        innerHTML: '',
        querySelector: () => null,
        querySelectorAll: () => [],
        addEventListener() {},
        removeEventListener() {},
    };
}

function fixture(lineEnding = '\n') {
    const content = ['first statement', 'second statement'].join(lineEnding);
    const doc = { id: 'pipeline', name: 'pipeline.etlsql', content };
    const context = createStudioDocumentContext();
    const canvas = element();
    const palette = element();
    const container = element();
    container.querySelector = selector => ({
        '[data-dag-canvas]': canvas,
        '[data-pipeline-editor]': palette,
    })[selector] ?? null;
    const requests = [];
    const state = createStudioState({ documents: [doc] });
    const current = { buffer: content.replaceAll('\r\n', '\n'), doc };
    const view = createStudioPipelineView({
        state,
        canvasContainer: container,
        apiBase: '',
        getActiveDoc: () => current.doc,
        activeScriptText: () => current.buffer,
        documentContext: () => context,
        authFetch: (_url, init) => new Promise(resolve => requests.push({ init, resolve })),
    });
    function respond(taskBody = async () => ({ tasks: [] })) {
        requests[0].resolve({ ok: true, json: async () => ({ parsed: true, dag: { nodes: [], edges: [] } }) });
        requests[1].resolve({ ok: true, json: taskBody });
        requests[2].resolve({ ok: true, json: async () => ({ designState: { connections: [] } }) });
    }
    return { doc, context, state, current, container, palette, requests, view, respond };
}

const previousDocument = globalThis.document;
globalThis.document = { body: { classList: { contains: () => false } } };
try {
    for (const lineEnding of ['\n', '\r\n']) {
        const test = fixture(lineEnding);
        const originalContent = test.doc.content;
        const rendered = test.view.renderPipelineDag(test.doc, test.current.buffer);
        test.respond();
        await rendered;
        assert.equal(test.state.dagDocumentId, test.doc.id, `Opening ${JSON.stringify(lineEnding)} must paint the pipeline.`);
        assert.equal(test.context.lastValidDag?.script, test.current.buffer);
        assert.match(test.container.innerHTML, /Engine projection/);
        assert.match(test.palette.innerHTML, /data-task-palette/);
        assert.equal(test.doc.content, originalContent, 'Projecting does not rewrite the file line endings.');
        for (const request of test.requests) {
            assert.equal(JSON.parse(request.init.body).script, test.current.buffer);
        }
        test.view.disposePipelineDag();
    }

    // The document can still contain the prior text while the editor has already changed.
    const edited = fixture();
    const oldBuffer = edited.current.buffer;
    const oldResponse = edited.view.renderPipelineDag(edited.doc, oldBuffer);
    edited.current.buffer += '\nnew statement';
    edited.respond();
    await oldResponse;
    assert.equal(edited.context.lastValidDag, null, 'A response for the prior editor buffer must be discarded.');
    assert.equal(edited.state.dagDocumentId, null);

    // JSON body reads are asynchronous too: an edit while reading tasks invalidates the projection.
    const reading = fixture();
    let releaseTasks;
    let readingTasks;
    const tasksStarted = new Promise(resolve => { readingTasks = resolve; });
    const inFlight = reading.view.renderPipelineDag(reading.doc, reading.current.buffer);
    reading.respond(() => {
        readingTasks();
        return new Promise(resolve => { releaseTasks = resolve; });
    });
    await tasksStarted;
    reading.current.buffer += '\nnew statement';
    releaseTasks({ tasks: [] });
    await inFlight;
    assert.equal(reading.context.lastValidDag, null, 'Edits during response body reads must also invalidate the projection.');
    assert.equal(reading.state.dagDocumentId, null);
} finally {
    if (previousDocument === undefined) delete globalThis.document;
    else globalThis.document = previousDocument;
}
console.log('Studio pipeline view: LF/CRLF opening and stale editor/body-read responses passed.');
