import type { LineageCatalogState } from './lineage-catalog-types.js';

export function esc(s: unknown) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
export function escAttr(s: unknown) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

export function renderLineageEmpty(message = 'Choose a catalog query and run Search.') {
  return `<div class="empty-state empty-state-panel">
    <div class="empty-state-icon empty-state-icon-lineage" aria-hidden="true"></div>
    <h2>No lineage results</h2>
    <p>${esc(message)}</p>
  </div>`;
}

type DateFormatter = (value: string | null | undefined) => string;

export function createLineageRenderers(state: LineageCatalogState, timeAgo: DateFormatter, formatBuiltAt: DateFormatter) {
  function renderStewardshipMetric(label: string, value: number | null | undefined) {
    return `<div class="stewardship-metric">
      <span>${esc(label)}</span>
      <strong>${value == null ? '—' : value.toLocaleString()}</strong>
    </div>`;
  }

  function renderStewardshipAsset(item: StewardshipAssetDto) {
    const tags = item.tags || {};
    const missing = Array.isArray(item.missingTags) ? item.missingTags : [];
    const sourceList = Array.isArray(item.sourceTables) && item.sourceTables.length
      ? item.sourceTables.join(', ')
      : 'No source recorded';
    const badges = [
      item.isRestricted ? '<span class="stewardship-badge stewardship-badge-danger">restricted</span>' : '',
      item.isSensitive ? '<span class="stewardship-badge stewardship-badge-warn">sensitive</span>' : '',
      item.isStale ? '<span class="stewardship-badge">stale</span>' : '',
      missing.length ? `<span class="stewardship-badge">${missing.length} missing</span>` : '',
    ].filter(Boolean).join('');
    const tagChips = Object.entries(tags).slice(0, 8)
      .map(([key, value]) => `<span class="stewardship-chip"><b>@${esc(key)}</b>${esc(value)}</span>`)
      .join('');
    const missingText = missing.length
      ? `<div class="stewardship-gap">Missing: ${missing.map(t => `@${esc(t)}`).join(', ')}</div>`
      : '';
    return `<article class="stewardship-row">
      <div class="stewardship-row-main">
        <div class="stewardship-row-title">
          <code>${esc(item.targetTable)}${item.targetColumn ? `.${esc(item.targetColumn)}` : ''}</code>
          ${badges}
        </div>
        <div class="lineage-detail">Sources: ${esc(sourceList)}</div>
        <div class="lineage-detail">Steward: ${esc(item.steward || 'Unassigned')} &middot; Owner: ${esc(item.owner || 'Unassigned')} &middot; Domain: ${esc(item.domain || 'Unassigned')}</div>
        ${missingText}
        ${tagChips ? `<div class="stewardship-tags">${tagChips}</div>` : ''}
      </div>
      <div class="stewardship-row-meta">
        <strong>${esc(timeAgo(item.runAt))}</strong>
        <span>${esc(formatBuiltAt(item.runAt))}</span>
        <span>${esc(item.staleReason || 'Fresh')}</span>
      </div>
    </article>`;
  }

  function renderStewardshipFacetOptions(values: readonly StewardshipFacetDto[] | null | undefined, selected: string) {
    return (Array.isArray(values) ? values : [])
      .map(v => `<option value="${escAttr(v.value)}"${v.value === selected ? ' selected' : ''}>${esc(v.value)} (${Number(v.count || 0).toLocaleString()})</option>`)
      .join('');
  }

  function renderProtectedAuditRow(row: ProtectedLineageHistoryEntry) {
    const target = `${row.targetTable || ''}${row.targetColumn ? `.${row.targetColumn}` : ''}`;
    const sources = Array.isArray(row.sourceTables) && row.sourceTables.length ? row.sourceTables.join(', ') : 'No source recorded';
    const protectionTags = Array.isArray(row.protectionTags) ? row.protectionTags.join(', ') : row.protectionReason || '';
    return `<article class="stewardship-row steward-audit-row">
      <div class="stewardship-row-main">
        <div class="stewardship-row-title">
          <code>${esc(target)}</code>
          <span class="stewardship-badge stewardship-badge-danger">protected</span>
          ${isStaleAuditRow(row, state.stewardshipStaleDays) ? '<span class="stewardship-badge">stale</span>' : ''}
        </div>
        <div class="lineage-detail">Protection: ${esc(protectionTags || 'classified')}</div>
        <div class="lineage-detail">Sources: ${esc(sources)}</div>
        <div class="lineage-detail">Steward: ${esc(row.steward || 'Unassigned')} &middot; Owner: ${esc(row.owner || 'Unassigned')} &middot; Domain: ${esc(row.domain || 'Unassigned')}</div>
      </div>
      <div class="stewardship-row-meta">
        <strong>${esc(timeAgo(row.runAt))}</strong>
        <span>${esc(row.classification || 'Unclassified')}</span>
        <span>${esc(row.jobName || 'Ad hoc')}</span>
      </div>
    </article>`;
  }

  function renderSuggestionAuditRow(row: ProtectedDataSuggestionEntry) {
    const target = `${row.targetTable || ''}${row.targetColumn ? `.${row.targetColumn}` : ''}`;
    const confidence = Math.round(Number(row.confidence || 0) * 100);
    return `<article class="stewardship-row steward-audit-row">
      <div class="stewardship-row-main">
        <div class="stewardship-row-title">
          <code>${esc(target)}</code>
          <span class="stewardship-badge stewardship-badge-warn">review</span>
          <span class="stewardship-badge">${esc(row.suggestedTag)}=${esc(row.suggestedValue)}</span>
        </div>
        <div class="lineage-detail">${esc(row.reason || 'Classifier suggestion')}</div>
        <div class="lineage-detail">Evidence: ${esc(row.evidenceKind || 'Rule')} &middot; ${esc(row.evidence || '')}</div>
      </div>
      <div class="stewardship-row-meta">
        <strong>${confidence}%</strong>
        <span>${esc(timeAgo(row.runAt))}</span>
        <span>${esc(row.jobName || 'Ad hoc')}</span>
      </div>
    </article>`;
  }

  function renderAuditEvent(event: AuditLogDto) {
    return `<article class="impact-row">
      <div>
        <strong>${esc(event.resourceId || event.resourceType || event.action || 'Audit event')}</strong>
        <small>${esc(event.action || 'Audit')}</small>
      </div>
      <div>
        ${event.username ? `<span>${esc(event.username)}</span>` : ''}
        ${event.timestamp ? `<span>${esc(timeAgo(event.timestamp))}</span>` : ''}
        ${event.detail ? `<span>${esc(event.detail)}</span>` : ''}
      </div>
    </article>`;
  }

  function renderAuditImpactList(impact: { reports: string[]; datasets: string[]; subscriptions: string[] }) {
    const rows = [
      ...impact.reports.map(name => ({ type: 'Report', name })),
      ...impact.datasets.map(name => ({ type: 'Dataset', name })),
      ...impact.subscriptions.map(name => ({ type: 'Subscription', name })),
    ];
    if (!rows.length) return renderLineageEmpty('No named report, dataset, or subscription references appear in the returned protected lineage.');
    return `<div class="impact-list">${rows.slice(0, 12).map(row => `<article class="impact-row">
      <div><strong>${esc(row.name)}</strong><small>${esc(row.type)}</small></div>
      <div><span>Reference recorded in protected lineage</span></div>
    </article>`).join('')}</div>`;
  }

  function isStaleAuditRow(row: ProtectedLineageHistoryEntry, staleDays: number) {
    if (!row?.runAt) return false;
    const runAt = new Date(row.runAt).getTime();
    if (!Number.isFinite(runAt)) return false;
    return Date.now() - runAt > Math.max(1, Number(staleDays || 30)) * 86400000;
  }

  function renderImpactSection(title: string, items: readonly LineageImpactItemDto[] | null | undefined) {
    items = Array.isArray(items) ? items : [];
    if (!items.length) return '';
    return `<section class="impact-section">
      <h3>${esc(title)}</h3>
      <div class="impact-list">${items.map(renderImpactItem).join('')}</div>
    </section>`;
  }

  function renderImpactItem(item: LineageImpactItemDto) {
    const detail = item.detail ? `<span>${esc(item.detail)}</span>` : '';
    const seen = item.lastSeen ? `<span>${esc(timeAgo(item.lastSeen))}</span>` : '';
    const count = item.count != null ? `<span>${Number(item.count).toLocaleString()} observations</span>` : '';
    return `<article class="impact-row">
      <div>
        <strong>${esc(item.name)}</strong>
        <small>${esc(item.type)}</small>
      </div>
      <div>${detail}${seen}${count}</div>
    </article>`;
  }
  return { renderLineageEmpty, renderStewardshipMetric, renderStewardshipAsset, renderStewardshipFacetOptions, renderProtectedAuditRow, renderSuggestionAuditRow, renderAuditEvent, renderAuditImpactList, isStaleAuditRow, renderImpactSection, renderImpactItem };
}

