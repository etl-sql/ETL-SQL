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
 * The palette. Every kind here writes exactly one labelled statement, and every one of them is
 * covered by a focused parse, lint, formatter, and reference test before it appears — a chip that
 * emits a statement failing any of those is worse than no chip, because the author finds out when
 * the pipeline runs rather than when they click.
 */
export const PIPELINE_TASK_KINDS = Object.freeze([
    Object.freeze({
        id: 'execution',
        label: 'Execution',
        glyph: '▶',
        hint: 'Run a block of SQL on a connection this script declares.',
    }),
    Object.freeze({
        id: 'fileoperation',
        label: 'File',
        glyph: '🗎',
        hint: 'Copy a file from one path to another.',
    }),
    Object.freeze({
        id: 'validation',
        label: 'Validation',
        glyph: '✓',
        hint: 'Assert a condition and stop the run with a message when it fails.',
    }),
    Object.freeze({
        id: 'notification',
        label: 'Notification',
        glyph: '✉',
        hint: 'Send an email through an SMTP connection this script declares.',
    }),
]);

/** The palette entry for a kind the host reported, so a card can say what it is. */
export function taskKindLabel(kind) {
    return PIPELINE_TASK_KINDS.find(entry => entry.id === String(kind || '').toLowerCase())?.label ?? 'Task';
}

/**
 * Renders the task toolbar and inspector into `host`, and makes labelled cards inside `canvas`
 * draggable.
 *
 * @param host       Element that receives the toolbar and inspector markup.
 * @param canvas     Element containing the rendered DAG cards.
 * @param tasks      `[{ id, connection, body, line }]` as the host reported them.
 * @param selectedId Task to show as selected, or null.
 * @param onSelect   `(id | null) => void`
 * @param onAdd      `({ kind, after }) => Promise<void>`
 * @param onEdit     `({ id }) => Promise<void>`, opens the task editor.
 * @param onConnect  `({ from, to }) => Promise<void>`, declares that `to` runs after `from`.
 * @param onDisconnect `({ from, to }) => Promise<void>`, removes one declared edge.
 * @param onMove     `({ id, after }) => Promise<void>` — after null means "run first".
 * @param onRemove   `({ id }) => Promise<void>`
 * @param onOpenLine `(line) => void`, to reveal the task in the script.
 * @returns `{ dispose }`
 */
