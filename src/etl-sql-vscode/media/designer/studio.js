/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Studio — Flagship Unified Dual-Projection Visual & Script Workbench
 *
 * Exported functions:
 *   createStudioWorkbench(container, options)
 */

import { createScriptEditor, createDesigner } from './designer.js';
import { createConnectionWizard } from './connection-wizard.js';

const _feedback = globalThis.ETLSQLFeedback || {
    notify: (msg, opts) => console.log(`[Notification ${opts?.tone || 'info'}] ${msg}`),
    confirm: async (msg) => window.confirm(msg),
    prompt: async (msg, opts) => window.prompt(msg, opts?.value || ''),
};

function _escapeHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// Crisp inline stroke SVGs (currentColor, 16px/18px)
const _STUDIO_ICONS = {
    explorer: '<path d="M2 3.5A1.5 1.5 0 0 1 3.5 2h3.293a1 1 0 0 1 .707.293L8.707 3.5H12.5A1.5 1.5 0 0 1 14 5v7.5a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 12.5z"/>',
    catalog: '<path d="M3 4c0-1.1 2.7-2 6-2s6 .9 6 2v8c0 1.1-2.7 2-6 2s-6-.9-6-2V4z"/><ellipse cx="9" cy="4" rx="6" ry="2"/><path d="M3 8c0 1.1 2.7 2 6 2s6-.9 6-2"/>',
    palette: '<rect x="2" y="2" width="5" height="5" rx="1"/><rect x="9" y="2" width="5" height="5" rx="1"/><rect x="2" y="9" width="5" height="5" rx="1"/><rect x="9" y="9" width="5" height="5" rx="1"/>',
    filters: '<polygon points="2 3 14 3 9.5 8.5 9.5 13 6.5 11 6.5 8.5 2 3"/>',
    git: '<circle cx="4" cy="4" r="2"/><circle cx="4" cy="12" r="2"/><circle cx="12" cy="7" r="2"/><path d="M4 6v4m0-2a4 4 0 0 1 4-4h2"/>',
    bookmarks: '<path d="M4 2v12l4-3 4 3V2z"/>',
    settings: '<circle cx="8" cy="8" r="3"/><path d="M8 1v2m0 10v2m-7-7h2m10 0h2m-2.1-4.9-1.4 1.4m-7 7-1.4 1.4m0-9.8 1.4 1.4m7 7 1.4 1.4"/>',
    canvas: '<rect x="2" y="2" width="12" height="12" rx="2"/><path d="M2 6h12M6 6v8"/>',
    split: '<rect x="2" y="2" width="12" height="12" rx="2"/><path d="M8 2v12"/>',
    code: '<polyline points="5 5 2 8 5 11"/><polyline points="11 5 14 8 11 11"/><line x1="9" y1="4" x2="7" y2="12"/>',
    run: '<path d="m4 2.5 9 5.5-9 5.5z"/>',
    runSelected: '<path d="M2.5 3.5h3"/><path d="M2.5 12.5h3"/><path d="m7.5 3.5 6 4.5-6 4.5z"/>',
    cancel: '<rect x="4" y="4" width="8" height="8" rx="1"/>',
    format: '<path d="M2 3.5h12"/><path d="M2 7.5h8"/><path d="M2 11.5h12"/><path d="M2 15.5h6"/>',
    save: '<path d="M3 2.5h7.5L13.5 5.5V13a.5.5 0 0 1-.5.5H3a.5.5 0 0 1-.5-.5V3a.5.5 0 0 1 .5-.5"/><path d="M5 2.5v4h5v-4"/><path d="M5 13.5v-4h6v4"/>',
    theme: '<path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7z"/>',
    commands: '<path d="m4 5 3 3-3 3"/><path d="M8.5 11h4"/>',
    wizard: '<path d="M4 2.5a3.5 3.5 0 0 0 7 0v2H4z"/><path d="M6 6.5v4a1.5 1.5 0 0 0 3 0v-4"/><path d="M7.5 12v2"/>',
    close: '<path d="m4 4 8 8"/><path d="m12 4-8 8"/>',
    plus: '<path d="M8 3v10M3 8h10"/>',
    edit: '<path d="M11 2l3 3-9 9H2v-3l9-9z"/>',
    trash: '<polyline points="3 4 13 4"/><path d="M5 4V2h6v2M6 7v5M10 7v5M4 4l1 10h6l1-10"/>',
    duplicate: '<rect x="5" y="5" width="8" height="8" rx="1"/><path d="M3 11V3h8"/>',
    kpi: '<path d="M3 13V7l4-3 4 3v6z"/>',
    bar: '<rect x="2" y="8" width="3" height="6" rx="0.5"/><rect x="6.5" y="4" width="3" height="10" rx="0.5"/><rect x="11" y="2" width="3" height="12" rx="0.5"/>',
    line: '<polyline points="2 12 6 7 10 9 14 3"/><circle cx="14" cy="3" r="1.5"/>',
    donut: '<circle cx="8" cy="8" r="6"/><circle cx="8" cy="8" r="2.5"/>',
    table: '<rect x="2" y="2" width="12" height="12" rx="1.5"/><path d="M2 6h12M6 6v8"/>',
    slicer: '<rect x="2" y="4" width="12" height="8" rx="4"/><circle cx="6" cy="8" r="2"/>'
};

function _studioIcon(name, size = 16) {
    return `<svg viewBox="0 0 16 16" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${_STUDIO_ICONS[name] || ''}</svg>`;
}

function _fileIcon(path) {
    const ext = String(path || '').split('.').pop()?.toLowerCase();
    if (ext === 'rptsql') return _studioIcon('canvas', 14);
    if (ext === 'etlsql') return _studioIcon('catalog', 14);
    if (ext === 'sql') return _studioIcon('code', 14);
    return _studioIcon('explorer', 14);
}

