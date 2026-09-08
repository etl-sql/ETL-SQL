/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * visual-format-inspector.js — split out of designer.js, TODO.md §2.
 * HTML builders and value parsers for the visual formatting inspector.
 */

import { esc } from './designer-util.js';

    export function toHexColor(val, fallback) {
        if (!val || typeof val !== 'string') return fallback;
        const s = val.trim();
        const match = s.match(/^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/);
        if (match) {
            if (match[1].length === 3) {
                return '#' + match[1].split('').map(c => c + c).join('');
            }
            return s;
        }
        return fallback;
    }

    export function parseNumericRadius(val, fallback) {
        if (!val) return fallback;
        const num = parseInt(String(val).replace(/[^0-9]/g, ''), 10);
        return isNaN(num) ? fallback : Math.min(32, Math.max(0, num));
    }

    export function parseNumericOpacity(val, fallback) {
        if (!val) return fallback;
        const floatVal = parseFloat(String(val));
        if (isNaN(floatVal)) return fallback;
        if (floatVal <= 1) return Math.round(floatVal * 100);
        return Math.min(100, Math.max(0, Math.round(floatVal)));
    }

    const FORMAT_INSPECTOR_CHARTS = new Set([
        'BAR', 'HBAR', 'LINE', 'AREA', 'PIE', 'DONUT', 'SCATTER', 'BUBBLE', 'GAUGE',
        'RADAR', 'HEATMAP', 'FUNNEL', 'WATERFALL', 'TREEMAP', 'BOXPLOT', 'COMBO',
        'CANDLESTICK', 'GANTT', 'MAP', 'SANKEY', 'SUNBURST', 'NETWORK', 'TRELLIS', 'MATRIX'
    ]);
    const FORMAT_INSPECTOR_CARTESIAN = new Set([
        'BAR', 'HBAR', 'HORIZONTALBAR', 'LINE', 'AREA', 'SCATTER', 'BUBBLE', 'HEATMAP',
        'WATERFALL', 'BOXPLOT', 'COMBO', 'CANDLESTICK', 'GANTT', 'TRELLIS'
    ]);

    export function visualFormatting(v) {
        v.formatting ||= {};
        v.formatting.title ||= { text: v.title || '' };
        v.formatting.xAxis ||= {};
        v.formatting.yAxis ||= {};
        v.formatting.palette ||= [];
        v.formatting.conditionalRules ||= [];
        v.formatting.fields ||= {};
        return v.formatting;
    }

    export function splitRuleCondition(condition, fallbackField) {
        const match = String(condition || '').trim().match(/^(.+?)\s*(<=|>=|<>|!=|=|<|>)\s*(.+)$/);
        return match
            ? { field: match[1].trim(), operator: match[2], value: match[3].trim() }
            : { field: fallbackField || 'value', operator: '<', value: '0' };
    }

    export function renderVisualFormatInspectorHtml(v, columns) {
        const formatting = v.formatting || {};
        const title = formatting.title || {};
        const subtitle = formatting.subtitle || {};
        const xAxis = formatting.xAxis || {};
        const yAxis = formatting.yAxis || {};
        const palette = formatting.palette || [];
        const rules = formatting.conditionalRules || [];
        const fields = formatting.fields || {};
        const namedColors = Object.entries(v.options || {})
            .filter(([key]) => key.toUpperCase().startsWith('COLOR:'))
            .map(([key, color]) => ({ name: key.slice('COLOR:'.length), color }));
        const overlays = v.options?.overlays || '';
        const isChart = FORMAT_INSPECTOR_CHARTS.has(v.type);
        const isCartesian = FORMAT_INSPECTOR_CARTESIAN.has(v.type);
        const isPieOrDonut = ['PIE', 'DONUT'].includes(v.type);
        const isScatter = v.type === 'SCATTER';
        const isBubble = v.type === 'BUBBLE';
        const isHeatmap = v.type === 'HEATMAP';
        const isWaterfall = v.type === 'WATERFALL';
        const isGantt = v.type === 'GANTT';
        const isCandlestick = v.type === 'CANDLESTICK';
        const isRadar = v.type === 'RADAR';
        const isFunnel = v.type === 'FUNNEL';
        const isSankey = v.type === 'SANKEY';
        const isTreemap = v.type === 'TREEMAP';
        const isSunburst = v.type === 'SUNBURST';
        const isBoxPlot = v.type === 'BOXPLOT';
        const isNetwork = v.type === 'NETWORK';
        const supportsZeroLine = ['BAR', 'HBAR', 'HORIZONTALBAR', 'LINE', 'AREA', 'COMBO'].includes(v.type);
        const supportsStacking = ['BAR', 'HBAR', 'HORIZONTALBAR', 'LINE', 'AREA'].includes(v.type);
        const isTable = v.type === 'TABLE';
        const supportsRules = isChart || isTable || v.type === 'CARD' || v.type === 'KPI';
        const availableFields = [...new Set([
            ...Object.values(v.mappings || {}).filter(Boolean),
            ...(columns || [])
        ])];
        const formatValue = v.options?.FORMAT || '';
        const fieldOptions = value => availableFields.map(field =>
            `<option value="${esc(field)}"${field === value ? ' selected' : ''}>${esc(field)}</option>`).join('');

        const tableFields = isTable ? availableFields : [];

        return `
                <details class="etlsql-format-group" open>
                    <summary>Title & number</summary>
                    <div class="etlsql-format-group-body">
                        <label class="etlsql-dsgn-label">Title
                            <input id="pp-title" class="form-control" value="${esc(v.title || '')}" placeholder="Visual Title">
                        </label>
                        <label class="etlsql-dsgn-label">Subtitle
                            <input id="pp-format-subtitle" class="form-control" value="${esc(subtitle.text || '')}" placeholder="Optional context line">
                        </label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Title font
                                <select id="pp-title-font" class="form-control">
                                    <option value="">Default</option>
                                    <option value="Segoe UI"${title.font === 'Segoe UI' ? ' selected' : ''}>Segoe UI</option>
                                    <option value="Inter"${title.font === 'Inter' ? ' selected' : ''}>Inter</option>
                                    <option value="Georgia"${title.font === 'Georgia' ? ' selected' : ''}>Georgia</option>
                                    <option value="Cascadia Code"${title.font === 'Cascadia Code' ? ' selected' : ''}>Cascadia Code</option>
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Size
                                <input id="pp-title-size" class="form-control" value="${esc(title.size || '')}" placeholder="14px">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Weight
                                <select id="pp-title-weight" class="form-control">
                                    <option value="">Default</option>
                                    ${['NORMAL', '500', 'SEMIBOLD', 'BOLD'].map(value => `<option${title.weight === value ? ' selected' : ''}>${value}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Align
                                <select id="pp-title-align" class="form-control">
                                    ${['LEFT', 'CENTER', 'RIGHT'].map(value => `<option${(title.align || 'LEFT') === value ? ' selected' : ''}>${value}</option>`).join('')}
                                </select>
                            </label>
                        </div>
                        <label class="etlsql-dsgn-label">Title color
                            <div class="etlsql-dsgn-color-picker-row">
                                <input type="color" id="pp-title-color-picker" value="${toHexColor(title.color, '#e6edf3')}">
                                <input id="pp-title-color" class="form-control" value="${esc(title.color || '')}" placeholder="#e6edf3">
                            </div>
                        </label>
                        ${!isTable ? `<label class="etlsql-dsgn-label">Number format
                            <input id="pp-number-format" class="form-control" list="pp-number-format-list" value="${esc(formatValue)}" placeholder="$#,##0.00">
                            <datalist id="pp-number-format-list"><option value="C2"></option><option value="N0"></option><option value="P1"></option><option value="$#,##0.00"></option></datalist>
                        </label>` : ''}
                        ${isScatter ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Jitter controls</div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-scatter-jitter" ${(v.options?.JITTER || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Enable jitter</span></label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Jitter width (0-1)
                                <input type="number" id="pp-format-scatter-jitter-width" class="form-control" min="0" max="1" step="0.05" value="${esc(v.options?.['JITTER:WIDTH'] || v.options?.JITTER_WIDTH || '')}" placeholder="0.15">
                            </label>
                            <label class="etlsql-dsgn-label">Jitter height (0-1)
                                <input type="number" id="pp-format-scatter-jitter-height" class="form-control" min="0" max="1" step="0.05" value="${esc(v.options?.['JITTER:HEIGHT'] || v.options?.JITTER_HEIGHT || '')}" placeholder="0.15">
                            </label>
                        </div>` : ''}
                        ${isBubble ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Bubble size controls</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Min bubble size (px)
                                <input type="number" id="pp-format-bubble-min-size" class="form-control" min="0" max="200" step="1" value="${esc(v.options?.MIN_BUBBLE_SIZE || '')}" placeholder="5">
                            </label>
                            <label class="etlsql-dsgn-label">Max bubble size (px)
                                <input type="number" id="pp-format-bubble-max-size" class="form-control" min="0" max="200" step="1" value="${esc(v.options?.MAX_BUBBLE_SIZE || '')}" placeholder="65">
                            </label>
                        </div>` : ''}
                        ${isHeatmap ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Heatmap controls</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Midpoint value
                                <input type="number" id="pp-format-heatmap-midpoint" class="form-control" value="${esc(v.options?.MIDPOINT || '')}" placeholder="0">
                            </label>
                            <label class="etlsql-dsgn-label">Null cell color
                                <input type="color" id="pp-format-heatmap-null-color" value="${toHexColor(v.options?.NULL_COLOR, '#f1f5f9')}">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Low color
                                <input type="color" id="pp-format-heatmap-color-low" value="${toHexColor(v.options?.COLOR_LOW || v.options?.['color:low'] || v.options?.['color:min'], '#dbeafe')}">
                            </label>
                            <label class="etlsql-dsgn-label">Mid color
                                <input type="color" id="pp-format-heatmap-color-mid" value="${toHexColor(v.options?.COLOR_MID || v.options?.['color:mid'], '#ffffff')}">
                            </label>
                            <label class="etlsql-dsgn-label">High color
                                <input type="color" id="pp-format-heatmap-color-high" value="${toHexColor(v.options?.COLOR_HIGH || v.options?.['color:high'] || v.options?.['color:max'], '#1d4ed8')}">
                            </label>
                        </div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-heatmap-cell-border" ${(v.options?.CELL_BORDER || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Show cell borders</span></label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Border color
                                <input type="color" id="pp-format-heatmap-border-color" value="${toHexColor(v.options?.CELL_BORDER_COLOR, '#ffffff')}">
                            </label>
                            <label class="etlsql-dsgn-label">X axis sort
                                <select id="pp-format-heatmap-x-sort" class="form-control">
                                    ${['', 'SOURCE', 'ALPHA', 'VALUE_DESC', 'VALUE_ASC'].map(value => `<option value="${value}"${(v.options?.X_SORT || '').toUpperCase() === value ? ' selected' : ''}>${value || '(Default)'}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Y axis sort
                                <select id="pp-format-heatmap-y-sort" class="form-control">
                                    ${['', 'SOURCE', 'ALPHA', 'VALUE_DESC', 'VALUE_ASC'].map(value => `<option value="${value}"${(v.options?.Y_SORT || '').toUpperCase() === value ? ' selected' : ''}>${value || '(Default)'}</option>`).join('')}
                                </select>
                            </label>
                        </div>` : ''}
                        ${isWaterfall ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Waterfall controls</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Orientation
                                <select id="pp-format-waterfall-orientation" class="form-control">
                                    <option value="VERTICAL"${(v.options?.ORIENTATION || 'VERTICAL').toUpperCase() === 'VERTICAL' ? ' selected' : ''}>Vertical</option>
                                    <option value="HORIZONTAL"${(v.options?.ORIENTATION || '').toUpperCase() === 'HORIZONTAL' ? ' selected' : ''}>Horizontal</option>
                                </select>
                            </label>
                            <label class="etlsql-format-toggle" style="margin-top:20px;"><input type="checkbox" id="pp-format-waterfall-connector-lines" ${(v.options?.CONNECTOR_LINES || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Connector lines</span></label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Total color
                                <input type="color" id="pp-format-waterfall-color-total" value="${toHexColor(v.options?.COLOR_TOTAL, '#2980b9')}">
                            </label>
                            <label class="etlsql-dsgn-label">Subtotal color
                                <input type="color" id="pp-format-waterfall-color-subtotal" value="${toHexColor(v.options?.COLOR_SUBTOTAL, '#475569')}">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Increase color
                                <input type="color" id="pp-format-waterfall-color-up" value="${toHexColor(v.options?.COLOR_UP || v.options?.COLOR_INCREASE, '#27ae60')}">
                            </label>
                            <label class="etlsql-dsgn-label">Decrease color
                                <input type="color" id="pp-format-waterfall-color-down" value="${toHexColor(v.options?.COLOR_DOWN || v.options?.COLOR_DECREASE, '#e74c3c')}">
                            </label>
                        </div>` : ''}
                        ${isGantt ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Gantt controls</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-format-toggle" style="margin-top:20px;"><input type="checkbox" id="pp-format-gantt-today-line" ${(v.options?.TODAY_LINE || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Today line</span></label>
                            <label class="etlsql-dsgn-label">Today line color
                                <input type="color" id="pp-format-gantt-today-color" value="${toHexColor(v.options?.TODAY_COLOR, '#ef4444')}">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Today date override
                                <input type="text" id="pp-format-gantt-today-date" class="form-control" value="${esc(v.options?.TODAY_DATE || '')}" placeholder="YYYY-MM-DD">
                            </label>
                            <label class="etlsql-dsgn-label">Label position
                                <select id="pp-format-gantt-label-position" class="form-control">
                                    ${['LEFT', 'INSIDE', 'RIGHT', 'NONE'].map(val => `<option${(v.options?.LABEL_POSITION || 'LEFT').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                        </div>` : ''}
                        ${isCandlestick ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Candlestick styling</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Bullish / Up candle color
                                <input type="color" id="pp-format-candlestick-color-up" value="${toHexColor(v.options?.COLOR_UP, '#26a69a')}">
                            </label>
                            <label class="etlsql-dsgn-label">Bearish / Down candle color
                                <input type="color" id="pp-format-candlestick-color-down" value="${toHexColor(v.options?.COLOR_DOWN, '#ef5350')}">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Wick color override
                                <input type="color" id="pp-format-candlestick-wick-color" value="${toHexColor(v.options?.WICK_COLOR, '#666666')}">
                            </label>
                            <label class="etlsql-dsgn-label">Volume bars color
                                <input type="color" id="pp-format-candlestick-volume-color" value="${toHexColor(v.options?.VOLUME_COLOR, '#94a3b8')}">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Up wick color
                                <input type="color" id="pp-format-candlestick-wick-color-up" value="${toHexColor(v.options?.WICK_COLOR_UP, '#26a69a')}">
                            </label>
                            <label class="etlsql-dsgn-label">Down wick color
                                <input type="color" id="pp-format-candlestick-wick-color-down" value="${toHexColor(v.options?.WICK_COLOR_DOWN, '#ef5350')}">
                            </label>
                        </div>` : ''}
                        ${isRadar ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Radar options</div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-radar-independent-axes" ${(v.options?.INDEPENDENT_AXES || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Independent axes</span></label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Shape style
                                <select id="pp-format-radar-shape" class="form-control">
                                    ${['POLYGON', 'CIRCLE'].map(val => `<option${(v.options?.SHAPE || 'POLYGON').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Fill opacity (0.0 – 1.0)
                                <input type="number" step="0.05" min="0" max="1" id="pp-format-radar-fill-opacity" class="form-control" value="${v.options?.FILL_OPACITY ?? '0.18'}">
                            </label>
                        </div>` : ''}
                        ${isFunnel ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Funnel options</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Funnel shape
                                <select id="pp-format-funnel-shape" class="form-control">
                                    ${['FUNNEL', 'PYRAMID'].map(val => `<option${(v.options?.FUNNEL_SHAPE || v.options?.SHAPE || 'FUNNEL').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Stage sort
                                <select id="pp-format-funnel-sort" class="form-control">
                                    ${['VALUE_DESC', 'VALUE_ASC', 'SOURCE'].map(val => `<option${(v.options?.SORT || 'VALUE_DESC').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                        </div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-funnel-show-percent" ${(v.options?.SHOW_PERCENT || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show conversion %</span></label>
                        <label class="etlsql-dsgn-label">Percent mode
                            <select id="pp-format-funnel-percent-mode" class="form-control">
                                ${['STEP', 'TOTAL'].map(val => `<option${(v.options?.PERCENT_MODE || 'STEP').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                            </select>
                        </label>` : ''}
                        ${isSankey ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Sankey options</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Node alignment
                                <select id="pp-format-sankey-node-align" class="form-control">
                                    ${['JUSTIFY', 'LEFT', 'RIGHT', 'CENTER'].map(val => `<option${(v.options?.NODE_ALIGN || 'JUSTIFY').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Node padding (px)
                                <input type="number" min="0" max="100" id="pp-format-sankey-node-padding" class="form-control" value="${v.options?.NODE_PADDING ?? '12'}">
                            </label>
                        </div>
                        <label class="etlsql-dsgn-label">Link opacity (0.0 – 1.0)
                            <input type="number" step="0.05" min="0" max="1" id="pp-format-sankey-link-opacity" class="form-control" value="${v.options?.LINK_OPACITY ?? '0.55'}">
                        </label>` : ''}
                        ${(isTreemap || isSunburst) ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">${isTreemap ? 'Treemap' : 'Sunburst'} options</div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-hierarchy-show-breadcrumb" ${(v.options?.SHOW_BREADCRUMB || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show breadcrumb path</span></label>
                        ${isTreemap ? `
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Label min size (px)
                                <input type="number" min="0" max="200" id="pp-format-treemap-label-min-size" class="form-control" value="${v.options?.LABEL_MIN_SIZE ?? '42'}">
                            </label>
                            <label class="etlsql-dsgn-label">Label overflow
                                <select id="pp-format-treemap-label-overflow" class="form-control">
                                    ${['CLIP', 'WRAP', 'HIDDEN'].map(val => `<option${(v.options?.LABEL_OVERFLOW || 'CLIP').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                        </div>` : ''}` : ''}
                        ${isBoxPlot ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Box plot options</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Box style
                                <select id="pp-format-boxplot-style" class="form-control">
                                    ${['BOX', 'VIOLIN', 'BOTH'].map(val => `<option${(v.options?.BOX_STYLE || 'BOX').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Orientation
                                <select id="pp-format-boxplot-orientation" class="form-control">
                                    ${['VERTICAL', 'HORIZONTAL'].map(val => `<option${(v.options?.ORIENTATION || 'VERTICAL').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                        </div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-boxplot-notched" ${(v.options?.NOTCHED || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Notched boxes (median CI)</span></label>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-boxplot-show-mean" ${(v.options?.SHOW_MEAN || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show mean marker</span></label>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-boxplot-show-violin" ${(v.options?.SHOW_VIOLIN || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show violin density</span></label>` : ''}
                        ${isNetwork ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Network options</div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Layout
                                <select id="pp-format-network-layout" class="form-control">
                                    ${['FORCE', 'CIRCULAR'].map(val => `<option${(v.options?.LAYOUT || 'FORCE').toUpperCase() === val ? ' selected' : ''}>${val}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Repulsion force
                                <input type="number" min="50" max="5000" step="50" id="pp-format-network-repulsion" class="form-control" value="${v.options?.REPULSION ?? '500'}">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Min label size (px)
                                <input type="number" min="0" max="50" id="pp-format-network-label-min-size" class="form-control" value="${v.options?.NODE_LABEL_MIN_SIZE ?? '0'}">
                            </label>
                            <label class="etlsql-dsgn-label">Node color
                                <input type="color" id="pp-format-network-node-color" class="form-control form-control-color" value="${v.options?.NODE_COLOR || '#2563eb'}">
                            </label>
                        </div>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-network-directed" ${(v.options?.DIRECTED || v.options?.ARROWS || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Directed edges (arrows)</span></label>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-network-node-labels" ${(v.options?.NODE_LABELS || v.options?.LABELS || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Show node labels</span></label>` : ''}
                    </div>
                </details>

                ${isChart ? `<details class="etlsql-format-group">
                    <summary>Axes & legend</summary>
                    <div class="etlsql-format-group-body">
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-legend" ${(v.options?.LEGEND || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Show legend</span></label>
                        <label class="etlsql-dsgn-label">Legend placement
                            <select id="pp-format-legend-position" class="form-control">
                                ${['TOP', 'RIGHT', 'BOTTOM', 'LEFT', 'INSIDE'].map(value => `<option${(v.options?.LEGEND_POSITION || 'BOTTOM').toUpperCase() === value ? ' selected' : ''}>${value}</option>`).join('')}
                            </select>
                        </label>
                        <label class="etlsql-dsgn-label" id="pp-format-legend-anchor-wrap" style="${(v.options?.LEGEND_POSITION || '').toUpperCase() === 'INSIDE' ? '' : 'display:none;'}">Legend anchor
                            <select id="pp-format-legend-anchor" class="form-control">
                                ${['TOP_RIGHT', 'TOP_LEFT', 'BOTTOM_RIGHT', 'BOTTOM_LEFT'].map(value => `<option${(v.options?.LEGEND_ANCHOR || 'TOP_RIGHT').toUpperCase() === value ? ' selected' : ''}>${value}</option>`).join('')}
                            </select>
                        </label>
                        <label class="etlsql-dsgn-label">Legend orientation
                            <select id="pp-format-legend-orientation" class="form-control">
                                ${['', 'HORIZONTAL', 'VERTICAL'].map(value => `<option value="${value}"${(v.options?.LEGEND_ORIENTATION || '').toUpperCase() === value ? ' selected' : ''}>${value || '(Default)'}</option>`).join('')}
                            </select>
                        </label>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-legend-reverse" ${(v.options?.LEGEND_REVERSE || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Reverse series order</span></label>
                        <label class="etlsql-dsgn-label">Legend title
                            <input type="text" id="pp-format-legend-title" class="form-control" value="${esc(v.options?.LEGEND_TITLE || '')}" placeholder="Title or NONE">
                        </label>
                        <label class="etlsql-dsgn-label">Legend columns
                            <input type="number" id="pp-format-legend-columns" class="form-control" min="1" max="20" value="${esc(v.options?.LEGEND_COLUMNS || '')}" placeholder="Auto">
                        </label>
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-grid-lines" ${(v.options?.GRID_LINES || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Show background grid lines</span></label>
                        ${isCartesian ? `<div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Grid color
                                <input type="color" id="pp-format-grid-color" value="${toHexColor(v.options?.GRID_LINE_COLOR, '#e5e7eb')}">
                            </label>
                            <label class="etlsql-dsgn-label">Grid line
                                <select id="pp-format-grid-dash" class="form-control">${['SOLID', 'DASHED', 'DOTTED'].map(value => `<option${(v.options?.GRID_LINE_DASH || 'SOLID').toUpperCase() === value ? ' selected' : ''}>${value}</option>`).join('')}</select>
                            </label>
                            <label class="etlsql-dsgn-label">Grid width
                                <input type="number" id="pp-format-grid-width" class="form-control" min="0.1" max="10" step="0.1" value="${esc(v.options?.GRID_LINE_WIDTH || '1')}">
                            </label>
                            <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-minor-grid-lines" ${(v.options?.MINOR_GRID_LINES || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Minor grid lines</span></label>
                        </div>` : ''}
                        ${supportsZeroLine ? `<label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-zero-line" ${(v.options?.ZERO_LINE || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show zero line</span></label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Zero-line color<input type="color" id="pp-format-zero-line-color" value="${toHexColor(v.options?.ZERO_LINE_COLOR, '#6b7280')}"></label>
                            <label class="etlsql-dsgn-label">Zero-line style<select id="pp-format-zero-line-dash" class="form-control">${['SOLID', 'DASHED', 'DOTTED'].map(value => `<option${(v.options?.ZERO_LINE_DASH || 'SOLID').toUpperCase() === value ? ' selected' : ''}>${value}</option>`).join('')}</select></label>
                            <label class="etlsql-dsgn-label">Zero-line width<input type="number" id="pp-format-zero-line-width" class="form-control" min="0.1" max="10" step="0.1" value="${esc(v.options?.ZERO_LINE_WIDTH || '1.5')}"></label>
                        </div>` : ''}
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-zoom-slider" ${(v.options?.ZOOM_SLIDER || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show zoom slider</span></label>
                        ${isPieOrDonut ? `
                        <div class="etlsql-dsgn-section-divider"></div>
                        <div class="etlsql-dsgn-section-title">Slice controls</div>
                        <label class="etlsql-dsgn-label">Slice sort order
                            <select id="pp-format-pie-sort" class="form-control">
                                ${['SOURCE', 'VALUE_DESC', 'VALUE_ASC', 'ALPHA'].map(value => `<option${(v.options?.SORT || 'SOURCE').toUpperCase() === value ? ' selected' : ''}>${value}</option>`).join('')}
                            </select>
                        </label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Min slice threshold (%)
                                <input type="number" id="pp-format-pie-min-slice-pct" class="form-control" min="0" max="100" step="any" value="${esc(v.options?.MIN_SLICE_PCT || '')}" placeholder="None">
                            </label>
                            <label class="etlsql-dsgn-label">Other label
                                <input type="text" id="pp-format-pie-other-label" class="form-control" value="${esc(v.options?.OTHER_LABEL || '')}" placeholder="Other">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Explode slice
                                <input type="text" id="pp-format-pie-explode" class="form-control" value="${esc(v.options?.EXPLODE || '')}" placeholder="Slice name">
                            </label>
                            <label class="etlsql-dsgn-label">Explode all (px)
                                <input type="number" id="pp-format-pie-explode-all" class="form-control" min="0" max="100" step="1" value="${esc(v.options?.EXPLODE_ALL || '')}" placeholder="0">
                            </label>
                        </div>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Border color
                                <input type="color" id="pp-format-pie-border-color" value="${toHexColor(v.options?.SLICE_BORDER_COLOR, '#ffffff')}">
                            </label>
                            <label class="etlsql-dsgn-label">Border width (px)
                                <input type="number" id="pp-format-pie-border-width" class="form-control" min="0" max="20" step="0.5" value="${esc(v.options?.SLICE_BORDER_WIDTH || '2')}">
                            </label>
                        </div>
                        <label class="etlsql-dsgn-label">Start angle (degrees)
                            <input type="number" id="pp-format-pie-start-angle" class="form-control" min="-360" max="360" step="15" value="${esc(v.options?.START_ANGLE || '')}" placeholder="0° (12 o'clock)">
                        </label>` : ''}
                        ${isCartesian ? `<div class="etlsql-format-axis-grid">
                            <strong>X axis</strong><strong>Y axis</strong>
                            <select data-axis="x" data-axis-key="SCALE" class="form-control">${['LINEAR', 'LOG'].map(value => `<option value="${value}"${(xAxis.SCALE || 'LINEAR').toUpperCase() === value ? ' selected' : ''}>Scale: ${value}</option>`).join('')}</select>
                            <select data-axis="y" data-axis-key="SCALE" class="form-control">${['LINEAR', 'LOG'].map(value => `<option value="${value}"${(yAxis.SCALE || 'LINEAR').toUpperCase() === value ? ' selected' : ''}>Scale: ${value}</option>`).join('')}</select>
                            <input data-axis="x" data-axis-key="LABEL" class="form-control" value="${esc(xAxis.LABEL || xAxis.label || '')}" placeholder="Label">
                            <input data-axis="y" data-axis-key="LABEL" class="form-control" value="${esc(yAxis.LABEL || yAxis.label || '')}" placeholder="Label">
                            <input data-axis="x" data-axis-key="MIN" class="form-control" value="${esc(xAxis.MIN || xAxis.min || '')}" placeholder="Min · Auto">
                            <input data-axis="y" data-axis-key="MIN" class="form-control" value="${esc(yAxis.MIN || yAxis.min || '')}" placeholder="Min · Auto">
                            <input data-axis="x" data-axis-key="MAX" class="form-control" value="${esc(xAxis.MAX || xAxis.max || '')}" placeholder="Max · Auto">
                            <input data-axis="y" data-axis-key="MAX" class="form-control" value="${esc(yAxis.MAX || yAxis.max || '')}" placeholder="Max · Auto">
                            <input data-axis="x" data-axis-key="FORMAT" class="form-control" value="${esc(xAxis.FORMAT || xAxis.format || '')}" placeholder="Format">
                            <input data-axis="y" data-axis-key="FORMAT" class="form-control" value="${esc(yAxis.FORMAT || yAxis.format || '')}" placeholder="Format">
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="x" data-axis-key="INCLUDE_ZERO" data-axis-boolean ${(xAxis.INCLUDE_ZERO || '').toUpperCase() === 'ON' ? 'checked' : ''}><span>Include zero</span></label>
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="y" data-axis-key="INCLUDE_ZERO" data-axis-boolean ${(yAxis.INCLUDE_ZERO || '').toUpperCase() === 'ON' ? 'checked' : ''}><span>Include zero</span></label>
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="x" data-axis-key="REVERSE" data-axis-boolean ${(xAxis.REVERSE || '').toUpperCase() === 'ON' ? 'checked' : ''}><span>Reverse</span></label>
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="y" data-axis-key="REVERSE" data-axis-boolean ${(yAxis.REVERSE || '').toUpperCase() === 'ON' ? 'checked' : ''}><span>Reverse</span></label>
                            <input type="number" min="2" max="100" data-axis="x" data-axis-key="MAJOR_TICK_COUNT" class="form-control" value="${esc(xAxis.MAJOR_TICK_COUNT || '')}" placeholder="Major ticks · Auto">
                            <input type="number" min="2" max="100" data-axis="y" data-axis-key="MAJOR_TICK_COUNT" class="form-control" value="${esc(yAxis.MAJOR_TICK_COUNT || '')}" placeholder="Major ticks · Auto">
                            <input type="number" min="0" step="any" data-axis="x" data-axis-key="TICK_INTERVAL" class="form-control" value="${esc(xAxis.TICK_INTERVAL || '')}" placeholder="Tick interval · Auto">
                            <input type="number" min="0" step="any" data-axis="y" data-axis-key="TICK_INTERVAL" class="form-control" value="${esc(yAxis.TICK_INTERVAL || '')}" placeholder="Tick interval · Auto">
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="x" data-axis-key="MINOR_TICKS" data-axis-boolean ${(xAxis.MINOR_TICKS || '').toUpperCase() === 'ON' ? 'checked' : ''}><span>Minor ticks</span></label>
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="y" data-axis-key="MINOR_TICKS" data-axis-boolean ${(yAxis.MINOR_TICKS || '').toUpperCase() === 'ON' ? 'checked' : ''}><span>Minor ticks</span></label>
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="x" data-axis-key="AXIS_LINE" data-axis-boolean ${(xAxis.AXIS_LINE || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Axis line</span></label>
                            <label class="etlsql-format-toggle"><input type="checkbox" data-axis="y" data-axis-key="AXIS_LINE" data-axis-boolean ${(yAxis.AXIS_LINE || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Axis line</span></label>
                            <select data-axis="x" data-axis-key="LABEL_ROTATION" class="form-control">${['AUTO', '0', '45', '90'].map(value => `<option value="${value}"${(xAxis.LABEL_ROTATION || 'AUTO').toUpperCase() === value ? ' selected' : ''}>${value === 'AUTO' ? 'Rotation · Auto' : `${value}°`}</option>`).join('')}</select>
                            <select data-axis="y" data-axis-key="LABEL_ROTATION" class="form-control">${['AUTO', '0', '45', '90'].map(value => `<option value="${value}"${(yAxis.LABEL_ROTATION || 'AUTO').toUpperCase() === value ? ' selected' : ''}>${value === 'AUTO' ? 'Rotation · Auto' : `${value}°`}</option>`).join('')}</select>
                            <input type="number" min="0" data-axis="x" data-axis-key="LABEL_SKIP" class="form-control" value="${esc(xAxis.LABEL_SKIP || '')}" placeholder="Label skip · Auto">
                            <input type="number" min="0" data-axis="y" data-axis-key="LABEL_SKIP" class="form-control" value="${esc(yAxis.LABEL_SKIP || '')}" placeholder="Label skip · Auto">
                        </div>` : ''}
                    </div>
                </details>` : ''}

                ${isChart ? `<details class="etlsql-format-group">
                    <summary>Marks & labels</summary>
                    <div class="etlsql-format-group-body">
                        <label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-data-labels" ${(v.options?.DATA_LABELS || 'OFF').toUpperCase() === 'ON' ? 'checked' : ''}><span>Show data labels</span></label>
                        <label class="etlsql-dsgn-label">Label position
                            <select id="pp-format-data-label-position" class="form-control">
                                ${['OUTSIDE_TOP', 'OUTSIDE_MIDDLE', 'OUTSIDE_BOTTOM', 'INSIDE_TOP', 'INSIDE_MIDDLE', 'INSIDE_BOTTOM'].map(value => `<option${(v.options?.['DATA_LABELS:POSITION'] || 'OUTSIDE_TOP').toUpperCase() === value ? ' selected' : ''}>${value.replaceAll('_', ' ')}</option>`).join('')}
                            </select>
                        </label>
                        ${v.type === 'LINE' || v.type === 'COMBO' ? `<label class="etlsql-format-toggle"><input type="checkbox" id="pp-format-symbols" ${(v.options?.SYMBOLS || 'ON').toUpperCase() !== 'OFF' ? 'checked' : ''}><span>Show data points</span></label>` : ''}
                        ${supportsStacking ? `<label class="etlsql-dsgn-label">Stacking
                            <select id="pp-format-stacked" class="form-control">
                                <option value="OFF"${(v.options?.STACKED || 'OFF').toUpperCase() === 'OFF' ? ' selected' : ''}>Off</option>
                                <option value="ON"${(v.options?.STACKED || '').toUpperCase() === 'ON' ? ' selected' : ''}>Stacked</option>
                                <option value="100PCT"${(v.options?.STACKED || '').toUpperCase() === '100PCT' ? ' selected' : ''}>100% stacked</option>
                            </select>
                        </label>` : ''}
                        ${v.type === 'BAR' || v.type === 'HBAR' || v.type === 'HORIZONTALBAR' || v.type === 'COMBO' ? `<label class="etlsql-dsgn-label">Bar width
                            <div class="etlsql-dsgn-slider-row"><input type="range" id="pp-format-band-size" min="0.1" max="1" step="0.05" value="${esc(v.options?.BAND_SIZE || '0.75')}"><output id="pp-format-band-size-value">${esc(v.options?.BAND_SIZE || '0.75')}</output></div>
                            <span class="etlsql-format-hint">Narrower bars create more spacing.</span>
                        </label>
                        <label class="etlsql-dsgn-label">Series gap
                            <div class="etlsql-dsgn-slider-row"><input type="range" id="pp-format-series-gap" min="0" max="1" step="0.05" value="${esc(v.options?.SERIES_GAP || '0')}"><output id="pp-format-series-gap-value">${esc(v.options?.SERIES_GAP || '0')}</output></div>
                        </label>
                        <label class="etlsql-dsgn-label">Outer padding
                            <div class="etlsql-dsgn-slider-row"><input type="range" id="pp-format-outer-padding" min="0" max="1" step="0.05" value="${esc(v.options?.OUTER_PADDING || '0')}"><output id="pp-format-outer-padding-value">${esc(v.options?.OUTER_PADDING || '0')}</output></div>
                        </label>` : ''}
                        <label class="etlsql-dsgn-label">Overlays
                            <textarea id="pp-format-overlays" class="form-control etlsql-code-editor" rows="3" placeholder="OVERLAYS (GOAL(100) AS DASHED)">${esc(overlays)}</textarea>
                        </label>
                    </div>
                </details>` : ''}

                ${isChart ? `<details class="etlsql-format-group">
                    <summary>Series palette <span>${palette.length || 'Theme'}</span></summary>
                    <div class="etlsql-format-group-body">
                        <p class="etlsql-format-hint">Colors are written to the visual STYLE palette in series order.</p>
                        <div class="etlsql-format-palette-list">
                            ${palette.map((color, index) => `<div>
                                <input type="color" data-palette-color="${index}" value="${toHexColor(color, '#2563eb')}">
                                <input class="form-control" data-palette-text="${index}" value="${esc(color)}" aria-label="Palette color ${index + 1}">
                                <button type="button" data-palette-remove="${index}" aria-label="Remove palette color">×</button>
                            </div>`).join('')}
                        </div>
                        <button type="button" class="etlsql-format-add" data-palette-add>+ Add series color</button>
                        <p class="etlsql-format-hint">Named colors stay attached to a series or category when its order changes.</p>
                        <div class="etlsql-format-palette-list">
                            ${namedColors.map(({ name, color }, index) => `<div data-named-color-row="${index}">
                                <input class="form-control" data-named-color-name value="${esc(name)}" aria-label="Series or category name">
                                <input type="color" data-named-color-value value="${toHexColor(color, '#2563eb')}" aria-label="Assigned color">
                                <button type="button" data-named-color-remove aria-label="Remove assigned color">×</button>
                            </div>`).join('')}
                        </div>
                        <button type="button" class="etlsql-format-add" data-named-color-add>+ Assign named color</button>
                    </div>
                </details>` : ''}

                ${isTable ? `<details class="etlsql-format-group">
                    <summary>Table cells <span>${tableFields.length}</span></summary>
                    <div class="etlsql-format-group-body etlsql-format-field-list">
                        ${tableFields.map(field => {
                            const key = Object.keys(v.mappings || {}).find(role => role.toUpperCase() === field.toUpperCase()) || field;
                            const value = fields[key] || fields[key.toUpperCase()] || {};
                            return `<div class="etlsql-format-field" data-format-field="${esc(key)}">
                                <strong>${esc(field)}</strong>
                                <input class="form-control" data-field-format value="${esc(value.format || '')}" placeholder="Format · C2">
                                <label class="etlsql-format-toggle"><input type="checkbox" data-field-data-bar ${value.dataBar ? 'checked' : ''}><span>Data bar</span></label>
                                <input type="color" data-field-data-bar-color value="${toHexColor(value.dataBarColor, '#4472c4')}" aria-label="Data bar color">
                            </div>`;
                        }).join('') || '<p class="etlsql-format-hint">Map or sample a column to format table cells.</p>'}
                    </div>
                </details>` : ''}

                ${supportsRules ? `<details class="etlsql-format-group" ${rules.length ? 'open' : ''}>
                    <summary>${isChart || isTable ? 'Conditional formatting' : 'Alert states'} <span>${rules.length}</span></summary>
                    <div class="etlsql-format-group-body">
                        <div class="etlsql-format-rule-list">
                            ${rules.map((rule, index) => {
                                const condition = splitRuleCondition(rule.condition, availableFields[0]);
                                return `<div class="etlsql-format-rule" data-rule-index="${index}">
                                    <div class="etlsql-format-rule-line"><span>IF</span>
                                        <select data-rule-field class="form-control">${fieldOptions(condition.field)}${availableFields.includes(condition.field) ? '' : `<option selected>${esc(condition.field)}</option>`}</select>
                                        <select data-rule-operator class="form-control">${['<', '<=', '=', '!=', '>=', '>'].map(operator => `<option${operator === condition.operator ? ' selected' : ''}>${esc(operator)}</option>`).join('')}</select>
                                        <input data-rule-value class="form-control" value="${esc(condition.value)}" aria-label="Comparison value">
                                    </div>
                                    <div class="etlsql-format-rule-result"><span>THEN</span><label>Fill <input type="color" data-rule-background value="${toHexColor(rule.backgroundColor, '#fee2e2')}"></label><label>Text <input type="color" data-rule-font value="${toHexColor(rule.fontColor, '#991b1b')}"></label><button type="button" data-rule-remove aria-label="Remove rule">Remove</button></div>
                                </div>`;
                            }).join('')}
                        </div>
                        <button type="button" class="etlsql-format-add" data-rule-add>+ Add rule</button>
                    </div>
                </details>` : ''}`;
    }

    export function renderFormattingSectionHtml(v) {
        const bg = v.options?.BACKGROUND || '';
        const color = v.options?.COLOR || '';
        const border = v.options?.BORDER || '';
        const radius = v.options?.BORDER_RADIUS || '';
        const font = v.options?.FONT || '';
        const fontSize = v.options?.FONT_SIZE || '';
        const fontWeight = v.options?.FONT_WEIGHT || '';
        const shadow = v.options?.SHADOW || '';
        const opacity = v.options?.OPACITY || '';

        return `
            <details class="etlsql-format-group etlsql-dsgn-formatting-section">
                <summary>Card style <span>${[bg, border, shadow, radius].filter(Boolean).length || 'Default'}</span></summary>
                <div class="etlsql-format-group-body">
                
                <label class="etlsql-dsgn-label">Background Color
                    <div class="etlsql-dsgn-color-picker-row">
                        <input type="color" id="pp-fmt-bg-picker" value="${toHexColor(bg, '#ffffff')}">
                        <input type="text" id="pp-fmt-bg-text" class="form-control" placeholder="#ffffff, transparent" value="${esc(bg)}">
                    </div>
                    <div class="etlsql-dsgn-swatch-row" data-target-input="#pp-fmt-bg-text" data-target-picker="#pp-fmt-bg-picker">
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#ffffff" title="White" data-color="#ffffff"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#f8fafc" title="Slate Light" data-color="#f8fafc"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#0f172a" title="Dark Slate" data-color="#0f172a"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#2563eb" title="Blue Accent" data-color="#2563eb"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#10b981" title="Emerald Green" data-color="#10b981"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#f59e0b" title="Amber" data-color="#f59e0b"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#ef4444" title="Ruby Red" data-color="#ef4444"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:repeating-linear-gradient(45deg,#ccc,#ccc 2px,#fff 2px,#fff 4px)" title="Transparent" data-color="transparent"></button>
                    </div>
                </label>

                <label class="etlsql-dsgn-label">Text Color
                    <div class="etlsql-dsgn-color-picker-row">
                        <input type="color" id="pp-fmt-color-picker" value="${toHexColor(color, '#0f172a')}">
                        <input type="text" id="pp-fmt-color-text" class="form-control" placeholder="#0f172a" value="${esc(color)}">
                    </div>
                    <div class="etlsql-dsgn-swatch-row" data-target-input="#pp-fmt-color-text" data-target-picker="#pp-fmt-color-picker">
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#0f172a" title="Dark Slate" data-color="#0f172a"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#475569" title="Muted Slate" data-color="#475569"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#ffffff" title="White" data-color="#ffffff"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#2563eb" title="Blue Accent" data-color="#2563eb"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#10b981" title="Green" data-color="#10b981"></button>
                        <button type="button" class="etlsql-dsgn-swatch-chip" style="background:#ef4444" title="Red" data-color="#ef4444"></button>
                    </div>
                </label>

                <label class="etlsql-dsgn-label">Border
                    <input type="text" id="pp-fmt-border-text" class="form-control" placeholder="1px solid #e2e8f0, none" value="${esc(border)}">
                    <div class="etlsql-dsgn-preset-chips" data-target-input="#pp-fmt-border-text">
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="none">None</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="1px solid #e2e8f0">1px Subtle</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="1px solid #94a3b8">1px Muted</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="2px solid #2563eb">2px Accent</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="1px dashed #cbd5e1">Dashed</button>
                    </div>
                </label>

                <label class="etlsql-dsgn-label">Border Radius
                    <div class="etlsql-dsgn-slider-row">
                        <input type="range" id="pp-fmt-radius-slider" min="0" max="32" step="1" value="${parseNumericRadius(radius, 8)}">
                        <input type="text" id="pp-fmt-radius-text" class="form-control" placeholder="8px" value="${esc(radius)}">
                    </div>
                    <div class="etlsql-dsgn-preset-chips" data-target-input="#pp-fmt-radius-text" data-target-slider="#pp-fmt-radius-slider">
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="0px">0px</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="4px">4px</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="8px">8px</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="12px">12px</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="16px">16px</button>
                        <button type="button" class="etlsql-dsgn-preset-chip" data-val="9999px">Pill</button>
                    </div>
                </label>

                <div class="etlsql-dsgn-typography-grid">
                    <label class="etlsql-dsgn-label">Font Family
                        <select id="pp-fmt-font-select" class="form-control">
                            <option value="">Default</option>
                            <option value="Inter, sans-serif"${font.includes('Inter') ? ' selected' : ''}>Inter</option>
                            <option value="Segoe UI, sans-serif"${font.includes('Segoe') ? ' selected' : ''}>Segoe UI</option>
                            <option value="Roboto, sans-serif"${font.includes('Roboto') ? ' selected' : ''}>Roboto</option>
                            <option value="ui-monospace, Consolas, monospace"${font.includes('monospace') ? ' selected' : ''}>Monospace</option>
                            <option value="Georgia, serif"${font.includes('Georgia') ? ' selected' : ''}>Georgia</option>
                        </select>
                    </label>
                    <label class="etlsql-dsgn-label">Font Size
                        <select id="pp-fmt-size-select" class="form-control">
                            <option value="">Default</option>
                            <option value="11px"${fontSize === '11px' ? ' selected' : ''}>11px (XS)</option>
                            <option value="12px"${fontSize === '12px' ? ' selected' : ''}>12px (Small)</option>
                            <option value="13px"${fontSize === '13px' ? ' selected' : ''}>13px (Compact)</option>
                            <option value="14px"${fontSize === '14px' ? ' selected' : ''}>14px (Body)</option>
                            <option value="16px"${fontSize === '16px' ? ' selected' : ''}>16px (Medium)</option>
                            <option value="18px"${fontSize === '18px' ? ' selected' : ''}>18px (Large)</option>
                            <option value="20px"${fontSize === '20px' ? ' selected' : ''}>20px (XL)</option>
                            <option value="24px"${fontSize === '24px' ? ' selected' : ''}>24px (Title)</option>
                            <option value="32px"${fontSize === '32px' ? ' selected' : ''}>32px (KPI)</option>
                        </select>
                    </label>
                </div>
                <div class="etlsql-dsgn-typography-grid">
                    <label class="etlsql-dsgn-label">Font Weight
                        <select id="pp-fmt-weight-select" class="form-control">
                            <option value="">Default</option>
                            <option value="400"${fontWeight === '400' || fontWeight.toUpperCase() === 'NORMAL' ? ' selected' : ''}>400</option>
                            <option value="500"${fontWeight === '500' ? ' selected' : ''}>500</option>
                            <option value="600"${fontWeight === '600' || fontWeight.toUpperCase() === 'SEMIBOLD' ? ' selected' : ''}>600</option>
                            <option value="700"${fontWeight === '700' || fontWeight.toUpperCase() === 'BOLD' ? ' selected' : ''}>700</option>
                        </select>
                    </label>
                    <label class="etlsql-dsgn-label">Card Shadow
                        <select id="pp-fmt-shadow-select" class="form-control">
                            <option value="">Default</option>
                            <option value="OFF"${shadow.toUpperCase() === 'OFF' ? ' selected' : ''}>Flat (OFF)</option>
                            <option value="ON"${shadow.toUpperCase() === 'ON' ? ' selected' : ''}>Elevated (ON)</option>
                            <option value="0 4px 12px rgba(0,0,0,0.1)"${shadow.includes('12px') ? ' selected' : ''}>Medium</option>
                            <option value="0 8px 24px rgba(0,0,0,0.15)"${shadow.includes('24px') ? ' selected' : ''}>Heavy</option>
                        </select>
                    </label>
                </div>

                <label class="etlsql-dsgn-label">Card Opacity
                    <div class="etlsql-dsgn-slider-row">
                        <input type="range" id="pp-fmt-opacity-slider" min="10" max="100" step="5" value="${parseNumericOpacity(opacity, 100)}">
                        <input type="text" id="pp-fmt-opacity-text" class="form-control" placeholder="1" value="${esc(opacity)}">
                    </div>
                </label>
                </div>
            </details>
        `;
    }
