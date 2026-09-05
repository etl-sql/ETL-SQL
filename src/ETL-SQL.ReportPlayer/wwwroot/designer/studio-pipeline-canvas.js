// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-pipeline-canvas.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * The editable layer over the read-only pipeline execution map.
 *
 * The projection itself stays exactly what it was: an accurate map the engine derives from the
 * script. This module adds the one thing that makes it an editor — a task the author can add, name,
 * drag into a different position, and delete — and it does so without the canvas ever assembling
 * ETL-SQL. Every edit is a request to the host by task label; the host owns the bytes.
 *
 * Identity is the section label, never the node id. Node ids are positional, so a hand edit above a
 * task renumbers it and a canvas that remembered an id would follow the wrong box. That is the whole
 * reason a task carries a label in the script.
 *
 * It obeys the authoring component contract (see studio-authoring.js): host-neutral, no network of
 * its own, and it never writes to the document — it reports intent through the injected callbacks.
 */

import { escapeHtml, noteMarkup } from './studio-authoring-ui.js';

/**
 * The palette, grouped the way an author looks for something.
 *
 * Every entry writes exactly one labelled statement, and every one of them is covered by a focused
 * parse, lint, formatter, and reference test before it appears here — `PipelineTaskEmissionTests`
 * is that gate, and it fails when a kind the service can write has no draft in it. A chip that emits
 * a statement failing any of those checks is worse than no chip, because the author finds out when
 * the pipeline runs rather than when they click.
 *
 * The vocabulary is taken from the language rather than invented for the canvas: these are the
 * control-flow and file constructs ETL-SQL actually has, named as the script names them, so that
 * dragging one in and reading what it wrote is a way to learn the language.
 *
 * `needsLoop` marks the two entries the engine only accepts inside a loop. They are still shown —
 * finding out BREAK exists is the point of a palette — and they say so on the chip rather than
 * appearing to work and failing at run time.
 */
/**
 * One chip in the palette. `id` is a task kind the host can write, not a free string.
 *
 * `container` marks a kind that holds other tasks; `needsLoop` marks one the engine refuses
 * outside a loop. Both mirror the predicates in PipelineTaskKinds.
 *
 * @typedef {{ id: PipelineTaskKind, label: string, glyph: string, hint: string, container?: boolean, needsLoop?: boolean }} PipelineTaskChip
 */

/**
 * One drawer of the palette.
 *
 * @typedef {{ id: string, label: string, hint: string, kinds: ReadonlyArray<PipelineTaskChip> }} PipelineTaskGroup
 */

/**
 * The palette, and the vocabulary the host can write, held to being the same list.
 *
 * `PipelineTaskKind` is generated from the C# enum (types/etlsql-contracts.generated.d.ts), so a
 * chip naming a kind no host can write now fails the type gate on the line that wrote it rather
 * than becoming a control that refuses every time an author uses it. The other direction — a kind
 * the host can write with no chip to create it — is not something a type can state about an array,
 * and stays a test: see PipelineTaskAuthoringService's palette contract tests.
 *
 * @type {ReadonlyArray<PipelineTaskGroup>}
 */
