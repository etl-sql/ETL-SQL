/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Governance panel, tag origins, and tag editing.
 */

import { _escapeHtml, _feedback, _studioIcon, errorMessage, queryElement, queryElements } from './studio-context.js';

import type { StudioDomElement, StudioDynamic, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';

export interface StudioGovernanceTagsContext {
    readonly activeScriptText: () => any;
    readonly bindGovernanceDatasets: () => void;
    readonly bindGovernanceQuality: (scope: StudioDynamic) => void;
    readonly bindGovernanceSchedule: () => void;
    readonly bindGovernanceSecurity: () => void;
    readonly canonicalScriptMutation: (label: string, route: string, operation: Record<string, unknown>) => Promise<unknown>;
    readonly designerApiJson: <T = StudioDynamic>(path: string, body: unknown) => Promise<T>;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly governanceDatasetsMarkup: (governance: StudioDynamic) => string;
    readonly governanceRoutingMarkup: (governance: StudioDynamic, scope: StudioDynamic) => string;
    readonly governanceRulesMarkup: (governance: StudioDynamic, scope: StudioDynamic) => string;
    readonly governanceScheduleMarkup: (governance: StudioDynamic) => string;
    readonly governanceSecurityMarkup: () => string;
    readonly loadPreviewAsVocabulary: () => Promise<void>;
    readonly sidebarContent: StudioDomElement;
    readonly sidebarTitle: StudioDomElement;
    readonly state: StudioRuntimeState;
}

export function createStudioGovernanceTags(hostContext: StudioGovernanceTagsContext) {

    // ── Governance panel ─────────────────────────────────────────────────────
    // Tags, what they are inherited from, and what a policy says is missing — in the one place the
    // author is already looking at the object they apply to. Every write goes through the host's
    // governance route, which edits the author's own bytes and refuses out loud, so the panel never
    // assembles ETL-SQL itself and a refused edit never looks like a redraw.

    async function renderGovernancePanel() {
        hostContext.sidebarTitle.textContent = 'Governance';
        const doc = hostContext.getActiveDoc();
        if (!doc) {
            hostContext.sidebarContent.innerHTML = '<div class="etlsql-studio-empty-guidance"><strong>Open a script</strong><span>Tags are read from the script you are editing.</span></div>';
            return;
        }

        hostContext.sidebarContent.innerHTML = '<div class="etlsql-studio-git-loading" role="status">Reading the script…</div>';
        await hostContext.loadPreviewAsVocabulary();

        let governance;
        try {
            // The document path travels with every governance call: a schedule names a path on the
            // server, and the panel has to be able to say so before an author asks for one.
            governance = await hostContext.designerApiJson(STUDIO_ROUTES.governance, {
                script: hostContext.activeScriptText(),
                op: 'read',
                documentUri: doc.path || null,
            });
        } catch (error) {
            hostContext.sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>Governance could not be read</strong><p>${_escapeHtml(errorMessage(error) || String(error))}</p></div>`;
            return;
        }
        if (hostContext.state.activeActivity !== 'governance' || hostContext.getActiveDoc() !== doc) return;

        hostContext.state.governance = governance;
        paintGovernancePanel();
    }

    function paintGovernancePanel() {
        const governance = hostContext.state.governance;
        if (!governance) return;

        if (!governance.parsed) {
            hostContext.sidebarContent.innerHTML = `<div class="etlsql-studio-capability-state" role="alert"><strong>The script does not parse yet</strong><p>${_escapeHtml(governance.error || '')}</p><p>Tags are read from the parsed script, so fix the syntax first.</p></div>`;
            return;
        }

        const scopes = governance.scopes || [];
        const selected = scopes.find((scope: StudioDynamic) => scope.id === hostContext.state.governanceScopeId) || scopes[0] || null;
        hostContext.state.governanceScopeId = selected?.id || null;

        hostContext.sidebarContent.innerHTML = `
            <section class="etlsql-studio-library-section">
                <div class="etlsql-studio-subhead"><div><strong>What can be tagged</strong><span>${scopes.length} object${scopes.length === 1 ? '' : 's'} in this script</span></div></div>
                <div class="etlsql-studio-gov-list" role="listbox" aria-label="Taggable objects">
                    ${scopes.map((scope: StudioDynamic) => governanceScopeRowMarkup(scope, scope.id === hostContext.state.governanceScopeId)).join('')}
                </div>
                ${(governance.tasks || []).length
                    ? `<p class="etlsql-studio-outline-note">Tasks (${(governance.tasks || []).map(_escapeHtml).join(', ')}) are shown as the producer of what they write. A task carries no tags of its own: there is no task tag in the language, so one written here would be a word nothing reads.</p>`
                    : ''}
            </section>
            ${selected ? governanceScopeDetailMarkup(governance, selected) : ''}
            ${selected ? hostContext.governanceRulesMarkup(governance, selected) : ''}
            ${selected ? hostContext.governanceRoutingMarkup(governance, selected) : ''}
            ${hostContext.governanceDatasetsMarkup(governance)}
            ${hostContext.governanceScheduleMarkup(governance)}
            ${hostContext.governanceSecurityMarkup()}
            ${governanceFindingsMarkup(governance, selected)}`;

        bindGovernancePanel();
    }

    function governanceScopeRowMarkup(scope: StudioDynamic, isSelected: boolean) {
        const missing = (scope.missing || []).length;
        const tags = (scope.tags || []).length;
        const label = scope.kind === 'column' && scope.table ? `${scope.table}.${scope.name}` : scope.name;
        return `<button type="button" class="etlsql-studio-gov-row ${isSelected ? 'is-selected' : ''}" data-gov-scope="${_escapeHtml(scope.id)}" role="option" aria-selected="${isSelected}">
            <span class="etlsql-studio-gov-kind">${_escapeHtml(scope.kind)}</span>
            <span class="etlsql-studio-gov-name">${_escapeHtml(label)}</span>
            <span class="etlsql-studio-gov-counts">${tags} tag${tags === 1 ? '' : 's'}${missing ? ` · ${missing} missing` : ''}</span>
        </button>`;
    }

    function governanceScopeDetailMarkup(governance: StudioDynamic, scope: StudioDynamic) {
        const tags = scope.tags || [];
        const missing = scope.missing || [];
        return `
            <section class="etlsql-studio-library-section" data-gov-detail>
                <div class="etlsql-studio-subhead"><div><strong>${_escapeHtml(scope.name)}</strong><span>${_escapeHtml(governanceWriteExplanation(scope))}</span></div></div>
                ${scope.producer ? `<p class="etlsql-studio-outline-note">Written by task <strong>${_escapeHtml(scope.producer)}</strong>.</p>` : ''}
                ${scope.detail ? `<p class="etlsql-studio-outline-note">${_escapeHtml(scope.detail)}</p>` : ''}
                <div class="etlsql-studio-gov-tags">
                    ${tags.length
                        ? tags.map((tag: StudioDynamic) => governanceTagMarkup(tag)).join('')
                        : '<div class="etlsql-studio-empty-compact">No tags here yet.</div>'}
                </div>
                ${missing.length
                    ? `<div class="etlsql-studio-gov-missing">
                          <span>Required and not set:</span>
                          ${missing.map((name: string) => `<button type="button" class="etlsql-studio-chip" data-gov-fill="${_escapeHtml(name)}">@${_escapeHtml(name)}</button>`).join('')}
                       </div>`
                    : ''}
                ${governanceAddFormMarkup(governance, scope)}
            </section>`;
    }

    function governanceTagMarkup(tag: StudioDynamic) {
        const derived = tag.origin === 'derived';
        return `<div class="etlsql-studio-gov-tag ${derived ? 'is-derived' : ''} ${tag.problem ? 'is-invalid' : ''}">
            <span class="etlsql-studio-gov-tag-name">@${_escapeHtml(tag.name)}</span>
            <span class="etlsql-studio-gov-tag-value">${_escapeHtml(tag.value)}</span>
            <span class="etlsql-studio-gov-origin" title="${_escapeHtml(governanceOriginTitle(tag))}">${_escapeHtml(governanceOriginLabel(tag))}</span>
            ${tag.editable
                ? `<button type="button" class="etlsql-studio-icon-btn" data-gov-remove="${_escapeHtml(tag.name)}" title="Remove @${_escapeHtml(tag.name)}" aria-label="Remove @${_escapeHtml(tag.name)}">${_studioIcon('trash', 12)}</button>`
                : `<button type="button" class="etlsql-studio-icon-btn" data-gov-remove="${_escapeHtml(tag.name)}" title="Turn @${_escapeHtml(tag.name)} off here" aria-label="Turn @${_escapeHtml(tag.name)} off here">${_studioIcon('hidden', 12)}</button>`}
            ${tag.problem ? `<span class="etlsql-studio-gov-problem">${_escapeHtml(tag.problem)}</span>` : ''}
            ${tag.known ? '' : '<span class="etlsql-studio-gov-problem">Not a standard tag.</span>'}
        </div>`;
    }

    function governanceOriginLabel(tag: StudioDynamic) {
        if (tag.origin === 'derived') return tag.derivedFrom ? `from ${tag.derivedFrom}` : 'inherited';
        if (tag.origin === 'statement') return 'tag statement';
        if (tag.origin === 'script') return 'script header';
        return 'on the column';
    }

    function governanceOriginTitle(tag: StudioDynamic) {
        if (tag.origin === 'derived') {
            return 'Inherited at run time. Change it where it is set, or turn it off here — which writes a DELETE TAG, the thing the engine actually reads.';
        }
        return 'Written in this script, and editable here.';
    }

    /**
     * Said before the write, not after: the two authoring forms behave differently — a comment on a
     * column travels with the column, a tag statement applies at the point it runs — and an author
     * who cannot tell which one a button is about to write cannot tell what they have promised.
     */
    function governanceWriteExplanation(scope: StudioDynamic) {
        if (scope.writeTarget === 'inline') return 'Written as a comment on the column that projects it.';
        if (scope.writeTarget === 'header') return 'Written in the script header; reaches anything that does not set it itself.';
        return 'Written as an INSERT TAG statement.';
    }

    function governanceAddFormMarkup(governance: StudioDynamic, scope: StudioDynamic) {
        const scopeKind = scope.kind === 'column' ? 'column' : scope.kind === 'script' ? 'script' : 'table';
        const present = new Set((scope.tags || []).filter((tag: StudioDynamic) => tag.editable).map((tag: StudioDynamic) => tag.name));
        const available = (governance.catalog || [])
            .filter((definition: StudioDynamic) => (definition.scopes || []).includes(scopeKind))
            .filter((definition: StudioDynamic) => !present.has(definition.name));

        if (!available.length) {
            return '<p class="etlsql-studio-outline-note">Every tag the catalog defines for this kind of object is already set here.</p>';
        }

        return `<div class="etlsql-studio-gov-add">
            <label class="etlsql-studio-field-label" for="gov-tag-name">Add a tag</label>
            <select id="gov-tag-name" data-gov-name>
                ${available.map((definition: StudioDynamic) => `<option value="${_escapeHtml(definition.name)}">@${_escapeHtml(definition.name)}</option>`).join('')}
            </select>
            <div data-gov-value-host></div>
            <button type="button" class="etlsql-studio-btn is-primary" data-gov-apply>Set tag</button>
        </div>`;
    }

    function governanceValueControlMarkup(definition: StudioDynamic) {
        if (!definition) return '';
        const values = definition.kind === 'boolean' ? ['true', 'false'] : (definition.values || []);
        if (values.length) {
            return `<select data-gov-value aria-label="Value for @${_escapeHtml(definition.name)}">
                ${values.map((value: StudioDynamic) => `<option value="${_escapeHtml(value)}">${_escapeHtml(value)}</option>`).join('')}
            </select>`;
        }
        const placeholder = definition.kind === 'duration' ? 'e.g. 24h' : '';
        return `<input type="text" data-gov-value placeholder="${_escapeHtml(placeholder)}" aria-label="Value for @${_escapeHtml(definition.name)}">`;
    }

    function governanceFindingsMarkup(governance: StudioDynamic, scope: StudioDynamic) {
        const findings = governance.findings || [];
        if (!findings.length) return '';
        const here = scope ? findings.filter((finding: StudioDynamic) => finding.scopeId === scope.id) : [];
        const elsewhere = findings.filter((finding: StudioDynamic) => !here.includes(finding));
        const row = (finding: StudioDynamic) => `<button type="button" class="etlsql-studio-gov-finding is-${_escapeHtml(finding.severity)}" data-gov-goto="${finding.line}">
            <span class="etlsql-studio-gov-finding-message">${_escapeHtml(finding.message)}</span>
            <span class="etlsql-studio-gov-finding-code">${_escapeHtml(finding.code)} · line ${finding.line}</span>
        </button>`;

        return `<section class="etlsql-studio-library-section">
            <div class="etlsql-studio-subhead"><div><strong>Policy</strong><span>${findings.length} finding${findings.length === 1 ? '' : 's'}</span></div></div>
            ${here.length ? `<div class="etlsql-studio-gov-findings">${here.map(row).join('')}</div>` : ''}
            ${elsewhere.length ? `<div class="etlsql-studio-gov-findings">${elsewhere.map(row).join('')}</div>` : ''}
        </section>`;
    }

    function bindGovernancePanel() {
        const governance = hostContext.state.governance;
        const scope = (governance?.scopes || []).find((item: StudioDynamic) => item.id === hostContext.state.governanceScopeId) || null;

        queryElements(hostContext.sidebarContent, '[data-gov-scope]').forEach(button => {
            button.addEventListener('click', () => {
                hostContext.state.governanceScopeId = button.getAttribute('data-gov-scope');
                paintGovernancePanel();
            });
        });

        queryElements(hostContext.sidebarContent, '[data-gov-goto]').forEach(button => {
            button.addEventListener('click', () => {
                const line = Number(button.getAttribute('data-gov-goto'));
                if (Number.isFinite(line) && line > 0) hostContext.state.editorInstance?.revealLine?.(line);
            });
        });

        const nameSelect = queryElement(hostContext.sidebarContent, '[data-gov-name]');
        const valueHost = queryElement(hostContext.sidebarContent, '[data-gov-value-host]');
        const paintValueControl = () => {
            if (!nameSelect || !valueHost) return;
            const definition = (governance?.catalog || []).find((item: StudioDynamic) => item.name === nameSelect.value);
            valueHost.innerHTML = governanceValueControlMarkup(definition);
        };
        nameSelect?.addEventListener('change', paintValueControl);
        paintValueControl();

        queryElements(hostContext.sidebarContent, '[data-gov-fill]').forEach(button => {
            button.addEventListener('click', () => {
                if (!nameSelect) return;
                nameSelect.value = button.getAttribute('data-gov-fill');
                paintValueControl();
                queryElement(hostContext.sidebarContent, '[data-gov-value]')?.focus();
            });
        });

        queryElement(hostContext.sidebarContent, '[data-gov-apply]')?.addEventListener('click', () => {
            if (!scope || !nameSelect) return;
            const name = nameSelect.value;
            const value = queryElement(hostContext.sidebarContent, '[data-gov-value]')?.value ?? '';
            if (!String(value).trim()) {
                _feedback.notify(`@${name} needs a value.`, { title: 'Nothing written', tone: 'error' });
                return;
            }
            void writeGovernanceTags(scope, { [name]: value });
        });

        queryElements(hostContext.sidebarContent, '[data-gov-remove]').forEach(button => {
            button.addEventListener('click', () => {
                if (!scope) return;
                const removeTag = button.getAttribute('data-gov-remove');
                if (removeTag) void writeGovernanceTags(scope, { [removeTag]: null });
            });
        });

        hostContext.bindGovernanceQuality(scope);
        hostContext.bindGovernanceDatasets();
        hostContext.bindGovernanceSchedule();
        hostContext.bindGovernanceSecurity();
    }

    async function writeGovernanceTags(scope: StudioDynamic, tags: Record<string, any>) {
        const names = Object.keys(tags).map(name => `@${name}`).join(', ');
        const result = await hostContext.canonicalScriptMutation(`Set ${names}`, STUDIO_ROUTES.governance, {
            op: 'write',
            scopeId: scope.id,
            tags,
        });
        if (!result) return;
        hostContext.state.governance = result;
        if (hostContext.state.activeActivity === 'governance') paintGovernancePanel();
    }

    return { renderGovernancePanel, paintGovernancePanel };
}
