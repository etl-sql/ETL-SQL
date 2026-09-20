// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-inspector-format.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-inspector-format.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Visual formatting inspector controls and search.
 */
import { checkedTarget, controlTarget, datasetValue, queryElement, queryElements } from './designer-context.js';
import { parseNumericOpacity, parseNumericRadius, toHexColor, visualFormatting } from './visual-format-inspector.js';
export function createDesignerFormatting(context) {
    function bindInspectorSearch(panel) {
        const searchInput = queryElement(panel, '#pp-search-filter');
        const clearBtn = queryElement(panel, '#pp-search-clear');
        if (!searchInput)
            return;
        const groups = queryElements(panel, 'details.etlsql-format-group');
        const defaultOpenMap = new Map();
        groups.forEach(g => {
            defaultOpenMap.set(g, g.hasAttribute('open'));
        });
        const filter = () => {
            const q = (searchInput.value || '').trim().toLowerCase();
            if (clearBtn)
                clearBtn.style.display = q ? 'block' : 'none';
            if (!q) {
                groups.forEach(g => {
                    g.style.display = '';
                    if (defaultOpenMap.get(g))
                        g.setAttribute('open', '');
                    else
                        g.removeAttribute('open');
                    queryElements(g, '.etlsql-dsgn-label, .etlsql-dsgn-map-row, .etlsql-format-rule, .etlsql-format-field, .etlsql-format-axis-grid, .etlsql-dsgn-grid4, .etlsql-format-toggle, .etlsql-dsgn-chart-quick-controls, .etlsql-dsgn-color-grid').forEach(row => {
                        row.style.display = '';
                    });
                });
                return;
            }
            groups.forEach(g => {
                const summaryText = (queryElement(g, 'summary')?.textContent || '').toLowerCase();
                const rows = queryElements(g, '.etlsql-dsgn-label, .etlsql-dsgn-map-row, .etlsql-format-rule, .etlsql-format-field, .etlsql-format-axis-grid, .etlsql-dsgn-grid4, .etlsql-format-toggle, .etlsql-dsgn-chart-quick-controls, .etlsql-dsgn-color-grid');
                let matchCount = 0;
                if (summaryText.includes(q)) {
                    g.style.display = '';
                    g.setAttribute('open', '');
                    rows.forEach(row => { row.style.display = ''; });
                    return;
                }
                rows.forEach(row => {
                    const rowText = String(row.textContent || '').toLowerCase();
                    const inputMeta = Array.from(queryElements(row, 'input, select, textarea'))
                        .map(i => `${'placeholder' in i ? i.placeholder || '' : ''} ${i.id || ''} ${i.name || ''} ${i.dataset?.role || ''} ${i.dataset?.axisKey || ''}`)
                        .join(' ').toLowerCase();
                    const isMatch = rowText.includes(q) || inputMeta.includes(q);
                    row.style.display = isMatch ? '' : 'none';
                    if (isMatch)
                        matchCount++;
                });
                if (matchCount > 0) {
                    g.style.display = '';
                    g.setAttribute('open', '');
                }
                else {
                    g.style.display = 'none';
                }
            });
        };
        searchInput.addEventListener('input', filter);
        searchInput.addEventListener('keydown', ((e) => {
            if (e.key === 'Escape') {
                searchInput.value = '';
                filter();
                searchInput.blur();
            }
        }));
        clearBtn?.addEventListener('click', () => {
            searchInput.value = '';
            filter();
            searchInput.focus();
        });
    }
    function bindVisualFormatInspector(panel, v, columns, rerender) {
        const formatting = visualFormatting(v);
        const sync = () => { context.renderCanvas(); context.syncScriptFromGridDebounced(); };
        const bindValue = (selector, target, key) => queryElement(panel, selector)?.addEventListener('change', event => {
            const value = controlTarget(event).value.trim();
            if (value)
                target[key] = value;
            else
                delete target[key];
            sync();
        });
        queryElement(panel, '#pp-format-subtitle')?.addEventListener('change', event => {
            formatting.subtitle ||= {};
            const value = controlTarget(event).value.trim();
            if (value)
                formatting.subtitle.text = value;
            else
                delete formatting.subtitle.text;
            sync();
        });
        bindValue('#pp-title-font', formatting.title, 'font');
        bindValue('#pp-title-size', formatting.title, 'size');
        bindValue('#pp-title-weight', formatting.title, 'weight');
        bindValue('#pp-title-align', formatting.title, 'align');
        bindValue('#pp-title-color', formatting.title, 'color');
        queryElement(panel, '#pp-title-color-picker')?.addEventListener('input', event => {
            formatting.title.color = controlTarget(event).value;
            const text = queryElement(panel, '#pp-title-color');
            if (text)
                text.value = controlTarget(event).value;
            sync();
        });
        queryElement(panel, '#pp-number-format')?.addEventListener('change', event => {
            v.options ||= {};
            const value = controlTarget(event).value.trim();
            if (value)
                v.options.FORMAT = value;
            else
                delete v.options.FORMAT;
            sync();
            rerender();
        });
        queryElement(panel, '#pp-format-legend')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        queryElement(panel, '#pp-format-legend-position')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND_POSITION = controlTarget(event).value;
            const anchorWrap = queryElement(panel, '#pp-format-legend-anchor-wrap');
            if (anchorWrap)
                anchorWrap.style.display = controlTarget(event).value === 'INSIDE' ? '' : 'none';
            sync();
        });
        queryElement(panel, '#pp-format-legend-anchor')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND_ANCHOR = controlTarget(event).value;
            sync();
        });
        queryElement(panel, '#pp-format-legend-orientation')?.addEventListener('change', event => {
            v.options ||= {};
            if (controlTarget(event).value)
                v.options.LEGEND_ORIENTATION = controlTarget(event).value;
            else
                delete v.options.LEGEND_ORIENTATION;
            sync();
        });
        queryElement(panel, '#pp-format-legend-reverse')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.LEGEND_REVERSE = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        queryElement(panel, '#pp-format-legend-title')?.addEventListener('input', event => {
            v.options ||= {};
            if (controlTarget(event).value)
                v.options.LEGEND_TITLE = controlTarget(event).value;
            else
                delete v.options.LEGEND_TITLE;
            sync();
        });
        queryElement(panel, '#pp-format-legend-columns')?.addEventListener('change', event => {
            v.options ||= {};
            if (controlTarget(event).value)
                v.options.LEGEND_COLUMNS = controlTarget(event).value;
            else
                delete v.options.LEGEND_COLUMNS;
            sync();
        });
        queryElement(panel, '#pp-format-grid-lines')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.GRID_LINES = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        const bindOption = (selector, key, eventName = 'change', getValue = el => el.value) => queryElement(panel, selector)?.addEventListener(eventName, event => {
            v.options ||= {};
            v.options[key] = getValue(controlTarget(event));
            sync();
        });
        bindOption('#pp-format-grid-color', 'GRID_LINE_COLOR', 'input');
        bindOption('#pp-format-grid-dash', 'GRID_LINE_DASH');
        bindOption('#pp-format-grid-width', 'GRID_LINE_WIDTH');
        bindOption('#pp-format-pie-sort', 'SORT');
        bindOption('#pp-format-pie-min-slice-pct', 'MIN_SLICE_PCT');
        bindOption('#pp-format-pie-other-label', 'OTHER_LABEL');
        bindOption('#pp-format-pie-explode', 'EXPLODE');
        bindOption('#pp-format-pie-explode-all', 'EXPLODE_ALL');
        bindOption('#pp-format-pie-border-color', 'SLICE_BORDER_COLOR', 'input');
        bindOption('#pp-format-pie-border-width', 'SLICE_BORDER_WIDTH');
        bindOption('#pp-format-pie-start-angle', 'START_ANGLE');
        bindOption('#pp-format-scatter-jitter', 'JITTER', 'change', (el) => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-scatter-jitter-width', 'JITTER_WIDTH');
        bindOption('#pp-format-scatter-jitter-height', 'JITTER_HEIGHT');
        bindOption('#pp-format-bubble-min-size', 'MIN_BUBBLE_SIZE');
        bindOption('#pp-format-bubble-max-size', 'MAX_BUBBLE_SIZE');
        bindOption('#pp-format-heatmap-midpoint', 'MIDPOINT');
        bindOption('#pp-format-heatmap-null-color', 'NULL_COLOR', 'input');
        bindOption('#pp-format-heatmap-color-low', 'COLOR_LOW', 'input');
        bindOption('#pp-format-heatmap-color-mid', 'COLOR_MID', 'input');
        bindOption('#pp-format-heatmap-color-high', 'COLOR_HIGH', 'input');
        bindOption('#pp-format-heatmap-cell-border', 'CELL_BORDER', 'change', (el) => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-heatmap-border-color', 'CELL_BORDER_COLOR', 'input');
        bindOption('#pp-format-heatmap-x-sort', 'X_SORT');
        bindOption('#pp-format-heatmap-y-sort', 'Y_SORT');
        bindOption('#pp-format-waterfall-orientation', 'ORIENTATION');
        bindOption('#pp-format-waterfall-connector-lines', 'CONNECTOR_LINES', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-waterfall-color-total', 'COLOR_TOTAL', 'input');
        bindOption('#pp-format-waterfall-color-subtotal', 'COLOR_SUBTOTAL', 'input');
        bindOption('#pp-format-waterfall-color-up', 'COLOR_UP', 'input');
        bindOption('#pp-format-waterfall-color-down', 'COLOR_DOWN', 'input');
        bindOption('#pp-format-gantt-today-line', 'TODAY_LINE', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-gantt-today-color', 'TODAY_COLOR', 'input');
        bindOption('#pp-format-gantt-today-date', 'TODAY_DATE');
        bindOption('#pp-format-gantt-label-position', 'LABEL_POSITION');
        bindOption('#pp-format-candlestick-color-up', 'COLOR_UP', 'input');
        bindOption('#pp-format-candlestick-color-down', 'COLOR_DOWN', 'input');
        bindOption('#pp-format-candlestick-wick-color', 'WICK_COLOR', 'input');
        bindOption('#pp-format-candlestick-wick-color-up', 'WICK_COLOR_UP', 'input');
        bindOption('#pp-format-candlestick-wick-color-down', 'WICK_COLOR_DOWN', 'input');
        bindOption('#pp-format-candlestick-volume-color', 'VOLUME_COLOR', 'input');
        bindOption('#pp-format-radar-independent-axes', 'INDEPENDENT_AXES', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-radar-shape', 'SHAPE');
        bindOption('#pp-format-radar-fill-opacity', 'FILL_OPACITY');
        bindOption('#pp-format-funnel-shape', 'FUNNEL_SHAPE');
        bindOption('#pp-format-funnel-sort', 'SORT');
        bindOption('#pp-format-funnel-show-percent', 'SHOW_PERCENT', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-funnel-percent-mode', 'PERCENT_MODE');
        bindOption('#pp-format-sankey-node-align', 'NODE_ALIGN');
        bindOption('#pp-format-sankey-node-padding', 'NODE_PADDING');
        bindOption('#pp-format-sankey-link-opacity', 'LINK_OPACITY');
        bindOption('#pp-format-hierarchy-show-breadcrumb', 'SHOW_BREADCRUMB', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-treemap-label-min-size', 'LABEL_MIN_SIZE');
        bindOption('#pp-format-treemap-label-overflow', 'LABEL_OVERFLOW');
        bindOption('#pp-format-boxplot-style', 'BOX_STYLE');
        bindOption('#pp-format-boxplot-orientation', 'ORIENTATION');
        bindOption('#pp-format-boxplot-notched', 'NOTCHED', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-boxplot-show-mean', 'SHOW_MEAN', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-boxplot-show-violin', 'SHOW_VIOLIN', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-network-layout', 'LAYOUT');
        bindOption('#pp-format-network-repulsion', 'REPULSION');
        bindOption('#pp-format-network-label-min-size', 'NODE_LABEL_MIN_SIZE');
        bindOption('#pp-format-network-node-color', 'NODE_COLOR', 'input');
        bindOption('#pp-format-network-directed', 'DIRECTED', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-network-node-labels', 'NODE_LABELS', 'change', el => el.checked ? 'ON' : 'OFF');
        bindOption('#pp-format-zero-line-color', 'ZERO_LINE_COLOR', 'input');
        bindOption('#pp-format-zero-line-dash', 'ZERO_LINE_DASH');
        bindOption('#pp-format-zero-line-width', 'ZERO_LINE_WIDTH');
        queryElement(panel, '#pp-format-minor-grid-lines')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.MINOR_GRID_LINES = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        queryElement(panel, '#pp-format-zero-line')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.ZERO_LINE = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        queryElement(panel, '#pp-format-zoom-slider')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.ZOOM_SLIDER = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        queryElement(panel, '#pp-format-data-labels')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.DATA_LABELS = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        queryElement(panel, '#pp-format-data-label-position')?.addEventListener('change', event => {
            v.options ||= {};
            v.options['DATA_LABELS:POSITION'] = controlTarget(event).value.replaceAll(' ', '_');
            sync();
        });
        queryElement(panel, '#pp-format-symbols')?.addEventListener('change', event => {
            v.options ||= {};
            v.options.SYMBOLS = checkedTarget(event) ? 'ON' : 'OFF';
            sync();
        });
        bindOption('#pp-format-stacked', 'STACKED');
        queryElement(panel, '#pp-format-band-size')?.addEventListener('input', event => {
            v.options ||= {};
            v.options.BAND_SIZE = controlTarget(event).value;
            const output = queryElement(panel, '#pp-format-band-size-value');
            if (output)
                output.value = controlTarget(event).value;
            sync();
        });
        for (const [selector, key, outputSelector] of [
            ['#pp-format-series-gap', 'SERIES_GAP', '#pp-format-series-gap-value'],
            ['#pp-format-outer-padding', 'OUTER_PADDING', '#pp-format-outer-padding-value']
        ])
            queryElement(panel, selector)?.addEventListener('input', event => {
                v.options ||= {};
                v.options[key] = controlTarget(event).value;
                const output = queryElement(panel, outputSelector);
                if (output)
                    output.value = controlTarget(event).value;
                sync();
            });
        queryElement(panel, '#pp-format-overlays')?.addEventListener('change', event => {
            v.options ||= {};
            const value = controlTarget(event).value.trim();
            if (value)
                v.options.overlays = value;
            else
                delete v.options.overlays;
            sync();
        });
        queryElements(panel, '[data-axis]').forEach(input => input.addEventListener('change', () => {
            const axis = input.dataset.axis === 'x' ? formatting.xAxis : formatting.yAxis;
            const key = datasetValue(input, 'axisKey');
            const value = input.hasAttribute('data-axis-boolean')
                ? (input.checked ? 'ON' : 'OFF')
                : input.value.trim();
            if (value)
                axis[key] = value;
            else
                delete axis[key];
            sync();
        }));
        queryElements(panel, '[data-palette-color]').forEach(input => input.addEventListener('input', () => {
            const index = Number(datasetValue(input, 'paletteColor'));
            formatting.palette[index] = input.value;
            const text = queryElement(panel, `[data-palette-text="${index}"]`);
            if (text)
                text.value = input.value;
            sync();
        }));
        queryElements(panel, '[data-palette-text]').forEach(input => input.addEventListener('change', () => {
            formatting.palette[Number(datasetValue(input, 'paletteText'))] = input.value.trim();
            sync();
            rerender();
        }));
        queryElements(panel, '[data-palette-remove]').forEach(button => button.addEventListener('click', () => {
            formatting.palette.splice(Number(button.dataset.paletteRemove), 1);
            sync();
            rerender();
        }));
        queryElement(panel, '[data-palette-add]')?.addEventListener('click', () => {
            formatting.palette.push(['#2563eb', '#16a34a', '#f59e0b', '#dc2626'][formatting.palette.length % 4]);
            sync();
            rerender();
        });
        const syncNamedColors = () => {
            v.options ||= {};
            Object.keys(v.options).filter(key => key.toUpperCase().startsWith('COLOR:')).forEach(key => delete v.options[key]);
            queryElements(panel, '[data-named-color-row]').forEach(row => {
                const name = queryElement(row, '[data-named-color-name]')?.value.trim();
                const color = queryElement(row, '[data-named-color-value]')?.value;
                if (name && color)
                    v.options[`COLOR:${name}`] = color;
            });
            sync();
        };
        queryElements(panel, '[data-named-color-row]').forEach(row => {
            queryElement(row, '[data-named-color-name]')?.addEventListener('change', syncNamedColors);
            queryElement(row, '[data-named-color-value]')?.addEventListener('input', syncNamedColors);
            queryElement(row, '[data-named-color-remove]')?.addEventListener('click', () => {
                row.remove();
                syncNamedColors();
                rerender();
            });
        });
        queryElement(panel, '[data-named-color-add]')?.addEventListener('click', () => {
            v.options ||= {};
            let index = 1;
            while (Object.keys(v.options).some(key => key.toUpperCase() === `COLOR:SERIES${index}`))
                index++;
            v.options[`COLOR:Series${index}`] = '#2563eb';
            sync();
            rerender();
        });
        const ensureTableMappings = () => {
            if (Object.keys(v.mappings || {}).length)
                return;
            v.mappings ||= {};
            for (const column of columns || [])
                v.mappings[column] = column;
        };
        queryElements(panel, '[data-format-field]').forEach(row => {
            const key = row.dataset.formatField;
            if (!key)
                return;
            const field = formatting.fields[key] ||= {};
            queryElement(row, '[data-field-format]')?.addEventListener('change', event => {
                ensureTableMappings();
                const value = controlTarget(event).value.trim();
                if (value)
                    field.format = value;
                else
                    delete field.format;
                sync();
            });
            queryElement(row, '[data-field-data-bar]')?.addEventListener('change', event => {
                ensureTableMappings();
                field.dataBar = checkedTarget(event);
                sync();
            });
            queryElement(row, '[data-field-data-bar-color]')?.addEventListener('input', event => {
                ensureTableMappings();
                field.dataBar = true;
                field.dataBarColor = controlTarget(event).value;
                const checkbox = queryElement(row, '[data-field-data-bar]');
                if (checkbox)
                    checkbox.checked = true;
                sync();
            });
        });
        const updateRule = (row) => {
            const rule = formatting.conditionalRules[Number(row.dataset.ruleIndex)];
            if (!rule)
                return;
            rule.condition = `${queryElement(row, '[data-rule-field]').value} ${queryElement(row, '[data-rule-operator]').value} ${queryElement(row, '[data-rule-value]').value.trim()}`;
            rule.backgroundColor = queryElement(row, '[data-rule-background]').value;
            rule.fontColor = queryElement(row, '[data-rule-font]').value;
            sync();
        };
        queryElements(panel, '[data-rule-index]').forEach(row => {
            queryElements(row, 'select,input').forEach(input => input.addEventListener('change', () => updateRule(row)));
            queryElement(row, '[data-rule-remove]')?.addEventListener('click', () => {
                formatting.conditionalRules.splice(Number(row.dataset.ruleIndex), 1);
                sync();
                rerender();
            });
        });
        queryElement(panel, '[data-rule-add]')?.addEventListener('click', () => {
            const fallback = Object.values(v.mappings || {}).find(Boolean) || columns?.[0] || 'value';
            formatting.conditionalRules.push({ condition: `${fallback} < 0`, backgroundColor: '#fee2e2', fontColor: '#991b1b' });
            sync();
            rerender();
        });
    }
    function bindFormattingSection(propsPanel, v, renderCanvas, syncScriptFromGridDebounced) {
        const ensureOptions = () => { if (!v.options)
            v.options = {}; };
        const bgPicker = queryElement(propsPanel, '#pp-fmt-bg-picker');
        const bgText = queryElement(propsPanel, '#pp-fmt-bg-text');
        if (bgPicker && bgText) {
            bgPicker.addEventListener('input', e => {
                ensureOptions();
                bgText.value = controlTarget(e).value;
                v.options.BACKGROUND = controlTarget(e).value;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            bgText.addEventListener('input', e => {
                ensureOptions();
                const val = controlTarget(e).value.trim();
                if (val) {
                    v.options.BACKGROUND = val;
                    const hex = toHexColor(val, '');
                    if (hex)
                        bgPicker.value = hex;
                }
                else {
                    delete v.options.BACKGROUND;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
        const colorPicker = queryElement(propsPanel, '#pp-fmt-color-picker');
        const colorText = queryElement(propsPanel, '#pp-fmt-color-text');
        if (colorPicker && colorText) {
            colorPicker.addEventListener('input', e => {
                ensureOptions();
                colorText.value = controlTarget(e).value;
                v.options.COLOR = controlTarget(e).value;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            colorText.addEventListener('input', e => {
                ensureOptions();
                const val = controlTarget(e).value.trim();
                if (val) {
                    v.options.COLOR = val;
                    const hex = toHexColor(val, '');
                    if (hex)
                        colorPicker.value = hex;
                }
                else {
                    delete v.options.COLOR;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
        queryElements(propsPanel, '.etlsql-dsgn-swatch-row').forEach(row => {
            const inputSel = row.dataset.targetInput || "";
            const pickerSel = row.dataset.targetPicker || "";
            const inputEl = queryElement(propsPanel, inputSel);
            const pickerEl = queryElement(propsPanel, pickerSel);
            queryElements(row, '.etlsql-dsgn-swatch-chip').forEach(btn => {
                btn.addEventListener('click', () => {
                    const colorVal = btn.dataset.color;
                    if (!colorVal)
                        return;
                    ensureOptions();
                    if (inputEl)
                        inputEl.value = colorVal;
                    const hex = toHexColor(colorVal, '');
                    if (pickerEl && hex)
                        pickerEl.value = hex;
                    if (inputSel.includes('bg')) {
                        v.options.BACKGROUND = colorVal;
                    }
                    else if (inputSel.includes('color')) {
                        v.options.COLOR = colorVal;
                    }
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            });
        });
        const borderText = queryElement(propsPanel, '#pp-fmt-border-text');
        if (borderText) {
            borderText.addEventListener('input', e => {
                ensureOptions();
                const val = controlTarget(e).value.trim();
                if (val)
                    v.options.BORDER = val;
                else
                    delete v.options.BORDER;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            queryElements(propsPanel, '.etlsql-dsgn-preset-chips[data-target-input="#pp-fmt-border-text"] .etlsql-dsgn-preset-chip').forEach(btn => {
                btn.addEventListener('click', () => {
                    const val = btn.dataset.val || '';
                    ensureOptions();
                    borderText.value = val;
                    if (val && val !== 'none')
                        v.options.BORDER = val;
                    else if (val === 'none')
                        v.options.BORDER = 'none';
                    else
                        delete v.options.BORDER;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            });
        }
        const radiusSlider = queryElement(propsPanel, '#pp-fmt-radius-slider');
        const radiusText = queryElement(propsPanel, '#pp-fmt-radius-text');
        if (radiusSlider && radiusText) {
            radiusSlider.addEventListener('input', e => {
                ensureOptions();
                const val = `${controlTarget(e).value}px`;
                radiusText.value = val;
                v.options.BORDER_RADIUS = val;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            radiusText.addEventListener('input', e => {
                ensureOptions();
                const val = controlTarget(e).value.trim();
                if (val) {
                    v.options.BORDER_RADIUS = val;
                    radiusSlider.value = String(parseNumericRadius(val, 8));
                }
                else {
                    delete v.options.BORDER_RADIUS;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            queryElements(propsPanel, '.etlsql-dsgn-preset-chips[data-target-input="#pp-fmt-radius-text"] .etlsql-dsgn-preset-chip').forEach(btn => {
                btn.addEventListener('click', () => {
                    const val = btn.dataset.val || '';
                    ensureOptions();
                    radiusText.value = val;
                    radiusSlider.value = String(parseNumericRadius(val, 8));
                    v.options.BORDER_RADIUS = val;
                    renderCanvas();
                    syncScriptFromGridDebounced();
                });
            });
        }
        const fontSelect = queryElement(propsPanel, '#pp-fmt-font-select');
        if (fontSelect) {
            fontSelect.addEventListener('change', e => {
                ensureOptions();
                if (controlTarget(e).value)
                    v.options.FONT = controlTarget(e).value;
                else
                    delete v.options.FONT;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
        const sizeSelect = queryElement(propsPanel, '#pp-fmt-size-select');
        if (sizeSelect) {
            sizeSelect.addEventListener('change', e => {
                ensureOptions();
                if (controlTarget(e).value)
                    v.options.FONT_SIZE = controlTarget(e).value;
                else
                    delete v.options.FONT_SIZE;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
        const weightSelect = queryElement(propsPanel, '#pp-fmt-weight-select');
        if (weightSelect) {
            weightSelect.addEventListener('change', e => {
                ensureOptions();
                if (controlTarget(e).value)
                    v.options.FONT_WEIGHT = controlTarget(e).value;
                else
                    delete v.options.FONT_WEIGHT;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
        const shadowSelect = queryElement(propsPanel, '#pp-fmt-shadow-select');
        if (shadowSelect) {
            shadowSelect.addEventListener('change', e => {
                ensureOptions();
                if (controlTarget(e).value)
                    v.options.SHADOW = controlTarget(e).value;
                else
                    delete v.options.SHADOW;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
        const opacitySlider = queryElement(propsPanel, '#pp-fmt-opacity-slider');
        const opacityText = queryElement(propsPanel, '#pp-fmt-opacity-text');
        if (opacitySlider && opacityText) {
            opacitySlider.addEventListener('input', e => {
                ensureOptions();
                const pct = parseInt(controlTarget(e).value, 10);
                const val = pct === 100 ? '1' : (pct / 100).toFixed(2).replace(/\.?0+$/, '');
                opacityText.value = val;
                v.options.OPACITY = val;
                renderCanvas();
                syncScriptFromGridDebounced();
            });
            opacityText.addEventListener('input', e => {
                ensureOptions();
                const val = controlTarget(e).value.trim();
                if (val) {
                    v.options.OPACITY = val;
                    opacitySlider.value = String(parseNumericOpacity(val, 100));
                }
                else {
                    delete v.options.OPACITY;
                }
                renderCanvas();
                syncScriptFromGridDebounced();
            });
        }
    }
    return { bindInspectorSearch, bindVisualFormatInspector, bindFormattingSection };
}
