/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Native SVG charts, toolbox, zoom, and responsive layout.
 */
import { noDataEl } from './rt-util.js';
import { actionsFor, applyPageCrossFilter, executeAction, showCtxMenu } from './rt-actions.js';
import { appendDetailStaticNote, attachDetailSurface } from './rt-detail.js';
import { apiBase, getLastManifest, isWebMode, vscode } from './rt-state.js';
import { isOfflineSnapshot } from './rt-views.js';
import { renderManifest } from './report-runtime.js';
import type { ActionVisual } from './rt-actions.js';
import type { DetailVisual, DetailManifest } from './rt-detail.js';

export type ChartRow = unknown[];

export interface ChartLayout {
    tier?: string;
    compactMaxWidth?: number;
    standardMaxWidth?: number;
}

export interface ChartExtent {
    axis?: string;
    anchor?: string;
}

export interface ChartInteraction {
    key?: string | null;
    valueKey?: string | null;
    select: string;
    effect: string;
    highlight: string;
    extent?: ChartExtent | null;
}

export interface ChartVisual extends ActionVisual {
    name?: string;
    columns?: string[];
    rows?: ChartRow[];
    options?: Record<string, string>;
    visualType?: string;
    title?: string;
    nativeSvg?: string;
    layout?: ChartLayout;
    interaction?: ChartInteraction;
    interactions?: Record<string, unknown>;
    highlightRows?: ChartRow[];
}

export interface ChartManifest {
    visuals?: ChartVisual[];
    pages?: Array<{ visuals?: ChartVisual[] }>;
    [key: string]: unknown;
}

let _nativeLayoutObservers: ResizeObserver[] = [];
export const _nativeLayoutTimers = new Map<string, ReturnType<typeof setTimeout>>();
const _nativeLayoutRequests = new Map<string, { tier: string; controller: AbortController }>();

// Chart-type visuals are rendered server-side into `nativeSvg`. A manifest that reaches
// the browser without one — an older snapshot, a lightweight/externalized manifest, an
// unrecognized visual type — has no payload the runtime can draw. Degrade to an explicit,
// announced state for that one card instead of aborting the whole page render.
export function renderMissingChartPayload(container: HTMLElement, visual: ChartVisual): HTMLElement {
    const type = (visual.visualType || 'chart').toUpperCase();
    const name = visual.title || visual.name || 'this visual';
    const el = noDataEl('Chart payload missing for ' + name + ' (' + type + '). Re-run the report to regenerate it.');
    el.classList.add('missing-chart-payload');
    el.setAttribute('role', 'status');
    container.appendChild(el);
    return el;
}

// ── Interaction contract ──────────────────────────────────────────────
//
// The server resolves every interaction decision — which column a selection is keyed on, which
// column carries its measure, and how a selection is drawn — and ships it as the compact
// `visual.interaction` manifest. The runtime reads that and nothing else: it never re-derives a
// filter column from `mapping:*` options, and never infers geometry from a visual's type name.
//
// `legacyInteraction()` is the migration path for manifests built before v0.19 — offline
// snapshots, cached artifacts — which carry the old `visual.interactions` map instead. It is the
// only place left that reads `visualType`, and it is reached only when `visual.interaction` is
// absent.

export function resolveInteraction(visual?: ChartVisual | Record<string, unknown> | null): ChartInteraction {
    const resolved = visual && (visual.interaction as ChartInteraction | undefined);
    if (!resolved) return legacyInteraction((visual || {}) as ChartVisual);
    return {
        key: resolved.key || null,
        valueKey: resolved.valueKey || null,
        select: String(resolved.select || 'NONE').toUpperCase(),
        effect: String(resolved.effect || 'HIGHLIGHT').toUpperCase(),
        highlight: String(resolved.highlight || 'NONE').toUpperCase(),
        extent: null
    };
}

function legacyInteraction(visual: ChartVisual | Record<string, unknown>): ChartInteraction {
    const legacy = (visual.interactions || {}) as Record<string, string>;
    const mode = String(legacy['ON_SELECT'] || '').toUpperCase();
    const active = !!mode && mode !== 'NONE';
    const options = (visual.options || {}) as Record<string, string>;
    const key = legacy['MATCHING'] || options['mapping:x'] || options['mapping:label'] ||
        options['mapping:name'] || options['mapping:region'] || options['mapping:y'] ||
        ((visual.columns as string[] | undefined) || [])[0] || null;
    const type = String(visual.visualType || '').toUpperCase();
    const bar = type === 'BAR' || type === 'HBAR' || type === 'HORIZONTALBAR';
    return {
        key: key,
        valueKey: options['mapping:y'] || null,
        select: active ? 'MULTIPLE' : 'NONE',
        effect: mode === 'FILTER' ? 'FILTER' : 'HIGHLIGHT',
        highlight: active ? (bar ? 'PROPORTIONAL' : 'CATEGORICAL') : 'CATEGORICAL',
        // Pre-v0.19 SVG carries no semantic extent attributes, so the shim supplies what the
        // server now stamps on the mark itself.
        extent: bar
            ? { axis: type === 'BAR' ? 'y' : 'x', anchor: type === 'BAR' ? 'end' : 'start' }
            : null
    };
}

