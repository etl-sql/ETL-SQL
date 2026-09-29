// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-pipeline-view.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-pipeline-view.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Pipeline DAG projection, scoped execution, and task selection.
 */
import { _escapeHtml, _feedback, _readErrorText, errorMessage, queryElement } from './studio-context.js';
import { renderDag } from './designer.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
import { attachPipelineTaskEditing } from './studio-pipeline-canvas.js';
export function createStudioPipelineView(hostContext) {
    function disposePipelineDag() {
        hostContext.state.pipelineTaskEditor?.dispose?.();
        hostContext.state.pipelineTaskEditor = null;
        hostContext.state.dagInstance?.dispose?.();
        hostContext.state.dagInstance = null;
        hostContext.state.dagDocumentId = null;
    }
    function paintPipelineDag(doc, graph, message, tone = 'neutral', tasks = [], connections = []) {
        if (hostContext.getActiveDoc() !== doc)
            return;
        disposePipelineDag();
        const nodes = graph?.nodes ?? graph?.Nodes ?? [];
        const edges = graph?.edges ?? graph?.Edges ?? [];
        hostContext.canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-dag-view>
                <header class="etlsql-studio-dag-head">
                    <div>
                        <strong>Pipeline execution map</strong>
                        <span>${nodes.length} stage${nodes.length === 1 ? '' : 's'} · ${edges.length} edge${edges.length === 1 ? '' : 's'}</span>
                    </div>
                    <span class="etlsql-studio-dag-status is-${_escapeHtml(tone)}" data-dag-status>${_escapeHtml(message)}</span>
                </header>
                <div class="etlsql-studio-dag-body-row">
                    <div class="etlsql-studio-pipeline-editor" data-pipeline-editor></div>
                    <div class="etlsql-studio-dag-canvas" data-dag-canvas></div>
                </div>
            </section>`;
        const dagCanvas = queryElement(hostContext.canvasContainer, '[data-dag-canvas]');
        hostContext.state.dagInstance = renderDag(dagCanvas, { nodes: nodes, edges: edges }, {
            theme: document.body.classList.contains('theme-dark') ? 'vscode' : 'portal',
            orientation: 'horizontal',
            // Steps sit 300 units apart; a 200-unit card leaves a 100-unit gutter for the connector,
            // and the zoom floor keeps both readable rather than fitting the whole pipeline as a strip.
            cardWidth: 200,
            minFitZoom: 0.85,
            onNodeClick: (_nodeId, meta) => {
                const line = Number(meta?.line ?? meta?.Line);
                if (!line || Number.isNaN(line))
                    return;
                if (doc.projection === 'canvas')
                    hostContext.setProjection('split');
                hostContext.state.editorInstance?.gotoLine?.(line);
            },
        });
        hostContext.state.dagDocumentId = doc.id;
        // The editable layer goes on after the map is drawn: it decorates the cards the projection
        // already produced rather than rendering its own copy of them, so the two can never disagree
        // about what the script contains.
        const known = new Set(tasks.map(task => String(task.id).toLowerCase()));
        if (hostContext.state.selectedTaskId && !known.has(String(hostContext.state.selectedTaskId).toLowerCase())) {
            hostContext.state.selectedTaskId = null;
        }
        hostContext.state.pipelineTaskEditor = attachPipelineTaskEditing(queryElement(hostContext.canvasContainer, '[data-pipeline-editor]'), dagCanvas, {
            tasks: tasks,
            selectedId: hostContext.state.selectedTaskId,
            onSelect: id => {
                hostContext.state.selectedTaskId = id;
                hostContext.renderVisualStage();
            },
            // `after` is the task it was dropped beside and `into` the container it was dropped
            // inside — where it lands on the map decides where in the script it is written, and
            // nothing else about the drop is remembered. There is no saved layout, so the script
            // stays the only thing that says what this pipeline is.
            onAdd: async ({ kind, after, into }) => {
                // The editor collects the whole task before anything is written, so a half-filled
                // statement never reaches the script and the author never sees a parse error
                // about syntax they did not type.
                const intent = await hostContext.openPipelineTaskEditor({
                    kind,
                    connections,
                    suggestedId: uniqueTaskId(tasks),
                    placement: { after, into },
                });
                if (!intent)
                    return;
                hostContext.state.selectedTaskId = intent.id;
                const result = (await hostContext.canonicalPipelineMutation('Add task', { op: 'add', after, into, ...intent }));
                revealWrittenTask(doc, result, intent.id);
            },
            onEdit: async ({ id }) => {
                const task = tasks.find(entry => String(entry.id).toLowerCase() === String(id).toLowerCase());
                if (!task)
                    return;
                const intent = await hostContext.openPipelineTaskEditor({ task, connections });
                if (!intent)
                    return;
                // Every field the editor returned, and only those: it sends what changed, and the
                // host rewrites each one over its own span in the statement.
                const changed = { ...intent };
                delete changed.kind;
                const result = (await hostContext.canonicalPipelineMutation('Update task', {
                    ...changed, op: 'update', id: task.id, newId: intent.id,
                }));
                if (result?.applied)
                    hostContext.state.selectedTaskId = intent.id;
            },
            onMove: async ({ id, after }) => { await hostContext.canonicalPipelineMutation('Move task', { op: 'move', id, after }); },
            // `after` names the container, so "move into" and "move out" are the same request
            // with and without one. A refusal — a PARALLEL branch that waits for a sibling, a
            // container dropped into itself — comes back with its reason like any other.
            onNest: async ({ id, container }) => {
                await hostContext.canonicalPipelineMutation(container ? 'Move into container' : 'Move out of container', { op: 'nest', id, after: container });
            },
            // `id` is the dependent and `after` the dependency, so the request reads the same way
            // the tag reads in the script: this task runs after that one.
            onConnect: async ({ from, to }) => { await hostContext.canonicalPipelineMutation('Connect tasks', { op: 'connect', id: to, after: from }); },
            // Re-declaring an existing edge is how its condition changes: the host replaces the
            // prerequisite in place and rewrites the control flow that enforces it, so there is
            // no window in which the tag and the script disagree about when the task runs.
            onSetEdge: async ({ from, to, edge, expression }) => {
                await hostContext.canonicalPipelineMutation('Set edge condition', { op: 'connect', id: to, after: from, edge, expression });
            },
            onDisconnect: async ({ from, to }) => { await hostContext.canonicalPipelineMutation('Remove dependency', { op: 'disconnect', id: to, after: from }); },
            onRemove: async ({ id }) => {
                const result = (await hostContext.canonicalPipelineMutation('Delete task', { op: 'remove', id }));
                if (result?.applied)
                    hostContext.state.selectedTaskId = null;
            },
            // The ELSE is written into the IF's own bytes, and its tasks are then dropped on the
            // ELSE card like any other container. Removing one refuses while it still holds work.
            onAddElse: async ({ id }) => { await hostContext.canonicalPipelineMutation('Add ELSE', { op: 'add-else', id }); },
            onRemoveElse: async ({ id }) => { await hostContext.canonicalPipelineMutation('Remove ELSE', { op: 'remove-else', id }); },
            onRunTo: ({ id }) => runToPipelineTask(doc, id),
            onOpenLine: line => {
                if (!line)
                    return;
                if (doc.projection === 'canvas')
                    hostContext.setProjection('split');
                hostContext.state.editorInstance?.gotoLine?.(line);
            },
            // What the selected task can see, and what the last run measured there. The scope is
            // read for the script exactly as it stands, so it follows a hand edit like the map does.
            scope: hostContext.state.selectedTaskId ? scopeFor(doc, hostContext.state.selectedTaskId) : null,
            runtime: hostContext.state.selectedTaskId ? runtimeFor(doc, hostContext.state.selectedTaskId) : null,
        });
        if (hostContext.state.selectedTaskId)
            void refreshPipelineScope(doc, hostContext.state.selectedTaskId);
    }
    /**
     * Shows the author the ETL-SQL a canvas gesture just wrote.
     *
     * This is what makes the palette a way to learn the language rather than a way to avoid it: drag
     * a chip in, fill in a form, and the statement it produced is on screen, selected, with the code
     * pane open at it. A canvas that quietly edits a file you cannot see teaches nothing.
     *
     * The line span comes from the host's own re-read of the script it just wrote, so it is where the
     * statement actually landed — not where the client assumed it would go. A refused edit reveals
     * nothing, because there is nothing to look at and the refusal has already been reported.
     */
    function revealWrittenTask(doc, result, taskId) {
        if (!result?.applied || !taskId)
            return;
        const written = (result.tasks ?? []).find((task) => String(task.id).toLowerCase() === String(taskId).toLowerCase());
        const line = Number(written?.line) || 0;
        if (!line)
            return;
        // On the canvas-only projection there is no code pane to reveal into, so open one. The whole
        // gesture is "see what it wrote", and it cannot be completed on a surface with no script.
        if (doc?.projection === 'canvas')
            hostContext.setProjection('split');
        const endLine = Number(written?.endLine) || line;
        if (hostContext.state.editorInstance?.revealLines)
            hostContext.state.editorInstance.revealLines(line, endLine);
        else
            hostContext.state.editorInstance?.gotoLine?.(line);
    }
    /**
     * Runs the pipeline through a selected task, so its variables and `#temp` tables land in Results.
     *
     * Two round trips, deliberately. The first asks the host what running to this task would execute
     * and what it would leave behind; the second is the ordinary run route, handed the slice as a
     * selection. Nothing here assembles the script, and the route that could execute is never the
     * route that asked — so the confirmation cannot be bypassed by a client that forgets to show it.
     *
     * The author is asked only when there is something to be asked about. A plan whose effects are
     * empty writes nothing outside the session, and stopping to confirm that is how a confirmation
     * stops being read by the time it matters.
     */
    async function runToPipelineTask(doc, taskId) {
        if (!doc || !taskId)
            return;
        const script = hostContext.activeScriptText();
        let plan;
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.pipelineRunPlan, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, id: taskId, documentUri: doc.path || doc.name }),
            });
            if (!response.ok) {
                _feedback.notify(await _readErrorText(response), { title: 'Could not plan the run', tone: 'error' });
                return;
            }
            plan = await response.json();
        }
        catch (error) {
            // A plan that did not arrive is not an empty plan. Running anyway would execute the whole
            // script with nothing shown to the author first, which is the one outcome to rule out.
            _feedback.notify(errorMessage(error) || 'The run plan did not arrive, so nothing was run.', { title: 'Could not plan the run', tone: 'error' });
            return;
        }
        if (!plan?.resolved) {
            _feedback.notify(plan?.error || 'That task could not be planned.', { title: 'Could not plan the run', tone: 'error' });
            return;
        }
        if ((plan.effects ?? []).length
            && !await hostContext.openPipelineRunPlanConfirm({ taskId, plan })) {
            return;
        }
        // Handed to the ordinary run path as a selection, so the slice meets the same policy, the
        // same governance preamble, and the same results plumbing as any other run. A second
        // execution path for debugging is a second place for the rules to be wrong.
        await hostContext.executeRun(doc, { script, selection: plan.script, label: `pipeline through ${taskId}` });
    }
    /** The cached scope for this task, when it was read from the script the document now holds. */
    function scopeFor(doc, taskId) {
        if (!taskId)
            return null;
        const cached = hostContext.documentContext(doc).taskScope;
        return cached
            && cached.script === doc.content
            && String(cached.taskId).toLowerCase() === String(taskId).toLowerCase()
            ? cached.data
            : null;
    }
    /**
     * Reads the scope for the selected task and repaints when the answer is new.
     *
     * Fired after the canvas is drawn rather than before it, so selecting a task never waits on a
     * round trip to show the task itself.
     */
    async function refreshPipelineScope(doc, taskId) {
        if (!taskId || scopeFor(doc, taskId))
            return;
        const context = hostContext.documentContext(doc);
        const content = doc.content;
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.pipelineScope, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: content, id: taskId, documentUri: doc.path || doc.name }),
            });
            // A host that does not serve the route leaves the panel saying it is still reading rather
            // than claiming the task has nothing in scope.
            if (!response.ok)
                return;
            context.taskScope = { script: content, taskId, data: await response.json() };
        }
        catch {
            return;
        }
        if (hostContext.getActiveDoc() === doc && doc.content === content
            && String(hostContext.state.selectedTaskId ?? '').toLowerCase() === String(taskId).toLowerCase()) {
            hostContext.renderVisualStage();
        }
    }
    /**
     * What the last run reported for this task, or null.
     *
     * Matched by name against the engine's execution tree. Only a stage the engine actually named
     * counts: inventing a zero row count for a task that has never run would read as a result.
     */
    function runtimeFor(doc, taskId) {
        if (!taskId)
            return null;
        const progress = (hostContext.documentContext(doc).resultsTrace ?? [])
            .filter(entry => entry?.type === 'progress')
            .at(-1)?.data;
        if (!Array.isArray(progress))
            return null;
        const wanted = String(taskId).toLowerCase();
        const stack = [...progress];
        while (stack.length) {
            const stage = stack.pop();
            if (!stage)
                continue;
            if (Array.isArray(stage.children))
                stack.push(...stage.children);
            if (!String(stage.name ?? '').toLowerCase().includes(wanted))
                continue;
            return {
                rows: Number.isFinite(stage.rowsProcessed) ? stage.rowsProcessed : undefined,
                durationMs: Number.isFinite(stage.durationMs) ? stage.durationMs : undefined,
                status: stage.status || undefined,
                note: stage.spilled ? 'Spilled to disk.' : undefined,
            };
        }
        return null;
    }
    /** A label that is not already taken, so Add never fails on a name the author did not choose. */
    function uniqueTaskId(tasks) {
        const taken = new Set(tasks.map((task) => String(task.id).toLowerCase()));
        let index = tasks.length + 1;
        while (taken.has(`task_${index}`))
            index++;
        return `task_${index}`;
    }
    function paintPipelineDagMessage(title, detail, tone = 'neutral') {
        disposePipelineDag();
        hostContext.canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-dag-view>
                <div class="etlsql-studio-dag-message is-${_escapeHtml(tone)}">
                    <strong>${_escapeHtml(title)}</strong>
                    <span>${_escapeHtml(detail)}</span>
                </div>
            </section>`;
    }
    async function renderPipelineDag(doc, content) {
        const context = hostContext.documentContext(doc);
        if (context.lastValidDag?.script === content) {
            paintPipelineDag(doc, context.lastValidDag.graph, 'Engine projection', 'neutral', context.lastValidDag.tasks, context.lastValidDag.connections);
            return;
        }
        const revision = ++context.dagRevision;
        context.dagAbort?.abort();
        const controller = new AbortController();
        context.dagAbort = controller;
        if (context.lastValidDag) {
            paintPipelineDag(doc, context.lastValidDag.graph, 'Updating from script…', 'pending', context.lastValidDag.tasks, context.lastValidDag.connections);
        }
        else {
            paintPipelineDagMessage('Projecting pipeline…', 'Reading control flow and validation stages from the current script.');
        }
        try {
            // The map, the editable tasks in it, and the connections a new task can run against, all
            // read from the same bytes in one round trip. Reading them separately is how a canvas
            // ends up offering an edit against a script the projection no longer matches.
            const post = (route) => hostContext.authFetch(hostContext.apiBase + route, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: content, documentUri: doc.path || doc.name, op: 'read' }),
                signal: controller.signal,
            });
            const [response, taskResponse, parseResponse] = await Promise.all([
                post(STUDIO_ROUTES.dag),
                post(STUDIO_ROUTES.pipelineTask),
                post(STUDIO_ROUTES.parse),
            ]);
            if (!response.ok)
                throw new Error(await _readErrorText(response));
            const projected = await response.json();
            if (projected?.parsed === false || projected?.error) {
                throw new Error(projected.error || 'The script could not be projected.');
            }
            if (controller.signal.aborted || context.dagRevision !== revision || hostContext.getActiveDoc() !== doc || doc.content !== content)
                return;
            // A host that does not serve the editing routes still gets the read-only map: the canvas
            // simply offers no editable tasks, rather than failing to draw.
            const tasks = taskResponse.ok ? ((await taskResponse.json())?.tasks ?? []) : [];
            const connections = parseResponse.ok ? ((await parseResponse.json())?.designState?.connections ?? []) : [];
            const graph = projected?.dag || projected || { nodes: [], edges: [] };
            context.lastValidDag = { script: content, graph, tasks, connections };
            paintPipelineDag(doc, graph, 'Engine projection', 'neutral', tasks, connections);
        }
        catch (error) {
            if (controller.signal.aborted || context.dagRevision !== revision || hostContext.getActiveDoc() !== doc)
                return;
            const detail = errorMessage(error) || String(error);
            if (context.lastValidDag) {
                paintPipelineDag(doc, context.lastValidDag.graph, `Last valid flow · ${detail}`, 'warning', context.lastValidDag.tasks, context.lastValidDag.connections);
            }
            else {
                paintPipelineDagMessage('Pipeline projection failed', detail, 'error');
            }
        }
        finally {
            if (context.dagAbort === controller)
                context.dagAbort = null;
        }
    }
    return { disposePipelineDag, renderPipelineDag };
}
