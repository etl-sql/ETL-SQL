import { assertApiContract } from './api-contracts.generated.js';
import { collectAdminUsers } from './admin-user-directory.js';
import { apiFetch, apiJson, responseError } from './portal-http.js';
export { auth, authApi, apiFetch } from './portal-http.js';

type ResourceId = number | string;
interface DatasetQuery {
    page?: number; pageSize?: number; sort?: string | null; dir?: string | null;
    search?: string | null; filters?: readonly unknown[] | null;
}

function versionHeaders(version: number | null | undefined): HeadersInit {
    return version == null ? {} : { 'If-Match': '"' + version + '"' };
}

function contracted(name: string, request: Promise<unknown>): Promise<unknown> {
    return request.then(value => { assertApiContract(name, value); return value; });
}

// ── Folders ────────────────────────────────────────────────────────────────────

export const foldersApi = {
    list: ()                  => contracted('folderList', apiJson('/api/folders')),
    create: (name: string, parentId: number | null)  => contracted('folder', apiJson('/api/folders', { method: 'POST', body: { name, parentId } })),
    update: (id: ResourceId, body: UpdateFolderRequest, version?: number | null) => contracted('folder', apiJson(`/api/folders/${id}`, { method: 'PUT', headers: versionHeaders(version), body })),
    delete: (id: ResourceId, cascade: boolean, version?: number | null) => apiJson(`/api/folders/${id}?cascade=${!!cascade}`, { method: 'DELETE', headers: versionHeaders(version) }),
    listAcl: (id: ResourceId)             => apiJson(`/api/folders/${id}/acl`),
    grantAcl: (id: ResourceId, groupId: number, permission: number, version?: number | null) =>
        apiJson(`/api/folders/${id}/acl`, { method: 'POST', headers: versionHeaders(version), body: { groupId, permission } }),
    revokeAcl: (id: ResourceId, groupId: number, version?: number | null) =>
        apiJson(`/api/folders/${id}/acl/${groupId}`, { method: 'DELETE', headers: versionHeaders(version) })
};

// ── Reports ────────────────────────────────────────────────────────────────────