export function renderLineageCatalogShell(state: LineageCatalogState, allowAudit: boolean,
  renderStewardshipFacetOptions: (values: readonly StewardshipFacetDto[] | null | undefined, selected: string) => string) {
    const valueLabel = state.kind === 'tag' ? 'Tag key' : state.kind === 'source-file' ? 'Source file' : 'Name';
    const stewardship = state.stewardAudit?.stewardship ?? state.stewardship;
    const isStewardshipMode = state.mode === 'stewardship';
    const isProtectedMode = state.mode === 'protected';
    const isAuditMode = state.mode === 'audit';
    const isImpactMode = state.mode === 'impact';
    const title = isProtectedMode
      ? 'Protected Data & Tags'
      : isAuditMode
        ? 'Governance Audit Log'
        : isStewardshipMode
          ? 'Stewardship & Health'
          : isImpactMode
            ? 'Impact Analysis'
            : 'Lineage Explorer';
    const subtitle = isProtectedMode
      ? 'PII classification, sensitive dataset discovery, and tag suggestions'
      : isAuditMode
        ? 'Protected data, metadata gaps, stale assets, impact, and audit delivery'
        : isStewardshipMode
          ? 'Metadata gaps, sensitive assets, stale lineage, and steward queues'
          : isImpactMode
            ? 'Upstream and downstream blast radius across reports, datasets, jobs, and stewards'
          : 'Cross-run history with report context';
    const savedOptions = state.savedViews
      .map(v => `<option value="${escAttr(v.name)}"${v.name === state.selectedView ? ' selected' : ''}>${esc(v.name)}</option>`)
      .join('');
    return `
      <div class="library-toolbar lineage-toolbar">
        <div class="library-title">
          <span class="library-kicker">Data Governance</span>
          <h2>${esc(title)}</h2>
          <span class="folder-count">${esc(subtitle)}</span>
        </div>
        <div class="lineage-mode-toggle" role="tablist" aria-label="Catalog mode">
          <button type="button" class="lineage-mode-btn ${state.mode === 'history' ? 'active' : ''}" role="tab" data-lineage-mode="history" aria-selected="${state.mode === 'history'}">History</button>
          <button type="button" class="lineage-mode-btn ${state.mode === 'stewardship' ? 'active' : ''}" role="tab" data-lineage-mode="stewardship" aria-selected="${state.mode === 'stewardship'}">Stewardship</button>
          <button type="button" class="lineage-mode-btn ${state.mode === 'protected' ? 'active' : ''}" role="tab" data-lineage-mode="protected" aria-selected="${state.mode === 'protected'}">Protected</button>
          ${allowAudit ? `<button type="button" class="lineage-mode-btn ${state.mode === 'audit' ? 'active' : ''}" role="tab" data-lineage-mode="audit" aria-selected="${state.mode === 'audit'}">Audit</button>` : ''}
          <button type="button" class="lineage-mode-btn ${state.mode === 'impact' ? 'active' : ''}" role="tab" data-lineage-mode="impact" aria-selected="${state.mode === 'impact'}">Impact</button>
        </div>
        <form id="lineageSearchForm" class="lineage-query" autocomplete="off" ${state.mode === 'history' ? '' : 'hidden style="display:none"'}>
          <select id="lineageKind" class="library-sort" aria-label="Lineage query type">
            <option value="table"${state.kind === 'table' ? ' selected' : ''}>Target table</option>
            <option value="source"${state.kind === 'source' ? ' selected' : ''}>Source table</option>
            <option value="source-file"${state.kind === 'source-file' ? ' selected' : ''}>Source file</option>
            <option value="tag"${state.kind === 'tag' ? ' selected' : ''}>Tag</option>
            <option value="job"${state.kind === 'job' ? ' selected' : ''}>Job</option>
          </select>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="lineageQuery" type="search" placeholder="${escAttr(valueLabel)}" value="${escAttr(state.query)}">
          </label>
          <input id="lineageColumn" class="lineage-input lineage-column" type="search" placeholder="Column" value="${escAttr(state.column)}" ${state.kind === 'table' ? '' : 'hidden style="display:none"'}>
          <input id="lineageTagValue" class="lineage-input" type="search" placeholder="Tag value" value="${escAttr(state.tagValue)}" ${state.kind === 'tag' ? '' : 'hidden style="display:none"'}>
          <input id="lineageFrom" class="lineage-date" type="date" value="${escAttr(state.from)}" aria-label="From date">
          <input id="lineageTo" class="lineage-date" type="date" value="${escAttr(state.to)}" aria-label="To date">
          <button class="btn btn-primary" type="submit">Search</button>
          <button class="btn btn-outline" id="lineageExportBtn" type="button" ${state.rows.length ? '' : 'disabled'}>Export CSV</button>
          <button class="btn btn-outline" id="lineageViewToggle" type="button">${state.view === 'graph' ? 'Table' : 'Graph'}</button>
          <select id="lineageSavedView" class="library-sort lineage-saved" aria-label="Saved lineage view">
            <option value="">Saved views</option>
            ${savedOptions}
          </select>
          <button class="btn btn-outline" id="lineageSaveViewBtn" type="button">Save View</button>
          <button class="btn btn-outline" id="lineageDeleteViewBtn" type="button" ${state.selectedView ? '' : 'disabled'}>Delete</button>
        </form>
        <form id="stewardshipSearchForm" class="lineage-query stewardship-query" autocomplete="off" ${state.mode === 'stewardship' ? '' : 'hidden style="display:none"'}>
          <select id="stewardshipView" class="library-sort" aria-label="Stewardship view">
            <option value="all"${state.stewardshipView === 'all' ? ' selected' : ''}>All assets</option>
            <option value="sensitive"${state.stewardshipView === 'sensitive' ? ' selected' : ''}>Sensitive</option>
            <option value="missing"${state.stewardshipView === 'missing' ? ' selected' : ''}>Missing metadata</option>
            <option value="stale"${state.stewardshipView === 'stale' ? ' selected' : ''}>Stale lineage</option>
            <option value="queue"${state.stewardshipView === 'queue' ? ' selected' : ''}>Steward queue</option>
          </select>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="stewardshipQuery" type="search" placeholder="Search assets or tags" value="${escAttr(state.stewardshipQuery)}">
          </label>
          <select id="stewardshipSteward" class="library-sort lineage-saved" aria-label="Filter by steward">
            <option value="">All stewards</option>
            ${renderStewardshipFacetOptions(stewardship?.stewards, state.stewardshipSteward)}
          </select>
          <select id="stewardshipDomain" class="library-sort lineage-saved" aria-label="Filter by domain">
            <option value="">All domains</option>
            ${renderStewardshipFacetOptions(stewardship?.domains, state.stewardshipDomain)}
          </select>
          <input id="stewardshipStaleDays" class="lineage-input stewardship-days" type="number" min="1" max="3660" value="${escAttr(state.stewardshipStaleDays)}" aria-label="Stale after days">
          <button class="btn btn-primary" type="submit">Search</button>
        </form>
        <form id="protectedSearchForm" class="lineage-query stewardship-query" autocomplete="off" ${state.mode === 'protected' ? '' : 'hidden style="display:none"'}>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="protectedQuery" type="search" placeholder="Search protected assets or tags" value="${escAttr(state.stewardshipQuery)}">
          </label>
          <select id="protectedSteward" class="library-sort lineage-saved" aria-label="Filter by steward">
            <option value="">All stewards</option>
            ${renderStewardshipFacetOptions(stewardship?.stewards, state.stewardshipSteward)}
          </select>
          <select id="protectedDomain" class="library-sort lineage-saved" aria-label="Filter by domain">
            <option value="">All domains</option>
            ${renderStewardshipFacetOptions(stewardship?.domains, state.stewardshipDomain)}
          </select>
          <button class="btn btn-primary" type="submit">Search</button>
        </form>
        <form id="impactSearchForm" class="lineage-query stewardship-query" autocomplete="off" ${state.mode === 'impact' ? '' : 'hidden style="display:none"'}>
          <select id="impactKind" class="library-sort" aria-label="Impact target type">
            <option value="table"${state.impactKind === 'table' ? ' selected' : ''}>Table</option>
            <option value="column"${state.impactKind === 'column' ? ' selected' : ''}>Column</option>
            <option value="job"${state.impactKind === 'job' ? ' selected' : ''}>Job</option>
            <option value="script"${state.impactKind === 'script' ? ' selected' : ''}>Script</option>
            <option value="dataset"${state.impactKind === 'dataset' ? ' selected' : ''}>Dataset</option>
            <option value="report"${state.impactKind === 'report' ? ' selected' : ''}>Report</option>
            <option value="owner"${state.impactKind === 'owner' ? ' selected' : ''}>Owner</option>
            <option value="steward"${state.impactKind === 'steward' ? ' selected' : ''}>Steward</option>
          </select>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="impactName" type="search" placeholder="Impact target" value="${escAttr(state.impactName)}">
          </label>
          <input id="impactColumn" class="lineage-input lineage-column" type="search" placeholder="Column" value="${escAttr(state.impactColumn)}" ${state.impactKind === 'table' || state.impactKind === 'column' ? '' : 'hidden style="display:none"'}>
          <select id="impactDirection" class="library-sort" aria-label="Impact direction">
            <option value="downstream"${state.impactDirection === 'downstream' ? ' selected' : ''}>Downstream</option>
            <option value="upstream"${state.impactDirection === 'upstream' ? ' selected' : ''}>Upstream</option>
            <option value="both"${state.impactDirection === 'both' ? ' selected' : ''}>Both</option>
          </select>
          <input id="impactDepth" class="lineage-input stewardship-days" type="number" min="1" max="8" value="${escAttr(state.impactDepth)}" aria-label="Traversal depth">
          <button class="btn btn-primary" type="submit">Analyze</button>
        </form>
        <form id="auditSearchForm" class="lineage-query stewardship-query" autocomplete="off" ${state.mode === 'audit' ? '' : 'hidden style="display:none"'}>
          <label class="library-search lineage-search">
            <span class="search-icon" aria-hidden="true"></span>
            <input id="auditQuery" type="search" placeholder="Search assets or tags" value="${escAttr(state.stewardshipQuery)}">
          </label>
          <select id="auditSteward" class="library-sort lineage-saved" aria-label="Filter by steward">
            <option value="">All stewards</option>
            ${renderStewardshipFacetOptions(stewardship?.stewards, state.stewardshipSteward)}
          </select>
          <select id="auditDomain" class="library-sort lineage-saved" aria-label="Filter by domain">
            <option value="">All domains</option>
            ${renderStewardshipFacetOptions(stewardship?.domains, state.stewardshipDomain)}
          </select>
          <input id="auditStaleDays" class="lineage-input stewardship-days" type="number" min="1" max="3660" value="${escAttr(state.stewardshipStaleDays)}" aria-label="Stale after days">
          <input id="auditLimit" class="lineage-input stewardship-days" type="number" min="1" max="200" value="${escAttr(state.auditLimit)}" aria-label="Audit event limit">
          <button class="btn btn-primary" type="submit">Refresh</button>
        </form>
      </div>
      <div id="lineageResults">${state.mode === 'stewardship' && state.stewardship ? '' : renderLineageEmpty()}</div>`;

}
