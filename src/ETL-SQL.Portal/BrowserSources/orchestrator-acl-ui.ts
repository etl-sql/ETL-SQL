import { asError } from './http-error.js';
import type { ApiRequestOptions } from './portal-http.js';
import { readCatalogJson } from './orchestrator-catalog-responses.js';
import { readGrants } from './orchestrator-detail-responses.js';
import { requireMutationStatus } from './orchestrator-job-actions.js';

type Escape = (value: unknown) => string;
export type OrchestratorAccessKind = 'Job' | 'Schedule' | 'Notification';
export interface AccessPanelSubject {
    name?: string | null;
    createdBy?: string | null;
}
export interface AccessPanelState {
    job?: Partial<JobDefinitionDto> | null;
    subject?: AccessPanelSubject | null;
    objectKind?: OrchestratorAccessKind;
    grants?: Partial<ObjectGrantResponse>[] | null;
    error?: string | null;
    isAdmin?: boolean;
}

export interface ObjectAccessTarget {
    kind: OrchestratorAccessKind;
    name: string;
    createdBy?: string | null;
}

export interface ObjectAccessPanelOptions {
    host: HTMLElement;
    idPrefix: string;
    isAdmin: boolean;
    esc: Escape;
    fetch(url: string, init?: ApiRequestOptions): Promise<Response>;
    notify(message: string, failed?: boolean): void;
    confirm(message: string, options: Parameters<EtlSqlFeedback['confirm']>[1]): Promise<boolean>;
    sessionExpired(): void;
    write(key: string, send: (signal: AbortSignal) => Promise<Response>, status: number, done: string,
        after: () => unknown): Promise<boolean>;
    onOwnerUpdated?(target: ObjectAccessTarget): Promise<void> | void;
}

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
 * The principal kinds that can *own* an object — one fewer than can be granted on it.
 *
 * A group cannot own: ownership names who is accountable, and the decision compares it against one
 * caller's key, so a group owner would read as owned and behave as unowned. Offering it here would
 * produce a form whose only outcome is a 400.
 */
export const OWNER_KINDS = ['USER', 'SERVICE'];

/**
 * Splits a stored principal reference like `user:3f2a…` into its kind and key.
 * Attribution is written as `kind:key`, and the two halves mean different things: the kind selects
 * how a grant matches, the key is what it matches on.
 */
