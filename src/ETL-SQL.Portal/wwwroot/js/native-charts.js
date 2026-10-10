/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/native-charts.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
"use strict";
/*!
 * ETL-SQL Portal operational charts — native-charts.js
 *
 * Owner: ETL-SQL Report Portal (internal operator UI). Not a shared report runtime asset.
 *
 * Boundary — deliberate, see docs/architecture/portal-ui.md:
 *   - Authored in Portal BrowserSources and compiled to wwwroot/js. NOT in
 *     ETL-SQL.ReportRuntime/Resources/Shared/, NOT copied to report hosts, NOT in the
 *     Report-SQL capability matrix.
 *   - NOT a PlotPlan consumer, and must not become one. It draws the orchestrator page's Gantt,
 *     sparkline, and dependency-graph views only.
 *   - Zero third-party dependencies. No import, no require, no remote URL.
 *
 * Gates: docs/architecture/standards/report-runtime-asset-standards.md section 7, asserted by
 * PortalOperationalChartAssetTests (ownership, dependency/license, accessibility, behaviour, footprint).
 *
 * Copyright (c) ETL-SQL contributors. Licensed under the repository's license.
 */
(function (global) {
    'use strict';
    const instances = new WeakMap();
    const entities = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
    const esc = (value) => String(value ?? '').replace(/[&<>"']/g, ch => entities[ch]);
    const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
    const objects = (value) => Array.isArray(value) ? value.filter(isObject) : [];
    function init(host) {
        const handlers = {};
        function handleClick(event) {
            const mark = event.target instanceof Element ? event.target.closest('[data-index]') : null;
            handlers.click?.({ name: mark?.dataset.name, dataIndex: Number(mark?.dataset.index || 0), event: { event } });
        }
        const api = {
            setOption(option) { render(host, option); },
            resize() { },
            dispose() { host.removeEventListener('click', handleClick); host.replaceChildren(); instances.delete(host); },
            on(name, callback) { handlers[name] = callback; },
            dispatchAction() { }
        };
        host.addEventListener('click', handleClick);
        instances.set(host, api);
        return api;
    }
    function render(host, value) {
        const option = isObject(value) ? value : {};
        const width = Math.max(320, host.clientWidth || 600), height = Math.max(120, host.clientHeight || 240), pad = 24;
        const series = objects(option.series);
        const graph = series.find(item => item.type === 'graph');
        let marks = '';
        if (graph) {
            const nodes = objects(graph.data), radius = Math.min(width, height) * .34, cx = width / 2, cy = height / 2;
            const positions = new Map(nodes.map((node, index) => [String(node.name), [cx + radius * Math.cos(index * 2 * Math.PI / Math.max(1, nodes.length)), cy + radius * Math.sin(index * 2 * Math.PI / Math.max(1, nodes.length))]]));
            marks += objects(graph.links).map(link => { const a = positions.get(String(link.source)), b = positions.get(String(link.target)); return a && b ? `<line x1="${a[0]}" y1="${a[1]}" x2="${b[0]}" y2="${b[1]}" stroke="#94a3b8"/>` : ''; }).join('');
            marks += nodes.map((node, index) => { const p = positions.get(String(node.name)); return `<g data-index="${index}" data-name="${esc(node.name)}"><circle cx="${p[0]}" cy="${p[1]}" r="7" fill="#3b82f6"/><text x="${p[0]}" y="${p[1] + 18}" text-anchor="middle" font-size="10">${esc(node.name)}</text></g>`; }).join('');
        }
        else {
            const values = series.flatMap(item => Array.isArray(item.data) ? item.data.map((point) => Number(Array.isArray(point) ? point.at(-1) : isObject(point) ? point.value ?? point : point) || 0) : []);
            const maximum = Math.max(1, ...values.map(Math.abs)), slot = (width - pad * 2) / Math.max(1, values.length);
            marks = values.map((value, index) => { const h = Math.abs(value) / maximum * (height - pad * 2); return `<rect data-index="${index}" x="${pad + index * slot + slot * .15}" y="${height - pad - h}" width="${slot * .7}" height="${h}" rx="2" fill="#3b82f6"/>`; }).join('');
        }
        const label = (isObject(option.title) ? option.title.text : '') || (isObject(option.aria) ? option.aria.label : '') || host.dataset.chartLabel || 'Operational chart';
        host.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="${esc(label)}" style="width:100%;height:100%">${marks}</svg>`;
    }
    global.nativeCharts = { init, getInstanceByDom: host => instances.get(host) };
})(window);
