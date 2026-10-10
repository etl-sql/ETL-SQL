import { apiFetch, PortalApiError, responseError } from './portal-http.js';
import { readStudioSession } from './authoring-responses.js';
import { readAdminReportCatalog, readAdminReportIdentity, readAdminReportReceipt, readAdminReportValidation,
  readAdminReportScripts } from './admin-reports-responses.js';
import type { AdminReportInput, AdminReportTarget } from './admin-reports-responses.js';
import type { ApiRequestOptions } from './portal-http.js';

export class AdminReportOutcomeUnconfirmed extends Error {
  readonly reportId: number | null;
  constructor(reportId: number | null, cause: unknown) {
    super('Report change outcome is unconfirmed. Refresh reports before trying another change.', { cause });
    this.reportId = reportId;
  }
}
async function read(url: string): Promise<unknown> {
  const response = await apiFetch(url);
  if (response.status !== 200) throw await responseError(response, 'The requested report source is unavailable.');
  return response.json();
}
async function mutate<T>(reportId: number | null, url: string, options: ApiRequestOptions, status: number,
  receipt: (value: unknown) => T): Promise<T> {
  try {
    const response = await apiFetch(url, options);
    if (!response.ok) throw await responseError(response, 'The requested report change failed.');
    if (response.status !== status) throw new TypeError('The server did not confirm the expected report change.');
    return receipt(status === 204 ? null : await response.json());
  } catch (error) {
    if (error instanceof PortalApiError && error.status >= 400 && error.status < 500) throw error;
    throw new AdminReportOutcomeUnconfirmed(reportId, error);
  }
}
export const adminReportsApi = {
  session: async () => {
    const session = readStudioSession(await read('/api/studio/session'));
    if (!['CatalogOnly', 'SourceControlled', 'Disabled'].includes(session.mode)) throw new TypeError('The Studio deployment mode is unavailable.');
    return session;
  },
  catalog: async () => readAdminReportCatalog(await read('/api/admin/reports')),
  get: async (id: number) => readAdminReportIdentity(await read(`/api/reports/${id}`), id),
  scripts: async () => readAdminReportScripts(await read('/api/reports/available-scripts')),
  validate: async (scriptPath: string) => {
    const body: ValidateReportScriptRequest = { scriptPath };
    const response = await apiFetch('/api/reports/validate', { method: 'POST', body });
    if (response.status !== 200 && response.status !== 400) throw await responseError(response, 'The script could not be validated.');
    const result = readAdminReportValidation(await response.json(), scriptPath);
    if (result.isValid !== (response.status === 200)) throw new TypeError('The script validation status is inconsistent.');
    return result;
  },
  create: (body: AdminReportInput) => mutate(null, '/api/reports', { method: 'POST', body }, 201,
    value => readAdminReportReceipt(value, body, null)),
  update: (report: AdminReportTarget, body: AdminReportInput) => mutate(report.id, `/api/reports/${report.id}`,
    { method: 'PUT', body, headers: { 'If-Match': `"${report.version}"` } }, 200, value => readAdminReportReceipt(value, body, report)),
  delete: (report: AdminReportDto) => mutate(report.id, `/api/reports/${report.id}?cascade=true`,
    { method: 'DELETE', headers: { 'If-Match': `"${report.version}"` } }, 204, () => undefined)
};
