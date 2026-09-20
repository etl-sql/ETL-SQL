// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-preview.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-preview.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report preview, pagination, parameters, PDF export, and interactions.
 */
import { asHtml, asInput } from './studio-authoring-context.js';
import { escapeHtml, noteMarkup as guidedNoteMarkup } from './studio-authoring-ui.js';
export function createStudioAuthoringPreview(hostContext) {
    /**
     * Runs the report and shows the pages it would print.
     *
     * <p>The canvas draws a page-width sheet, which is a hint about paper and not an answer to "how
     * many pages, and what lands on each" — the question a paginated report is written to answer.
     * The engine already compiles that breakdown for the export; this asks for the same manifest and
     * reads it back, so what the dialog lists is what the PDF will contain rather than a second
     * guess at it.</p>
     */
    async function runPreviewStep() {
        const designState = hostContext.shell.designerState();
        const visuals = (designState?.pages || []).flatMap((page) => page.visuals || []);
        if (!visuals.length) {
            await hostContext.guidedBlocker({
                kicker: 'Step 7 · Preview pagination',
                title: 'There is nothing to paginate yet',
                lede: 'A preview runs the report and lays its visuals onto physical pages. This report has no visuals, so every page would be blank.',
                remedyLabel: 'Add detail bands',
                remedy: () => hostContext.runDetailsStep(),
            });
            return;
        }
        const parameters = await promptForReportParameters('Step 7 · Preview pagination');
        if (parameters === null) {
            hostContext.feedback.notify('Preview cancelled, so the report was not run.', { title: 'Preview', tone: 'info' });
            return;
        }
        hostContext.shell.setProjection('split');
        hostContext.feedback.notify('Running the report. Rows and messages appear below the script.', { title: 'Preview running', tone: 'info' });
        // The answers travel with the run, so what is previewed is the report as answered rather
        // than the defaults the script happens to declare.
        hostContext.shell.runReport(Object.keys(parameters).length ? parameters : null);
        await showPaginationBreakdown(parameters);
    }
    /** One page of the compiled breakdown, described in the author's terms. */
    function physicalPageSummary(page) {
        const placements = page.visuals || [];
        if (!placements.length)
            return 'nothing — this page prints empty';
        return placements.map((placement) => {
            const name = placement.visual?.name || 'visual';
            const rows = placement.startRowIndex != null && placement.endRowIndex != null
                ? ` rows ${placement.startRowIndex + 1}–${placement.endRowIndex + 1}`
                : '';
            return `${name}${rows}`;
        }).join(', ');
    }
    async function showPaginationBreakdown(parameters) {
        let manifest;
        try {
            manifest = await hostContext.request(hostContext.routes.preview, {
                body: {
                    script: hostContext.shell.getScriptText(),
                    parameters,
                    // Every page is run: the point of this step is what the finished document holds.
                    runEveryPage: true,
                },
                fallbackError: 'The pagination could not be compiled.',
            });
        }
        catch (error) {
            hostContext.feedback.notify(error?.message || 'The pagination could not be compiled.', { title: 'Preview failed', tone: 'error' });
            return;
        }
        const pages = (manifest?.pages || []).filter((page) => String(page.mode || '').toUpperCase() === 'PAGINATED' || page.printLayout);
        await hostContext.studioDialog({ kicker: 'Step 7 · Preview pagination', title: 'Pages this report prints' }, api => api.render({
            lede: 'This is the breakdown the export uses. A detail table that does not fit is split, and the '
                + 'row numbers say where each page continues from.',
            body: pages.length
                ? pages.map((page) => {
                    const physical = page.physicalPages || [];
                    return `<div class="etlsql-studio-guided-group">
                        <span>${escapeHtml(page.name || 'Page')} · ${physical.length || 1} `
                        + `physical page${(physical.length || 1) === 1 ? '' : 's'}`
                        + `${page.printLayout?.pageSize ? ` · ${escapeHtml(page.printLayout.pageSize)}` : ''}`
                        + `${page.printLayout?.orientation ? ` ${escapeHtml(String(page.printLayout.orientation).toLowerCase())}` : ''}</span>
                        <ol class="etlsql-studio-guided-list">${physical.length
                            ? physical.map((physicalPage) => `<li><strong>Page ${physicalPage.pageNumber}</strong> — ${escapeHtml(physicalPageSummary(physicalPage))}</li>`).join('')
                            : '<li>Not compiled — this page declares no print layout.</li>'}</ol>
                    </div>`;
                }).join('')
                : guidedNoteMarkup('No page in this report is paginated, so there is nothing to lay onto sheets. '
                    + 'A dashboard page is one scrolling canvas.', 'info'),
            actions: [
                { id: 'close', label: 'Close', run: () => api.close(null) },
                { id: 'export', label: 'Export PDF', primary: true, run: () => { api.close('export'); runExportStep(); } },
            ],
        }));
    }
    /**
     * Asks for the report's INPUT parameters before it runs, and returns the answers.
     *
     * <p>A prompt is what `INPUT` means: the reader is asked, and their answer replaces the
     * declaration's initial value — the same precedence `--var` has at the command line. Running a
     * report that asks for a date range and silently using the defaults previews a report nobody
     * requested, and exports one nobody can explain.</p>
     *
     * @returns `{}` when the report prompts for nothing, a map of answers, or null if the author
     *          cancelled — which must abort the run rather than fall back to defaults.
     */
    async function promptForReportParameters(intent) {
        let parameters = [];
        try {
            const parsed = await hostContext.request(hostContext.routes.parse, { body: { script: hostContext.shell.getScriptText() } });
            parameters = (parsed.designState?.parameters || []).filter((parameter) => parameter.isInput);
        }
        catch {
            // A script that cannot be parsed has no prompts to ask about; the run itself will
            // report the syntax error, which is the more useful message.
            return {};
        }
        if (!parameters.length)
            return {};
        const answers = Object.fromEntries(parameters.map((parameter) => [parameter.name, unquoteParameterValue(parameter.initialValue)]));
        const confirmed = await hostContext.studioDialog({ kicker: intent, title: 'Answer the report’s prompts' }, api => {
            const paint = () => {
                const missing = parameters.filter((parameter) => parameter.isRequired && !String(answers[parameter.name] || '').trim());
                api.render({
                    lede: 'This report declares <code>INPUT</code> parameters. Your answers are used for this '
                        + 'run only — the script keeps the defaults it declares.',
                    body: parameters.map((parameter) => `
                            <label class="etlsql-studio-guided-field">
                                <span>${escapeHtml(parameter.name)}${parameter.isRequired ? ' *' : ''}
                                    <small>${escapeHtml(parameter.dataType || '')}</small></span>
                                <input type="${parameter.isSensitive ? 'password' : 'text'}"
                                    data-parameter-answer="${escapeHtml(parameter.name)}"
                                    value="${escapeHtml(answers[parameter.name] || '')}" spellcheck="false">
                            </label>`).join('')
                        + (missing.length
                            ? guidedNoteMarkup(`${missing.map((parameter) => parameter.name).join(', ')} `
                                + `${missing.length === 1 ? 'is' : 'are'} required, so the report cannot run without `
                                + `${missing.length === 1 ? 'a value' : 'values'}.`, 'warning')
                            : ''),
                    actions: [
                        { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                        {
                            id: 'accept', label: 'Continue', primary: true, disabled: missing.length > 0,
                            run: () => api.close(true),
                        },
                    ],
                    wire: host => host.querySelectorAll('[data-parameter-answer]').forEach(input => input.addEventListener('input', () => {
                        const name = asHtml(input).dataset.parameterAnswer;
                        answers[name] = asInput(input).value;
                        const blocked = parameters.some((parameter) => parameter.isRequired && !String(answers[parameter.name] || '').trim());
                        // Repaint only when the form crosses between "can run" and "cannot".
                        // Repainting on every keystroke would rebuild the field under the
                        // cursor, which is how a form starts eating characters.
                        if (blocked === (missing.length > 0))
                            return;
                        paint();
                        const refreshed = asInput(hostContext.dialog.box.querySelector(`[data-parameter-answer="${name}"]`));
                        if (refreshed) {
                            refreshed.focus();
                            refreshed.setSelectionRange(refreshed.value.length, refreshed.value.length);
                        }
                    })),
                });
            };
            paint();
        });
        return confirmed ? answers : null;
    }
    /** A declaration's initial value is script text — `'North'` — and a prompt wants the value. */
    function unquoteParameterValue(value) {
        const text = String(value ?? '').trim();
        return /^'(?:[^']|'')*'$/.test(text) ? text.slice(1, -1).replace(/''/g, "'") : text;
    }
    /**
     * Exports the report in the buffer as a PDF and hands the file to the reader.
     *
     * The step used to be a page of instructions: it named the PDF export and told the author to go
     * and find it, which for a report that only exists in an unsaved buffer meant there was nowhere
     * to go. The host renders the same manifest the preview builds, so what lands in the file is the
     * pagination the author has been looking at.
     */
    async function exportReportPdf() {
        const doc = hostContext.getActiveDocument();
        const script = hostContext.shell.getScriptText();
        if (!script.trim()) {
            hostContext.feedback.notify('There is nothing to export — the script is empty.', { title: 'Export', tone: 'warning' });
            return false;
        }
        const parameters = await promptForReportParameters('Step 8 · Export');
        if (parameters === null) {
            hostContext.feedback.notify('Export cancelled, so nothing was written.', { title: 'Export', tone: 'info' });
            return false;
        }
        try {
            const blob = await hostContext.request(hostContext.routes.previewPdf, {
                body: { script, page: null, parameters },
                accept: 'application/pdf',
                fallbackError: 'The report could not be exported.',
            });
            const name = String(doc?.name || 'report').replace(/\.[^.]+$/, '') || 'report';
            downloadBlob(blob, `${name}.pdf`);
            hostContext.feedback.notify(`${name}.pdf was exported with the page setup you configured.`, { title: 'Export complete', tone: 'success' });
            return true;
        }
        catch (error) {
            hostContext.feedback.notify(error?.message || 'The report could not be exported.', { title: 'Export failed', tone: 'error' });
            return false;
        }
    }
    /** Saves bytes as a file. A host that blocks downloads says so rather than doing nothing. */
    function downloadBlob(blob, filename) {
        const url = URL.createObjectURL(blob);
        try {
            const link = document.createElement('a');
            link.href = url;
            link.download = filename;
            document.body.appendChild(link);
            link.click();
            link.remove();
        }
        finally {
            // Revoked on the next turn: revoking synchronously can cancel the download in progress.
            setTimeout(() => URL.revokeObjectURL(url), 0);
        }
    }
    async function runExportStep() {
        await hostContext.studioDialog({ kicker: 'Step 8 · Export', title: 'Export this report' }, api => api.render({
            lede: 'A paginated report exports two different things. <strong>PDF</strong> keeps the physical pages, margins, and breaks you configured. '
                + '<strong>CSV and Excel</strong> export the result rows only, with no page layout.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li><strong>PDF</strong> — exported here, from the report as it stands in the editor.</li>
                    <li><strong>CSV / Excel</strong> — run the report, then use the export buttons on the Results pane below the script.</li>
                </ul>`
                + guidedNoteMarkup('Exporting runs the report again against live data, so a parameter prompt applies at export time too.', 'info'),
            actions: [
                { id: 'close', label: 'Close', run: () => api.close(null) },
                { id: 'preview', label: 'Preview pagination', run: () => { api.close('preview'); runPreviewStep(); } },
                {
                    id: 'export', label: 'Export PDF', primary: true, run: async () => {
                        api.busy(true);
                        const exported = await exportReportPdf();
                        api.busy(false);
                        if (exported)
                            api.close('export');
                    },
                },
            ],
        }));
    }
    // --- Dashboard steps --------------------------------------------------------------------------
    async function runVisualsStep() {
        if (!await hostContext.requireDataSample('Step 2 · Visuals'))
            return;
        hostContext.shell.setActivity('palette');
        await hostContext.studioDialog({ kicker: 'Step 2 · Visuals', title: 'Add visuals to the canvas' }, api => api.render({
            lede: `The Visual Components panel is now open on the left, listing every visual type this report can use. `
                + `They all read from <strong>${escapeHtml(hostContext.activeContext().snapshot.source)}</strong>.`,
            body: `<ul class="etlsql-studio-guided-list">
                    <li>Click a type to drop it on the canvas, or drag it where you want it.</li>
                    <li>Select a tile, then use the Fields list to assign columns to its chart roles.</li>
                    <li>Every tile you add is written to the script as a <code>CREATE VISUAL</code> statement.</li>
                </ul>`,
            actions: [
                { id: 'close', label: 'Got it', run: () => api.close(null) },
                { id: 'build', label: 'Build a visual', primary: true, run: () => { api.close('build'); hostContext.openChartBuilder({ kicker: 'Step 2 · Visuals' }); } },
            ],
        }));
    }
    async function runCrossFilterStep() {
        if (!await hostContext.requireDataSample('Step 3 · Cross-filters'))
            return;
        hostContext.shell.setActivity('filters');
        const context = hostContext.activeContext();
        await hostContext.studioDialog({ kicker: 'Step 3 · Cross-filters', title: 'Filter across visuals' }, api => api.render({
            lede: 'A <strong>filter</strong> narrows the rows every visual sees. Promoting one to a viewer control turns it into a slicer '
                + 'the reader can change, backed by a report parameter.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li>Drag a field from the Fields list into the Filters lane on the left.</li>
                    <li>Choose <em>Dataset global</em> to filter every visual, or <em>Selected visual</em> for just one.</li>
                    <li>Use <em>Promote to viewer control</em> to give the reader a slicer for that field.</li>
                </ul>`,
            actions: [
                {
                    id: 'skip',
                    label: 'Skip / No filters needed',
                    run: () => {
                        context.skipCrossFilters = true;
                        api.close('skip');
                    }
                },
                { id: 'close', label: 'Configure filters', primary: true, run: () => api.close(null) },
            ],
        }));
    }
    return { runPreviewStep, runExportStep, runVisualsStep, runCrossFilterStep };
}