export const PIPELINE_TASK_GROUPS = Object.freeze([
    Object.freeze({
        id: 'work',
        label: 'Work',
        hint: 'The statements that do something to your data.',
        kinds: Object.freeze([
            Object.freeze({
                id: 'execution',
                label: 'Execution',
                glyph: '\u25B6',
                hint: 'Run a block of SQL on a connection this script declares.',
            }),
            Object.freeze({
                id: 'validation',
                label: 'Validation',
                glyph: '\u2713',
                hint: 'Assert a condition and stop the run with a message when it fails.',
            }),
            Object.freeze({
                id: 'notification',
                label: 'Notification',
                glyph: '\u2709',
                hint: 'Send an email through an SMTP connection this script declares.',
            }),
            Object.freeze({
                id: 'throw',
                label: 'Fail',
                glyph: '\u26A1',
                hint: 'Stop the run with your own error. Inside a TRY, the CATCH takes over.',
            }),
            Object.freeze({
                id: 'waitfor',
                label: 'Wait',
                glyph: '\u23F1',
                hint: 'Pause for a duration, or until a time of day.',
            }),
        ]),
    }),
    Object.freeze({
        id: 'flow',
        label: 'Control flow',
        hint: 'Blocks that decide what runs, how often, and what happens when something fails.',
        kinds: Object.freeze([
            Object.freeze({
                id: 'if',
                label: 'If',
                glyph: '\u2442',
                container: true,
                hint: 'A block that runs only when its condition is true.',
            }),
            Object.freeze({
                id: 'foreach',
                label: 'For each',
                glyph: '\u21BB',
                container: true,
                hint: 'A block run once per item of a list, a #temp table, or a query.',
            }),
            Object.freeze({
                id: 'for',
                label: 'Count',
                glyph: '\u2261',
                container: true,
                hint: 'A block run once per number, counting from one value to another.',
            }),
            Object.freeze({
                id: 'while',
                label: 'While',
                glyph: '\u27F3',
                container: true,
                hint: 'A block repeated for as long as its condition holds.',
            }),
            Object.freeze({
                id: 'parallel',
                label: 'Parallel',
                glyph: '\u21C9',
                container: true,
                hint: 'A block whose tasks all start at the same time. This is the only thing in ETL-SQL that means concurrency.',
            }),
            Object.freeze({
                id: 'transaction',
                label: 'Transaction',
                glyph: '\u26E8',
                container: true,
                hint: 'A block that commits as one unit and rolls back if anything inside it fails.',
            }),
            Object.freeze({
                id: 'break',
                label: 'Break',
                glyph: '\u23F9',
                needsLoop: true,
                hint: 'Leaves the loop it is in. Only legal inside a loop.',
            }),
            Object.freeze({
                id: 'continue',
                label: 'Continue',
                glyph: '\u23ED',
                needsLoop: true,
                hint: 'Skips to the loop\'s next item. Only legal inside a loop.',
            }),
        ]),
    }),
    Object.freeze({
        id: 'files',
        label: 'Files',
        hint: 'One file at a time, by path.',
        kinds: Object.freeze([
            Object.freeze({
                id: 'copyfile',
                label: 'Copy file',
                glyph: '\u2398',
                hint: 'Copy a file to another path, leaving the original where it is.',
            }),
            Object.freeze({
                id: 'movefile',
                label: 'Move file',
                glyph: '\u2192',
                hint: 'Move a file to another path.',
            }),
            Object.freeze({
                id: 'renamefile',
                label: 'Rename file',
                glyph: '\u270E',
                hint: 'Give a file a new name, in the directory it is already in.',
            }),
            Object.freeze({
                id: 'deletefile',
                label: 'Delete file',
                glyph: '\u2717',
                hint: 'Delete one file.',
            }),
        ]),
    }),
    Object.freeze({
        id: 'directories',
        label: 'Directories',
        hint: 'Whole folders, by path.',
        kinds: Object.freeze([
            Object.freeze({
                id: 'createdirectory',
                label: 'Create folder',
                glyph: '\u002B',
                hint: 'Create a directory, and any parent it needs.',
            }),
            Object.freeze({
                id: 'copydirectory',
                label: 'Copy folder',
                glyph: '\u29C9',
                hint: 'Copy a directory and everything in it.',
            }),
            Object.freeze({
                id: 'movedirectory',
                label: 'Move folder',
                glyph: '\u21E5',
                hint: 'Move a directory and everything in it.',
            }),
            Object.freeze({
                id: 'renamedirectory',
                label: 'Rename folder',
                glyph: '\u270D',
                hint: 'Give a directory a new name, where it already sits.',
            }),
            Object.freeze({
                id: 'deletedirectorycontents',
                label: 'Empty folder',
                glyph: '\u2205',
                hint: 'Delete what is inside a directory and keep the directory.',
            }),
            Object.freeze({
                id: 'deletedirectory',
                label: 'Delete folder',
                glyph: '\u2326',
                hint: 'Delete a directory and everything in it.',
            }),
        ]),
    }),
]);

/**
 * Every palette entry, flat.
 *
 * Kept as the exported name it has always had, because other modules look a kind up by id and do
 * not care which drawer it is filed under.
 */
export const PIPELINE_TASK_KINDS = Object.freeze(
    PIPELINE_TASK_GROUPS.flatMap(group => group.kinds));

/** The palette entry for a kind, or null when this build does not offer it. */
export function taskKind(kind) {
    return PIPELINE_TASK_KINDS.find(entry => entry.id === String(kind || '').toLowerCase()) ?? null;
}

/**
 * The drag format a palette chip carries.
 *
 * A type of its own so the canvas can tell a chip from a card during `dragover`, where the payload
 * itself cannot be read. Anything else dragged over the map — a file, a selection from the script —
 * has no such type and is left alone rather than being guessed at.
 */
export const PALETTE_DRAG_TYPE = 'application/x-etlsql-task-kind';

/** One drawer of the palette. */
function paletteGroupMarkup(group) {
    return `<section class="etlsql-studio-palette-group" data-palette-group="${escapeHtml(group.id)}">
        <h4 title="${escapeHtml(group.hint)}">${escapeHtml(group.label)}</h4>
        <div class="etlsql-studio-palette-chips">
            ${group.kinds.map(paletteChipMarkup).join('')}
        </div>
    </section>`;
}

/**
 * One chip.
 *
 * A button as well as a drag source: the drag is the gesture the canvas is for, and the button is
 * the one that works without a mouse. The two do the same thing, so neither is a second-class path.
 */
function paletteChipMarkup(kind) {
    return `<button type="button"
        class="etlsql-studio-task-chip${kind.container ? ' is-container-chip' : ''}"
        draggable="true"
        data-task-kind="${escapeHtml(kind.id)}"
        title="${escapeHtml(kind.hint)}${kind.needsLoop ? ' Drop it inside a loop.' : ''}">
        <span class="etlsql-studio-task-chip-glyph" aria-hidden="true">${escapeHtml(kind.glyph)}</span>
        <span class="etlsql-studio-task-chip-label">${escapeHtml(kind.label)}</span>
        ${kind.needsLoop ? '<span class="etlsql-studio-task-chip-tag">in a loop</span>' : ''}
    </button>`;
}