export const reportsApi = {
    list:   (folderId: ResourceId) => contracted('reportList', apiJson(`/api/folders/${folderId}/reports`)),
    get:    (id: ResourceId)       => contracted('report', apiJson(`/api/reports/${id}`)),
    create: (body: PublishReportRequest)     => contracted('report', apiJson('/api/reports', { method: 'POST', body })),
    update: (id: ResourceId, body: UpdateReportRequest, version?: number | null) => contracted('report', apiJson(`/api/reports/${id}`, { method: 'PUT', headers: versionHeaders(version), body })),
    delete: (id: ResourceId, version?: number | null)       => apiJson(`/api/reports/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    favorite: (id: ResourceId)    => apiJson(`/api/reports/${id}/favorite`, { method: 'POST' }),
    unfavorite: (id: ResourceId)  => apiJson(`/api/reports/${id}/favorite`, { method: 'DELETE' }),
    dependencies: (id: ResourceId) => apiJson(`/api/reports/${id}/dependencies`),
    history: (id: ResourceId) => apiJson(`/api/reports/${id}/history`),

    getSnapshot: (id: ResourceId, includeManifest = false) =>
        apiJson(`/api/reports/${id}/snapshot?includeManifest=${includeManifest}`),

    execute: (id: ResourceId, parameters: Record<string, unknown>) =>
        contracted('jobAccepted', apiJson(`/api/reports/${id}/execute`, { method: 'POST', body: { parameters } })),

    refresh: (id: ResourceId) =>
        apiJson(`/api/reports/${id}/refresh`, { method: 'POST' }),

    pollJob: (jobId: string) => contracted('jobStatus', apiJson(`/api/jobs/${jobId}`)),

    setParameter:  (id: ResourceId, name: string, value: unknown) =>
        apiJson(`/api/reports/${id}/parameter`,  { method: 'POST', body: { name, value } }),
    setParameters: (id: ResourceId, params: Record<string, unknown>)      =>
        apiJson(`/api/reports/${id}/parameters`, { method: 'POST', body: { params } }),

    exportUrl: (id: ResourceId, format: string) => `/api/reports/${id}/export/${format}`,

    // Authenticated download: window.open() can't send the Bearer header (the JWT
    // lives in sessionStorage), so fetch with auth and save the blob instead.
    async exportFile(id: ResourceId, format: string) {
        const res = await apiFetch(`/api/reports/${id}/export/${format}`);
        if (!res.ok) {
            throw await responseError(res, `Export failed (${res.status})`);
        }
        const cd = res.headers.get('Content-Disposition') || '';
        const m  = /filename\*?=(?:UTF-8'')?["']?([^"';]+)/i.exec(cd);
        const ext = format === 'pdf' ? 'pdf' : format === 'xlsx' ? 'xlsx' : 'csv';
        const filename = m ? decodeURIComponent(m[1]) : `report.${ext}`;
        const blob = await res.blob();
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href = url; a.download = filename;
        document.body.appendChild(a); a.click();
        document.body.removeChild(a); URL.revokeObjectURL(url);
    },

    getParameters: (id: ResourceId) => apiJson(`/api/reports/${id}/parameters`),
    validateScript: (scriptPath: string) => apiJson('/api/reports/validate', { method: 'POST', body: { scriptPath } }),
    listAvailableScripts: () => apiJson('/api/reports/available-scripts')
};

// ── Subscriptions ──────────────────────────────────────────────────────────────

export const subscriptionsApi = {
    list:       ()          => apiJson('/api/subscriptions'),
    get:        (id: ResourceId)        => apiJson(`/api/subscriptions/${id}`),
    create:     (body: CreateSubscriptionRequest)      => apiJson('/api/subscriptions',      { method: 'POST',   body }),
    update:     (id: ResourceId, body: UpdateSubscriptionRequest, version?: number | null) => apiJson(`/api/subscriptions/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    delete:     (id: ResourceId, version?: number | null) => apiJson(`/api/subscriptions/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    history:    (id: ResourceId, n=50)  => apiJson(`/api/subscriptions/${id}/history?limit=${n}`),
    smtpAliases: ()         => apiJson('/api/smtp-aliases')
};

// ── Data Quality ──────────────────────────────────────────────────────────────

// Durable governance workflow. Every call reaches the server: there is no cached or seeded
// fallback, because a governance figure that survives an unreachable API is a figure nobody can
// trace back to evidence. Errors keep their `status`, which is how the dashboard tells "you may
// not see this" (403) apart from "we could not find out" (anything else).
async function governanceMutation(url: string, options: RequestInit, expected: 200 | 204 = 200): Promise<unknown> {
    const headers = new Headers(options.headers);
    if (options.body !== undefined && options.body !== null) headers.set('Content-Type', 'application/json');
    const response = await apiFetch(url, { ...options, headers });
    if (!response.ok) throw await responseError(response, 'Governance change failed.');
    if (response.status !== expected) throw new TypeError('Governance acknowledgement is unconfirmed.');
    return expected === 204 ? null : response.json();
}

export const governanceApi = {
    dashboard({ scope = 'all' } = {}, signal?: AbortSignal) {
        return apiJson(`/api/governance/dashboard?scope=${encodeURIComponent(scope)}`, { signal });
    },
    findings({ status = '', limit = 200 } = {}, signal?: AbortSignal) {
        const params = new URLSearchParams({ limit: String(limit) });
        if (status) params.set('status', status);
        return apiJson(`/api/governance/findings?${params.toString()}`, { signal });
    },
    scans: ({ limit = 20 } = {}) => apiJson(`/api/governance/scans?limit=${limit}`),
    scan: () => governanceMutation('/api/governance/scan', { method: 'POST' }),
    settings: (signal?: AbortSignal) => apiJson('/api/governance/settings', { signal }),
    saveSettings: (body: UpdateStewardshipSettingsRequest) => governanceMutation('/api/governance/settings',
        { method: 'PUT', body: JSON.stringify(body) }),
    categories: (signal?: AbortSignal) => apiJson('/api/governance/categories', { signal }),
    saveCategory: (body: SaveGovernanceCategoryRequest) => governanceMutation('/api/governance/categories',
        { method: 'POST', body: JSON.stringify(body) }),
    disableCategory: (value: string) =>
        governanceMutation(`/api/governance/categories/${encodeURIComponent(value)}`, { method: 'DELETE' }, 204),
    glossary: (signal?: AbortSignal) => apiJson('/api/governance/glossary', { signal }),
    saveGlossaryTerm: (body: SaveGlossaryTermRequest) => governanceMutation('/api/governance/glossary',
        { method: 'POST', body: JSON.stringify(body) }),
    deleteGlossaryTerm: (term: string) =>
        governanceMutation(`/api/governance/glossary/${encodeURIComponent(term)}`, { method: 'DELETE' }, 204),
    decideFinding: (id: ResourceId, body: DecideFindingRequest) => governanceMutation(`/api/governance/findings/${id}/decide`,
        { method: 'POST', body: JSON.stringify(body) }),
    reviewAsset: (body: ReviewAssetRequest) => governanceMutation('/api/governance/assets/review',
        { method: 'POST', body: JSON.stringify(body) }),
    assignBadge: (body: AssignBadgeRequest) => governanceMutation('/api/governance/assets/badges',
        { method: 'POST', body: JSON.stringify(body) }),
    removeBadge: ({ assetKey, badge }: { assetKey: string; badge: string }) => governanceMutation(
        `/api/governance/assets/badges?assetKey=${encodeURIComponent(assetKey)}&badge=${encodeURIComponent(badge)}`,
        { method: 'DELETE' }, 204),
};

async function qualityAccepted(url: string, body: ReplayQuarantineRequest | QuarantineDispositionRequest, signal?: AbortSignal): Promise<unknown> {
    const response = await apiFetch(url, { method: 'POST', body, signal });
    if (!response.ok) throw await responseError(response, 'Data-quality submission failed.');
    if (response.status !== 202) throw new TypeError('Data-quality submission did not return an accepted acknowledgement.');
    return response.json();
}

export const dataQualityApi = {
    quarantineQueue({ jobName = '', q = '', replayable = '', limit = 100 }: { jobName?: string; q?: string; replayable?: string | null; limit?: number } = {}, signal?: AbortSignal) {
        const params = new URLSearchParams();
        if (jobName) params.set('jobName', jobName);
        if (q) params.set('q', q);
        if (replayable !== '' && replayable !== null && replayable !== undefined) params.set('replayable', replayable);
        params.set('limit', String(limit));
        return apiJson(`/api/data-quality/quarantine?${params.toString()}`, { signal });
    },
    /**
     * @param {Object} [options]
     * @param {string} [options.quarantineTarget]
     * @param {string} [options.jobName]
     * @param {string} [options.status]
     * @param {number} [options.limit]
     */
    quarantineRows({ quarantineTarget, jobName = '', status = 'quarantined', limit = 50 }: { quarantineTarget?: string; jobName?: string; status?: string; limit?: number } = {}, signal?: AbortSignal) {
        const params = new URLSearchParams({ status, limit: String(limit) });
        if (quarantineTarget) params.set('quarantineTarget', quarantineTarget);
        if (jobName) params.set('jobName', jobName);
        return apiJson(`/api/data-quality/quarantine/rows?${params.toString()}`, { signal });
    },
    replayQuarantine(quarantineTarget: string, jobName: string | null = null, signal?: AbortSignal) {
        return qualityAccepted('/api/data-quality/quarantine/replay', { quarantineTarget, jobName }, signal);
    },
    /**
     * @param {Object} [options]
     * @param {string} [options.quarantineTarget]
     * @param {string|null} [options.jobName]
     * @param {Array<*>} [options.rowIds]
     * @param {string} [options.disposition]
     * @param {*} [options.changes]
     * @param {string|null} [options.note]
     */
    updateQuarantineDisposition({ quarantineTarget, jobName = null, rowIds = [], disposition, changes = null, note = null, expectedStatus = 'quarantined' }: QuarantineDispositionRequest, signal?: AbortSignal) {
        return qualityAccepted('/api/data-quality/quarantine/disposition', { quarantineTarget, jobName, rowIds, disposition, changes, note, expectedStatus }, signal);
    },
    /**
     * @param {Object} [options]
     * @param {string} [options.jobName]
     * @param {number} [options.limit]
     */
    qualityTrend({ jobName, limit = 30 }: { jobName?: string; limit?: number } = {}, signal?: AbortSignal) {
        const params = new URLSearchParams({ limit: String(limit) });
        if (jobName) params.set('jobName', jobName);
        return apiJson(`/api/data-quality/trend?${params.toString()}`, { signal });
    },
    qualityJobs: (signal?: AbortSignal) => apiJson('/api/data-quality/jobs', { signal }),
    qualityRules: (jobName: string, signal?: AbortSignal) => apiJson(`/api/data-quality/rules?jobName=${encodeURIComponent(jobName)}`, { signal }),
    qualityRulesAll: (signal?: AbortSignal) => apiJson('/api/data-quality/rules/all', { signal }),
    // Data-quality submissions are IJobChannel jobs, not report executions. `/api/jobs/{id}` is the
    // report-execution namespace and never held these ids, so polling it answered 404 forever —
    // which the client read as a transient outage and retried every second for the life of the tab.
    jobStatus: (jobId: string, signal?: AbortSignal) => contracted(
        'dataQualitySubmissionStatus',
        apiJson(`/api/data-quality/jobs/${encodeURIComponent(jobId)}`, { signal }))
};

// ── Datasets ───────────────────────────────────────────────────────────────────

async function datasetMutation(url: string, method: string, expectedStatus: number,
    body: object | undefined = undefined, version?: number | null, signal?: AbortSignal): Promise<unknown> {
    const response = await apiFetch(url, { method, body, headers: versionHeaders(version), signal });
    if (!response.ok) throw await responseError(response, 'Dataset mutation failed.');
    if (response.status !== expectedStatus) throw new TypeError('Dataset mutation acknowledgement has an unexpected HTTP status.');
    return expectedStatus === 204 ? undefined : response.json();
}
export const datasetsApi = {
    list:          (signal?: AbortSignal)   => apiJson('/api/datasets', { signal }),
    get:           (id: ResourceId)                     => apiJson(`/api/datasets/${id}`),
    update:        (id: ResourceId, body: UpdateDatasetRequest, version?: number | null, signal?: AbortSignal) => datasetMutation(`/api/datasets/${id}`, 'PATCH', 200, body, version, signal),
    delete:        (id: ResourceId, version?: number | null, signal?: AbortSignal) => datasetMutation(`/api/datasets/${id}`, 'DELETE', 204, undefined, version, signal),
    refresh:       (id: ResourceId, signal?: AbortSignal) => datasetMutation(`/api/datasets/${id}/refresh`, 'POST', 202, undefined, undefined, signal),
    refreshStatus: (id: ResourceId)                     => apiJson(`/api/datasets/${id}/refresh-status`),
    listAcl:       (id: ResourceId, signal?: AbortSignal) => apiJson(`/api/datasets/${id}/acl`, { signal }),
    grantAcl:      (id: ResourceId, groupId: number, permission: string, version?: number | null, signal?: AbortSignal) =>
        datasetMutation(`/api/datasets/${id}/acl`, 'POST', 200, { groupId, permission }, version, signal),
    revokeAcl:     (id: ResourceId, groupId: number, version?: number | null, signal?: AbortSignal) =>
        datasetMutation(`/api/datasets/${id}/acl/${groupId}`, 'DELETE', 200, undefined, version, signal),
    revokeUserAcl: (id: ResourceId, userId: number, version?: number | null, signal?: AbortSignal) =>
        datasetMutation(`/api/datasets/${id}/acl/user/${userId}`, 'DELETE', 200, undefined, version, signal),

    data(id: ResourceId, { page = 1, pageSize = 50, sort = null, dir = null, search = null, filters = null }: DatasetQuery = {}, signal?: AbortSignal) {
        const p = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        if (sort)    p.set('sort', sort);
        if (dir)     p.set('dir', dir);
        if (search)  p.set('search', search);
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        return apiJson(`/api/datasets/${id}/data?${p}`, { signal });
    },
    async exportCsv(id: ResourceId, filename: string, { sort = null, dir = null, search = null, filters = null }: DatasetQuery = {}, signal?: AbortSignal) {
        const p = new URLSearchParams();
        if (sort)    p.set('sort', sort);
        if (dir)     p.set('dir', dir);
        if (search)  p.set('search', search);
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        const res = await apiFetch(`/api/datasets/${id}/data/export?${p}`, { signal });
        if (!res.ok) throw new Error(`Export failed: ${res.status}`);
        const blob = await res.blob();
        signal?.throwIfAborted();
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href = url; a.download = filename || 'dataset.csv';
        document.body.appendChild(a); a.click();
        document.body.removeChild(a); URL.revokeObjectURL(url);
    },
    async exportXlsx(id: ResourceId, filename: string, { sort = null, dir = null, search = null, filters = null }: DatasetQuery = {}, signal?: AbortSignal) {
        const p = new URLSearchParams({ format: 'xlsx' });
        if (sort)    p.set('sort', sort);
        if (dir)     p.set('dir', dir);
        if (search)  p.set('search', search);
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        const res = await apiFetch(`/api/datasets/${id}/data/export?${p}`, { signal });
        if (!res.ok) throw new Error(`Export failed: ${res.status}`);
        const blob = await res.blob();
        signal?.throwIfAborted();
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href = url; a.download = filename || 'dataset.xlsx';
        document.body.appendChild(a); a.click();
        document.body.removeChild(a); URL.revokeObjectURL(url);
    },
    stats(id: ResourceId, filters: readonly unknown[] | null = null, signal?: AbortSignal) {
        const p = new URLSearchParams();
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        return apiJson(`/api/datasets/${id}/data/stats?${p}`, { signal });
    },
    columnValues(id: ResourceId, colName: string, { search = null, limit = 50 }: { search?: string | null; limit?: number } = {}, signal?: AbortSignal) {
        const p = new URLSearchParams({ limit: String(limit) });
        if (search) p.set('search', search);
        return apiJson(`/api/datasets/${id}/column/${encodeURIComponent(colName)}/values?${p}`, { signal });
    }
};

// ── Catalog ───────────────────────────────────────────────────────────────────

export const catalogApi = {
    consumerHome: (limit = 8) =>
        apiJson(`/api/catalog/consumer-home?limit=${limit}`),
    search: (q: string, limit = 50) =>
        apiJson(`/api/catalog/search?q=${encodeURIComponent(q)}&limit=${limit}`),
    recent: (limit = 20) =>
        apiJson(`/api/catalog/recent?limit=${limit}`),
    favorites: (limit = 50) =>
        apiJson(`/api/catalog/favorites?limit=${limit}`),
    lineage(kind: string, { name = null, key = null, value = null, path = null, column = null, from = null, to = null, limit = 100 }: { name?: string | null; key?: string | null; value?: string | null; path?: string | null; column?: string | null; from?: string | null; to?: string | null; limit?: number } = {}, signal?: AbortSignal) {
        const p = new URLSearchParams({ limit: String(limit) });
        if (name)  p.set('name', name);
        if (key)   p.set('key', key);
        if (value) p.set('value', value);
        if (path)  p.set('path', path);
        if (column) p.set('column', column);
        if (from)  p.set('from', from);
        if (to)    p.set('to', to);
        return apiJson(`/api/catalog/lineage/${kind}?${p}`, { signal });
    },
    stewardship({ view = 'all', q = null, steward = null, domain = null, staleAfterDays = 30, limit = 100 }: { view?: string; q?: string | null; steward?: string | null; domain?: string | null; staleAfterDays?: number; limit?: number } = {}, signal?: AbortSignal) {
        const p = new URLSearchParams({ view, staleAfterDays: String(staleAfterDays), limit: String(limit) });
        if (q) p.set('q', q);
        if (steward) p.set('steward', steward);
        if (domain) p.set('domain', domain);
        return apiJson(`/api/catalog/stewardship?${p}`, { signal });
    },
    protectedData({ limit = 100 } = {}, signal?: AbortSignal) {
        return apiJson(`/api/catalog/protected-data?limit=${limit}`, { signal });
    },
    protectedDataSuggestions({ limit = 100 } = {}, signal?: AbortSignal) {
        return apiJson(`/api/catalog/protected-data/suggestions?limit=${limit}`, { signal });
    },
    /**
     * @param {Object} [options]
     * @param {string} [options.kind]
     * @param {string} [options.name]
     * @param {string|null} [options.column]
     * @param {string} [options.direction]
     * @param {number} [options.depth]
     * @param {number} [options.limit]
     */
    impact({ kind = 'table', name, column = null, direction = 'downstream', depth = 4, limit = 100 }: { kind?: string; name: string; column?: string | null; direction?: string; depth?: number; limit?: number }, signal?: AbortSignal) {
        const p = new URLSearchParams({ kind, name, direction, depth: String(depth), limit: String(limit) });
        if (column) p.set('column', column);
        return apiJson(`/api/catalog/impact?${p}`, { signal });
    }
};

// ── Studio ───────────────────────────────────────────────────────────────────

// Which top-level destinations to offer this caller. Computed on the server because two of them
// (Docs, Studio) depend on module and capability state no token claim carries.
export const navigationApi = {
    destinations: () => apiJson('/api/portal/navigation')
};

export const studioApi = {
    session: () => apiJson('/api/studio/session'),
    reports: () => apiJson('/api/studio/reports'),
    folders: () => apiJson('/api/studio/folders'),
    privateDrafts: () => apiJson('/api/studio/private-drafts'),
    createReport: (body: CreateStudioReportRequest) => apiJson('/api/studio/reports', { method: 'POST', body })
};

// ── Admin — Portal secret store (values are write-only) ───────────────────────

export const secretsApi = {
    list:      ()            => apiJson('/api/admin/secrets'),
    set:       (name: string, value: string) => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}`, { method: 'PUT', body: { value } }),
    verify:    (name: string)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/verify`, { method: 'POST' }),
    verifyAll: ()            => apiJson('/api/admin/secrets/verify-all', { method: 'POST' }),
    disable:   (name: string)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/disable`, { method: 'POST' }),
    enable:    (name: string)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/enable`, { method: 'POST' }),
    impact:    (name: string)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/impact`),
    remove:    (name: string)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}`, { method: 'DELETE' }),
};

// ── Admin — shared connection catalog (SHARED:alias) ──────────────────────────

export const connectionsApi = {
    list:      (signal?: AbortSignal) => apiJson('/api/admin/connections', { signal }),
    detail:    (alias: string, signal?: AbortSignal) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}`, { signal }),
    set:       (alias: string, entry: SetConnectionRequest, signal?: AbortSignal) => connectionAcknowledgement(`/api/admin/connections/${encodeURIComponent(alias)}`, 'PUT', signal, entry),
    verify:    (alias: string, signal?: AbortSignal) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/verify`, { method: 'POST', signal }),
    test:      (alias: string, signal?: AbortSignal) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/test`, { method: 'POST', signal }),
    disable:   (alias: string, signal?: AbortSignal) => connectionAcknowledgement(`/api/admin/connections/${encodeURIComponent(alias)}/disable`, 'POST', signal),
    enable:    (alias: string, signal?: AbortSignal) => connectionAcknowledgement(`/api/admin/connections/${encodeURIComponent(alias)}/enable`, 'POST', signal),
    remove:    (alias: string, signal?: AbortSignal) => connectionAcknowledgement(`/api/admin/connections/${encodeURIComponent(alias)}`, 'DELETE', signal),
    exportAll: (signal?: AbortSignal) => apiJson('/api/admin/connections/export', { signal }),
    importAll: (entries: PortalSharedConnectionExport[], signal?: AbortSignal) => apiJson('/api/admin/connections/import', { method: 'POST', body: entries, signal }),
    impact:    (alias: string, signal?: AbortSignal) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/impact`, { signal }),
    getHelp:   (type: string, signal?: AbortSignal) => apiJson(`/api/admin/connections/help/${encodeURIComponent(type)}`, { signal }),
    listAcl:   (alias: string)          => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/acl`),
    grantAcl:  (alias: string, groupId: number) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/acl`, { method: 'POST', body: { groupId } }),
    revokeAcl: (alias: string, groupId: number) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/acl/${groupId}`, { method: 'DELETE' }),
};

