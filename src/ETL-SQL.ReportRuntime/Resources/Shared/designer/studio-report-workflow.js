/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-report-workflow.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Guided report workflow selection and navigation.
 */
import { _escapeHtml, _feedback, _studioIcon, asStudioDesignState, controlChecked, errorMessage, getStoredProjectionPreference, queryElement, queryElements } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
export function createStudioReportWorkflow(hostContext) {
    const STUDIO_RAIL_PREFERENCE = 'etlsql-studio-guided-rail';
    function explicitReportWorkflow(script, designState) {
        const declaredModes = [];
        const pattern = /\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?PAGE\b[\s\S]*?\bAS\s+(DASHBOARD|PAGINATED)\b/gi;
        let match;
        while ((match = pattern.exec(script || '')) !== null) {
            const mode = (match[1] || '').toLowerCase();
            if (mode === 'dashboard' || mode === 'paginated')
                declaredModes.push(mode);
        }
        if (!declaredModes.length)
            return null;
        const parsedModes = (designState?.pages || []).map(page => String(page.mode || '').toLowerCase());
        if (parsedModes.length !== declaredModes.length)
            return null;
        return declaredModes.every(mode => mode === declaredModes[0]) ? declaredModes[0] : null;
    }
    async function promptForReportWorkflow(doc) {
        return await new Promise(resolve => {
            const titleId = 'etlsql-workflow-choice-title';
            hostContext.modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <div><span class="etlsql-studio-kicker">Choose an authoring surface</span><h2 id="${titleId}">${_escapeHtml(doc.name)}</h2></div>
                </div>
                <div class="etlsql-studio-modal-body etlsql-workflow-choice" role="group" aria-label="Report workflow">
                    <p>This file does not say which kind of report it is, so Studio does not know which tools to offer. The choice changes Studio's tools only — the script is not edited either way, and you can change your mind from the canvas at any time.</p>
                    <button type="button" data-choose-workflow="dashboard"><strong>Dashboard</strong><span>Responsive canvas for charts, KPIs, tables, slicers, and cross-filtering.</span></button>
                    <button type="button" data-choose-workflow="paginated"><strong>Paginated Report</strong><span>Physical pages for parameters, detail rows, totals, headers, footers, and export.</span></button>
                    <div class="etlsql-studio-modal-actions">
                        <button type="button" class="etlsql-studio-btn" data-choose-workflow-cancel>Not now</button>
                    </div>
                </div>`;
            let settled = false;
            let cleanupModal = null;
            const finish = (workflow) => {
                if (settled)
                    return;
                settled = true;
                cleanupModal?.();
                hostContext.modalBox.innerHTML = '';
                resolve(workflow);
            };
            cleanupModal = hostContext.setupModalAccessibility(hostContext.modalBox, hostContext.modalBackdrop, () => finish(null), titleId);
            queryElement(hostContext.modalBox, '[data-choose-workflow-cancel]').addEventListener('click', () => finish(null));
            queryElements(hostContext.modalBox, '[data-choose-workflow]').forEach(button => {
                button.addEventListener('click', () => {
                    const workflow = button.dataset.chooseWorkflow;
                    finish(workflow === 'dashboard' || workflow === 'paginated' ? workflow : null);
                });
            });
            queryElement(hostContext.modalBox, '[data-choose-workflow]')?.focus();
        });
    }
    async function ensureReportWorkflow(doc, { askWhenAmbiguous = true } = {}) {
        if (!doc || !(doc.path || '').toLowerCase().endsWith('.rptsql'))
            return null;
        if (doc.reportWorkflow)
            return doc.reportWorkflow;
        // Asked once. An author who dismissed the question gets the canvas restore strip instead of
        // the same modal on every tab switch, which is how a dismissible prompt becomes a nag.
        if (doc.reportWorkflowDeclined)
            return null;
        const parsed = await hostContext.designerApiJson(STUDIO_ROUTES.parse, { script: doc.content || '' });
        if (parsed.error)
            return null;
        const inferred = explicitReportWorkflow(doc.content, asStudioDesignState(parsed.designState));
        if (!inferred && !askWhenAmbiguous)
            return null;
        const chosen = inferred || await promptForReportWorkflow(doc);
        if (!chosen) {
            doc.reportWorkflowDeclined = true;
            return null;
        }
        doc.reportWorkflow = chosen;
        return doc.reportWorkflow;
    }
    function pageSetupMarkup(page = {}) {
        const layout = page.printLayout || {};
        const detailTable = (page.visuals || []).find(visual => visual.type === 'TABLE');
        const breaksAfterDetails = /PAGE_BREAK_AFTER\s*=\s*ON/i.test(detailTable?.options?.print_layout || '');
        const size = layout.pageSize || 'Letter';
        const orientation = layout.orientation || 'PORTRAIT';
        const margin = layout.marginTop ?? 0.75;
        return `<div class="etlsql-paginated-setup">
            <label>Page size<select data-page-setup="pageSize"><option ${size === 'Letter' ? 'selected' : ''}>Letter</option><option ${size === 'A4' ? 'selected' : ''}>A4</option><option ${size === 'Legal' ? 'selected' : ''}>Legal</option></select></label>
            <label>Orientation<select data-page-setup="orientation"><option value="PORTRAIT" ${orientation === 'PORTRAIT' ? 'selected' : ''}>Portrait</option><option value="LANDSCAPE" ${orientation === 'LANDSCAPE' ? 'selected' : ''}>Landscape</option></select></label>
            <label>Margins (in)<input type="number" min="0" max="3" step="0.125" value="${_escapeHtml(margin)}" data-page-setup="margin"></label>
            <label class="etlsql-page-break-toggle"><input type="checkbox" data-page-break-after ${breaksAfterDetails ? 'checked' : ''}>Break after details</label>
        </div>`;
    }
    // ---------------------------------------------------------------------------------------------
    // Guided workflow steps
    //
    // A numbered step is a teaching surface, not a shortcut. Clicking one opens a dialog that names
    // the concept, collects the inputs, shows the exact Report-SQL it is about to write, and only
    // then patches the script. A step that cannot run yet says what is missing and offers the control
    // that fixes it, rather than writing a half-formed statement or failing into a toast the author
    // has no way to act on.
    // ---------------------------------------------------------------------------------------------
    /**
     * Opens a Studio dialog and resolves with whatever `api.close(value)` is given, or null when the
     * author dismisses it. `controller(api)` drives the content: `api.render({ lede, body, actions,
     * wire })` paints the body and footer, so a multi-pane step just calls `render` again.
     */
    // Guided authoring surfaces live in studio-authoring.js; this shell composes them below.
    function guidedRailToggleMarkup() {
        return (hostContext.state.guidedRailHidden ?? guidedRailHidden())
            ? `<button type="button" class="etlsql-studio-rail-restore" data-show-rail>${_studioIcon('commands', 13)} Show guided steps</button>`
            : '';
    }
    function wireGuidedRailToggle(host) {
        queryElement(host, '[data-show-rail]')?.addEventListener('click', () => setGuidedRailHidden(false));
    }
    function guidedRailHidden() {
        try {
            return localStorage.getItem(STUDIO_RAIL_PREFERENCE) === 'hidden';
        }
        catch {
            // Private browsing and locked-down hosts throw on access; showing the rail is the safe
            // default because it is discoverable and dismissible again.
            return false;
        }
    }
    function setGuidedRailHidden(hidden) {
        try {
            localStorage.setItem(STUDIO_RAIL_PREFERENCE, hidden ? 'hidden' : 'shown');
        }
        catch {
            // A preference that cannot persist still applies for this session.
        }
        hostContext.state.guidedRailHidden = hidden;
        renderReportWorkflowChrome(hostContext.getActiveDoc());
        hostContext.renderSidebarContent(hostContext.state.activeActivity);
        if (!hidden)
            _feedback.notify('Guided steps are back on the report toolbar.', { title: 'Guided steps', tone: 'info' });
    }
    function renderReportWorkflowChrome(doc, designState = hostContext.state.designerInstance?.getState?.()) {
        const workflow = doc?.reportWorkflow;
        const isReport = Boolean(doc && (doc.path || '').toLowerCase().endsWith('.rptsql'));
        const hidden = hostContext.state.guidedRailHidden ?? guidedRailHidden();
        hostContext.workflowBar.hidden = !isReport;
        hostContext.visualStage.classList.toggle('is-dashboard-workflow', isReport && workflow === 'dashboard');
        hostContext.visualStage.classList.toggle('is-paginated-workflow', isReport && workflow === 'paginated');
        if (!isReport) {
            hostContext.workflowBar.innerHTML = '';
            return;
        }
        // Dismissing the rail hides the teaching, not the way back to it. The restore sits on the
        // canvas where the rail was, because a control that only exists in a collapsed sidebar panel
        // is the same as no control for the author who most needs it. The same strip carries the
        // surface choice for a report whose mode nobody has picked yet: declining that question once
        // must not cost the author the tools for the rest of the session.
        if (!workflow || hidden) {
            hostContext.workflowBar.classList.add('is-collapsed');
            hostContext.workflowBar.innerHTML = workflow
                ? `<span>Guided steps are hidden. Every action they run is also in the sidebar's Build section.</span>
                   <button type="button" class="etlsql-studio-rail-restore" data-show-rail>${_studioIcon('commands', 13)} Show guided steps</button>`
                : `<span>No authoring surface chosen, so the guided steps are off. This changes Studio's tools only; the script is untouched either way.</span>
                   <button type="button" class="etlsql-studio-rail-restore" data-choose-surface>${_studioIcon('commands', 13)} Choose Dashboard or Report</button>`;
            wireGuidedRailToggle(hostContext.workflowBar);
            queryElement(hostContext.workflowBar, '[data-choose-surface]')?.addEventListener('click', async () => {
                if (!doc)
                    return;
                const chosen = await promptForReportWorkflow(doc);
                if (!chosen)
                    return;
                doc.reportWorkflow = chosen;
                if (chosen === 'dashboard' && doc.projection !== 'canvas') {
                    const pref = getStoredProjectionPreference();
                    if (pref !== 'code' && pref !== 'split') {
                        hostContext.setProjection('canvas');
                    }
                }
                renderReportWorkflowChrome(doc);
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            });
            return;
        }
        hostContext.workflowBar.classList.remove('is-collapsed');
        // Every step reports whether it is already satisfied, so the rail doubles as a checklist of
        // what this report still needs rather than eight identical buttons.
        const visuals = (designState?.pages || []).flatMap(page => page.visuals || []);
        const hasParameters = Boolean(designState?.parameters?.length);
        const detailTable = visuals.find(visual => visual.type === 'TABLE');
        const done = {
            catalog: hostContext.hasDataSample(),
            parameter: hasParameters,
            details: Boolean(detailTable),
            totals: Boolean(detailTable?.options?.GRAND_TOTAL),
            furniture: visuals.some(visual => visual.type === 'TEXT'),
            palette: visuals.length > 0,
            filters: Object.keys(hostContext.activeDocumentContext().activeFilters || {}).length > 0 || Boolean(hostContext.activeDocumentContext().skipCrossFilters),
        };
        const stepClass = (key) => (done[key] ? ' class="is-done"' : '');
        if (workflow === 'dashboard') {
            hostContext.workflowBar.innerHTML = `<div class="etlsql-workflow-identity"><span class="etlsql-workflow-kind">Dashboard</span><strong>Responsive visual canvas</strong><span>Build the story with chart, KPI, table, and slicer tiles. Use filters for cross-visual interaction; use Format on the selected tile for presentation.</span></div><ol class="etlsql-workflow-steps" aria-label="Dashboard workflow"><li><button type="button" data-workflow-step="catalog"${stepClass('catalog')}><b>1</b><span><strong>Data</strong><small>Connection, dataset, fields</small><code class="etlsql-workflow-syntax-tag">CREATE CONNECTION</code></span></button></li><li><button type="button" data-workflow-step="palette"${stepClass('palette')}><b>2</b><span><strong>Visuals</strong><small>Charts, KPIs, tables, slicers</small><code class="etlsql-workflow-syntax-tag">CREATE VISUAL</code></span></button></li><li><button type="button" data-workflow-step="filters"${stepClass('filters')}><b>3</b><span><strong>Cross-filters</strong><small>Narrow every visual at once</small><code class="etlsql-workflow-syntax-tag">FILTER / SLICER</code></span></button></li><li><button type="button" data-workflow-step="layout"><b>4</b><span><strong>Layout</strong><small>Arrange tiles on the canvas</small><code class="etlsql-workflow-syntax-tag">LAYOUT (...)</code></span></button></li><li><button type="button" data-workflow-step="format"><b>5</b><span><strong>Format + code</strong><small>Style the selection beside the script</small><code class="etlsql-workflow-syntax-tag">OPTIONS / MAPPINGS</code></span></button></li></ol>`;
        }
        else {
            const page = designState?.pages?.[0] || {};
            hostContext.workflowBar.innerHTML = `<div class="etlsql-workflow-identity"><span class="etlsql-workflow-kind">Paginated Report</span><strong>Physical page authoring</strong><span>Work top-to-bottom: prompts, repeating detail, totals, page furniture, then pagination and export.</span></div><ol class="etlsql-workflow-steps etlsql-paginated-steps"><li><button type="button" data-workflow-step="catalog"${stepClass('catalog')}><b>1</b><span><strong>Choose data</strong><small>Connection, dataset, fields</small></span></button></li><li><button type="button" data-workflow-step="parameter"${stepClass('parameter')}><b>2</b><span><strong>Define parameters</strong><small>Input prompts before execution</small></span></button></li><li><button type="button" data-workflow-step="details"${stepClass('details')}><b>3</b><span><strong>Groups + details</strong><small>Matrix groups and table rows</small></span></button></li><li><button type="button" data-workflow-step="totals"${stepClass('totals')}><b>4</b><span><strong>Add totals</strong><small>Grand total on the detail table</small></span></button></li><li><button type="button" data-workflow-step="furniture"${stepClass('furniture')}><b>5</b><span><strong>Header + footer</strong><small>Text bands and page breaks</small></span></button></li><li><div><b>6</b><span><strong>Page setup + breaks</strong><small>Writes PRINT_LAYOUT through the patcher</small></span>${pageSetupMarkup(page)}</div></li><li><button type="button" data-workflow-step="preview"><b>7</b><span><strong>Preview pagination</strong><small>Run and inspect physical pages</small></span></button></li><li><button type="button" data-workflow-step="export"><b>8</b><span><strong>Export</strong><small>PDF for pages; CSV/Excel for results</small></span></button></li></ol>`;
        }
        const dismiss = document.createElement('button');
        dismiss.type = 'button';
        dismiss.className = 'etlsql-workflow-dismiss';
        dismiss.dataset.dismissRail = '';
        dismiss.setAttribute('aria-label', 'Hide guided steps');
        dismiss.title = 'Hide guided steps — the Build section in the sidebar keeps every action';
        dismiss.textContent = '×';
        dismiss.addEventListener('click', () => setGuidedRailHidden(true));
        hostContext.workflowBar.appendChild(dismiss);
        queryElements(hostContext.workflowBar, '[data-page-setup]').forEach(control => control.addEventListener('change', async () => {
            await hostContext.canonicalDesignerMutation('Update page setup', designState => {
                const design = asStudioDesignState(designState);
                const page = design.pages?.[0];
                if (!page)
                    return false;
                page.mode = 'Paginated';
                page.printLayout ||= { pageSize: 'Letter', orientation: 'PORTRAIT', marginTop: 0.75, marginRight: 0.75, marginBottom: 0.75, marginLeft: 0.75, units: 'in', overflow: 'SPLIT' };
                const value = control.value;
                if (control.dataset.pageSetup === 'margin') {
                    const margin = Number(value);
                    page.printLayout.marginTop = margin;
                    page.printLayout.marginRight = margin;
                    page.printLayout.marginBottom = margin;
                    page.printLayout.marginLeft = margin;
                }
                else if (control.dataset.pageSetup)
                    page.printLayout[control.dataset.pageSetup] = value;
                return true;
            });
        }));
        queryElement(hostContext.workflowBar, '[data-page-break-after]')?.addEventListener('change', async (event) => {
            await hostContext.canonicalDesignerMutation('Update detail page break', design => {
                const table = (design.pages || []).flatMap(page => page.visuals || []).find(visual => visual.type === 'TABLE');
                if (!table)
                    throw new Error('Add a detail table before configuring its page break.');
                table.options ||= {};
                if (controlChecked(event))
                    table.options.print_layout = 'PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)';
                else
                    delete table.options.print_layout;
                return true;
            });
        });
        const steps = {
            catalog: hostContext.runChooseDataStep,
            parameter: hostContext.runParameterStep,
            details: hostContext.runDetailsStep,
            totals: hostContext.runTotalsStep,
            furniture: hostContext.runFurnitureStep,
            preview: hostContext.runPreviewStep,
            export: hostContext.runExportStep,
            palette: hostContext.runVisualsStep,
            filters: hostContext.runCrossFilterStep,
            layout: async () => hostContext.setProjection('canvas'),
            format: async () => hostContext.setProjection('split'),
        };
        queryElements(hostContext.workflowBar, '[data-workflow-step]').forEach(button => button.addEventListener('click', async () => {
            const step = button.dataset.workflowStep ? steps[button.dataset.workflowStep] : undefined;
            if (!step)
                return;
            try {
                await step();
                const currentDoc = hostContext.getActiveDoc();
                if (currentDoc)
                    renderReportWorkflowChrome(currentDoc);
            }
            catch (error) {
                // A step that throws must say so. Swallowing it here is what made these buttons look
                // dead in the first place.
                _feedback.notify(errorMessage(error) || 'The step could not be completed.', { title: 'Step failed', tone: 'error' });
            }
        }));
    }
    return { explicitReportWorkflow, ensureReportWorkflow, guidedRailToggleMarkup, wireGuidedRailToggle, renderReportWorkflowChrome };
}
