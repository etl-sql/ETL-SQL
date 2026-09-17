/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Host mode and shared report session state.
 */

// Web mode  (single or multi-report server): window.__IS_WEB__ = true
// VS Code mode (webview preview):           window.__MANIFEST__ set, no __IS_WEB__
// Offline snapshot (.etlsnap viewer):       window.__ETLSNAP__ = true, manifest inlined
//
// The offline host is decided before web mode, not after it. A snapshot viewer is a single file
// that carries its own manifest and has no server behind it, but it is often opened over http —
// off a file share, a static site, an artifact server — and protocol alone would then class it
// as web mode and start it polling an API that does not exist. Everything that reads the
// manifest (pages, bookmarks, detail popovers) works either way; everything that would reach for
// a network is what has to stay off.
export const isOfflineHost: boolean = !!(typeof window !== 'undefined' && (window.__ETLSNAP__ || window.__OFFLINE__));
export const isWebMode: boolean = !isOfflineHost && (typeof window !== 'undefined' && Boolean(window.__IS_WEB__ || window.location.protocol.startsWith('http')));
export const vscode = (typeof acquireVsCodeApi === 'function') ? acquireVsCodeApi() : null;
export const isInteractive: unknown = isWebMode || vscode;
export const safeRequestAnimationFrame = typeof requestAnimationFrame === 'function' ? requestAnimationFrame : (cb: FrameRequestCallback) => setTimeout(cb, 16);
export const feedback = typeof window !== 'undefined' ? window.ETLSQLFeedback : undefined;

let baselineManifest: any = null;

// In multi-report mode the server injects window.__API_BASE__ = '/reports/{name}/api'.
// Single-report and VS Code modes default to '/api'.
export const apiBase: string = ((typeof window !== 'undefined' && window.__API_BASE__) || '/api').replace(/\/$/, '');

// Current report parameters (for interactive controls)
export const parameters: Record<string, any> = {};
export const pendingParameters: Record<string, any> = {}; // Paginated page staged parameters
let _refreshTimers: any[] = [];
let _lastActivePage: any = null;
export const _drillHistory: any[] = [];
export const _crossFilterStates: Record<string, any> = {}; // Keyed by page element ID; persists across renderManifest re-builds
export const _uiStates: Record<string, any> = {};          // Keyed by object name; persists across re-renders (e.g. collapsed: true)
let _lastManifest: any = null;

export function getBaselineManifest(): any { return baselineManifest; }
export function setBaselineManifest(value: any): void { baselineManifest = value; }

export function getLastManifest(): any { return _lastManifest; }
export function setLastManifest(value: any): void { _lastManifest = value; }

export function getLastActivePage(): any { return _lastActivePage; }
export function setLastActivePage(value: any): void { _lastActivePage = value; }

export function getRefreshTimers(): any[] { return _refreshTimers; }
export function setRefreshTimers(value: any[]): void { _refreshTimers = value; }
