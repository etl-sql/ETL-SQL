/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-reports-api.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { readStudioSession } from './authoring-responses.js';
import { readAdminReportCatalog, readAdminReportIdentity, readAdminReportReceipt, readAdminReportValidation, readAdminReportScripts } from './admin-reports-responses.js';
export class AdminReportOutcomeUnconfirmed extends Error {
    reportId;
    constructor(reportId, cause) {
        super('Report change outcome is unconfirmed. Refresh reports before trying another change.', { cause });
        this.reportId = reportId;
    }
}
async function read(url) {
    const response = await apiFetch(url);
    if (response.status !== 200)
        throw await responseError(response, 'The requested report source is unavailable.');
    return response.json();
}
async function mutate(reportId, url, options, status, receipt) {
    try {
        const response = await apiFetch(url, options);
        if (!response.ok)
            throw await responseError(response, 'The requested report change failed.');
        if (response.status !== status)
            throw new TypeError('The server did not confirm the expected report change.');
        return receipt(status === 204 ? null : await response.json());
    }
    catch (error) {
        if (error instanceof PortalApiError && error.status >= 400 && error.status < 500)
            throw error;
        throw new AdminReportOutcomeUnconfirmed(reportId, error);
    }
}
export const adminReportsApi = {
    session: async () => {
        const session = readStudioSession(await read('/api/studio/session'));
        if (!['CatalogOnly', 'SourceControlled', 'Disabled'].includes(session.mode))
            throw new TypeError('The Studio deployment mode is unavailable.');
        return session;
    },
    catalog: async () => readAdminReportCatalog(await read('/api/admin/reports')),
    get: async (id) => readAdminReportIdentity(await read(`/api/reports/${id}`), id),
    scripts: async () => readAdminReportScripts(await read('/api/reports/available-scripts')),
    validate: async (scriptPath) => {
        const body = { scriptPath };
        const response = await apiFetch('/api/reports/validate', { method: 'POST', body });
        if (response.status !== 200 && response.status !== 400)
            throw await responseError(response, 'The script could not be validated.');
        const result = readAdminReportValidation(await response.json(), scriptPath);
        if (result.isValid !== (response.status === 200))
            throw new TypeError('The script validation status is inconsistent.');
        return result;
    },
    create: (body) => mutate(null, '/api/reports', { method: 'POST', body }, 201, value => readAdminReportReceipt(value, body, null)),
    update: (report, body) => mutate(report.id, `/api/reports/${report.id}`, { method: 'PUT', body, headers: { 'If-Match': `"${report.version}"` } }, 200, value => readAdminReportReceipt(value, body, report)),
    delete: (report) => mutate(report.id, `/api/reports/${report.id}?cascade=true`, { method: 'DELETE', headers: { 'If-Match': `"${report.version}"` } }, 204, () => undefined)
};