export function crossFilterActive(interaction: ChartInteraction): boolean {
    return interaction.select !== 'NONE' && !!interaction.key;
}

export function renderNativeSvg(container: HTMLElement, visual: ChartVisual, manifest: ChartManifest, pageTheme: string | null): void {
    const wrapper = document.createElement('div');
    wrapper.className = 'chart-wrapper native-chart-wrapper';
    const parsed = new DOMParser().parseFromString(String(visual.nativeSvg || ''), 'image/svg+xml');
    const svg = parsed.documentElement;
    if (!svg || svg.nodeName.toLowerCase() !== 'svg' || parsed.querySelector('parsererror')) {
        container.appendChild(noDataEl('Invalid native chart payload'));
        return;
    }
    wrapper.appendChild(document.importNode(svg, true));
    if (visual.layout?.tier) wrapper.dataset.layoutTier = String(visual.layout.tier).toUpperCase();
    container.appendChild(wrapper);
    attachNativeZoomSlider(container, wrapper, visual);
    attachNativeChartToolbox(container, wrapper, visual);
    attachProgressiveReveal(wrapper, visual);
    observeNativeLayout(wrapper, visual);

    const clickActions = actionsFor(visual, 'ON_CLICK');
    const interaction = resolveInteraction(visual);
    const crossFilter = crossFilterActive(interaction);
    const mappingColumn = interaction.key;
    let activeRow: ChartRow | null = null;

    applyNativeHighlight(wrapper, visual, interaction);

    // The wrapper owns its detail surface: re-rendering or unmounting the visual
    // tears it down, so a surface can never outlive the marks it is anchored to.
    const detailSurface = attachDetailSurface(wrapper, (visual as unknown as DetailVisual), (manifest as unknown as DetailManifest), pageTheme, (mappingColumn as string | null));
    /** @type {EtlSqlVisualHost} */ ((wrapper) as EtlSqlVisualHost)._detailSurface = detailSurface;
    appendDetailStaticNote(container, (visual as unknown as DetailVisual));

    
    const vopts = visual.options || {};
    const crosshairOpt = (vopts['CROSSHAIR'] || '').toUpperCase();
    const crosshairAxis = (vopts['CROSSHAIR_AXIS'] || 'BOTH').toUpperCase();
    const crosshairColor = vopts['CROSSHAIR_COLOR'] || '#94a3b8';
    const crosshairDash = vopts['CROSSHAIR_DASH'] || '4 4';
    const linkTooltipGroup = vopts['LINK_TOOLTIP'] ? vopts['LINK_TOOLTIP'].trim() : null;

    if (linkTooltipGroup) {
        wrapper.dataset.linkTooltip = linkTooltipGroup;
    }

    const svgEl = wrapper.querySelector<SVGSVGElement>('svg');
    if (svgEl && (crosshairOpt === 'ON' || crosshairOpt === 'TRUE' || vopts['CROSSHAIR_AXIS'] || linkTooltipGroup)) {
        const chartSvg = svgEl;
        let crosshairG = svgEl.querySelector('.plot-crosshair-group');
        if (!crosshairG) {
            crosshairG = document.createElementNS('http://www.w3.org/2000/svg', 'g');
            crosshairG.setAttribute('class', 'plot-crosshair-group');
            crosshairG.setAttribute('pointer-events', 'none');
            /** @type {HTMLElement} */ ((crosshairG) as unknown as HTMLElement).style.display = 'none';
            svgEl.appendChild(crosshairG);
        }

        const lineX = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        lineX.setAttribute('class', 'plot-crosshair-x');
        lineX.setAttribute('stroke', crosshairColor);
        lineX.setAttribute('stroke-dasharray', crosshairDash);
        lineX.setAttribute('stroke-width', '1');
        if (crosshairAxis === 'X' || crosshairAxis === 'BOTH') crosshairG.appendChild(lineX);

        const lineY = document.createElementNS('http://www.w3.org/2000/svg', 'line');
        lineY.setAttribute('class', 'plot-crosshair-y');
        lineY.setAttribute('stroke', crosshairColor);
        lineY.setAttribute('stroke-dasharray', crosshairDash);
        lineY.setAttribute('stroke-width', '1');
        if (crosshairAxis === 'Y' || crosshairAxis === 'BOTH') crosshairG.appendChild(lineY);

        function updateCrosshair(svgPoint: { x: number; y: number }): void {
            const bbox = chartSvg.viewBox?.baseVal || { x: 0, y: 0, width: chartSvg.clientWidth || 600, height: chartSvg.clientHeight || 400 };
            /** @type {HTMLElement} */ ((crosshairG) as unknown as HTMLElement).style.display = '';
            if (lineX) {
                lineX.setAttribute('x1', String(svgPoint.x));
                lineX.setAttribute('x2', String(svgPoint.x));
                lineX.setAttribute('y1', String(bbox.y || 0));
                lineX.setAttribute('y2', String((bbox.y || 0) + (bbox.height || 400)));
            }
            if (lineY) {
                lineY.setAttribute('y1', String(svgPoint.y));
                lineY.setAttribute('y2', String(svgPoint.y));
                lineY.setAttribute('x1', String(bbox.x || 0));
                lineY.setAttribute('x2', String((bbox.x || 0) + (bbox.width || 600)));
            }
        }

        function hideCrosshair(): void {
            /** @type {HTMLElement} */ ((crosshairG) as unknown as HTMLElement).style.display = 'none';
        }

        /** @type {EtlSqlVisualHost} */ ((wrapper) as EtlSqlVisualHost)._updateCrosshair = updateCrosshair;
        /** @type {EtlSqlVisualHost} */ ((wrapper) as EtlSqlVisualHost)._hideCrosshair = hideCrosshair;

        chartSvg.addEventListener('pointermove', event => {
            const pt = chartSvg.createSVGPoint();
            pt.x = event.clientX;
            pt.y = event.clientY;
            const ctm = chartSvg.getScreenCTM();
            if (ctm) {
                const svgPt = pt.matrixTransform(ctm.inverse());
                updateCrosshair(svgPt);

                if (linkTooltipGroup) {
                    const linkedWrappers = document.querySelectorAll(`[data-link-tooltip="${CSS.escape(linkTooltipGroup)}"]`);
                    linkedWrappers.forEach(w => {
                        if (w !== wrapper && typeof /** @type {EtlSqlVisualHost} */ ((w) as EtlSqlVisualHost)._updateCrosshair === 'function') {
                            /** @type {EtlSqlVisualHost} */ ((w) as EtlSqlVisualHost)._updateCrosshair!(svgPt);
                        }
                    });
                }
            }
        });

        chartSvg.addEventListener('pointerleave', () => {
            hideCrosshair();
            if (linkTooltipGroup) {
                const linkedWrappers = document.querySelectorAll(`[data-link-tooltip="${CSS.escape(linkTooltipGroup)}"]`);
                linkedWrappers.forEach(w => {
                    if (w !== wrapper && typeof /** @type {EtlSqlVisualHost} */ ((w) as EtlSqlVisualHost)._hideCrosshair === 'function') {
                        /** @type {EtlSqlVisualHost} */ ((w) as EtlSqlVisualHost)._hideCrosshair!();
                    }
                });
            }
        });
    }

    // Setup Hover Focus & Series Emphasis
    const hoverFocusMode = (svgEl?.dataset?.hoverFocus || vopts['HOVER_FOCUS'] || 'NONE').toUpperCase();
    if (svgEl && hoverFocusMode !== 'NONE') {
        svgEl.addEventListener('pointerover', event => {
            if (hoverFocusMode === 'SERIES') {
                const target = /** @type {Element} */ ((event.target) as Element).closest('[data-series]');
                if (target) {
                    const seriesKey = /** @type {HTMLElement} */ ((target) as HTMLElement).dataset.series;
                    const allSeriesMarks = svgEl.querySelectorAll('[data-series]');
                    allSeriesMarks.forEach(m => {
                        if (/** @type {HTMLElement} */ ((m) as HTMLElement).dataset.series === seriesKey) {
                            m.classList.add('plot-series-focused');
                            m.classList.remove('plot-series-dimmed');
                        } else {
                            m.classList.add('plot-series-dimmed');
                            m.classList.remove('plot-series-focused');
                        }
                    });
                }
            } else if (hoverFocusMode === 'SELF') {
                const mark = /** @type {Element} */ ((event.target) as Element).closest('[data-row-index]');
                if (mark) {
                    const allMarks = svgEl.querySelectorAll('[data-row-index]');
                    allMarks.forEach(m => {
                        if (m === mark) {
                            m.classList.add('plot-mark-focused');
                            m.classList.remove('plot-mark-dimmed');
                        } else {
                            m.classList.add('plot-mark-dimmed');
                            m.classList.remove('plot-mark-focused');
                        }
                    });
                }
            }
        });

        svgEl.addEventListener('pointerleave', () => {
            if (hoverFocusMode === 'SERIES') {
                svgEl.querySelectorAll('.plot-series-focused, .plot-series-dimmed').forEach(m => {
                    m.classList.remove('plot-series-focused', 'plot-series-dimmed');
                });
            } else if (hoverFocusMode === 'SELF') {
                svgEl.querySelectorAll('.plot-mark-focused, .plot-mark-dimmed').forEach(m => {
                    m.classList.remove('plot-mark-focused', 'plot-mark-dimmed');
                });
            }
        });
    }

    // Setup Animation
    const animOpt = (svgEl?.dataset?.animation || vopts['ANIMATION'] || 'ON').toUpperCase();
    if (svgEl && animOpt !== 'OFF') {
        const rawDuration = svgEl?.dataset?.animationDuration || vopts['ANIMATION_DURATION'] || '800';
        const durationMs = parseInt(rawDuration, 10) || 800;
        const easingOpt = (svgEl?.dataset?.animationEasing || vopts['ANIMATION_EASING'] || 'EASE_OUT').toUpperCase();
        const easingMap: Record<string, string> = {
            'LINEAR': 'linear',
            'EASE_IN': 'cubic-bezier(0.4, 0, 1, 1)',
            'EASE_OUT': 'cubic-bezier(0, 0, 0.2, 1)',
            'ELASTIC': 'cubic-bezier(0.68, -0.55, 0.265, 1.55)',
            'BOUNCE': 'cubic-bezier(0.34, 1.56, 0.64, 1)'
        };
        const easingCss = easingMap[easingOpt] || 'cubic-bezier(0, 0, 0.2, 1)';
        wrapper.style.setProperty('--anim-duration', `${durationMs}ms`);
        wrapper.style.setProperty('--anim-easing', easingCss);
        wrapper.classList.add('chart-animated');

        const updateAnimOpt = (svgEl?.dataset?.updateAnimation || vopts['UPDATE_ANIMATION'] || 'ON').toUpperCase();
        if (updateAnimOpt !== 'OFF') {
            wrapper.classList.add('update-animated');
        }
    }

    wrapper.addEventListener('pointerover', event => {
        const mark = /** @type {Element} */ ((event.target) as Element).closest('[data-row-index]');
        activeRow = mark ? (visual.rows || [])[Number(/** @type {HTMLElement} */ ((mark) as HTMLElement).dataset.rowIndex)] || null : null;
    });
    wrapper.addEventListener('click', event => {
        const mark = /** @type {Element} */ ((event.target) as Element).closest('[data-row-index]');
        if (!mark) return;
        const index = Number(/** @type {HTMLElement} */ ((mark) as HTMLElement).dataset.rowIndex);
        const row = (visual.rows || [])[index] || [];
        const columnIndex = crossFilter && mappingColumn
            ? (visual.columns || []).findIndex(column => column.toLowerCase() === mappingColumn.toLowerCase())
            : -1;
        // No positional fallback. Cross-filtering on "whatever column came first" produces a
        // confidently wrong filter; doing nothing is the safer failure.
        if (columnIndex >= 0) {
            const value = row[columnIndex];
            if (value != null) applyPageCrossFilter(container, String(value), mappingColumn, visual.name, event);
        } else {
            clickActions.forEach(action => executeAction(action, row, visual.columns || [], visual.name, visual));
        }
    });
    // A linked chart's left click selects, so its ON_CLICK actions move to this menu.
    const menuClickActions = crossFilter ? clickActions : [];
    wrapper.addEventListener('contextmenu', event => {
        const drills = (visual.actions || []).some(action => action.type === 'DRILL_DOWN');
        if (!drills && !(menuClickActions.length && activeRow)) return;
        event.preventDefault();
        showCtxMenu(event.clientX, event.clientY, visual, activeRow, menuClickActions);
    });
}

