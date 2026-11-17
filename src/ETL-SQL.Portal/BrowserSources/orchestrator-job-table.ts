import { escHtml, fmtDt } from './orchestrator-admin-ui.js';
import { ownerLabel } from './orchestrator-acl-ui.js';
import { isObject } from './http-error.js';

export type TargetKindLabel = 'Report' | 'Bundle' | 'Pipeline' | 'Script';

/** Parses a catalog pipeline slot name (`PIPELINE:<id>`) and returns its positive numeric ID. */
export function parsePipelineJobId(jobName?: string | null): number | null {
    if (typeof jobName !== 'string') return null;
    const match = /^PIPELINE:(\d+)$/i.exec(jobName.trim());
    if (!match) return null;
    const id = Number(match[1]);
    return Number.isSafeInteger(id) && id > 0 ? id : null;
}

/** Direct Studio URL for a catalog document id. */
export function studioDocumentHref(pipelineId: number): string {
    return `/studio.html?reportId=${encodeURIComponent(String(pipelineId))}`;
}

/** Resolves a job's operator-facing target kind from its name, persisted JobTargetKind, and URI scheme. */
export function targetKindLabel(jobType: 0 | 1 | number, targetPath?: string | null, jobName?: string | null): TargetKindLabel {
    if (parsePipelineJobId(jobName) !== null) return 'Pipeline';
    if (jobType === 1) return 'Report';
    const path = (targetPath ?? '').trim().toLowerCase();
    if (path.startsWith('orch://') || path.startsWith('bundle://')) return 'Bundle';
    return 'Script';
}

export function jobSchedule(job: JobDefinition): string {
    if (!job.interval) return 'Manual';
    const unit = (job.unit || 'HOUR').toLowerCase();
    const plural = job.interval === 1 ? unit.replace(/s$/, '') : unit.endsWith('s') ? unit : unit + 's';
    return `Every ${job.interval} ${plural}${job.atTime ? ` at ${job.atTime}` : ''}`;
}

export function renderScheduledJobsTable(
    jobs: readonly JobDefinition[],
    selectedId?: string | null,
    catalogPipelines?: ReadonlyMap<string, CatalogPipelineLinkDto> | null
): string {
    if (!jobs.length) return '<div class="empty-state">No jobs match the search or filter criteria.</div>';
    return `<div style="overflow-x:auto"><table class="data-table">
      <thead><tr><th>Job Name</th><th>Target / Sandbox</th><th>Owner</th><th>Schedule</th>
        <th>Status</th><th>Last Run</th><th>Next Run</th><th>Actions</th></tr></thead><tbody>
      ${jobs.map(job => {
        const pipelineId = parsePipelineJobId(job.name);
        const catalogLink = pipelineId !== null
            ? (catalogPipelines?.get(job.name) ?? catalogPipelines?.get(`PIPELINE:${pipelineId}`) ?? null)
            : null;
        const display = catalogLink?.name || job.displayName || job.name;
        const path = job.targetPath || '';
        const kind = targetKindLabel(job.jobType, path, job.name);
        const badgeLabel = kind === 'Pipeline' ? '🔀 Pipeline'
            : kind === 'Report' ? '📊 Report'
            : kind === 'Bundle' ? '📦 Bundle'
            : '📜 Script';
        const badgeBackground = kind === 'Bundle' || kind === 'Pipeline' ? 'var(--portal-accent-soft)' : 'var(--portal-surface-subtle)';
        let sandbox = 'Default';
        if (job.options) {
            try {
                const options: unknown = JSON.parse(job.options);
                if (isObject(options) && typeof options.SandboxProfile === 'string' && options.SandboxProfile)
                    sandbox = options.SandboxProfile;
            } catch { /* Free-form options may not contain a sandbox profile. */ }
        }
        return `<tr class="job-row${selectedId === job.id ? ' selected' : ''}" data-name="${escHtml(job.name)}">
          <td><strong>${escHtml(display)}</strong>
            ${display !== job.name ? `<div style="font-size:.78em;color:var(--portal-muted)">${escHtml(job.name)}</div>` : ''}</td>
          <td><div style="display:flex;align-items:center;gap:4px;flex-wrap:wrap">
            <span class="badge" style="font-size:.75em;background:${badgeBackground}">${badgeLabel}</span>
            ${catalogLink?.folderPath ? `<span class="badge" style="font-size:.72em;background:var(--portal-surface-subtle)" title="Catalog folder">📁 ${escHtml(catalogLink.folderPath)}</span>` : ''}
            ${sandbox !== 'Default' ? `<span class="badge badge-accent" style="font-size:.72em">${escHtml(sandbox)}</span>` : ''}</div>
            ${path ? `<div style="font-size:.72em;color:var(--portal-muted);max-width:180px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${escHtml(path)}">${escHtml(path)}</div>` : ''}</td>
          <td class="orch-owner-cell" title="${escHtml(ownerLabel(job.createdBy))}">${escHtml(ownerLabel(job.createdBy))}</td>
          <td>${escHtml(jobSchedule(job))}</td>
          <td>${job.isEnabled ? '<span class="badge badge-success">Active</span>' : '<span class="badge badge-neutral">Disabled</span>'}</td>
          <td>${job.lastRun ? fmtDt(job.lastRun) : '<span style="color:var(--portal-muted)">Never</span>'}</td>
          <td>${job.nextRun ? fmtDt(job.nextRun) : '—'}</td>
          <td><div class="table-actions">
            ${pipelineId !== null ? `<a class="btn btn-sm btn-outline" href="${escHtml(studioDocumentHref(pipelineId))}" title="Open this pipeline in Studio">Open in Studio ↗</a>` : ''}
            <button class="btn btn-sm btn-outline" data-action="trigger" data-name="${escHtml(job.name)}" title="Run now">▶ Run</button>
            <button class="btn btn-sm btn-outline" data-action="toggle" data-name="${escHtml(job.name)}" data-enabled="${job.isEnabled}">${job.isEnabled ? 'Disable' : 'Enable'}</button>
            <button class="btn btn-sm btn-outline" data-action="kill" data-name="${escHtml(job.name)}" title="Cancel running instance">Kill</button>
            <button class="btn btn-sm btn-outline btn-danger-outline" data-action="delete" data-name="${escHtml(job.name)}">Delete</button>
          </div></td></tr>`;
    }).join('')}</tbody></table></div>`;
}
