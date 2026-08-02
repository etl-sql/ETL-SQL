const roleClaim = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
const nameClaim = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name';
const emailClaim = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';

function decodePayload(token) {
    if (!token || typeof token !== 'string') throw new TypeError('A session token is required.');
    const part = token.split('.')[1];
    if (!part) throw new TypeError('The session token has no payload.');
    const normalized = part.replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
    return JSON.parse(atob(padded));
}

function normalizeRoles(value) {
    return (Array.isArray(value) ? value : value ? [value] : [])
        .map(role => String(role).trim())
        .filter(Boolean);
}

export function getSessionIdentity(token) {
    const claims = decodePayload(token);
    const roles = normalizeRoles(claims.role ?? claims[roleClaim]);
    const displayName = claims.unique_name
        ?? claims[nameClaim]
        ?? claims.name
        ?? claims.preferred_username
        ?? claims.email
        ?? claims[emailClaim]
        ?? claims.sub
        ?? '';
    return Object.freeze({
        displayName: String(displayName),
        subject: claims.sub == null ? null : String(claims.sub),
        email: claims.email ?? claims[emailClaim] ?? null,
        roles: Object.freeze(roles),
        claims: Object.freeze({ ...claims })
    });
}

export function hasRole(identity, ...roles) {
    const expected = new Set(roles.map(role => role.toLowerCase()));
    return identity.roles.some(role => expected.has(role.toLowerCase()));
}

export function renderSessionIdentity(identity, element) {
    if (!element) return;
    element.textContent = identity.displayName;
    element.title = identity.email && identity.email !== identity.displayName
        ? `${identity.displayName} — ${identity.email}`
        : identity.displayName;
    element.dataset.subject = identity.subject ?? '';
}
