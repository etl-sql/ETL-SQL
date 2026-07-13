// Admin -> Policy Authority surface over api/admin/policy-authority.
//
// The private signing key is never handled here. This module shows signer status, validates and
// publishes organization-policy JSON, manages staged/active versions, and registers/revokes
// enrolled machine identities that are allowed to retrieve signed envelopes.

function esc(s) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function escAttr(s) {
  return esc(s).replace(/'/g, '&#39;');
}
function formatDate(value) {
  return value ? new Date(value).toLocaleString() : '-';
}
function defaultExpiry() {
  const date = new Date();
  date.setDate(date.getDate() + 30);
  return date.toISOString().slice(0, 16);
}
function toIsoFromLocal(value) {
  return value ? new Date(value).toISOString() : null;
}

const SAMPLE_POLICY = JSON.stringify({
  schemaVersion: "1.0"
}, null, 2);

const PANEL_HTML = `
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Enterprise policy authority</span>
        <h3>Signed Organization Policies</h3>
      </div>
      <div class="admin-action-group">
        <span id="pa-status" class="form-hint"></span>
        <button class="btn btn-outline btn-sm" id="pa-refreshBtn">Refresh</button>
      </div>
    </div>
    <div id="pa-statusBody"></div>
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
    <div id="pa-error" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-outline btn-sm" id="pa-validateBtn">Validate</button>
      <button class="btn btn-primary btn-sm" id="pa-publishBtn">Publish</button>
      <span id="pa-validateResult" class="form-hint"></span>
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
    <div id="pa-canaryError" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="pa-publishCanaryBtn">Publish canary</button>
      <span id="pa-canaryResult" class="form-hint"></span>
    </div>
  </div>

  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Version history</span>
        <h3>Policies for <span id="pa-scopeLabel"></span></h3>
      </div>
      <div class="admin-action-group">
        <button class="btn btn-outline btn-sm" id="pa-loadVersionsBtn">Load versions</button>
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
        <button class="btn btn-outline btn-sm" id="pa-loadMachinesBtn">Load machines</button>
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
    <div id="pa-machineError" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="pa-registerMachineBtn">Register machine</button>
    </div>
    <div id="pa-machineWrap"><div class="empty-state">No machines loaded.</div></div>
  </div>`;

export function createPolicyAuthorityAdmin({ host, policyAuthorityApi }) {
  host.innerHTML = PANEL_HTML;
  const $ = (id) => host.querySelector(`#${id}`);

  $('pa-expires').value = defaultExpiry();
  $('pa-policyJson').value = SAMPLE_POLICY;

  function scope() {
    return {
      tenant: $('pa-tenant').value.trim(),
      environment: $('pa-env').value.trim()
    };
  }
  function setError(id, text) {
    const box = $(id);
    box.textContent = text || '';
    box.classList.toggle('show', !!text);
  }
  function requireScope() {
    const s = scope();
    if (!s.tenant || !s.environment) throw new Error('Tenant and environment are required.');
    $('pa-scopeLabel').textContent = `${s.tenant}/${s.environment}`;
    return s;
  }

  async function loadStatus() {
    try {
      const status = await policyAuthorityApi.status();
      $('pa-status').textContent = status.configured ? 'Configured' : 'Not configured';
      $('pa-statusBody').innerHTML = status.configured
        ? `<span class="chip chip-active">Signing key available</span>
           <p class="form-hint">Public key fingerprint source is the configured signer. Private key material is never exported through this UI.</p>`
        : `<span class="chip chip-inactive">Signing disabled</span>
           <p class="form-hint">${esc(status.error || 'Configure Portal:PolicyAuthority:SigningCertThumbprint to publish signed envelopes.')}</p>`;
    } catch (err) {
      $('pa-statusBody').innerHTML = `<div class="error-msg show">${esc(err.message)}</div>`;
    }
  }

  async function loadVersions() {
    const { tenant, environment } = requireScope();
    try {
      const versions = await policyAuthorityApi.versions(tenant, environment);
      if (!versions.length) {
        $('pa-versionWrap').innerHTML = '<div class="empty-state">No policy versions published for this scope.</div>';
        return;
      }
      $('pa-versionWrap').innerHTML = `
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
                ${v.rolloutState === 'Staged' ? '<button class="btn btn-outline btn-sm" data-pa-act="activate">Activate</button>' : ''}
                ${v.rolloutState === 'Canary' ? '<button class="btn btn-primary btn-sm" data-pa-act="promote">Promote</button>' : ''}
                ${v.rolloutState === 'Canary' ? '<button class="btn btn-danger-soft btn-sm" data-pa-act="halt">Halt</button>' : ''}
                <button class="btn btn-outline btn-sm" data-pa-act="rollback">Rollback to</button>
              </td>
            </tr>`).join('')}</tbody>
        </table>`;
    } catch (err) {
      $('pa-versionWrap').innerHTML = `<div class="error-msg show">${esc(err.message)}</div>`;
    }
  }

  function stateChip(state) {
    if (state === 'Active') return '<span class="chip chip-active">Active</span>';
    if (state === 'Staged') return '<span class="badge badge-warn">Staged</span>';
    if (state === 'Canary') return '<span class="badge badge-warn">Canary</span>';
    if (state === 'RolledBack') return '<span class="chip chip-inactive">Rolled back</span>';
    return `<span class="form-hint">${esc(state)}</span>`;
  }

  // Escaped; group names are operator-supplied. Only Canary versions carry a cohort.
  function cohortLabel(v) {
    if (v.canaryGroup) return `<span class="chip">group: ${esc(v.canaryGroup)}</span>`;
    if (v.canaryPercentage != null) return `<span class="chip">${esc(String(v.canaryPercentage))}%</span>`;
    return '<span class="form-hint">—</span>';
  }

  async function loadMachines() {
    const { tenant, environment } = requireScope();
    try {
      const machines = await policyAuthorityApi.machines(tenant, environment);
      if (!machines.length) {
        $('pa-machineWrap').innerHTML = '<div class="empty-state">No machines registered for this scope.</div>';
        return;
      }
      $('pa-machineWrap').innerHTML = `
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
                ${m.revoked ? '' : '<button class="btn btn-danger-soft btn-sm" data-pa-act="revoke-machine">Revoke</button>'}
              </td>
            </tr>`).join('')}</tbody>
        </table>`;
    } catch (err) {
      $('pa-machineWrap').innerHTML = `<div class="error-msg show">${esc(err.message)}</div>`;
    }
  }

  async function validatePolicy() {
    setError('pa-error', '');
    const result = await policyAuthorityApi.validate($('pa-policyJson').value);
    $('pa-validateResult').innerHTML = result.isValid
      ? '<span class="chip chip-active">Valid</span>'
      : `<span class="badge badge-error">Invalid</span> ${esc((result.errors || []).join('; '))}`;
    return result.isValid;
  }

  $('pa-refreshBtn').addEventListener('click', loadStatus);
  $('pa-loadVersionsBtn').addEventListener('click', () => loadVersions().catch((err) => setError('pa-error', err.message)));
  $('pa-loadMachinesBtn').addEventListener('click', () => loadMachines().catch((err) => setError('pa-machineError', err.message)));
  $('pa-validateBtn').addEventListener('click', () => validatePolicy().catch((err) => setError('pa-error', err.message)));

  $('pa-cohortType').addEventListener('change', () => {
    const byGroup = $('pa-cohortType').value === 'group';
    $('pa-groupGroup').style.display = byGroup ? '' : 'none';
    $('pa-percentageGroup').style.display = byGroup ? 'none' : '';
  });

  $('pa-publishCanaryBtn').addEventListener('click', async () => {
    setError('pa-canaryError', '');
    try {
      const { tenant, environment } = requireScope();
      const expiresAtUtc = toIsoFromLocal($('pa-expires').value);
      if (!expiresAtUtc) throw new Error('Set "Expires at" above before publishing a canary.');
      const byGroup = $('pa-cohortType').value === 'group';
      const published = await policyAuthorityApi.publishCanary({
        tenant,
        environment,
        policyVersion: $('pa-canaryVersion').value.trim(),
        policyJson: $('pa-policyJson').value,
        reviewer: $('pa-reviewer').value.trim() || null,
        expiresAtUtc,
        canaryGroup: byGroup ? ($('pa-canaryGroup').value.trim() || null) : null,
        canaryPercentage: byGroup ? null : Number($('pa-canaryPercentage').value)
      });
      $('pa-canaryResult').innerHTML = `<span class="chip chip-active">Canary ${esc(published.policyVersion)} published</span>`;
      await loadVersions();
    } catch (err) {
      setError('pa-canaryError', err.message);
    }
  });

  $('pa-publishBtn').addEventListener('click', async () => {
    setError('pa-error', '');
    try {
      const { tenant, environment } = requireScope();
      const expiresAtUtc = toIsoFromLocal($('pa-expires').value);
      if (!expiresAtUtc) throw new Error('Expiration is required.');
      const published = await policyAuthorityApi.publish({
        tenant,
        environment,
        policyVersion: $('pa-version').value.trim(),
        policyJson: $('pa-policyJson').value,
        reviewer: $('pa-reviewer').value.trim() || null,
        expiresAtUtc,
        staged: $('pa-staged').value === 'true'
      });
      $('pa-validateResult').innerHTML = `<span class="chip chip-active">Published ${esc(published.policyVersion)}</span>`;
      await loadVersions();
    } catch (err) {
      setError('pa-error', err.message);
    }
  });

  $('pa-registerMachineBtn').addEventListener('click', async () => {
    setError('pa-machineError', '');
    try {
      const { tenant, environment } = requireScope();
      await policyAuthorityApi.registerMachine({
        tenant,
        environment,
        machineId: $('pa-machineId').value.trim(),
        enrollmentId: $('pa-enrollmentId').value.trim(),
        clientCertificateThumbprint: $('pa-thumbprint').value.trim() || null
      });
      $('pa-machineId').value = '';
      $('pa-enrollmentId').value = '';
      $('pa-thumbprint').value = '';
      await loadMachines();
    } catch (err) {
      setError('pa-machineError', err.message);
    }
  });

  host.addEventListener('click', async (e) => {
    const btn = e.target.closest('button[data-pa-act]');
    if (!btn) return;
    try {
      const { tenant, environment } = requireScope();
      if (btn.dataset.paAct === 'activate') {
        const version = btn.closest('tr')?.dataset.version;
        await policyAuthorityApi.activate(tenant, environment, version);
        await loadVersions();
      } else if (btn.dataset.paAct === 'promote') {
        const version = btn.closest('tr')?.dataset.version;
        if (!window.confirm(`Promote canary ${version} to the whole fleet?`)) return;
        await policyAuthorityApi.promoteCanary(tenant, environment, version);
        await loadVersions();
      } else if (btn.dataset.paAct === 'halt') {
        const version = btn.closest('tr')?.dataset.version;
        if (!window.confirm(`Halt canary ${version} and revert its machines to the active policy?`)) return;
        await policyAuthorityApi.haltCanary(tenant, environment, version, $('pa-reviewer').value.trim() || null);
        await loadVersions();
      } else if (btn.dataset.paAct === 'rollback') {
        const targetPolicyVersion = btn.closest('tr')?.dataset.version;
        const newPolicyVersion = window.prompt(`New policy version for rollback to ${targetPolicyVersion}:`);
        if (!newPolicyVersion) return;
        const expiresAtUtc = toIsoFromLocal($('pa-expires').value);
        await policyAuthorityApi.rollback({
          tenant,
          environment,
          targetPolicyVersion,
          newPolicyVersion,
          reviewer: $('pa-reviewer').value.trim() || null,
          expiresAtUtc
        });
        await loadVersions();
      } else if (btn.dataset.paAct === 'revoke-machine') {
        const machineId = btn.closest('tr')?.dataset.machine;
        const reason = window.prompt(`Reason for revoking machine ${machineId}:`) || null;
        await policyAuthorityApi.revokeMachine(machineId, reason);
        await loadMachines();
      }
    } catch (err) {
      setError('pa-error', err.message);
      setError('pa-machineError', err.message);
    }
  });

  async function load() {
    requireScope();
    await loadStatus();
    await Promise.allSettled([loadVersions(), loadMachines()]);
  }

  return { load };
}
