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

// ── Auth ───────────────────────────────────────────────────────────────────────

export const authApi = {
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
    update: (id, body)        => apiJson(`/api/folders/${id}`, { method: 'PUT', body }),
    delete: (id, cascade)     => apiJson(`/api/folders/${id}?cascade=${!!cascade}`, { method: 'DELETE' }),
    listAcl: (id)             => apiJson(`/api/folders/${id}/acl`),
    grantAcl: (id, groupId, permission) =>
        apiJson(`/api/folders/${id}/acl`, { method: 'POST', body: { groupId, permission } }),
    revokeAcl: (id, groupId)  => apiJson(`/api/folders/${id}/acl/${groupId}`, { method: 'DELETE' })
};

// ── Reports ────────────────────────────────────────────────────────────────────

export const reportsApi = {
    list:   (folderId) => apiJson(`/api/folders/${folderId}/reports`),
    get:    (id)       => apiJson(`/api/reports/${id}`),
    create: (body)     => apiJson('/api/reports', { method: 'POST', body }),
    update: (id, body) => apiJson(`/api/reports/${id}`, { method: 'PUT', body }),
    delete: (id)       => apiJson(`/api/reports/${id}`, { method: 'DELETE' }),
    favorite: (id)    => apiJson(`/api/reports/${id}/favorite`, { method: 'POST' }),
    unfavorite: (id)  => apiJson(`/api/reports/${id}/favorite`, { method: 'DELETE' }),
    dependencies: (id) => apiJson(`/api/reports/${id}/dependencies`),

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

    getParameters: (id) => apiJson(`/api/reports/${id}/parameters`),
    listAvailableScripts: () => apiJson('/api/reports/available-scripts')
};

// ── Subscriptions ──────────────────────────────────────────────────────────────

export const subscriptionsApi = {
    list:       ()          => apiJson('/api/subscriptions'),
    get:        (id)        => apiJson(`/api/subscriptions/${id}`),
    create:     (body)      => apiJson('/api/subscriptions',      { method: 'POST',   body }),
    update:     (id, body)  => apiJson(`/api/subscriptions/${id}`, { method: 'PUT',    body }),
    delete:     (id)        => apiJson(`/api/subscriptions/${id}`, { method: 'DELETE' }),
    history:    (id, n=50)  => apiJson(`/api/subscriptions/${id}/history?limit=${n}`),
    smtpAliases: ()         => apiJson('/api/smtp-aliases')
};

// ── Datasets ───────────────────────────────────────────────────────────────────

export const datasetsApi = {
    list:          ()                       => apiJson('/api/datasets'),
    get:           (id)                     => apiJson(`/api/datasets/${id}`),
    update:        (id, body)               => apiJson(`/api/datasets/${id}`, { method: 'PATCH', body }),
    delete:        (id)                     => apiJson(`/api/datasets/${id}`, { method: 'DELETE' }),
    refresh:       (id)                     => apiJson(`/api/datasets/${id}/refresh`, { method: 'POST' }),
    refreshStatus: (id)                     => apiJson(`/api/datasets/${id}/refresh-status`),
    listAcl:       (id)                     => apiJson(`/api/datasets/${id}/acl`),
    grantAcl:      (id, groupId, permission) =>
        apiJson(`/api/datasets/${id}/acl`, { method: 'POST', body: { groupId, permission } }),
    revokeAcl:     (id, groupId)            =>
        apiJson(`/api/datasets/${id}/acl/${groupId}`, { method: 'DELETE' })
};

// ── Catalog ───────────────────────────────────────────────────────────────────

export const catalogApi = {
    search: (q, limit = 50) =>
        apiJson(`/api/catalog/search?q=${encodeURIComponent(q)}&limit=${limit}`),
    recent: (limit = 20) =>
        apiJson(`/api/catalog/recent?limit=${limit}`),
    favorites: (limit = 50) =>
        apiJson(`/api/catalog/favorites?limit=${limit}`)
};

// ── Admin — users ──────────────────────────────────────────────────────────────

export const adminApi = {
    // users
    listUsers:       ()           => apiJson('/api/admin/users'),
    createUser:      (body)       => apiJson('/api/admin/users',     { method: 'POST',   body }),
    updateUser:      (id, body)   => apiJson(`/api/admin/users/${id}`, { method: 'PUT',  body }),
    deleteUser:      (id)         => apiJson(`/api/admin/users/${id}`, { method: 'DELETE' }),
    resetPassword:   (id, pwd)    => apiJson(`/api/admin/users/${id}/reset-password`,
                                        { method: 'POST', body: { newPassword: pwd } }),
    revokeTokens:    (id)         => apiJson(`/api/admin/users/${id}/revoke-tokens`, { method: 'POST' }),

    // groups
    listGroups:      ()           => apiJson('/api/admin/groups'),
    createGroup:     (body)       => apiJson('/api/admin/groups',    { method: 'POST',   body }),
    updateGroup:     (id, body)   => apiJson(`/api/admin/groups/${id}`, { method: 'PUT', body }),
    deleteGroup:     (id)         => apiJson(`/api/admin/groups/${id}`, { method: 'DELETE' }),
    listMembers:     (id)         => apiJson(`/api/admin/groups/${id}/members`),
    addMember:       (id, userId) => apiJson(`/api/admin/groups/${id}/members`,
                                        { method: 'POST', body: { userId } }),
    removeMember:    (id, userId) => apiJson(`/api/admin/groups/${id}/members/${userId}`,
                                        { method: 'DELETE' }),

    // audit
    auditLog: (page = 1, pageSize = 50, action = '', userId = '') =>
        apiJson(`/api/admin/audit?page=${page}&pageSize=${pageSize}&action=${encodeURIComponent(action)}&userId=${userId}`),

    // smtp
    listSmtp: () => apiJson('/api/admin/smtp'),

    // subscriptions (admin sees all)
    listAllSubscriptions: () => apiJson('/api/subscriptions'),

    // reports (admin sees all)
    listAllReports: () => apiJson('/api/admin/reports'),

    // orchestrator connection settings
    getOrchestratorSettings:    ()     => apiJson('/api/admin/settings/orchestrator'),
    updateOrchestratorSettings: (body) => apiJson('/api/admin/settings/orchestrator', { method: 'PUT', body })
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