/** @returns {boolean} whether an ON/TRUE/1 toggle is set on the visual. */
function nativeToggleOn(visual: ChartVisual, key: string): boolean {
    const value = String(visual.options?.[key] || '').toUpperCase();
    return value === 'ON' || value === 'TRUE' || value === '1';
}

/**
 * Per-chart toolbox: `SHOW_EXPORT = ON` adds a PNG download, `SHOW_DATA_VIEW = ON` adds a
 * toggle between the chart and a table of its SOURCE rows. Both are chart-local — neither
 * touches page state, and the data view is built from the visual's own columns and rows.
 */
function attachNativeChartToolbox(container: HTMLElement, wrapper: HTMLElement, visual: ChartVisual): void {
    const wantsExport = nativeToggleOn(visual, 'SHOW_EXPORT');
    const wantsDataView = nativeToggleOn(visual, 'SHOW_DATA_VIEW');
    if (!wantsExport && !wantsDataView) return;

    const bar = document.createElement('div');
    bar.className = 'native-chart-toolbox';
    bar.setAttribute('role', 'group');
    bar.setAttribute('aria-label', 'Chart tools');

    if (wantsExport) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'native-chart-toolbox-button';
        button.dataset.tool = 'save-image';
        button.title = 'Save chart as PNG';
        button.setAttribute('aria-label', 'Save chart as PNG');
        button.textContent = '⤓';
        button.addEventListener('click', () => saveChartImage(wrapper, visual));
        bar.appendChild(button);
    }

    if (wantsDataView) {
        const table = buildDataViewTable(visual);
        table.hidden = true;
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'native-chart-toolbox-button';
        button.dataset.tool = 'data-view';
        button.title = 'Show data table';
        button.setAttribute('aria-label', 'Show data table');
        button.setAttribute('aria-pressed', 'false');
        button.textContent = '▤';
        button.addEventListener('click', () => {
            const showing = table.hidden;
            table.hidden = !showing;
            wrapper.hidden = showing;
            button.setAttribute('aria-pressed', showing ? 'true' : 'false');
            button.title = showing ? 'Show chart' : 'Show data table';
        });
        bar.appendChild(button);
        container.appendChild(bar);
        container.appendChild(table);
        return;
    }

    container.appendChild(bar);
}

