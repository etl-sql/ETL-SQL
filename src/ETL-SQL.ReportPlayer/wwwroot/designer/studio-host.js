/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-host.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Normalizes the host-specific services supplied to the canonical Studio runtime.
 */

export function createStudioHostAdapter(options = {}) {
    const authFetch = options.authFetch ?? ((url, init) => fetch(url, {
        ...init,
        headers: { ...(options.headers || {}), ...(init?.headers || {}) }
    }));
    const hasWorkspaceHost = options.hasWorkspaceHost ?? !options.deploymentMode;
    const hasGitHost = typeof options.onLoadGitStatus === 'function'
        && typeof options.onLoadGitHistory === 'function'
        && typeof options.onLoadGitDiff === 'function';

    return {
        authFetch,
        apiBase: options.apiBase || '',
        hasWorkspaceHost,
        hasGitHost,
        hasCapability(state, capability) {
            return state.deploymentMode === 'Desktop' || state.capabilities.has(capability);
        }
    };
}