/** True when this kind holds other tasks. */
export function isContainerKind(kind) {
    return Boolean(PIPELINE_TASK_KINDS.find(entry => entry.id === String(kind || '').toLowerCase())?.container);
}

/**
 * When an edge hands over.
 *
 * `always` is plain precedence and costs the script nothing. Every other condition is written into
 * the file as real control flow — a `BEGIN TRY` guard around the task being watched and an `IF`
 * around the task that waits — which is why each one says what it does to the script rather than
 * only what it means on the diagram.
 */
/**
 * One edge condition offer.
 *
 * @typedef {{ id: PipelineEdgeCondition, label: string, summary: string, hint: string }} PipelineEdgeConditionOffer
 */

/** @type {ReadonlyArray<PipelineEdgeConditionOffer>} */
export const PIPELINE_EDGE_CONDITIONS = Object.freeze([
    Object.freeze({
        id: 'always',
        label: 'Always',
        summary: 'runs next',
        hint: 'Plain precedence. A failure upstream still stops the run.',
    }),
    Object.freeze({
        id: 'onsuccess',
        label: 'On success',
        summary: 'on success',
        hint: 'Runs only when that task finished without an error.',
    }),
    Object.freeze({
        id: 'onfailure',
        label: 'On failure',
        summary: 'on failure',
        hint: 'Runs only when that task threw. The error is caught, so the run continues.',
    }),
    Object.freeze({
        id: 'oncompletion',
        label: 'On completion',
        summary: 'either way',
        hint: 'Runs either way. The error upstream is caught and no longer stops the run.',
    }),
    Object.freeze({
        id: 'expression',
        label: 'When…',
        summary: 'when',
        hint: 'Runs when your own condition is true, checked after that task.',
    }),
]);

function edgeCondition(id) {
    return PIPELINE_EDGE_CONDITIONS.find(entry => entry.id === String(id || 'always').toLowerCase())
        ?? PIPELINE_EDGE_CONDITIONS[0];
}

/** What to call a kind the host reported, so a card can say what it is. */
export function taskKindLabel(kind) {
    return taskKind(kind)?.label ?? 'Task';
}

/** True when the engine only accepts this kind inside a loop. */
export function needsALoop(kind) {
    return Boolean(taskKind(kind)?.needsLoop);
}

/**
 * Renders the task toolbar and inspector into `host`, and makes labelled cards inside `canvas`
 * draggable.
 *
 * @param {HTMLElement} host   Element that receives the toolbar and inspector markup.
 * @param {HTMLElement} canvas Element containing the rendered DAG cards.
 * @param {Object} [options]
 * @param {Array<{id: *, kind?: string, connection?: string, body?: string, line?: number}>} [options.tasks]
 *   As the host reported them.
 * @param {*} [options.selectedId] Task to show as selected, or null.
 * @param {(id: *) => void} [options.onSelect]
 * @param {(change: {kind: PipelineTaskKind, after: *, into?: *}) => Promise<void>} [options.onAdd]
 *   `into` names the container the new task is dropped inside, when it is dropped into one.
 * @param {(change: {id: *}) => Promise<void>} [options.onEdit] Opens the task editor.
 * @param {(change: {from: *, to: *}) => Promise<void>} [options.onConnect]
 *   Declares that `to` runs after `from`.
 * @param {(change: {from: *, to: *, edge?: PipelineEdgeCondition, expression?: string}) => Promise<void>} [options.onSetEdge]
 *   Changes one edge's condition.
 * @param {(change: {from: *, to: *}) => Promise<void>} [options.onDisconnect] Removes one edge.
 * @param {(change: {id: *, after: *}) => Promise<void>} [options.onMove]
 *   `after` null means "run first".
 * @param {(change: {id: *, container: *}) => Promise<void>} [options.onNest]
 *   `container` null means "move out".
 * @param {(change: {id: *, [key: string]: *}) => Promise<void>} [options.onUpdate]
 * @param {(change: {id: *}) => Promise<void>} [options.onRemove]
 * @param {((change: {id: *}) => Promise<void>)|null} [options.onRunTo]
 *   Executes the pipeline through this task.
 * @param {(line: number) => void} [options.onOpenLine] Reveals the task in the script.
 * @param {*} [options.scope]   `{ resolved, error, variables, tempTables }` where the task sits.
 * @param {*} [options.runtime] `{ rows, durationMs, note }` the last run reported for it.
 * @returns {{dispose: () => void}}
 */