/** Renders the visual's SOURCE rows as an accessible table for `SHOW_DATA_VIEW`. */
function buildDataViewTable(visual: ChartVisual): HTMLTableElement {
    const columns = visual.columns || [];
    const rows = visual.rows || [];
    const table = document.createElement('table');
    table.className = 'native-chart-data-view';
    table.setAttribute('aria-label', (visual.name || 'Chart') + ' data');
    const head = document.createElement('tr');
    columns.forEach(column => {
        const cell = document.createElement('th');
        cell.scope = 'col';
        cell.textContent = String(column);
        head.appendChild(cell);
    });
    const thead = document.createElement('thead');
    thead.appendChild(head);
    table.appendChild(thead);
    const body = document.createElement('tbody');
    rows.forEach(row => {
        const tr = document.createElement('tr');
        columns.forEach((_column, index) => {
            const cell = document.createElement('td');
            cell.textContent = row[index] == null ? '' : String(row[index]);
            tr.appendChild(cell);
        });
        body.appendChild(tr);
    });
    table.appendChild(body);
    return table;
}

/**
 * Rasterises the chart's SVG through a canvas and hands the viewer a PNG. The SVG is serialized
 * from the live DOM, so what downloads is what is on screen, including any zoom applied.
 */
function saveChartImage(wrapper: HTMLElement, visual: ChartVisual): void {
    const svg = wrapper.querySelector('svg');
    if (!svg) return;
    const width = Number(svg.getAttribute('width')) || svg.clientWidth || 600;
    const height = Number(svg.getAttribute('height')) || svg.clientHeight || 400;
    const markup = new XMLSerializer().serializeToString(svg);
    const source = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(markup);
    const image = new Image();
    image.onload = () => {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, width, height);
        ctx.drawImage(image, 0, 0, width, height);
        canvas.toBlob(blob => {
            if (!blob) return;
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            anchor.href = url;
            anchor.download = (visual.name || 'chart') + '.png';
            document.body.appendChild(anchor);
            anchor.click();
            document.body.removeChild(anchor);
            URL.revokeObjectURL(url);
        }, 'image/png');
    };
    image.onerror = () => console.warn('Chart image export failed for', visual.name);
    image.src = source;
}

