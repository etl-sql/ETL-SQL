/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Detail surface placement, focus, announcements, and teardown.
 */
import { formatValue } from './rt-util.js';
import { renderContainer } from './rt-layout.js';
import { renderVisual } from './rt-visual.js';
import { postParameters } from './rt-transport.js';


// ════════════════════════════════════════════════════════════════════════
// Detail surfaces
//
// One controller, shared by every chart adapter. Adapters call
// attachDetailSurface(); they never keep private tooltip state, so dismissal,
// focus, pinning, and stale-refresh fencing behave identically everywhere.
//
// Two surfaces, chosen by manifest `mode` (older manifests without it fall back
// to deriving from `type`):
//
//   tooltip  — transient, non-interactive text. role="tooltip" +
//              aria-describedby on the mark. Never focusable, never contains
//              interactive descendants, dismissed on pointer leave or blur.
//   popover  — persistent detail carrying formatted content or visuals. A fine
//              pointer gets an unpinned preview; click, tap, Enter, or Space
//              pins a labelled dialog that survives pointer leave and is
//              dismissed only by Escape, outside click, trigger toggle, opening
//              another surface, or refresh/unmount.
// ════════════════════════════════════════════════════════════════════════

export const DETAIL_VIEWPORT_MARGIN = 8;   // px kept clear at every viewport edge
export const DETAIL_ANCHOR_GAP = 10;       // px between the mark and the surface
const DETAIL_HOVER_DELAY = 120;     // ms before a hover preview requests detail
export const DETAIL_PREFERRED_SIDE = 'top';

// Flip order per preferred side: the opposite side first, then the perpendicular
// pair. Deterministic, so a given anchor/viewport always resolves the same way.
const DETAIL_FLIP_ORDER = {
    top: ['top', 'bottom', 'right', 'left'],
    bottom: ['bottom', 'top', 'right', 'left'],
    right: ['right', 'left', 'top', 'bottom'],
    left: ['left', 'right', 'top', 'bottom']
};

/**
 * Anchor-based placement. Pure: given an anchor rect, the surface size, and the
 * viewport, it returns the chosen side and the clamped position. Replaces the old
 * cursor-follow clamping so placement is reproducible in geometry fixtures.
 *
 * @param {{left:number,top:number,right:number,bottom:number}} anchor
 * @param {{width:number,height:number}} size
 * @param {{width:number,height:number}} viewport
 * @param {{preferredSide?:string, gap?:number, margin?:number, rtl?:boolean}} [options]
 * @returns {{side:string, left:number, top:number, flipped:boolean, shifted:boolean}}
 */
export function computeDetailPlacement(anchor, size, viewport, options) {
    const opts = options || {};
    const gap = opts.gap == null ? DETAIL_ANCHOR_GAP : opts.gap;
    const margin = opts.margin == null ? DETAIL_VIEWPORT_MARGIN : opts.margin;
    const preferred = DETAIL_FLIP_ORDER[opts.preferredSide] ? opts.preferredSide : DETAIL_PREFERRED_SIDE;
    const order = DETAIL_FLIP_ORDER[preferred];
    const rtl = !!opts.rtl;

    const minLeft = margin;
    const maxLeft = Math.max(margin, viewport.width - size.width - margin);
    const minTop = margin;
    const maxTop = Math.max(margin, viewport.height - size.height - margin);

    // Cross-axis alignment. Horizontally aligned surfaces align to the anchor's
    // leading edge, which is the right edge under RTL.
    const alignLeft = rtl
        ? anchor.right - size.width
        : anchor.left;
    const alignTop = anchor.top + (anchor.bottom - anchor.top) / 2 - size.height / 2;

    function candidate(side) {
        switch (side) {
            case 'top': return { left: alignLeft, top: anchor.top - size.height - gap };
            case 'bottom': return { left: alignLeft, top: anchor.bottom + gap };
            case 'right': return { left: anchor.right + gap, top: alignTop };
            default: return { left: anchor.left - size.width - gap, top: alignTop };
        }
    }

    function fits(side, pos) {
        // Only the placement axis decides fit; the cross axis is always shifted
        // into view, so a surface never leaves the viewport on that axis.
        if (side === 'top') return pos.top >= margin;
        if (side === 'bottom') return pos.top + size.height <= viewport.height - margin;
        if (side === 'right') return pos.left + size.width <= viewport.width - margin;
        return pos.left >= margin;
    }

    let chosen = null;
    for (let i = 0; i < order.length; i++) {
        const pos = candidate(order[i]);
        if (fits(order[i], pos)) { chosen = { side: order[i], pos: pos }; break; }
    }

    // Nothing fits (oversized content, or a very small viewport): keep the
    // preferred side and let the clamp below place it deterministically.
    if (!chosen) chosen = { side: preferred, pos: candidate(preferred) };

    const left = Math.min(Math.max(chosen.pos.left, minLeft), maxLeft);
    const top = Math.min(Math.max(chosen.pos.top, minTop), maxTop);

    return {
        side: chosen.side,
        left: left,
        top: top,
        flipped: chosen.side !== preferred,
        shifted: left !== chosen.pos.left || top !== chosen.pos.top
    };
}

