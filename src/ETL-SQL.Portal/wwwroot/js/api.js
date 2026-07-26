/* api.js — JWT storage + fetch interceptor + API client */

// ── Token storage ──────────────────────────────────────────────────────────────

const TOKEN_KEY   = 'etlsql_token';
const REFRESH_KEY = 'etlsql_refresh';

export const auth = {
    getToken:        () => sessionStorage.getItem(TOKEN_KEY),
    getRefreshToken: () => sessionStorage.getItem(REFRESH_KEY),
    setTokens(token, refreshToken) {
        sessionStorage.setItem(TOKEN_KEY, token);
        if (refreshToken) sessionStorage.setItem(REFRESH_KEY, refreshToken);
    },
    clear() {
        sessionStorage.removeItem(TOKEN_KEY);
        sessionStorage.removeItem(REFRESH_KEY);
    },
    isLoggedIn: () => !!sessionStorage.getItem(TOKEN_KEY),
    redirectToLogin() {
        auth.clear();
        window.location.href = '/login.html';
    }
};

// ── Fetch interceptor ──────────────────────────────────────────────────────────

let _refreshing = null;

async function apiFetch(url, opts = {}) {
    const token = auth.getToken();
    const headers = { ...(opts.headers || {}) };
    if (token) headers['Authorization'] = `Bearer ${token}`;
    if (opts.body && typeof opts.body === 'object' && !(opts.body instanceof FormData)) {
        headers['Content-Type'] = 'application/json';
        opts = { ...opts, body: JSON.stringify(opts.body) };
    }

    let res = await fetch(url, { ...opts, headers });

    if (res.status === 401) {
        // Try token refresh once
        const refreshToken = auth.getRefreshToken();
        if (!refreshToken) { auth.redirectToLogin(); return res; }

        if (!_refreshing) {
            _refreshing = fetch('/api/auth/refresh', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken })
            }).then(async r => {
                if (!r.ok) { auth.redirectToLogin(); return null; }
                const data = await r.json();
                auth.setTokens(data.token, data.refreshToken);
                return data.token;
            }).finally(() => { _refreshing = null; });
        }

        const newToken = await _refreshing;
        if (!newToken) return res;

        headers['Authorization'] = `Bearer ${newToken}`;
        res = await fetch(url, { ...opts, headers });
        if (res.status === 401) { auth.redirectToLogin(); }
    }

    return res;
}

async function apiJson(url, opts = {}) {
    const res = await apiFetch(url, opts);
    if (!res.ok) {
        const err = await res.json().catch(() => ({ error: res.statusText }));
        // MustChangePassword middleware returns 403 with a redirect hint
        if (res.status === 403 && err.redirect) {
            window.location.href = err.redirect;
            return;
        }
        throw Object.assign(new Error(err.error || res.statusText), { status: res.status, body: err });
    }
    if (res.status === 204) return {};
    return res.json();
}

function versionHeaders(version) {
    if (version === undefined || version === null) return {};
    return { 'If-Match': `"${version}"` };
}

// ── Auth ───────────────────────────────────────────────────────────────────────

export const authApi = {
    // Effective identity configuration (anonymous) so the login page can offer SSO when enabled.
    async providers() {
        const res = await fetch('/api/auth/providers');
        if (!res.ok) return { local: true, oidcEnabled: false };
        return res.json();
    },
    async login(username, password) {
        const res = await fetch('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username, password })
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw Object.assign(new Error(err.error || 'Login failed'), { status: res.status });
        }
        return res.json();
    },
    async changePassword(currentPassword, newPassword) {
        return apiJson('/api/auth/change-password', {
            method: 'POST',
            body: { currentPassword, newPassword }
        });
    },
    async logout() {
        const refreshToken = auth.getRefreshToken();
        if (refreshToken) {
            await apiFetch('/api/auth/logout', {
                method: 'POST',
                body: { refreshToken }
            }).catch(() => {});
        }
        auth.redirectToLogin();
    }
};

// ── Folders ────────────────────────────────────────────────────────────────────