/**
 * `PROGRESSIVE = ON` reveals a dense chart's marks in `PROGRESSIVE_CHUNK` sized batches across
 * animation frames rather than painting every mark in one layout pass. The marks are already in
 * the served SVG, so this staggers when the browser has to composite them, which is the cost
 * that blocks the main thread on a high-cardinality series.
 */
function attachProgressiveReveal(wrapper: HTMLElement, visual: ChartVisual): void {
    if (!nativeToggleOn(visual, 'PROGRESSIVE')) return;
    if (typeof requestAnimationFrame !== 'function') return;
    const svg = wrapper.querySelector('svg');
    if (!svg) return;
    const marks = Array.from(svg.querySelectorAll('[data-row-index]'));
    const chunk = Math.max(1, Number(visual.options?.['PROGRESSIVE_CHUNK']) || 200);
    if (marks.length <= chunk) return;

    marks.forEach(mark => mark.setAttribute('visibility', 'hidden'));
    wrapper.dataset.progressive = String(chunk);
    let index = 0;
    const step = () => {
        const end = Math.min(marks.length, index + chunk);
        for (; index < end; index++) marks[index].removeAttribute('visibility');
        if (index < marks.length) requestAnimationFrame(step);
        else delete wrapper.dataset.progressive;
    };
    requestAnimationFrame(step);
}

