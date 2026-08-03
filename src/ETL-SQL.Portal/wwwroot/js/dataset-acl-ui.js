// Dataset permissions table for the Admin datasets panel.
//
// Why this module exists: dataset grants come in two kinds. Group grants are what an administrator
// creates; user grants are written automatically for a dataset's creator, because dataset authorship
// is not standing permission — the creator holds a real, revocable Owner row instead. A listing that
// showed only group rows therefore hid the grant that actually lets the author in, and offered no
// way to revoke it. Extracted from datasets-admin.js so that distinction is unit-testable.

/** True when an ACL entry grants a single user rather than a group. */
export function isUserGrant(entry) {
    return entry?.principalKind === 'User';
}

/** Display name for the principal an entry grants to. */
export function principalName(entry) {
    return isUserGrant(entry)
        ? (entry.userName || `user ${entry.userId}`)
        : (entry.groupName || '');
}

/**
 * Builds the permissions table body. `esc`/`escAttr` are the caller's escapers; the revoke button
 * carries `data-uid` for user grants and `data-gid` for group grants, so the two revoke routes
 * cannot be confused for one another.
 */
export function aclRowsHtml(entries, esc, escAttr) {
    return (entries || []).map(entry => {
        const user = isUserGrant(entry);
        const revokeAttr = user
            ? `data-uid="${escAttr(entry.userId)}"`
            : `data-gid="${escAttr(entry.groupId)}"`;
        return `
        <tr>
          <td>${esc(principalName(entry))}</td>
          <td><span class="chip">${user ? 'User' : 'Group'}</span></td>
          <td><span class="chip chip-${(entry.permission || '').toLowerCase()}">${esc(entry.permission)}</span></td>
          <td><button class="btn btn-outline btn-sm btn-danger-soft" ${revokeAttr}>Revoke</button></td>
        </tr>`;
    }).join('');
}

/** Full table markup, including the empty state. */
export function aclTableHtml(entries, esc, escAttr) {
    const rows = aclRowsHtml(entries, esc, escAttr);
    return `
        <table class="data-table">
          <thead><tr><th>Principal</th><th>Type</th><th>Permission</th><th></th></tr></thead>
          <tbody>${rows || '<tr><td colspan="4" class="empty-state">No permissions granted.</td></tr>'}</tbody>
        </table>`;
}