async function connectionAcknowledgement(url: string, method: string, signal?: AbortSignal, body?: SetConnectionRequest): Promise<void> {
    const response = await apiFetch(url, { method, body, signal });
    if (response.status !== 204) throw new Error('Connection mutation outcome is unconfirmed. Refresh the catalog before retrying.');
}

// ── Admin — on-premises data gateways ────────────────────────────────────────

export const gatewaysApi = {
    list:   (signal?: AbortSignal) => apiJson('/api/admin/gateways', { signal }),
    get:    (gatewayId: string) => apiJson(`/api/admin/gateways/${encodeURIComponent(gatewayId)}`),
    getResources: (gatewayId: string) => apiJson(`/api/admin/gateways/${encodeURIComponent(gatewayId)}/resources`),
    enroll: (gatewayId: string, expirationMinutes = 60, signal?: AbortSignal) => {
        const body: IssueEnrollmentRequest = { gatewayId, expirationMinutes };
        return apiJson('/api/admin/gateways/enroll', { method: 'POST', body, signal });
    },
    revoke: (gatewayId: string, signal?: AbortSignal) =>
        apiJson(`/api/admin/gateways/${encodeURIComponent(gatewayId)}/revoke`, { method: 'POST', signal }),
};

// ── Admin — enterprise policy authority ───────────────────────────────────────

