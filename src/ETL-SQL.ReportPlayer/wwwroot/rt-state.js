// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-state.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-state.ts
 * Run: node scripts/sync-assets.js
 */
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
export const isOfflineHost = !!(typeof window !== 'undefined' && (window.__ETLSNAP__ || window.__OFFLINE__));
export const isWebMode = !isOfflineHost && (typeof window !== 'undefined' && Boolean(window.__IS_WEB__ || window.location.protocol.startsWith('http')));
export const vscode = (typeof acquireVsCodeApi === 'function') ? acquireVsCodeApi() : null;
export const isInteractive = isWebMode || vscode;
export const safeRequestAnimationFrame = typeof requestAnimationFrame === 'function' ? requestAnimationFrame : (cb) => setTimeout(cb, 16);
export const feedback = typeof window !== 'undefined' ? window.ETLSQLFeedback : undefined;
let baselineManifest = null;
// In multi-report mode the server injects window.__API_BASE__ = '/reports/{name}/api'.
// Single-report and VS Code modes default to '/api'.
export const apiBase = ((typeof window !== 'undefined' && window.__API_BASE__) || '/api').replace(/\/$/, '');
// Current report parameters (for interactive controls)
export const parameters = {};
export const pendingParameters = {}; // Paginated page staged parameters
let _refreshTimers = [];
let _lastActivePage = null;
export const _drillHistory = [];
export const _crossFilterStates = {}; // Keyed by page element ID; persists across renderManifest re-builds
export const _uiStates = {}; // Keyed by object name; persists across re-renders (e.g. collapsed: true)
let _lastManifest = null;
export function getBaselineManifest() { return baselineManifest; }
export function setBaselineManifest(value) { baselineManifest = value; }
export function getLastManifest() { return _lastManifest; }
export function setLastManifest(value) { _lastManifest = value; }
export function getLastActivePage() { return _lastActivePage; }
export function setLastActivePage(value) { _lastActivePage = value; }
export function getRefreshTimers() { return _refreshTimers; }
export function setRefreshTimers(value) { _refreshTimers = value; }