function attachNativeZoomSlider(container: HTMLElement, wrapper: HTMLElement, visual: ChartVisual): void {
    const enabled = String(visual.options?.ZOOM_SLIDER || '').toUpperCase();
    const grouped = String(visual.options?.ZOOM_GROUP || '').trim() !== '';
    // Naming a ZOOM_GROUP implies the slider: a chart cannot join a linked zoom without one.
    if (!grouped && enabled !== 'ON' && enabled !== 'TRUE' && enabled !== '1') return;
    const svg = wrapper.querySelector('svg');
    const raw = String(svg?.getAttribute('viewBox') || '').trim().split(/\s+/).map(Number);
    if (!svg || raw.length !== 4 || raw.some(value => !Number.isFinite(value)) || raw[2] <= 0) return;

    const [originX, originY, fullWidth, fullHeight] = raw;
    const controls = document.createElement('div');
    controls.className = 'native-chart-zoom-slider';
    controls.setAttribute('role', 'group');
    controls.setAttribute('aria-label', 'Chart zoom range');
    const start = document.createElement('input');
    const end = document.createElement('input');
    for (const input of [start, end]) {
        input.type = 'range';
        input.min = '0';
        input.max = '100';
        input.step = '1';
    }
    start.value = '0';
    end.value = '100';
    start.setAttribute('aria-label', 'Visible range start');
    end.setAttribute('aria-label', 'Visible range end');
    const value = document.createElement('output');
    value.textContent = '0–100%';

    // ZOOM_GROUP links sliders: zooming one chart scrolls every chart naming the same group.
    const group = String(visual.options?.ZOOM_GROUP || '').trim();

    /** Applies a range to this chart without re-broadcasting, so linked charts cannot loop. */
    const applyRange = (first: number, last: number): void => {
        start.value = String(first);
        end.value = String(last);
        const x = originX + fullWidth * first / 100;
        const width = fullWidth * (last - first) / 100;
        svg.setAttribute('viewBox', `${x} ${originY} ${width} ${fullHeight}`);
        value.textContent = `${first}–${last}%`;
    };

    const update = (changed: HTMLInputElement): void => {
        let first = Number(start.value);
        let last = Number(end.value);
        if (last - first < 5) {
            if (changed === start) first = Math.max(0, last - 5);
            else last = Math.min(100, first + 5);
        }
        applyRange(first, last);
        if (group) broadcastZoomRange(group, controls, first, last);
    };
    start.addEventListener('input', () => update(start));
    end.addEventListener('input', () => update(end));
    controls.append(start, end, value);
    if (group) {
        controls.dataset.zoomGroup = group;
        /** @type {any} */ ((controls) as any)._applyZoomRange = applyRange;
    }
    container.appendChild(controls);
}

/**
 * Pushes one chart's zoom range onto every other slider in its `ZOOM_GROUP`. Peers apply the
 * range directly rather than through their own input handler, so a group never echoes.
 */
function broadcastZoomRange(group: string, origin: HTMLElement, first: number, last: number): void {
    document.querySelectorAll('.native-chart-zoom-slider').forEach(peer => {
        if (peer === origin) return;
        if (/** @type {HTMLElement} */ ((peer) as HTMLElement).dataset.zoomGroup !== group) return;
        const apply = /** @type {any} */ ((peer) as any)._applyZoomRange;
        if (typeof apply === 'function') apply(first, last);
    });
}

export function nativeLayoutTier(layout?: ChartLayout | null, containerWidth?: number | string | null): string | null {
    const width = Number(containerWidth);
    if (!layout || !Number.isFinite(width) || width <= 0) return null;
    const compactMax = Number(layout.compactMaxWidth);
    const standardMax = Number(layout.standardMaxWidth);
    if (!Number.isFinite(compactMax) || !Number.isFinite(standardMax)) return null;
    if (width <= compactMax) return 'COMPACT';
    if (width <= standardMax) return 'STANDARD';
    return 'WIDE';
}

export function observeNativeLayout(wrapper: HTMLElement, visual: ChartVisual): void {
    if (!visual.layout || !isWebMode || vscode || isOfflineSnapshot()
        || typeof ResizeObserver !== 'function') return;

    const visualName = String(visual.name || '');
    if (!visualName) return;
    const layout = visual.layout;
    const observer = new ResizeObserver(entries => {
        const width = entries[entries.length - 1]?.contentRect?.width;
        const tier = nativeLayoutTier(layout, width);
        if (!tier || tier === String(layout.tier || '').toUpperCase()) return;

        const pending = _nativeLayoutRequests.get(visualName);
        if (pending?.tier === tier) return;
        const oldTimer = _nativeLayoutTimers.get(visualName);
        if (oldTimer) clearTimeout(oldTimer);
        _nativeLayoutTimers.set(visualName, setTimeout(() => {
            _nativeLayoutTimers.delete(visualName);
            requestNativeLayout(visualName, tier);
        }, 180));
    });
    observer.observe(wrapper);
    _nativeLayoutObservers.push(observer);
}

