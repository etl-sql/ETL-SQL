import { apiFetch, responseError } from './portal-http.js';
import { readAdminAuditPage } from './admin-audit-responses.js';

export const adminAuditApi = {
  async catalog(page: number, action: string, signal?: AbortSignal): Promise<PagedResult<AuditLogDto>> {
    const pageSize = 50;
    const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (action) query.set('action', action);
    const response = await apiFetch(`/api/admin/audit?${query}`, { signal });
    if (response.status !== 200) throw await responseError(response, 'Audit entries could not be loaded.');
    return readAdminAuditPage(await response.json(), page, pageSize);
  },
  async exportCsv(action: string, signal?: AbortSignal): Promise<Blob> {
    const query = new URLSearchParams();
    if (action) query.set('action', action);
    const response = await apiFetch(`/api/admin/audit/export/csv${query.size ? `?${query}` : ''}`, { signal });
    if (response.status !== 200) throw await responseError(response, 'The audit CSV could not be exported.');
    if (response.headers.get('Content-Type')?.split(';')[0]?.trim().toLowerCase() !== 'text/csv')
      throw new TypeError('The server did not return an audit CSV.');
    const blob = await response.blob();
    if (blob.size === 0) throw new TypeError('The audit CSV response was empty.');
    return blob;
  }
};
