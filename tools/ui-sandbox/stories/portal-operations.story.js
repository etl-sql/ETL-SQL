const fixtures = [
  { id: 'attention', label: 'Needs attention' },
  { id: 'steady', label: 'Steady state' },
];

function metric(label, value, note, tone = '') {
  return `<article class="ops-signal ${tone}"><span>${label}</span><strong>${value}</strong><small>${note}</small></article>`;
}

function markup(fixtureId) {
  const attention = fixtureId === 'attention';
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <link rel="stylesheet" href="/src/ETL-SQL.Portal/wwwroot/css/portal.css"></head><body><main class="ops-hub">
    <header class="ops-heading"><div><span class="library-kicker">Operations</span><h1>Control room</h1><p>Trace live signals to the identity, access grant, node, or service run responsible.</p></div><button class="btn btn-outline btn-sm">Refresh snapshot</button></header>
    <nav class="ops-signal-rail" aria-label="Operational signals">
      ${metric('Fleet', attention ? 'Degraded' : 'Healthy', '1 environment · schema current', attention ? 'is-warning' : 'is-good')}
      ${metric('Execution queue', attention ? '12' : '2', attention ? 'Oldest waiting 4m' : 'Within capacity')}
      ${metric('Approvals', attention ? '3' : '0', attention ? 'Awaiting a decision' : 'Nothing pending', attention ? 'is-warning' : '')}
      ${metric('Audit delivery', attention ? '18' : '0', attention ? 'Oldest pending 9m' : 'Outbox clear', attention ? 'is-warning' : 'is-good')}
    </nav>
    <section class="ops-lane"><header><span>01</span><div><h2>Now</h2><p>Runtime health and deployment readiness.</p></div></header><div class="ops-grid">
      <article class="card ops-card"><div class="card-header"><h3>Fleet status</h3><span class="status-badge ${attention ? 'status-warning' : 'status-success'}">${attention ? 'Degraded' : 'Healthy'}</span></div><dl class="ops-facts"><div><dt>Node</dt><dd>portal-chi-01</dd></div><div><dt>Version</dt><dd>0.17.0</dd></div><div><dt>Schema</dt><dd>Current</dd></div><div><dt>Upgrade</dt><dd>Ready</dd></div></dl></article>
      <article class="card ops-card"><div class="card-header"><h3>Workload</h3><span>24-hour window</span></div><dl class="ops-facts"><div><dt>Active</dt><dd>4 / 16</dd></div><div><dt>Failures</dt><dd>${attention ? '7' : '0'} / 182</dd></div><div><dt>Stale datasets</dt><dd>${attention ? '2' : '0'}</dd></div><div><dt>Storage</dt><dd>18.4 GB</dd></div></dl></article>
    </div></section>
    <section class="ops-lane"><header><span>02</span><div><h2>Authority</h2><p>Machine identities, requests, and anonymous report access.</p></div></header><div class="ops-grid ops-grid-wide">
      <article class="card ops-card"><div class="card-header"><h3>Pending approvals</h3><button class="btn btn-primary btn-sm">Review ${attention ? '3' : '0'}</button></div><p class="text-muted">Report access decisions waiting for a manager.</p></article>
      <article class="card ops-card"><div class="card-header"><h3>Service accounts</h3><button class="btn btn-outline btn-sm">New account</button></div><p><strong>4 active</strong> · 1 expires within 30 days</p><small class="text-muted">Last machine use 11 minutes ago</small></article>
      <article class="card ops-card"><div class="card-header"><h3>Anonymous access</h3><button class="btn btn-outline btn-sm">Inspect</button></div><p><strong>6 active</strong> share or embed grants</p><small class="text-muted">2 created by a disabled user</small></article>
    </div></section>
    <section class="ops-lane"><header><span>03</span><div><h2>Automation</h2><p>Native administrative services and durable run history.</p></div></header><div class="card ops-service-list">
      ${[['Failure digest','Enabled','Succeeded · 07:00'],['Backup report','Enabled', attention ? 'Failed · 06:00' : 'Succeeded · 06:00'],['Capacity report','Disabled','No recent run']].map(([name,state,last]) => `<button><span class="ops-service-dot"></span><strong>${name}</strong><span>${state}</span><small>${last}</small><span aria-hidden="true">›</span></button>`).join('')}
    </div></section>
  </main></body></html>`;
}

export default {
  id: 'portal-operations',
  title: 'Portal Operations hub',
  subtitle: 'A signal-to-owner control room for administrators',
  fixtures,
  async mount(stage, fixtureId, ctx) {
    const frame = document.createElement('iframe');
    frame.title = `Portal Operations — ${fixtureId}`;
    frame.style.cssText = 'display:block;width:100%;height:100%;min-height:820px;border:0;background:white';
    frame.srcdoc = markup(fixtureId);
    stage.replaceChildren(frame);
    ctx.stat(fixtureId === 'attention' ? 'Attention state' : 'Steady state');
    return { resize() {} };
  },
};
