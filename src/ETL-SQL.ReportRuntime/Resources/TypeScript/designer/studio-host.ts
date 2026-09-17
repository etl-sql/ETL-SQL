/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Normalizes the host-specific services supplied to the canonical Studio runtime.
 */

export interface StudioHostOptions {
    authFetch?: typeof fetch;
    headers?: Record<string, string>;
    hasWorkspaceHost?: boolean;
    deploymentMode?: string;
    onLoadGitStatus?: unknown;
    onLoadGitHistory?: unknown;
    onLoadGitDiff?: unknown;
    apiBase?: string;
}

export interface StudioCapabilityState {
    deploymentMode: string;
    capabilities: ReadonlySet<string>;
}

export function createStudioHostAdapter(options: StudioHostOptions = {}) {
    const authFetch = options.authFetch ?? ((url: RequestInfo | URL, init?: RequestInit) => fetch(url, {
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
        hasCapability(state: StudioCapabilityState, capability: string) {
            return state.deploymentMode === 'Desktop' || state.capabilities.has(capability);
        }
    };
}