export function attachPipelineTaskEditing(host, canvas, {
    tasks = [],
    selectedId = null,
    onSelect = () => {},
    onAdd = async () => {},
    onEdit = async () => {},
    onConnect = async () => {},
    onSetEdge = async () => {},
    onDisconnect = async () => {},
    onMove = async () => {},
    onNest = async () => {},
    onUpdate = async () => {},
    onRemove = async () => {},
    onRunTo = null,
    onOpenLine = () => {},
    scope = null,
    runtime = null,
} = {}) {
    const selected = tasks.find(task => sameId(task.id, selectedId)) || null;

    // What is being dragged, and what the gesture means: a palette chip being added, a card being
    // reordered, or a connector declaring a dependency. Held here rather than read back off the
    // document, because the drag payload is deliberately unreadable during dragover and a component
    // that queries the shell to find out what it is dragging has reached outside its own host.
    let dragging = null;
    let draggingKind = null;

    const listeners = [];
    const on = (element, type, handler) => {
        element.addEventListener(type, handler);
        listeners.push(() => element.removeEventListener(type, handler));
    };

    host.innerHTML = `
        <div class="etlsql-studio-pipeline-tools">
            <aside class="etlsql-studio-pipeline-palette" data-task-palette aria-label="Statements you can add">
                <p class="etlsql-studio-palette-lede">Drag one onto the map, or click to add it after
                    ${selected ? `<code>${escapeHtml(selected.id)}</code>` : 'the last statement'}.</p>
                ${PIPELINE_TASK_GROUPS.map(paletteGroupMarkup).join('')}
            </aside>
            <span class="etlsql-studio-pipeline-hint">${escapeHtml(hint(tasks, selected))}</span>
        </div>
        <div class="etlsql-studio-pipeline-inspector" data-task-inspector>${inspectorMarkup(selected, Boolean(onRunTo))}</div>`;

    // A click adds beside the selection; a drag decides where from where it lands. Both are kept:
    // the drag is the gesture the canvas is for, and the click is the one that works from a keyboard.
    for (const chip of host.querySelectorAll('[data-task-kind]')) {
        const kind = taskKind(/** @type {HTMLElement} */ (chip).dataset.taskKind)?.id ?? null;
        if (!kind) continue;
        on(chip, 'click', () => onAdd({ kind, after: selected?.id ?? null }));
        on(chip, 'dragstart', event => {
            dragging = kind;
            draggingKind = 'palette';
            event.dataTransfer.effectAllowed = 'copy';
            // Both formats on purpose. The custom type is what the canvas tests for during dragover,
            // where the payload itself is unreadable; the plain text is what makes the chip mean
            // something when it is dropped into the script pane or another editor.
            event.dataTransfer.setData(PALETTE_DRAG_TYPE, kind);
            event.dataTransfer.setData('text/plain', kind);
            chip.classList.add('is-dragging-chip');
        });
        on(chip, 'dragend', () => {
            dragging = null;
            draggingKind = null;
            chip.classList.remove('is-dragging-chip');
            canvas.classList.remove('is-drop-target');
            for (const card of canvas.querySelectorAll('[data-task-key]')) card.classList.remove('is-drop-target');
        });
    }

    const inspector = host.querySelector('[data-task-inspector]');
    if (selected) {
        inspector.insertAdjacentHTML('beforeend', scopeMarkup(scope, runtime));
        for (const link of inspector.querySelectorAll('[data-scope-line]')) {
            on(link, 'click', () => onOpenLine(Number(/** @type {HTMLElement} */ (link).dataset.scopeLine) || 0));
        }
    }

    const edit = inspector.querySelector('[data-task-edit]');
    if (edit) on(edit, 'click', () => onEdit({ id: selected.id }));

    const remove = inspector.querySelector('[data-task-remove]');
    if (remove) on(remove, 'click', () => onRemove({ id: selected.id }));

    // Absent rather than disabled on a host that does not offer it: a greyed-out Run is a promise
    // the canvas cannot keep, and there is nothing the author could do here to earn it.
    const runTo = inspector.querySelector('[data-task-run-to]');
    if (runTo) on(runTo, 'click', () => onRunTo({ id: selected.id }));

    const reveal = inspector.querySelector('[data-task-reveal]');
    if (reveal) on(reveal, 'click', () => onOpenLine(selected.line));

    const first = inspector.querySelector('[data-task-first]');
    if (first) on(first, 'click', () => onMove({ id: selected.id, after: null }));

    const unnest = inspector.querySelector('[data-task-unnest]');
    if (unnest) on(unnest, 'click', () => onNest({ id: selected.id, container: null }));

    for (const chip of inspector.querySelectorAll('[data-task-disconnect]')) {
        on(chip, 'click', () => onDisconnect({ from: /** @type {HTMLElement} */ (chip).dataset.taskDisconnect, to: selected.id }));
    }

    // ── Edge conditions ──────────────────────────────────────────────────────
    // Choosing `When…` does not send anything: the edge is not describable until the expression is
    // typed, and writing a gate on an empty condition would be a change the author did not make.
    for (const picker of inspector.querySelectorAll('[data-task-edge]')) {
        const from = /** @type {HTMLElement} */ (picker).dataset.taskEdge;
        const field = inspector.querySelector(`[data-task-expression="${cssEscape(from)}"]`);
        on(picker, 'change', () => {
            const chosen = edgeCondition(/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (picker).value).id;
            if (chosen !== 'expression') {
                void onSetEdge({ from, to: selected.id, edge: chosen });
                return;
            }
            if (field) {
                /** @type {HTMLElement} */ (field).hidden = false;
                /** @type {HTMLElement} */ (field).focus();
                /** @type {HTMLInputElement | HTMLTextAreaElement} */ (field).select();
            }
        });
    }

    for (const field of inspector.querySelectorAll('[data-task-expression]')) {
        const from = /** @type {HTMLElement} */ (field).dataset.taskExpression;
        const commit = () => {
            const expression = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (field).value.trim();
            if (!expression || expression === /** @type {HTMLElement} */ (field).dataset.taskExpressionValue) return;
            void onSetEdge({ from, to: selected.id, edge: 'expression', expression });
        };
        on(field, 'keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                commit();
            }
        });
        on(field, 'blur', commit);
    }

    // ── The cards ────────────────────────────────────────────────────────────
    // Only labelled cards take part. Everything else on the map is a projection stage: real, and
    // deliberately not draggable, because the canvas cannot edit it losslessly yet.

    const containers = new Set(tasks
        .filter(task => isContainerKind(task.kind))
        .map(task => String(task.id).toLowerCase()));

    const cards = [...canvas.querySelectorAll('[data-task-key]')];
    for (const card of cards) {
        const id = /** @type {HTMLElement} */ (card).dataset.taskKey;
        card.classList.add('is-editable-task');
        /** @type {HTMLElement} */ (card).draggable = true;
        card.classList.toggle('is-selected-task', sameId(id, selectedId));
        card.classList.toggle('is-container-task', containers.has(String(id).toLowerCase()));

        // Dragging the card body reorders; dragging this handle declares a dependency. Two gestures
        // because they mean different things: one moves a statement, the other writes a declaration
        // about what has to finish first.
        //
        // The handle is the dot the map already draws on the right edge of the card, promoted to a
        // real control. It used to be decoration beside a separate handle the author had to find:
        // the obvious thing to grab did nothing, and the thing that worked was somewhere else. Where
        // the map has no dot to promote — an older projection, a re-render — one is created, so a
        // connectable card always has a visible handle.
        if (!card.querySelector('[data-task-connector]')) {
            const existing = card.querySelector('.card-port-right');
            const handle = existing ?? card.ownerDocument.createElement('span');
            handle.classList.add('etlsql-dag-connector');
            // The map paints a port the colour of the node type. A control is not decoration, so it
            // drops the inline colour and takes the accent every other control on this surface uses.
            /** @type {HTMLElement} */ (handle).style.background = '';
            /** @type {HTMLElement} */ (handle).dataset.taskConnector = id;
            /** @type {HTMLElement} */ (handle).draggable = true;
            /** @type {HTMLElement} */ (handle).tabIndex = 0;
            handle.setAttribute('role', 'button');
            /** @type {HTMLElement} */ (handle).title = `Drag onto another task to make it run after ${id}`;
            handle.setAttribute('aria-label', `Connect ${id} to another task`);
            if (!existing) card.appendChild(handle);

            on(handle, 'click', event => event.stopPropagation());
            on(handle, 'dragstart', event => {
                event.stopPropagation();
                dragging = id;
                draggingKind = 'connect';
                event.dataTransfer.effectAllowed = 'link';
                event.dataTransfer.setData('text/plain', id);
                card.classList.add('is-connecting-task');
            });
            on(handle, 'dragend', () => {
                dragging = null;
                draggingKind = null;
                card.classList.remove('is-connecting-task');
                cards.forEach(other => other.classList.remove('is-drop-target'));
            });
        }

        on(card, 'click', () => onSelect(id));
        on(card, 'dragstart', event => {
            dragging = id;
            draggingKind = 'move';
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', id);
            card.classList.add('is-dragging-task');
        });
        on(card, 'dragend', () => {
            dragging = null;
            draggingKind = null;
            card.classList.remove('is-dragging-task');
            cards.forEach(other => other.classList.remove('is-drop-target'));
        });
        on(card, 'dragover', event => {
            if (!dragging) return;
            if (draggingKind !== 'palette' && sameId(id, dragging)) return;
            event.preventDefault();
            event.stopPropagation();
            event.dataTransfer.dropEffect = draggingKind === 'connect' ? 'link'
                : draggingKind === 'palette' ? 'copy' : 'move';
            card.classList.add('is-drop-target');
        });
        on(card, 'dragleave', () => card.classList.remove('is-drop-target'));
        on(card, 'drop', event => {
            event.preventDefault();
            event.stopPropagation();
            card.classList.remove('is-drop-target');

            // A chip dropped on a container goes inside it and one dropped on a task goes after it.
            // Both are one request: where it was dropped decides where in the script it is written,
            // and nothing else about the drop is remembered.
            if (draggingKind === 'palette') {
                const kind = event.dataTransfer.getData(PALETTE_DRAG_TYPE) || dragging;
                if (!kind) return;
                onAdd(containers.has(String(id).toLowerCase())
                    ? { kind, into: id, after: null }
                    : { kind, after: id, into: null });
                return;
            }

            const moved = event.dataTransfer.getData('text/plain') || dragging;
            if (!moved || sameId(moved, id)) return;

            if (draggingKind === 'connect') {
                // Dropping a connector onto a task declares that the task waits for the one the drag
                // started from. Several of those on one task is a join, never concurrency.
                onConnect({ from: moved, to: id });
            } else if (containers.has(String(id).toLowerCase())) {
                // Dropping a task into a container puts it inside — the gesture matches the picture.
                // To make a task run *after* a container instead, drag its connector onto it: that
                // says "wait for this", which is the thing a container can actually be waited on for.
                onNest({ id: moved, container: id });
            } else {
                // Dropping a task onto another means "run after this one". Order in the script is the
                // dependency; nothing here implies concurrency either.
                onMove({ id: moved, after: id });
            }
        });
    }

    // Every other card on the map is a projection stage the canvas cannot author. It keeps its shape
    // and its position — it is a true picture of the script — and its round dots stop pretending to
    // be controls. They looked exactly like the handle that does something, and dragging one was the
    // first thing manual testing tried.
    //
    // Marked rather than removed: the map anchors every edge line on these two elements and skips
    // any edge whose endpoints are missing, so deleting them erases the arrows between the stages
    // they connect. They are drawn as what they are — the point a line meets the card.
    for (const card of canvas.querySelectorAll('.etlsql-dag-card:not([data-task-key])')) {
        for (const port of card.querySelectorAll('.card-port-left, .card-port-right')) {
            port.classList.add('is-anchor-port');
            /** @type {HTMLElement} */ (port).style.background = '';
        }
        card.classList.add('is-projection-stage');
        if (!/** @type {HTMLElement} */ (card).title) {
            /** @type {HTMLElement} */ (card).title = 'This statement is on the map because the script has it. The canvas cannot '
                + 'author this one yet, so it is shown rather than editable.';
        }
    }

    // An editable card's left dot is an anchor too — the point an incoming line meets it — so it is
    // drawn the same muted way. After this, the only filled dot anywhere on the map is the connector
    // handle, and a dot that looks like a control is one.
    for (const card of cards) {
        const inbound = card.querySelector('.card-port-left');
        if (!inbound) continue;
        inbound.classList.add('is-anchor-port');
        /** @type {HTMLElement} */ (inbound).style.background = '';
    }

    // A chip dropped on empty canvas goes at the end of the script. That is the one place on the map
    // with no statement under the cursor, so it is the only drop that can mean "just add it".
    on(canvas, 'dragover', event => {
        if (draggingKind !== 'palette' || !dragging) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
        canvas.classList.add('is-drop-target');
    });
    on(canvas, 'dragleave', event => {
        if (event.target === canvas) canvas.classList.remove('is-drop-target');
    });
    on(canvas, 'drop', event => {
        canvas.classList.remove('is-drop-target');
        if (draggingKind !== 'palette') return;
        event.preventDefault();
        const kind = event.dataTransfer.getData(PALETTE_DRAG_TYPE) || dragging;
        if (kind) onAdd({ kind, after: null, into: null });
    });

    return {
        dispose: () => {
            listeners.forEach(off => off());
            listeners.length = 0;
        },
    };
}

