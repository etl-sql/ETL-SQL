// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/report-runtime.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report bootstrap, manifest rendering, and host event wiring.
 */
import { getExportReadyPromise, getPendingLazyRows, markExportNotReady, markExportReady, publishExportState } from './rt-data.js';
import { _crossFilterStates, apiBase, getBaselineManifest, getLastActivePage, getLastManifest, getRefreshTimers, isWebMode, parameters, setBaselineManifest, setLastManifest, setRefreshTimers, vscode } from './rt-state.js';
import { abbreviateNumber, escHtml, getParam, getStyle, isOn } from './rt-util.js';
import { applyBookmark, applySavedView, applyUserDefaultSavedView, buildViewsPicker, captureResolvedState, isOfflineSnapshot, parseStateHash, savedViewsBase } from './rt-views.js';
import { renderAutoPanel, renderFooter, renderHeader, renderNavBar, renderPipelineConsole, showRequiredParametersModal } from './rt-chrome.js';
import { _nativeLayoutTimers, getNativeLayoutObservers, nativeLayoutTier, observeNativeLayout, renderNativeSvg, setNativeLayoutObservers } from './rt-charts.js';
import { DESIGN_TOKENS, applyDesignTokens, getDefaultTheme, isAllowedTokenName, isSafeCssValue, resolveDesignTokens, updateBodyTheme } from './rt-theme.js';
import { renderPage } from './rt-layout.js';
import { closeMaximizedVisual, renderCard, renderVisual, resizeChartsIn } from './rt-visual.js';
import { DETAIL_ANCHOR_GAP, DETAIL_PREFERRED_SIDE, DETAIL_VIEWPORT_MARGIN, closeOpenDetail, computeDetailPlacement, destroyDetailSurfaces, getOpenDetail } from './rt-detail.js';
import { reApplyCrossFilterStyling } from './rt-actions.js';
import { renderDatePicker } from './rt-controls-date.js';
import { renderButton, renderSearch, renderSlider } from './rt-controls-input.js';
window.__etlSqlReportWhenExportReady = function (timeoutMs) {
    const timeout = Number(timeoutMs || 0);
    if (window.__etlSqlReportExportReady) {
        return Promise.resolve(window.__etlSqlReportExportState);
    }
    if (timeout <= 0)
        return getExportReadyPromise();
    return Promise.race([
        getExportReadyPromise(),
        new Promise((_, reject) => {
            setTimeout(() => reject(new Error('Report export readiness timed out.')), timeout);
        })
    ]);
};
/**
 * Entry point: obtain manifest and render all visuals + pages.
 */
