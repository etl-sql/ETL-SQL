// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-governance-security.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-governance-security.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Security policy preview and identity vocabulary.
 */
import { _escapeHtml, _feedback, _studioIcon, queryElement, queryElements } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
export function createStudioGovernanceSecurity(hostContext) {
    // ── Row-level security preview ───────────────────────────────────────────
    // A run evaluates the author's own HAS_GROUP / HAS_ROLE predicates as a named audience. It is
    // not impersonation of a person: the audience carries no user id and never administrator
    // authority, and the run reaches exactly the data it always could. What it changes is the one
    // thing an author cannot otherwise see — what their predicates do to somebody else's rows.
    function governanceSecurityMarkup() {
        const preview = hostContext.state.previewAs;
        const vocabulary = hostContext.state.previewAsVocabulary;
        const names = (list) => (list || []).join(', ');
        return `
            <section class="etlsql-studio-library-section" data-gov-security>
                <div class="etlsql-studio-subhead"><div><strong>Row-level security</strong><span>Run as an audience to see what its rows look like</span></div></div>
                ${preview
            ? `<div class="etlsql-studio-gov-tag">
                          <span class="etlsql-studio-gov-tag-name">${_escapeHtml(preview.label || 'preview')}</span>
                          <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governancePreviewSummary(preview))}</span>
                          <button type="button" class="etlsql-studio-icon-btn" data-gov-preview-clear title="Stop previewing" aria-label="Stop previewing">${_studioIcon('close', 12)}</button>
                       </div>
                       <p class="etlsql-studio-outline-note">Every run is now this audience's. Results are theirs, not yours.</p>`
            : '<div class="etlsql-studio-empty-compact">Runs use your own identity.</div>'}
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label" for="gov-preview-label">Audience name</label>
                    <input type="text" id="gov-preview-label" data-gov-preview-label placeholder="e.g. a Northern sales rep" value="${_escapeHtml(preview?.label || '')}">
                    <label class="etlsql-studio-field-label" for="gov-preview-groups">Groups</label>
                    <input type="text" id="gov-preview-groups" data-gov-preview-groups placeholder="Comma separated" value="${_escapeHtml(names(preview?.groups))}">
                    ${(vocabulary?.groups || []).length
            ? `<div class="etlsql-studio-gov-missing">${(vocabulary?.groups || []).map((group) => `<button type="button" class="etlsql-studio-chip" data-gov-preview-add-group="${_escapeHtml(group)}">${_escapeHtml(group)}</button>`).join('')}</div>`
            : ''}
                    <label class="etlsql-studio-field-label" for="gov-preview-roles">Roles</label>
                    <input type="text" id="gov-preview-roles" data-gov-preview-roles placeholder="Comma separated" value="${_escapeHtml(names(preview?.roles))}">
                    ${(vocabulary?.roles || []).length
            ? `<div class="etlsql-studio-gov-missing">${(vocabulary?.roles || []).map((role) => `<button type="button" class="etlsql-studio-chip" data-gov-preview-add-role="${_escapeHtml(role)}">${_escapeHtml(role)}</button>`).join('')}</div>`
            : ''}
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-preview-apply>Preview as this audience</button>
                    ${vocabulary?.note ? `<p class="etlsql-studio-outline-note">${_escapeHtml(vocabulary.note)}</p>` : ''}
                </div>
            </section>`;
    }
    function governancePreviewSummary(preview) {
        const parts = [];
        if (preview.groups?.length)
            parts.push(`groups ${preview.groups.join(', ')}`);
        if (preview.roles?.length)
            parts.push(`roles ${preview.roles.join(', ')}`);
        return parts.length ? parts.join(' · ') : 'no groups or roles';
    }
    function bindGovernanceSecurity() {
        const readList = (selector) => (queryElement(hostContext.sidebarContent, selector)?.value || '')
            .split(',')
            .map((item) => item.trim())
            .filter(Boolean);
        const appendTo = (selector, value) => {
            if (!value)
                return;
            const field = queryElement(hostContext.sidebarContent, selector);
            if (!field)
                return;
            const current = field.value.split(',').map((item) => item.trim()).filter(Boolean);
            if (!current.some((item) => item.toLowerCase() === value.toLowerCase()))
                current.push(value);
            field.value = current.join(', ');
        };
        queryElements(hostContext.sidebarContent, '[data-gov-preview-add-group]').forEach(button => {
            button.addEventListener('click', () => appendTo('[data-gov-preview-groups]', button.getAttribute('data-gov-preview-add-group')));
        });
        queryElements(hostContext.sidebarContent, '[data-gov-preview-add-role]').forEach(button => {
            button.addEventListener('click', () => appendTo('[data-gov-preview-roles]', button.getAttribute('data-gov-preview-add-role')));
        });
        queryElement(hostContext.sidebarContent, '[data-gov-preview-apply]')?.addEventListener('click', () => {
            const groups = readList('[data-gov-preview-groups]');
            const roles = readList('[data-gov-preview-roles]');
            const label = (queryElement(hostContext.sidebarContent, '[data-gov-preview-label]')?.value || '').trim();
            if (!groups.length && !roles.length) {
                // An audience with nothing in it is a real thing to preview — it is what a user with
                // no membership sees — but it is also what an empty form looks like, so it has to be
                // asked for by name rather than assumed.
                if (!label) {
                    _feedback.notify('Name the audience, or give it a group or role.', { title: 'Nothing previewed', tone: 'error' });
                    return;
                }
            }
            setPreviewAs({ label: label || 'preview', groups, roles });
        });
        queryElement(hostContext.sidebarContent, '[data-gov-preview-clear]')?.addEventListener('click', () => setPreviewAs(null));
    }
    function setPreviewAs(preview) {
        hostContext.state.previewAs = preview;
        renderPreviewAsBanner();
        if (hostContext.state.activeActivity === 'governance')
            hostContext.paintGovernancePanel();
        _feedback.notify(preview
            ? `Runs now evaluate row-level security as ${preview.label}.`
            : 'Runs use your own identity again.', { title: 'Preview identity', tone: 'info' });
    }
    /**
     * The banner is not decoration. Previewed rows look exactly like real ones, and an author who
     * forgets which identity a result came from will read somebody else's empty result as a bug in
     * their query — or worse, their own full result as proof the predicate works.
     */
    function renderPreviewAsBanner() {
        const host = queryElement(hostContext.shell, '[data-studio-preview-banner]');
        if (!host)
            return;
        const preview = hostContext.state.previewAs;
        host.hidden = !preview;
        host.innerHTML = preview
            ? `<span>Previewing as <strong>${_escapeHtml(preview.label)}</strong> — ${_escapeHtml(governancePreviewSummary(preview))}</span>
               <button type="button" class="etlsql-studio-btn" data-preview-banner-clear>Stop previewing</button>`
            : '';
        queryElement(host, '[data-preview-banner-clear]')?.addEventListener('click', () => setPreviewAs(null));
    }
    async function loadPreviewAsVocabulary() {
        if (hostContext.state.previewAsVocabulary)
            return;
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.previewAs);
            if (!response.ok)
                return;
            hostContext.state.previewAsVocabulary = await response.json();
        }
        catch {
            // A host that cannot answer leaves the picker to free text, which is the whole
            // vocabulary anyway on a host with no directory.
        }
    }
    return { governanceSecurityMarkup, bindGovernanceSecurity, loadPreviewAsVocabulary };
}
