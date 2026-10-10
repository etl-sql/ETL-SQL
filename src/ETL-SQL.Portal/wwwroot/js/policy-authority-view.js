/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/policy-authority-view.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
export const PANEL_HTML = `
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Enterprise policy authority</span>
        <h3>Signed Organization Policies</h3>
      </div>
      <div class="admin-action-group">
        <span id="pa-status" class="form-hint" role="status"></span>
        <button type="button" class="btn btn-outline btn-sm" id="pa-refreshBtn">Refresh</button>
      </div>
    </div>
    <div id="pa-statusBody"></div><div id="pa-actionResult" class="form-hint" role="status"></div>
  </div>

  <div class="card">
    <div class="card-header"><h3>Publish policy version</h3></div>
    <div class="form-row">
      <div class="form-group">
        <label for="pa-tenant">Tenant</label>
        <input id="pa-tenant" type="text" value="default" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="pa-env">Environment</label>
        <input id="pa-env" type="text" value="prod" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="pa-version">Policy version</label>
        <input id="pa-version" type="text" placeholder="2026.07.11.1" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="pa-reviewer">Reviewer (optional)</label>
        <input id="pa-reviewer" type="text" autocomplete="off">
      </div>
    </div>
    <div class="form-row">
      <div class="form-group">
        <label for="pa-expires">Expires at</label>
        <input id="pa-expires" type="datetime-local">
      </div>
      <div class="form-group">
        <label for="pa-staged">Rollout state</label>
        <select id="pa-staged">
          <option value="false">Publish active</option>
          <option value="true">Stage only</option>
        </select>
      </div>
    </div>
    <div class="form-group">
      <label for="pa-policyJson">Policy JSON</label>
      <textarea id="pa-policyJson" rows="12" spellcheck="false"></textarea>
    </div>
    <div id="pa-error" class="error-msg" role="alert"></div>
    <div class="form-actions">
      <button type="button" class="btn btn-outline btn-sm" id="pa-validateBtn">Validate</button>
      <button type="button" class="btn btn-primary btn-sm" id="pa-publishBtn">Publish</button>
      <span id="pa-validateResult" class="form-hint" role="status"></span>
    </div>
  </div>

  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Progressive rollout</span>
        <h3>Publish canary</h3>
      </div>
    </div>
    <p class="form-hint">Publishes the <strong>Policy JSON</strong> and <strong>Expires at</strong> above to a
      cohort only — the fleet stays on the active version until you promote. Halting reverts the cohort.</p>
    <div class="form-row">
      <div class="form-group">
        <label for="pa-canaryVersion">Canary version</label>
        <input id="pa-canaryVersion" type="text" placeholder="2026.07.11.1-canary" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="pa-cohortType">Cohort</label>
        <select id="pa-cohortType">
          <option value="percentage">Percentage of fleet</option>
          <option value="group">Named machine group</option>
        </select>
      </div>
      <div class="form-group" id="pa-percentageGroup">
        <label for="pa-canaryPercentage">Percentage (1–100)</label>
        <input id="pa-canaryPercentage" type="number" min="1" max="100" value="10">
      </div>
      <div class="form-group" id="pa-groupGroup" style="display:none">
        <label for="pa-canaryGroup">Group name</label>
        <input id="pa-canaryGroup" type="text" placeholder="ring0" autocomplete="off">
      </div>
    </div>
    <div id="pa-canaryError" class="error-msg" role="alert"></div>
    <div class="form-actions">
      <button type="button" class="btn btn-primary btn-sm" id="pa-publishCanaryBtn">Publish canary</button>
      <span id="pa-canaryResult" class="form-hint" role="status"></span>
    </div>
  </div>

  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Version history</span>
        <h3>Policies for <span id="pa-scopeLabel"></span></h3>
      </div>
      <div class="admin-action-group">
        <button type="button" class="btn btn-outline btn-sm" id="pa-loadVersionsBtn">Load versions</button>
      </div>
    </div>
    <div id="pa-versionWrap"><div class="empty-state">Choose tenant/environment and load versions.</div></div>
  </div>

  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Machine enrollment</span>
        <h3>Registered Machines</h3>
      </div>
      <div class="admin-action-group">
        <button type="button" class="btn btn-outline btn-sm" id="pa-loadMachinesBtn">Load machines</button>
      </div>
    </div>
    <div class="form-row">
      <div class="form-group">
        <label for="pa-machineId">Machine ID</label>
        <input id="pa-machineId" type="text" placeholder="32-character enrollment machine GUID" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="pa-enrollmentId">Enrollment ID</label>
        <input id="pa-enrollmentId" type="text" placeholder="32-character enrollment GUID" autocomplete="off">
      </div>
      <div class="form-group">
        <label for="pa-thumbprint">Client cert thumbprint (optional)</label>
        <input id="pa-thumbprint" type="text" autocomplete="off">
      </div>
    </div>
    <div id="pa-machineError" class="error-msg" role="alert"></div>
    <div class="form-actions">
      <button type="button" class="btn btn-primary btn-sm" id="pa-registerMachineBtn">Register machine</button>
    </div>
    <div id="pa-machineWrap"><div class="empty-state">No machines loaded.</div></div>
  </div>`;