async function boot() {
    markExportNotReady('boot');
    if (window.__IS_PREVIEW__) {
        document.body.classList.add('preview-mode');
    }
    let manifest;
    if (window.__MANIFEST__) {
        // Pre-embedded (single-report web mode or VS Code preview)
        manifest = window.__MANIFEST__;
    }
    else if (isWebMode) {
        // Multi-report web mode: fetch from API
        try {
            const qs = window.location.search;
            const res = await fetch(apiBase + '/manifest' + qs);
            manifest = (await res.json());
        }
        catch (e) {
            const err = e;
            const root = document.getElementById('root');
            if (root) {
                root.innerHTML = '<p class="error">Failed to load manifest: ' + escHtml(err.message || String(e)) + '</p>';
            }
            publishExportState('error', { reason: 'manifest-load-failed', message: err.message || String(e) });
            return;
        }
    }
    else {
        const root = document.getElementById('root');
        if (root) {
            root.innerHTML = '<p class="error">No manifest available.</p>';
        }
        publishExportState('error', { reason: 'manifest-missing' });
        return;
    }
    renderManifest(manifest);
    // Launch precedence (exact order):
    //   1. explicit #bookmark=Name
    //   2. explicit #view=SavedViewId
    //   3. user's default Portal saved view
    //   4. author default bookmark (DEFAULT = ON)
    //   5. declared parameter/navigation defaults (already applied by renderManifest)
    await applyLaunchPrecedence(manifest);
    // A launch bookmark/view may be the source of a REQUIRED parameter. Validate only after
    // launch precedence has had the opportunity to apply it atomically.
    const launchedManifest = getLastManifest() || manifest;
    if (!checkRequiredParameters(launchedManifest)) {
        publishExportState('blocked', { reason: 'required-parameters' });
        return;
    }
    // Identifier-only hash replay. URLs and history carry only an identifier — never parameter,
    // filter, search, drill, or presentation values.
    window.addEventListener('hashchange', () => {
        const parsed = parseStateHash(window.location.hash);
        if (parsed.bookmark)
            applyBookmark(parsed.bookmark);
        else if (parsed.view)
            applySavedView(parsed.view);
    });
}
async function applyLaunchPrecedence(manifest) {
    const parsed = parseStateHash(window.location.hash);
    if (parsed.bookmark) {
        if (await applyBookmark(parsed.bookmark))
            return true;
    }
    if (parsed.view) {
        if (await applySavedView(parsed.view))
            return true;
    }
    // User's default Portal saved view (Portal mode only). A stale/unknown default must never
    // prevent the base report from opening, so failure falls through to the author default.
    if (await applyUserDefaultSavedView())
        return true;
    if (manifest.bookmarks) {
        const authorDefault = manifest.bookmarks.find(b => b.isDefault);
        if (authorDefault && await applyBookmark(authorDefault.name))
            return true;
    }
    // else: declared parameter/navigation defaults already applied by renderManifest.
    return false;
}
function checkRequiredParameters(manifest) {
    if (!manifest.parameterMetadata)
        return true;
    const missing = [];
    const required = [];
    for (const name in manifest.parameterMetadata) {
        const meta = manifest.parameterMetadata[name];
        if (meta && meta.isRequired) {
            required.push(meta);
            const val = getParam(manifest.parameters, name);
            if (val === undefined || val === null || val === "" || val === "null") {
                missing.push(meta);
            }
        }
    }
    if (missing.length > 0) {
        // Show all REQUIRED parameters in the modal, not just the missing ones,
        // to provide full context to the user.
        showRequiredParametersModal(required, manifest);
        return false;
    }
    return true;
}
export function renderManifest(inputManifest) {
    const manifest = (inputManifest || {});
    markExportNotReady('render-manifest');
    setLastManifest(manifest);
    // Cancel any running per-page auto-refresh timers before rebuilding.
    getRefreshTimers().forEach((id) => clearInterval(id));
    setRefreshTimers([]);
    getNativeLayoutObservers().forEach((observer) => observer.disconnect());
    setNativeLayoutObservers([]);
    _nativeLayoutTimers.forEach((id) => clearTimeout(id));
    _nativeLayoutTimers.clear();
    const root = document.getElementById('root');
    if (!root) {
        publishExportState('error', { reason: 'root-missing' });
        return;
    }
    const frag = document.createDocumentFragment();
    renderHeader(frag, manifest);
    // Cache baseline manifest (the first one with no parameters set)
    if (!getBaselineManifest() && (!manifest.parameters || Object.keys(manifest.parameters).length === 0)) {
        setBaselineManifest(JSON.parse(JSON.stringify(manifest)));
    }
    window.__CURRENT_MANIFEST__ = manifest;
    // Update local parameters from manifest
    if (manifest.parameters) {
        Object.keys(manifest.parameters).forEach(k => {
            parameters[k] = manifest.parameters[k];
        });
    }
    // Navigation bar
    const navDef = manifest.navigations && manifest.navigations.length > 0
        ? manifest.navigations[0] : null;
    let activePageName = null;
    if (manifest.pages && manifest.pages.length > 0) {
        const firstVisible = manifest.pages.find(p => !p.isHidden);
        const defaultPage = navDef
            ? (navDef.defaultPage || (firstVisible && firstVisible.name) || null)
            : ((firstVisible && firstVisible.name) || null);
        const requestedPage = String(getLastActivePage() || window.__INITIAL_PAGE__ || '').trim();
        activePageName = (requestedPage && manifest.pages.some(p => p.name === requestedPage)) ? requestedPage : defaultPage;
    }
    updateBodyTheme(manifest, activePageName);
    /** @type {Record<string, HTMLElement>} */
    const pageSections = {};
    let effectiveActivePage = null;
    if (manifest.pages && manifest.pages.length > 0) {
        const firstVisible = manifest.pages.find(p => !p.isHidden);
        const defaultPageName = navDef
            ? (navDef.defaultPage || (firstVisible && firstVisible.name) || null)
            : ((firstVisible && firstVisible.name) || null);
        effectiveActivePage = activePageName || defaultPageName;
        manifest.pages.forEach(page => {
            const reportStyles = manifest.styles || {};
            const pageStyles = page.styles || {};
            const pageTheme = getStyle(pageStyles, 'THEME') || getStyle(reportStyles, 'THEME') || getDefaultTheme(manifest);
            const section = renderPage(manifest, page, pageSections, pageTheme);
            frag.appendChild(section);
            // Hidden pages start invisible; active page starts visible with active class
            const isPageActive = !page.isHidden && (page.name === effectiveActivePage);
            if (isPageActive) {
                section.style.display = 'block';
                section.classList.add('active');
            }
            else {
                section.style.display = 'none';
                section.classList.remove('active');
            }
        });
        if (navDef) {
            renderNavBar(frag, navDef, pageSections, manifest.pages, manifest);
        }
        else if (manifest.pages.length > 0) {
            // No navigation — show the first page by default
            const firstPage = manifest.pages.find(p => !p.isHidden) || manifest.pages[0];
            const section = pageSections[firstPage.name];
            if (section) {
                section.style.display = 'block';
                section.classList.add('active');
            }
        }
    }
    else {
        (manifest.visuals || []).forEach(v => renderVisual(frag, v, getDefaultTheme(manifest), manifest));
    }
    destroyDetailSurfaces(root); // Close detail surfaces before their marks disappear.
    root.replaceChildren(frag); // Atomic swap to eliminate white flash!
    if (manifest.pages && manifest.pages.length > 0) {
        const activeSection = effectiveActivePage ? pageSections[effectiveActivePage] : null;
        if (activeSection) {
            resizeChartsIn(activeSection);
        }
    }
    // Synchronize parameter values to any newly rendered controls
    if (manifest.parameters) {
        syncParameters(manifest.parameters);
    }
    // Cross-filter state management across re-renders:
    // - Non-interaction rebuild (slicer/param change): clear all selection state.
    // - Interaction rebuild (chart click): re-apply dimming/source CSS so the visual
    //   feedback survives the full DOM rebuild that renderManifest does.
    const hasCrossHighlights = (manifest.visuals || []).some(visual => Array.isArray(visual.highlightRows) && visual.highlightRows.length > 0);
    if (!manifest.isInteraction && !hasCrossHighlights) {
        for (let k in _crossFilterStates)
            delete _crossFilterStates[k];
    }
    else {
        reApplyCrossFilterStyling();
    }
    // Set up per-page auto-refresh timers (web mode only; VS Code preview ignores).
    if (isWebMode && manifest.pages) {
        manifest.pages.forEach(page => {
            if (!page.refreshIntervalSeconds || page.refreshIntervalSeconds <= 0)
                return;
            const id = setInterval(() => {
                // Only refresh when the page section is visible.
                const section = document.getElementById('page-' + page.name.toLowerCase());
                if (!section || section.style.display === 'none')
                    return;
                fetch(apiBase + '/manifest')
                    .then(r => r.ok ? r.json() : null)
                    .then(m => { if (m)
                    renderManifest(m); })
                    .catch(() => { });
            }, page.refreshIntervalSeconds * 1000);
            getRefreshTimers().push(id);
        });
    }
    renderFooter(root, manifest);
    renderPipelineConsole(root, manifest);
    renderAutoPanel(root, manifest);
    if (getPendingLazyRows() === 0) {
        markExportReady(manifest);
    }
}
function syncParameters(params) {
    if (!params)
        return;
    for (const name in params) {
        const val = params[name];
        const elements = document.querySelectorAll('[data-parameter]');
        elements.forEach(el => {
            const paramKey = el.getAttribute('data-parameter');
            if (paramKey && paramKey.toLowerCase() === name.toLowerCase()) {
                const targets = (el.tagName === 'SELECT' || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA')
                    ? [/** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (el)]
                    : Array.from(el.querySelectorAll('select, input, textarea'));
                targets.forEach(t => {
                    if ( /** @type {HTMLInputElement | HTMLSelectElement} */(t).multiple && t.tagName === 'SELECT') {
                        const csvValues = (String(val || '')).split(',').map(v => v.trim());
                        Array.from(/** @type {HTMLSelectElement} */ (t).options).forEach(opt => {
                            opt.selected = csvValues.includes(opt.value);
                        });
                    }
                    else if ( /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */(t).value !== val) {
                        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (t).value = String(val ?? '');
                    }
                });
            }
        });
    }
}
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape')
        closeMaximizedVisual();
});
// Exposed for deterministic geometry fixtures and for driving the refresh/unmount
// teardown from a test. Reading it does not open a surface.
window.__ETLSQL_DETAIL__ = Object.freeze({
    computeDetailPlacement: computeDetailPlacement,
    destroyIn: function (scope) { destroyDetailSurfaces(scope || document); },
    preferredSide: DETAIL_PREFERRED_SIDE,
    viewportMargin: DETAIL_VIEWPORT_MARGIN,
    anchorGap: DETAIL_ANCHOR_GAP
});
// Escape and outside click are document-level and installed once.
document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape' || !getOpenDetail())
        return;
    event.stopPropagation();
    closeOpenDetail(true);
}, true);
document.addEventListener('pointerdown', (event) => {
    const openDetail = getOpenDetail();
    if (!openDetail || !openDetail.pinned)
        return;
    const targetNode = event.target;
    if (targetNode && openDetail.element && openDetail.element.contains(targetNode))
        return;
    if (targetNode && openDetail.trigger && openDetail.trigger.contains(targetNode))
        return; // toggle handles this
    closeOpenDetail(false);
}, true);
// Boot on DOMContentLoaded
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
}
else {
    boot();
}
// VS Code message listener
if (vscode) {
    window.addEventListener('message', (event) => {
        const message = event.data;
        if (message && message.type === 'reportManifest') {
            renderManifest(message);
        }
    });
}
// Test escape hatch: exposes pure functions for automated testing.
// Harmless in production (just sets a window property that nothing reads).
if (typeof window !== 'undefined') {
    window.__reportRuntime__ = { isOn, renderCard, renderDatePicker, renderSlider, renderSearch, renderButton, renderNativeSvg, nativeLayoutTier, observeNativeLayout, abbreviateNumber, savedViewsBase, buildViewsPicker, parseStateHash, applyBookmark, applySavedView, captureResolvedState, isOfflineSnapshot, resolveDesignTokens, applyDesignTokens, isSafeCssValue, isAllowedTokenName, DESIGN_TOKENS };
}