export const policyAuthorityApi = {
    status:   (signal?: AbortSignal) => apiJson('/api/admin/policy-authority/status', { signal }),
    validate: (policyJson: string, signal?: AbortSignal) => {
        const body: PolicyValidateRequest = { policyJson };
        return apiJson('/api/admin/policy-authority/validate', { method: 'POST', body, signal });
    },
    versions: (tenant: string, environment: string, signal?: AbortSignal) =>
        apiJson(`/api/admin/policy-authority/versions?tenant=${encodeURIComponent(tenant)}&environment=${encodeURIComponent(environment)}`, { signal }),
    active: (tenant: string, environment: string) =>
        apiJson(`/api/admin/policy-authority/active?tenant=${encodeURIComponent(tenant)}&environment=${encodeURIComponent(environment)}`),
    publish: (body: PolicyPublishRequest, signal?: AbortSignal) => apiJson('/api/admin/policy-authority/publish', { method: 'POST', body, signal }),
    activate: (tenant: string, environment: string, policyVersion: string, signal?: AbortSignal) => {
        const body: PolicyActivateRequest = { tenant, environment, policyVersion };
        return apiJson('/api/admin/policy-authority/activate', { method: 'POST', body, signal });
    },
    rollback: (body: PolicyRollbackRequest, signal?: AbortSignal) => apiJson('/api/admin/policy-authority/rollback', { method: 'POST', body, signal }),
    canary: (tenant: string, environment: string) =>
        apiJson(`/api/admin/policy-authority/canary?tenant=${encodeURIComponent(tenant)}&environment=${encodeURIComponent(environment)}`),
    publishCanary: (body: PolicyCanaryPublishRequest, signal?: AbortSignal) => apiJson('/api/admin/policy-authority/publish-canary', { method: 'POST', body, signal }),
    promoteCanary: (tenant: string, environment: string, policyVersion: string, signal?: AbortSignal) => {
        const body: PolicyCanaryPromoteRequest = { tenant, environment, policyVersion };
        return apiJson('/api/admin/policy-authority/promote-canary', { method: 'POST', body, signal });
    },
    haltCanary: (tenant: string, environment: string, policyVersion: string, reviewer: string | null, signal?: AbortSignal) => {
        const body: PolicyCanaryHaltRequest = { tenant, environment, policyVersion, reviewer };
        return apiJson('/api/admin/policy-authority/halt-canary', { method: 'POST', body, signal });
    },
    machines: (tenant: string = '', environment: string = '', signal?: AbortSignal) => {
        const p = new URLSearchParams();
        if (tenant) p.set('tenant', tenant);
        if (environment) p.set('environment', environment);
        return apiJson(`/api/admin/policy-authority/machines${p.toString() ? `?${p}` : ''}`, { signal });
    },
    registerMachine: (body: PolicyMachineRegisterRequest, signal?: AbortSignal) => apiJson('/api/admin/policy-authority/machines', { method: 'POST', body, signal }),
    revokeMachine: (machineId: string, reason: string, signal?: AbortSignal) => {
        const body: PolicyMachineRevokeRequest = { reason };
        return apiJson(`/api/admin/policy-authority/machines/${encodeURIComponent(machineId)}/revoke`, { method: 'POST', body, signal });
    },
};

