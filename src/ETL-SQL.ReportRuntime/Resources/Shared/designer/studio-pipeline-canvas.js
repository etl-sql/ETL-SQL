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
    Object.freeze({
        id: 'parallel',
        label: 'Parallel',
        glyph: '⇉',
        container: true,
        hint: 'A block whose tasks all start at the same time. This is the only thing in ETL-SQL that means concurrency.',
    }),
    Object.freeze({
        id: 'foreach',
        label: 'For each',
        glyph: '↻',
        container: true,
        hint: 'A block run once per item of a list, a #temp table, or a query.',
    }),
    Object.freeze({
        id: 'transaction',
        label: 'Transaction',
        glyph: '⛨',
        container: true,
        hint: 'A block that commits as one unit and rolls back if anything inside it fails.',
    }),
]);

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
 * @param onSetEdge  `({ from, to, edge, expression }) => Promise<void>`, changes one edge's condition.
 * @param onDisconnect `({ from, to }) => Promise<void>`, removes one declared edge.
 * @param onMove     `({ id, after }) => Promise<void>` — after null means "run first".
 * @param onNest     `({ id, container }) => Promise<void>` — container null means "move out".
 * @param onRemove   `({ id }) => Promise<void>`
 * @param onRunTo    `({ id }) => Promise<void>`, executes the pipeline through this task.
 * @param onOpenLine `(line) => void`, to reveal the task in the script.
 * @param scope      `{ resolved, error, variables, tempTables }` in scope where the task sits, or null.
 * @param runtime    `{ rows, durationMs, note }` the last run reported for it, or null.
 * @returns `{ dispose }`
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
        <div class="etlsql-studio-pipeline-inspector" data-task-inspector>${inspectorMarkup(selected, Boolean(onRunTo))}</div>`;

    for (const chip of host.querySelectorAll('[data-task-kind]')) {
        on(chip, 'click', () => onAdd({ kind: chip.dataset.taskKind, after: selected?.id ?? null }));
    }

    const inspector = host.querySelector('[data-task-inspector]');
    if (selected) {
        inspector.insertAdjacentHTML('beforeend', scopeMarkup(scope, runtime));
        for (const link of inspector.querySelectorAll('[data-scope-line]')) {
            on(link, 'click', () => onOpenLine(Number(link.dataset.scopeLine) || 0));
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
        on(chip, 'click', () => onDisconnect({ from: chip.dataset.taskDisconnect, to: selected.id }));
    }

    // ── Edge conditions ──────────────────────────────────────────────────────
    // Choosing `When…` does not send anything: the edge is not describable until the expression is
    // typed, and writing a gate on an empty condition would be a change the author did not make.
    for (const picker of inspector.querySelectorAll('[data-task-edge]')) {
        const from = picker.dataset.taskEdge;
        const field = inspector.querySelector(`[data-task-expression="${cssEscape(from)}"]`);
        on(picker, 'change', () => {
            if (picker.value !== 'expression') {
                void onSetEdge({ from, to: selected.id, edge: picker.value });
                return;
            }
            if (field) {
                field.hidden = false;
                field.focus();
                field.select();
            }
        });
    }

    for (const field of inspector.querySelectorAll('[data-task-expression]')) {
        const from = field.dataset.taskExpression;
        const commit = () => {
            const expression = field.value.trim();
            if (!expression || expression === field.dataset.taskExpressionValue) return;
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

    // What is being dragged, and whether the drag started on the card or on its connector handle.
    // Held here rather than read back off the document: the drag payload is deliberately unreadable
    // during dragover, and a component that queries the shell to find out what it is dragging has
    // reached outside its own host.
    let dragging = null;
    let draggingKind = null;

    const containers = new Set(tasks
        .filter(task => isContainerKind(task.kind))
        .map(task => String(task.id).toLowerCase()));

    const cards = [...canvas.querySelectorAll('[data-task-key]')];
    for (const card of cards) {
        const id = card.dataset.taskKey;
        card.classList.add('is-editable-task');
        card.draggable = true;
        card.classList.toggle('is-selected-task', sameId(id, selectedId));
        card.classList.toggle('is-container-task', containers.has(String(id).toLowerCase()));

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
            ' bound to the current one.',
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