export function splitPrincipal(value: string | null | undefined) {
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
export function ownerLabel(createdBy: JobDefinitionDto["createdBy"]) {
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
export function canAdminister(state: AccessPanelState | null | undefined) {
    return !!state && !state.error && Array.isArray(state.grants);
}

/**
 * Table body for the grants on one object.
 *
 * The permission is rendered as a chip rather than a dropdown: changing a grant re-issues it through
 * the same route that creates one, so an edit is an explicit act rather than a stray change event on
 * a select the operator was only reading.
 */
export function grantRowsHtml(grants: AccessPanelState["grants"], esc: Escape, escAttr: Escape, editable: boolean) {
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
 * The objects nobody is accountable for, as a list an administrator can act on.
 *
 * The empty case is stated rather than left blank, because "no unowned objects" and "the list failed
 * to load" look identical otherwise — and here the first is the good news, so silence would read as
 * reassurance the page has not earned.
 */
export function unownedListHtml(objects: Partial<UnownedObjectResponse>[] | null | undefined, esc: Escape) {
    const rows = (objects || []).map(entry => `
        <tr>
          <td><span class="chip">${esc((entry.kind || '').toUpperCase())}</span></td>
          <td>${esc(entry.name)}</td>
        </tr>`).join('');
    return rows
        ? `<table class="data-table"><tbody>${rows}</tbody></table>`
        : `<div class="empty-state">${esc('Every job, schedule, and notification has a recorded owner.')}</div>`;
}

/**
 * Whether to offer owner reassignment.
 *
 * Unlike {@link canAdminister}, this one *is* read from the viewer's own session, because ownership
 * is administrator-only and the role is in the token the browser already holds. It decides what to
 * draw, never what is permitted: the Orchestrator refuses a caller who is not an administrator
 * whatever this returns, and a refusal is rendered rather than swallowed.
 */
export function canReassignOwner(state: AccessPanelState | null | undefined) {
    return !!state && !state.error && state.isAdmin === true;
}

/**
 * The owner-reassignment control.
 *
 * Present for an administrator whether or not the object already has an owner: an unowned object
 * needs adopting, and an owned one whose owner has left the organization needs transferring. Those
 * are the same act, so they are the same control — a separate "adopt" button would imply that
 * transferring was something else.
 */
export function ownerFormHtml(state: AccessPanelState | null | undefined, idPrefix = 'acl') {
    if (!canReassignOwner(state)) return '';
    return `
        <div class="orch-acl-owner-set">
          <select id="${idPrefix}OwnerKind" class="form-select form-select-sm" aria-label="Owner kind">
            ${OWNER_KINDS.map(kind => `<option value="${kind}">${kind.charAt(0)}${kind.slice(1).toLowerCase()}</option>`).join('')}
          </select>
          <input id="${idPrefix}OwnerKey" class="param-input" type="text" placeholder="Principal key" aria-label="Owner principal key">
          <button class="btn btn-sm btn-outline" id="${idPrefix}OwnerBtn" data-acl-action="owner" type="button">Set owner</button>
        </div>`;
}

/**
 * The whole panel: owner, grants, and — for someone who may administer them — the add form.
 *
 * `error` is rendered rather than swallowed. The two failures an operator hits here mean different
 * things and both look like an empty table otherwise: 403 is "you may reach this object but not
 * administer it", and 404 is "no such object in your tenant".
 */
export function accessPanelHtml(state: AccessPanelState | null | undefined, esc: Escape, escAttr: Escape, idPrefix = 'acl') {
    const { job, subject, grants, error } = state || {};
    const owner = (subject ?? job)?.createdBy;
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
          <select id="${idPrefix}PrincipalKind" class="form-select form-select-sm" aria-label="Principal kind">
            ${PRINCIPAL_KINDS.map(kind => `<option value="${kind}">${kind.charAt(0)}${kind.slice(1).toLowerCase()}</option>`).join('')}
          </select>
          <input id="${idPrefix}PrincipalKey" class="param-input" type="text" placeholder="Principal key" aria-label="Principal key">
          <select id="${idPrefix}Permission" class="form-select form-select-sm" aria-label="Permission">
            ${PERMISSIONS.map(permission => `<option value="${permission}">${permission}</option>`).join('')}
          </select>
          <button class="btn btn-sm btn-primary" id="${idPrefix}GrantBtn" data-acl-action="grant" type="button">Grant</button>
        </div>
        <p class="orch-acl-hint">
          The principal <em>key</em>, not a username — a name can be reassigned, and a grant that
          followed one would move with it.
        </p>` : '';

    return `
        <div class="orch-acl-owner">
          <label>Owner</label>
          <div class="orch-detail-meta"><span>${esc(ownerLabel(owner))}</span></div>
          ${ownerFormHtml(state, idPrefix)}
        </div>
        <table class="data-table orch-acl-table">
          <thead><tr><th>Principal</th><th>Type</th><th>Permission</th><th></th></tr></thead>
          <tbody>${rows || `<tr><td colspan="4" class="empty-state">${esc(emptyMessage)}</td></tr>`}</tbody>
        </table>
        ${form}`;
}

export async function loadObjectAccessPanel(
    options: ObjectAccessPanelOptions,
    target: ObjectAccessTarget,
    signal: AbortSignal,
    isLive: () => boolean
): Promise<void> {
    const draw = (state: AccessPanelState) => {
        options.host.innerHTML = accessPanelHtml(state, options.esc, options.esc, options.idPrefix);
    };
    const kindLabel = target.kind.toLowerCase();
    try {
        const response = await options.fetch(
            `/api/orchestrator/authorization/${encodeURIComponent(target.kind)}/${encodeURIComponent(target.name)}`,
            { signal, cache: 'no-store' });
        if (response.status === 401) {
            options.sessionExpired();
            return;
        }
        const state: AccessPanelState = response.status === 403
            ? { subject: target, objectKind: target.kind, error: `You can reach this ${kindLabel} but are not permitted to administer its access.` }
            : { subject: target, objectKind: target.kind, grants: readGrants(await readCatalogJson(response)), isAdmin: options.isAdmin };
        if (isLive()) draw(state);
    } catch (failure) {
        if (isLive()) {
            draw({ subject: target, objectKind: target.kind, error: `Access could not be loaded: ${asError(failure).message}` });
        }
    }
}

export async function handleObjectAccessClick(
    options: ObjectAccessPanelOptions,
    event: Event,
    target: ObjectAccessTarget | null,
    signal: AbortSignal,
    isLive: () => boolean
): Promise<void> {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
    if (!button || !target) return;
    const base = `/api/orchestrator/authorization/${encodeURIComponent(target.kind)}/${encodeURIComponent(target.name)}`;
    const value = (suffix: string) =>
        options.host.querySelector<HTMLInputElement | HTMLSelectElement>(`#${options.idPrefix}${suffix}`)?.value.trim() ?? '';
    const principal = (kind: string, key: string) => `${base}/${encodeURIComponent(kind)}/${encodeURIComponent(key)}`;
    const kindLabel = target.kind.toLowerCase();

    if (button.dataset.revokeKind && button.dataset.revokeKey) {
        const kind = button.dataset.revokeKind, key = button.dataset.revokeKey;
        if (!await options.confirm(`Revoke access for '${key}' on ${kindLabel} '${target.name}'?`, {
            title: 'Revoke access', confirmLabel: 'Revoke access', danger: true, auditAction: 'orchestrator.grant.revoke' })) return;
        await options.write(`grant:${target.kind}:${target.name}:${kind}:${key}`,
            reqSignal => options.fetch(principal(kind, key), { method: 'DELETE', signal: reqSignal }),
            204, `Access revoked for '${key}'.`, () => loadObjectAccessPanel(options, target, signal, isLive));
        return;
    }

    const action = button.dataset.aclAction;
    if (action === 'grant' || button.id === `${options.idPrefix}GrantBtn`) {
        const kind = value('PrincipalKind'), key = value('PrincipalKey'), permission = value('Permission');
        if (!key) { options.notify('Error: Enter the principal key to grant access to.', true); return; }
        await options.write(`grant:${target.kind}:${target.name}:${kind}:${key}`,
            reqSignal => options.fetch(principal(kind, key), { method: 'PUT', body: { permission }, signal: reqSignal }),
            200, `${permission} granted to '${key}'.`, () => loadObjectAccessPanel(options, target, signal, isLive));
        return;
    }

    if (action === 'owner' || button.id === `${options.idPrefix}OwnerBtn`) {
        const principalKind = value('OwnerKind'), principalId = value('OwnerKey');
        if (!principalId) { options.notify('Error: Enter the principal key of the new owner.', true); return; }
        const changed = await options.write(`owner:${target.kind}:${target.name}`,
            reqSignal => options.fetch(`${base}/owner`, { method: 'PUT', body: { principalKind, principalId }, signal: reqSignal }),
            200, `Owner of '${target.name}' updated.`, () => undefined);
        if (!changed || !isLive()) return;
        const nextOwner = `${principalKind.toLowerCase()}:${principalId}`;
        const updatedTarget: ObjectAccessTarget = { ...target, createdBy: nextOwner };
        await options.onOwnerUpdated?.(updatedTarget);
        if (isLive()) await loadObjectAccessPanel(options, updatedTarget, signal, isLive);
    }
}

export { requireMutationStatus };