// ── Admin — users ──────────────────────────────────────────────────────────────

export const adminApi = {
    // users
    listUsers:       ()           => collectAdminUsers((page: number, pageSize: number) =>
        contracted('userCatalog', apiJson(`/api/admin/users?page=${page}&pageSize=${pageSize}`))),
    userCatalog:     (query = '') => contracted('userCatalog', apiJson(`/api/admin/users/catalog${query ? `?${query}` : ''}`)),
    createUser:      (body: CreateUserRequest)       => contracted('user', apiJson('/api/admin/users',     { method: 'POST',   body })),
    updateUser:      (id: ResourceId, body: UpdateUserRequest, version?: number | null) => contracted('user', apiJson(`/api/admin/users/${id}`, { method: 'PUT', headers: versionHeaders(version), body })),
    deleteUser:      (id: ResourceId, version?: number | null) => apiJson(`/api/admin/users/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    bulkUserStatus:  (users: VersionedResourceRequest[], isActive: boolean) => apiJson('/api/admin/users/bulk-status',
                                        { method: 'POST', body: { users, isActive } }),
    resetPassword:   (id: ResourceId, pwd: string, version?: number | null) => apiJson(`/api/admin/users/${id}/reset-password`,
                                        { method: 'POST', headers: versionHeaders(version), body: { newPassword: pwd } }),
    revokeTokens:    (id: ResourceId, version?: number | null) => apiJson(`/api/admin/users/${id}/revoke-tokens`,
                                        { method: 'POST', headers: versionHeaders(version) }),

    // groups
    listGroups:      (signal?: AbortSignal) => apiJson('/api/admin/groups', { signal }),
    groupCatalog:    (query = '') => apiJson(`/api/admin/groups/catalog${query ? `?${query}` : ''}`),
    createGroup:     (body: CreateGroupRequest)       => apiJson('/api/admin/groups',    { method: 'POST',   body }),
    updateGroup:     (id: ResourceId, body: UpdateGroupRequest, version?: number | null) => apiJson(`/api/admin/groups/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    deleteGroup:     (id: ResourceId, version?: number | null) => apiJson(`/api/admin/groups/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    bulkDeleteGroups:(groups: VersionedResourceRequest[], cascade: boolean = false) => apiJson('/api/admin/groups/bulk-delete',
                                        { method: 'POST', body: { groups, cascade } }),
    listMembers:     (id: ResourceId)         => apiJson(`/api/admin/groups/${id}/members`),
    memberCatalog:   (id: ResourceId, query = '') => apiJson(`/api/admin/groups/${id}/members/catalog${query ? `?${query}` : ''}`),
    addMember:       (id: ResourceId, userId: number, version?: number | null) => apiJson(`/api/admin/groups/${id}/members`,
                                        { method: 'POST', headers: versionHeaders(version), body: { userId } }),
    bulkAddMembers:  (id: ResourceId, userIds: number[], version?: number | null) => apiJson(`/api/admin/groups/${id}/members/bulk-add`,
                                        { method: 'POST', headers: versionHeaders(version), body: { userIds } }),
    bulkRemoveMembers: (id: ResourceId, userIds: number[], version?: number | null) => apiJson(`/api/admin/groups/${id}/members/bulk-remove`,
                                        { method: 'POST', headers: versionHeaders(version), body: { userIds } }),
    removeMember:    (id: ResourceId, userId: number, version?: number | null) => apiJson(`/api/admin/groups/${id}/members/${userId}`,
                                        { method: 'DELETE', headers: versionHeaders(version) }),

    // audit
    auditLog: (page: number = 1, pageSize: number = 50, action = '', userId: number | string = '', resourceType = '', resourceId = '', signal?: AbortSignal) =>
        apiJson(`/api/admin/audit?page=${page}&pageSize=${pageSize}&action=${encodeURIComponent(action)}&userId=${userId}&resourceType=${encodeURIComponent(resourceType)}&resourceId=${encodeURIComponent(resourceId)}`, { signal }),
    operationalMetrics: (signal?: AbortSignal) => apiJson('/api/admin/metrics/operational', { signal }),
    gatewayAmbiguousWrites: (includeResolved = true) => apiJson(`/api/admin/gateway-operations/ambiguous-writes?includeResolved=${includeResolved}`),
    acknowledgeGatewayAmbiguousWrite: (id: ResourceId, body: CaseNoteRequest) => apiJson(`/api/admin/gateway-operations/ambiguous-writes/${id}/acknowledge`, { method: 'POST', body }),
    assignGatewayAmbiguousWrite: (id: ResourceId, body: AssignCaseRequest) => apiJson(`/api/admin/gateway-operations/ambiguous-writes/${id}/assign`, { method: 'POST', body }),
    addGatewayAmbiguousWriteEvidence: (id: ResourceId, body: EvidenceRequest) => apiJson(`/api/admin/gateway-operations/ambiguous-writes/${id}/evidence`, { method: 'POST', body }),
    resolveGatewayAmbiguousWrite: (id: ResourceId, body: ResolveCaseRequest) => apiJson(`/api/admin/gateway-operations/ambiguous-writes/${id}/resolve`, { method: 'POST', body }),

    // operations hub
    fleetStatus: () => apiJson('/api/fleet/status'),
    listAdminServices: () => apiJson('/api/admin/services'),
    adminServiceHistory: (name: string, limit = 50) => apiJson(`/api/admin/services/${encodeURIComponent(name)}/history?limit=${limit}`),
    listServiceAccounts: () => apiJson('/api/admin/service-accounts'),
    createServiceAccount: (body: CreateServiceAccountRequest) => apiJson('/api/admin/service-accounts', { method: 'POST', body }),
    updateServiceAccount: (id: ResourceId, body: UpdateServiceAccountRequest, version?: number | null) => apiJson(`/api/admin/service-accounts/${encodeURIComponent(id)}`, { method: 'PUT', headers: versionHeaders(version), body }),
    rotateServiceAccount: (id: ResourceId, version?: number | null) => apiJson(`/api/admin/service-accounts/${encodeURIComponent(id)}/rotate-secret`, { method: 'POST', headers: versionHeaders(version) }),
    revokeServiceAccount: (id: ResourceId, version?: number | null) => apiJson(`/api/admin/service-accounts/${encodeURIComponent(id)}/revoke`, { method: 'POST', headers: versionHeaders(version) }),
    pendingAccessRequests: () => apiJson('/api/reports/access-requests/pending'),
    approveAccessRequest: (id: ResourceId, body: ApproveReportAccessRequestDto) => apiJson(`/api/reports/access-requests/${id}/approve`, { method: 'POST', body }),
    denyAccessRequest: (id: ResourceId, body: DenyReportAccessRequestDto) => apiJson(`/api/reports/access-requests/${id}/deny`, { method: 'POST', body }),
    anonymousReportAccess: () => apiJson('/api/admin/anonymous-report-access'),
    revokeAnonymousReportAccess: (type: string, id: ResourceId) => apiJson(`/api/admin/anonymous-report-access/${encodeURIComponent(type)}/${id}`, { method: 'DELETE' }),

    // subscriptions (admin sees all)
    listAllSubscriptions: () => apiJson('/api/subscriptions'),
    subscriptionCatalog: (query = '') => apiJson(`/api/admin/subscriptions/catalog${query ? `?${query}` : ''}`),
    bulkSubscriptionStatus: (subscriptions: VersionedResourceRequest[], isActive: boolean) => apiJson('/api/admin/subscriptions/bulk-status',
                                        { method: 'POST', body: { subscriptions, isActive } }),

    // reports (admin sees all)
    listAllReports: () => apiJson('/api/admin/reports'),

    // orchestrator connection settings
    getOrchestratorSettings:    ()     => apiJson('/api/admin/settings/orchestrator'),
    updateOrchestratorSettings: (body: UpdateOrchestratorSettingsRequest) => apiJson('/api/admin/settings/orchestrator', { method: 'PUT', body }),

    // portal branding settings
    getBrandingSettings:    ()     => apiJson('/api/admin/settings/branding'),
    updateBrandingSettings: (body: UpdatePortalBrandingRequest) => apiJson('/api/admin/settings/branding', { method: 'PUT', body })
};