function findVisualInManifest(m?: ChartManifest | null, name?: string | null): ChartVisual | null {
    if (!m || !name) return null;
    const lower = name.toLowerCase();
    const top = (m.visuals || []).find(v => (v.name || '').toLowerCase() === lower);
    if (top) return top;
    if (m.pages) {
        for (const p of m.pages) {
            const pv = (p.visuals || []).find(v => (v.name || '').toLowerCase() === lower);
            if (pv) return pv;
        }
    }
    return null;
}

function updateNativeVisualInPlace(card: HTMLElement, visual: ChartVisual): boolean {
    const wrapper = card.querySelector('.native-chart-wrapper');
    if (!wrapper) return false;

    const parsed = new DOMParser().parseFromString(String(visual.nativeSvg || ''), 'image/svg+xml');
    const newSvg = parsed.documentElement;
    if (!newSvg || newSvg.nodeName.toLowerCase() !== 'svg' || parsed.querySelector('parsererror')) {
        return false;
    }

    const oldSvg = wrapper.querySelector('svg');
    if (oldSvg) {
        wrapper.replaceChild(document.importNode(newSvg, true), oldSvg);
    } else {
        wrapper.appendChild(document.importNode(newSvg, true));
    }

    if (visual.layout?.tier) {
        /** @type {HTMLElement} */ ((wrapper) as HTMLElement).dataset.layoutTier = String(visual.layout.tier).toUpperCase();
    }

    const interaction = resolveInteraction(visual);
    applyNativeHighlight(/** @type {HTMLElement} */ ((wrapper) as HTMLElement), visual, interaction);

    const vopts = visual.options || {};
    const crosshairOpt = (vopts['CROSSHAIR'] || '').toUpperCase();
    const linkTooltipGroup = vopts['LINK_TOOLTIP'] ? vopts['LINK_TOOLTIP'].trim() : null;
    const svgEl = wrapper.querySelector('svg');
    if (svgEl && (crosshairOpt === 'ON' || crosshairOpt === 'TRUE' || vopts['CROSSHAIR_AXIS'] || linkTooltipGroup)) {
        let crosshairG = svgEl.querySelector('.plot-crosshair-group');
        if (!crosshairG) {
            crosshairG = document.createElementNS('http://www.w3.org/2000/svg', 'g');
            crosshairG.setAttribute('class', 'plot-crosshair-group');
            crosshairG.setAttribute('pointer-events', 'none');
            /** @type {HTMLElement} */ ((crosshairG) as unknown as HTMLElement).style.display = 'none';
            svgEl.appendChild(crosshairG);
        }
    }

    return true;
}

async function requestNativeLayout(visualName: string, tier: string): Promise<void> {
    const previous = _nativeLayoutRequests.get(visualName);
    if (previous?.tier === tier) return;
    if (previous) previous.controller.abort();

    const controller = new AbortController();
    _nativeLayoutRequests.set(visualName, { tier, controller });
    try {
        const response = await fetch(apiBase + '/layout', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ visualName, tier }),
            signal: controller.signal
        });
        if (!response.ok) return;
        const manifest = (await response.json()) as ChartManifest;
        if (_nativeLayoutRequests.get(visualName)?.controller !== controller) return;

        const newVisual = findVisualInManifest(manifest, visualName);
        const targetVisual = findVisualInManifest(getLastManifest() as ChartManifest, visualName);
        const cards = document.querySelectorAll('.visual-card');
        const card = Array.from(cards).find(c => (c.getAttribute('data-name') || '').toLowerCase() === visualName.toLowerCase());

        let updatedInPlace = false;
        if (newVisual && targetVisual && card) {
            targetVisual.nativeSvg = newVisual.nativeSvg;
            if (targetVisual.layout && newVisual.layout) {
                targetVisual.layout.tier = newVisual.layout.tier;
            }
            if (window.__CURRENT_MANIFEST__) {
                const currVisual = findVisualInManifest(window.__CURRENT_MANIFEST__ as ChartManifest, visualName);
                if (currVisual) {
                    currVisual.nativeSvg = newVisual.nativeSvg;
                    if (currVisual.layout && newVisual.layout) {
                        currVisual.layout.tier = newVisual.layout.tier;
                    }
                }
            }
            updatedInPlace = updateNativeVisualInPlace(/** @type {HTMLElement} */ ((card) as HTMLElement), targetVisual);
        }

        if (!updatedInPlace) {
            renderManifest(manifest);
        }
    } catch (error: any) {
        if (error?.name !== 'AbortError') console.warn('Native chart layout refresh failed:', error);
    } finally {
        if (_nativeLayoutRequests.get(visualName)?.controller === controller)
            _nativeLayoutRequests.delete(visualName);
    }
}

