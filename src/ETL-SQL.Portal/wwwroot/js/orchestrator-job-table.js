/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/orchestrator-job-table.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { escHtml, fmtDt } from './orchestrator-admin-ui.js';
import { ownerLabel } from './orchestrator-acl-ui.js';
import { isObject } from './http-error.js';
export function jobSchedule(job) {
    if (!job.interval)
        return 'Manual';
    const unit = (job.unit || 'HOUR').toLowerCase();
    const plural = job.interval === 1 ? unit.replace(/s$/, '') : unit.endsWith('s') ? unit : unit + 's';
    return `Every ${job.interval} ${plural}${job.atTime ? ` at ${job.atTime}` : ''}`;
}
export function renderScheduledJobsTable(jobs, selectedId) {
    if (!jobs.length)
        return '<div class="empty-state">No jobs match the search or filter criteria.</div>';
    return `<div style="overflow-x:auto"><table class="data-table">
      <thead><tr><th>Job Name</th><th>Target / Sandbox</th><th>Owner</th><th>Schedule</th>
        <th>Status</th><th>Last Run</th><th>Next Run</th><th>Actions</th></tr></thead><tbody>
      ${jobs.map(job => {
        const display = job.displayName || job.name;
        const path = job.targetPath || '';
        const bundle = path.startsWith('bundle://');
        let sandbox = 'Default';
        if (job.options) {
            try {
                const options = JSON.parse(job.options);
                if (isObject(options) && typeof options.SandboxProfile === 'string' && options.SandboxProfile)
                    sandbox = options.SandboxProfile;
            }
            catch { /* Free-form options may not contain a sandbox profile. */ }
        }
        return `<tr class="job-row${selectedId === job.id ? ' selected' : ''}" data-name="${escHtml(job.name)}">
          <td><strong>${escHtml(display)}</strong>
            ${display !== job.name ? `<div style="font-size:.78em;color:var(--portal-muted)">${escHtml(job.name)}</div>` : ''}</td>
          <td><div style="display:flex;align-items:center;gap:4px;flex-wrap:wrap">
            <span class="badge" style="font-size:.75em;background:${bundle ? 'var(--portal-accent-soft)' : 'var(--portal-surface-subtle)'}">${bundle ? '📦 Bundle' : '📜 Script'}</span>
            ${sandbox !== 'Default' ? `<span class="badge badge-accent" style="font-size:.72em">${escHtml(sandbox)}</span>` : ''}</div>
            ${path ? `<div style="font-size:.72em;color:var(--portal-muted);max-width:180px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${escHtml(path)}">${escHtml(path)}</div>` : ''}</td>
          <td class="orch-owner-cell" title="${escHtml(ownerLabel(job.createdBy))}">${escHtml(ownerLabel(job.createdBy))}</td>
          <td>${escHtml(jobSchedule(job))}</td>
          <td>${job.isEnabled ? '<span class="badge badge-success">Active</span>' : '<span class="badge badge-neutral">Disabled</span>'}</td>
          <td>${job.lastRun ? fmtDt(job.lastRun) : '<span style="color:var(--portal-muted)">Never</span>'}</td>
          <td>${job.nextRun ? fmtDt(job.nextRun) : '—'}</td>
          <td><div class="table-actions">
            <button class="btn btn-sm btn-outline" data-action="trigger" data-name="${escHtml(job.name)}" title="Run now">▶ Run</button>
            <button class="btn btn-sm btn-outline" data-action="toggle" data-name="${escHtml(job.name)}" data-enabled="${job.isEnabled}">${job.isEnabled ? 'Disable' : 'Enable'}</button>
            <button class="btn btn-sm btn-outline" data-action="kill" data-name="${escHtml(job.name)}" title="Cancel running instance">Kill</button>
            <button class="btn btn-sm btn-outline btn-danger-outline" data-action="delete" data-name="${escHtml(job.name)}">Delete</button>
          </div></td></tr>`;
    }).join('')}</tbody></table></div>`;
}