/** Normalises the manifest tooltip into the accepted surface contract. */
function detailSurfaceMode(tooltip) {
    if (!tooltip || typeof tooltip !== 'object') return null;
    if (tooltip.mode === 'popover' || tooltip.mode === 'tooltip') return tooltip.mode;
    // Manifests published before `mode` existed: container and inline-with-visuals
    // carry visuals, so they are popovers; everything else is a transient tooltip.
    if (tooltip.type === 'container') return 'popover';
    if (tooltip.type === 'inline' && (tooltip.visuals || []).length > 0) return 'popover';
    return 'tooltip';
}

// ── Shared announcement region ──────────────────────────────────────────
// One polite region for every surface. Repeated identical messages are dropped
// so a hover sweep across many marks cannot flood a screen reader.

let detailLiveRegion = null;
let detailLastAnnouncement = '';

function announceDetail(message) {
    if (!message || message === detailLastAnnouncement) return;
    detailLastAnnouncement = message;
    if (!detailLiveRegion) {
        detailLiveRegion = document.createElement('div');
        detailLiveRegion.id = 'report-detail-live';
        detailLiveRegion.className = 'report-detail-live';
        detailLiveRegion.setAttribute('role', 'status');
        detailLiveRegion.setAttribute('aria-live', 'polite');
        document.body.appendChild(detailLiveRegion);
    }
    detailLiveRegion.textContent = message;
}

// ── The single open surface ─────────────────────────────────────────────
// Opening a surface closes whatever was open, so only one detail surface exists
// in the document at any time.

let openDetail = null;

export function closeOpenDetail(restoreFocus) {
    if (!openDetail) return;
    const closing = openDetail;
    openDetail = null;
    closing.generation++;
    if (closing.element && closing.element.parentNode) closing.element.remove();
    if (closing.trigger) {
        closing.trigger.removeAttribute('aria-describedby');
        closing.trigger.setAttribute('aria-expanded', 'false');
    }
    detailLastAnnouncement = '';
    window.removeEventListener('scroll', closing.reposition, true);
    window.removeEventListener('resize', closing.reposition);
    if (closing.resizeObserver) closing.resizeObserver.disconnect();
    if (restoreFocus && closing.pinned && closing.trigger && document.contains(closing.trigger)) {
        closing.trigger.focus();
    }
}

/**
 * Adds the non-hoverable fallback note for a visual's detail surface.
 *
 * The note is hidden on screen — the live surface is what a browser reader uses — but is
 * present in the accessibility tree and revealed when printing, so a browser-printed PDF
 * carries the same semantic summary the static exporters emit. The wording comes from the
 * manifest (`staticSummary`), computed once server-side, so the two cannot drift.
 *
 * @param {HTMLElement} container the visual's card
 * @param {object} visual the visual manifest entry
 */
export function appendDetailStaticNote(container, visual) {
    const summary = visual.tooltip && visual.tooltip.staticSummary;
    if (!summary) return;
    const note = document.createElement('p');
    note.className = 'report-detail-static-note';
    note.textContent = summary;
    container.appendChild(note);
}

/**
 * Destroys every detail surface attached beneath `scope`. The surface element is
 * appended to document.body, so clearing the report root would otherwise orphan an
 * open popover; this must run before any bulk re-render or unmount.
 *
 * @param {ParentNode} scope
 */
export function destroyDetailSurfaces(scope) {
    if (!scope || typeof scope.querySelectorAll !== 'function') return;
    scope.querySelectorAll('.chart-wrapper').forEach(wrapper => {
        const handle = /** @type {EtlSqlVisualHost} */ (wrapper)._detailSurface;
        if (handle && typeof handle.destroy === 'function') handle.destroy();
        /** @type {EtlSqlVisualHost} */ (wrapper)._detailSurface = null;
    });
}

