export function esc(s: unknown) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
export function escAttr(s: unknown) {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
export function formatOptionalDate(value: string | null | undefined) {
  return value ? new Date(value).toLocaleString() : 'Not refreshed';
}

export const PANEL_HTML = `
  <p id="datasetsStatus" class="library-subtitle" role="status"></p>
  <div class="card">
    <div class="card-header">
      <div>
        <span class="section-kicker">Reusable data assets</span>
        <h3>Shared Datasets</h3>
      </div>
      <div class="admin-action-group">
        <button class="btn btn-outline btn-sm" id="datasetsRefreshBtn">Refresh</button>
      </div>
    </div>
    <div id="datasetsTableWrap"><div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading datasets…</span></div></div>
  </div>

  <div class="card" id="editDatasetForm" style="display:none">
    <div class="card-header"><h3>Edit Dataset</h3></div>
    <div class="form-row">
      <div class="form-group">
        <label for="ds-accessLevel">Access Level</label>
        <select id="ds-accessLevel">
          <option value="Public">Public</option>
          <option value="Private">Private</option>
        </select>
      </div>
      <div class="form-group">
        <label for="ds-ttl">TTL (e.g. 1h, 30m, 7d — blank = no expiry)</label>
        <input id="ds-ttl" type="text" placeholder="e.g. 1h">
      </div>
    </div>
    <div id="ds-error" class="error-msg"></div>
    <div class="form-actions">
      <button class="btn btn-primary btn-sm" id="ds-saveBtn">Save</button>
      <button class="btn btn-outline btn-sm" id="ds-cancelBtn">Cancel</button>
    </div>
    <input type="hidden" id="ds-id">
  </div>

  <div class="card" id="dsAclPanel" style="display:none">
    <div class="card-header">
      <h3>Permissions — <span id="dsAclDatasetName"></span></h3>
      <button class="btn btn-outline btn-sm" id="dsAclCloseBtn">Close</button>
    </div>
    <div id="dsAclTableWrap"></div>
    <div class="stacked-form">
      <h4 class="section-kicker">Grant Permission</h4>
      <div class="form-row">
        <div class="form-group">
          <label for="ds-acl-group">Group</label>
          <select id="ds-acl-group"></select>
        </div>
        <div class="form-group">
          <label for="ds-acl-perm">Permission</label>
          <select id="ds-acl-perm">
            <option value="Viewer">Viewer</option>
            <option value="Refresh">Refresh</option>
            <option value="Editor">Editor</option>
            <option value="Owner">Owner</option>
          </select>
        </div>
      </div>
      <button class="btn btn-primary btn-sm" id="ds-acl-grantBtn">Grant</button>
    </div>
  </div>`;

export const MODALS_HTML = `
  <div id="datasetLineageModal" class="modal-overlay" style="display:none" role="dialog" aria-modal="true" aria-labelledby="dsLineageTitle">
    <div class="modal-card modal-lg">
      <div class="modal-header">
        <div>
          <span class="library-kicker">Lineage</span>
          <h3 class="modal-title" id="dsLineageTitle">Dataset Lineage</h3>
          <p class="modal-subtitle">Source tables that feed this dataset, aggregated across recent runs.</p>
        </div>
      </div>
      <div id="dsLineageDag" style="height:420px;min-height:420px;"></div>
      <div class="modal-actions">
        <button class="btn btn-outline" id="dsLineageCloseBtn" data-dialog-close>Close</button>
      </div>
    </div>
  </div>

  <div id="datasetViewerModal" class="modal-overlay" style="display:none" role="dialog" aria-modal="true" aria-labelledby="dv-title">
    <div class="modal-card dv-modal">
      <div class="modal-header">
        <div>
          <span class="library-kicker">Dataset</span>
          <h3 id="dv-title" class="modal-title">Loading…</h3>
        </div>
        <button class="btn btn-ghost" id="dv-closeBtn" data-dialog-close aria-label="Close">✕</button>
      </div>
      <div class="dv-toolbar">
        <span class="dv-counts"><strong id="dv-totalCount">—</strong> rows total · <strong id="dv-filteredCount">—</strong> matching</span>
        <div class="dv-toolbar-right">
          <input type="text" id="dv-search" placeholder="Search all columns…" class="admin-filter-input" style="width:200px">
          <span id="dv-filter-badge" class="chip chip-filter" style="display:none"></span>
          <button class="btn btn-outline btn-sm" id="dv-resetBtn">Reset</button>
          <button class="btn btn-outline btn-sm" id="dv-exportBtn">Export CSV</button>
          <button class="btn btn-outline btn-sm" id="dv-exportXlsxBtn">Export XLSX</button>
        </div>
      </div>
      <div class="dv-table-wrap" id="dv-tableWrap">
        <div class="loading-state loading-state-compact"><span class="spinner"></span><span>Loading…</span></div>
      </div>
      <p class="library-subtitle">Column statistics apply to column filters. Search affects the displayed rows.</p>
      <p id="dv-statsStatus" class="library-subtitle" role="status"></p>
      <div class="dv-pagination">
        <button class="btn btn-outline btn-sm" id="dv-prevBtn">← Prev</button>
        <span class="dv-page-info" id="dv-pageInfo">Page 1 of —</span>
        <button class="btn btn-outline btn-sm" id="dv-nextBtn">Next →</button>
        <select id="dv-pageSize" class="param-input" style="width:auto">
          <option value="25">25 / page</option>
          <option value="50" selected>50 / page</option>
          <option value="100">100 / page</option>
          <option value="200">200 / page</option>
        </select>
      </div>
  <div id="dv-pickerPopover" class="dv-picker-popover" style="display:none" role="group" aria-label="Choose column values">
    <input type="text" id="dv-pickerSearch" class="dv-picker-search" placeholder="Search values…" aria-label="Search column values">
    <div id="dv-pickerList"></div>
    <button class="btn btn-primary btn-sm dv-picker-apply" id="dv-pickerApply">Apply</button>
  </div>
    </div>
  </div>`;
