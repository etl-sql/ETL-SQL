// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-data.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Row loading and export readiness.
 */
import { getLastManifest, safeRequestAnimationFrame } from './rt-state.js';

let _exportReadyGeneration = 0;
let _exportReadyPromise = Promise.resolve();
let _exportReadyResolve = null;
let _pendingLazyRows = 0;

export function publishExportState(status, detail) {
    const state = {
        status,
        ready: status === 'ready',
        timestamp: new Date().toISOString(),
        ...(detail || {})
    };
    window.__etlSqlReportExportReady = state.ready;
    window.__etlSqlReportExportState = state;
    window.dispatchEvent(new CustomEvent('etl-sql-report-export-state', { detail: state }));
    if (state.ready) {
        window.dispatchEvent(new CustomEvent('etl-sql-report-export-ready', { detail: state }));
    }
    return state;
}

export function markExportNotReady(reason, detail) {
    _exportReadyGeneration++;
    _exportReadyPromise = new Promise(resolve => {
        _exportReadyResolve = resolve;
    });
    publishExportState('rendering', { reason, ...(detail || {}) });
}

function waitForImagesToSettle() {
    const images = Array.from(document.images || []);
    const pending = images
        .filter(img => !img.complete)
        .map(img => {
            if (typeof img.decode === 'function') {
                return img.decode().catch(() => {});
            }
            return new Promise(resolve => {
                img.addEventListener('load', resolve, { once: true });
                img.addEventListener('error', resolve, { once: true });
            });
        });
    return Promise.all(pending);
}

export function markExportReady(manifest) {
    const generation = _exportReadyGeneration;
    safeRequestAnimationFrame(() => {
        safeRequestAnimationFrame(() => {
            waitForImagesToSettle().then(() => {
                if (generation !== _exportReadyGeneration) return;
                const pageCount = manifest && manifest.pages ? manifest.pages.length : 0;
                const visualCount = manifest && manifest.visuals ? manifest.visuals.length : 0;
                const state = publishExportState('ready', { pageCount, visualCount });
                if (_exportReadyResolve) _exportReadyResolve(state);
                _exportReadyResolve = null;
            });
        });
    });
}

export function hasDeferredRows(visual) {
    return !!(visual && visual.rowsSource && visual.rowsSource.url && (!visual.rows || visual.rows.length === 0));
}

function beginLazyRows() {
    _pendingLazyRows++;
    markExportNotReady('lazy-rows', { pendingLazyRows: _pendingLazyRows });
}

function finishLazyRows(completed) {
    _pendingLazyRows = Math.max(0, _pendingLazyRows - 1);
    if (completed && _pendingLazyRows === 0 && getLastManifest()) {
        markExportReady(getLastManifest());
    } else if (completed) {
        publishExportState('rendering', { reason: 'lazy-rows', pendingLazyRows: _pendingLazyRows });
    }
}

let _arrowLibraryPromise = null;
function ensureArrowLibrary() {
    if (window.arrow) return Promise.resolve(window.arrow);
    if (_arrowLibraryPromise) return _arrowLibraryPromise;

    _arrowLibraryPromise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        let baseUrl;
        if (window.__IS_WEB__) {
            baseUrl = "";
        } else if (window.__API_BASE__) {
            baseUrl = "/js";
        } else {
            baseUrl = "/js";
        }
        script.src = baseUrl + "/arrow.min.js";
        script.onload = () => {
            if (window.arrow) {
                resolve(window.arrow);
            } else {
                reject(new Error("Apache Arrow library failed to initialize."));
            }
        };
        script.onerror = () => reject(new Error("Failed to load Apache Arrow library script."));
        document.head.appendChild(script);
    });

    return _arrowLibraryPromise;
}

function fetchJsonRows(source) {
    return fetch(source.url, { credentials: 'same-origin' })
        .then(res => {
            if (!res.ok) throw new Error('Failed to load rows.');
            return res.json();
        });
}

export function loadVisualRows(visual) {
    if (!hasDeferredRows(visual)) return Promise.resolve(visual);
    if (visual.__rowsPromise) return visual.__rowsPromise;

    beginLazyRows();
    const source = visual.rowsSource;

    let promise;
    if (source.arrowUrl) {
        promise = ensureArrowLibrary()
            .then(() => {
                return fetch(source.arrowUrl, { credentials: 'same-origin' })
                    .then(res => {
                        if (!res.ok) throw new Error('Failed to load binary Arrow stream.');
                        return res.arrayBuffer();
                    })
                    .then(buffer => {
                        const table = window.arrow.tableFromIPC(new Uint8Array(buffer));
                        const columns = table.schema.fields.map(f => f.name);
                        const rows = [];
                        for (let i = 0; i < table.numRows; i++) {
                            const row = [];
                            for (let j = 0; j < table.numCols; j++) {
                                const cell = table.getChildAt(j).get(i);
                                row.push(cell === null ? null : String(cell));
                            }
                            rows.push(row);
                        }
                        return { columns, rows };
                    });
            })
            .catch(err => {
                console.warn("Client-side Arrow parsing failed, falling back to JSON: ", err);
                return fetchJsonRows(source);
            });
    } else {
        promise = fetchJsonRows(source);
    }

    visual.__rowsPromise = promise
        .then(payload => {
            visual.columns = payload.columns || source.columns || visual.columns || [];
            visual.rows = payload.rows || [];
            visual.rowsSource = null;
            visual.__rowsLoaded = true;
            return visual;
        })
        .then(
            result => {
                finishLazyRows(true);
                return result;
            },
            error => {
                finishLazyRows(false);
                throw error;
            });
    return visual.__rowsPromise;
}

export function getExportReadyPromise() { return _exportReadyPromise; }
export function setExportReadyPromise(value) { _exportReadyPromise = value; }


export function getPendingLazyRows() { return _pendingLazyRows; }
export function setPendingLazyRows(value) { _pendingLazyRows = value; }

