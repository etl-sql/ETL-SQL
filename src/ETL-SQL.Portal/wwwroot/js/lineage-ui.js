const VISUAL_ROLES = new Set(['XAXIS','YAXIS','COLOR','SIZE','LABEL','VALUE','CATEGORY','TOOLTIP','DETAIL','SERIES','AGGREGATE','FILTER','DRILLTHROUGH','BUBBLESIZE','LATITUDE','LONGITUDE']);

const XFORM_CLASSES = {
  Aggregation:     'badge-warning',
  FunctionCall:    'badge-refresh',
  Cast:            'badge-neutral',
  WindowFunction:  'badge-stale',
  Arithmetic:      'badge-neutral',
  CaseExpression:  'badge-stale',
  StringOperation: 'badge-neutral',
  Subquery:        'badge-neutral',
  Conditional:     'badge-stale',
  Literal:         'badge-neutral',
};

const SECURITY_TAGS = { pii: 'badge-error', phi: 'badge-error', pci: 'badge-error', sensitive: 'badge-warning' };
const CLASS_TAGS    = { confidential: 'badge-warning', restricted: 'badge-error', internal: 'badge-neutral', public: 'badge-ok' };
const INFO_TAGS     = new Set(['owner','domain','steward','contact','quality','freshness','sla','nullable','encrypted_at_rest']);

