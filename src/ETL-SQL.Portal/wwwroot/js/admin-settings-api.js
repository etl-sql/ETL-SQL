/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-settings-api.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { readOrchestratorSettings, readPortalBranding, readOrchestratorConnectionStatus } from './admin-settings-responses.js';
export class AdminSettingsOutcomeUnconfirmed extends Error {
    constructor() { super('Change outcome is unconfirmed. Refresh saved settings before making another change.'); }
}
async function read(url, signal) {
    const response = await apiFetch(url, { signal });
    if (response.status !== 200)
        throw await responseError(response, 'Saved settings could not be loaded.');
    return response.json();
}
async function save(url, body) {
    try {
        const response = await apiFetch(url, { method: 'PUT', body });
        if (!response.ok)
            throw await responseError(response, 'The settings change was rejected.');
        if (response.status !== 204)
            throw new TypeError('Settings change was not confirmed.');
    }
    catch (error) {
        if (error instanceof PortalApiError && error.status >= 400 && error.status < 500)
            throw error;
        throw new AdminSettingsOutcomeUnconfirmed();
    }
}
export const adminSettingsApi = {
    orchestrator: async (signal) => readOrchestratorSettings(await read('/api/admin/settings/orchestrator', signal)),
    branding: async (signal) => readPortalBranding(await read('/api/admin/settings/branding', signal)),
    status: async (signal) => readOrchestratorConnectionStatus(await read('/api/orchestrator/status', signal)),
    saveOrchestrator: (body) => save('/api/admin/settings/orchestrator', body),
    saveBranding: (body) => save('/api/admin/settings/branding', body)
};
