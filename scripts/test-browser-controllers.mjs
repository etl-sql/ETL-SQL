import assert from 'node:assert/strict';
import { createDesignerHistory } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-history.js';
import { createDesignerScriptSync } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-script-sync.js';
import { createWorkbenchExecution } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/workbench-execution.js';

// Each designer owns its history; replacing pages during undo must remain visible to its peers.
function historyFixture() {
    let sequence = 0;
    const context = {
        state: { pages: [{ id: 'p1', visuals: [{ id: 'v1', type: 'BAR', name: 'Sales', gridRow: 1, options: {} }] }], datasets: [] },
        isDirty: false, selVisualId: 'v1', selVisualIds: new Set(['v1']), renders: 0,
        curPage: () => context.state.pages[0],
        findVis: id => context.curPage().visuals.find(v => v.id === id),
        uid: () => `v_copy${++sequence}`,
        selectVisual: id => { context.selVisualId = id; },
        renderAll: () => { context.renders++; },
    };
    return { context, controller: createDesignerHistory(context) };
}
const first = historyFixture();
const second = historyFixture();
first.controller.copySelectedVisuals();
first.controller.pasteVisuals();
assert.equal(first.context.curPage().visuals.length, 2);
assert.equal(first.context.selVisualId, 'v_copy1');
assert.equal(first.context.isDirty, true);
second.controller.undoCanvasState();
assert.equal(second.context.renders, 0, 'History cannot leak between designer instances.');
first.controller.undoCanvasState();
assert.equal(first.context.curPage().visuals.length, 1);
first.controller.redoCanvasState();
assert.equal(first.context.curPage().visuals.length, 2);
first.controller.undoCanvasState();
first.controller.duplicateVisual('v1');
first.controller.redoCanvasState();
assert.equal(first.context.curPage().visuals.length, 2, 'A new edit clears the old redo branch.');
assert.equal(first.context.curPage().visuals[1].id, 'v_copy2');

// Older parses and responses arriving after disposal/invalidation must never repaint the canvas.
const requests = [];
let renders = 0;
let diagnostic = null;
const parseContext = {
    state: { pages: [{ id: 'initial' }], datasets: [] },
    isSplitActive: true, pageIdx: 4, selVisualId: 'old',
    apiJson: (_url, _method, body) => new Promise((resolve, reject) => requests.push({ body, resolve, reject })),
    setScriptDiagnosticBadge: value => { diagnostic = value; },
    renderAll: () => { renders++; },
    errorText: error => error.message,
};
const sync = createDesignerScriptSync(parseContext);
const older = sync.applyScriptText('older');
const newer = sync.applyScriptText('newer');
requests[1].resolve({ designState: { pages: [{ id: 'newer' }] } });
assert.equal((await newer).applied, true);
requests[0].resolve({ designState: { pages: [{ id: 'older' }] } });
assert.deepEqual(await older, { applied: false, stale: true });
assert.equal(parseContext.state.pages[0].id, 'newer');
assert.equal(parseContext.pageIdx, 0);
assert.equal(parseContext.selVisualId, null);
assert.equal(renders, 1);
const invalidated = sync.applyScriptText('closed document');
sync.invalidateScriptApply();
requests[2].reject(new Error('Late parse failure'));
assert.deepEqual(await invalidated, { applied: false, stale: true });
assert.equal(diagnostic, null);
assert.equal(renders, 1);

// A cancellation reaches the exact signal sent to the host and releases the running UI state.
const trace = [];
const runButton = { disabled: false };
const classes = new Set();
let selected = '';
let capturedSignal;
let capturedSelection;
let stops = 0;
const runContext = {
    root: { classList: { toggle: (name, enabled) => enabled ? classes.add(name) : classes.delete(name) } },
    container: { querySelector: () => runButton },
    editor: { getValue: () => 'SELECT 1; SELECT 2;', getSelection: () => selected, getCurrentStatement: () => '' },
    resultsPanel: { replay: events => trace.push(...events), startElapsed() {}, stopElapsed: () => { stops++; } },
    opts: { onRun: ({ signal, selection }) => {
        capturedSignal = signal;
        capturedSelection = selection;
        return new Promise((_resolve, reject) => signal.addEventListener('abort', () => reject(new DOMException('Stopped', 'AbortError'))));
    } },
    runAbort: null,
};
const execution = createWorkbenchExecution(runContext);
await execution.run('selection');
assert.equal(capturedSignal, undefined, 'An empty selection must not run the full file.');
assert.ok(trace.some(event => event.text?.includes('No query or statement selected')));
selected = 'SELECT 2;';
const running = execution.run('selection');
assert.equal(capturedSelection, selected);
assert.equal(runButton.disabled, true);
assert.equal(capturedSignal.aborted, false);
execution.cancelRun();
await running;
assert.equal(capturedSignal.aborted, true);
assert.equal(runButton.disabled, false);
assert.equal(classes.has('is-running'), false);
assert.equal(stops, 1);
assert.ok(trace.some(event => event.status === 'Cancelled (Unconfirmed)'));
console.log('Browser controllers: isolated undo, stale parse/invalidation, explicit selection, and cancellation passed.');