function esc(s) {
  return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function csvCell(value) {
  const text = String(value ?? '');
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function defaultFormatBuiltAt(value) {
  return value ? new Date(value).toLocaleString() : 'Unknown';
}

function defaultTimeAgo(value) {
  return defaultFormatBuiltAt(value);
}

export function formatTags(tags) {
  if (!tags || typeof tags !== 'object') return '';
  return Object.entries(tags)
    .filter(([k]) => k !== 'd')
    .map(([k, v]) => `${k}: ${v}`)
    .join('; ');
}

export function renderLineageRow(row, helpers = {}) {
  const timeAgo = helpers.timeAgo ?? defaultTimeAgo;
  const formatBuiltAt = helpers.formatBuiltAt ?? defaultFormatBuiltAt;
  const target = row.targetColumn ? `${row.targetTable}.${row.targetColumn}` : row.targetTable;
  const srcCols = row.sourceColumns || [];
  const sources = (row.sourceTables || [])
    .map((t, i) => srcCols[i] ? `${t}.${srcCols[i]}` : t)
    .join(', ') || 'None recorded';
  const xform = formatTransformationKind(row.transformationKind, row.transformationExpression, row.functionsApplied);
  const tags = formatTags(row.tags);
  const report = row.reportId
    ? `<button class="btn btn-outline btn-sm" type="button" data-open-report="${row.reportId}">${esc(row.reportName || `Report ${row.reportId}`)}</button>`
    : '<span class="lineage-muted">No report link</span>';

  return `
    <div class="lineage-result-row">
      <div class="lineage-run">
        <strong>${esc(timeAgo(row.runAt))}</strong>
        <span>${esc(formatBuiltAt(row.runAt))}</span>
      </div>
      <div class="lineage-main">
        <div class="lineage-title">
          <code>${esc(target)}</code>
          <span class="badge">${esc(row.operation || 'UNKNOWN')}</span>
          ${xform}
        </div>
        <div class="lineage-detail">Sources: ${esc(sources)}</div>
        ${row.derivedFromDescriptions ? `<div class="lineage-detail">Description: ${esc(row.derivedFromDescriptions)}</div>` : ''}
        <div class="lineage-detail">Job: ${esc(row.jobName || 'Ad hoc')}${row.sourceFile ? ` &middot; File: ${esc(row.sourceFile)}` : ''}</div>
        ${tags ? `<div class="lineage-tags">${esc(tags)}</div>` : ''}
      </div>
      <div class="lineage-report">
        ${report}
        ${row.folderPath ? `<span>${esc(row.folderPath)}</span>` : ''}
      </div>
    </div>`;
}

export function lineageRowsToCsv(rows) {
  const headers = ['RunAt', 'JobName', 'Report', 'Folder', 'Target', 'Operation', 'Transform', 'Sources', 'Description', 'Tags', 'SourceFile', 'Line'];
  const dataRows = (rows || []).map(row => {
    const target = row.targetColumn ? `${row.targetTable}.${row.targetColumn}` : row.targetTable;
    const srcCols = row.sourceColumns || [];
    const sources = (row.sourceTables || []).map((t, i) => srcCols[i] ? `${t}.${srcCols[i]}` : t).join('; ');
    return [
      row.runAt || '',
      row.jobName || '',
      row.reportName || '',
      row.folderPath || '',
      target,
      row.operation || '',
      row.transformationExpression || row.transformationKind || '',
      sources,
      row.derivedFromDescriptions || '',
      formatTags(row.tags),
      row.sourceFile || '',
      row.line ?? ''
    ];
  });
  return [headers, ...dataRows].map(r => r.map(csvCell).join(',')).join('\r\n');
}

export function renderDependencies(data, downstream = [], helpers = {}) {
  const formatBuiltAt = helpers.formatBuiltAt ?? defaultFormatBuiltAt;
  const snapshot = data.snapshot
    ? `<span>Snapshot ${esc(formatBuiltAt(data.snapshot.builtAt))}</span>`
    : '<span>No snapshot yet</span>';
  const manifestRows = (data.manifestDatasets || []).map(d => `
    <tr><td>${esc(d.tempTableName)}</td><td>${esc(d.rowCount ?? 0)}</td><td>${esc(d.refreshInterval || 'Manual')}</td><td>${esc(d.ttl || 'None')}</td></tr>`).join('');
  const registryRows = (data.registeredDatasets || []).map(d => `
    <tr><td>${esc(d.name)}</td><td>${esc(d.folderPath)}</td><td>${esc(d.accessLevel)}</td><td>${esc(d.rowCount ?? 0)}</td><td>${esc((d.sources || []).map(s => s.name).join(', ') || 'Not available')}</td></tr>`).join('');
  const jobRows = (data.refreshJobs || []).map(j => `
    <tr><td>${esc(j.orchestratorJobName)}</td><td>${esc(j.refreshInterval || 'Manual')}</td><td>${esc(j.lastRefreshedAt ? formatBuiltAt(j.lastRefreshedAt) : 'Never')}</td></tr>`).join('');
  const sourceRows = (data.sources || []).map(s => `
    <tr><td>${esc(s.connection || 'Engine')}</td><td>${esc(s.objectName || s.name)}</td><td>${esc(s.kind)}</td></tr>`).join('');

  const meaningful = (data.lineageEntries || []).filter(e => e.target !== 'RESULTSET');
  const lineageRows = meaningful.map(e => {
    const targetCell = esc(e.target);
    const roleCell   = e.targetColumn ? formatLineageRole(e.targetColumn) : '';
    const opCell     = esc(e.operation);
    const xformCell  = formatTransformationKind(e.transformationKind, e.transformationExpression, e.functionsApplied);
    const srcCell    = formatSourcesWithColumns(e.sources, e.sourceColumns);
    const tagsCell   = formatTagBadges(e.tags, e.derivedFromDescriptions);
    return `<tr><td>${targetCell}</td><td>${roleCell}</td><td>${opCell}</td><td>${xformCell}</td><td>${srcCell}</td><td>${tagsCell}</td></tr>`;
  }).join('');

  return `
    <div class="dependency-summary">
      <span>${esc(data.report?.folderPath || '')} / ${esc(data.report?.name || 'Report')}</span>
      ${snapshot}
    </div>
    ${renderDependencyTable('Manifest Datasets', ['Dataset', 'Rows', 'Refresh', 'TTL'], manifestRows)}
    ${renderDependencyTable('Registered Datasets', ['Dataset', 'Folder', 'Access', 'Rows', 'Sources'], registryRows)}
    ${renderDependencyTable('Refresh Jobs', ['Job', 'Interval', 'Last Refresh'], jobRows)}
    ${renderDependencyTable('Sources', ['Connection', 'Object', 'Kind'], sourceRows)}
    ${renderDependencyTable('Lineage and Tags', ['Target', 'Role / Column', 'Operation', 'Transformation', 'Sources -> Columns', 'Tags'], lineageRows || null)}
    ${renderDownstreamImpact(downstream, formatBuiltAt)}`;
}

export function formatLineageRole(col) {
  if (!col) return '';
  if (VISUAL_ROLES.has(col.toUpperCase()))
    return `<span class="badge badge-refresh" style="font-size:.72em">${esc(col)}</span>`;
  return `<code style="font-size:.8em">${esc(col)}</code>`;
}

export function formatTransformationKind(kind, expr, fns) {
  if (!kind || kind === 'PassThrough' || kind === 'Unknown') return '';
  const cls = XFORM_CLASSES[kind] || 'badge-neutral';
  const title = expr ? ` title="${esc(expr)}"` : '';

  let label = kind;
  const fnList = Array.isArray(fns) ? fns : [];
  function leadingFn(s) { const m = s?.match(/^(\w+)\s*\(/); return m?.[1] ?? null; }

  if ((kind === 'Aggregation' || kind === 'FunctionCall' || kind === 'StringOperation' || kind === 'WindowFunction') && fnList.length) {
    const names = fnList.slice(0, 3);
    const suffix = kind === 'FunctionCall' || kind === 'StringOperation' ? '()' : '';
    label = names.length === 1 ? `${names[0]}${suffix}` : names.join(' / ');
    if (kind === 'WindowFunction') label += ' OVER';
  } else if ((kind === 'Aggregation' || kind === 'FunctionCall' || kind === 'StringOperation' || kind === 'WindowFunction') && expr) {
    const fn = leadingFn(expr);
    if (fn) {
      const suffix = kind === 'FunctionCall' || kind === 'StringOperation' ? '()' : '';
      label = `${fn}${suffix}`;
      if (kind === 'WindowFunction') label += ' OVER';
    }
  } else if (kind === 'Cast' && expr) {
    const asMatch   = expr.match(/\bAS\s+(\w+)/i);
    const convMatch = expr.match(/^CONVERT\s*\(\s*(\w+)/i);
    const target = asMatch?.[1] || convMatch?.[1];
    if (target) label = `-> ${target}`;
  }

  return `<span class="badge ${cls}" style="font-size:.72em; cursor:default"${title}>${esc(label)}</span>`;
}

export function formatSourcesWithColumns(sources, sourceCols) {
  const srcs = sources || [];
  const cols = sourceCols || [];
  if (!srcs.length) return '<span style="color:var(--portal-muted)">-</span>';
  return srcs.map((s, i) => {
    const col = cols[i];
    return col ? `<code style="font-size:.8em">${esc(s)}.${esc(col)}</code>` : esc(s);
  }).join('<br>');
}

export function formatTagBadges(tags, derivedFromDescriptions) {
  if ((!tags || typeof tags !== 'object') && !derivedFromDescriptions) return '';
  const parts = [];

  const desc = (tags && tags.d) || derivedFromDescriptions;
  if (desc) parts.push(`<span style="display:block;font-size:.78em;color:var(--portal-text-soft);margin-bottom:3px;font-style:italic">${esc(desc)}</span>`);

  for (const [k, v] of Object.entries(tags || {})) {
    if (k === 'd') continue;
    const key = k.toLowerCase();
    const lowerValue = String(v ?? '').toLowerCase();
    if (SECURITY_TAGS[key] && (lowerValue === 'true' || lowerValue === '1'))
      parts.push(`<span class="badge ${SECURITY_TAGS[key]}" style="font-size:.72em">${esc(k.toUpperCase())}</span>`);
    else if (key === 'classification' && CLASS_TAGS[lowerValue])
      parts.push(`<span class="badge ${CLASS_TAGS[lowerValue]}" style="font-size:.72em">${esc(v)}</span>`);
    else if (INFO_TAGS.has(key))
      parts.push(`<span class="chip badge-neutral" style="font-size:.72em;padding:1px 6px;border-radius:8px;background:var(--portal-surface-subtle);color:var(--portal-muted)">${esc(k)}: ${esc(v)}</span>`);
    else
      parts.push(`<span style="font-size:.78em;color:var(--portal-muted)">${esc(k)}: ${esc(v)}</span>`);
  }
  return parts.join(' ');
}

function renderDependencyTable(title, headers, rows) {
  return `
    <div class="dependency-section">
      <h4>${esc(title)}</h4>
      ${rows ? `<div class="table-scroll"><table class="dependency-table">
        <thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead>
        <tbody>${rows}</tbody>
      </table></div>` : '<p class="text-muted">No data available.</p>'}
    </div>`;
}

function renderDownstreamImpact(downstream, formatBuiltAt) {
  const items = (downstream || []).filter(d => d.reportName);
  const title = 'Downstream Impact';
  if (!items.length) {
    return `<div class="dependency-section"><h4>${esc(title)}</h4><p class="text-muted">No downstream consumers found in lineage catalog.</p></div>`;
  }
  const chips = items.map(d => {
    const path = d.folderPath ? `${esc(d.folderPath)} / ` : '';
    const runs = `${d.runCount} run${d.runCount !== 1 ? 's' : ''}`;
    const last = esc(formatBuiltAt(d.lastSeen));
    const inner = `<span class="viewer-meta-pill" title="${path}${esc(d.reportName)} &middot; ${runs} &middot; last ${last}">${path}${esc(d.reportName)}</span>`;
    const rid = Number(d.reportId);
    return Number.isInteger(rid) && rid > 0
      ? `<a href="#" data-downstream-report-id="${rid}" style="text-decoration:none">${inner}</a>`
      : inner;
  }).join(' ');
  return `<div class="dependency-section"><h4>${esc(title)}</h4>
    <p class="text-sm text-muted" style="margin:0 0 8px">Source tables in this report are consumed by ${items.length} other report${items.length !== 1 ? 's' : ''}:</p>
    <div class="viewer-meta" style="flex-wrap:wrap;gap:6px">${chips}</div></div>`;
}
