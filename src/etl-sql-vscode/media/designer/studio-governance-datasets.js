// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-governance-datasets.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-governance-datasets.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Governance dataset preparation and schedule editors.
 */
import { _escapeHtml, _studioIcon, queryElement, queryElements } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
export function createStudioGovernanceDatasets(hostContext) {
    // ── Dataset lifecycle ────────────────────────────────────────────────────
    // What the script says about each dataset it creates — who may see it, how long it lives, and
    // the refresh, export and publish steps it performs on it. Every write is a span edit on the
    // clause it names: a CREATE DATASET carries clauses no authoring model represents, and the way
    // to guarantee they survive is to never write the bytes that hold them.
    function governanceDatasetsMarkup(governance) {
        const datasets = governance.datasets || [];
        if (!datasets.length)
            return '';
        return `
            <section class="etlsql-studio-library-section" data-gov-datasets>
                <div class="etlsql-studio-subhead"><div><strong>Datasets</strong><span>${datasets.length} in this script</span></div></div>
                ${datasets.map((dataset) => governanceDatasetMarkup(governance, dataset)).join('')}
            </section>`;
    }
    function governanceDatasetMarkup(governance, dataset) {
        const levels = governance.accessLevels || ['PRIVATE', 'PUBLIC'];
        const steps = dataset.lifecycle || [];
        return `
            <div class="etlsql-studio-gov-dataset" data-gov-dataset="${_escapeHtml(dataset.name)}">
                <div class="etlsql-studio-gov-tag">
                    <span class="etlsql-studio-gov-tag-name">${_escapeHtml(dataset.name)}</span>
                    <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governanceDatasetSummary(dataset))}</span>
                </div>
                ${dataset.encryption === 'password' || dataset.encryption === 'keyfile'
            ? `<p class="etlsql-studio-outline-note">Its encryption credential is written in the script and is not edited here — a panel that rewrote that clause would read the credential and send it back for no reason you asked for.</p>`
            : ''}
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label">Who may see it</label>
                    <select data-gov-dataset-access>
                        ${levels.map((level) => `<option value="${_escapeHtml(level)}"${level === dataset.access ? ' selected' : ''}>${_escapeHtml(level)}</option>`).join('')}
                    </select>
                    <label class="etlsql-studio-field-label">How long it lives</label>
                    <input type="text" data-gov-dataset-ttl placeholder="e.g. 1h — empty to keep it until refreshed" value="${_escapeHtml(dataset.ttl || '')}">
                    <button type="button" class="etlsql-studio-btn" data-gov-dataset-save>Apply</button>

                    <label class="etlsql-studio-field-label">Add a step</label>
                    <select data-gov-dataset-step>
                        <option value="refresh">Refresh — rebuild it from its query</option>
                        <option value="export">Export — write a portable copy</option>
                        <option value="publish">Publish — import an exported copy</option>
                    </select>
                    <div data-gov-dataset-step-fields></div>
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-dataset-step-add>Write the statement</button>
                    <p class="etlsql-studio-outline-note">A step is a statement in the script, so it runs every time the script does and says why it exists. ${governance.datasetRegistryUrl
            ? `Refreshing or sharing one copy right now is the catalog's job — <a href="${_escapeHtml(governance.datasetRegistryUrl)}" target="_blank" rel="noopener" data-gov-registry-link>open it there</a>.`
            : 'This host has no dataset registry, so there is nobody to share a copy with.'}</p>
                </div>
                ${steps.length
            ? `<div class="etlsql-studio-gov-tags">${steps.map((step) => `<div class="etlsql-studio-gov-tag">
                        <span class="etlsql-studio-gov-tag-name">${_escapeHtml(step.kind)}</span>
                        <span class="etlsql-studio-gov-tag-value">${_escapeHtml(step.detail || 'in this script')}</span>
                        <button type="button" class="etlsql-studio-icon-btn" data-gov-dataset-goto="${step.line}" title="Show it" aria-label="Show it">${_studioIcon('code', 12)}</button>
                    </div>`).join('')}</div>`
            : ''}
            </div>`;
    }
    function governanceDatasetSummary(dataset) {
        const parts = [String(dataset.access || '').toLowerCase()];
        if (dataset.ttl)
            parts.push(`kept ${dataset.ttl}`);
        if (dataset.compress)
            parts.push('compressed');
        parts.push(dataset.encryption === 'none' ? 'not encrypted' : `${dataset.encryption} encryption`);
        return parts.join(' · ');
    }
    function governanceDatasetStepFieldsMarkup(kind, governance) {
        if (kind === 'refresh') {
            return '<p class="etlsql-studio-outline-note">Rebuilds the dataset from its own query at this point in the script.</p>';
        }
        const levels = governance?.accessLevels || ['PRIVATE', 'PUBLIC'];
        return `
            <input type="text" data-gov-dataset-path placeholder="${kind === 'export' ? 'File to write' : 'Exported file to read'}" aria-label="File">
            <select data-gov-dataset-encryption aria-label="Transport credential">
                <option value="PASSWORD">Protected by a password</option>
                <option value="KEYFILE">Protected by a key file</option>
            </select>
            <input type="text" data-gov-dataset-secret placeholder="Password or key file path" aria-label="Transport credential value">
            ${kind === 'publish'
            ? `<input type="text" data-gov-dataset-folder placeholder="Folder to publish into (optional)" aria-label="Folder">
                   <select data-gov-dataset-publish-access aria-label="Access level">
                       ${levels.map((level) => `<option value="${_escapeHtml(level)}">${_escapeHtml(level)}</option>`).join('')}
                   </select>`
            : ''}
            <p class="etlsql-studio-outline-note">The file leaves this machine, so it cannot carry the at-rest key only this machine holds. A password or key file is what lets it be published somewhere else.</p>`;
    }
    function bindGovernanceDatasets() {
        const governance = hostContext.state.governance;
        queryElements(hostContext.sidebarContent, '[data-gov-dataset]').forEach(host => {
            const name = host.getAttribute('data-gov-dataset');
            const stepSelect = queryElement(host, '[data-gov-dataset-step]');
            const stepFields = queryElement(host, '[data-gov-dataset-step-fields]');
            const paintStep = () => {
                if (stepFields)
                    stepFields.innerHTML = governanceDatasetStepFieldsMarkup(stepSelect?.value || 'refresh', governance);
            };
            stepSelect?.addEventListener('change', paintStep);
            paintStep();
            queryElement(host, '[data-gov-dataset-save]')?.addEventListener('click', async () => {
                const access = queryElement(host, '[data-gov-dataset-access]')?.value;
                const ttl = queryElement(host, '[data-gov-dataset-ttl]')?.value ?? '';
                const dataset = (governance?.datasets || []).find((item) => item.name === name);
                // Two clauses, two edits, and only the ones that actually changed — so applying a TTL
                // never touches the access level and vice versa.
                if (dataset && access && access !== dataset.access) {
                    if (!await writeGovernanceDataset('Set dataset access', { op: 'dataset-access', dataset: name, access }))
                        return;
                }
                if (dataset && (ttl.trim() || '') !== (dataset.ttl || '')) {
                    await writeGovernanceDataset('Set dataset lifetime', { op: 'dataset-ttl', dataset: name, ttl: ttl.trim() || null });
                }
            });
            queryElement(host, '[data-gov-dataset-step-add]')?.addEventListener('click', () => {
                const kind = stepSelect?.value || 'refresh';
                void writeGovernanceDataset(`Add ${kind}`, {
                    op: 'dataset-step',
                    dataset: name,
                    action: kind,
                    path: queryElement(host, '[data-gov-dataset-path]')?.value || null,
                    encryption: queryElement(host, '[data-gov-dataset-encryption]')?.value || null,
                    secret: queryElement(host, '[data-gov-dataset-secret]')?.value || null,
                    folder: queryElement(host, '[data-gov-dataset-folder]')?.value || null,
                    access: queryElement(host, '[data-gov-dataset-publish-access]')?.value || null,
                });
            });
            queryElements(host, '[data-gov-dataset-goto]').forEach(button => {
                button.addEventListener('click', () => {
                    const line = Number(button.getAttribute('data-gov-dataset-goto'));
                    if (Number.isFinite(line) && line > 0)
                        hostContext.state.editorInstance?.revealLine?.(line);
                });
            });
        });
    }
    async function writeGovernanceDataset(label, operation) {
        const result = await hostContext.canonicalScriptMutation(label, STUDIO_ROUTES.governance, operation);
        if (!result)
            return false;
        hostContext.state.governance = result;
        if (hostContext.state.activeActivity === 'governance')
            hostContext.paintGovernancePanel();
        return true;
    }
    // ── Scheduling and delivery handoff ──────────────────────────────────────
    // Studio does not host schedules or subscriptions. Both live in catalogs that already have a
    // permission model, a history, and an operator who owns them, and a workbench that listed and
    // edited them would be a second door onto both with a weaker gate. What Studio does is the one
    // thing only it can: write the statements that make *this* document recurring, into the file the
    // author is looking at — and then open the Orchestrator at the job it just named.
    function governanceScheduleMarkup(governance) {
        const schedule = governance.schedule;
        if (!schedule)
            return '';
        const jobs = schedule.jobs || [];
        const declared = schedule.schedules || [];
        const suggestion = governanceSuggestedJobName(schedule);
        return `
            <section class="etlsql-studio-library-section" data-gov-schedule>
                <div class="etlsql-studio-subhead"><div><strong>Run it on a schedule</strong><span>From a run that worked to a job that repeats</span></div></div>
                ${jobs.length
            ? `<div class="etlsql-studio-gov-tags">${jobs.map((job) => `<div class="etlsql-studio-gov-tag">
                        <span class="etlsql-studio-gov-tag-name">${_escapeHtml(job.job)}</span>
                        <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governanceJobSummary(job, declared))}</span>
                        ${schedule.orchestratorUrl
                ? `<a class="etlsql-studio-gov-origin" href="${_escapeHtml(schedule.orchestratorUrl)}?job=${encodeURIComponent(job.job)}" target="_blank" rel="noopener" data-gov-job-link>Operate it</a>`
                : '<span class="etlsql-studio-gov-origin">declared here</span>'}
                    </div>`).join('')}</div>`
            : ''}
                ${schedule.canSchedule
            ? `<div class="etlsql-studio-gov-add">
                        <label class="etlsql-studio-field-label" for="gov-schedule-job">Job name</label>
                        <input type="text" id="gov-schedule-job" data-gov-schedule-job value="${_escapeHtml(suggestion)}">
                        <label class="etlsql-studio-field-label" for="gov-schedule-when">How often</label>
                        <select id="gov-schedule-when" data-gov-schedule-when>
                            ${declared.map((item) => `<option value="reuse:${_escapeHtml(item.name)}">Reuse ${_escapeHtml(item.name)} — ${_escapeHtml(item.cron)}</option>`).join('')}
                            ${(schedule.cadences || []).map((cadence) => `<option value="cron:${_escapeHtml(cadence.cron)}">${_escapeHtml(cadence.label)}</option>`).join('')}
                            <option value="cron:">Something else…</option>
                        </select>
                        <div data-gov-schedule-fields></div>
                        <button type="button" class="etlsql-studio-btn is-primary" data-gov-schedule-apply>Write the schedule</button>
                        <p class="etlsql-studio-outline-note">This writes CREATE SCHEDULE, CREATE JOB and ALTER JOB … ADD SCHEDULE into ${_escapeHtml(schedule.target || 'this script')}, so the recurrence is reviewable and deployable with everything else. ${schedule.orchestratorUrl
                ? 'Running, pausing and reading its history stay with the Orchestrator.'
                : 'This host runs no orchestrator; the statements register the job wherever the script is run.'}</p>
                       </div>`
            : `<div class="etlsql-studio-empty-compact">${_escapeHtml(schedule.reason || 'This document cannot be scheduled yet.')}</div>`}
                <p class="etlsql-studio-outline-note">Delivering the result to people — who gets it, in what format, on what cadence — is a subscription on the report itself, kept where its recipients and their permissions are.</p>
            </section>`;
    }
    function governanceJobSummary(job, declared) {
        const cadences = (job.schedules || [])
            .map((name) => declared.find((item) => item.name === name))
            .filter(Boolean)
            .map((item) => item.cron);
        const when = cadences.length ? cadences.join(', ') : (job.schedules || []).join(', ') || 'no schedule attached';
        return `${job.targetKind} ${job.target} · ${when}`;
    }
    function bindGovernanceSchedule() {
        const schedule = hostContext.state.governance?.schedule;
        if (!schedule)
            return;
        const when = queryElement(hostContext.sidebarContent, '[data-gov-schedule-when]');
        const fields = queryElement(hostContext.sidebarContent, '[data-gov-schedule-fields]');
        const paintFields = () => {
            if (!when || !fields)
                return;
            const value = when.value || '';
            if (value.startsWith('reuse:')) {
                fields.innerHTML = '<p class="etlsql-studio-outline-note">Two jobs on the same cadence share the schedule that names it, so changing the cadence later is one edit rather than a search.</p>';
                return;
            }
            const cron = value.slice('cron:'.length);
            fields.innerHTML = `
                <label class="etlsql-studio-field-label" for="gov-schedule-name">Schedule name</label>
                <input type="text" id="gov-schedule-name" data-gov-schedule-name value="${_escapeHtml(governanceSuggestedScheduleName(cron))}">
                <label class="etlsql-studio-field-label" for="gov-schedule-cron">Cadence (cron)</label>
                <input type="text" id="gov-schedule-cron" data-gov-schedule-cron value="${_escapeHtml(cron)}" placeholder="0 2 * * *">
                <label class="etlsql-studio-field-label" for="gov-schedule-zone">Time zone</label>
                <input type="text" id="gov-schedule-zone" data-gov-schedule-zone placeholder="UTC — empty uses the server default">`;
        };
        when?.addEventListener('change', paintFields);
        paintFields();
        queryElement(hostContext.sidebarContent, '[data-gov-schedule-apply]')?.addEventListener('click', async () => {
            const value = when?.value || '';
            const reuse = value.startsWith('reuse:') ? value.slice('reuse:'.length) : null;
            const result = await hostContext.canonicalScriptMutation('Schedule this document', STUDIO_ROUTES.governance, {
                op: 'schedule',
                documentUri: hostContext.getActiveDoc()?.path || null,
                job: queryElement(hostContext.sidebarContent, '[data-gov-schedule-job]')?.value || '',
                reuseSchedule: reuse,
                schedule: reuse ? null : (queryElement(hostContext.sidebarContent, '[data-gov-schedule-name]')?.value || ''),
                cron: reuse ? null : (queryElement(hostContext.sidebarContent, '[data-gov-schedule-cron]')?.value || ''),
                timeZone: reuse ? null : (queryElement(hostContext.sidebarContent, '[data-gov-schedule-zone]')?.value || null),
            });
            if (!result)
                return;
            hostContext.state.governance = result;
            if (hostContext.state.activeActivity === 'governance')
                hostContext.paintGovernancePanel();
        });
    }
    /** A name derived from the file, because a job the author has to invent a name for is one they put off. */
    function governanceSuggestedJobName(schedule) {
        const base = String(schedule?.target || 'job')
            .split(/[\\/]/)
            .pop()
            ?.replace(/\.(etlsql|rptsql|sql)$/i, '')
            .replace(/[^A-Za-z0-9_]/g, '_') || 'job';
        const name = /^[A-Za-z_]/.test(base) ? base : `job_${base}`;
        return `${name}_scheduled`;
    }
    function governanceSuggestedScheduleName(cron) {
        const known = {
            '0 * * * *': 'Hourly',
            '0 2 * * *': 'Nightly',
            '0 7 * * 1-5': 'Weekdays',
            '0 6 * * 1': 'Weekly',
            '0 3 1 * *': 'Monthly',
        };
        return known[cron] || 'OnSchedule';
    }
    return { governanceDatasetsMarkup, bindGovernanceDatasets, governanceScheduleMarkup, bindGovernanceSchedule };
}
