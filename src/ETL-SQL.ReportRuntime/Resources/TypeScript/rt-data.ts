/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Row loading and export readiness.
 */
import { getLastManifest, safeRequestAnimationFrame } from './rt-state.js';

let _exportReadyGeneration: number = 0;
let _exportReadyPromise: Promise<any> = Promise.resolve();
let _exportReadyResolve: ((value?: any) => void) | null = null;
let _pendingLazyRows: number = 0;

export function publishExportState(status: string, detail?: Record<string, any>): Record<string, any> {
    const state: Record<string, any> = {
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

export function markExportNotReady(reason: string, detail?: Record<string, any>): void {
    _exportReadyGeneration++;
    _exportReadyPromise = new Promise(resolve => {
        _exportReadyResolve = resolve;
    });
    publishExportState('rendering', { reason, ...(detail || {}) });
}

function waitForImagesToSettle(): Promise<unknown[]> {
    const images: HTMLImageElement[] = Array.from(document.images || []);
    const pending = images
        .filter(img => !img.complete)
        .map(img => {
            if (typeof img.decode === 'function') {
                return img.decode().catch(() => {});
            }
            return new Promise<void>(resolve => {
                img.addEventListener('load', () => resolve(), { once: true });
                img.addEventListener('error', () => resolve(), { once: true });
            });
        });
    return Promise.all(pending);
}

export function markExportReady(manifest?: any): void {
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

export function hasDeferredRows(visual: any): boolean {
    return !!(visual && visual.rowsSource && visual.rowsSource.url && (!visual.rows || visual.rows.length === 0));
}

function beginLazyRows(): void {
    _pendingLazyRows++;
    markExportNotReady('lazy-rows', { pendingLazyRows: _pendingLazyRows });
}

function finishLazyRows(completed: boolean): void {
    _pendingLazyRows = Math.max(0, _pendingLazyRows - 1);
    if (completed && _pendingLazyRows === 0 && getLastManifest()) {
        markExportReady(getLastManifest());
    } else if (completed) {
        publishExportState('rendering', { reason: 'lazy-rows', pendingLazyRows: _pendingLazyRows });
    }
}

let _arrowLibraryPromise: Promise<any> | null = null;
function ensureArrowLibrary(): Promise<any> {
    if (window.arrow) return Promise.resolve(window.arrow);
    if (_arrowLibraryPromise) return _arrowLibraryPromise;

    _arrowLibraryPromise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        let baseUrl: string;
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

function fetchJsonRows(source: any): Promise<any> {
    return fetch(source.url, { credentials: 'same-origin' })
        .then(res => {
            if (!res.ok) throw new Error('Failed to load rows.');
            return res.json();
        });
}

export function loadVisualRows(visual: any): Promise<any> {
    if (!hasDeferredRows(visual)) return Promise.resolve(visual);
    if (visual.__rowsPromise) return visual.__rowsPromise;

    beginLazyRows();
    const source = visual.rowsSource;

    let promise: Promise<any>;
    if (source.arrowUrl) {
        promise = ensureArrowLibrary()
            .then(() => {
                return fetch(source.arrowUrl, { credentials: 'same-origin' })
                    .then(res => {
                        if (!res.ok) throw new Error('Failed to load binary Arrow stream.');
                        return res.arrayBuffer();
                    })
                    .then(buffer => {
                        const arrow = window.arrow!;
                        const table = arrow.tableFromIPC(new Uint8Array(buffer));
                        const columns = table.schema.fields.map((f: { name: string }) => f.name);
                        const rows: (string | null)[][] = [];
                        for (let i = 0; i < table.numRows; i++) {
                            const row: (string | null)[] = [];
                            for (let j = 0; j < table.numCols; j++) {
                                const child = table.getChildAt(j);
                                const cell = child ? child.get(i) : null;
                                row.push(cell === null ? null : String(cell));
                            }
                            rows.push(row);
                        }
                        return { columns, rows };
                    });
            })
            .catch((err: unknown) => {
                console.warn("Client-side Arrow parsing failed, falling back to JSON: ", err);
                return fetchJsonRows(source);
            });
    } else {
        promise = fetchJsonRows(source);
    }

    visual.__rowsPromise = promise
        .then((payload: any) => {
            visual.columns = payload.columns || source.columns || visual.columns || [];
            visual.rows = payload.rows || [];
            visual.rowsSource = null;
            visual.__rowsLoaded = true;
            return visual;
        })
        .then(
            (result: any) => {
                finishLazyRows(true);
                return result;
            },
            (error: unknown) => {
                finishLazyRows(false);
                throw error;
            });
    return visual.__rowsPromise;
}

export function getExportReadyPromise(): Promise<any> { return _exportReadyPromise; }
export function setExportReadyPromise(value: Promise<any>): void { _exportReadyPromise = value; }

export function getPendingLazyRows(): number { return _pendingLazyRows; }
export function setPendingLazyRows(value: number): void { _pendingLazyRows = value; }
