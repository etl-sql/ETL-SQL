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
        { id: 'load_orders', connection: 'staging_db', body: 'SELECT 1;', line: 7 },
        { id: 'archive_orders', connection: 'staging_db', body: 'SELECT 2;', line: 15 },
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

    test('the inspector edits the selected task by label', () => {
        card('load_orders');
        const updates: any[] = [];
        const removals: any[] = [];

        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            onUpdate: (update: any) => { updates.push(update); },
            onRemove: (removal: any) => { removals.push(removal); },
        });

        (host.querySelector('[data-task-field="label"]') as HTMLInputElement).value = 'load_orders_v2';
        (host.querySelector('[data-task-save]') as HTMLElement).click();
        expect(updates).toEqual([{ id: 'load_orders', newId: 'load_orders_v2', connection: 'staging_db' }]);

        (host.querySelector('[data-task-remove]') as HTMLElement).click();
        expect(removals).toEqual([{ id: 'load_orders' }]);
    });

    test('adding a task places it after the selected one', () => {
        const adds: any[] = [];

        attach(host, canvas, { tasks, selectedId: 'load_orders', onAdd: (add: any) => { adds.push(add); } });
        (host.querySelector('[data-task-add]') as HTMLElement).click();

        expect(adds).toEqual([{ after: 'load_orders' }]);
    });

    test('a task label is escaped, never rendered as markup', () => {
        card('x');
        attach(host, canvas, {
            tasks: [{ id: '<img src=x onerror=alert(1)>', connection: 'db', body: '', line: 1 }],
            selectedId: '<img src=x onerror=alert(1)>',
        });

        expect(host.querySelector('img')).toBeNull();
        expect((host.querySelector('[data-task-field="label"]') as HTMLInputElement).value)
            .toBe('<img src=x onerror=alert(1)>');
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