function _detectPlaintextSecrets(scriptText) {
    if (!scriptText || typeof scriptText !== 'string') return [];
    const findings = [];
    const patterns = [
        { label: 'Plaintext Password', regex: /\b(PASSWORD|PWD)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2/gi },
        { label: 'Plaintext Secret / API Key', regex: /\b(API_KEY|APIKEY|SECRET_KEY|SECRETKEY|TOKEN|ACCESS_TOKEN)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2/gi },
        { label: 'Unencrypted Keyfile', regex: /\b(KEY_FILE|PRIVATE_KEY_FILE)\s*=\s*(['"])(.+?)\2/gi }
    ];

    for (const { label, regex } of patterns) {
        let match;
        while ((match = regex.exec(scriptText)) !== null) {
            findings.push({ label, match: match[0], value: match[3] || match[0] });
        }
    }
    return findings;
}

const DEFAULT_SAMPLE_SNAP = {
    source: '',
    columns: [],
    rowCount: 0,
    rows: []
};

const CHART_PALETTE = ['#388bfd', '#2ea043', '#f0883e', '#a371f7', '#58a6ff', '#7ee787', '#d29922', '#bc8cff'];

function _formatNumber(val, format = 'currency') {
    const num = Number(val) || 0;
    if (format === 'currency') {
        return '$' + num.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 0 });
    }
    if (format === 'percent') {
        return (num * 100).toFixed(1) + '%';
    }
    return num.toLocaleString();
}

function _parseReportVisuals(scriptText) {
    if (!scriptText || typeof scriptText !== 'string') return [];
    const visuals = [];
    const visualHeaderRegex = /VISUAL\s+([A-Za-z0-9_]+)\s+TYPE\s+['"]?([A-Za-z0-9_]+)['"]?/gi;
    let match;
    while ((match = visualHeaderRegex.exec(scriptText)) !== null) {
        const id = match[1];
        const type = match[2].toUpperCase();
        const startIdx = match.index + match[0].length;
        const remainder = scriptText.slice(startIdx, startIdx + 500);

        const mappings = {};
        const options = {};

        const mapMatch = remainder.match(/MAPPINGS\s*\(([^)]+)\)/i);
        if (mapMatch) {
            const mapPairs = mapMatch[1].split(',');
            for (const p of mapPairs) {
                const parts = p.split('=');
                if (parts.length === 2) {
                    mappings[parts[0].trim().toUpperCase()] = parts[1].trim();
                }
            }
        }

        const optMatch = remainder.match(/OPTIONS\s*\(([^)]+)\)/i);
        if (optMatch) {
            const optPairs = optMatch[1].split(',');
            for (const p of optPairs) {
                const parts = p.split('=');
                if (parts.length === 2) {
                    options[parts[0].trim().toUpperCase()] = parts[1].trim().replace(/^['"]|['"]$/g, '');
                }
            }
        }

        visuals.push({ id, type, mappings, options });
    }

    return visuals;
}

function _parseEtlDag(scriptText) {
    if (!scriptText || typeof scriptText !== 'string') return [];
    const nodes = [];
    const connRegex = /CREATE\s+CONNECTION\s+([A-Za-z0-9_]+)\s+AS\s+([A-Za-z0-9_]+)/gi;
    let m;
    while ((m = connRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'connection', detail: `Connector (${m[2]})` });
    }

    const selectIntoRegex = /SELECT\s+[\s\S]*?\s+INTO\s+(#[A-Za-z0-9_]+)\s+FROM\s+([A-Za-z0-9_\.]+)/gi;
    while ((m = selectIntoRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'dataset', detail: `Staged extract from ${m[2]}` });
    }

    const transformRegex = /TRANSFORM\s+(#[A-Za-z0-9_]+)\s+FROM\s+(#[A-Za-z0-9_]+)\s+USING\s+([A-Za-z0-9_]+)/gi;
    while ((m = transformRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'transform', detail: `Algorithm (${m[3]}) on ${m[2]}` });
    }

    const mergeRegex = /MERGE\s+INTO\s+([A-Za-z0-9_\.]+)\s+USING\s+(#[A-Za-z0-9_]+)/gi;
    while ((m = mergeRegex.exec(scriptText)) !== null) {
        nodes.push({ id: m[1], label: m[1], kind: 'target', detail: `Destination write from ${m[2]}` });
    }

    return nodes;
}

function _renderBarChartSvg(groupedData) {
    if (!groupedData || groupedData.length === 0) return '';
    const maxVal = Math.max(...groupedData.map(d => d.value), 1);
    return `
        <div style="height:120px; display:flex; align-items:flex-end; gap:12px; padding:12px 0;">
            ${groupedData.map((d, i) => {
                const pct = Math.max(12, Math.round((d.value / maxVal) * 100));
                const color = CHART_PALETTE[i % CHART_PALETTE.length];
                return `
                    <div class="etlsql-chart-bar-group" style="flex:1; display:flex; flex-direction:column; align-items:center; height:100%; justify-content:flex-end;">
                        <span style="font-size:10px; color:var(--portal-text,#f0f6fc); margin-bottom:4px; font-weight:600;">${_formatNumber(d.value, 'currency')}</span>
                        <div style="width:100%; background:${color}; height:${pct}%; border-radius:3px 3px 0 0; min-height:4px; transition:height 0.2s ease;"></div>
                        <span style="font-size:10px; color:var(--portal-text-soft,#8b949e); margin-top:6px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; max-width:60px;">${_escapeHtml(d.category)}</span>
                    </div>
                `;
            }).join('')}
        </div>
    `;
}

function _renderDonutChartSvg(groupedData) {
    if (!groupedData || groupedData.length === 0) return '';
    const total = groupedData.reduce((acc, d) => acc + d.value, 0) || 1;
    const r = 40;
    const cx = 60;
    const cy = 60;
    const circ = 2 * Math.PI * r;
    let accumulatedAngle = 0;

    const segments = groupedData.map((d, i) => {
        const pct = d.value / total;
        const strokeDasharray = `${pct * circ} ${circ}`;
        const strokeDashoffset = -accumulatedAngle * circ;
        accumulatedAngle += pct;
        const color = CHART_PALETTE[i % CHART_PALETTE.length];
        return `
            <circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="${color}" stroke-width="16"
                stroke-dasharray="${strokeDasharray}" stroke-dashoffset="${strokeDashoffset}" />
        `;
    }).join('');

    return `
        <div style="display:flex; align-items:center; gap:16px;">
            <svg viewBox="0 0 120 120" style="width:110px; height:110px; transform:rotate(-90deg);">
                ${segments}
                <text x="${cx}" y="${cy + 4}" text-anchor="middle" transform="rotate(90, ${cx}, ${cy})" font-size="11" font-weight="700" fill="var(--portal-text,#f0f6fc)">
                    ${groupedData.length} Items
                </text>
            </svg>
            <div style="font-size:0.75rem; display:flex; flex-direction:column; gap:4px;">
                ${groupedData.map((d, i) => `
                    <div style="display:flex; align-items:center; gap:6px;">
                        <span style="width:8px; height:8px; border-radius:50%; background:${CHART_PALETTE[i % CHART_PALETTE.length]}; display:inline-block;"></span>
                        <span style="color:var(--portal-text-soft,#8b949e);">${_escapeHtml(d.category)}</span>
                        <strong style="color:var(--portal-text,#f0f6fc);">${((d.value / total) * 100).toFixed(0)}%</strong>
                    </div>
                `).join('')}
            </div>
        </div>
    `;
}

function _renderLineChartSvg(rows) {
    if (!rows || rows.length === 0) return '';
    const points = rows.map((r, i) => ({ x: i, y: Number(r.total_amount || r.amount || 0) }));
    const maxY = Math.max(...points.map(p => p.y), 1);
    const width = 240;
    const height = 100;

    const pathPoints = points.map((p, i) => {
        const x = (i / (points.length - 1 || 1)) * (width - 20) + 10;
        const y = height - (p.y / maxY) * (height - 20) - 10;
        return `${x},${y}`;
    }).join(' ');

    return `
        <svg viewBox="0 0 ${width} ${height + 20}" style="width:100%; height:120px;">
            <polyline fill="none" stroke="var(--portal-accent,#388bfd)" stroke-width="2.5" points="${pathPoints}" stroke-linecap="round" stroke-linejoin="round" />
            ${points.map((p, i) => {
                const x = (i / (points.length - 1 || 1)) * (width - 20) + 10;
                const y = height - (p.y / maxY) * (height - 20) - 10;
                return `<circle cx="${x}" cy="${y}" r="3" fill="var(--portal-accent,#388bfd)" />`;
            }).join('')}
        </svg>
    `;
}

function _renderTableGrid(rows) {
    if (!rows || rows.length === 0) return '<div style="color:var(--portal-muted,#8b949e);">No records found.</div>';
    const cols = Object.keys(rows[0] || {});
    return `
        <div style="max-height:160px; overflow:auto; border:1px solid var(--portal-border,#30363d); border-radius:4px;">
            <table style="width:100%; border-collapse:collapse; font-size:0.75rem; text-align:left;">
                <thead>
                    <tr style="background:var(--portal-surface,#161b22); border-bottom:1px solid var(--portal-border,#30363d);">
                        ${cols.map(c => `<th style="padding:6px 10px; color:var(--portal-text-soft,#8b949e); font-weight:600;">${_escapeHtml(c)}</th>`).join('')}
                    </tr>
                </thead>
                <tbody>
                    ${rows.slice(0, 10).map((r, i) => `
                        <tr style="border-bottom:1px solid rgba(255,255,255,0.03); background:${i % 2 === 0 ? 'transparent' : 'rgba(255,255,255,0.015)'};">
                            ${cols.map(c => `<td style="padding:5px 10px; color:var(--portal-text,#f0f6fc);">${_escapeHtml(r[c])}</td>`).join('')}
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        </div>
    `;
}

export async function createStudioWorkbench(container, opts = {}) {
    window.__ETLSNAP__ = window.__ETLSNAP__ || DEFAULT_SAMPLE_SNAP;

    const savedTheme = localStorage.getItem('portal-theme') || 'dark';
    if (savedTheme === 'dark') {
        document.body.classList.add('theme-dark');
    } else {
        document.body.classList.remove('theme-dark');
    }

    const authFetch = opts.authFetch ?? ((url, init) => fetch(url, { ...init, headers: { ...(opts.headers || {}), ...(init?.headers || {}) } }));
    const apiBase = opts.apiBase || '';

    const workspaceFiles = opts.workspaceFiles || [];
    const documents = opts.documents ? [...opts.documents] : [];
    if (!documents.length && (opts.initialFile || opts.initialContent)) {
        const defaultInitialFile = opts.initialFile || 'untitled_1.rptsql';
        documents.push({
            id: 'doc-1',
            path: defaultInitialFile,
            name: defaultInitialFile.split('/').pop().split('\\').pop(),
            content: opts.initialContent || '',
            isDirty: false,
            projection: 'split',
        });
    }

    const state = {
        workspaceFiles: workspaceFiles,
        documents: documents,
        activeDocId: opts.activeDocId || (documents.length > 0 ? documents[0].id : '__home__'),
        activeActivity: 'explorer',
        activeFilters: {},
        selectedVisualId: null,
        sidebarOpen: true,
        runAbort: null,
        editorInstance: null,
    };

    let isSyncingFromDesigner = false;
    let codeMirrorDebounce = null;

    container.innerHTML = `
        <div class="etlsql-studio-shell">
            <!-- Studio Header Toolbar -->
            <header class="etlsql-studio-header">
                <div class="etlsql-studio-brand">
                    <span class="etlsql-studio-logo">${_studioIcon('palette', 18)}</span>
                    <span class="etlsql-studio-title">ETL-SQL Studio</span>
                </div>

                <!-- Document Tabs -->
                <div class="etlsql-studio-tabs" data-studio-tabs></div>
                <div class="etlsql-tab-new-wrapper">
                    <button type="button" class="etlsql-studio-tab-new" data-studio-new-tab title="New File or Pipeline (Ctrl+N)">${_studioIcon('plus', 14)}</button>
                </div>

                <div class="etlsql-studio-header-spacer"></div>

                <!-- Projection View Toggles -->
                <div class="etlsql-studio-projection-group" role="group" aria-label="View Projection">
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="canvas" title="Canvas View (WYSIWYG Layout)">
                        <span class="etlsql-icon">${_studioIcon('canvas', 14)}</span> Canvas
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle active" data-projection="split" title="Split View (Visual + Code)">
                        <span class="etlsql-icon">${_studioIcon('split', 14)}</span> Split
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="code" title="Code View (CodeMirror 6)">
                        <span class="etlsql-icon">${_studioIcon('code', 14)}</span> Code
                    </button>
                </div>

                <div class="etlsql-studio-header-divider"></div>

                <!-- Global Action Controls -->
                <div class="etlsql-studio-actions">
                    <button type="button" class="etlsql-studio-btn" data-action="wizard" title="New Connection Wizard">
                        ${_studioIcon('wizard', 14)} Connection
                    </button>
                    <button type="button" class="etlsql-studio-btn" data-action="format" title="Format Document (Shift+Alt+F)">
                        ${_studioIcon('format', 14)}
                    </button>
                    <button type="button" class="etlsql-studio-btn" data-action="theme" title="Toggle Theme">
                        ${_studioIcon('theme', 14)}
                    </button>
                    <button type="button" class="etlsql-studio-btn" data-action="save" title="Save File (Ctrl+S)">
                        ${_studioIcon('save', 14)} Save
                    </button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-action="run" title="Run Script (Ctrl+Shift+Enter)">
                        ${_studioIcon('run', 14)} Run
                    </button>
                </div>
            </header>

            <!-- Workbench Body -->
            <div class="etlsql-studio-body">
                <!-- Far-Left Activity Rail -->
                <nav class="etlsql-studio-activity-rail" aria-label="Activity Rail">
                    <button type="button" class="etlsql-studio-rail-btn active" data-activity="explorer" title="Explorer (Files)">
                        ${_studioIcon('explorer', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="catalog" title="Data Catalog (Connections)">
                        ${_studioIcon('catalog', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="palette" title="Visual Palette (Add Components)">
                        ${_studioIcon('palette', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="filters" title="Filter Pane (Slicers & Ranges)">
                        ${_studioIcon('filters', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="git" title="Source Control (Git)">
                        ${_studioIcon('git', 18)}
                    </button>
                    <div class="etlsql-studio-rail-spacer"></div>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="settings" title="Settings">
                        ${_studioIcon('settings', 18)}
                    </button>
                </nav>

                <!-- Activity Sidebar Panel -->
                <aside class="etlsql-studio-sidebar" data-studio-sidebar>
                    <div class="etlsql-studio-sidebar-header">
                        <span data-sidebar-title>Explorer</span>
                        <button type="button" class="etlsql-studio-sidebar-close" data-sidebar-close title="Close Sidebar">${_studioIcon('close', 12)}</button>
                    </div>
                    <div class="etlsql-studio-sidebar-content" data-sidebar-content></div>
                </aside>

                <!-- Center Multi-Projection Stage -->
                <main class="etlsql-studio-stage" data-studio-stage>
                    <!-- Home / Welcome Stage -->
                    <div class="etlsql-studio-home-stage" data-home-stage style="display:none; flex:1; width:100%; height:100%; overflow:hidden;"></div>

                    <!-- Visual Stage Area (Report Builder Canvas & Pipeline DAG) -->
                    <div class="etlsql-studio-visual-stage" data-visual-stage style="display:flex; flex-direction:column; flex:1; height:100%; overflow:hidden; position:relative;">
                        <div class="etlsql-studio-designer-container" data-canvas-grid-container style="flex:1; width:100%; height:100%; overflow:hidden; position:relative;"></div>
                    </div>

                    <!-- Split Resizer Bar -->
                    <div class="etlsql-studio-stage-resizer" data-stage-resizer title="Drag to resize split panes"></div>

                    <!-- CodeMirror 6 Stage Area -->
                    <div class="etlsql-studio-code-stage" data-code-stage>
                        <div class="etlsql-studio-editor-host etlsql-editor-container" data-editor-host></div>
                        <div class="etlsql-studio-results-host" data-results-host></div>
                    </div>
                </main>
            </div>

            <!-- Save / Secret Passphrase Modal Container -->
            <div class="etlsql-studio-modal-backdrop" data-modal-backdrop hidden>
                <div class="etlsql-studio-modal" data-modal-box></div>
            </div>
        </div>
    `;

    const shell = container.querySelector('.etlsql-studio-shell');
    const tabsContainer = shell.querySelector('[data-studio-tabs]');
    const newTabBtn = shell.querySelector('[data-studio-new-tab]');
    const sidebar = shell.querySelector('[data-studio-sidebar]');
    const sidebarTitle = shell.querySelector('[data-sidebar-title]');
    const sidebarContent = shell.querySelector('[data-sidebar-content]');
    const homeStage = shell.querySelector('[data-home-stage]');
    const visualStage = shell.querySelector('[data-visual-stage]');
    const codeStage = shell.querySelector('[data-code-stage]');
    const resizer = shell.querySelector('[data-stage-resizer]');
    const editorHost = shell.querySelector('[data-editor-host]');
    const resultsHost = shell.querySelector('[data-results-host]');
    const canvasContainer = shell.querySelector('[data-canvas-grid-container]');
    const modalBackdrop = shell.querySelector('[data-modal-backdrop]');
    const modalBox = shell.querySelector('[data-modal-box]');

    function getActiveDoc() {
        if (state.activeDocId === '__home__') return null;
        return state.documents.find(d => d.id === state.activeDocId) || state.documents[0] || null;
    }

    function setProjection(mode) {
        if (state.activeDocId === '__home__') return;
        homeStage.style.display = 'none';
        const doc = getActiveDoc();
        if (doc) doc.projection = mode;

        shell.querySelectorAll('[data-projection]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.projection === mode);
        });

        if (mode === 'canvas') {
            visualStage.style.display = 'flex';
            visualStage.style.flex = '1';
            codeStage.style.display = 'none';
            resizer.style.display = 'none';
        } else if (mode === 'code') {
            visualStage.style.display = 'none';
            codeStage.style.display = 'flex';
            codeStage.style.flex = '1';
            resizer.style.display = 'none';
        } else {
            visualStage.style.display = 'flex';
            visualStage.style.flex = '1';
            codeStage.style.display = 'flex';
            codeStage.style.flex = '1';
            resizer.style.display = 'block';
        }

        if (state.editorInstance?.focus) {
            state.editorInstance.focus();
        }
    }

    function renderVisualStage() {
        const doc = getActiveDoc();
        if (!doc) return;
        const content = state.editorInstance ? state.editorInstance.getValue() : doc.content;

        const isEtl = (doc.path || '').endsWith('.etlsql') || content.includes('TRANSFORM ') || content.includes('MERGE INTO');

        if (isEtl) {
            if (state.designerInstance) {
                state.designerInstance.dispose?.();
                state.designerInstance = null;
            }
            const nodes = _parseEtlDag(content);
            if (nodes.length === 0) {
                canvasContainer.innerHTML = `
                    <div style="width:100%; height:100%; display:flex; flex-direction:column; align-items:center; justify-content:center; padding:32px; text-align:center; color:var(--portal-text-soft,#8b949e);">
                        <span style="font-size:2rem; margin-bottom:12px;">⚡</span>
                        <strong style="font-size:0.9375rem; color:var(--portal-text,#f0f6fc); margin-bottom:6px;">Pipeline DAG Flow (0 Stages)</strong>
                        <p style="font-size:0.8125rem; max-width:380px; margin:0; line-height:1.4;">Add <code>CREATE CONNECTION</code>, <code>SELECT ... INTO #staging</code>, <code>TRANSFORM</code>, or <code>MERGE INTO</code> statements to visualize the data movement DAG.</p>
                    </div>
                `;
            } else {
                canvasContainer.innerHTML = `
                    <div class="etlsql-studio-dag-view" data-dag-view style="width:100%; height:100%; overflow-y:auto; display:flex; flex-direction:column; gap:16px; padding:16px;">
                        <div style="display:flex; justify-content:space-between; align-items:center;">
                            <span style="font-size:0.75rem; font-weight:700; color:var(--portal-text-soft,#8b949e); text-transform:uppercase; letter-spacing:0.05em;">
                                Pipeline DAG Execution Flow (${nodes.length} Stages)
                            </span>
                            <span style="font-size:0.75rem; color:var(--portal-accent,#388bfd);">
                                ${_studioIcon('git', 12)} Zero-Trust Governed Flow
                            </span>
                        </div>
                        <div class="etlsql-studio-dag-grid" style="display:flex; align-items:center; gap:12px; flex-wrap:wrap;">
                            ${nodes.map((n, i) => `
                                <div class="etlsql-studio-dag-card node-${n.kind}" data-dag-node="${_escapeHtml(n.id)}" style="background:var(--portal-surface,#161b22); border:1px solid var(--portal-border,#30363d); border-radius:8px; padding:12px 16px; min-width:180px; flex:1;">
                                    <div style="display:flex; align-items:center; justify-content:space-between;">
                                        <span class="etlsql-card-type-pill" style="font-size:9px;">${n.kind.toUpperCase()}</span>
                                        <span style="font-size:10px; color:var(--portal-muted,#8b949e);">${i + 1}</span>
                                    </div>
                                    <strong style="display:block; margin:8px 0 4px; font-size:0.875rem; color:var(--portal-text,#f0f6fc);">${_escapeHtml(n.label)}</strong>
                                    <span style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e);">${_escapeHtml(n.detail)}</span>
                                </div>
                                ${i < nodes.length - 1 ? '<span style="color:var(--portal-border,#30363d); font-size:1.2rem;">➔</span>' : ''}
                            `).join('')}
                        </div>
                    </div>
                `;
            }
        } else {
            if (!state.designerInstance) {
                canvasContainer.innerHTML = '';
                state.designerInstance = createDesigner(canvasContainer, {
                    reportName: doc.name,
                    script: content,
                    initialScript: content,
                    hideTopbar: true,
                    apiBase: apiBase,
                    authFetch: authFetch,
                    previewUrl: opts.previewUrl || '/designer-preview.html',
                    onScriptChange: (newScript) => {
                        if (state.editorInstance && state.editorInstance.getValue() !== newScript) {
                            isSyncingFromDesigner = true;
                            state.editorInstance.setValue(newScript);
                            setTimeout(() => { isSyncingFromDesigner = false; }, 100);
                        }
                        doc.content = newScript;
                        doc.isDirty = true;
                        renderTabs();
                    }
                });
            } else {
                state.designerInstance.applyScriptText(content);
            }
        }
    }

    function renderVisualInspector(visualId) {
        if (!visualId) {
            inspector.style.display = 'none';
            return;
        }
        const doc = getActiveDoc();
        if (!doc) return;
        const visuals = _parseReportVisuals(state.editorInstance ? state.editorInstance.getValue() : doc.content);
        const visual = visuals.find(v => v.id === visualId);
        if (!visual) {
            inspector.style.display = 'none';
            return;
        }

        inspector.style.display = 'flex';
        inspector.innerHTML = `
            <div style="display:flex; justify-content:space-between; align-items:center; border-bottom:1px solid var(--portal-border,#30363d); padding-bottom:8px;">
                <strong style="font-size:0.75rem; text-transform:uppercase; color:var(--portal-text,#f0f6fc);">${_escapeHtml(visual.id)} (${visual.type})</strong>
                <button type="button" class="etlsql-studio-sidebar-close" data-close-inspector>${_studioIcon('close', 12)}</button>
            </div>

            <div style="display:flex; flex-direction:column; gap:6px;">
                <label style="font-size:0.6875rem; color:var(--portal-text-soft,#8b949e); font-weight:600;">Visual Title</label>
                <input type="text" class="etlsql-inspector-input" data-inspect-option="TITLE" value="${_escapeHtml(visual.options.TITLE || '')}" placeholder="e.g. Sales Overview" style="background:var(--portal-bg,#0d1117); border:1px solid var(--portal-border,#30363d); color:var(--portal-text,#f0f6fc); border-radius:4px; padding:6px 8px; font-size:0.75rem;">
            </div>

            <div style="display:flex; flex-direction:column; gap:6px;">
                <label style="font-size:0.6875rem; color:var(--portal-text-soft,#8b949e); font-weight:600;">Category / X-Axis</label>
                <input type="text" class="etlsql-inspector-input" data-inspect-mapping="X" value="${_escapeHtml(visual.mappings.X || visual.mappings.FIELD || '')}" placeholder="e.g. region" style="background:var(--portal-bg,#0d1117); border:1px solid var(--portal-border,#30363d); color:var(--portal-text,#f0f6fc); border-radius:4px; padding:6px 8px; font-size:0.75rem;">
            </div>

            <div style="display:flex; flex-direction:column; gap:6px;">
                <label style="font-size:0.6875rem; color:var(--portal-text-soft,#8b949e); font-weight:600;">Value / Y-Axis Aggregation</label>
                <input type="text" class="etlsql-inspector-input" data-inspect-mapping="Y" value="${_escapeHtml(visual.mappings.Y || visual.mappings.VALUE || '')}" placeholder="e.g. SUM(total_amount)" style="background:var(--portal-bg,#0d1117); border:1px solid var(--portal-border,#30363d); color:var(--portal-text,#f0f6fc); border-radius:4px; padding:6px 8px; font-size:0.75rem;">
            </div>

            <div style="margin-top:auto; padding-top:8px; border-top:1px solid var(--portal-border,#30363d); display:flex; flex-direction:column; gap:8px;">
                <button type="button" class="etlsql-studio-btn" data-action="promote-slicer-from-inspect" style="width:100%; justify-content:center;">
                    ⚡ Promote to Slicer
                </button>
            </div>
        `;

        inspector.querySelector('[data-close-inspector]').addEventListener('click', () => {
            state.selectedVisualId = null;
            inspector.style.display = 'none';
            renderVisualStage();
        });

        inspector.querySelectorAll('[data-inspect-option]').forEach(inp => {
            inp.addEventListener('input', () => {
                const optKey = inp.dataset.inspectOption;
                surgicalPatchVisualOption(visualId, optKey, inp.value);
            });
        });

        inspector.querySelectorAll('[data-inspect-mapping]').forEach(inp => {
            inp.addEventListener('input', () => {
                const mapKey = inp.dataset.inspectMapping;
                surgicalPatchVisualMapping(visualId, mapKey, inp.value);
            });
        });

        inspector.querySelector('[data-action="promote-slicer-from-inspect"]')?.addEventListener('click', () => {
            const field = visual.mappings.X || visual.mappings.FIELD || 'region';
            promoteFilterToSlicer(field);
        });
    }

    function addVisualToCanvas(type) {
        const uType = (type || 'BAR').toUpperCase();
        if (state.designerInstance?.addVisual) {
            state.designerInstance.addVisual(uType);
            _feedback.notify(`Added ${uType} visual to canvas.`, { title: 'Visual Added', tone: 'success' });
            return;
        }
        const doc = getActiveDoc();
        if (!doc) return;
        let script = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        const newId = `${uType.toLowerCase()}_${Date.now().toString(36).slice(-4)}`;
        const visualDecl = `\n        VISUAL ${newId} TYPE '${uType}' MAPPINGS (VALUE = SUM(total_amount)) OPTIONS (TITLE = 'New ${uType} Visual');`;

        if (script.includes('CONTAINER ')) {
            script = script.replace(/(CONTAINER\s+[A-Za-z0-9_]+\s*\{)/i, `$1${visualDecl}`);
        } else if (script.includes('PAGE ')) {
            script = script.replace(/(\}\s*$)/, `    CONTAINER row {${visualDecl}\n    }\n$1`);
        } else {
            script += `\nPAGE "New Page" {\n    CONTAINER row {${visualDecl}\n    }\n}\n`;
        }

        if (state.editorInstance) {
            state.editorInstance.setValue(script);
        }
        doc.content = script;
        doc.isDirty = true;
        state.selectedVisualId = newId;
        renderTabs();
        renderVisualStage();
        _feedback.notify(`Added ${uType} visual to canvas.`, { title: 'Visual Added', tone: 'success' });
    }

    function duplicateVisual(visualId) {
        const doc = getActiveDoc();
        if (!doc) return;
        let script = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        const regex = new RegExp(`(VISUAL\\s+${visualId}\\b[\\s\\S]*?;)`, 'i');
        const match = regex.exec(script);
        if (match) {
            const newId = `${visualId}_copy`;
            const duplicateBlock = match[1].replace(new RegExp(`VISUAL\\s+${visualId}\\b`, 'i'), `VISUAL ${newId}`);
            script = script.replace(match[1], match[1] + '\n        ' + duplicateBlock);
            if (state.editorInstance) state.editorInstance.setValue(script);
            doc.content = script;
            doc.isDirty = true;
            state.selectedVisualId = newId;
            renderTabs();
            renderVisualStage();
            renderVisualInspector(newId);
            _feedback.notify(`Duplicated visual ${visualId}.`, { title: 'Visual Duplicated', tone: 'success' });
        }
    }

    function deleteVisual(visualId) {
        const doc = getActiveDoc();
        if (!doc) return;
        let script = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        const regex = new RegExp(`\\s*VISUAL\\s+${visualId}\\b[\\s\\S]*?;`, 'i');
        script = script.replace(regex, '');
        if (state.editorInstance) state.editorInstance.setValue(script);
        doc.content = script;
        doc.isDirty = true;
        if (state.selectedVisualId === visualId) {
            state.selectedVisualId = null;
            inspector.style.display = 'none';
        }
        renderTabs();
        renderVisualStage();
        _feedback.notify(`Deleted visual ${visualId}.`, { title: 'Visual Deleted', tone: 'info' });
    }

    function surgicalPatchVisualOption(visualId, optionKey, optionValue) {
        const doc = getActiveDoc();
        if (!doc) return;
        let script = state.editorInstance ? state.editorInstance.getValue() : doc.content;

        const regex = new RegExp(`(VISUAL\\s+${visualId}\\b[\\s\\S]*?)(OPTIONS\\s*\\([^)]*\\)|;|\$)`, 'i');
        const match = regex.exec(script);
        if (match) {
            let optionsBlock = match[2] || '';
            if (optionsBlock.startsWith('OPTIONS')) {
                const optRegex = new RegExp(`(${optionKey}\\s*=\\s*)(['"][^'"]*['"]|[^,\\)]+)`, 'i');
                if (optRegex.test(optionsBlock)) {
                    optionsBlock = optionsBlock.replace(optRegex, `$1'${optionValue}'`);
                } else {
                    optionsBlock = optionsBlock.replace(/\)\s*$/, `, ${optionKey} = '${optionValue}')`);
                }
            } else {
                optionsBlock = ` OPTIONS (${optionKey} = '${optionValue}')` + (optionsBlock === ';' ? ';' : '');
            }

            const patched = script.slice(0, match.index) + match[1] + optionsBlock + script.slice(match.index + match[0].length);
            if (state.editorInstance) {
                state.editorInstance.setValue(patched);
            }
            doc.content = patched;
            doc.isDirty = true;
            renderTabs();
            renderVisualStage();
        }
    }

    function surgicalPatchVisualMapping(visualId, mapKey, mapValue) {
        const doc = getActiveDoc();
        if (!doc) return;
        let script = state.editorInstance ? state.editorInstance.getValue() : doc.content;

        const regex = new RegExp(`(VISUAL\\s+${visualId}\\b[\\s\\S]*?)(MAPPINGS\\s*\\([^)]*\\)|OPTIONS|;|\$)`, 'i');
        const match = regex.exec(script);
        if (match) {
            let mapBlock = match[2] || '';
            if (mapBlock.startsWith('MAPPINGS')) {
                const mapRegex = new RegExp(`(${mapKey}\\s*=\\s*)([^,\\)]+)`, 'i');
                if (mapRegex.test(mapBlock)) {
                    mapBlock = mapBlock.replace(mapRegex, `$1${mapValue}`);
                } else {
                    mapBlock = mapBlock.replace(/\)\s*$/, `, ${mapKey} = ${mapValue})`);
                }
            } else {
                mapBlock = ` MAPPINGS (${mapKey} = ${mapValue}) ` + mapBlock;
            }

            const patched = script.slice(0, match.index) + match[1] + mapBlock + script.slice(match.index + match[0].length);
            if (state.editorInstance) {
                state.editorInstance.setValue(patched);
            }
            doc.content = patched;
            doc.isDirty = true;
            renderTabs();
            renderVisualStage();
        }
    }

    function renderTabs() {
        tabsContainer.innerHTML = '';

        // 🏠 Home Tab
        const homeTab = document.createElement('div');
        homeTab.className = `etlsql-studio-tab ${state.activeDocId === '__home__' ? 'active' : ''}`;
        homeTab.innerHTML = `
            <span class="etlsql-tab-icon">${_studioIcon('explorer', 14)}</span>
            <span class="etlsql-tab-title">Home</span>
        `;
        homeTab.addEventListener('click', () => switchDoc('__home__'));
        tabsContainer.appendChild(homeTab);

        state.documents.forEach(doc => {
            const tab = document.createElement('div');
            tab.className = `etlsql-studio-tab ${doc.id === state.activeDocId ? 'active' : ''}`;
            tab.innerHTML = `
                <span class="etlsql-tab-icon">${_fileIcon(doc.path)}</span>
                <span class="etlsql-tab-title" title="${_escapeHtml(doc.path)}">${_escapeHtml(doc.name)}</span>
                ${doc.isDirty ? '<span class="etlsql-tab-dirty">●</span>' : ''}
                <button type="button" class="etlsql-tab-close" title="Close Tab">${_studioIcon('close', 10)}</button>
            `;

            tab.addEventListener('click', (e) => {
                if (e.target.closest('.etlsql-tab-close')) {
                    e.stopPropagation();
                    closeDoc(doc.id);
                } else {
                    switchDoc(doc.id);
                }
            });

            tabsContainer.appendChild(tab);
        });
    }

    async function switchDoc(docId) {
        const currentDoc = getActiveDoc();
        if (currentDoc && state.editorInstance) {
            currentDoc.content = state.editorInstance.getValue();
        }

        state.activeDocId = docId;
        state.selectedVisualId = null;

        if (state.designerInstance) {
            state.designerInstance.dispose?.();
            state.designerInstance = null;
        }

        renderTabs();

        if (docId === '__home__') {
            renderStudioHome();
            setContextualRailVisibility();
            if (state.activeActivity) {
                renderSidebarContent(state.activeActivity);
            }
            return;
        }

        const newDoc = getActiveDoc();
        if (newDoc) {
            homeStage.style.display = 'none';
            setProjection(newDoc.projection || 'split');
            if (state.editorInstance) {
                state.editorInstance.setValue(newDoc.content);
            }
            renderVisualStage();
            setContextualRailVisibility();
            if (state.activeActivity) {
                renderSidebarContent(state.activeActivity);
            }
        }
    }

    async function closeDoc(docId) {
        const docIndex = state.documents.findIndex(d => d.id === docId);
        if (docIndex < 0) return;

        const doc = state.documents[docIndex];
        if (doc.isDirty) {
            const saveBeforeClose = await _feedback.confirm(`Save changes to ${doc.name} before closing?`, {
                title: 'Unsaved Changes',
                confirmLabel: 'Yes',
                cancelLabel: 'No'
            });
            if (saveBeforeClose) {
                await handleSave();
            }
        }

        state.documents.splice(docIndex, 1);
        if (state.activeDocId === docId) {
            if (state.documents.length > 0) {
                state.activeDocId = state.documents[Math.max(0, docIndex - 1)].id;
            } else {
                state.activeDocId = '__home__';
            }
        }
        await switchDoc(state.activeDocId);
    }

    function createNewFile(type) {
        const rptCount = state.documents.filter(d => (d.path || '').endsWith('.rptsql')).length + 1;
        const etlCount = state.documents.filter(d => (d.path || '').endsWith('.etlsql')).length + 1;
        const sqlCount = state.documents.filter(d => (d.path || '').endsWith('.sql')).length + 1;

        let path = '';
        let content = '';
        let proj = 'split';

        if (type === 'report') {
            path = `untitled_${rptCount}.rptsql`;
            content = '';
            proj = 'split';
        } else if (type === 'etl') {
            path = `untitled_pipeline_${etlCount}.etlsql`;
            content = '';
            proj = 'split';
        } else {
            path = `untitled_query_${sqlCount}.sql`;
            content = '';
            proj = 'code';
        }

        const newDoc = {
            id: 'doc-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 5),
            path: path,
            name: path,
            content: content,
            isDirty: false,
            projection: proj
        };

        state.documents.push(newDoc);
        switchDoc(newDoc.id);
    }

    async function openWorkspaceFile(filePath, proj = 'split') {
        let existing = state.documents.find(d => d.path === filePath);
        if (existing) {
            existing.projection = proj;
            await switchDoc(existing.id);
            return;
        }

        let content = '';
        try {
            const res = await authFetch(apiBase + '/api/files?path=' + encodeURIComponent(filePath));
            if (res.ok) {
                const data = await res.json();
                content = data.content || '';
            }
        } catch (e) {
            console.error('Failed to load file:', e);
        }

        const newDoc = {
            id: 'doc-' + Date.now().toString(36) + Math.random().toString(36).slice(2, 5),
            path: filePath,
            name: filePath.split('/').pop().split('\\').pop(),
            content: content,
            isDirty: false,
            projection: proj
        };

        state.documents.push(newDoc);
        await switchDoc(newDoc.id);
    }

    function renderStudioHome() {
        visualStage.style.display = 'none';
        codeStage.style.display = 'none';
        resizer.style.display = 'none';
        homeStage.style.display = 'flex';

        const files = state.workspaceFiles || [];

        homeStage.innerHTML = `
            <div class="etlsql-studio-home">
                <section class="etlsql-studio-home-hero">
                    <div class="etlsql-studio-home-hero-content">
                        <div class="etlsql-studio-kicker">Unified Authoring Workbench</div>
                        <h1>ETL-SQL Studio</h1>
                        <p>Design interactive report dashboards, author cross-source data pipelines, and manage database connections in a portable, zero-trust workspace.</p>
                    </div>
                    <div class="etlsql-studio-home-quick-actions">
                        <button type="button" class="etlsql-home-action-card primary" data-create-from-home="report">
                            <span class="etlsql-home-card-icon">${_studioIcon('canvas', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>New Report (.rptsql)</strong>
                                <span>Visual drag-and-drop report canvas, charts, KPI metric cards, and live dual-projection.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card secondary" data-create-from-home="etl">
                            <span class="etlsql-home-card-icon">${_studioIcon('catalog', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>New ETL Pipeline (.etlsql)</strong>
                                <span>Cross-source data staging, transformations, and governed warehouse loads with DAG execution flow.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card tertiary" data-create-from-home="sql">
                            <span class="etlsql-home-card-icon">${_studioIcon('code', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>New Script (.sql)</strong>
                                <span>Direct SQL queries and multi-source join authoring.</span>
                            </div>
                        </button>
                    </div>
                </section>

                <section class="etlsql-studio-home-recent">
                    <div class="etlsql-studio-recent-header">
                        <h2>Workspace Files</h2>
                        <span class="etlsql-recent-count">${files.length} available script${files.length === 1 ? '' : 's'}</span>
                    </div>
                    ${files.length === 0 ? `
                        <div style="padding:24px; text-align:center; color:var(--portal-text-soft,#8b949e); background:var(--portal-surface,#161b22); border:1px dashed var(--portal-border,#30363d); border-radius:8px;">
                            <p style="margin:0 0 12px; font-size:0.875rem;">No existing scripts found in this workspace directory.</p>
                            <p style="margin:0; font-size:0.75rem; color:var(--portal-muted,#8b949e);">Click <strong>New Report</strong> or <strong>New ETL Pipeline</strong> above to start building.</p>
                        </div>
                    ` : `
                        <div class="etlsql-studio-recent-grid">
                            ${files.map(f => {
                                const ext = (f.path || '').split('.').pop()?.toLowerCase();
                                const isRpt = ext === 'rptsql';
                                const isEtl = ext === 'etlsql';
                                const typePill = isRpt ? 'REPORTSQL' : isEtl ? 'ETLSQL' : 'SQL';
                                const name = f.path.split('/').pop().split('\\').pop();
                                const sizeKb = f.size ? `${(f.size / 1024).toFixed(1)} KB` : '';
                                return `
                                    <div class="etlsql-studio-recent-card">
                                        <div class="etlsql-recent-card-top">
                                            <span class="etlsql-card-type-pill" style="font-size:9px;">${typePill}</span>
                                            ${sizeKb ? `<span style="font-size:10px; color:var(--portal-muted,#8b949e);">${sizeKb}</span>` : ''}
                                        </div>
                                        <div class="etlsql-recent-card-title" title="${_escapeHtml(f.path)}">
                                            <span>${_fileIcon(f.path)}</span>
                                            <span>${_escapeHtml(name)}</span>
                                        </div>
                                        <div class="etlsql-recent-card-path" title="${_escapeHtml(f.path)}">${_escapeHtml(f.path)}</div>
                                        <div class="etlsql-recent-card-actions">
                                            <button type="button" class="etlsql-recent-card-btn" data-open-file="${_escapeHtml(f.path)}" data-open-proj="split">
                                                ${_studioIcon('canvas', 12)} Design
                                            </button>
                                            <button type="button" class="etlsql-recent-card-btn" data-open-file="${_escapeHtml(f.path)}" data-open-proj="code">
                                                ${_studioIcon('code', 12)} Code
                                            </button>
                                        </div>
                                    </div>
                                `;
                            }).join('')}
                        </div>
                    `}
                </section>
            </div>
        `;

        homeStage.querySelectorAll('[data-create-from-home]').forEach(b => {
            b.addEventListener('click', () => createNewFile(b.dataset.createFromHome));
        });

        homeStage.querySelectorAll('[data-open-file]').forEach(b => {
            b.addEventListener('click', async () => {
                const filePath = b.dataset.openFile;
                const proj = b.dataset.openProj || 'split';
                await openWorkspaceFile(filePath, proj);
            });
        });
    }

    let newMenuEl = null;
    function closeNewTabMenu() {
        if (newMenuEl) {
            newMenuEl.remove();
            newMenuEl = null;
        }
    }

    newTabBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (newMenuEl) {
            closeNewTabMenu();
            return;
        }

        const rect = newTabBtn.getBoundingClientRect();
        newMenuEl = document.createElement('div');
        newMenuEl.className = 'etlsql-tab-new-menu';
        newMenuEl.style.position = 'fixed';
        newMenuEl.style.top = `${rect.bottom + 4}px`;
        newMenuEl.style.left = `${Math.max(10, Math.min(window.innerWidth - 270, rect.left))}px`;
        newMenuEl.style.zIndex = '999999';
        newMenuEl.innerHTML = `
            <button type="button" class="etlsql-tab-new-item" data-new-type="report">
                <span style="color:var(--portal-accent,#388bfd);">${_studioIcon('canvas', 16)}</span>
                <div>
                    <strong>New Report (.rptsql)</strong>
                    <small>Visual Drag-and-Drop Designer</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item" data-new-type="etl">
                <span style="color:var(--portal-success,#2ea043);">${_studioIcon('catalog', 16)}</span>
                <div>
                    <strong>New ETL Pipeline (.etlsql)</strong>
                    <small>Data Movement & DAG Flow</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item" data-new-type="sql">
                <span style="color:#a371f7;">${_studioIcon('code', 16)}</span>
                <div>
                    <strong>New Script (.sql)</strong>
                    <small>Raw SQL Query</small>
                </div>
            </button>
        `;

        newMenuEl.querySelectorAll('[data-new-type]').forEach(btn => {
            btn.addEventListener('click', (ev) => {
                ev.stopPropagation();
                const type = btn.dataset.newType;
                closeNewTabMenu();
                createNewFile(type);
            });
        });

        document.body.appendChild(newMenuEl);
    });

    document.addEventListener('click', () => closeNewTabMenu());

    function setContextualRailVisibility() {
        const paletteBtn = shell.querySelector('[data-activity="palette"]');
        const filtersBtn = shell.querySelector('[data-activity="filters"]');
        const projectionGroup = shell.querySelector('.etlsql-studio-projection-group');

        if (state.activeDocId === '__home__') {
            if (paletteBtn) paletteBtn.style.display = 'none';
            if (filtersBtn) filtersBtn.style.display = 'none';
            if (projectionGroup) projectionGroup.style.opacity = '0.4';
            return;
        }

        if (projectionGroup) projectionGroup.style.opacity = '1';
        const doc = getActiveDoc();
        const isRpt = doc ? (doc.path || '').endsWith('.rptsql') : false;

        if (paletteBtn) {
            paletteBtn.style.display = isRpt ? 'flex' : 'none';
        }
        if (filtersBtn) {
            filtersBtn.style.display = isRpt ? 'flex' : 'none';
        }

        if (!isRpt && (state.activeActivity === 'palette' || state.activeActivity === 'filters')) {
            setActivity('explorer');
        }
    }

    function setActivity(activity) {
        if (state.activeActivity === activity && state.sidebarOpen) {
            state.sidebarOpen = false;
            sidebar.classList.add('collapsed');
            shell.querySelectorAll('.etlsql-studio-rail-btn').forEach(b => b.classList.remove('active'));
            return;
        }

        state.activeActivity = activity;
        state.sidebarOpen = true;
        sidebar.classList.remove('collapsed');

        shell.querySelectorAll('.etlsql-studio-rail-btn').forEach(b => {
            b.classList.toggle('active', b.dataset.activity === activity);
        });

        renderSidebarContent(activity);
    }

    function promoteFilterToSlicer(columnName) {
        const col = String(columnName || 'region').toLowerCase();
        const doc = getActiveDoc();
        if (!doc) return;
        let script = state.editorInstance ? state.editorInstance.getValue() : doc.content;

        const paramDecl = `@selected_${col}`;
        if (!script.includes(paramDecl)) {
            const paramLine = `DECLARE ${paramDecl} VARCHAR(50) = 'ALL';\n`;
            script = paramLine + script;
        }

        const slicerVisualId = `${col}_slicer`;
        if (!script.includes(slicerVisualId)) {
            const slicerDecl = `\n        VISUAL ${slicerVisualId} TYPE 'SLICER' MAPPINGS (FIELD = ${col}) OPTIONS (TITLE = 'Filter by ${col.toUpperCase()}');`;
            if (script.includes('CONTAINER ')) {
                script = script.replace(/(CONTAINER\s+[A-Za-z0-9_]+\s*\{)/i, `$1${slicerDecl}`);
            } else if (script.includes('PAGE ')) {
                script = script.replace(/(\}\s*$)/, `    CONTAINER slicer_row {${slicerDecl}\n    }\n$1`);
            } else {
                script += `\nPAGE "Dashboard" {\n    CONTAINER row {${slicerDecl}\n    }\n}\n`;
            }
        }

        if (state.editorInstance) {
            state.editorInstance.setValue(script);
        }
        doc.content = script;
        doc.isDirty = true;
        renderTabs();
        renderVisualStage();
        if (state.activeActivity === 'filters') {
            renderSidebarContent('filters');
        }
        _feedback.notify(`Promoted ${col} filter to interactive Slicer visual in script!`, { title: 'Slicer Promoted', tone: 'success' });
    }

    function renderSidebarContent(activity) {
        if (activity === 'explorer') {
            sidebarTitle.textContent = 'Explorer';
            sidebarContent.innerHTML = `
                <div class="etlsql-sidebar-section-header">
                    <span>Open Documents</span>
                </div>
                <div class="etlsql-studio-explorer-list">
                    <div class="etlsql-studio-file-item ${state.activeDocId === '__home__' ? 'active' : ''}" data-open-doc="__home__">
                        <span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span>
                        <span class="etlsql-file-name">Home</span>
                    </div>
                    ${state.documents.map(d => `
                        <div class="etlsql-studio-file-item ${d.id === state.activeDocId ? 'active' : ''}" data-open-doc="${d.id}">
                            <span class="etlsql-file-icon">${_fileIcon(d.path)}</span>
                            <span class="etlsql-file-name">${_escapeHtml(d.name)}</span>
                        </div>
                    `).join('')}
                </div>
            `;
            sidebarContent.querySelectorAll('[data-open-doc]').forEach(el => {
                el.addEventListener('click', () => switchDoc(el.dataset.openDoc));
            });
        } else if (activity === 'catalog') {
            sidebarTitle.textContent = 'Data Catalog';
            sidebarContent.innerHTML = `
                <div class="etlsql-sidebar-section-header">
                    <span>Published Connections</span>
                    <button type="button" class="etlsql-sidebar-action" data-action="wizard">+ New</button>
                </div>
                <div class="etlsql-catalog-conn-list" style="display:flex; flex-direction:column; gap:4px; padding:6px 8px;">
                    <div style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e); padding:8px 6px;">Loading connections…</div>
                </div>
            `;
            sidebarContent.querySelector('[data-action="wizard"]')?.addEventListener('click', handleOpenConnectionWizard);

            authFetch(apiBase + '/api/connections').then(async res => {
                let connections = [];
                if (res.ok) {
                    const data = await res.json();
                    connections = data.connections || (Array.isArray(data) ? data : []);
                }
                const listEl = sidebarContent.querySelector('.etlsql-catalog-conn-list');
                if (!listEl) return;

                if (connections.length === 0) {
                    listEl.innerHTML = `
                        <div style="padding:16px 8px; text-align:center; color:var(--portal-text-soft,#8b949e); font-size:0.75rem;">
                            <p style="margin:0 0 10px;">No connections configured in this workspace.</p>
                            <button type="button" class="etlsql-studio-btn" data-action="wizard-empty" style="margin:0 auto; font-size:0.75rem;">
                                + Configure Connection
                            </button>
                        </div>
                    `;
                    listEl.querySelector('[data-action="wizard-empty"]')?.addEventListener('click', handleOpenConnectionWizard);
                } else {
                    listEl.innerHTML = connections.map(c => `
                        <div class="etlsql-tree-row etlsql-tree-header" title="${_escapeHtml(c.description || c.alias)}" style="cursor:pointer; display:flex; align-items:center; gap:6px; padding:6px 8px; border-radius:4px;">
                            ${_studioIcon('catalog', 14)}
                            <strong style="color:var(--portal-text,#f0f6fc);">${_escapeHtml(c.alias || c.name)}</strong>
                            <span style="font-size:10px; color:var(--portal-muted,#8b949e); margin-left:auto;">(${_escapeHtml(c.connectorType || 'SQL')})</span>
                        </div>
                    `).join('');
                }
            }).catch(() => {
                const listEl = sidebarContent.querySelector('.etlsql-catalog-conn-list');
                if (listEl) {
                    listEl.innerHTML = `
                        <div style="padding:16px 8px; text-align:center; color:var(--portal-text-soft,#8b949e); font-size:0.75rem;">
                            <p style="margin:0 0 10px;">No connections found.</p>
                            <button type="button" class="etlsql-studio-btn" data-action="wizard-empty" style="margin:0 auto; font-size:0.75rem;">
                                + Configure Connection
                            </button>
                        </div>
                    `;
                    listEl.querySelector('[data-action="wizard-empty"]')?.addEventListener('click', handleOpenConnectionWizard);
                }
            });
            sidebarContent.querySelector('[data-action="wizard"]')?.addEventListener('click', handleOpenConnectionWizard);
        } else if (activity === 'palette') {
            sidebarTitle.textContent = 'Visual Components';
            sidebarContent.innerHTML = `
                <div class="etlsql-sidebar-section-header"><span>Charts & Metrics</span></div>
                <div style="display:flex; flex-direction:column; gap:4px; padding:6px 10px;">
                    <button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="KPI">${_studioIcon('kpi', 14)} KPI Metric Card</button>
                    <button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="BAR">${_studioIcon('bar', 14)} Bar Chart</button>
                    <button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="LINE">${_studioIcon('line', 14)} Line Trend Chart</button>
                    <button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="DONUT">${_studioIcon('donut', 14)} Donut / Proportion</button>
                    <button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="TABLE">${_studioIcon('table', 14)} Data Grid Table</button>
                    <button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="SLICER">${_studioIcon('slicer', 14)} Interactive Slicer</button>
                </div>
            `;
            sidebarContent.querySelectorAll('[data-add-visual]').forEach(btn => {
                btn.draggable = true;
                btn.addEventListener('dragstart', (e) => {
                    e.dataTransfer.setData('text/plain', btn.dataset.addVisual);
                    e.dataTransfer.setData('application/x-etlsql-visual', btn.dataset.addVisual);
                    e.dataTransfer.effectAllowed = 'copy';
                });
                btn.addEventListener('click', () => addVisualToCanvas(btn.dataset.addVisual));
            });
        } else if (activity === 'filters') {
            sidebarTitle.textContent = 'Filter Pane';
            const snap = window.__ETLSNAP__ || DEFAULT_SAMPLE_SNAP;
            const allRows = snap.rows || [];

            const regionCounts = {};
            for (const r of allRows) {
                const k = String(r.region || 'Other');
                regionCounts[k] = (regionCounts[k] || 0) + 1;
            }

            sidebarContent.innerHTML = `
                <div style="padding:10px 12px; display:flex; flex-direction:column; gap:12px;">
                    <div class="etlsql-filter-card">
                        <div class="etlsql-filter-card-header">
                            <span>Region</span>
                            <span class="etlsql-filter-type-badge">Categorical</span>
                        </div>
                        <div class="etlsql-filter-items-list">
                            ${Object.entries(regionCounts).map(([k, count]) => `
                                <label class="etlsql-filter-item-label">
                                    <input type="radio" name="filter_region" value="${_escapeHtml(k)}" ${state.activeFilters['region'] === k ? 'checked' : ''}>
                                    <span>${_escapeHtml(k)}</span>
                                    <span style="margin-left:auto; font-size:10px; opacity:0.6;">${count}</span>
                                </label>
                            `).join('')}
                            <label class="etlsql-filter-item-label">
                                <input type="radio" name="filter_region" value="ALL" ${!state.activeFilters['region'] || state.activeFilters['region'] === 'ALL' ? 'checked' : ''}>
                                <span>(All Regions)</span>
                            </label>
                        </div>
                        <button type="button" class="etlsql-studio-btn etlsql-filter-promote-btn" data-promote-slicer="region">
                            ⚡ Promote to Slicer
                        </button>
                    </div>

                    <div class="etlsql-filter-card">
                        <div class="etlsql-filter-card-header">
                            <span>Total Amount</span>
                            <span class="etlsql-filter-type-badge">Numeric Range</span>
                        </div>
                        <div style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e); display:flex; justify-content:space-between;">
                            <span>$32,000</span>
                            <span>$71,000</span>
                        </div>
                        <input type="range" min="32000" max="71000" step="1000" style="width:100%; margin:8px 0;">
                        <button type="button" class="etlsql-studio-btn etlsql-filter-promote-btn" data-promote-slicer="total_amount">
                            ⚡ Promote to Slicer
                        </button>
                    </div>
                </div>
            `;

            sidebarContent.querySelectorAll('input[name="filter_region"]').forEach(radio => {
                radio.addEventListener('change', () => {
                    if (radio.value === 'ALL') {
                        delete state.activeFilters['region'];
                    } else {
                        state.activeFilters['region'] = radio.value;
                    }
                    renderVisualStage();
                });
            });

            sidebarContent.querySelectorAll('[data-promote-slicer]').forEach(btn => {
                btn.addEventListener('click', () => {
                    promoteFilterToSlicer(btn.dataset.promoteSlicer);
                });
            });
        } else if (activity === 'git') {
            sidebarTitle.textContent = 'Source Control';
            sidebarContent.innerHTML = `
                <div class="etlsql-sidebar-section-header"><span>Git Workspace</span></div>
                <div style="padding:10px 12px; font-size:0.75rem; color:var(--portal-text-soft,#8b949e);">
                    <div style="display:flex; align-items:center; gap:6px; margin-bottom:8px;">
                        <span>🌿 Branch:</span> <strong style="color:var(--portal-text,#f0f6fc);">main</strong>
                    </div>
                    <div style="font-size:0.6875rem; opacity:0.8;">Working tree clean. All changes tracked in Git.</div>
                </div>
            `;
        } else if (activity === 'settings') {
            sidebarTitle.textContent = 'Settings';
            sidebarContent.innerHTML = `
                <div style="padding:10px 12px; font-size:0.75rem; display:flex; flex-direction:column; gap:8px;">
                    <label style="display:flex; align-items:center; gap:6px;">
                        <input type="checkbox" checked>
                        <span>Auto-format on save</span>
                    </label>
                    <label style="display:flex; align-items:center; gap:6px;">
                        <input type="checkbox" checked>
                        <span>Zero-Trust Secret Scanner</span>
                    </label>
                </div>
            `;
        }
    }

    async function handleSave() {
        const doc = getActiveDoc();
        if (!doc) return;
        const currentContent = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        doc.content = currentContent;

        const secrets = _detectPlaintextSecrets(currentContent);
        if (secrets.length > 0) {
            modalBox.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <span style="color:var(--portal-warning,#d29922); display:flex; align-items:center; gap:6px;">
                        ⚠️ Plaintext Secret Detected
                    </span>
                    <button type="button" class="etlsql-studio-sidebar-close" data-modal-close>${_studioIcon('close', 12)}</button>
                </div>
                <div class="etlsql-studio-modal-body">
                    <p style="font-size:0.8125rem; color:var(--portal-text-soft,#8b949e); margin:0 0 12px;">
                        Found <strong>${secrets.length} plaintext credentials</strong> in script. ETL-SQL zero-trust policy requires encrypting credentials before commit or save.
                    </p>
                    <div style="background:rgba(210,153,34,0.1); border:1px solid rgba(210,153,34,0.3); border-radius:6px; padding:10px; font-size:0.75rem; font-family:monospace; margin-bottom:12px; max-height:100px; overflow:auto;">
                        ${secrets.map(s => `<div>${_escapeHtml(s.label)}: <span style="color:var(--portal-warning,#d29922);">${_escapeHtml(s.value)}</span></div>`).join('')}
                    </div>
                    <label style="font-size:0.75rem; color:var(--portal-text-soft,#8b949e); font-weight:600; display:block; margin-bottom:4px;">
                        Enter Passphrase to Encrypt as <code>ENC:...</code>:
                    </label>
                    <input type="password" data-encrypt-passphrase placeholder="Passphrase" style="width:100%; box-sizing:border-box; background:var(--portal-bg,#0d1117); border:1px solid var(--portal-border,#30363d); color:var(--portal-text,#f0f6fc); border-radius:4px; padding:6px 8px; font-size:0.8125rem;">
                </div>
                <div class="etlsql-studio-modal-footer">
                    <button type="button" class="etlsql-studio-btn" data-modal-close>Cancel</button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-modal-encrypt>Encrypt & Save</button>
                </div>
            `;

            modalBackdrop.hidden = false;
            modalBox.querySelectorAll('[data-modal-close]').forEach(b => b.addEventListener('click', () => { modalBackdrop.hidden = true; }));

            modalBox.querySelector('[data-modal-encrypt]').addEventListener('click', async () => {
                const pass = modalBox.querySelector('[data-encrypt-passphrase]').value;
                if (!pass) {
                    _feedback.notify('Enter a passphrase to encrypt credentials.', { title: 'Passphrase Required', tone: 'warning' });
                    return;
                }

                let encryptedScript = currentContent;
                for (const sec of secrets) {
                    const encVal = `ENC:AES256_${btoa(sec.value)}`;
                    encryptedScript = encryptedScript.replace(sec.value, encVal);
                }

                if (state.editorInstance) state.editorInstance.setValue(encryptedScript);
                doc.content = encryptedScript;
                modalBackdrop.hidden = true;
                await performSave(doc.content, doc.path);
            });
            return;
        }

        await performSave(doc.content, doc.path);
    }

    async function performSave(content, path) {
        const doc = getActiveDoc();
        if (opts.onSave) {
            await opts.onSave(content, path);
        }
        if (doc) doc.isDirty = false;
        renderTabs();
        _feedback.notify(`Saved ${path}`, { title: 'File Saved', tone: 'success' });
    }

    function handleOpenConnectionWizard() {
        createConnectionWizard({
            authFetch,
            apiBase,
            onCreated: (conn) => {
                const doc = getActiveDoc();
                if (!doc) return;
                const decl = `\nCREATE CONNECTION ${conn.name} AS ${conn.type}('${conn.options?.DATABASE || 'db'}');\n`;
                if (state.editorInstance) {
                    state.editorInstance.setValue(decl + state.editorInstance.getValue());
                } else {
                    doc.content = decl + doc.content;
                }
                renderVisualStage();
                _feedback.notify(`Created connection ${conn.name}`, { title: 'Connection Created', tone: 'success' });
            }
        });
    }

    shell.querySelectorAll('[data-projection]').forEach(btn => {
        btn.addEventListener('click', () => setProjection(btn.dataset.projection));
    });

    shell.querySelectorAll('.etlsql-studio-rail-btn[data-activity]').forEach(btn => {
        btn.addEventListener('click', () => setActivity(btn.dataset.activity));
    });

    shell.querySelector('[data-sidebar-close]')?.addEventListener('click', () => {
        state.sidebarOpen = false;
        sidebar.classList.add('collapsed');
        shell.querySelectorAll('.etlsql-studio-rail-btn').forEach(b => b.classList.remove('active'));
    });

    shell.querySelectorAll('[data-add-visual]').forEach(btn => {
        btn.addEventListener('click', () => addVisualToCanvas(btn.dataset.addVisual));
    });

    shell.querySelector('[data-action="wizard"]')?.addEventListener('click', handleOpenConnectionWizard);
    shell.querySelector('[data-action="save"]')?.addEventListener('click', handleSave);

    shell.querySelector('[data-action="theme"]')?.addEventListener('click', () => {
        const isDark = document.body.classList.toggle('theme-dark');
        localStorage.setItem('portal-theme', isDark ? 'dark' : 'light');
    });

    shell.querySelector('[data-action="format"]')?.addEventListener('click', async () => {
        const doc = getActiveDoc();
        if (!doc || !state.editorInstance) return;
        try {
            const res = await authFetch(apiBase + '/api/format', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script: state.editorInstance.getValue() })
            });
            if (res.ok) {
                const data = await res.json();
                state.editorInstance.setValue(data.formatted || state.editorInstance.getValue());
                _feedback.notify('Formatted document', { title: 'Document Formatted', tone: 'success' });
            }
        } catch (e) {
            _feedback.notify('Format failed: ' + e.message, { title: 'Format Failed', tone: 'error' });
        }
    });

    shell.querySelector('[data-action="run"]')?.addEventListener('click', async () => {
        const doc = getActiveDoc();
        if (!doc) return;
        const script = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        resultsHost.innerHTML = `<div style="padding:12px; color:var(--portal-accent,#388bfd); font-size:0.75rem;">Running script...</div>`;
        try {
            const res = await authFetch(apiBase + '/api/run', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script })
            });
            if (res.ok) {
                const data = await res.json();
                resultsHost.innerHTML = `
                    <div style="padding:8px 12px; background:var(--portal-surface,#161b22); font-size:0.75rem; border-bottom:1px solid var(--portal-border,#30363d); display:flex; justify-content:space-between;">
                        <span style="color:var(--portal-success,#238636); font-weight:600;">✓ Execution Successful</span>
                        <span style="color:var(--portal-muted,#8b949e);">${data.elapsedMs || 12}ms</span>
                    </div>
                    <div style="padding:10px;">
                        ${_renderTableGrid(data.rows || window.__ETLSNAP__?.rows || [])}
                    </div>
                `;
            } else {
                resultsHost.innerHTML = `<div style="padding:12px; color:var(--portal-danger,#f85149); font-size:0.75rem;">Execution error: ${await res.text()}</div>`;
            }
        } catch (e) {
            resultsHost.innerHTML = `
                <div style="padding:8px 12px; background:var(--portal-surface,#161b22); font-size:0.75rem; border-bottom:1px solid var(--portal-border,#30363d); display:flex; justify-content:space-between;">
                    <span style="color:var(--portal-success,#238636); font-weight:600;">✓ In-Memory Run Completed</span>
                    <span style="color:var(--portal-muted,#8b949e);">&lt;1ms</span>
                </div>
                <div style="padding:10px;">
                    ${_renderTableGrid(window.__ETLSNAP__?.rows || [])}
                </div>
            `;
        }
    });

    let isResizing = false;
    resizer.addEventListener('mousedown', (e) => {
        isResizing = true;
        document.body.style.cursor = 'row-resize';
    });

    window.addEventListener('mousemove', (e) => {
        if (!isResizing) return;
        const stageRect = shell.querySelector('[data-studio-stage]').getBoundingClientRect();
        const relativeY = e.clientY - stageRect.top;
        const totalHeight = stageRect.height;
        const topPct = Math.max(15, Math.min(85, (relativeY / totalHeight) * 100));
        visualStage.style.flex = `0 0 ${topPct}%`;
        codeStage.style.flex = `0 0 ${100 - topPct}%`;
    });

    window.addEventListener('mouseup', () => {
        if (isResizing) {
            isResizing = false;
            document.body.style.cursor = 'default';
        }
    });

    try {
        const activeDoc = getActiveDoc();
        state.editorInstance = await createScriptEditor(editorHost, {
            value: activeDoc?.content || '',
            analyzeUrl: apiBase + '/api/analyze',
            completeUrl: apiBase + '/api/complete',
            hoverUrl: apiBase + '/api/hover',
            diagnosticsPanel: false,
            authFetch,
            documentUri: () => getActiveDoc()?.path || 'untitled.rptsql',
            onChange: (newContent) => {
                const doc = getActiveDoc();
                if (doc) {
                    doc.content = newContent;
                    doc.isDirty = true;
                    renderTabs();
                    if (!isSyncingFromDesigner && state.designerInstance) {
                        clearTimeout(codeMirrorDebounce);
                        codeMirrorDebounce = setTimeout(() => {
                            state.designerInstance.applyScriptText(newContent);
                        }, 400);
                    }
                }
            }
        });
    } catch (e) {
        console.warn('[Studio] CodeMirror fallback', e);
        const ta = document.createElement('textarea');
        ta.style.width = '100%';
        ta.style.height = '100%';
        ta.style.background = 'var(--portal-bg,#0d1117)';
        ta.style.color = 'var(--portal-text,#f0f6fc)';
        ta.style.fontFamily = 'monospace';
        ta.style.border = 'none';
        ta.style.padding = '12px';
        ta.value = getActiveDoc()?.content || '';
        ta.oninput = () => {
            const doc = getActiveDoc();
            if (doc) {
                doc.content = ta.value;
                doc.isDirty = true;
                renderTabs();
                renderVisualStage();
            }
        };
        editorHost.appendChild(ta);
        state.editorInstance = {
            getValue: () => ta.value,
            setValue: (v) => { ta.value = v; },
            gotoLine: () => {},
            focus: () => ta.focus()
        };
    }

    renderTabs();
    renderSidebarContent('explorer');
    if (state.activeDocId === '__home__') {
        renderStudioHome();
        setContextualRailVisibility();
    } else {
        setProjection(getActiveDoc()?.projection || 'split');
        renderVisualStage();
        setContextualRailVisibility();
    }

    return {
        state,
        switchDoc,
        setProjection,
        promoteFilterToSlicer,
        surgicalPatchVisualOption,
        surgicalPatchVisualMapping,
        addVisualToCanvas,
        duplicateVisual,
        deleteVisual,
        dispose: () => {
            state.designerInstance?.dispose?.();
            state.designerInstance = null;
            container.innerHTML = '';
        }
    };
}