export const foldersApi = {
    list: ()                  => apiJson('/api/folders'),
    create: (name, parentId)  => apiJson('/api/folders', { method: 'POST', body: { name, parentId } }),
    update: (id, body, version) => apiJson(`/api/folders/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    delete: (id, cascade, version) => apiJson(`/api/folders/${id}?cascade=${!!cascade}`, { method: 'DELETE', headers: versionHeaders(version) }),
    listAcl: (id)             => apiJson(`/api/folders/${id}/acl`),
    grantAcl: (id, groupId, permission, version) =>
        apiJson(`/api/folders/${id}/acl`, { method: 'POST', headers: versionHeaders(version), body: { groupId, permission } }),
    revokeAcl: (id, groupId, version) =>
        apiJson(`/api/folders/${id}/acl/${groupId}`, { method: 'DELETE', headers: versionHeaders(version) })
};

// ── Reports ────────────────────────────────────────────────────────────────────

export const reportsApi = {
    list:   (folderId) => apiJson(`/api/folders/${folderId}/reports`),
    get:    (id)       => apiJson(`/api/reports/${id}`),
    create: (body)     => apiJson('/api/reports', { method: 'POST', body }),
    update: (id, body, version) => apiJson(`/api/reports/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    delete: (id, version)       => apiJson(`/api/reports/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    favorite: (id)    => apiJson(`/api/reports/${id}/favorite`, { method: 'POST' }),
    unfavorite: (id)  => apiJson(`/api/reports/${id}/favorite`, { method: 'DELETE' }),
    dependencies: (id) => apiJson(`/api/reports/${id}/dependencies`),
    history: (id) => apiJson(`/api/reports/${id}/history`),

    getSnapshot: (id, includeManifest = false) =>
        apiJson(`/api/reports/${id}/snapshot?includeManifest=${includeManifest}`),

    execute: (id, parameters) =>
        apiJson(`/api/reports/${id}/execute`, { method: 'POST', body: { parameters } }),

    refresh: (id) =>
        apiJson(`/api/reports/${id}/refresh`, { method: 'POST' }),

    pollJob: (jobId) => apiJson(`/api/jobs/${jobId}`),

    setParameter:  (id, name, value) =>
        apiJson(`/api/reports/${id}/parameter`,  { method: 'POST', body: { name, value } }),
    setParameters: (id, params)      =>
        apiJson(`/api/reports/${id}/parameters`, { method: 'POST', body: { params } }),

    exportUrl: (id, format) => `/api/reports/${id}/export/${format}`,

    // Authenticated download: window.open() can't send the Bearer header (the JWT
    // lives in sessionStorage), so fetch with auth and save the blob instead.
    async exportFile(id, format) {
        const res = await apiFetch(`/api/reports/${id}/export/${format}`);
        if (!res.ok) {
            const e = await res.json().catch(() => ({}));
            throw new Error(e.error || `Export failed (${res.status})`);
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

    getParameters: (id) => apiJson(`/api/reports/${id}/parameters`),
    validateScript: (scriptPath) => apiJson('/api/reports/validate', { method: 'POST', body: { scriptPath } }),
    listAvailableScripts: () => apiJson('/api/reports/available-scripts')
};

// ── Subscriptions ──────────────────────────────────────────────────────────────

export const subscriptionsApi = {
    list:       ()          => apiJson('/api/subscriptions'),
    get:        (id)        => apiJson(`/api/subscriptions/${id}`),
    create:     (body)      => apiJson('/api/subscriptions',      { method: 'POST',   body }),
    update:     (id, body, version) => apiJson(`/api/subscriptions/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    delete:     (id, version) => apiJson(`/api/subscriptions/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    history:    (id, n=50)  => apiJson(`/api/subscriptions/${id}/history?limit=${n}`),
    smtpAliases: ()         => apiJson('/api/smtp-aliases')
};

// ── Data Quality ──────────────────────────────────────────────────────────────

export const dataQualityApi = {
    quarantineQueue({ jobName = '', q = '', replayable = '', limit = 100 } = {}) {
        const params = new URLSearchParams();
        if (jobName) params.set('jobName', jobName);
        if (q) params.set('q', q);
        if (replayable !== '' && replayable !== null && replayable !== undefined) params.set('replayable', replayable);
        params.set('limit', String(limit));
        return apiJson(`/api/data-quality/quarantine?${params.toString()}`);
    },
    quarantineRows({ quarantineTarget, jobName = '', status = 'quarantined', limit = 50 } = {}) {
        const params = new URLSearchParams({ quarantineTarget, status, limit: String(limit) });
        if (jobName) params.set('jobName', jobName);
        return apiJson(`/api/data-quality/quarantine/rows?${params.toString()}`);
    },
    replayQuarantine(quarantineTarget, jobName = null) {
        return apiJson('/api/data-quality/quarantine/replay', {
            method: 'POST',
            body: { quarantineTarget, jobName }
        });
    },
    updateQuarantineDisposition({ quarantineTarget, jobName = null, rowIds = [], disposition, changes = null } = {}) {
        return apiJson('/api/data-quality/quarantine/disposition', {
            method: 'POST',
            body: { quarantineTarget, jobName, rowIds, disposition, changes }
        });
    },
    qualityTrend({ jobName, limit = 30 } = {}) {
        const params = new URLSearchParams({ jobName, limit: String(limit) });
        return apiJson(`/api/data-quality/trend?${params.toString()}`);
    }
};

// ── Datasets ───────────────────────────────────────────────────────────────────

export const datasetsApi = {
    list:          ()                       => apiJson('/api/datasets'),
    get:           (id)                     => apiJson(`/api/datasets/${id}`),
    update:        (id, body, version)       => apiJson(`/api/datasets/${id}`, { method: 'PATCH', headers: versionHeaders(version), body }),
    delete:        (id, version)             => apiJson(`/api/datasets/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    refresh:       (id)                     => apiJson(`/api/datasets/${id}/refresh`, { method: 'POST' }),
    refreshStatus: (id)                     => apiJson(`/api/datasets/${id}/refresh-status`),
    listAcl:       (id)                     => apiJson(`/api/datasets/${id}/acl`),
    grantAcl:      (id, groupId, permission, version) =>
        apiJson(`/api/datasets/${id}/acl`, { method: 'POST', headers: versionHeaders(version), body: { groupId, permission } }),
    revokeAcl:     (id, groupId, version) =>
        apiJson(`/api/datasets/${id}/acl/${groupId}`, { method: 'DELETE', headers: versionHeaders(version) }),

    data(id, { page = 1, pageSize = 50, sort = null, dir = null, search = null, filters = null } = {}) {
        const p = new URLSearchParams({ page, pageSize });
        if (sort)    p.set('sort', sort);
        if (dir)     p.set('dir', dir);
        if (search)  p.set('search', search);
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        return apiJson(`/api/datasets/${id}/data?${p}`);
    },
    async exportCsv(id, filename, { sort = null, dir = null, search = null, filters = null } = {}) {
        const p = new URLSearchParams();
        if (sort)    p.set('sort', sort);
        if (dir)     p.set('dir', dir);
        if (search)  p.set('search', search);
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        const res = await apiFetch(`/api/datasets/${id}/data/export?${p}`);
        if (!res.ok) throw new Error(`Export failed: ${res.status}`);
        const blob = await res.blob();
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href = url; a.download = filename || 'dataset.csv';
        document.body.appendChild(a); a.click();
        document.body.removeChild(a); URL.revokeObjectURL(url);
    },
    async exportXlsx(id, filename, { sort = null, dir = null, search = null, filters = null } = {}) {
        const p = new URLSearchParams({ format: 'xlsx' });
        if (sort)    p.set('sort', sort);
        if (dir)     p.set('dir', dir);
        if (search)  p.set('search', search);
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        const res = await apiFetch(`/api/datasets/${id}/data/export?${p}`);
        if (!res.ok) throw new Error(`Export failed: ${res.status}`);
        const blob = await res.blob();
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href = url; a.download = filename || 'dataset.xlsx';
        document.body.appendChild(a); a.click();
        document.body.removeChild(a); URL.revokeObjectURL(url);
    },
    stats(id, filters = null) {
        const p = new URLSearchParams();
        if (filters && filters.length) p.set('filters', JSON.stringify(filters));
        return apiJson(`/api/datasets/${id}/data/stats?${p}`);
    },
    columnValues(id, colName, { search = null, limit = 50 } = {}) {
        const p = new URLSearchParams({ limit });
        if (search) p.set('search', search);
        return apiJson(`/api/datasets/${id}/column/${encodeURIComponent(colName)}/values?${p}`);
    }
};

// ── Catalog ───────────────────────────────────────────────────────────────────

export const catalogApi = {
    search: (q, limit = 50) =>
        apiJson(`/api/catalog/search?q=${encodeURIComponent(q)}&limit=${limit}`),
    recent: (limit = 20) =>
        apiJson(`/api/catalog/recent?limit=${limit}`),
    favorites: (limit = 50) =>
        apiJson(`/api/catalog/favorites?limit=${limit}`),
    lineage(kind, { name = null, key = null, value = null, path = null, column = null, from = null, to = null, limit = 100 } = {}) {
        const p = new URLSearchParams({ limit });
        if (name)  p.set('name', name);
        if (key)   p.set('key', key);
        if (value) p.set('value', value);
        if (path)  p.set('path', path);
        if (column) p.set('column', column);
        if (from)  p.set('from', from);
        if (to)    p.set('to', to);
        return apiJson(`/api/catalog/lineage/${kind}?${p}`);
    },
    stewardship({ view = 'all', q = null, steward = null, domain = null, staleAfterDays = 30, limit = 100 } = {}) {
        const p = new URLSearchParams({ view, staleAfterDays, limit });
        if (q) p.set('q', q);
        if (steward) p.set('steward', steward);
        if (domain) p.set('domain', domain);
        return apiJson(`/api/catalog/stewardship?${p}`);
    },
    protectedData({ limit = 100 } = {}) {
        return apiJson(`/api/catalog/protected-data?limit=${limit}`);
    },
    protectedDataSuggestions({ limit = 100 } = {}) {
        return apiJson(`/api/catalog/protected-data/suggestions?limit=${limit}`);
    },
    impact({ kind = 'table', name, column = null, direction = 'downstream', depth = 4, limit = 100 } = {}) {
        const p = new URLSearchParams({ kind, name, direction, depth, limit });
        if (column) p.set('column', column);
        return apiJson(`/api/catalog/impact?${p}`);
    }
};

// ── Admin — Portal secret store (values are write-only) ───────────────────────

export const secretsApi = {
    list:      ()            => apiJson('/api/admin/secrets'),
    set:       (name, value) => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}`, { method: 'PUT', body: { value } }),
    verify:    (name)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/verify`, { method: 'POST' }),
    verifyAll: ()            => apiJson('/api/admin/secrets/verify-all', { method: 'POST' }),
    disable:   (name)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/disable`, { method: 'POST' }),
    enable:    (name)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/enable`, { method: 'POST' }),
    impact:    (name)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}/impact`),
    remove:    (name)        => apiJson(`/api/admin/secrets/${encodeURIComponent(name)}`, { method: 'DELETE' }),
};

// ── Admin — shared connection catalog (SHARED:alias) ──────────────────────────

export const connectionsApi = {
    list:      ()             => apiJson('/api/admin/connections'),
    detail:    (alias)        => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}`),
    set:       (alias, entry) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}`, { method: 'PUT', body: entry }),
    verify:    (alias)        => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/verify`, { method: 'POST' }),
    test:      (alias)        => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/test`, { method: 'POST' }),
    disable:   (alias)        => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/disable`, { method: 'POST' }),
    enable:    (alias)        => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/enable`, { method: 'POST' }),
    remove:    (alias)        => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}`, { method: 'DELETE' }),
    exportAll: ()             => apiJson('/api/admin/connections/export'),
    importAll: (entries)      => apiJson('/api/admin/connections/import', { method: 'POST', body: entries }),
    impact:    (alias)          => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/impact`),
    getHelp:   (type)         => apiJson(`/api/admin/connections/help/${encodeURIComponent(type)}`),
    listAcl:   (alias)          => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/acl`),
    grantAcl:  (alias, groupId) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/acl`, { method: 'POST', body: { groupId } }),
    revokeAcl: (alias, groupId) => apiJson(`/api/admin/connections/${encodeURIComponent(alias)}/acl/${groupId}`, { method: 'DELETE' }),
};

// ── Admin — enterprise policy authority ───────────────────────────────────────

export const policyAuthorityApi = {
    status:   () => apiJson('/api/admin/policy-authority/status'),
    validate: (policyJson) => apiJson('/api/admin/policy-authority/validate', { method: 'POST', body: { policyJson } }),
    versions: (tenant, environment) =>
        apiJson(`/api/admin/policy-authority/versions?tenant=${encodeURIComponent(tenant)}&environment=${encodeURIComponent(environment)}`),
    active: (tenant, environment) =>
        apiJson(`/api/admin/policy-authority/active?tenant=${encodeURIComponent(tenant)}&environment=${encodeURIComponent(environment)}`),
    publish: (body) => apiJson('/api/admin/policy-authority/publish', { method: 'POST', body }),
    activate: (tenant, environment, policyVersion) =>
        apiJson('/api/admin/policy-authority/activate', { method: 'POST', body: { tenant, environment, policyVersion } }),
    rollback: (body) => apiJson('/api/admin/policy-authority/rollback', { method: 'POST', body }),
    canary: (tenant, environment) =>
        apiJson(`/api/admin/policy-authority/canary?tenant=${encodeURIComponent(tenant)}&environment=${encodeURIComponent(environment)}`),
    publishCanary: (body) => apiJson('/api/admin/policy-authority/publish-canary', { method: 'POST', body }),
    promoteCanary: (tenant, environment, policyVersion) =>
        apiJson('/api/admin/policy-authority/promote-canary', { method: 'POST', body: { tenant, environment, policyVersion } }),
    haltCanary: (tenant, environment, policyVersion, reviewer) =>
        apiJson('/api/admin/policy-authority/halt-canary', { method: 'POST', body: { tenant, environment, policyVersion, reviewer } }),
    machines: (tenant = '', environment = '') => {
        const p = new URLSearchParams();
        if (tenant) p.set('tenant', tenant);
        if (environment) p.set('environment', environment);
        return apiJson(`/api/admin/policy-authority/machines${p.toString() ? `?${p}` : ''}`);
    },
    registerMachine: (body) => apiJson('/api/admin/policy-authority/machines', { method: 'POST', body }),
    revokeMachine: (machineId, reason) =>
        apiJson(`/api/admin/policy-authority/machines/${encodeURIComponent(machineId)}/revoke`, { method: 'POST', body: { reason } }),
};

// ── Admin — users ──────────────────────────────────────────────────────────────

export const adminApi = {
    // users
    listUsers:       ()           => apiJson('/api/admin/users'),
    userCatalog:     (query = '') => apiJson(`/api/admin/users/catalog${query ? `?${query}` : ''}`),
    createUser:      (body)       => apiJson('/api/admin/users',     { method: 'POST',   body }),
    updateUser:      (id, body, version) => apiJson(`/api/admin/users/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    deleteUser:      (id, version) => apiJson(`/api/admin/users/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    bulkUserStatus:  (users, isActive) => apiJson('/api/admin/users/bulk-status',
                                        { method: 'POST', body: { users, isActive } }),
    resetPassword:   (id, pwd, version) => apiJson(`/api/admin/users/${id}/reset-password`,
                                        { method: 'POST', headers: versionHeaders(version), body: { newPassword: pwd } }),
    revokeTokens:    (id, version) => apiJson(`/api/admin/users/${id}/revoke-tokens`,
                                        { method: 'POST', headers: versionHeaders(version) }),

    // groups
    listGroups:      ()           => apiJson('/api/admin/groups'),
    groupCatalog:    (query = '') => apiJson(`/api/admin/groups/catalog${query ? `?${query}` : ''}`),
    createGroup:     (body)       => apiJson('/api/admin/groups',    { method: 'POST',   body }),
    updateGroup:     (id, body, version) => apiJson(`/api/admin/groups/${id}`, { method: 'PUT', headers: versionHeaders(version), body }),
    deleteGroup:     (id, version) => apiJson(`/api/admin/groups/${id}`, { method: 'DELETE', headers: versionHeaders(version) }),
    bulkDeleteGroups:(groups, cascade = false) => apiJson('/api/admin/groups/bulk-delete',
                                        { method: 'POST', body: { groups, cascade } }),
    listMembers:     (id)         => apiJson(`/api/admin/groups/${id}/members`),
    memberCatalog:   (id, query = '') => apiJson(`/api/admin/groups/${id}/members/catalog${query ? `?${query}` : ''}`),
    addMember:       (id, userId, version) => apiJson(`/api/admin/groups/${id}/members`,
                                        { method: 'POST', headers: versionHeaders(version), body: { userId } }),
    bulkAddMembers:  (id, userIds, version) => apiJson(`/api/admin/groups/${id}/members/bulk-add`,
                                        { method: 'POST', headers: versionHeaders(version), body: { userIds } }),
    bulkRemoveMembers: (id, userIds, version) => apiJson(`/api/admin/groups/${id}/members/bulk-remove`,
                                        { method: 'POST', headers: versionHeaders(version), body: { userIds } }),
    removeMember:    (id, userId, version) => apiJson(`/api/admin/groups/${id}/members/${userId}`,
                                        { method: 'DELETE', headers: versionHeaders(version) }),

    // audit
    auditLog: (page = 1, pageSize = 50, action = '', userId = '') =>
        apiJson(`/api/admin/audit?page=${page}&pageSize=${pageSize}&action=${encodeURIComponent(action)}&userId=${userId}`),
    operationalMetrics: () => apiJson('/api/admin/metrics/operational'),

    // smtp
    listSmtp: () => apiJson('/api/admin/smtp'),

    // subscriptions (admin sees all)
    listAllSubscriptions: () => apiJson('/api/subscriptions'),
    subscriptionCatalog: (query = '') => apiJson(`/api/admin/subscriptions/catalog${query ? `?${query}` : ''}`),
    bulkSubscriptionStatus: (subscriptions, isActive) => apiJson('/api/admin/subscriptions/bulk-status',
                                        { method: 'POST', body: { subscriptions, isActive } }),

    // reports (admin sees all)
    listAllReports: () => apiJson('/api/admin/reports'),

    // orchestrator connection settings
    getOrchestratorSettings:    ()     => apiJson('/api/admin/settings/orchestrator'),
    updateOrchestratorSettings: (body) => apiJson('/api/admin/settings/orchestrator', { method: 'PUT', body }),

    // portal branding settings
    getBrandingSettings:    ()     => apiJson('/api/admin/settings/branding'),
    updateBrandingSettings: (body) => apiJson('/api/admin/settings/branding', { method: 'PUT', body })
};

// ── Install global fetch intercept for report-runtime.js ──────────────────────
// report-runtime.js calls fetch() without auth headers; this patch adds them.

const _origFetch = window.fetch.bind(window);
window.fetch = async (input, init = {}) => {
    const url = typeof input === 'string' ? input : input.url;
    // Only intercept same-origin /api/ calls that have no auth header yet
    if (url.startsWith('/api/') || url.startsWith(window.location.origin + '/api/')) {
        const token = auth.getToken();
        if (token) {
            const headers = new Headers(init.headers || {});
            if (!headers.has('Authorization'))
                headers.set('Authorization', `Bearer ${token}`);
            return _origFetch(input, { ...init, headers });
        }
    }
    return _origFetch(input, init);
};
