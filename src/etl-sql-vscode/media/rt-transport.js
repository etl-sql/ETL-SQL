// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-transport.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Network transport and VS Code messages.
 */
import { isOfflineSnapshot, recordParametersOffline, savedViewsBase } from './rt-views.js';
import { getActivePageName, getDrillInFlight, isActivePagePaginated, setDrillInFlight } from './rt-actions.js';
import { apiBase, getLastManifest, pendingParameters, vscode } from './rt-state.js';
import { renderManifest } from './report-runtime.js';
import { updateStagedUI } from './rt-chrome.js';


// ── Portal saved views: per-user CRUD over the shared resolved-state envelope ───────────────
// Every write sends the envelope; the server stamps the script hash it was captured against so a
// later republish of the report surfaces as a drift warning instead of a silently partial view.

export async function savedViewsRequest(path, init) {
    const base = savedViewsBase();
    if (!base) return null;
    try {
        const res = await fetch(base + (path || ''), init);
        if (!res.ok) return null;
        if (res.status === 204) return true;
        return await res.json();
    } catch (e) {
        console.warn('Saved-view request failed:', e && e.message);
        return null;
    }
}

export function postDrillIn(visualName, clickedValue) {
    if (getDrillInFlight()) return;
    setDrillInFlight(true);
    if (vscode) {
        setDrillInFlight(false);
        vscode.postMessage({ type: 'drillIn', visualName, clickedValue });
        return;
    }
    fetch(apiBase + '/drill', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ visualName, direction: 'IN', clickedValue })
    }).then(r => r.ok ? r.json() : null).then(m => {
        setDrillInFlight(false);
        if (m) renderManifest(m);
    }).catch(() => { setDrillInFlight(false); });
}

export function postDrillUp(visualName, targetDepth) {
    if (vscode) {
        vscode.postMessage({ type: 'drillUp', visualName, targetDepth });
        return;
    }
    fetch(apiBase + '/drill', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ visualName, direction: 'UP', targetDepth })
    }).then(r => r.ok ? r.json() : null).then(m => { if (m) renderManifest(m); });
}

// Batch-update multiple parameters in a single server round-trip.
// Batch-update multiple parameters in a single server round-trip.
// Batch-update multiple parameters in a single server round-trip.
export async function postParameters(params, isInteraction = false, stage = null, sourceVisual = null) {
    const shouldStage = stage === null
        ? (!isInteraction && isActivePagePaginated())
        : !!stage;
    if (shouldStage && !isInteraction) {
        // Paginated pages stage prompt changes until APPLY_PARAMETERS.
        // Dashboard pages post immediately.
        Object.assign(pendingParameters, params);
        updateStagedUI();
        return Promise.resolve(null);
    }
    return _postParametersInternal(params, isInteraction, getActivePageName(), sourceVisual);
}

export async function _postParametersInternal(params, isInteraction = false, pageName = null, sourceVisual = null) {
    // Convert dictionary to required List<ParameterUpdateRequest> format
    const paramList = Object.entries(params).map(([name, value]) => ({
        name: name,
        value: String(value ?? '')
    }));

    console.debug('[ParameterUpdate] Sending:', { params: paramList, isInteraction });

    // Offline snapshot: the manifest in memory is the entire report, so every consumer of this
    // function is answered from it rather than from an API that is not there. Detail popovers
    // are the reason this matters — they refresh through here on every open, so without this
    // branch a popover in a snapshot viewer showed "could not be loaded" and the offline claim
    // in the tooltip documentation was false. An interaction (`@hover_value`) is transient and
    // must not be written into the report's parameter state.
    if (isOfflineSnapshot()) {
        if (!isInteraction) recordParametersOffline(params);
        return getLastManifest();
    }

    if (vscode) {
        vscode.postMessage({
            type: 'refreshReport',
            parameters: params, // VS Code extension handles the dictionary
            isInteraction: isInteraction,
            pageName: pageName,
            sourceVisual: sourceVisual
        });
        return null;
    }

    try {
        const res = await fetch(apiBase + '/parameters', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({
                params: paramList,
                isInteraction: isInteraction,
                pageName: pageName,
                // Gates EMIT_FILTER: the server narrows the receivers to the source's TARGETS.
                sourceVisual: sourceVisual
            })
        });
        if (!res.ok) {
            console.error('Parameter update failed:', res.status, await res.text());
            return null;
        }
        const manifest = await res.json();
        console.debug('[ParameterUpdate] Received new manifest');
        return manifest;
    } catch (e) {
        console.error('Parameter update request failed:', e);
        return null;
    }
}

export async function postRunScript(scriptPath, parameters) {
    try {
        const res = await fetch(apiBase + '/run-script', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ scriptPath, parameters })
        });
        if (!res.ok) return { message: `Server error: ${res.status}` };
        return await res.json();
    } catch (e) {
        return { message: `Request failed: ${e.message}` };
    }
}

export async function postRefreshVisuals(visuals) {
    try {
        const res = await fetch(apiBase + '/refresh-visuals', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ visuals })
        });
        if (!res.ok) {
            console.error('Visual refresh failed:', res.status, await res.text());
            return null;
        }
        return await res.json();
    } catch (e) {
        console.error('Visual refresh request failed:', e);
        return null;
    }
}