export const SAMPLE_POLICY = JSON.stringify({ schemaVersion: '1.0' }, null, 2);
export function escapePolicy(value) {
    return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
}
const esc = escapePolicy, escAttr = escapePolicy;
const formatDate = (value) => value ? new Date(value).toLocaleString() : '—';
function stateChip(state) {
    if (state === 'Active')
        return '<span class="chip chip-active">Active</span>';
    if (state === 'Staged' || state === 'Canary')
        return '<span class="badge badge-warn">' + state + '</span>';
    if (state === 'RolledBack')
        return '<span class="chip chip-inactive">Rolled back</span>';
    return '<span class="form-hint">' + esc(state) + '</span>';
}
function cohortLabel(version) {
    if (version.canaryGroup)
        return '<span class="chip">group: ' + esc(version.canaryGroup) + '</span>';
    if (version.canaryPercentage != null)
        return '<span class="chip">' + version.canaryPercentage + '%</span>';
    return '<span class="form-hint">—</span>';
}
export function renderPolicyVersions(versions) {
    if (!versions.length)
        return '<div class="empty-state">No policy versions published for this scope.</div>';
    return `
        <table class="data-table">
          <thead><tr><th>Version</th><th>State</th><th>Cohort</th><th>Hash</th><th>Issued</th><th>Expires</th><th>Author</th><th></th></tr></thead>
          <tbody>${versions.map((v) => `
            <tr data-version="${escAttr(v.policyVersion)}">
              <td><code>${esc(v.policyVersion)}</code></td>
              <td>${stateChip(v.rolloutState)}</td>
              <td>${cohortLabel(v)}</td>
              <td><code>${esc(String(v.policyHash || '').slice(0, 12))}</code></td>
              <td>${esc(formatDate(v.issuedAtUtc))}</td>
              <td>${esc(formatDate(v.expiresAtUtc))}</td>
              <td>${esc(v.author || '-')}</td>
              <td class="table-actions">
                ${v.rolloutState === 'Staged' ? '<button type="button" class="btn btn-outline btn-sm" data-pa-act="activate">Activate</button>' : ''}
                ${v.rolloutState === 'Canary' ? '<button type="button" class="btn btn-primary btn-sm" data-pa-act="promote">Promote</button>' : ''}
                ${v.rolloutState === 'Canary' ? '<button type="button" class="btn btn-danger-soft btn-sm" data-pa-act="halt">Halt</button>' : ''}
                <button type="button" class="btn btn-outline btn-sm" data-pa-act="rollback">Rollback to</button>
              </td>
            </tr>`).join('')}</tbody>
        </table>`;
}
export function renderPolicyMachines(machines) {
    if (!machines.length)
        return '<div class="empty-state">No machines registered for this scope.</div>';
    return `
        <table class="data-table">
          <thead><tr><th>Machine</th><th>Enrollment</th><th>Cert</th><th>Status</th><th>Registered</th><th>Last seen</th><th></th></tr></thead>
          <tbody>${machines.map((m) => `
            <tr data-machine="${escAttr(m.machineId)}">
              <td><code>${esc(m.machineId)}</code></td>
              <td><code>${esc(m.enrollmentId)}</code></td>
              <td>${m.requiresClientCertificate ? '<span class="chip chip-active">Required</span>' : '<span class="form-hint">Not required</span>'}</td>
              <td>${m.revoked ? `<span class="chip chip-inactive">Revoked</span> ${esc(m.revokedReason || '')}` : '<span class="chip chip-active">Active</span>'}</td>
              <td>${esc(formatDate(m.registeredAtUtc))}</td>
              <td>${esc(formatDate(m.lastSeenAtUtc))}</td>
              <td class="table-actions">
                ${m.revoked ? '' : '<button type="button" class="btn btn-danger-soft btn-sm" data-pa-act="revoke-machine">Revoke</button>'}
              </td>
            </tr>`).join('')}</tbody>
        </table>`;
}
export function defaultPolicyExpiry(now = new Date()) {
    const date = new Date(now);
    date.setDate(date.getDate() + 30);
    const pad = (value) => String(value).padStart(2, '0');
    return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate())
        + 'T' + pad(date.getHours()) + ':' + pad(date.getMinutes());
}
export function parsePolicyExpiry(value, now = Date.now()) {
    const expires = new Date(value);
    if (!value || !Number.isFinite(expires.getTime()) || expires.getTime() <= now)
        throw new Error('Set a valid future expiration.');
    return expires.toISOString();
}