export function attachPipelineTaskEditing(host, canvas, {
    tasks = [],
    selectedId = null,
    onSelect = () => {},
    onAdd = async () => {},
    onEdit = async () => {},
    onConnect = async () => {},
    onDisconnect = async () => {},
    onMove = async () => {},
    onUpdate = async () => {},
    onRemove = async () => {},
    onOpenLine = () => {},
} = {}) {
    const selected = tasks.find(task => sameId(task.id, selectedId)) || null;
    const listeners = [];
    const on = (element, type, handler) => {
        element.addEventListener(type, handler);
        listeners.push(() => element.removeEventListener(type, handler));
    };

    host.innerHTML = `
        <div class="etlsql-studio-pipeline-tools">
            <div class="etlsql-studio-pipeline-palette" role="group" aria-label="Add a task">
                ${PIPELINE_TASK_KINDS.map(kind => `<button type="button"
                    class="etlsql-studio-task-chip"
                    data-task-kind="${escapeHtml(kind.id)}"
                    title="${escapeHtml(kind.hint)}">
                    <span class="etlsql-studio-task-chip-glyph" aria-hidden="true">${escapeHtml(kind.glyph)}</span>
                    <span>${escapeHtml(kind.label)}</span>
                </button>`).join('')}
            </div>
            <span class="etlsql-studio-pipeline-hint">${escapeHtml(hint(tasks, selected))}</span>
        </div>
        <div class="etlsql-studio-pipeline-inspector" data-task-inspector>${inspectorMarkup(selected)}</div>`;

    for (const chip of host.querySelectorAll('[data-task-kind]')) {
        on(chip, 'click', () => onAdd({ kind: chip.dataset.taskKind, after: selected?.id ?? null }));
    }

    const inspector = host.querySelector('[data-task-inspector]');

    const edit = inspector.querySelector('[data-task-edit]');
    if (edit) on(edit, 'click', () => onEdit({ id: selected.id }));

    const remove = inspector.querySelector('[data-task-remove]');
    if (remove) on(remove, 'click', () => onRemove({ id: selected.id }));

    const reveal = inspector.querySelector('[data-task-reveal]');
    if (reveal) on(reveal, 'click', () => onOpenLine(selected.line));

    const first = inspector.querySelector('[data-task-first]');
    if (first) on(first, 'click', () => onMove({ id: selected.id, after: null }));

    for (const chip of inspector.querySelectorAll('[data-task-disconnect]')) {
        on(chip, 'click', () => onDisconnect({ from: chip.dataset.taskDisconnect, to: selected.id }));
    }

    // ── The cards ────────────────────────────────────────────────────────────
    // Only labelled cards take part. Everything else on the map is a projection stage: real, and
    // deliberately not draggable, because the canvas cannot edit it losslessly yet.

    // What is being dragged, and whether the drag started on the card or on its connector handle.
    // Held here rather than read back off the document: the drag payload is deliberately unreadable
    // during dragover, and a component that queries the shell to find out what it is dragging has
    // reached outside its own host.
    let dragging = null;
    let draggingKind = null;

    const cards = [...canvas.querySelectorAll('[data-task-key]')];
    for (const card of cards) {
        const id = card.dataset.taskKey;
        card.classList.add('is-editable-task');
        card.draggable = true;
        card.classList.toggle('is-selected-task', sameId(id, selectedId));

        // Dragging the card body reorders; dragging this handle declares a dependency. Two gestures
        // because they mean different things: one moves a statement, the other writes a declaration
        // about what has to finish first.
        if (!card.querySelector('[data-task-connector]')) {
            const handle = card.ownerDocument.createElement('button');
            handle.type = 'button';
            handle.className = 'etlsql-dag-connector';
            handle.dataset.taskConnector = id;
            handle.draggable = true;
            handle.title = `Drag onto another task to make it run after ${id}`;
            handle.setAttribute('aria-label', `Connect ${id} to another task`);
            card.appendChild(handle);

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
            if (!dragging || sameId(id, dragging)) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = draggingKind === 'connect' ? 'link' : 'move';
            card.classList.add('is-drop-target');
        });
        on(card, 'dragleave', () => card.classList.remove('is-drop-target'));
        on(card, 'drop', event => {
            event.preventDefault();
            card.classList.remove('is-drop-target');
            const moved = event.dataTransfer.getData('text/plain') || dragging;
            if (!moved || sameId(moved, id)) return;

            if (draggingKind === 'connect') {
                // Dropping a connector onto a task declares that the task waits for the one the drag
                // started from. Several of those on one task is a join, never concurrency.
                onConnect({ from: moved, to: id });
            } else {
                // Dropping a task onto another means "run after this one". Order in the script is the
                // dependency; nothing here implies concurrency either.
                onMove({ id: moved, after: id });
            }
        });
    }

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
    if (!tasks.length) return 'No editable tasks yet. Add one from the palette, or keep writing the script — the map follows either way.';
    if (!selected) return `${tasks.length} editable task${tasks.length === 1 ? '' : 's'}. Select one to edit it, or drag it onto another to run it after that one.`;
    return `${selected.id} selected. Drag it onto another task to run it after that one.`;
}

function inspectorMarkup(task) {
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
        : [{ strong: taskKindLabel(task.kind) }];

    return `
        <div class="etlsql-studio-pipeline-selected">
            <span class="etlsql-studio-pipeline-selected-name">${escapeHtml(task.id)}</span>
            ${noteMarkup(detail, 'info')}
        </div>
        ${dependencyMarkup(task)}
        <div class="etlsql-studio-pipeline-actions">
            <button type="button" class="etlsql-studio-btn is-primary" data-task-edit>Edit</button>
            <button type="button" class="etlsql-studio-btn" data-task-first>Run first</button>
            <button type="button" class="etlsql-studio-btn" data-task-reveal>Show in script</button>
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
    const dependencies = task.dependsOn ?? [];
    if (!dependencies.length) {
        return `<p class="etlsql-studio-pipeline-deps-empty">Runs in script order. Drag another task's
            connector onto this one to make it wait for that task.</p>`;
    }

    return `<div class="etlsql-studio-pipeline-deps">
        <span>${dependencies.length === 1 ? 'Waits for' : `Waits for all ${dependencies.length}`}</span>
        ${dependencies.map(name => `<span class="etlsql-studio-dep-chip">
            <code>${escapeHtml(name)}</code>
            <button type="button" data-task-disconnect="${escapeHtml(name)}"
                aria-label="Stop waiting for ${escapeHtml(name)}" title="Remove this dependency">&times;</button>
        </span>`).join('')}
    </div>`;
}
