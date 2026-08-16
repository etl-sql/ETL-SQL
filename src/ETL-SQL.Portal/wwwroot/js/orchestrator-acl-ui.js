// Access panel for one Orchestrator object — its owner, and who has been granted what.
//
// Why this module exists: until now grants were unreachable through the product. The per-object model
// was enforced on every request, but setting a grant meant hand-crafting a signed assertion with the
// Orchestrator's shared secret, so in practice nobody used it. This is the surface that makes it
// administrable.
//
// Two things are shown together on purpose. Ownership decides access — an object's owner can manage
// it — so a grants table that omitted the owner would list "everyone with access" while leaving out
// the one principal who always has it. That is the misreading this panel exists to prevent.
//
// Rendering only: every function here is pure, so the panel's rules can be tested without a portal,
// a browser, or a running Orchestrator. Fetching and wiring live in orchestrator.html.

/** The permission ladder, widest last. MANAGE includes all; EXECUTE includes READ. */
export const PERMISSIONS = ['READ', 'EXECUTE', 'OVERRIDE', 'MANAGE'];

/** The principal kinds a grant can name. */
export const PRINCIPAL_KINDS = ['USER', 'GROUP', 'SERVICE'];

/**
 * Splits a stored principal reference like `user:3f2a…` into its kind and key.
 * Attribution is written as `kind:key`, and the two halves mean different things: the kind selects
 * how a grant matches, the key is what it matches on.
 */
export function splitPrincipal(value) {
    const text = (value || '').trim();
    if (!text) return null;
    const separator = text.indexOf(':');
    return separator <= 0
        ? { kind: 'USER', key: text }
        : { kind: text.slice(0, separator).toUpperCase(), key: text.slice(separator + 1) };
}

/**
 * How an owner should read in the panel.
 *
 * An object with no recorded owner is not "unowned, therefore open" — it is reachable only by an
 * administrator until someone adopts it. Saying so plainly is the difference between an operator
 * understanding why they cannot edit a job and assuming the UI is broken.
 */
export function ownerLabel(createdBy) {
    const principal = splitPrincipal(createdBy);
    if (!principal) return 'No recorded owner — administrators only, until it is adopted.';
    return `${principal.kind.charAt(0)}${principal.kind.slice(1).toLowerCase()} ${principal.key}`;
}

/**
 * Whether to offer the editing controls.
 *
 * Deliberately not computed from the viewer's own identity. Listing an object's grants already
 * requires MANAGE on it, so a successful read *is* the Orchestrator's answer to "may this caller
 * administer this object" — and re-deriving it here from an owner comparison would be a second
 * permission model in miniature, one that can disagree with the server and would have to be kept in
 * step by hand. The client asks; it does not decide.
 */
export function canAdminister(state) {
    return !!state && !state.error && Array.isArray(state.grants);
}

/**
 * Table body for the grants on one object.
 *
 * The permission is rendered as a chip rather than a dropdown: changing a grant re-issues it through
 * the same route that creates one, so an edit is an explicit act rather than a stray change event on
 * a select the operator was only reading.
 */
export function grantRowsHtml(grants, esc, escAttr, editable) {
    return (grants || []).map(grant => {
        const kind = (grant.principalKind || '').toUpperCase();
        const permission = (grant.permission || '').toUpperCase();
        const revoke = editable
            ? `<button class="btn btn-outline btn-sm btn-danger-soft" data-revoke-kind="${escAttr(kind)}" data-revoke-key="${escAttr(grant.principalId)}">Revoke</button>`
            : '';
        return `
        <tr>
          <td class="orch-acl-key" title="${escAttr(grant.principalId)}">${esc(grant.principalId)}</td>
          <td><span class="chip">${esc(kind.charAt(0) + kind.slice(1).toLowerCase())}</span></td>
          <td><span class="chip chip-${esc(permission.toLowerCase())}">${esc(permission)}</span></td>
          <td>${revoke}</td>
        </tr>`;
    }).join('');
}

/**
 * The whole panel: owner, grants, and — for someone who may administer them — the add form.
 *
 * `error` is rendered rather than swallowed. The two failures an operator hits here mean different
 * things and both look like an empty table otherwise: 403 is "you may reach this object but not
 * administer it", and 404 is "no such object in your tenant".
 */
export function accessPanelHtml(state, esc, escAttr) {
    const { job, grants, error } = state || {};
    const editable = canAdminister(state);

    if (error) {
        return `<div class="orch-acl-error empty-state">${esc(error)}</div>`;
    }

    const rows = grantRowsHtml(grants, esc, escAttr, editable);
    const emptyMessage = editable
        ? 'No grants. Only the owner and administrators can reach this object.'
        : 'No grants.';

    const form = editable ? `
        <div class="orch-acl-add">
          <select id="aclPrincipalKind" aria-label="Principal kind">
            ${PRINCIPAL_KINDS.map(kind => `<option value="${kind}">${kind.charAt(0)}${kind.slice(1).toLowerCase()}</option>`).join('')}
          </select>
          <input id="aclPrincipalKey" type="text" placeholder="Principal key" aria-label="Principal key">
          <select id="aclPermission" aria-label="Permission">
            ${PERMISSIONS.map(permission => `<option value="${permission}">${permission}</option>`).join('')}
          </select>
          <button class="btn btn-sm btn-primary" id="aclGrantBtn">Grant</button>
        </div>
        <p class="orch-acl-hint">
          The principal <em>key</em>, not a username — a name can be reassigned, and a grant that
          followed one would move with it.
        </p>` : '';

    return `
        <div class="orch-acl-owner">
          <label>Owner</label>
          <div class="orch-detail-meta"><span>${esc(ownerLabel(job?.createdBy))}</span></div>
        </div>
        <table class="data-table orch-acl-table">
          <thead><tr><th>Principal</th><th>Type</th><th>Permission</th><th></th></tr></thead>
          <tbody>${rows || `<tr><td colspan="4" class="empty-state">${esc(emptyMessage)}</td></tr>`}</tbody>
        </table>
        ${form}`;
}
