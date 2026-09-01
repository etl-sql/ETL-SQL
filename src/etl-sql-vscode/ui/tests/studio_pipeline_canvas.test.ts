/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, test, expect, beforeEach } from 'vitest';
import { JSDOM } from 'jsdom';

/**
 * The editable layer over the pipeline execution map.
 *
 * The host owns every byte of the script; this module only decides what the author asked for. So
 * these tests assert on the intent it reports — which task, moved after which — rather than on any
 * SQL, because the moment this module starts assembling ETL-SQL is the moment the round-trip
 * guarantee stops being the host's to keep.
 */
describe('Pipeline task editing layer', () => {
    let dom: JSDOM;
    let document: Document;
    let attach: any;
    let host: HTMLElement;
    let canvas: HTMLElement;

    const tasks = [
        { id: 'load_orders', kind: 'execution', connection: 'staging_db', body: 'SELECT 1;', line: 7 },
        { id: 'archive_orders', kind: 'execution', connection: 'staging_db', body: 'SELECT 2;', line: 15 },
    ];

    /** A card the projection drew: labelled cards carry the section label, others do not. */
    const card = (key: string | null) => {
        const element = document.createElement('div');
        element.className = 'etlsql-dag-card';
        if (key) element.dataset.taskKey = key;
        canvas.appendChild(element);
        return element;
    };

    const dragEvent = (type: string, data: Record<string, string> = {}) => {
        const event: any = new dom.window.Event(type, { bubbles: true, cancelable: true });
        event.dataTransfer = {
            effectAllowed: '',
            dropEffect: '',
            types: Object.keys(data),
            setData: (format: string, value: string) => { data[format] = value; },
            getData: (format: string) => data[format] ?? '',
        };
        return event;
    };

    beforeEach(async () => {
        dom = new JSDOM('<!DOCTYPE html><html><body><div id="host"></div><div id="canvas"></div></body></html>');
        document = dom.window.document;
        (globalThis as any).window = dom.window;
        (globalThis as any).document = document;
        host = document.getElementById('host')!;
        canvas = document.getElementById('canvas')!;
        attach = (await import('../../media/designer/studio-pipeline-canvas.js')).attachPipelineTaskEditing;
    });

    test('only labelled cards become editable', () => {
        const labelled = card('load_orders');
        const projectionOnly = card(null);

        attach(host, canvas, { tasks });

        expect(labelled.classList.contains('is-editable-task')).toBe(true);
        expect((labelled as HTMLElement).draggable).toBe(true);
        expect(projectionOnly.classList.contains('is-editable-task')).toBe(false);
        expect((projectionOnly as HTMLElement).draggable).toBe(false);
    });

    test('dropping one task onto another asks for "run after this one"', () => {
        const source = card('load_orders');
        const target = card('archive_orders');
        const moves: any[] = [];

        attach(host, canvas, { tasks, onMove: (move: any) => { moves.push(move); } });

        source.dispatchEvent(dragEvent('dragstart'));
        target.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));

        expect(moves).toEqual([{ id: 'load_orders', after: 'archive_orders' }]);
    });

    test('a task dropped onto itself is not an edit', () => {
        const only = card('load_orders');
        const moves: any[] = [];

        attach(host, canvas, { tasks, onMove: (move: any) => { moves.push(move); } });

        only.dispatchEvent(dragEvent('dragstart'));
        only.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));

        expect(moves).toEqual([]);
    });

    test('selection is by label, and matches whatever the projection numbered the node', () => {
        const first = card('archive_orders');
        const selections: any[] = [];

        attach(host, canvas, {
            tasks,
            selectedId: 'ARCHIVE_ORDERS', // labels are matched case-insensitively, as the parser does
            onSelect: (id: string) => { selections.push(id); },
        });

        expect(first.classList.contains('is-selected-task')).toBe(true);
        first.dispatchEvent(new dom.window.Event('click', { bubbles: true }));
        expect(selections).toEqual(['archive_orders']);
    });

    test('the inspector opens the editor and deletes, both by label', () => {
        card('load_orders');
        const edits: any[] = [];
        const removals: any[] = [];

        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            onEdit: (edit: any) => { edits.push(edit); },
            onRemove: (removal: any) => { removals.push(removal); },
        });

        // Editing happens in the task editor, not in fields on the canvas: the editor is where the
        // query workbench lives, and two places to change a body is how they drift apart.
        expect(host.querySelector('[data-task-field="label"]')).toBeNull();

        (host.querySelector('[data-task-edit]') as HTMLElement).click();
        expect(edits).toEqual([{ id: 'load_orders' }]);

        (host.querySelector('[data-task-remove]') as HTMLElement).click();
        expect(removals).toEqual([{ id: 'load_orders' }]);
    });

    test('the palette offers every proven kind, and adding places it after the selected task', () => {
        const adds: any[] = [];

        attach(host, canvas, { tasks, selectedId: 'load_orders', onAdd: (add: any) => { adds.push(add); } });

        const kinds = [...host.querySelectorAll('[data-task-kind]')].map(chip => (chip as HTMLElement).dataset.taskKind);
        expect(kinds).toEqual(['execution', 'fileoperation', 'validation', 'notification']);

        // Nothing in the palette is a dead control: each kind has passed its emission gate, so none
        // of them is rendered disabled.
        expect(host.querySelectorAll('[data-task-kind][disabled]').length).toBe(0);

        (host.querySelector('[data-task-kind="validation"]') as HTMLElement).click();
        expect(adds).toEqual([{ kind: 'validation', after: 'load_orders' }]);
    });

    test('a task label is escaped, never rendered as markup', () => {
        card('x');
        attach(host, canvas, {
            tasks: [{ id: '<img src=x onerror=alert(1)>', kind: 'execution', connection: 'db', body: '', line: 1 }],
            selectedId: '<img src=x onerror=alert(1)>',
        });

        expect(host.querySelector('img')).toBeNull();
        expect(host.innerHTML).toContain('&lt;img src=x onerror=alert(1)&gt;');
    });

    test('dispose removes the handlers it added', () => {
        const target = card('archive_orders');
        const moves: any[] = [];
        const editor = attach(host, canvas, { tasks, onMove: (move: any) => { moves.push(move); } });

        editor.dispose();
        target.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));

        expect(moves).toEqual([]);
    });
});
