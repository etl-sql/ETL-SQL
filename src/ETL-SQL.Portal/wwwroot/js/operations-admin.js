// Administration control room. All data is durable server state; partial API failures stay visible
// instead of being collapsed into a misleading all-clear summary.
const esc = (value) => String(value ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const date = (value) => value ? new Date(value).toLocaleString() : 'Never';
const bytes = (value) => {
  let amount = Number(value || 0); let unit = 'B';
  for (const next of ['KB', 'MB', 'GB', 'TB']) { if (amount < 1024) break; amount /= 1024; unit = next; }
  return `${amount.toFixed(unit === 'B' ? 0 : 1)} ${unit}`;
};
const chip = (label, tone = '') => `<span class="chip ${tone ? `chip-${tone}` : ''}">${esc(label)}</span>`;

const MARKUP = `
  <div class="ops-hub">
    <header class="ops-heading"><div><span class="section-kicker">Operations</span><h3>Control room</h3><p>Trace live signals to the identity, access grant, node, or service run responsible.</p></div><button class="btn btn-outline btn-sm" data-action="refresh">Refresh snapshot</button></header>
    <div id="ops-error" class="error-msg" role="status"></div>
    <nav id="ops-signals" class="ops-signal-rail" aria-label="Operational signals"></nav>
    <section class="ops-lane" id="ops-now"><header><span>01</span><div><h3>Now</h3><p>Runtime health and deployment readiness.</p></div></header><div id="ops-now-grid" class="ops-grid"></div></section>
    <section class="ops-lane" id="ops-ambiguous"><header><span>02</span><div><h3>Ambiguous Gateway writes</h3><p>High-priority cases that must be reconciled before any retry.</p></div></header><div id="ops-ambiguous-writes"></div></section>
    <section class="ops-lane" id="ops-authority"><header><span>03</span><div><h3>Authority</h3><p>Machine identities, requests, and anonymous report access.</p></div></header>
      <div class="ops-section-head"><h4>Pending approvals</h4></div><div id="ops-approvals"></div>
      <div class="ops-section-head"><h4>Service accounts</h4><button class="btn btn-primary btn-sm" data-action="create-account">New service account</button></div><div id="ops-accounts"></div>
      <div class="ops-section-head"><h4>Anonymous access</h4></div><div id="ops-access"></div>
    </section>
    <section class="ops-lane" id="ops-automation"><header><span>04</span><div><h3>Automation</h3><p>Native administrative services and durable run history.</p></div></header><div id="ops-services"></div></section>
  </div>
  <div class="modal-overlay" id="ops-modal" style="display:none" role="dialog" aria-modal="true" aria-labelledby="ops-modal-title"><div class="modal-card modal-xl"><div class="modal-header"><div><span class="section-kicker" id="ops-modal-kicker">Operations</span><h3 class="modal-title" id="ops-modal-title"></h3></div></div><div id="ops-modal-body"></div><div id="ops-modal-error" class="error-msg"></div><div class="modal-actions"><button class="btn btn-outline" data-action="close-modal">Cancel</button><button class="btn btn-primary" id="ops-modal-submit">Continue</button></div></div></div>`;

export function createOperationsAdmin({ host, adminApi }) {
  host.innerHTML = MARKUP;
  const find = (selector) => host.querySelector(selector);
  let state = { metrics: null, fleet: null, ambiguousWrites: [], approvals: [], accounts: [], access: [], services: [], users: [] };
  let submitHandler = null;

  function signal(label, value, note, tone, target) {
    return `<button class="ops-signal ${tone ? `is-${tone}` : ''}" data-target="${target}"><span>${esc(label)}</span><strong>${esc(value)}</strong><small>${esc(note)}</small></button>`;
  }

  function renderSignals() {
    const m = state.metrics; const f = state.fleet;
    const unresolved = state.ambiguousWrites.filter(item => item.state !== 'Resolved');
    find('#ops-signals').innerHTML = [
      signal('Fleet', f?.status || 'Unavailable', f ? `${f.environment} · ${f.inventory?.upgradeReadiness?.ready ? 'upgrade ready' : 'review readiness'}` : 'Status could not be loaded', f?.status === 'Healthy' ? 'good' : 'warning', 'ops-now'),
      signal('Execution queue', m?.queuedExecutions ?? '—', m ? `${m.activeExecutions} active · cap ${m.executionCap}` : 'Metrics unavailable', m?.queuedExecutions > 0 ? 'warning' : '', 'ops-now'),
      signal('Approvals', state.approvals.length, state.approvals.length ? 'Awaiting a decision' : 'Nothing pending', state.approvals.length ? 'warning' : '', 'ops-authority'),
      signal('Audit delivery', m?.auditOutboxPending ?? '—', m ? `${m.auditOutboxFailed} failed · oldest ${Math.round(m.auditOutboxOldestPendingAgeSeconds || 0)}s` : 'Metrics unavailable', m?.auditOutboxFailed || m?.auditOutboxPending ? 'warning' : 'good', 'ops-now'),
      signal('Ambiguous writes', unresolved.length, unresolved.length ? 'Retry blocked · operator evidence required' : 'No unresolved Gateway writes', unresolved.length ? 'warning' : 'good', 'ops-ambiguous'),
    ].join('');
  }

  function renderNow() {
    const m = state.metrics; const f = state.fleet; const inventory = f?.inventory;
    find('#ops-now-grid').innerHTML = `
      <article class="card ops-card"><div class="card-header"><h4>Fleet status</h4>${chip(f?.status || 'Unavailable', f?.status === 'Healthy' ? 'active' : 'inactive')}</div><dl class="ops-facts"><div><dt>Environment</dt><dd>${esc(f?.environment || '—')}</dd></div><div><dt>Node</dt><dd>${esc(inventory?.nodeId || m?.nodeId || '—')}</dd></div><div><dt>Version</dt><dd>${esc(inventory?.installedVersion || '—')}</dd></div><div><dt>Schema</dt><dd>${m?.schemaUpToDate ? 'Current' : 'Review'}</dd></div><div><dt>Upgrade</dt><dd>${inventory?.upgradeReadiness?.ready ? 'Ready' : 'Blocked'}</dd></div><div><dt>Storage</dt><dd>${esc(f?.storage || '—')}</dd></div></dl>${inventory?.upgradeReadiness?.findings?.length ? `<p class="ops-finding">${esc(inventory.upgradeReadiness.findings.join(' · '))}</p>` : ''}</article>
      <article class="card ops-card"><div class="card-header"><h4>Workload</h4><span>${m ? `${m.windowHours}-hour window` : 'Unavailable'}</span></div><dl class="ops-facts"><div><dt>Active</dt><dd>${m ? `${m.activeExecutions} / ${m.executionCap}` : '—'}</dd></div><div><dt>Queued</dt><dd>${m?.queuedExecutions ?? '—'}</dd></div><div><dt>Execution failures</dt><dd>${m ? `${m.recentExecutionFailures} / ${m.recentExecutions}` : '—'}</dd></div><div><dt>Stale datasets</dt><dd>${m?.staleDatasets ?? '—'}</dd></div><div><dt>Dataset storage</dt><dd>${m ? bytes(m.datasetStorageBytes) : '—'}</dd></div><div><dt>Security events</dt><dd>${m ? `${m.securityEventPending} pending / ${m.securityEventFailed} failed` : '—'}</dd></div></dl></article>`;
  }

  function table(headers, rows, empty) {
    return rows.length ? `<div class="table-scroll"><table class="data-table"><thead><tr>${headers.map(h => `<th>${h}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>` : `<div class="empty-state">${esc(empty)}</div>`;
  }

  function renderAmbiguousWrites() {
    find('#ops-ambiguous-writes').innerHTML = table(
      ['Priority', 'Operation', 'Tenant / Gateway / Resource', 'Correlation', 'Executed', 'Owner', 'State', ''],
      state.ambiguousWrites.map(item => `<tr class="${item.state === 'Resolved' ? '' : 'is-warning'}">
        <td>${chip(item.priority, 'inactive')}</td>
        <td><code>${esc(item.operationId)}</code></td>
        <td>${esc(item.tenantId)}<small class="ops-cell-note">${esc(item.gatewayId)} / ${esc(item.resourceId)}</small></td>
        <td><code>${esc(item.correlationId)}</code></td><td>${date(item.executedAtUtc)}</td>
        <td>${esc(item.owner || 'Unassigned')}</td><td>${chip(item.state, item.state === 'Resolved' ? 'active' : 'inactive')}</td>
        <td><button class="btn btn-outline btn-sm" data-action="ambiguous-review" data-id="${item.id}">Review case</button></td></tr>`),
      'No ambiguous Gateway writes have been recorded.');
  }

  function ambiguousCaseBody(item) {
    const events = table(['When', 'Event', 'Actor', 'Evidence / note', 'Resolution'], (item.events || []).map(entry =>
      `<tr><td>${date(entry.createdAtUtc)}</td><td>${esc(entry.eventType)}</td><td>${esc(entry.actor)}</td><td>${esc(entry.evidenceReference || entry.note || '—')}</td><td>${esc(entry.resolution || '—')}</td></tr>`), 'No case events were recorded.');
    const actions = item.state === 'Resolved' ? '' : `<div class="modal-actions">
      <button class="btn btn-outline" data-action="ambiguous-ack" data-id="${item.id}">Acknowledge</button>
      <button class="btn btn-outline" data-action="ambiguous-assign" data-id="${item.id}">Assign</button>
      <button class="btn btn-outline" data-action="ambiguous-evidence" data-id="${item.id}">Add evidence</button>
      <button class="btn btn-primary" data-action="ambiguous-resolve" data-id="${item.id}">Record verified outcome</button></div>`;
    return `<dl class="ops-facts"><div><dt>Operation</dt><dd><code>${esc(item.operationId)}</code></dd></div><div><dt>Tenant</dt><dd>${esc(item.tenantId)}</dd></div><div><dt>Gateway</dt><dd>${esc(item.gatewayId)}</dd></div><div><dt>Resource</dt><dd>${esc(item.resourceId)}</dd></div><div><dt>Correlation</dt><dd><code>${esc(item.correlationId)}</code></dd></div><div><dt>Executed</dt><dd>${date(item.executedAtUtc)}</dd></div><div><dt>Owner</dt><dd>${esc(item.owner || 'Unassigned')}</dd></div><div><dt>State</dt><dd>${esc(item.state)}</dd></div></dl><h4>Immutable event history</h4>${events}${actions}`;
  }

  function renderAuthority() {
    find('#ops-approvals').innerHTML = table(['Report', 'Requester', 'Reason', 'Requested', ''], state.approvals.map(r => `<tr><td><strong>${esc(r.reportTitle)}</strong></td><td>${esc(r.requesterUserName)}<small class="ops-cell-note">${esc(r.requesterEmail)}</small></td><td>${esc(r.reason || 'No reason supplied')}</td><td>${date(r.createdAt)}</td><td class="table-actions"><button class="btn btn-primary btn-sm" data-action="approve" data-id="${r.id}">Approve</button><button class="btn btn-outline btn-sm" data-action="deny" data-id="${r.id}">Deny</button></td></tr>`), 'No report access requests are awaiting a decision.');
    find('#ops-accounts').innerHTML = table(['Account', 'Owner', 'Scope', 'Expiry / last use', 'Status', ''], state.accounts.map(a => { const owner = state.users.find(u => u.id === a.ownerUserId); const status = a.revokedAt ? 'Revoked' : !a.isEnabled ? 'Disabled' : a.expiresAt && new Date(a.expiresAt) <= new Date() ? 'Expired' : 'Active'; return `<tr><td><strong>${esc(a.name)}</strong><small class="ops-cell-note"><code>${esc(a.clientId)}</code></small></td><td>${esc(owner?.username || owner?.userName || `User ${a.ownerUserId}`)}</td><td>${a.scopes.map(s => chip(s)).join(' ')}</td><td>${date(a.expiresAt)}<small class="ops-cell-note">Last use: ${date(a.lastUsedAt)}</small></td><td>${chip(status, status === 'Active' ? 'active' : 'inactive')}</td><td class="table-actions"><button class="btn btn-outline btn-sm" data-action="account-history" data-id="${esc(a.id)}">Audit</button>${!a.revokedAt ? `<button class="btn btn-outline btn-sm" data-action="rotate" data-id="${esc(a.id)}">Rotate</button><button class="btn btn-danger btn-sm" data-action="revoke-account" data-id="${esc(a.id)}">Revoke</button>` : ''}</td></tr>`; }), 'No service accounts have been created.');
    find('#ops-access').innerHTML = table(['Grant', 'Report', 'Creator', 'Expiry', 'Status', ''], state.access.map(a => `<tr><td><strong>${esc(a.name || a.type)}</strong><small class="ops-cell-note">${esc(a.type)}</small></td><td>${esc(a.folderPath)} / ${esc(a.reportName)}</td><td>${esc(a.creatorUsername || `User ${a.createdBy}`)}</td><td>${date(a.expiresAt)}</td><td>${chip(a.status, a.status === 'Active' ? 'active' : 'inactive')}</td><td>${a.status === 'Active' ? `<button class="btn btn-danger btn-sm" data-action="revoke-access" data-type="${esc(a.type)}" data-id="${a.id}">Revoke</button>` : ''}</td></tr>`), 'No anonymous share or embed grants exist.');
  }

  function renderServices() {
    find('#ops-services').innerHTML = state.services.length ? `<div class="card ops-service-list">${state.services.map(s => { const next = s.enabled && s.lastRun?.completedAtUtc ? new Date(new Date(s.lastRun.completedAtUtc).getTime() + s.intervalHours * 3600000) : null; const delivery = [s.smtpAlias, ...(s.recipients || [])].filter(Boolean).join(' · '); return `<button data-action="service-history" data-name="${esc(s.name)}"><span class="ops-service-dot ${s.lastRun?.outcome === 'Failed' ? 'is-warning' : ''}"></span><strong>${esc(s.name)}</strong><span>${s.enabled ? `Every ${s.intervalHours}h` : 'Disabled'}${delivery ? ` · ${esc(delivery)}` : ''}</span><small>${s.lastRun ? `${esc(s.lastRun.outcome)} · ${date(s.lastRun.completedAtUtc)}` : 'No recorded run'}${next ? ` · next ${date(next)}` : ''}</small><span aria-hidden="true">›</span></button>`; }).join('')}</div>` : '<div class="empty-state">Administrative service status is unavailable.</div>';
  }

  /**
   * @param {Object} modal
   * @param {string} modal.title
   * @param {string} [modal.kicker]
   * @param {string} modal.body
   * @param {string} [modal.submit]
   * @param {Function} [modal.onSubmit] Omitted for a modal that only shows something; the submit
   *   button is hidden when there is nothing to submit.
   * @param {boolean} [modal.destructive]
   */
  function showModal({ title, kicker = 'Operations', body, submit = 'Continue', onSubmit, destructive = false }) {
    find('#ops-modal-title').textContent = title; find('#ops-modal-kicker').textContent = kicker;
    find('#ops-modal-body').innerHTML = body; find('#ops-modal-error').textContent = '';
    const button = find('#ops-modal-submit'); button.textContent = submit; button.className = `btn ${destructive ? 'btn-danger' : 'btn-primary'}`; button.style.display = onSubmit ? '' : 'none';
    submitHandler = onSubmit || null; find('#ops-modal').style.display = 'flex'; find('#ops-modal [data-action="close-modal"]').focus();
  }
  function closeModal() { find('#ops-modal-body').replaceChildren(); find('#ops-modal').style.display = 'none'; submitHandler = null; }
  function modalError(error) { const el = find('#ops-modal-error'); el.textContent = error?.message || String(error); el.classList.add('show'); }

  async function load() {
    find('#ops-error').classList.remove('show');
    const requests = [adminApi.operationalMetrics(), adminApi.fleetStatus(), adminApi.gatewayAmbiguousWrites(true), adminApi.pendingAccessRequests(), adminApi.listServiceAccounts(), adminApi.anonymousReportAccess(), adminApi.listAdminServices(), adminApi.listUsers()];
    const results = await Promise.allSettled(requests); const keys = ['metrics', 'fleet', 'ambiguousWrites', 'approvals', 'accounts', 'access', 'services', 'users'];
    const failures = [];
    results.forEach((result, index) => { if (result.status === 'fulfilled') state[keys[index]] = result.value; else failures.push(keys[index]); });
    if (failures.length) { const error = find('#ops-error'); error.textContent = `Some operational sources are unavailable: ${failures.join(', ')}. Available sections remain current.`; error.classList.add('show'); }
    renderSignals(); renderNow(); renderAmbiguousWrites(); renderAuthority(); renderServices();
  }

  function accountForm() {
    const users = state.users.filter(u => u.isActive !== false).map(u => `<option value="${u.id}">${esc(u.username || u.userName || u.email)}</option>`).join('');
    return `<div class="form-row"><div class="form-group"><label for="ops-account-name">Name</label><input id="ops-account-name" maxlength="100"></div><div class="form-group"><label for="ops-account-owner">Owner</label><select id="ops-account-owner">${users}</select></div></div><div class="form-group"><label for="ops-account-description">Description</label><textarea id="ops-account-description" maxlength="500" rows="2"></textarea></div><fieldset class="ops-fieldset"><legend>Scopes</legend><label><input type="checkbox" name="ops-scope" value="portal.read" checked> Portal read</label><label><input type="checkbox" name="ops-scope" value="reports.execute"> Execute reports</label><label><input type="checkbox" name="ops-scope" value="orchestrator.read"> View orchestrator jobs and history</label><label><input type="checkbox" name="ops-scope" value="orchestrator.execute"> Trigger, kill and resume jobs</label><label><input type="checkbox" name="ops-scope" value="orchestrator.publish"> Create and manage own jobs</label><label><input type="checkbox" name="ops-scope" value="orchestrator.admin"> Administer grants and the service</label></fieldset><div class="form-row"><div class="form-group"><label for="ops-account-roles">Roles (comma separated, cannot include Admin)</label><input id="ops-account-roles" placeholder="Viewer"></div><div class="form-group"><label for="ops-account-expiry">Expiry (optional)</label><input id="ops-account-expiry" type="datetime-local"></div></div>`;
  }

  function revealSecret(result) {
    showModal({ title: 'Copy the client secret now', kicker: 'One-time credential', submit: '', body: `<p>This secret will not be shown again. Store it in an approved secret manager.</p><div class="ops-secret"><code id="ops-secret-value">${esc(result.clientSecret)}</code><button class="btn btn-primary btn-sm" data-action="copy-secret">Copy</button></div><dl class="ops-facts"><div><dt>Client ID</dt><dd><code>${esc(result.account.clientId)}</code></dd></div></dl>` });
  }

  host.addEventListener('click', async (event) => {
    const button = event.target.closest('button'); if (!button) return;
    const action = button.dataset.action;
    if (button.dataset.target) { find(`#${button.dataset.target}`)?.focus(); find(`#${button.dataset.target}`)?.scrollIntoView({ behavior: 'smooth' }); return; }
    if (action === 'refresh') return load();
    if (action === 'close-modal') return closeModal();
    const ambiguous = state.ambiguousWrites.find(item => item.id === +button.dataset.id);
    if (action === 'ambiguous-review') return showModal({ title: `Ambiguous write ${ambiguous.operationId}`, kicker: 'High-priority Gateway triage', body: ambiguousCaseBody(ambiguous), onSubmit: null });
    if (action === 'ambiguous-ack') return showModal({ title: 'Acknowledge ambiguous write', body: '<div class="form-group"><label for="ops-case-note">Operator note</label><textarea id="ops-case-note" maxlength="4000" rows="3"></textarea></div>', submit: 'Acknowledge', onSubmit: async () => { await adminApi.acknowledgeGatewayAmbiguousWrite(ambiguous.id, { version: ambiguous.version, note: find('#ops-case-note').value }); closeModal(); await load(); } });
    if (action === 'ambiguous-assign') return showModal({ title: 'Assign ambiguous write', body: '<div class="form-group"><label for="ops-case-owner">Owner</label><input id="ops-case-owner" maxlength="256"></div><div class="form-group"><label for="ops-case-note">Note</label><textarea id="ops-case-note" maxlength="4000" rows="2"></textarea></div>', submit: 'Assign', onSubmit: async () => { await adminApi.assignGatewayAmbiguousWrite(ambiguous.id, { version: ambiguous.version, owner: find('#ops-case-owner').value, note: find('#ops-case-note').value }); closeModal(); await load(); } });
    if (action === 'ambiguous-evidence') return showModal({ title: 'Attach reconciliation evidence', body: '<div class="form-group"><label for="ops-case-evidence">Evidence reference</label><input id="ops-case-evidence" maxlength="1000" placeholder="Ticket, query result, or external verification reference"></div><div class="form-group"><label for="ops-case-note">Evidence note</label><textarea id="ops-case-note" maxlength="4000" rows="3"></textarea></div>', submit: 'Add evidence', onSubmit: async () => { await adminApi.addGatewayAmbiguousWriteEvidence(ambiguous.id, { version: ambiguous.version, evidenceReference: find('#ops-case-evidence').value, note: find('#ops-case-note').value }); closeModal(); await load(); } });
    if (action === 'ambiguous-resolve') return showModal({ title: 'Record externally verified outcome', body: '<div class="form-group"><label for="ops-case-resolution">Outcome</label><select id="ops-case-resolution"><option>confirmed committed</option><option>confirmed not applied</option><option>compensated</option><option>superseded</option></select></div><div class="form-group"><label for="ops-case-evidence">Evidence reference</label><input id="ops-case-evidence" maxlength="1000"></div><div class="form-group"><label for="ops-case-note">Verification note</label><textarea id="ops-case-note" maxlength="4000" rows="3"></textarea></div>', submit: 'Record outcome', onSubmit: async () => { await adminApi.resolveGatewayAmbiguousWrite(ambiguous.id, { version: ambiguous.version, resolution: find('#ops-case-resolution').value, evidenceReference: find('#ops-case-evidence').value, note: find('#ops-case-note').value }); closeModal(); await load(); } });
    if (action === 'copy-secret') { await navigator.clipboard.writeText(find('#ops-secret-value').textContent); button.textContent = 'Copied'; return; }
    if (action === 'create-account') return showModal({ title: 'Create service account', body: accountForm(), submit: 'Create account', onSubmit: async () => { const scopes = [...host.querySelectorAll('input[name="ops-scope"]:checked')].map(i => i.value); const result = await adminApi.createServiceAccount({ name: find('#ops-account-name').value, description: find('#ops-account-description').value, ownerUserId: +find('#ops-account-owner').value, scopes, roles: find('#ops-account-roles').value.split(',').map(v => v.trim()).filter(Boolean), expiresAt: find('#ops-account-expiry').value || null }); closeModal(); await load(); revealSecret(result); } });
    const account = state.accounts.find(a => a.id === button.dataset.id);
    if (action === 'rotate') return showModal({ title: `Rotate ${account.name}?`, body: '<p>Existing credentials stop working immediately. The replacement secret is displayed once.</p>', submit: 'Rotate secret', onSubmit: async () => { const result = await adminApi.rotateServiceAccount(account.id, account.version); closeModal(); await load(); revealSecret(result); }, destructive: true });
    if (action === 'revoke-account') return showModal({ title: `Revoke ${account.name}?`, body: '<p>This permanently disables the machine identity and invalidates its tokens.</p>', submit: 'Revoke account', onSubmit: async () => { await adminApi.revokeServiceAccount(account.id, account.version); closeModal(); await load(); }, destructive: true });
    if (action === 'account-history') { const history = await adminApi.auditLog(1, 100, '', '', 'ServiceAccount', account.id); return showModal({ title: `${account.name} audit history`, body: table(['When', 'Actor', 'Action', 'Detail'], history.items.map(h => `<tr><td>${date(h.timestamp)}</td><td>${esc(h.username || `User ${h.userId}`)}</td><td><code>${esc(h.action)}</code></td><td>${esc(h.detail || '—')}</td></tr>`), 'No audit events recorded.'), onSubmit: null }); }
    if (action === 'approve' || action === 'deny') { const request = state.approvals.find(r => r.id === +button.dataset.id); const approve = action === 'approve'; return showModal({ title: `${approve ? 'Approve' : 'Deny'} access to ${request.reportTitle}`, body: `${approve ? '<div class="form-group"><label for="ops-permission">Permission</label><select id="ops-permission"><option>Read</option><option>Execute</option><option>Author</option><option>Manage</option></select></div>' : ''}<div class="form-group"><label for="ops-reason">Decision reason</label><textarea id="ops-reason" rows="3"></textarea></div>`, submit: approve ? 'Approve request' : 'Deny request', destructive: !approve, onSubmit: async () => { const reason = find('#ops-reason').value; if (approve) await adminApi.approveAccessRequest(request.id, { permission: find('#ops-permission').value, decisionReason: reason }); else await adminApi.denyAccessRequest(request.id, { decisionReason: reason }); closeModal(); await load(); } }); }
    if (action === 'revoke-access') return showModal({ title: 'Revoke anonymous access?', body: '<p>The selected share or embed grant will stop resolving immediately. No token value is exposed to this page.</p>', submit: 'Revoke access', destructive: true, onSubmit: async () => { await adminApi.revokeAnonymousReportAccess(button.dataset.type, +button.dataset.id); closeModal(); await load(); } });
    if (action === 'service-history') { const rows = await adminApi.adminServiceHistory(button.dataset.name); return showModal({ title: `${button.dataset.name} run history`, body: table(['Started', 'Outcome', 'Attempts', 'Node', 'Detail'], rows.map(r => `<tr><td>${date(r.startedAtUtc)}</td><td>${chip(r.outcome, r.outcome === 'Succeeded' ? 'active' : 'inactive')}</td><td>${r.attempts}</td><td>${esc(r.nodeName || '—')}</td><td>${esc(r.detail || '—')}</td></tr>`), 'No runs recorded.'), onSubmit: null }); }
  });
  find('#ops-modal-submit').addEventListener('click', async () => { if (!submitHandler) return; try { await submitHandler(); } catch (error) { modalError(error); } });
  return { load };
}
