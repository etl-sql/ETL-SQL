/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Canvas cards, snapshot previews, page tabs, and layout.
 */

import { VTYPES, closestElement, queryElement, queryElements } from './designer-context.js';

import type { DesignerDom, DesignerOptions, DesignerPage, DesignerRow, DesignerSnapshotPackage, DesignerState, DesignerVisual } from './designer-context.js';
import { esc } from './designer-util.js';
import { HTML_PREVIEW_BUDGETS, _copyHtmlPreviewNode, _validateHtmlPreviewCss } from './html-preview.js';
import { renderVisualSample } from './visual-preview.js';

export interface DesignerCanvasRendererContext {
    readonly canvasGrid: DesignerDom;
    readonly canvasWrap: DesignerDom;
    readonly collapsedContainers: Set<unknown>;
    readonly curPage: () => DesignerPage;
    readonly curVis: () => DesignerVisual[];
    readonly isLocked: (v: DesignerVisual | null) => boolean;
    readonly maxRow: (vs: DesignerVisual[]) => number;
    readonly opts: DesignerOptions;
    pageIdx: number;
    readonly renderProps: () => void;
    selVisualId: string | null;
    selVisualIds: Set<string>;
    readonly sidebar: DesignerDom;
    readonly state: DesignerState;
    readonly topbar: DesignerDom;
}

