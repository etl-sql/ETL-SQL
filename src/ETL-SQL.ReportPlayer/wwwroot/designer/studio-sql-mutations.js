// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-sql-mutations.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Canonical Report-SQL mutations initiated by Studio controls.
 */

export function createStudioSqlMutationService({
    state,
    getActiveDocument,
    activeDocumentContext,
    designerApiJson,
    routes,
    renderVisualStage,
    renderWorkflow,
    renderTabs,
    offerUndo,
    feedback
}) {
    function filterContract(field, filter) {
        return {
            id: filter.id || field,
            column: field,
            kind: filter.kind,
            values: filter.values || null,
            minimum: filter.minimum == null ? null : String(filter.minimum),
            maximum: filter.maximum == null ? null : String(filter.maximum),
            // Absent means "between", which is what every filter meant before conditions existed.
            operator: filter.operator || null,
            parameterName: filter.parameterName || null,
            parameterOperator: filter.parameterOperator || null,
            allValue: filter.allValue || null
        };
    }

    async function composeFilteredSource(source, filters, asVisualSource = true) {
        const result = await designerApiJson(routes.queryFilter, { source, filters, asVisualSource });
        if (typeof result.source !== 'string') throw new Error('The filter service returned no query source.');
        return result.source;
    }

    function matchingFilters(context, scope, target) {
        return Object.entries(context.activeFilters)
            .filter(([, filter]) => filter?.scope === scope && filter?.target === target)
            .map(([field, filter]) => filterContract(field, filter));
    }

    function findDesignerVisual(designState, visualId) {
        const visuals = (designState.pages || []).flatMap(page => page.visuals || []);
        return visuals.find(visual => visual.id === visualId || visual.name === visualId) || null;
    }

    function resolveFilterTarget(designState, filter) {
        if (filter.scope === 'dataset') {
            const snapshotName = String(activeDocumentContext().snapshot?.source || '').replace(/^[&#]/, '');
            const dataset = (designState.datasets || []).find(item => item.name === filter.target || item.id === filter.target)
                || (designState.datasets || []).find(item => String(item.name || '').replace(/^[&#]/, '') === snapshotName)
                || designState.datasets?.[0];
            if (!dataset) throw new Error('Add or select a CREATE DATASET before applying a dataset-global filter.');
            return { scope: 'dataset', target: dataset.name, source: dataset.query, item: dataset };
        }

        const visual = findDesignerVisual(designState, filter.target || state.selectedVisualId);
        if (!visual) throw new Error('Select a visual before applying a visual-local filter.');
        const source = visual.options?.inline_source || visual.dataset || activeDocumentContext().snapshot?.source;
        if (!source) throw new Error(`Visual ${visual.name} has no filterable source.`);
        return { scope: 'visual', target: visual.name, source, item: visual };
    }

    function uniqueVisualName(designState, baseName) {
        const names = new Set((designState.pages || []).flatMap(page => page.visuals || []).map(visual => visual.name.toLowerCase()));
        let candidate = baseName;
        let suffix = 2;
        while (names.has(candidate.toLowerCase())) candidate = `${baseName}_${suffix++}`;
        return candidate;
    }

    function canonicalDesignerMutation(label, mutate) {
        const document = getActiveDocument();
        if (!document) return Promise.resolve(null);
        const context = document.studioContext;
        context.patchQueue ||= Promise.resolve();
        context.patchQueue = context.patchQueue.catch(() => {}).then(async () => {
            const script = getActiveDocument() === document && state.editorInstance ? state.editorInstance.getValue() : document.content;
            const parsed = await designerApiJson(routes.parse, { script });
            if (parsed.error) throw new Error(parsed.error);
            const designState = parsed.designState || { pages: [], datasets: [], bookmarks: null, parameters: null };
            if (!designState.pages?.length) designState.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
            const mutationResult = await mutate(designState);
            const patched = await designerApiJson(routes.patch, { script, designState });
            if (typeof patched.script !== 'string') throw new Error('The canonical patcher returned no script.');

            document.content = patched.script;
            document.isDirty = patched.script !== script || document.isDirty;
            if (getActiveDocument() === document) {
                const changed = state.editorInstance?.replaceAll?.(patched.script);
                if (changed) state.editorInstance?.revealRange?.(changed.from, changed.to);
                const applied = await state.designerInstance?.applyScriptText?.(patched.script);
                renderVisualStage();
                renderWorkflow(document, applied?.designState);
                offerUndo?.(label, { document, before: script, after: patched.script });
            }
            renderTabs();
            return mutationResult;
        }).catch(error => {
            feedback.notify(`${label} failed: ${error.message}`, { title: 'Script Not Changed', tone: 'error' });
            return null;
        });
        return context.patchQueue;
    }

    /**
     * The canonical pipeline mutation: one edit to one labelled task, by label.
     *
     * The report path is parse → edit design state → patch. A pipeline task has no design state: the
     * script *is* the model, so the host does the edit on the exact bytes in the buffer and hands
     * back either a new script or the reason it refused. The refusal is raised rather than absorbed,
     * because a canvas that redraws unchanged after a refused edit is indistinguishable from one
     * that applied it.
     */
    function canonicalPipelineMutation(label, operation) {
        return canonicalScriptMutation(label, routes.pipelineTask, operation);
    }

    /**
     * A script-shaped edit: the host rewrites the exact bytes in the buffer and answers with either
     * a new script or the reason it refused. Shared by every surface whose model *is* the script —
     * the pipeline canvas and the governance panel — because both need the same three things the
     * design-state path cannot give them: an edit applied to the author's own text, a refusal that
     * is raised rather than absorbed, and an undo offer covering exactly the write that happened.
     */
    function canonicalScriptMutation(label, route, operation) {
        const document = getActiveDocument();
        if (!document) return Promise.resolve(null);
        const context = document.studioContext;
        context.patchQueue ||= Promise.resolve();
        context.patchQueue = context.patchQueue.catch(() => {}).then(async () => {
            const script = getActiveDocument() === document && state.editorInstance
                ? state.editorInstance.getValue()
                : document.content;

            const result = await designerApiJson(route, { script, ...operation });
            if (!result.applied) throw new Error(result.error || 'The edit was refused.');
            if (typeof result.script !== 'string') throw new Error('The host returned no script.');
            if (result.script === script) return result;

            document.content = result.script;
            document.isDirty = true;
            if (getActiveDocument() === document) {
                const changed = state.editorInstance?.replaceAll?.(result.script);
                if (changed) state.editorInstance?.revealRange?.(changed.from, changed.to);
                renderVisualStage();
                offerUndo?.(label, { document, before: script, after: result.script });
            }
            renderTabs();
            return result;
        }).catch(error => {
            feedback.notify(`${label} failed: ${error.message}`, { title: 'Script Not Changed', tone: 'error' });
            return null;
        });
        return context.patchQueue;
    }

    function persistFilter(field, removedFilter = null) {
        const context = activeDocumentContext();
        const filter = removedFilter || context.activeFilters[field];
        if (!filter) return Promise.resolve(null);
        return canonicalDesignerMutation(`Apply ${field} filter`, async designState => {
            const resolved = resolveFilterTarget(designState, filter);
            filter.target = resolved.target;
            const contracts = matchingFilters(context, resolved.scope, resolved.target);
            const source = await composeFilteredSource(resolved.source, contracts, resolved.scope === 'visual');
            if (resolved.scope === 'dataset') resolved.item.query = source;
            else {
                resolved.item.options ||= {};
                resolved.item.options.inline_source = source;
            }
            return resolved.target;
        });
    }

    return {
        canonicalDesignerMutation,
        canonicalPipelineMutation,
        canonicalScriptMutation,
        composeFilteredSource,
        filterContract,
        findDesignerVisual,
        matchingFilters,
        persistFilter,
        resolveFilterTarget,
        uniqueVisualName
    };
}