function sameId(left, right) {
    return String(left ?? '').toLowerCase() === String(right ?? '').toLowerCase();
}

function hint(tasks, selected) {
    if (!tasks.length) {
        return 'Nothing on the map yet. Drag a statement from the palette onto it — the script it writes '
            + 'appears in the code pane, which is where the map comes from in the first place.';
    }
    if (!selected) {
        return `${tasks.length} editable task${tasks.length === 1 ? '' : 's'}. Drop a palette chip on one to `
            + 'write a statement after it, on a block to write it inside, or on empty space for the end of the script.';
    }
    return `${selected.id} selected. Drag it onto another task to run it after that one, then set when the edge hands over.`;
}

function inspectorMarkup(task, runnable) {
    if (!task) {
        return noteMarkup([
            'Pick a task to edit it, or add one from the palette. Every task is one labelled statement, '
            + 'so the label — like ',
            { code: 'load_orders:' },
            ' — is what the canvas tracks it by. Other stages on this map come from statements the '
            + 'canvas does not edit yet, so they are shown but not draggable.',
        ], 'info');
    }

    const detail = task.kind === 'execution' && task.connection
        ? [{ strong: taskKindLabel(task.kind) }, ' on ', { code: task.connection }]
        : task.kind === 'foreach' && task.collection
            ? [{ strong: taskKindLabel(task.kind) }, ' over ', { code: task.collection }]
            : task.kind === 'for' && task.variable
                ? [{ strong: taskKindLabel(task.kind) }, ' on ', { code: task.variable }]
                : [{ strong: taskKindLabel(task.kind) }];

    if (task.container) {
        detail.push(' · inside ', { code: task.container });
    }

    return `
        <div class="etlsql-studio-pipeline-selected">
            <span class="etlsql-studio-pipeline-selected-name">${escapeHtml(task.id)}</span>
            ${noteMarkup(detail, 'info')}
        </div>
        ${dependencyMarkup(task)}
        ${containerNote(task)}
        <div class="etlsql-studio-pipeline-actions">
            <button type="button" class="etlsql-studio-btn is-primary" data-task-edit>Edit</button>
            <button type="button" class="etlsql-studio-btn" data-task-first>Run first</button>
            ${task.container ? `<button type="button" class="etlsql-studio-btn"
                data-task-unnest>Move out of ${escapeHtml(task.container)}</button>` : ''}
            <button type="button" class="etlsql-studio-btn" data-task-reveal>Show in script</button>
            ${runnable ? `<button type="button" class="etlsql-studio-btn" data-task-run-to
                title="Run this pipeline from the top through ${escapeHtml(task.id)}, so its variables and
#temp tables land in Results.">Run to here</button>` : ''}
            <button type="button" class="etlsql-studio-btn is-danger" data-task-remove>Delete</button>
        </div>`;
}

/**
 * What the selected task waits for, each one removable.
 *
 * Reads as a list of prerequisites rather than as "edges", because that is what it means to the
 * author: several of them is a join — this task runs when all of them are done — and never an
 * instruction to run anything at the same time.
 */
function dependencyMarkup(task) {
    const dependencies = (task.dependsOn ?? []).map(normalizeDependency);
    if (!dependencies.length) {
        return `<p class="etlsql-studio-pipeline-deps-empty">Runs in script order. Drag another task's
            connector onto this one to make it wait for that task.</p>`;
    }

    // Read as a list of prerequisites, each with the condition it hands over on. Several of them is
    // a join — this task runs when all of them are satisfied — and never an instruction to run
    // anything at the same time.

    return `<div class="etlsql-studio-pipeline-deps">
        <span>${dependencies.length === 1 ? 'Waits for' : `Waits for all ${dependencies.length}`}</span>
        ${dependencies.map(dependencyRow).join('')}
    </div>`;
}

/**
 * The host reports a dependency as an object, but an older host — or a cached response written
 * before conditional edges existed — reports a bare label. Both mean "waits for this task", so both
 * are read; guessing a condition for the bare form would put a gate in the script nobody asked for.
 */
function normalizeDependency(entry) {
    if (typeof entry === 'string') return { id: entry, condition: 'always', expression: null };
    return {
        id: String(entry?.id ?? ''),
        condition: String(entry?.condition ?? 'always').toLowerCase(),
        expression: entry?.expression ?? null,
    };
}

function dependencyRow(dependency) {
    const condition = edgeCondition(dependency.condition);
    const expression = dependency.expression ?? '';
    const isExpression = condition.id === 'expression';

    return `<span class="etlsql-studio-dep-chip is-${escapeHtml(condition.id)}">
        <code>${escapeHtml(dependency.id)}</code>
        <select data-task-edge="${escapeHtml(dependency.id)}"
            aria-label="When ${escapeHtml(dependency.id)} hands over" title="${escapeHtml(condition.hint)}">
            ${PIPELINE_EDGE_CONDITIONS.map(entry => `<option value="${escapeHtml(entry.id)}"
                ${entry.id === condition.id ? 'selected' : ''}>${escapeHtml(entry.label)}</option>`).join('')}
        </select>
        <input type="text" class="etlsql-studio-dep-expression"
            data-task-expression="${escapeHtml(dependency.id)}"
            data-task-expression-value="${escapeHtml(expression)}"
            value="${escapeHtml(expression)}"
            placeholder="@@ROWCOUNT > 0"
            aria-label="Condition for the edge from ${escapeHtml(dependency.id)}"
            ${isExpression ? '' : 'hidden'}>
        <button type="button" data-task-disconnect="${escapeHtml(dependency.id)}"
            aria-label="Stop waiting for ${escapeHtml(dependency.id)}" title="Remove this dependency">&times;</button>
    </span>`;
}

/**
 * What the selected task can see from where it sits, and what the last run said about it.
 *
 * Positional on purpose. A panel listing every name in the file would tell the author they can read
 * a variable declared below this task or a `#temp` created after it — true of the file, false of the
 * moment the task runs, and only discoverable at run time.
 *
 * `null` scope means the host has not answered yet; an unresolved scope means it answered that it
 * could not tell. Neither is rendered as "nothing is in scope", because that is a different claim.
 */
function scopeMarkup(scope, runtime) {
    if (!scope) {
        return `<div class="etlsql-studio-pipeline-scope" data-task-scope>
            <span class="etlsql-studio-pipeline-scope-head">In scope here</span>
            <p class="etlsql-studio-pipeline-scope-empty">Reading the script…</p>
        </div>`;
    }

    if (scope.resolved === false) {
        return `<div class="etlsql-studio-pipeline-scope" data-task-scope>
            <span class="etlsql-studio-pipeline-scope-head">In scope here</span>
            ${noteMarkup([scope.error || 'The script could not be read.'], 'warning')}
        </div>`;
    }

    const variables = scope.variables ?? [];
    const tempTables = scope.tempTables ?? [];

    const body = !variables.length && !tempTables.length
        ? `<p class="etlsql-studio-pipeline-scope-empty">Nothing yet. Everything this task reads has to be
            declared or staged above it — the engine runs the script top to bottom.</p>`
        : `${variables.length ? `<ul class="etlsql-studio-scope-list">
                ${variables.map(scopeVariableRow).join('')}
            </ul>` : ''}
           ${tempTables.length ? `<ul class="etlsql-studio-scope-list">
                ${tempTables.map(scopeTempRow).join('')}
            </ul>` : ''}`;

    return `<div class="etlsql-studio-pipeline-scope" data-task-scope>
        <span class="etlsql-studio-pipeline-scope-head">In scope here</span>
        ${body}
        ${runtimeMarkup(runtime)}
    </div>`;
}

function scopeVariableRow(variable) {
    const origin = variable.origin === 'loop' ? 'per item'
        : variable.origin === 'assigned' ? 'set' : 'declared';

    return `<li class="etlsql-studio-scope-item is-variable">
        <button type="button" class="etlsql-studio-scope-name" data-scope-line="${escapeHtml(String(variable.line ?? 0))}"
            title="Show line ${escapeHtml(String(variable.line ?? 0))} in the script"><code>${escapeHtml(variable.name)}</code></button>
        ${variable.type ? `<span class="etlsql-studio-scope-type">${escapeHtml(variable.type)}</span>` : ''}
        ${variable.value ? `<code class="etlsql-studio-scope-value">${escapeHtml(variable.value)}</code>` : ''}
        <span class="etlsql-studio-scope-origin">${escapeHtml(origin)}</span>
    </li>`;
}

function scopeTempRow(table) {
    const columns = table.columns ?? [];

    return `<li class="etlsql-studio-scope-item is-temp">
        <button type="button" class="etlsql-studio-scope-name" data-scope-line="${escapeHtml(String(table.line ?? 0))}"
            title="Show line ${escapeHtml(String(table.line ?? 0))} in the script"><code>${escapeHtml(table.name)}</code></button>
        ${columns.length
            ? `<span class="etlsql-studio-scope-type">${escapeHtml(columns.map(column => column.name).join(', '))}</span>`
            : ''}
        <span class="etlsql-studio-scope-origin">${escapeHtml(table.origin || '')}</span>
    </li>`;
}

/**
 * What the last run measured here.
 *
 * Row counts and spill are run-time facts, so they are shown only when a run actually reported them
 * for this task. Rendering a zero when nothing has run yet would read as "this produced no rows".
 */
function runtimeMarkup(runtime) {
    if (!runtime) {
        return `<p class="etlsql-studio-pipeline-scope-empty">Row counts and spill appear here after a run
            reports them for this task.</p>`;
    }

    const parts = [];
    if (Number.isFinite(runtime.rows)) parts.push(`${runtime.rows.toLocaleString()} row${runtime.rows === 1 ? '' : 's'}`);
    if (Number.isFinite(runtime.durationMs)) parts.push(`${Math.round(runtime.durationMs)} ms`);
    if (runtime.status) parts.push(String(runtime.status));

    return `<p class="etlsql-studio-pipeline-scope-runtime">
        <span>Last run</span> ${escapeHtml(parts.join(' · '))}
        ${runtime.note ? `<em>${escapeHtml(runtime.note)}</em>` : ''}
    </p>`;
}

/**
 * What a container is, said where the author is looking at one.
 *
 * A `PARALLEL` block earns the most explanation: it is the only construct in ETL-SQL that means
 * concurrency, and the one place where a dependency the author might want to draw is something the
 * container cannot express.
 */
function containerNote(task) {
    if (!isContainerKind(task.kind)) return '';

    if (task.kind === 'parallel') {
        return noteMarkup([
            'Everything dropped in here starts at the same time. That means branches cannot wait for '
            + 'each other — to order two of them, move one out of the block.',
        ], 'warning');
    }

    if (task.kind === 'foreach') {
        return noteMarkup([
            'Everything dropped in here runs once per item, in order, with ',
            { code: task.variable || '@item' },
            ' bound to the current one. BREAK leaves the loop; CONTINUE starts the next item.',
        ], 'info');
    }

    if (task.kind === 'for') {
        return noteMarkup([
            'Everything dropped in here runs once per number, with ',
            { code: task.variable || '@i' },
            ' holding it. BREAK leaves the loop; CONTINUE starts the next number.',
        ], 'info');
    }

    if (task.kind === 'while') {
        return noteMarkup([
            'Everything dropped in here runs again for as long as the condition holds — so something '
            + 'inside has to change what the condition reads, or the loop never ends. BREAK leaves it.',
        ], 'info');
    }

    if (task.kind === 'if') {
        return noteMarkup([
            'Everything dropped in here runs only when the condition is true. An ',
            { code: 'ELSE' },
            ' branch is written in the script rather than on the canvas: the canvas tracks one block '
            + 'per label, so it would have no way to say which of two you dropped into.',
        ], 'info');
    }

    return noteMarkup([
        'Everything dropped in here commits as one unit. If any of it fails, the whole scope is '
        + 'rolled back and the error is re-thrown.',
    ], 'info');
}

/** `CSS.escape` where the host has it, and a conservative fallback where it does not. */
function cssEscape(value) {
    const text = String(value ?? '');
    return typeof CSS !== 'undefined' && CSS.escape ? CSS.escape(text) : text.replace(/[^\w-]/g, '\\$&');
}