export function createDesignerCanvasRenderer(context: DesignerCanvasRendererContext) {
    const snapshotResizeObservers: Set<ResizeObserver> = new Set();

    let activeSnapshotFilter: string | null = null;

    const VCOLOR = Object.fromEntries(VTYPES.map(([t, c]) => [t, c]));

    const expandedDsIds = new Set();

    function disconnectSnapshotResizeObservers() {
        for (const observer of snapshotResizeObservers) {
            try { observer.disconnect(); } catch { /* Already disconnected, or its element is gone; either way nothing is left to do. */ }
        }
        snapshotResizeObservers.clear();
    }

    function tidyLayout() {
        const page = context.curPage();
        if (!page?.visuals?.length) return;

        const visuals = [...page.visuals].sort((a, b) => ((a.gridRow || 1) - (b.gridRow || 1)) || ((a.gridCol || 1) - (b.gridCol || 1)));

        for (let i = 0; i < visuals.length; i++) {
            const v = visuals[i];
            const vColStart = v.gridCol || 1;
            const vColEnd = vColStart + (v.gridColSpan || 12) - 1;

            let newRow = 1;

            for (let j = 0; j < i; j++) {
                const prev = visuals[j];
                const pColStart = prev.gridCol || 1;
                const pColEnd = pColStart + (prev.gridColSpan || 12) - 1;

                const overlapsHorizontally = (vColStart <= pColEnd) && (vColEnd >= pColStart);

                if (overlapsHorizontally) {
                    const prevBottom = (prev.gridRow || 1) + (prev.gridRowSpan || 4);
                    if (prevBottom > newRow) {
                        newRow = prevBottom;
                    }
                }
            }

            const deltaRow = newRow - (v.gridRow || 1);
            v.gridRow = newRow;

            if (v.type === 'CONTAINER' && deltaRow !== 0) {
                for (const child of page.visuals) {
                    if (child.containerId === v.id) {
                        child.gridRow = Math.max(1, (child.gridRow || 1) + deltaRow);
                    }
                }
            }
        }

        renderCanvas();
        renderTree();
        context.renderProps();
    }

    function renderPageTabs(): void {
        const strip = queryElement(context.topbar, '#dsgn-pages');
        strip.innerHTML = '';
        context.state.pages.forEach((p, i) => {
            const tab = document.createElement('button');
            tab.className = 'etlsql-designer-page-tab' + (i === context.pageIdx ? ' active' : '');
            tab.textContent = p.name || `Page ${i + 1}`;
            tab.dataset.idx = String(i);
            strip.appendChild(tab);
        });
    }

    function _renderHtmlVisualPreview(bodyEl: DesignerDom, visual: DesignerVisual, snapshotPackage: DesignerSnapshotPackage | undefined): void {
        const tmpl = visual.options?.html_template || '<article class="custom-card"><h3>{{Title}}</h3><p>{{Description}}</p></article>';
        const css = visual.options?.html_style || '';
        const mode = visual.options?.html_mode || 'SINGLE';
        const rows: DesignerRow[] = (snapshotPackage && visual.dataset
            ? snapshotPackage.datasets?.[visual.dataset]?.rows as DesignerRow[] | undefined
            : undefined) || [];

        const renderRow = (row: DesignerRow, columns: string[]): string => {
            let rowHtml = tmpl;
            columns.forEach((col, idx) => {
                const val = Array.isArray(row) ? row[idx] : row[col];
                const reg = new RegExp(`\\{\\{${col}(?:\\s+FORMAT\\s+[^}]+)?\\}\\}`, 'gi');
                rowHtml = rowHtml.replace(reg, esc(String(val ?? '')));
            });
            return rowHtml;
        };

        let sampleHtml;
        let budgetHtml;
        if (mode === 'REPEATER' && rows.length > 0) {
            const columns = snapshotPackage?.datasets?.[visual.dataset!]?.columns || [];
            sampleHtml = rows.slice(0, 5).map(row => renderRow(row, columns)).join('');
            budgetHtml = rows.map(row => renderRow(row, columns)).join('');
        } else if (rows.length > 0) {
            const columns = visual.dataset ? snapshotPackage?.datasets?.[visual.dataset]?.columns || [] : [];
            sampleHtml = renderRow(rows[0], columns);
            budgetHtml = sampleHtml;
        } else {
            // Static or placeholder preview
            sampleHtml = tmpl.replace(/\{\{#IF\s+[^}]+\}\}/gi, '')
                             .replace(/\{\{\/IF\}\}/gi, '')
                             .replace(/\{\{([@a-zA-Z0-9_]+)(?:\s+FORMAT\s+[^}]+)?\}\}/g, '$1');
            budgetHtml = sampleHtml;
        }

        const encoder = new TextEncoder();
        const authored = new DOMParser().parseFromString(tmpl, 'text/html');
        const rendered = new DOMParser().parseFromString(sampleHtml, 'text/html');
        const budgetRendered = new DOMParser().parseFromString(budgetHtml, 'text/html');
        const templateNodes = queryElements(authored.body, '*').length;
        const rowLimit = Number(visual.options?.MAX_ROWS || visual.options?.max_rows || HTML_PREVIEW_BUDGETS.rows);
        const instances = mode === 'REPEATER' ? rows.length : 1;
        const authoredOutputNodes = templateNodes * instances;
        const outputNodes = queryElements(budgetRendered.body, '*').length;
        const outputBytes = encoder.encode(budgetHtml).length;
        const renderWork = outputNodes + Math.ceil(outputBytes / 256);
        const violations = [];
        if (encoder.encode(tmpl).length > HTML_PREVIEW_BUDGETS.templateBytes) violations.push('Template byte budget exceeded.');
        if (encoder.encode(css).length > HTML_PREVIEW_BUDGETS.cssBytes) violations.push('CSS byte budget exceeded.');
        if (templateNodes > HTML_PREVIEW_BUDGETS.templateNodes) violations.push('Template node budget exceeded.');
        if (mode === 'REPEATER' && rows.length > rowLimit) violations.push('Repeater row budget exceeded.');
        if (authoredOutputNodes > HTML_PREVIEW_BUDGETS.outputNodes || outputNodes > HTML_PREVIEW_BUDGETS.outputNodes)
            violations.push('Output node budget exceeded.');
        if (outputBytes > HTML_PREVIEW_BUDGETS.outputBytes) violations.push('Output byte budget exceeded.');
        if (renderWork > HTML_PREVIEW_BUDGETS.renderWork) violations.push('Render-work budget exceeded.');
        const cssViolation = _validateHtmlPreviewCss(css);
        if (cssViolation) violations.push(cssViolation);

        const sanitized = document.createDocumentFragment();
        for (const child of rendered.body.childNodes) {
            const copied = _copyHtmlPreviewNode(child, document, violations);
            if (copied) sanitized.appendChild(copied);
        }

        const preview = document.createElement('div');
        preview.className = 'etlsql-html-visual-preview';
        preview.style.cssText = 'width:100%;height:100%;overflow:auto;padding:8px;box-sizing:border-box;font-size:12px;';
        bodyEl.replaceChildren(preview);
        if (violations.length > 0) {
            const error = document.createElement('div');
            error.className = 'etlsql-html-preview-error';
            error.setAttribute('role', 'alert');
            error.textContent = `Preview blocked: ${violations[0]}`;
            preview.appendChild(error);
            return;
        }

        const shadow = preview.attachShadow({ mode: 'open' });
        if (css.trim()) {
            const style = document.createElement('style');
            style.textContent = css;
            shadow.appendChild(style);
        }
        const content = document.createElement('div');
        content.className = 'etlsql-html-visual-body';
        content.appendChild(sanitized);
        shadow.appendChild(content);
    }

    function _renderSnapshotCardBody(bodyEl: DesignerDom, visual: DesignerVisual, snapshotPackage: DesignerSnapshotPackage | undefined): void {
        if (!snapshotPackage || !snapshotPackage.sampleRows) {
            bodyEl.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--portal-muted,#64748b);font-size:11px;">No snapshot data</div>`;
            return;
        }

        // Resolve the visual's own identity first, then its dataset. The snapshot manifest records
        // visuals and datasets but never links them, so the server keys sample rows by visual name —
        // the only identity both sides share. Dataset lookup stays as a fallback for packages keyed
        // that way (the UI sandbox fixtures), and the first-entry fallback keeps a single-dataset
        // report rendering rather than showing nothing.
        const sampleRows = snapshotPackage.sampleRows;
        const byVisual = [visual.name, visual.title, visual.id].find(k => k && sampleRows[k]) as string | undefined;
        const dsName = visual.dataset;
        let rows: DesignerRow[] = (byVisual ? sampleRows[byVisual] : undefined)
            || (dsName && sampleRows[dsName])
            || Object.values(sampleRows)[0]
            || [];
        const type = (visual.type || '').toUpperCase();

        // Interactive Filter Slicers Simulation
        if (type === 'SLICER' || type === 'MULTISELECT' || type === 'DATEPICKER') {
            const categories = Array.from(new Set(rows.map(r => String(Array.isArray(r) ? r[0] : r))));
            const selected = activeSnapshotFilter;
            let btnHtml = `<button class="btn btn-xs ${!selected ? 'btn-primary' : ''}" data-slicer-val="" style="margin:2px;font-size:10px;">All</button>`;
            categories.slice(0, 8).forEach(cat => {
                const isSel = String(selected).toLowerCase() === String(cat).toLowerCase();
                btnHtml += `<button class="btn btn-xs ${isSel ? 'btn-primary' : ''}" data-slicer-val="${esc(cat)}" style="margin:2px;font-size:10px;">${esc(cat)}</button>`;
            });

            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:4px;text-align:center;">
                    <div style="font-size:10px;font-weight:600;color:var(--portal-muted,#64748b);margin-bottom:4px;">Filter by ${esc(visual.title || 'Category')}</div>
                    <div style="display:flex;flex-wrap:wrap;justify-content:center;gap:2px;">${btnHtml}</div>
                </div>`;

            queryElements(bodyEl, '[data-slicer-val]').forEach(b => {
                b.addEventListener('click', e => {
                    e.stopPropagation();
                    const btn = e.currentTarget as HTMLElement;
                    const val = btn.getAttribute('data-slicer-val');
                    activeSnapshotFilter = val || null;
                    renderCanvas();
                });
            });
            return;
        }

        if (type === 'CONTAINER') {
            const containerType = visual.options?.CONTAINER_TYPE || 'BOX';
            const childCount = context.curVis().filter(c => c.containerId === visual.id).length;
            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:12px;color:var(--portal-muted,#64748b);font-size:11px;border:1.5px dashed var(--portal-border-soft,#cbd5e1);border-radius:6px;background:rgba(37, 99, 235, 0.02);pointer-events:none;">
                    <div style="font-weight:600;color:var(--portal-text-soft,#475569);font-size:12px;margin-bottom:2px;">📁 ${esc(containerType)} Container</div>
                    <div style="font-size:10px;color:var(--portal-muted,#94a3b8);">${childCount > 0 ? `${childCount} visual${childCount === 1 ? '' : 's'} grouped inside` : 'Drag visuals on top to group'}</div>
                </div>`;
            return;
        }

        // Apply active filter if set
        if (activeSnapshotFilter) {
            const filterLower = activeSnapshotFilter.toLowerCase();
            rows = rows.filter(r => Array.isArray(r)
                ? r.some(cell => String(cell).toLowerCase() === filterLower)
                : String(r).toLowerCase() === filterLower);
        }

        if (type === 'HTML') {
            _renderHtmlVisualPreview(bodyEl, visual, snapshotPackage);
            return;
        }

        if (type === 'CARD') {
            const val = rows[0] ? (Array.isArray(rows[0]) ? (rows[0][rows[0].length - 1] ?? rows[0][0]) : Object.values(rows[0])[0]) : '0';
            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:4px;text-align:center;">
                    <div style="font-size:22px;font-weight:700;color:var(--portal-accent,#2563eb);line-height:1.2;">${esc(val)}</div>
                    <div style="font-size:11px;color:var(--portal-muted,#64748b);margin-top:2px;">${esc(visual.title || visual.name)}</div>
                </div>`;
            return;
        }

        if (type === 'TABLE' || type === 'MATRIX') {
            const mappings = visual.mappings || {};
            const sampleHeaders = Object.values(mappings).filter(Boolean);
            const headers = sampleHeaders.length ? sampleHeaders : (type === 'MATRIX' ? ['Row', 'Col', 'Value'] : ['Region', 'Quarter', 'Revenue']);
            let html = `<table style="width:100%;height:100%;font-size:11px;border-collapse:collapse;color:var(--portal-text,#172033);">
                <thead><tr style="background:var(--portal-surface-subtle,#f8fafc);border-bottom:1px solid var(--portal-border,#d9e0ea);">
                    ${headers.map(h => `<th style="padding:3px 5px;text-align:left;font-weight:600;">${esc(h)}</th>`).join('')}
                </tr></thead><tbody>`;
            const displayRows = rows.slice(0, 5);
            displayRows.forEach(r => {
                const cells = Array.isArray(r) ? r : [r];
                html += `<tr style="border-bottom:1px solid var(--portal-border,#e2e8f0);">${cells.map(cell => `<td style="padding:2px 5px;">${esc(cell)}</td>`).join('')}</tr>`;
            });
            html += `</tbody></table>`;
            bodyEl.innerHTML = html;
            return;
        }

        // Server-rendered native GoG SVG preview when available
        const visualSvgs = snapshotPackage.visualSvgs;
        const svgKey = byVisual || (dsName && visualSvgs?.[dsName] ? dsName : null) || visual.name || visual.id;
        const compiledSvg = visualSvgs && svgKey ? visualSvgs[svgKey] : null;
        if (compiledSvg && !activeSnapshotFilter) {
            bodyEl.innerHTML = compiledSvg;
            return;
        }

        // Dependency-free preview fallback; production manifests use the native SVG surface. It reads
        // the visual's MAPPINGS, so assigning a column to a role changes what the card draws. The
        // previous fallback chose columns by position and ignored the mapping entirely.
        const visualColumns = (byVisual ? snapshotPackage.columnsByVisual?.[byVisual] : undefined)
            || (visual.name ? snapshotPackage.columnsByVisual?.[visual.name] : undefined)
            || (dsName ? snapshotPackage.columnsByVisual?.[dsName] : undefined)
            || snapshotPackage.columns
            || [];
        renderVisualSample(bodyEl as unknown as HTMLElement, visual, { columns: Array.isArray(visualColumns) ? visualColumns : [], rows });
    }

    function renderCanvas() {
        disconnectSnapshotResizeObservers();
        context.canvasGrid.innerHTML = '';
        const visuals = context.curVis();
        if (!visuals.length) {
            const ph = document.createElement('div');
            ph.className = 'etlsql-dsgn-canvas-empty';
            const dataRequired = context.opts.requireDataFirst && context.opts.canAddVisual && !context.opts.canAddVisual();
            ph.innerHTML = dataRequired
                ? `<strong>Connect data to start</strong><span>Choose a source and build a reusable sample before adding visuals.</span><button type="button" data-empty-data>Choose data</button>`
                : `<strong>Build your first visual</strong><span>Search the visual library, or start with a familiar chart.</span><button type="button" data-empty-vtype="BAR">+ Add bar chart</button>`;
            context.canvasGrid.appendChild(ph);
            return;
        }
        const rows = context.maxRow(visuals) + 2;
        context.canvasGrid.style.gridTemplateRows = `repeat(${rows}, 60px)`;
        for (const v of visuals) {
            const isContainer = v.type === 'CONTAINER';
            const isFolded = isContainer && context.collapsedContainers.has(v.id);
            const card = document.createElement('div') as unknown as DesignerDom;
            card.className = 'etlsql-dsgn-visual-card' + (v.id === context.selVisualId ? ' selected' : '') + (isContainer ? ' is-container' : '') + (isFolded ? ' is-folded' : '') + (context.isLocked(v) ? ' is-locked' : '');
            if (v.containerId) {
                card.classList.add('has-container');
                card.dataset.containerId = v.containerId;
            }
            card.dataset.vid = v.id;
            card.dataset.visualId = v.id;
            card.classList.add('etlsql-studio-canvas-card');
            card.style.gridColumn = `${v.gridCol || 1} / span ${v.gridColSpan || 12}`;
            card.style.gridRow    = `${v.gridRow || 1} / span ${isFolded ? 1 : (v.gridRowSpan || 4)}`;
            card.style.setProperty('--vc', VCOLOR[v.type] || '#64748b');
            card.style.zIndex     = isContainer ? '1' : '2';

            if (v.options?.BACKGROUND) card.style.background = v.options.BACKGROUND;
            if (v.options?.COLOR) card.style.color = v.options.COLOR;
            if (v.options?.BORDER) card.style.border = v.options.BORDER;
            if (v.options?.BORDER_RADIUS) card.style.borderRadius = v.options.BORDER_RADIUS;
            if (v.options?.SHADOW) {
                const s = v.options.SHADOW.trim().toUpperCase();
                if (s === 'ON') card.style.boxShadow = '0 2px 8px rgba(0,0,0,0.08)';
                else if (s === 'OFF') card.style.boxShadow = 'none';
                else card.style.boxShadow = v.options.SHADOW;
            }
            if (v.options?.FONT) card.style.fontFamily = v.options.FONT;
            if (v.options?.FONT_SIZE) card.style.fontSize = v.options.FONT_SIZE;
            if (v.options?.FONT_WEIGHT) card.style.fontWeight = v.options.FONT_WEIGHT;
            if (v.options?.OPACITY) card.style.opacity = v.options.OPACITY;

            let badgeExtra = '';
            if (context.opts.snapshotPackage) {
                const meta = context.opts.snapshotPackage.metadata || {};
                if (meta.rlsPolicy || meta.rlsEnforced) {
                    badgeExtra += `<span style="background:var(--portal-accent,#2563eb);color:#fff;padding:1px 4px;border-radius:3px;font-size:9px;margin-left:4px;" title="RLS Governance Policy Enforced">🔒 RLS</span>`;
                }
                if (meta.isSampled) {
                    badgeExtra += `<span style="background:#f59e0b;color:#fff;padding:1px 4px;border-radius:3px;font-size:9px;margin-left:4px;" title="Sampled Snapshot Data">⚡ Sampled</span>`;
                }
            }

            const badgeText = isContainer ? `📁 ${v.options?.CONTAINER_TYPE || 'BOX'}` : v.type;
            const foldBtn = isContainer ? `<button class="etlsql-dsgn-vcard-fold" data-fold="${v.id}" title="${isFolded ? 'Expand container' : 'Collapse container'}" aria-label="${isFolded ? 'Expand container' : 'Collapse container'}"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="${isFolded ? 'M6 3.5 11 8l-5 4.5' : 'm3.5 6 4.5 5 4.5-5'}"/></svg></button>` : '';
            const dupBtn = `<button class="etlsql-dsgn-vcard-dup" data-dup="${v.id}" title="Duplicate visual" aria-label="Duplicate visual"><svg viewBox="0 0 16 16" aria-hidden="true"><rect x="5.5" y="2.5" width="8" height="8" rx="1.5"/><path d="M10.5 11v1.5a1 1 0 0 1-1 1h-6a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1H5"/></svg></button>`;
            const detachBtn = v.containerId ? `<button class="etlsql-dsgn-vcard-detach" data-detach="${v.id}" title="Detach from container" aria-label="Detach from container"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5 11 11.5 4.5M7.5 4.5h4v4M3 7v5a1 1 0 0 0 1 1h5"/></svg></button>` : '';

            const cardHdr = document.createElement('div') as unknown as DesignerDom;
            cardHdr.className = 'etlsql-dsgn-vcard-hdr';
            cardHdr.innerHTML = `
                <div class="etlsql-dsgn-vcard-badge">${badgeText}${badgeExtra}</div>
                <button type="button" class="etlsql-dsgn-vcard-name" data-edit-title="${v.id}" title="Rename visual">${esc(v.title || v.name)}</button>
                <div class="etlsql-dsgn-vcard-actions">${foldBtn}${dupBtn}${detachBtn}
                    <button class="etlsql-dsgn-vcard-del" data-del="${v.id}" title="Remove visual" aria-label="Remove visual"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="m4 4 8 8m0-8-8 8"/></svg></button>
                </div>
            `;
            const titleButton = queryElement(cardHdr, '.etlsql-dsgn-vcard-name');
            const titleFormatting = v.formatting?.title;
            if (titleButton && titleFormatting) {
                if (titleFormatting.color) /** @type {HTMLElement} */ (titleButton).style.color = titleFormatting.color;
                if (titleFormatting.font) /** @type {HTMLElement} */ (titleButton).style.fontFamily = titleFormatting.font;
                if (titleFormatting.size) /** @type {HTMLElement} */ (titleButton).style.fontSize = titleFormatting.size;
                if (titleFormatting.weight) /** @type {HTMLElement} */ (titleButton).style.fontWeight = titleFormatting.weight;
                if (titleFormatting.align) /** @type {HTMLElement} */ (titleButton).style.textAlign = titleFormatting.align.toLowerCase();
            }
            card.appendChild(cardHdr);

            const cardBody = document.createElement('div') as unknown as DesignerDom;
            cardBody.className = 'etlsql-dsgn-vcard-body';

            if (context.opts.snapshotPackage || context.opts.snapshotMode) {
                _renderSnapshotCardBody(cardBody, v, context.opts.snapshotPackage);
            } else if (v.type === 'CUSTOM') {
                const width = 360, height = 180, pad = 24;
                cardBody.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="${esc(v.title || v.name)}" style="width:100%;height:100%"><line x1="${pad}" y1="${height - pad}" x2="${width - pad}" y2="${height - pad}" stroke="#cbd5e1"/><line x1="${pad}" y1="${pad}" x2="${pad}" y2="${height - pad}" stroke="#cbd5e1"/><rect x="60" y="60" width="30" height="96" rx="2" fill="#8b5cf6" opacity="0.85"/><rect x="110" y="40" width="30" height="116" rx="2" fill="#8b5cf6" opacity="0.85"/><rect x="160" y="80" width="30" height="76" rx="2" fill="#8b5cf6" opacity="0.85"/><path d="M 75 80 L 125 50 L 175 90 L 225 30" fill="none" stroke="#06b6d4" stroke-width="2"/><circle cx="75" cy="80" r="3" fill="#06b6d4"/><circle cx="125" cy="50" r="3" fill="#06b6d4"/><circle cx="175" cy="90" r="3" fill="#06b6d4"/><circle cx="225" cy="30" r="3" fill="#06b6d4"/><text x="180" y="20" font-size="10" fill="#7a8798" text-anchor="middle">CUSTOM CHART (GoG Layers)</text></svg>`;
            } else if (v.type === 'HTML') {
                _renderHtmlVisualPreview(cardBody, v, context.opts.snapshotPackage);
            } else {
                cardBody.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--portal-muted,#64748b);font-size:11px;">${v.type} Placeholder</div>`;
            }

            card.appendChild(cardBody);

            const resizeHandle = document.createElement('div');
            resizeHandle.className = 'etlsql-dsgn-vcard-resize';
            resizeHandle.title = 'Drag to resize';
            card.appendChild(resizeHandle);

            context.canvasGrid.appendChild(card);
        }
    }

    function renderTree() {
        const tree = queryElement(context.sidebar, '#dsgn-tree');
        tree.innerHTML = '';
        const visuals = context.curVis();
        const containers = visuals.filter(v => v.type === 'CONTAINER');
        const rootVisuals = visuals.filter(v => !v.containerId || !containers.some(c => c.id === v.containerId));

        if (!rootVisuals.length) {
            tree.innerHTML = '<div class="etlsql-dsgn-sidebar-empty"><strong>No visuals on this page</strong><span>Add one from the visual library above.</span></div>';
            return;
        }

        for (const v of rootVisuals) {
            const item = document.createElement('div');
            item.className = 'etlsql-dsgn-tree-item' + (v.id === context.selVisualId ? ' selected' : '');
            item.dataset.vid = v.id;
            const icon = v.type === 'CONTAINER' ? '📁' : '📊';
            item.textContent = `${icon} ${v.name} (${v.type})`;
            tree.appendChild(item);

            if (v.type === 'CONTAINER') {
                const children = visuals.filter(c => c.containerId === v.id);
                for (const child of children) {
                    const citem = document.createElement('div');
                    citem.className = 'etlsql-dsgn-tree-item child-item' + (child.id === context.selVisualId ? ' selected' : '');
                    citem.style.paddingLeft = '20px';
                    citem.dataset.vid = child.id;
                    citem.textContent = `└─ ${child.name} (${child.type})`;
                    tree.appendChild(citem);
                }
            }
        }
    }

    function renderDatasets() {
        const list = queryElement(context.sidebar, '#dsgn-ds-list');
        list.innerHTML = '';
        if (!context.state.datasets.length) {
            list.innerHTML = '<div class="etlsql-dsgn-sidebar-empty"><strong>No datasets yet</strong><span>Add a dataset to expose fields for mappings.</span></div>';
            return;
        }
        for (const ds of context.state.datasets) {
            const isExpanded = expandedDsIds.has(ds.id);
            const row = document.createElement('div');
            row.className = 'etlsql-dsgn-ds-block';

            let cols: string[] = [];
            if (context.opts.snapshotPackage && Array.isArray(context.opts.snapshotPackage.columns)) {
                cols = context.opts.snapshotPackage.columns;
            } else if (context.opts.getDatasetColumns) {
                cols = context.opts.getDatasetColumns(ds.name) || [];
            }

            const toggleIcon = cols.length ? (isExpanded ? '▾' : '▸') : ' ';
            row.innerHTML = `
                <div class="etlsql-dsgn-ds-item" data-dstoggle="${esc(ds.id)}" style="cursor:pointer">
                    <span>${toggleIcon} #${esc(ds.name)}</span>
                    <button data-dsid="${esc(ds.id)}" title="Remove">✕</button>
                </div>
                ${isExpanded && cols.length ? `
                    <div class="etlsql-dsgn-ds-cols">
                        ${cols.map(c => `
                            <div class="etlsql-dsgn-col-pill" draggable="true" data-col="${esc(c)}" title="Drag into a mapping field">
                                📄 ${esc(c)}
                            </div>
                        `).join('')}
                    </div>
                ` : ''}
            `;
            list.appendChild(row);
        }
    }

    function renderAlignmentToolbar() {
        let bar = queryElement(context.canvasWrap, '#dsgn-align-bar');
        if (context.selVisualIds.size < 2) {
            if (bar) /** @type {HTMLElement} */ (bar).style.display = 'none';
            return;
        }

        if (!bar) {
            bar = document.createElement('div') as unknown as DesignerDom;
            bar.id = 'dsgn-align-bar';
            bar.className = 'etlsql-dsgn-align-bar';
            context.canvasWrap.appendChild(bar);

            bar.addEventListener('click', e => {
                const btn = closestElement(e, '[data-align]');
                if (!btn) return;
                const mode = /** @type {HTMLElement} */ (btn).dataset.align;
                const visuals = context.curVis().filter(v => context.selVisualIds.has(v.id));
                if (visuals.length < 2) return;

                if (mode === 'left') {
                    const minCol = Math.min(...visuals.map(v => v.gridCol || 1));
                    visuals.forEach(v => v.gridCol = minCol);
                } else if (mode === 'top') {
                    const minRow = Math.min(...visuals.map(v => v.gridRow || 1));
                    visuals.forEach(v => v.gridRow = minRow);
                } else if (mode === 'width') {
                    const targetSpan = visuals[0].gridColSpan || 12;
                    visuals.forEach(v => v.gridColSpan = targetSpan);
                } else if (mode === 'height') {
                    const targetSpan = visuals[0].gridRowSpan || 4;
                    visuals.forEach(v => v.gridRowSpan = targetSpan);
                }
                renderCanvas();
            });
        }

        bar.innerHTML = `
            <span style="font-size:11px;font-weight:600;margin-right:2px;">${context.selVisualIds.size} selected</span>
            <button class="btn btn-xs" data-align="left" title="Align Left">⬅ Left</button>
            <button class="btn btn-xs" data-align="top" title="Align Top">⬆ Top</button>
            <button class="btn btn-xs" data-align="width" title="Equal Width">↔ Width</button>
            <button class="btn btn-xs" data-align="height" title="Equal Height">↕ Height</button>
        `;
        /** @type {HTMLElement} */ (bar).style.display = 'flex';
    }

    function triggerChartResizes() {}

    return { disconnectSnapshotResizeObservers, tidyLayout, renderPageTabs, renderCanvas, renderTree, renderDatasets, renderAlignmentToolbar, triggerChartResizes };
}
