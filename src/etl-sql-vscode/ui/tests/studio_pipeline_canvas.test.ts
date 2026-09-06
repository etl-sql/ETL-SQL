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
        expect(kinds).toEqual([
            'execution', 'validation', 'notification', 'throw', 'waitfor',
            'if', 'foreach', 'for', 'while', 'parallel', 'transaction', 'break', 'continue',
            'copyfile', 'movefile', 'renamefile', 'deletefile',
            'createdirectory', 'copydirectory', 'movedirectory', 'renamedirectory', 'deletedirectorycontents', 'deletedirectory',
        ]);

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

    test('dragging a card body reorders, dragging its connector declares a dependency', () => {
        const source = card('load_orders');
        const target = card('archive_orders');
        const moves: any[] = [];
        const connects: any[] = [];

        attach(host, canvas, {
            tasks,
            onMove: (move: any) => { moves.push(move); },
            onConnect: (connect: any) => { connects.push(connect); },
        });

        // The card body: a move. Order in the script is the dependency.
        source.dispatchEvent(dragEvent('dragstart'));
        target.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));
        expect(moves).toEqual([{ id: 'load_orders', after: 'archive_orders' }]);
        expect(connects).toEqual([]);

        // The connector handle: a declaration. Two gestures because they mean different things —
        // one relocates a statement, the other writes down what has to finish first.
        const handle = source.querySelector('[data-task-connector]') as HTMLElement;
        expect(handle).not.toBeNull();
        handle.dispatchEvent(dragEvent('dragstart'));
        target.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));
        expect(connects).toEqual([{ from: 'load_orders', to: 'archive_orders' }]);
        expect(moves).toHaveLength(1);
    });

    test('the inspector lists what a task waits for, and each one can be removed', () => {
        card('archive_orders');
        const disconnects: any[] = [];

        attach(host, canvas, {
            tasks: [
                tasks[0],
                { ...tasks[1], dependsOn: ['load_orders', 'fetch_rates'] },
            ],
            selectedId: 'archive_orders',
            onDisconnect: (edge: any) => { disconnects.push(edge); },
        });

        const chips = [...host.querySelectorAll('[data-task-disconnect]')].map(
            button => (button as HTMLElement).dataset.taskDisconnect);
        expect(chips).toEqual(['load_orders', 'fetch_rates']);

        // Several incoming edges read as a join — waits for all of them — never as concurrency.
        // Scoped to the dependency list rather than the whole host, because the palette legitimately
        // offers a PARALLEL container: the claim is about what a join means, not about the word.
        const deps = host.querySelector('.etlsql-studio-pipeline-deps') as HTMLElement;
        expect(deps.textContent).toContain('Waits for all 2');
        expect(deps.textContent).not.toMatch(/parallel|at the same time|concurrent/i);

        (host.querySelector('[data-task-disconnect="fetch_rates"]') as HTMLElement).click();
        expect(disconnects).toEqual([{ from: 'fetch_rates', to: 'archive_orders' }]);
    });

    test('a task with no declared dependencies says it simply runs in script order', () => {
        card('load_orders');
        attach(host, canvas, { tasks, selectedId: 'load_orders' });

        expect(host.querySelectorAll('[data-task-disconnect]').length).toBe(0);
        expect(host.textContent).toContain('Runs in script order');
    });

    test('an edge reports its condition, and choosing another one asks the host to rewrite it', () => {
        card('archive_orders');
        const edges: any[] = [];

        attach(host, canvas, {
            tasks: [
                tasks[0],
                { ...tasks[1], dependsOn: [{ id: 'load_orders', condition: 'onfailure' }] },
            ],
            selectedId: 'archive_orders',
            onSetEdge: (edge: any) => { edges.push(edge); },
        });

        const picker = host.querySelector('[data-task-edge="load_orders"]') as HTMLSelectElement;
        expect(picker.value).toBe('onfailure');

        picker.value = 'onsuccess';
        picker.dispatchEvent(new dom.window.Event('change', { bubbles: true }));

        expect(edges).toEqual([{ from: 'load_orders', to: 'archive_orders', edge: 'onsuccess' }]);
    });

    test('choosing "When…" asks for nothing until the expression is typed', () => {
        card('archive_orders');
        const edges: any[] = [];

        attach(host, canvas, {
            tasks: [
                tasks[0],
                { ...tasks[1], dependsOn: [{ id: 'load_orders', condition: 'always' }] },
            ],
            selectedId: 'archive_orders',
            onSetEdge: (edge: any) => { edges.push(edge); },
        });

        const field = host.querySelector('[data-task-expression="load_orders"]') as HTMLInputElement;
        expect(field.hidden).toBe(true);

        const picker = host.querySelector('[data-task-edge="load_orders"]') as HTMLSelectElement;
        picker.value = 'expression';
        picker.dispatchEvent(new dom.window.Event('change', { bubbles: true }));

        // The edge is not describable yet. Writing a gate on an empty condition would be a change
        // the author did not make.
        expect(field.hidden).toBe(false);
        expect(edges).toEqual([]);

        field.value = '@@ROWCOUNT > 0';
        field.dispatchEvent(new dom.window.Event('blur', { bubbles: true }));

        expect(edges).toEqual([
            { from: 'load_orders', to: 'archive_orders', edge: 'expression', expression: '@@ROWCOUNT > 0' },
        ]);
    });

    test('an unchanged expression is not resent every time the field loses focus', () => {
        card('archive_orders');
        const edges: any[] = [];

        attach(host, canvas, {
            tasks: [
                tasks[0],
                {
                    ...tasks[1],
                    dependsOn: [{ id: 'load_orders', condition: 'expression', expression: '@@ROWCOUNT > 0' }],
                },
            ],
            selectedId: 'archive_orders',
            onSetEdge: (edge: any) => { edges.push(edge); },
        });

        const field = host.querySelector('[data-task-expression="load_orders"]') as HTMLInputElement;
        expect(field.hidden).toBe(false);
        expect(field.value).toBe('@@ROWCOUNT > 0');

        field.dispatchEvent(new dom.window.Event('blur', { bubbles: true }));
        expect(edges).toEqual([]);
    });

    test('a dependency reported as a bare label still reads as plain precedence', () => {
        card('archive_orders');

        attach(host, canvas, {
            tasks: [tasks[0], { ...tasks[1], dependsOn: ['load_orders'] }],
            selectedId: 'archive_orders',
        });

        // An older host, or a response cached before conditional edges existed, reports a string.
        // Guessing a condition for it would put a gate in the script nobody asked for.
        const picker = host.querySelector('[data-task-edge="load_orders"]') as HTMLSelectElement;
        expect(picker.value).toBe('always');
    });

    test('an expression is escaped, never rendered as markup', () => {
        card('archive_orders');

        attach(host, canvas, {
            tasks: [
                tasks[0],
                {
                    ...tasks[1],
                    dependsOn: [{ id: 'load_orders', condition: 'expression', expression: '"><img src=x onerror=alert(1)>' }],
                },
            ],
            selectedId: 'archive_orders',
        });

        expect(host.querySelector('img')).toBeNull();
        const field = host.querySelector('[data-task-expression="load_orders"]') as HTMLInputElement;
        expect(field.value).toBe('"><img src=x onerror=alert(1)>');
    });

    test('the palette offers the control-flow containers alongside the task kinds', () => {
        attach(host, canvas, { tasks });

        const containers = [...host.querySelectorAll('.is-container-chip')].map(chip => (chip as HTMLElement).dataset.taskKind);
        expect(containers).toEqual(['if', 'foreach', 'for', 'while', 'parallel', 'transaction']);
    });

    test('dropping a task onto a container puts it inside, not after it', () => {
        const container = card('load_all');
        const source = card('load_orders');
        const moves: any[] = [];
        const nests: any[] = [];

        attach(host, canvas, {
            tasks: [...tasks, { id: 'load_all', kind: 'parallel', connection: '', body: '', line: 1 }],
            onMove: (move: any) => { moves.push(move); },
            onNest: (nest: any) => { nests.push(nest); },
        });

        expect(container.classList.contains('is-container-task')).toBe(true);

        source.dispatchEvent(dragEvent('dragstart'));
        container.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));

        // The gesture matches the picture: a box is something things go inside. "Run after the
        // container" is the connector drag, which is what a container can actually be waited on for.
        expect(nests).toEqual([{ id: 'load_orders', container: 'load_all' }]);
        expect(moves).toEqual([]);
    });

    test('dropping a task onto a plain task still reorders', () => {
        const source = card('load_orders');
        const target = card('archive_orders');
        const moves: any[] = [];
        const nests: any[] = [];

        attach(host, canvas, {
            tasks,
            onMove: (move: any) => { moves.push(move); },
            onNest: (nest: any) => { nests.push(nest); },
        });

        source.dispatchEvent(dragEvent('dragstart'));
        target.dispatchEvent(dragEvent('drop', { 'text/plain': 'load_orders' }));

        expect(moves).toEqual([{ id: 'load_orders', after: 'archive_orders' }]);
        expect(nests).toEqual([]);
    });

    test('a nested task says where it is and offers a way out', () => {
        card('load_orders');
        const nests: any[] = [];

        attach(host, canvas, {
            tasks: [{ ...tasks[0], container: 'load_all' }, tasks[1]],
            selectedId: 'load_orders',
            onNest: (nest: any) => { nests.push(nest); },
        });

        expect(host.textContent).toContain('inside');
        (host.querySelector('[data-task-unnest]') as HTMLElement).click();
        expect(nests).toEqual([{ id: 'load_orders', container: null }]);
    });

    test('a parallel container says out loud that its branches cannot be ordered', () => {
        card('load_all');

        attach(host, canvas, {
            tasks: [{ id: 'load_all', kind: 'parallel', connection: '', body: '', line: 1 }],
            selectedId: 'load_all',
        });

        // The one place where an edge the author might want to draw is something the container
        // cannot express, so the inspector says so before they try.
        expect(host.textContent).toContain('starts at the same time');
        expect(host.textContent).toMatch(/cannot wait for each other/i);
    });

    test('a loop container names the variable its children are given', () => {
        card('per_region');

        attach(host, canvas, {
            tasks: [{
                id: 'per_region', kind: 'foreach', connection: '', body: '', line: 1,
                variable: '@region', collection: '#regions',
            }],
            selectedId: 'per_region',
        });

        expect(host.textContent).toContain('@region');
        expect(host.textContent).toContain('#regions');
    });

    test('the scope panel lists what is in scope, and each name goes back to its line', () => {
        card('load_orders');
        const lines: number[] = [];

        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            onOpenLine: (line: number) => { lines.push(line); },
            scope: {
                resolved: true,
                variables: [{ name: '@batch', type: 'VARCHAR', value: "'B-001'", line: 3, origin: 'declared' }],
                tempTables: [{ name: '#orders', columns: [{ name: 'OrderId' }], line: 5, origin: 'SELECT INTO' }],
            },
        });

        const panel = host.querySelector('[data-task-scope]') as HTMLElement;
        expect(panel.textContent).toContain('@batch');
        expect(panel.textContent).toContain('VARCHAR');
        expect(panel.textContent).toContain('#orders');
        expect(panel.textContent).toContain('OrderId');

        (panel.querySelector('[data-scope-line="5"]') as HTMLElement).click();
        expect(lines).toEqual([5]);
    });

    test('an empty scope and an unknown scope are different answers', () => {
        card('load_orders');

        // Nothing above the task: a real, empty scope.
        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            scope: { resolved: true, variables: [], tempTables: [] },
        });
        expect(host.querySelector('[data-task-scope]')!.textContent).toContain('Nothing yet');

        // The host could not tell. Rendering that as "nothing is in scope" is how a panel quietly lies.
        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            scope: { resolved: false, error: "'load_orders' is not a task in this script." },
        });
        const unresolved = host.querySelector('[data-task-scope]')!.textContent!;
        expect(unresolved).toContain('is not a task in this script');
        expect(unresolved).not.toContain('Nothing yet');

        // No answer at all yet.
        attach(host, canvas, { tasks, selectedId: 'load_orders' });
        expect(host.querySelector('[data-task-scope]')!.textContent).toContain('Reading the script');
    });

    test('row counts appear only when a run reported them for this task', () => {
        card('load_orders');

        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            scope: { resolved: true, variables: [], tempTables: [] },
        });
        // A zero here would read as "this task produced no rows", which is a result, not a silence.
        expect(host.querySelector('[data-task-scope]')!.textContent).toContain('after a run');
        expect(host.querySelector('[data-task-scope]')!.textContent).not.toContain('Last run');

        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            scope: { resolved: true, variables: [], tempTables: [] },
            runtime: { rows: 1200, durationMs: 42.4, status: 'Completed', note: 'Spilled to disk.' },
        });
        const measured = host.querySelector('[data-task-scope]')!.textContent!;
        expect(measured).toContain('Last run');
        expect(measured).toMatch(/1,?200 rows/);
        expect(measured).toContain('Spilled to disk.');
    });

    test('a scope value is escaped, never rendered as markup', () => {
        card('load_orders');

        attach(host, canvas, {
            tasks,
            selectedId: 'load_orders',
            scope: {
                resolved: true,
                variables: [{ name: '@x', type: null, value: '<img src=x onerror=alert(1)>', line: 1, origin: 'declared' }],
                tempTables: [],
            },
        });

        expect(host.querySelector('img')).toBeNull();
        expect(host.querySelector('[data-task-scope]')!.textContent).toContain('<img src=x onerror=alert(1)>');
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
