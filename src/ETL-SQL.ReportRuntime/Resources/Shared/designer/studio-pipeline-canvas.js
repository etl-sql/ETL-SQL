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
 * Renders the task toolbar and inspector into `host`, and makes labelled cards inside `canvas`
 * draggable.
 *
 * @param host       Element that receives the toolbar and inspector markup.
 * @param canvas     Element containing the rendered DAG cards.
 * @param tasks      `[{ id, connection, body, line }]` as the host reported them.
 * @param selectedId Task to show as selected, or null.
 * @param onSelect   `(id | null) => void`
 * @param onAdd      `({ after }) => Promise<void>`
 * @param onMove     `({ id, after }) => Promise<void>` — after null means "run first".
 * @param onUpdate   `({ id, newId, connection }) => Promise<void>`
 * @param onRemove   `({ id }) => Promise<void>`
 * @param onOpenLine `(line) => void`, to reveal the task in the script.
 * @returns `{ dispose }`
 */
export function attachPipelineTaskEditing(host, canvas, {
    tasks = [],
    selectedId = null,
    onSelect = () => {},
    onAdd = async () => {},
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
            <button type="button" class="etlsql-studio-btn is-primary" data-task-add>Add task</button>
            <span class="etlsql-studio-pipeline-hint">${escapeHtml(hint(tasks, selected))}</span>
        </div>
        <div class="etlsql-studio-pipeline-inspector" data-task-inspector>${inspectorMarkup(selected)}</div>`;

    on(host.querySelector('[data-task-add]'), 'click', () => onAdd({ after: selected?.id ?? null }));

    const inspector = host.querySelector('[data-task-inspector]');
    const field = name => inspector.querySelector(`[data-task-field="${name}"]`);

    const save = inspector.querySelector('[data-task-save]');
    if (save) {
        on(save, 'click', () => onUpdate({
            id: selected.id,
            newId: field('label').value.trim(),
            connection: field('connection').value.trim(),
        }));
    }

    const remove = inspector.querySelector('[data-task-remove]');
    if (remove) on(remove, 'click', () => onRemove({ id: selected.id }));

    const reveal = inspector.querySelector('[data-task-reveal]');
    if (reveal) on(reveal, 'click', () => onOpenLine(selected.line));

    const first = inspector.querySelector('[data-task-first]');
    if (first) on(first, 'click', () => onMove({ id: selected.id, after: null }));

    // ── The cards ────────────────────────────────────────────────────────────
    // Only labelled cards take part. Everything else on the map is a projection stage: real, and
    // deliberately not draggable, because the canvas cannot edit it losslessly yet.

    // The task being dragged. Held here rather than read back off the document: the drag payload is
    // deliberately unreadable during dragover, and a component that queries the shell to find out
    // what it is dragging has reached outside its own host.
    let dragging = null;

    const cards = [...canvas.querySelectorAll('[data-task-key]')];
    for (const card of cards) {
        const id = card.dataset.taskKey;
        card.classList.add('is-editable-task');
        card.draggable = true;
        card.classList.toggle('is-selected-task', sameId(id, selectedId));

        on(card, 'click', () => onSelect(id));
        on(card, 'dragstart', event => {
            dragging = id;
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', id);
            card.classList.add('is-dragging-task');
        });
        on(card, 'dragend', () => {
            dragging = null;
            card.classList.remove('is-dragging-task');
            cards.forEach(other => other.classList.remove('is-drop-target'));
        });
        on(card, 'dragover', event => {
            if (!dragging || sameId(id, dragging)) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            card.classList.add('is-drop-target');
        });
        on(card, 'dragleave', () => card.classList.remove('is-drop-target'));
        on(card, 'drop', event => {
            event.preventDefault();
            card.classList.remove('is-drop-target');
            const moved = event.dataTransfer.getData('text/plain') || dragging;
            // Dropping a task onto another means "run after this one". Order in the script is the
            // dependency; nothing here implies concurrency.
            if (moved && !sameId(moved, id)) onMove({ id: moved, after: id });
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
    if (!tasks.length) return 'No editable tasks yet. Add one, or keep writing the script — the map follows either way.';
    if (!selected) return `${tasks.length} editable task${tasks.length === 1 ? '' : 's'}. Select one to rename it, or drag it onto another to run it after that one.`;
    return `Editing ${selected.id}. Drag it onto another task to run it after that one.`;
}

function inspectorMarkup(task) {
    if (!task) {
        return noteMarkup([
            'A task is a labelled ',
            { code: 'EXECUTE <connection> BEGIN … END' },
            ' block. Other stages on this map come from statements the canvas does not edit yet, so they are shown but not draggable.',
        ], 'info');
    }

    return `
        <div class="etlsql-studio-pipeline-fields">
            <label>Label
                <input type="text" data-task-field="label" value="${escapeHtml(task.id)}" spellcheck="false">
            </label>
            <label>Connection
                <input type="text" data-task-field="connection" value="${escapeHtml(task.connection)}" spellcheck="false">
            </label>
        </div>
        <div class="etlsql-studio-pipeline-actions">
            <button type="button" class="etlsql-studio-btn is-primary" data-task-save>Apply</button>
            <button type="button" class="etlsql-studio-btn" data-task-first>Run first</button>
            <button type="button" class="etlsql-studio-btn" data-task-reveal>Show in script</button>
            <button type="button" class="etlsql-studio-btn is-danger" data-task-remove>Delete</button>
        </div>
        ${noteMarkup([
            'The block body is edited in the script for now. ',
            { code: `${task.id}:` },
            ' is what keeps this box the same box after a hand edit.',
        ], 'info')}`;
}
