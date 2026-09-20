/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-chart.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Chart field roles, aggregation, and chart builder.
 */
import { asHtml, asInput, asSelect, STUDIO_FORMAT_PATTERNS } from './studio-authoring-context.js';
import { escapeHtml, sqlPreviewMarkup } from './studio-authoring-ui.js';
import { columnName, columnType, snapshotColumns } from './studio-data.js';
import { aggregateRows, buildAggregatedSource, CHART_AGGREGATES, defaultAggregateAlias, missingRequiredRoles, renderVisualSample, rolesForVisualType, STUDIO_VISUAL_GROUPS, } from './visual-preview.js';
export function createStudioAuthoringChart(hostContext) {
    // --- The chart builder ------------------------------------------------------------------------
    //
    // Picking a visual type and assigning fields to its roles, against the real sample, with the
    // Report-SQL it will write shown before it is written. The same builder serves the dashboard's
    // "add a visual" step, the paginated report's bands, and the sidebar's Build entry, because they
    // are the same task: bind columns to a visual's roles and see the result.
    /** Field kind for a column, used to suggest a sensible default per role. */
    function guidedFieldKind(name) {
        const context = hostContext.activeContext();
        const column = snapshotColumns(context.snapshot).find((item) => columnName(item) === name);
        return column ? columnType(column, context.snapshot?.rows || []) : 'text';
    }
    /**
     * Opens the builder. `seed` may carry a starting type and mappings, so callers that already know
     * what they want (the paginated detail band, say) open it pre-filled rather than blank.
     * Resolves with the created visual's name, or null.
     */
    async function openChartBuilder(seed = {}) {
        if (!await hostContext.requireDataSample(seed.kicker || 'Build a chart'))
            return null;
        const context = hostContext.activeContext();
        const columns = snapshotColumns(context.snapshot).map(columnName);
        const draft = {
            type: (seed.type || 'BAR').toUpperCase(),
            title: seed.title || '',
            mappings: { ...(seed.mappings || {}) },
            // Per measure role: { aggregate, alias }. A chart usually plots a summary rather than a
            // stored column — "users per day" is a COUNT grouped by day — so the aggregate belongs to
            // the visual, letting two charts over one dataset summarise it differently.
            aggregates: {},
            // How the numbers and dates read. Two patterns, not a formatting panel: the value the
            // chart is about, and the axis it is plotted against. Everything past that — colours,
            // grid lines, data labels, axis bounds — belongs to the Format inspector on the selected
            // tile, which already does the job and is opened once this visual is added.
            format: { value: '', axis: '' },
        };
        if (!Object.keys(draft.mappings).length)
            autoAssignRoles(draft, columns);
        return await hostContext.studioDialog({ kicker: seed.kicker || 'Build a chart', title: 'Build a visual', wide: true }, api => {
            /** The measure role and its aggregate, when one is set. */
            const activeMeasure = () => {
                for (const role of rolesForVisualType(draft.type)) {
                    if (!role.measure)
                        continue;
                    const setting = draft.aggregates[role.key];
                    const column = draft.mappings[role.key];
                    if (!setting || setting.aggregate === 'NONE' || !column)
                        continue;
                    return {
                        role: role.key,
                        column,
                        aggregate: setting.aggregate,
                        alias: setting.alias || defaultAggregateAlias(setting.aggregate, column),
                    };
                }
                return null;
            };
            /** Everything bound that is not the aggregated measure becomes a grouping column. */
            const groupingColumns = (measure) => [...new Set(rolesForVisualType(draft.type)
                    .filter(role => role.key !== measure.role && !role.repeatable)
                    .map(role => draft.mappings[role.key])
                    .filter(Boolean))];
            /** Mappings as written: an aggregated role points at the alias, not the source column. */
            const resolvedMappings = () => {
                const measure = activeMeasure();
                if (!measure)
                    return draft.mappings;
                return { ...draft.mappings, [measure.role]: measure.alias };
            };
            const sourceExpression = () => {
                const binding = hostContext.visualSourceBinding();
                const base = binding.dataset || binding.options.inline_source || '&dataset';
                const measure = activeMeasure();
                return measure
                    ? buildAggregatedSource({ base, groupBy: groupingColumns(measure), measure })
                    : base;
            };
            /** The sample shaped the way the query will shape it, so the preview cannot mislead. */
            const previewSample = () => {
                const measure = activeMeasure();
                return measure
                    ? aggregateRows(context.snapshot, { groupBy: groupingColumns(measure), measure })
                    : context.snapshot;
            };
            const previewVisual = () => ({
                id: 'builder_preview',
                name: draft.title || `${draft.type.toLowerCase()}_visual`,
                type: draft.type,
                title: draft.title,
                mappings: resolvedMappings(),
                options: {},
            });
            /** The category axis a format can apply to; only a cartesian chart has one. */
            const axisRole = () => rolesForVisualType(draft.type).find(role => role.key === 'X') || null;
            /** Format patterns written the way the generator writes them, so preview and write agree. */
            const formatOptions = () => {
                const options = [];
                const value = draft.format.value.trim();
                const axis = draft.format.axis.trim();
                if (value)
                    options.push(`FORMAT = '${value.replace(/'/g, "''")}'`);
                if (axis && axisRole())
                    options.push(`X_AXIS (FORMAT = '${axis.replace(/'/g, "''")}')`);
                return options;
            };
            const sql = () => {
                const source = sourceExpression();
                const entries = Object.entries(resolvedMappings()).filter(([, value]) => value);
                return `CREATE VISUAL ${hostContext.datasetBaseName(draft.title || `${draft.type.toLowerCase()}_visual`)} AS ${draft.type} (\n`
                    + `    SOURCE = ${source}`
                    + (entries.length ? `,\n    MAPPINGS (${entries.map(([role, value]) => `${role} = ${value}`).join(', ')})` : '')
                    + (formatOptions().length ? `,\n    OPTIONS (${formatOptions().join(', ')})` : '')
                    + (draft.title ? `,\n    TITLE = '${String(draft.title).replace(/'/g, "''")}'` : '')
                    + '\n);';
            };
            const paint = () => api.render({
                lede: 'Drag a field onto a role, or click a role and pick one. The preview below runs against the '
                    + `sample from <strong>${escapeHtml(context.snapshot.source)}</strong>, so it is the real shape of your data.`,
                body: `
                    <div class="etlsql-studio-builder">
                        <div class="etlsql-studio-builder-types">
                            ${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-builder-group">
                                <span>${escapeHtml(group.name)}</span>
                                <div>${group.types.map(type => `<button type="button" data-builder-type="${type}"
                                    class="${draft.type === type ? 'active' : ''}">${type}</button>`).join('')}</div>
                            </div>`).join('')}
                        </div>
                        <div class="etlsql-studio-builder-main">
                            <div class="etlsql-studio-builder-bind">
                                <div class="etlsql-studio-builder-fields">
                                    <span>Fields</span>
                                    ${columns.map(column => `<button type="button" class="etlsql-studio-builder-field"
                                        draggable="true" data-builder-field="${escapeHtml(column)}"
                                        data-field-kind="${guidedFieldKind(column)}">${escapeHtml(column)}</button>`).join('')}
                                </div>
                                <div class="etlsql-studio-builder-roles">
                                    <span>Roles</span>
                                    ${rolesForVisualType(draft.type).map(role => roleSlotMarkup(role, draft)).join('')
                    || '<p class="etlsql-studio-guided-hint">This visual type takes no field bindings.</p>'}
                                </div>
                            </div>
                            <div class="etlsql-studio-builder-preview" data-builder-preview></div>
                        </div>
                    </div>
                    <label class="etlsql-studio-guided-field"><span>Title</span>
                        <input type="text" data-builder-title value="${escapeHtml(draft.title)}"
                            placeholder="${escapeHtml(`${draft.type} visual`)}"></label>
                    <div class="etlsql-studio-guided-row">
                        <label class="etlsql-studio-guided-field"><span>Value format</span>
                            <input type="text" data-builder-format list="etlsql-builder-formats"
                                value="${escapeHtml(draft.format.value)}" placeholder="1234.5 unformatted" spellcheck="false">
                        </label>
                        ${axisRole() ? `<label class="etlsql-studio-guided-field"><span>Axis label format</span>
                            <input type="text" data-builder-axis-format list="etlsql-builder-formats"
                                value="${escapeHtml(draft.format.axis)}" placeholder="Auto" spellcheck="false">
                        </label>` : ''}
                    </div>
                    <datalist id="etlsql-builder-formats">${STUDIO_FORMAT_PATTERNS.map(pattern => `<option value="${escapeHtml(pattern.pattern)}">${escapeHtml(pattern.label)}</option>`).join('')}</datalist>
                    <p class="etlsql-studio-guided-hint">Number and date patterns, as .NET writes them —
                        <code>N0</code>, <code>C2</code>, <code>P1</code>, <code>$#,##0.00</code>,
                        <code>MMM yyyy</code>. Everything else about how this looks — colours, grid lines,
                        data labels, axis bounds — is in <strong>Format</strong> on the selected tile, which
                        opens on the visual as soon as it is added.</p>`
                    + sqlPreviewMarkup(sql(), `Adds one ${draft.type} visual to the page, bound to the fields in the roles above. `
                        + 'It is appended after the statements already in the script; nothing existing is changed.'),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add to canvas', primary: true,
                        disabled: missingRequiredRoles(previewVisual()).length > 0,
                        run: addVisual,
                    },
                ],
                wire: host => {
                    renderVisualSample(asHtml(host.querySelector('[data-builder-preview]')), previewVisual(), previewSample());
                    host.querySelectorAll('[data-builder-type]').forEach(button => button.addEventListener('click', () => {
                        draft.type = asHtml(button).dataset.builderType || 'BAR';
                        // Roles differ per type, so carry over only the ones the new type accepts and
                        // fill the rest — an author switching BAR to PIE should not land on a blank.
                        // A repeatable role is a numbered family, so it is matched by prefix; keeping
                        // TABLE's COLUMN1..n on a BAR left every column spoken for and no role bound.
                        const roles = rolesForVisualType(draft.type);
                        const exact = new Set(roles.filter(role => !role.repeatable).map(role => role.key));
                        const prefixes = roles.filter(role => role.repeatable).map(role => role.key.replace(/S$/, ''));
                        draft.mappings = Object.fromEntries(Object.entries(draft.mappings).filter(([role]) => exact.has(role) || prefixes.some(prefix => new RegExp(`^${prefix}\\d*$`, 'i').test(role))));
                        autoAssignRoles(draft, columns);
                        paint();
                    }));
                    host.querySelectorAll('[data-builder-field]').forEach(field => {
                        field.addEventListener('dragstart', (event) => {
                            const dragEvent = event;
                            dragEvent.dataTransfer?.setData('text/plain', asHtml(field).dataset.builderField || '');
                            if (dragEvent.dataTransfer)
                                dragEvent.dataTransfer.effectAllowed = 'copy';
                        });
                    });
                    host.querySelectorAll('[data-role-slot]').forEach(slot => {
                        const role = asHtml(slot).dataset.roleSlot;
                        slot.addEventListener('dragover', (event) => {
                            const dragEvent = event;
                            dragEvent.preventDefault();
                            if (dragEvent.dataTransfer)
                                dragEvent.dataTransfer.dropEffect = 'copy';
                            slot.classList.add('is-over');
                        });
                        slot.addEventListener('dragleave', () => slot.classList.remove('is-over'));
                        slot.addEventListener('drop', (event) => {
                            const dragEvent = event;
                            dragEvent.preventDefault();
                            slot.classList.remove('is-over');
                            assignRole(role, dragEvent.dataTransfer?.getData('text/plain') || '');
                        });
                    });
                    host.querySelectorAll('[data-role-select]').forEach(select => select.addEventListener('change', () => assignRole(asHtml(select).dataset.roleSelect, asSelect(select).value)));
                    host.querySelectorAll('[data-role-aggregate]').forEach(select => select.addEventListener('change', () => {
                        const role = asHtml(select).dataset.roleAggregate;
                        const column = draft.mappings[role];
                        draft.aggregates[role] = asSelect(select).value === 'NONE'
                            ? { aggregate: 'NONE', alias: '' }
                            : { aggregate: asSelect(select).value, alias: defaultAggregateAlias(asSelect(select).value, column) };
                        paint();
                    }));
                    host.querySelectorAll('[data-role-alias]').forEach(input => input.addEventListener('change', () => {
                        const role = asHtml(input).dataset.roleAlias;
                        // Renaming the alias changes both the AS in the query and what the role maps to,
                        // which is the whole point of letting it be renamed.
                        draft.aggregates[role] = { ...draft.aggregates[role], alias: hostContext.datasetBaseName(asInput(input).value) };
                        paint();
                    }));
                    host.querySelectorAll('[data-role-clear]').forEach(button => button.addEventListener('click', () => assignRole(asHtml(button).dataset.roleClear, '')));
                    host.querySelectorAll('[data-role-add]').forEach(button => button.addEventListener('click', () => {
                        const next = nextRepeatableRole(draft, asHtml(button).dataset.roleAdd);
                        assignRole(next, columns.find(column => !Object.values(draft.mappings).includes(column)) || columns[0]);
                    }));
                    host.querySelector('[data-builder-title]')?.addEventListener('input', event => { draft.title = asInput(event.target).value; });
                    // Repainting on change, not on input: the preview under these fields is the SQL
                    // about to be written, and redrawing it on every keystroke makes a half-typed
                    // pattern look like the decision.
                    host.querySelector('[data-builder-format]')?.addEventListener('change', event => {
                        draft.format.value = asInput(event.target).value;
                        paint();
                    });
                    host.querySelector('[data-builder-axis-format]')?.addEventListener('change', event => {
                        draft.format.axis = asInput(event.target).value;
                        paint();
                    });
                },
            });
            const assignRole = (role, column) => {
                if (!role)
                    return;
                if (column)
                    draft.mappings[role] = column;
                else
                    delete draft.mappings[role];
                paint();
            };
            const addVisual = async () => {
                api.busy(true);
                const binding = hostContext.visualSourceBinding();
                const measure = activeMeasure();
                const valueFormat = draft.format.value.trim();
                const axisFormat = axisRole() ? draft.format.axis.trim() : '';
                // An aggregated visual reads from a grouped SELECT, so it carries an inline source
                // rather than a bare dataset reference.
                const source = measure
                    ? { dataset: null, options: { inline_source: sourceExpression() } }
                    : binding;
                const mappings = resolvedMappings();
                const type = draft.type;
                const added = await hostContext.mutate(`Add ${type} visual`, design => {
                    const page = design.pages[0];
                    page.visuals ||= [];
                    const name = hostContext.uniqueVisualName(design, hostContext.datasetBaseName(draft.title || `${type.toLowerCase()}_visual`));
                    const bottom = page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                    const wide = type === 'TABLE' || type === 'MATRIX';
                    page.visuals.push({
                        id: `studio_${Date.now().toString(36)}`,
                        name,
                        type,
                        gridCol: 1,
                        gridRow: bottom + 1,
                        gridColSpan: wide ? 12 : type === 'CARD' ? 3 : 6,
                        gridRowSpan: type === 'CARD' ? 2 : wide ? 6 : 4,
                        title: draft.title || null,
                        dataset: source.dataset,
                        mappings: { ...mappings },
                        options: {
                            ...source.options,
                            ...(valueFormat ? { FORMAT: valueFormat } : {}),
                        },
                        // The axis pattern rides on the visual's formatting, which is where the
                        // Format inspector reads and writes it — the builder starts that record
                        // rather than keeping a second one of its own.
                        ...(axisFormat ? { formatting: { xAxis: { FORMAT: axisFormat } } } : {}),
                    });
                    return name;
                });
                api.busy(false);
                if (added) {
                    hostContext.feedback.notify(`Added ${type} visual ${added}. Format on the selected tile carries on from here.`, { title: 'Visual added', tone: 'success' });
                    // The hand-off: the builder decides the shape of a visual once, and every later
                    // change belongs to the inspector that already edits every property. Selecting
                    // the new visual is what opens it, so the author lands on the controls that
                    // continue the job instead of reopening a wizard that would start a new one.
                    hostContext.shell.selectVisual?.(added);
                }
                api.close(added);
            };
            paint();
        });
    }
    /** Human-readable form of an aggregate, for the role hint. */
    function aggregateExpressionLabel(aggregate, column) {
        return aggregate === 'COUNT_DISTINCT' ? `a distinct count of ${column}` : `${aggregate.toLowerCase()} of ${column}`;
    }
    function roleSlotMarkup(role, draft) {
        const columns = snapshotColumns(hostContext.activeContext().snapshot).map(columnName);
        const options = (column) => `<option value="">—</option>${columns.map(item => `<option ${item === column ? 'selected' : ''}>${escapeHtml(item)}</option>`).join('')}`;
        if (!role.repeatable) {
            const value = draft.mappings[role.key] || '';
            const setting = draft.aggregates?.[role.key] || { aggregate: 'NONE', alias: '' };
            const aggregated = role.measure && value && setting.aggregate !== 'NONE';
            const alias = setting.alias || (aggregated ? defaultAggregateAlias(setting.aggregate, value) : '');
            // Only a measure role offers an aggregate. Naming the result is part of the same decision:
            // the alias is what the query says AS, and what the role ends up mapped to.
            const aggregateControls = role.measure && value
                ? `<label class="etlsql-studio-role-aggregate"><span>Summarise as</span>
                        <select data-role-aggregate="${role.key}">${CHART_AGGREGATES.map(option => `<option value="${option.id}" ${setting.aggregate === option.id ? 'selected' : ''}>${escapeHtml(option.label)}</option>`).join('')}</select></label>`
                    + (aggregated
                        ? `<label class="etlsql-studio-role-aggregate"><span>Call it</span>
                            <input type="text" data-role-alias="${role.key}" value="${escapeHtml(alias)}" spellcheck="false"></label>`
                        : '')
                : '';
            return `<div class="etlsql-studio-role-slot${value ? ' is-bound' : ''}${role.required && !value ? ' is-required' : ''}" data-role-slot="${role.key}">
                <span>${escapeHtml(role.label)}${role.required ? ' *' : ''}</span>
                <div><select data-role-select="${role.key}">${options(value)}</select>
                ${value ? `<button type="button" data-role-clear="${role.key}" aria-label="Clear ${escapeHtml(role.label)}">&times;</button>` : ''}</div>
                ${aggregateControls}
                <small>${escapeHtml(aggregated
                ? `Plots ${aggregateExpressionLabel(setting.aggregate, value)} per ${role.key === 'VALUE' ? 'group' : 'category'}`
                : (role.hint || ''))}</small>
            </div>`;
        }
        // A repeatable role is a numbered family (COLUMN1, COLUMN2, …); each bound entry gets its own
        // slot and there is always one more to drop onto.
        const bound = Object.entries(draft.mappings)
            .filter(([key, value]) => value && key.toUpperCase().startsWith(role.key.replace(/S$/, '')))
            .sort((left, right) => Number(left[0].replace(/\D/g, '') || 0) - Number(right[0].replace(/\D/g, '') || 0));
        return `<div class="etlsql-studio-role-repeat">
            <span>${escapeHtml(role.label)}${role.required ? ' *' : ''}</span>
            ${bound.map(([key, value]) => `<div class="etlsql-studio-role-slot is-bound" data-role-slot="${escapeHtml(key)}">
                <div><select data-role-select="${escapeHtml(key)}">${options(value)}</select>
                <button type="button" data-role-clear="${escapeHtml(key)}" aria-label="Remove column">&times;</button></div>
            </div>`).join('')}
            <div class="etlsql-studio-role-slot is-empty" data-role-slot="${escapeHtml(nextRepeatableRole(draft, role.key))}">
                <span>Drop a field here</span>
                <button type="button" data-role-add="${role.key}">+ Add column</button>
            </div>
            <small>${escapeHtml(role.hint || '')}</small>
        </div>`;
    }
    function nextRepeatableRole(draft, roleKey) {
        const prefix = roleKey.replace(/S$/, '');
        let index = 1;
        while (draft.mappings[`${prefix}${index}`])
            index++;
        return `${prefix}${index}`;
    }
    /** Fills unbound roles with the first column whose kind suits them, so the preview is never blank. */
    function autoAssignRoles(draft, columns) {
        const used = new Set(Object.values(draft.mappings).filter(Boolean));
        // Reusing a column across two roles is legitimate (count by the same field you group by), so
        // running out of unused columns must still bind something rather than leave the role empty.
        const pick = (kind) => columns.find(column => !used.has(column) && (kind === 'any' || guidedFieldKind(column) === kind))
            || columns.find(column => !used.has(column))
            || columns.find(column => kind === 'any' || guidedFieldKind(column) === kind)
            || columns[0];
        for (const role of rolesForVisualType(draft.type)) {
            if (role.repeatable) {
                if (!Object.keys(draft.mappings).some(key => key.toUpperCase().startsWith(role.key.replace(/S$/, '')))) {
                    columns.slice(0, 6).forEach((column, index) => { draft.mappings[`${role.key.replace(/S$/, '')}${index + 1}`] = column; });
                }
                continue;
            }
            if (draft.mappings[role.key] || !role.required)
                continue;
            const column = pick(role.kind);
            if (!column)
                continue;
            draft.mappings[role.key] = column;
            used.add(column);
        }
    }
    return { openChartBuilder };
}