// Draws the current selection over the unselected universe. Which treatment applies is a server
// decision carried on `interaction.highlight`; where a mark's value extent lies is a server
// decision carried on the mark's own `data-extent-axis`/`data-extent-anchor`. Neither is
// inferred here from a chart type.
function applyNativeHighlight(wrapper: HTMLElement, visual: ChartVisual, interaction: ChartInteraction): void {
    if (!Array.isArray(visual.highlightRows)) return;
    const columns = visual.columns || [];
    const mappingIndex = columns.findIndex(column =>
        column.toLowerCase() === String(interaction.key || '').toLowerCase());
    const valueIndex = columns.findIndex(column =>
        column.toLowerCase() === String(interaction.valueKey || '').toLowerCase());
    const rowKey = (row: ChartRow) => mappingIndex >= 0
        ? String(row?.[mappingIndex] ?? '')
        : JSON.stringify(row || []);
    const highlighted = new Set(visual.highlightRows.map(rowKey));

    const markExtent = (mark: SVGElement) => {
        const axis = mark.dataset.extentAxis || (interaction.extent && interaction.extent.axis);
        if (!axis) return null;
        return {
            axis: String(axis).toLowerCase(),
            anchor: String(mark.dataset.extentAnchor ||
                (interaction.extent && interaction.extent.anchor) || 'start').toLowerCase()
        };
    };

    if (interaction.highlight === 'PROPORTIONAL' && valueIndex >= 0) {
        const selectedValues = new Map<string, number>();
        visual.highlightRows.forEach(row => {
            const value = Number.parseFloat(String(row?.[valueIndex] ?? ''));
            if (!Number.isFinite(value)) return;
            const key = rowKey(row);
            selectedValues.set(key, (selectedValues.get(key) || 0) + value);
        });
        let drewProportional = false;
        wrapper.querySelectorAll<SVGElement>('rect[data-row-index]').forEach(mark => {
            const extent = markExtent(mark);
            if (!extent) return;
            drewProportional = true;
            const row = (visual.rows || [])[Number(mark.dataset.rowIndex)] || [];
            const universeValue = Number.parseFloat(String(row?.[valueIndex] ?? ''));
            const selectedValue = selectedValues.get(rowKey(row));
            mark.classList.add('cross-highlight-universe');
            if (!Number.isFinite(universeValue) || selectedValue === undefined) return;

            const ratio = universeValue === 0 ? 0 : Math.max(0, Math.min(1, selectedValue / universeValue));
            const overlay = mark.cloneNode(false) as SVGElement;
            overlay.removeAttribute('data-row-index');
            overlay.removeAttribute('data-extent-axis');
            overlay.removeAttribute('data-extent-anchor');
            overlay.classList.remove('cross-highlight-universe');
            overlay.classList.add('cross-highlight-selection');
            overlay.setAttribute('pointer-events', 'none');
            overlay.setAttribute('aria-hidden', 'true');
            if (extent.axis === 'y') {
                const fullHeight = Number.parseFloat(String(mark.getAttribute('height') || '')) || 0;
                const fullY = Number.parseFloat(String(mark.getAttribute('y') || '')) || 0;
                overlay.setAttribute('height', String(fullHeight * ratio));
                // An `end` anchor puts the baseline at the high edge of the axis, so the
                // selected share hugs the far edge of the mark rather than its own origin.
                if (extent.anchor === 'end') overlay.setAttribute('y', String(fullY + fullHeight * (1 - ratio)));
            } else {
                const fullWidth = Number.parseFloat(String(mark.getAttribute('width') || '')) || 0;
                const fullX = Number.parseFloat(String(mark.getAttribute('x') || '')) || 0;
                overlay.setAttribute('width', String(fullWidth * ratio));
                if (extent.anchor === 'end') overlay.setAttribute('x', String(fullX + fullWidth * (1 - ratio)));
            }
            if (mark.parentNode) {
                mark.parentNode.insertBefore(overlay, mark.nextSibling);
            }
        });
        // No mark declared a value extent, so there is nothing to draw a share inside of.
        // Fall through to the categorical treatment rather than leaving the selection invisible.
        if (drewProportional) return;
    }

    wrapper.querySelectorAll<SVGElement>('[data-row-index]').forEach(mark => {
        const row = (visual.rows || [])[Number(mark.dataset.rowIndex)] || [];
        const selected = highlighted.has(rowKey(row));
        mark.classList.toggle('cross-highlighted', selected);
        mark.classList.toggle('cross-dimmed', !selected);
    });
}

export function getNativeLayoutObservers(): ResizeObserver[] { return _nativeLayoutObservers; }
export function setNativeLayoutObservers(value: ResizeObserver[]): void { _nativeLayoutObservers = value; }
