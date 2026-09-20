/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Filter cards, slicer promotion, and filter-lane interactions.
 */

import { _escapeHtml, _feedback, _studioIcon, controlValue, queryElement, queryElements } from './studio-context.js';
import type { ActiveDocumentContextLike, DesignerStateLike, FilterContractSpec, FilterSourceLike, ResolvedFilterTarget } from './studio-sql-mutations.js';

import type { StudioDomElement, StudioDynamic, StudioRuntimeContext, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
import type { SnapshotLike } from './studio-data.js';
import { columnName as _columnName, columnType as _columnType, snapshotColumns as _snapshotColumns } from './studio-data.js';

export interface StudioFilterPanelContext {
    readonly activeDocumentContext: () => StudioRuntimeContext;
    readonly canonicalDesignerMutation: <T = unknown>(label: string, mutate: (designState: DesignerStateLike) => Promise<T> | T) => Promise<T | null>;
    readonly composeFilteredSource: (source: string, filters: FilterContractSpec[], asVisualSource?: boolean) => Promise<string>;
    readonly designerApiJson: <T = StudioDynamic>(path: string, body: unknown) => Promise<T>;
    readonly filterContract: (field: string, filter: FilterSourceLike) => FilterContractSpec;
    readonly filterSidebarContent: StudioDomElement;
    readonly matchingFilters: (context: ActiveDocumentContextLike, scope: string, target: string) => FilterContractSpec[];
    readonly modalBackdrop: StudioDomElement;
    readonly modalBox: StudioDomElement;
    readonly persistFilter: (field: string, removedFilter?: FilterSourceLike | null) => Promise<string | null>;
    readonly resolveFilterTarget: (designState: DesignerStateLike, filter: FilterSourceLike) => ResolvedFilterTarget;
    readonly setActivity: (activity: string) => void;
    readonly setFilterSidebar: (open: boolean) => void;
    readonly setupModalAccessibility: (box: HTMLElement, backdrop: HTMLElement, onClose: () => void, titleId?: string) => () => void;
    readonly state: StudioRuntimeState;
    readonly uniqueVisualName: (designState: DesignerStateLike, baseName: string) => string;
    readonly updateSnapshotPackage: (snapshot: SnapshotLike | null) => void;
}

export function createStudioFilterPanel(hostContext: StudioFilterPanelContext) {
    const FILTER_OPERATORS = Object.freeze([
        { value: 'between', label: 'Is between', fields: 'range' },
        { value: 'minimum', label: 'Is at least', fields: 'single' },
        { value: 'maximum', label: 'Is at most', fields: 'single' },
        { value: 'greater', label: 'Is greater than', fields: 'single' },
        { value: 'less', label: 'Is less than', fields: 'single' },
        { value: 'equals', label: 'Equals', fields: 'single' },
        { value: 'notequals', label: 'Does not equal', fields: 'single' },
        { value: 'isnull', label: 'Is blank', fields: 'none' },
        { value: 'notnull', label: 'Is not blank', fields: 'none' },
    ]);

    const FILTER_VALUE_PAGE = 25;

    async function promoteFilterToSlicer(columnName: string) {
        const col = String(columnName || 'region').trim();
        const identifier = col.replace(/[^A-Za-z0-9_]/g, '_').replace(/^[^A-Za-z_]/, '_').toLowerCase();
        const parameterName = `@selected_${identifier}`;
        const context = hostContext.activeDocumentContext();
        const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === col) || { name: col };
        const columnType = _columnType(column, context.snapshot?.rows || []);
        const existingFilter = context.activeFilters[col] || {
            id: identifier,
            kind: columnType === 'number' ? 'number' : columnType === 'date' ? 'date' : 'categorical',
            scope: 'visual',
            target: hostContext.state.selectedVisualId
        };
        const slicerName = await hostContext.canonicalDesignerMutation('Promote filter to slicer', async designState => {
            const resolved = hostContext.resolveFilterTarget(designState, existingFilter);
            existingFilter.target = resolved.target;
            const rows = context.snapshot?.rows || [];
            const values = rows.map(row => row?.[col]).filter(value => value != null);
            const numericValues = values.map(Number).filter(Number.isFinite);
            const isNumeric = columnType === 'number';
            const isDate = columnType === 'date';
            const dataType = isNumeric ? 'DECIMAL' : isDate ? 'DATE' : 'VARCHAR';
            const initialValue = isNumeric
                ? String(existingFilter.maximum ?? Math.max(...numericValues, 0))
                : isDate
                    ? `'${existingFilter.minimum || String(values[0] || new Date().toISOString()).slice(0, 10)}'`
                    : `'${String(existingFilter.values?.[0] || 'All').replaceAll("'", "''")}'`;
            designState.parameters ||= [];
            if (!(designState.parameters as any[]).some((parameter: any) => (parameter.name || '').toLowerCase() === parameterName)) {
                (designState.parameters as any[]).push({
                    name: parameterName,
                    dataType,
                    initialValue,
                    isInput: true,
                    isOutput: false,
                    isRequired: false,
                    isSensitive: false
                });
            }

            const page = designState.pages?.[0];
            if (!page) throw new Error('No page found to place slicer.');
            page.visuals ||= [];
            const name = hostContext.uniqueVisualName(designState, `${identifier}_slicer`);
            const maxRow = page.visuals.reduce((max: number, visual: any) => Math.max(max, (Number(visual.gridRow) || 0) + (Number(visual.gridRowSpan) || 0) - 1), 0);
            const controlType = isNumeric ? 'SLIDER' : isDate ? 'DATEPICKER' : 'SLICER';
            const options: Record<string, string> = { TITLE: `Filter by ${col}` };
            if (isNumeric) {
                options.MIN = String(Math.min(...numericValues, 0));
                options.MAX = String(Math.max(...numericValues, 0));
                options.STEP = '1';
                options['action:ON_CHANGE'] = `SET_PARAMETER(${parameterName}, value)`;
            } else if (isDate) {
                options['action:ON_CHANGE'] = `SET_PARAMETER(${parameterName}, value)`;
            } else {
                const optionSource = await hostContext.designerApiJson(STUDIO_ROUTES.optionSource, { source: resolved.source, column: col });
                options.inline_source = optionSource.source;
                options.INCLUDE_ALL = 'ON';
                options.ALL_LABEL = 'All';
                options['action:ON_CHANGE'] = `SET_PARAMETER(${parameterName}, ${col})`;
            }
            page.visuals.push({
                id: `studio_${Date.now().toString(36)}`,
                name,
                type: controlType,
                gridCol: 1,
                gridRow: maxRow + 1,
                gridColSpan: 3,
                gridRowSpan: 3,
                title: null,
                dataset: undefined,
                mappings: isNumeric || isDate ? {} : { VALUE: col },
                options
            });

            const otherFilters = hostContext.matchingFilters(context, resolved.scope, resolved.target)
                .filter(filter => filter.column !== col);
            otherFilters.push(hostContext.filterContract(col, {
                id: identifier,
                kind: 'parameter',
                parameterName,
                parameterOperator: isNumeric ? 'maximum' : isDate ? 'minimum' : 'equals',
                allValue: isNumeric || isDate ? null : 'All'
            }));
            const dependentSource = await hostContext.composeFilteredSource(
                resolved.source, otherFilters, resolved.scope === 'visual');
            if (resolved.scope === 'dataset') resolved.item.query = dependentSource;
            else {
                resolved.item.options ||= {};
                resolved.item.options.inline_source = dependentSource;
            }
            return name;
        });
        if (!slicerName) return;
        delete context.activeFilters[col];
        context.filterFields = context.filterFields.filter((field: string) => field !== col);
        hostContext.updateSnapshotPackage(context.snapshot);
        hostContext.state.designerInstance?.refreshSnapshot?.();
        if (hostContext.state.filterSidebarOpen) renderFilterPanel();
        _feedback.notify(`Promoted ${col} to a parameter-bound control.`, { title: 'Slicer Promoted', tone: 'success' });
        return slicerName;
    }

    /** Per-card view state: what the reader searched for and how much of the list they opened. */
    function filterViewState(context: StudioRuntimeContext, field: string) {
        context.filterView ||= {};
        context.filterView[field] ||= { search: '', visible: FILTER_VALUE_PAGE };
        return context.filterView[field];
    }

    function filterOperatorMarkup(field: string, current: string) {
        return `<label class="etlsql-filter-control-label">Condition<select data-filter-operator="${_escapeHtml(field)}">${
            FILTER_OPERATORS.map(option => `<option value="${option.value}"${option.value === current ? ' selected' : ''}>${option.label}</option>`).join('')
        }</select></label>`;
    }

    function filterCardMarkup(field: string) {
        const context = hostContext.activeDocumentContext();
        const rows = context.snapshot?.rows || [];
        const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
        const type = _columnType(column, rows);
        const values = rows.map(row => row?.[field]).filter(value => value != null);
        const filter = context.activeFilters[field] || {};
        const scope = filter.scope || (hostContext.state.selectedVisualId ? 'visual' : 'dataset');
        const operator = String(filter.operator || 'between');
        const shape = FILTER_OPERATORS.find(option => option.value === operator)?.fields || 'range';
        let control = '<div class="etlsql-filter-awaiting-data">Values appear after a sample loads.</div>';

        if (type === 'number' && values.length) {
            const numbers = values.map(Number).filter(Number.isFinite), min = Math.min(...numbers), max = Math.max(...numbers);
            const selectedMin = filter.minimum ?? (shape === 'range' ? min : '');
            const selectedMax = filter.maximum ?? max;
            const bounds = shape === 'none'
                ? '<p class="etlsql-filter-operator-note">This condition needs no value.</p>'
                : shape === 'single'
                    ? `<div class="etlsql-filter-range-label"><label>Value <input type="number" value="${_escapeHtml(String(selectedMin))}" data-filter-min="${_escapeHtml(field)}"></label></div>`
                    : `<div class="etlsql-filter-range-label"><label>Min <input type="number" min="${min}" max="${max}" value="${_escapeHtml(String(selectedMin))}" data-filter-min="${_escapeHtml(field)}"></label><label>Max <input type="number" min="${min}" max="${max}" value="${_escapeHtml(String(selectedMax))}" data-filter-max="${_escapeHtml(field)}"></label></div>`;
            control = filterOperatorMarkup(field, operator) + bounds;
        } else if (type === 'date' && values.length) {
            const dates = values.map(value => String(value).slice(0, 10)).filter(value => /^\d{4}-\d{2}-\d{2}$/.test(value)).sort();
            const selectedMin = filter.minimum || (shape === 'range' ? (dates[0] || '') : '');
            const selectedMax = filter.maximum || dates.at(-1) || '';
            const bounds = shape === 'none'
                ? '<p class="etlsql-filter-operator-note">This condition needs no value.</p>'
                : shape === 'single'
                    ? `<div class="etlsql-filter-range-label etlsql-filter-date-range"><input type="date" aria-label="Date" value="${_escapeHtml(String(selectedMin))}" data-filter-date-min="${_escapeHtml(field)}"></div>`
                    : `<label class="etlsql-filter-control-label">Date range<select data-date-preset="${_escapeHtml(field)}"><option value="custom">Custom</option><option value="last7">Last 7 days</option><option value="last30">Last 30 days</option><option value="quarter">This quarter</option><option value="ytd">Year to date</option></select></label><div class="etlsql-filter-range-label etlsql-filter-date-range"><input type="date" aria-label="Start date" value="${_escapeHtml(String(selectedMin))}" data-filter-date-min="${_escapeHtml(field)}"><input type="date" aria-label="End date" value="${_escapeHtml(String(selectedMax))}" data-filter-date-max="${_escapeHtml(field)}"></div>`;
            control = filterOperatorMarkup(field, operator) + bounds;
        } else if (values.length) {
            const counts = new Map();
            values.forEach(value => counts.set(String(value), (counts.get(String(value)) || 0) + 1));
            const selected = filter.values || [];
            const view = filterViewState(context, field);
            const search = view.search.trim().toLowerCase();
            const all = [...counts.entries()];
            const matching = search ? all.filter(([value]) => value.toLowerCase().includes(search)) : all;
            const shown = matching.slice(0, view.visible);
            const hidden = matching.length - shown.length;
            // Selected values that the search hides are still selected, and the card says so rather
            // than letting a search look like it cleared them.
            const selectedHidden = selected.filter(value => !shown.some(([shownValue]) => shownValue === value)).length;
            control = `<div class="etlsql-filter-value-tools">
                    <input type="search" class="etlsql-filter-search" placeholder="Search ${all.length} value${all.length === 1 ? '' : 's'}" value="${_escapeHtml(view.search)}" data-filter-search="${_escapeHtml(field)}" aria-label="Search ${_escapeHtml(field)} values">
                    <div class="etlsql-filter-value-actions">
                        <button type="button" data-filter-select-all="${_escapeHtml(field)}">Select all</button>
                        <button type="button" data-filter-select-none="${_escapeHtml(field)}">Clear</button>
                        <button type="button" data-filter-invert="${_escapeHtml(field)}">Invert</button>
                    </div>
                </div>
                ${matching.length
                    ? `<div class="etlsql-filter-items-list">${shown.map(([value, count]) => `<label class="etlsql-filter-item-label"><input type="checkbox" data-filter-value="${_escapeHtml(field)}" value="${_escapeHtml(value)}" ${selected.includes(value) ? 'checked' : ''}><span>${_escapeHtml(value)}</span><span>${count}</span></label>`).join('')}</div>`
                    : '<div class="etlsql-filter-awaiting-data">No value matches that search.</div>'}
                <div class="etlsql-filter-value-footer">
                    <span>${shown.length} of ${matching.length}${search ? ` matching · ${all.length} total` : ''}${selected.length ? ` · ${selected.length} selected` : ''}${selectedHidden ? ` (${selectedHidden} not shown)` : ''}</span>
                    ${hidden > 0 ? `<button type="button" data-filter-show-more="${_escapeHtml(field)}">Show ${Math.min(hidden, FILTER_VALUE_PAGE)} more</button>` : ''}
                </div>`;
        }

        return `<div class="etlsql-filter-card"><div class="etlsql-filter-card-header"><span>${_escapeHtml(field)}</span><button type="button" data-remove-filter="${_escapeHtml(field)}" aria-label="Remove ${_escapeHtml(field)} filter">×</button></div><span class="etlsql-filter-type-badge">${type}</span><label class="etlsql-filter-control-label">Scope<select data-filter-scope="${_escapeHtml(field)}"><option value="dataset" ${scope === 'dataset' ? 'selected' : ''}>Dataset global</option><option value="visual" ${scope === 'visual' ? 'selected' : ''} ${hostContext.state.selectedVisualId ? '' : 'disabled'}>Selected visual</option></select></label>${control}<button type="button" class="etlsql-studio-btn etlsql-filter-promote-btn" data-promote-slicer="${_escapeHtml(field)}">Promote to viewer control</button></div>`;
    }

    function ensureFilter(field: string, kind: string) {
        const context = hostContext.activeDocumentContext();
        context.activeFilters[field] ||= {
            id: field.replace(/[^A-Za-z0-9_]/g, '_').toLowerCase(),
            kind,
            scope: hostContext.state.selectedVisualId ? 'visual' : 'dataset',
            target: hostContext.state.selectedVisualId || null
        };
        return context.activeFilters[field];
    }

    function relativeDateRange(preset: string) {
        const end = new Date();
        const start = new Date(end);
        if (preset === 'last7') start.setDate(end.getDate() - 6);
        else if (preset === 'last30') start.setDate(end.getDate() - 29);
        else if (preset === 'quarter') start.setMonth(Math.floor(end.getMonth() / 3) * 3, 1);
        else if (preset === 'ytd') start.setMonth(0, 1);
        const iso = (value: Date) => value.toISOString().slice(0, 10);
        return { minimum: iso(start), maximum: iso(end) };
    }

    function openFilterSetupDialog(initialField: string | null = null) {
        const context = hostContext.activeDocumentContext();
        const columns = _snapshotColumns(context.snapshot).length ? _snapshotColumns(context.snapshot) : context.sourceColumns;
        if (!columns.length) {
            _feedback.notify('Choose a connection and table in Data before creating a filter.', { title: 'Load fields first', tone: 'warning' });
            if (hostContext.state.activeActivity !== 'catalog') hostContext.setActivity('catalog');
            return;
        }

        const names = (columns as any[]).map(_columnName).filter((Boolean as any)) as string[];
        const firstField = names.includes(initialField || '') ? (initialField || '') : (names[0] || '');
        const titleId = 'etlsql-filter-dialog-title';
        let cleanupModal: (() => void) | null = null;
        const close = () => {
            cleanupModal?.();
            hostContext.modalBox.innerHTML = '';
            hostContext.modalBox.classList.remove('etlsql-studio-filter-dialog');
        };
        hostContext.modalBox.classList.add('etlsql-studio-filter-dialog');
        cleanupModal = hostContext.setupModalAccessibility(hostContext.modalBox, hostContext.modalBackdrop, close, titleId);

        const render = (field: string) => {
            const column = (columns as any[]).find((item: any) => _columnName(item) === field) || { name: field };
            const type = _columnType(column, context.snapshot?.rows || []);
            const values = (context.snapshot?.rows || []).map(row => row?.[field]).filter(value => value != null);
            const existing = context.activeFilters[field] || {};
            const defaultScope = existing.scope || (hostContext.state.selectedVisualId ? 'visual' : 'dataset');
            let controls = '<div class="etlsql-filter-awaiting-data">Values appear after a sample loads.</div>';
            if (type === 'number' && values.length) {
                const numbers = values.map(Number).filter(Number.isFinite);
                const minimum = Math.min(...numbers);
                const maximum = Math.max(...numbers);
                controls = `<div class="etlsql-filter-dialog-range"><label>Minimum<input type="number" data-filter-dialog-min value="${_escapeHtml(existing.minimum ?? minimum)}" min="${minimum}" max="${maximum}"></label><label>Maximum<input type="number" data-filter-dialog-max value="${_escapeHtml(existing.maximum ?? maximum)}" min="${minimum}" max="${maximum}"></label></div>`;
            } else if (type === 'date' && values.length) {
                const dates = values.map(value => String(value).slice(0, 10)).filter(value => /^\d{4}-\d{2}-\d{2}$/.test(value)).sort();
                controls = `<div class="etlsql-filter-dialog-range"><label>Start date<input type="date" data-filter-dialog-min value="${_escapeHtml(existing.minimum || dates[0] || '')}"></label><label>End date<input type="date" data-filter-dialog-max value="${_escapeHtml(existing.maximum || dates.at(-1) || '')}"></label></div>`;
            } else if (values.length) {
                const distinct = [...new Set(values.map(String))].slice(0, 12);
                const selected = existing.values?.length ? existing.values.map(String) : distinct;
                controls = `<fieldset class="etlsql-filter-dialog-values"><legend>Included values</legend>${distinct.map(value => `<label><input type="checkbox" data-filter-dialog-value value="${_escapeHtml(value)}" ${selected.includes(value) ? 'checked' : ''}><span>${_escapeHtml(value)}</span><small>${values.filter(item => String(item) === value).length}</small></label>`).join('')}</fieldset>`;
            }
            const selectedSource = context.selectedSource as { connection?: string; table?: string } | null;
            const sourceLabel = selectedSource?.table
                ? `${selectedSource.connection}.${selectedSource.table}`
                : context.snapshot?.source || 'current dataset';
            hostContext.modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header"><div><strong id="${titleId}">New filter</strong><span>${_escapeHtml(sourceLabel)}</span></div><button type="button" class="etlsql-studio-sidebar-close" data-filter-dialog-close aria-label="Close filter setup">${_studioIcon('close', 13)}</button></div>
                <div class="etlsql-studio-modal-body etlsql-filter-dialog-body">
                    <label class="etlsql-filter-dialog-field">Field<select data-filter-dialog-field>${names.map((name: string) => `<option value="${_escapeHtml(name)}" ${name === field ? 'selected' : ''}>${_escapeHtml(name)}</option>`).join('')}</select></label>
                    <div class="etlsql-filter-dialog-kind"><span>${type}</span><small>${values.length ? `${values.length} sampled values` : 'No sample values'}</small></div>
                    <label class="etlsql-filter-dialog-field">Apply to<select data-filter-dialog-scope><option value="dataset" ${defaultScope === 'dataset' ? 'selected' : ''}>Dataset</option><option value="visual" ${defaultScope === 'visual' ? 'selected' : ''} ${hostContext.state.selectedVisualId ? '' : 'disabled'}>Selected visual</option></select></label>
                    ${controls}
                </div>
                <div class="etlsql-studio-modal-footer"><button type="button" class="etlsql-studio-btn" data-filter-dialog-close>Cancel</button><button type="button" class="etlsql-studio-btn is-primary" data-filter-dialog-apply>Apply filter</button></div>`;
            queryElements(hostContext.modalBox, '[data-filter-dialog-close]').forEach(button => button.addEventListener('click', close));
            queryElement(hostContext.modalBox, '[data-filter-dialog-field]').addEventListener('change', event => render(controlValue(event)));
            queryElement(hostContext.modalBox, '[data-filter-dialog-apply]').addEventListener('click', () => {
                const selectedField = queryElement(hostContext.modalBox, '[data-filter-dialog-field]').value;
                const selectedColumn = (columns as any[]).find((item: any) => _columnName(item) === selectedField) || { name: selectedField };
                const selectedType = _columnType(selectedColumn, context.snapshot?.rows || []);
                const filter = ensureFilter(selectedField, selectedType === 'text' ? 'categorical' : selectedType);
                filter.kind = selectedType === 'text' ? 'categorical' : selectedType;
                filter.scope = queryElement(hostContext.modalBox, '[data-filter-dialog-scope]').value;
                filter.target = filter.scope === 'visual' ? hostContext.state.selectedVisualId : null;
                if (selectedType === 'text') filter.values = [...hostContext.modalBox.querySelectorAll<HTMLInputElement>('[data-filter-dialog-value]:checked')].map(input => input.value);
                else {
                    filter.minimum = queryElement(hostContext.modalBox, '[data-filter-dialog-min]')?.value || null;
                    filter.maximum = queryElement(hostContext.modalBox, '[data-filter-dialog-max]')?.value || null;
                }
                if (!context.filterFields.includes(selectedField)) context.filterFields.push(selectedField);
                hostContext.updateSnapshotPackage(context.snapshot);
                hostContext.state.designerInstance?.refreshSnapshot?.();
                close();
                hostContext.setFilterSidebar(true);
                void hostContext.persistFilter(selectedField);
            });
            queryElement(hostContext.modalBox, '[data-filter-dialog-field]').focus();
        };

        render(firstField || '');
    }

    function wireFilterLane(host = hostContext.filterSidebarContent) {
        const drop = queryElement(host, '[data-filter-drop]');
        drop?.addEventListener('dragover', (event: DragEvent) => { if (event.dataTransfer?.types.includes('application/x-etlsql-field')) { event.preventDefault(); drop.classList.add('drag-over'); } });
        drop?.addEventListener('dragleave', () => drop.classList.remove('drag-over'));
        drop?.addEventListener('drop', (event: DragEvent) => {
            event.preventDefault();
            const field = event.dataTransfer?.getData('application/x-etlsql-field') || event.dataTransfer?.getData('text/plain');
            drop.classList.remove('drag-over');
            if (field) openFilterSetupDialog(field);
        });

        queryElements(host, '[data-remove-filter]').forEach(button => button.addEventListener('click', async () => {
            const context = hostContext.activeDocumentContext();
            const removed = context.activeFilters[button.dataset.removeFilter];
            context.filterFields = context.filterFields.filter((field: string) => field !== button.dataset.removeFilter);
            delete context.activeFilters[button.dataset.removeFilter];
            hostContext.updateSnapshotPackage(context.snapshot); hostContext.state.designerInstance?.refreshSnapshot?.(); renderFilterPanel();
            if (removed) await hostContext.persistFilter(button.dataset.removeFilter, removed);
        }));
        queryElements(host, '[data-filter-scope]').forEach(select => select.addEventListener('change', async () => {
            const context = hostContext.activeDocumentContext();
            const field = select.dataset.filterScope;
            const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
            const columnType = _columnType(column, context.snapshot?.rows || []);
            const filter = ensureFilter(field, columnType === 'text' ? 'categorical' : columnType);
            const previous = { ...filter };
            filter.scope = select.value;
            filter.target = select.value === 'visual' ? hostContext.state.selectedVisualId : null;
            await hostContext.persistFilter(field, previous);
            await hostContext.persistFilter(field);
        }));
        queryElements(host, '[data-filter-min], [data-filter-max]').forEach(input => input.addEventListener('change', () => {
            const context = hostContext.activeDocumentContext();
            const field = input.dataset.filterMin || input.dataset.filterMax;
            const filter = ensureFilter(field, 'number');
            if (input.dataset.filterMin) filter.minimum = input.value;
            else filter.maximum = input.value;
            hostContext.updateSnapshotPackage(context.snapshot); hostContext.state.designerInstance?.refreshSnapshot?.();
            hostContext.persistFilter(field);
        }));
        queryElements(host, '[data-filter-value]').forEach(input => input.addEventListener('change', () => {
            const context = hostContext.activeDocumentContext();
            const field = input.dataset.filterValue;
            const values = [...host.querySelectorAll<HTMLInputElement>('[data-filter-value]:checked')].filter(item => item.dataset.filterValue === field).map(item => item.value);
            const filter = ensureFilter(field, 'categorical');
            filter.values = values;
            hostContext.updateSnapshotPackage(context.snapshot); hostContext.state.designerInstance?.refreshSnapshot?.();
            hostContext.persistFilter(field);
        }));
        queryElements(host, '[data-date-preset]').forEach(select => select.addEventListener('change', () => {
            if (select.value === 'custom') return;
            const field = select.dataset.datePreset;
            const filter = ensureFilter(field, 'date');
            // A preset is a range, so choosing one says which condition it is as well as its bounds.
            filter.operator = 'between';
            Object.assign(filter, relativeDateRange(select.value));
            hostContext.persistFilter(field).then(() => renderFilterPanel());
        }));
        queryElements(host, '[data-filter-date-min], [data-filter-date-max]').forEach(input => input.addEventListener('change', () => {
            const field = input.dataset.filterDateMin || input.dataset.filterDateMax;
            const filter = ensureFilter(field, 'date');
            if (input.dataset.filterDateMin) filter.minimum = input.value;
            else filter.maximum = input.value;
            hostContext.updateSnapshotPackage(hostContext.activeDocumentContext().snapshot); hostContext.state.designerInstance?.refreshSnapshot?.();
            hostContext.persistFilter(field);
        }));
        queryElements(host, '[data-filter-operator]').forEach(select => select.addEventListener('change', () => {
            const context = hostContext.activeDocumentContext();
            const field = select.dataset.filterOperator;
            const column = _snapshotColumns(context.snapshot).find(item => _columnName(item) === field) || { name: field };
            const filter = ensureFilter(field, _columnType(column, context.snapshot?.rows || []));
            filter.operator = select.value;
            // A condition that takes no value must not keep the bounds of the one before it, or the
            // predicate would say "is blank" while the card still shows a range.
            if (select.value === 'isnull' || select.value === 'notnull') {
                delete filter.minimum;
                delete filter.maximum;
            } else if (select.value !== 'between') {
                delete filter.maximum;
            }
            renderFilterPanel();
            hostContext.persistFilter(field);
        }));

        // ── Categorical value list ────────────────────────────────────────────
        // Search, the three selection actions, and paging are view state: they change what the card
        // shows, and only the checkboxes change what the report filters on.
        queryElements(host, '[data-filter-search]').forEach(input => {
            input.addEventListener('input', () => {
                const context = hostContext.activeDocumentContext();
                const view = filterViewState(context, input.dataset.filterSearch);
                view.search = input.value;
                view.visible = FILTER_VALUE_PAGE;
                renderFilterPanel();
                // Repainting moves focus off the box the reader is typing in, so it is put back with
                // the caret where it was.
                const refreshed = queryElement<HTMLInputElement>(hostContext.filterSidebarContent, `[data-filter-search="${CSS.escape(input.dataset.filterSearch)}"]`);
                if (refreshed) { refreshed.focus(); refreshed.setSelectionRange(refreshed.value.length, refreshed.value.length); }
            });
        });
        queryElements(host, '[data-filter-show-more]').forEach(button => button.addEventListener('click', () => {
            const context = hostContext.activeDocumentContext();
            filterViewState(context, button.dataset.filterShowMore).visible += FILTER_VALUE_PAGE;
            renderFilterPanel();
        }));

        /** The values a card is currently showing, which is what Select all and Invert act on. */
        const shownValues = (field: string) => [...host.querySelectorAll<HTMLInputElement>('[data-filter-value]')]
            .filter(item => item.dataset.filterValue === field)
            .map(item => item.value);

        const commitValues = (field: string, values: string[]) => {
            const context = hostContext.activeDocumentContext();
            const filter = ensureFilter(field, 'categorical');
            filter.values = values;
            hostContext.updateSnapshotPackage(context.snapshot);
            hostContext.state.designerInstance?.refreshSnapshot?.();
            renderFilterPanel();
            hostContext.persistFilter(field);
        };

        queryElements(host, '[data-filter-select-all]').forEach(button => button.addEventListener('click', () => {
            const field = button.dataset.filterSelectAll;
            const context = hostContext.activeDocumentContext();
            const existing = context.activeFilters[field]?.values || [];
            // Selecting all while a search is active adds what is on screen and keeps the rest of
            // the selection, because the search narrowed the view, not the filter.
            commitValues(field, [...new Set([...existing, ...shownValues(field)])]);
        }));
        queryElements(host, '[data-filter-select-none]').forEach(button => button.addEventListener('click', () =>
            commitValues(button.dataset.filterSelectNone, [])));
        queryElements(host, '[data-filter-invert]').forEach(button => button.addEventListener('click', () => {
            const field = button.dataset.filterInvert;
            const context = hostContext.activeDocumentContext();
            const existing = new Set(context.activeFilters[field]?.values || []);
            const shown = shownValues(field);
            const inverted = shown.filter(value => !existing.has(value));
            const untouched = [...existing].filter(value => !shown.includes(value));
            commitValues(field, [...new Set([...untouched, ...inverted])]);
        }));

        queryElements(host, '[data-promote-slicer]').forEach(button => button.addEventListener('click', () => promoteFilterToSlicer(button.dataset.promoteSlicer)));
    }

    function renderFilterPanel() {
        const context = hostContext.activeDocumentContext();
        const selectedSource = context.selectedSource as { connection?: string; table?: string } | null;
        hostContext.filterSidebarContent.innerHTML = `<div class="etlsql-studio-filter-workflow"><div class="etlsql-studio-filter-intro"><strong>Filter the report</strong><span>Drop a field from Data or choose one from the current table.</span><button type="button" class="etlsql-studio-btn is-primary etlsql-studio-new-filter" data-new-filter>${_studioIcon('plus', 13)} New filter</button></div><div class="etlsql-studio-subhead"><div><strong>Active filters</strong><span>${selectedSource?.table ? _escapeHtml(`${selectedSource.connection}.${selectedSource.table}`) : 'Current dataset'}</span></div><span class="etlsql-studio-count">${context.filterFields.length}</span></div><div class="etlsql-studio-filter-drop" data-filter-drop>${context.filterFields.length ? context.filterFields.map(filterCardMarkup).join('') : '<div class="etlsql-studio-empty-guidance"><strong>No filters yet</strong><span>Drag a field here or choose New filter. Studio will ask how to apply it.</span></div>'}</div></div>`;
        queryElement(hostContext.filterSidebarContent, '[data-new-filter]')?.addEventListener('click', () => openFilterSetupDialog());
        wireFilterLane(hostContext.filterSidebarContent);
    }

    return { promoteFilterToSlicer, openFilterSetupDialog, renderFilterPanel };
}