/**
 * Attaches the shared detail-surface controller to one rendered visual.
 *
 * @param {HTMLElement} wrapper   the chart wrapper containing [data-row-index] marks
 * @param {object} visual         the visual manifest entry
 * @param {object} manifest       the report manifest
 * @param {string} pageTheme      resolved page theme
 * @param {string} mappingColumn  column whose value becomes the row context
 * @returns {{destroy: function}} handle used to tear the surface down on refresh/unmount
 */
export function attachDetailSurface(wrapper, visual, manifest, pageTheme, mappingColumn) {
    const tooltip = visual.tooltip;
    const mode = detailSurfaceMode(tooltip);
    if (!mode) return { destroy: function () { } };

    const surfaceId = 'detail-' + Math.random().toString(36).slice(2, 10);
    const isPopover = mode === 'popover';
    const columns = visual.columns || [];
    const rows = visual.rows || [];
    const columnIndex = columns.findIndex(column =>
        String(column).toLowerCase() === String(mappingColumn || '').toLowerCase());

    let hoverTimer = null;
    let localGeneration = 0;
    let destroyed = false;

    function rowContext(mark) {
        const rowIndex = Number(mark.dataset.rowIndex);
        if (!Number.isInteger(rowIndex)) return null;
        const row = rows[rowIndex] || [];
        return String(row[columnIndex >= 0 ? columnIndex : 0] ?? '');
    }

    // ── Marks become discoverable and focusable ─────────────────────────
    // Hover-only detail is not acceptable, so every mark that exposes detail is
    // reachable by keyboard and carries an accessible name.
    function prepareMarks() {
        wrapper.querySelectorAll('[data-row-index]').forEach(mark => {
            if (/** @type {HTMLElement} */ (mark).dataset.detailReady === '1') return;
            /** @type {HTMLElement} */ (mark).dataset.detailReady = '1';
            mark.setAttribute('tabindex', '0');
            mark.setAttribute('role', 'button');
            const context = rowContext(mark);
            const title = visual.title || visual.name || 'chart';
            mark.setAttribute('aria-label', isPopover
                ? `${context} — show details for ${title}`
                : `${context} — ${title}`);
            if (isPopover) {
                mark.setAttribute('aria-haspopup', 'dialog');
                mark.setAttribute('aria-expanded', 'false');
            }
        });
    }
    prepareMarks();

    function buildSurfaceElement(pinned) {
        const el = document.createElement('div');
        el.className = 'report-chart-tooltip' + (pinned ? ' report-chart-detail-pinned' : '');
        el.id = surfaceId;
        if (isPopover && pinned) {
            // Interactive detail is a labelled dialog, never role="tooltip":
            // a tooltip must not contain focusable descendants.
            el.setAttribute('role', 'dialog');
            el.setAttribute('aria-modal', 'false');
            el.setAttribute('aria-label', `Details for ${visual.title || visual.name || 'chart'}`);
            el.tabIndex = -1;
        } else {
            el.setAttribute('role', 'tooltip');
        }
        return el;
    }

    function contextElement(value) {
        const context = document.createElement('div');
        context.className = 'report-chart-tooltip-context';
        context.textContent = String(value ?? '');
        return context;
    }

    function reposition(state) {
        if (!state.element || !state.trigger || !document.contains(state.trigger)) return;
        const anchor = state.trigger.getBoundingClientRect();
        const size = state.element.getBoundingClientRect();
        const rtl = getComputedStyle(document.documentElement).direction === 'rtl' ||
            getComputedStyle(document.body).direction === 'rtl';
        const placement = computeDetailPlacement(
            { left: anchor.left, top: anchor.top, right: anchor.right, bottom: anchor.bottom },
            { width: size.width, height: size.height },
            { width: window.innerWidth, height: window.innerHeight },
            { preferredSide: DETAIL_PREFERRED_SIDE, rtl: rtl });
        state.element.style.left = placement.left + 'px';
        state.element.style.top = placement.top + 'px';
        state.element.dataset.side = placement.side;
    }

    function open(mark, pinned) {
        closeOpenDetail(false);
        if (destroyed) return null;

        const element = buildSurfaceElement(pinned);
        document.body.appendChild(element);

        const state = {
            element: element,
            trigger: mark,
            pinned: pinned,
            generation: ++localGeneration,
            owner: wrapper,
            reposition: null,
            resizeObserver: null
        };
        state.reposition = () => reposition(state);
        openDetail = state;

        if (!pinned) {
            // A transient surface describes its trigger; a pinned dialog does not,
            // so assistive technology is not told to read a whole dialog inline.
            mark.setAttribute('aria-describedby', surfaceId);
        } else if (isPopover) {
            mark.setAttribute('aria-expanded', 'true');
        }

        window.addEventListener('scroll', state.reposition, true);
        window.addEventListener('resize', state.reposition);
        if (typeof ResizeObserver === 'function') {
            // Content that grows after an async refresh must not stay mis-anchored.
            state.resizeObserver = new ResizeObserver(state.reposition);
            state.resizeObserver.observe(element);
        }
        return state;
    }

    function showText(mark, pinned) {
        const state = open(mark, pinned);
        if (!state) return;
        const value = rowContext(mark);
        state.element.replaceChildren(contextElement(value));
        if (tooltip.markdown || tooltip.text) {
            const body = document.createElement('div');
            body.className = 'report-chart-tooltip-text';
            body.textContent = tooltip.text || tooltip.markdown || '';
            state.element.appendChild(body);
        }
        // The declarative middle tier: a formatted field list read straight off the hovered
        // row. No server round-trip, which is what keeps it lighter than a popover container.
        const fields = tooltip.fields || [];
        if (fields.length > 0) {
            const rowIndex = Number(mark.dataset.rowIndex);
            const row = Number.isInteger(rowIndex) ? (rows[rowIndex] || []) : [];
            const list = document.createElement('dl');
            list.className = 'report-chart-tooltip-fields';
            fields.forEach(field => {
                const index = columns.findIndex(column =>
                    String(column).toLowerCase() === String(field.name).toLowerCase());
                const term = document.createElement('dt');
                term.textContent = String(field.name);
                const detail = document.createElement('dd');
                const raw = index < 0 ? null : row[index];
                const formatted = formatValue(raw, field.format);
                detail.textContent = formatted == null ? '' : String(formatted);
                list.append(term, detail);
            });
            state.element.appendChild(list);
        }
        reposition(state);
    }

    function renderDetailContent(state, value, sourceManifest) {
        state.element.replaceChildren(contextElement(value));

        if (tooltip.type === 'container') {
            const ref = String(tooltip.containerRef || '').toLowerCase();
            const containerDef = (sourceManifest.containers || [])
                .find(item => String(item.name).toLowerCase() === ref);
            if (!containerDef) {
                appendUnavailable(state, `Details for ${value} are unavailable.`);
                return;
            }
            renderContainer(state.element, containerDef, sourceManifest, pageTheme);
            announceDetail(`Details for ${value} loaded.`);
            return;
        }

        // Inline form: optional markdown, then each named visual rendered in order.
        // This is the path that previously parsed and serialized but rendered nothing.
        if (tooltip.markdown) {
            const md = document.createElement('div');
            md.className = 'report-chart-tooltip-text';
            md.textContent = tooltip.markdown;
            state.element.appendChild(md);
        }

        const names = tooltip.visuals || [];
        const byName = new Map((sourceManifest.visuals || [])
            .map(v => [String(v.name).toLowerCase(), v]));
        let rendered = 0;
        names.forEach(name => {
            const def = byName.get(String(name).toLowerCase());
            if (!def) return;
            renderVisual(state.element, def, pageTheme, sourceManifest);
            rendered++;
        });

        if (!rendered && !tooltip.markdown) {
            appendUnavailable(state, `Details for ${value} are unavailable.`);
            return;
        }
        announceDetail(`Details for ${value} loaded.`);
    }

    function appendUnavailable(state, message) {
        const unavailable = document.createElement('div');
        unavailable.className = 'report-chart-tooltip-loading';
        unavailable.textContent = message;
        state.element.appendChild(unavailable);
        announceDetail(message);
    }

    function showDetail(mark, pinned) {
        const state = open(mark, pinned);
        if (!state) return;
        const value = rowContext(mark);
        const generation = state.generation;

        state.element.replaceChildren(contextElement(value));
        const loading = document.createElement('div');
        loading.className = 'report-chart-tooltip-loading';
        loading.textContent = 'Loading details…';
        state.element.appendChild(loading);
        reposition(state);
        announceDetail(`Loading details for ${value}.`);

        if (pinned) state.element.focus();

        // Only the explicitly mapped, non-secret row value flows into the refresh.
        postParameters({ '@hover_value': value }, true).then(refreshed => {
            // Generation fencing: a response for a superseded row, a closed surface,
            // or a destroyed attachment never replaces current detail.
            if (destroyed || openDetail !== state || state.generation !== generation) return;
            renderDetailContent(state, value, refreshed || manifest);
            reposition(state);
        }).catch(() => {
            if (destroyed || openDetail !== state || state.generation !== generation) return;
            state.element.replaceChildren(contextElement(value));
            appendUnavailable(state, `Details for ${value} could not be loaded.`);
            reposition(state);
        });
    }

    function showFor(mark, pinned) {
        if (isPopover) showDetail(mark, pinned);
        else showText(mark, pinned);
    }

    function isOwnTrigger(mark) {
        return openDetail && openDetail.trigger === mark;
    }

    // ── Pointer: hover previews, click pins ─────────────────────────────

    function onPointerOver(event) {
        // Coarse pointers get no hover preview; tap opens the pinned surface instead.
        if (event.pointerType === 'touch') return;
        const mark = event.target.closest('[data-row-index]');
        if (!mark || !wrapper.contains(mark)) return;
        if (openDetail && openDetail.pinned) return; // a pinned surface owns the screen
        if (isOwnTrigger(mark)) return;
        if (hoverTimer) clearTimeout(hoverTimer);
        if (isPopover) hoverTimer = setTimeout(() => showFor(mark, false), DETAIL_HOVER_DELAY);
        else showFor(mark, false);
    }

    function onPointerLeave() {
        if (hoverTimer) { clearTimeout(hoverTimer); hoverTimer = null; }
        // Deterministic: an unpinned surface never bridges to the pointer, so leaving
        // the mark always dismisses it. A pinned surface is unaffected.
        if (openDetail && !openDetail.pinned && openDetail.owner === wrapper) closeOpenDetail(false);
    }

    function onClick(event) {
        const mark = event.target.closest('[data-row-index]');
        if (!mark || !wrapper.contains(mark)) return;
        if (hoverTimer) { clearTimeout(hoverTimer); hoverTimer = null; }
        // Trigger reactivation toggles consistently for both surfaces.
        if (openDetail && openDetail.pinned && openDetail.trigger === mark) {
            closeOpenDetail(true);
            return;
        }
        showFor(mark, true);
    }

    function onKeyDown(event) {
        if (event.key !== 'Enter' && event.key !== ' ' && event.key !== 'Spacebar') return;
        const mark = event.target.closest('[data-row-index]');
        if (!mark || !wrapper.contains(mark)) return;
        event.preventDefault(); // Space must not scroll the page
        if (openDetail && openDetail.pinned && openDetail.trigger === mark) {
            closeOpenDetail(true);
            return;
        }
        showFor(mark, true);
    }

    function onFocusIn(event) {
        const mark = event.target.closest('[data-row-index]');
        if (!mark || !wrapper.contains(mark)) return;
        if (openDetail && openDetail.pinned) return;
        if (isOwnTrigger(mark)) return;
        // Keyboard focus shows the transient surface; activation pins it.
        if (!isPopover) showFor(mark, false);
    }

    function onFocusOut(event) {
        if (!openDetail || openDetail.pinned || openDetail.owner !== wrapper) return;
        if (event.relatedTarget && wrapper.contains(event.relatedTarget)) return;
        closeOpenDetail(false);
    }

    wrapper.addEventListener('pointerover', onPointerOver);
    wrapper.addEventListener('pointerleave', onPointerLeave);
    wrapper.addEventListener('click', onClick);
    wrapper.addEventListener('keydown', onKeyDown);
    wrapper.addEventListener('focusin', onFocusIn);
    wrapper.addEventListener('focusout', onFocusOut);

    return {
        /** Closes any surface this attachment owns and detaches every listener. */
        destroy: function () {
            destroyed = true;
            if (hoverTimer) { clearTimeout(hoverTimer); hoverTimer = null; }
            localGeneration++;
            if (openDetail && openDetail.owner === wrapper) closeOpenDetail(false);
            wrapper.removeEventListener('pointerover', onPointerOver);
            wrapper.removeEventListener('pointerleave', onPointerLeave);
            wrapper.removeEventListener('click', onClick);
            wrapper.removeEventListener('keydown', onKeyDown);
            wrapper.removeEventListener('focusin', onFocusIn);
            wrapper.removeEventListener('focusout', onFocusOut);
        }
    };
}

export function getOpenDetail() { return openDetail; }
export function setOpenDetail(value) { openDetail = value; }

