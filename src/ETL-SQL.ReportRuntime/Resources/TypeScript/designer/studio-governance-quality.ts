/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Data-quality rules and quarantine routing editors.
 */

import { _escapeHtml, _feedback, _studioIcon, queryElement, queryElements } from './studio-context.js';

import type { StudioDomElement, StudioDynamic, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';

export interface StudioGovernanceQualityContext {
    readonly canonicalScriptMutation: (label: string, route: string, operation: Record<string, unknown>) => Promise<unknown>;
    readonly paintGovernancePanel: () => void;
    readonly sidebarContent: StudioDomElement;
    readonly state: StudioRuntimeState;
}

export function createStudioGovernanceQuality(hostContext: StudioGovernanceQualityContext) {

    // ── Data-quality rules, in the same panel as the tags ────────────────────
    // A rule and a tag are both governance an author attaches to the same column, so they are one
    // panel. They are not the same kind of thing, though, and the panel keeps them apart: a tag
    // describes the column, an EXPECT clause decides which rows leave the statement.

    function qualityStatementFor(governance: StudioDynamic, table: StudioDynamic) {
        return (governance.quality || []).find((statement: StudioDynamic) => statement.id === table) || null;
    }

    function qualityColumnFor(governance: StudioDynamic, scopeId: string) {
        for (const statement of governance.quality || []) {
            const column = (statement.columns || []).find((item: StudioDynamic) => item.scopeId === scopeId);
            if (column) return { statement, column };
        }
        return null;
    }

    function governanceRulesMarkup(governance: StudioDynamic, scope: StudioDynamic) {
        if (scope.kind !== 'column') return '';
        const found = qualityColumnFor(governance, scope.id);
        const clauses = found?.column.clauses || [];
        const vocabulary = governance.qualityVocabulary || { actions: [], ruleForms: [] };

        return `
            <section class="etlsql-studio-library-section" data-gov-rules>
                <div class="etlsql-studio-subhead"><div><strong>Data quality</strong><span>EXPECT clauses on this column</span></div></div>
                <div class="etlsql-studio-gov-tags">
                    ${clauses.length
                        ? clauses.map((clause: StudioDynamic) => `<div class="etlsql-studio-gov-tag">
                            <span class="etlsql-studio-gov-tag-value">EXPECT ${_escapeHtml(clause.rule)}</span>
                            <span class="etlsql-studio-gov-origin" title="${_escapeHtml(clause.actionExplicit
                                ? 'The clause names this action.'
                                : 'No action was written. WARN is the default — records and continues — which is not the same as somebody choosing it.')}">${_escapeHtml(clause.action)}${clause.actionExplicit ? '' : ' (default)'}</span>
                            <button type="button" class="etlsql-studio-icon-btn" data-gov-rule-remove="${clause.index}" title="Remove this rule" aria-label="Remove this rule">${_studioIcon('trash', 12)}</button>
                        </div>`).join('')
                        : '<div class="etlsql-studio-empty-compact">No rules on this column.</div>'}
                </div>
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label" for="gov-rule-form">Add a rule</label>
                    <select id="gov-rule-form" data-gov-rule-form>
                        ${(vocabulary.ruleForms || []).map((form: StudioDynamic, index: number) => `<option value="${index}">${_escapeHtml(form.label)}</option>`).join('')}
                    </select>
                    <input type="text" data-gov-rule-text aria-label="Rule">
                    <p class="etlsql-studio-outline-note" data-gov-rule-hint></p>
                    <label class="etlsql-studio-field-label" for="gov-rule-action">On failure</label>
                    <select id="gov-rule-action" data-gov-rule-action>
                        ${(vocabulary.actions || []).map((action: StudioDynamic) => `<option value="${_escapeHtml(action)}">${_escapeHtml(action)}</option>`).join('')}
                    </select>
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-rule-apply>Add rule</button>
                </div>
            </section>`;
    }

    function governanceRoutingMarkup(governance: StudioDynamic, scope: StudioDynamic) {
        if (scope.kind === 'column' || scope.kind === 'script') return '';
        const statement = qualityStatementFor(governance, scope.table || scope.name);
        if (!statement) return '';

        const routing = statement.routing || [];
        const vocabulary = governance.qualityVocabulary || { actions: [], handling: [] };

        return `
            <section class="etlsql-studio-library-section" data-gov-routing>
                <div class="etlsql-studio-subhead"><div><strong>Failure routing</strong><span>Where this statement sends rows a rule rejects</span></div></div>
                ${statement.missingQuarantineTarget
                    ? `<div class="etlsql-studio-gov-finding is-error">
                        <span class="etlsql-studio-gov-finding-message">A column here elects QUARANTINE and this statement routes nowhere, so those rows have nowhere to go. Add a QUARANTINE route below.</span>
                       </div>`
                    : ''}
                <div class="etlsql-studio-gov-tags">
                    ${routing.length
                        ? routing.map((clause: StudioDynamic) => `<div class="etlsql-studio-gov-tag">
                            <span class="etlsql-studio-gov-tag-name">${_escapeHtml(clause.action)}</span>
                            <span class="etlsql-studio-gov-tag-value">${_escapeHtml(governanceRoutingSummary(clause))}</span>
                            <button type="button" class="etlsql-studio-icon-btn" data-gov-routing-remove="${_escapeHtml(clause.action)}" title="Remove this route" aria-label="Remove this route">${_studioIcon('trash', 12)}</button>
                        </div>`).join('')
                        : '<div class="etlsql-studio-empty-compact">This statement routes nothing.</div>'}
                </div>
                ${governanceQuarantineLinkMarkup(governance, routing)}
                <div class="etlsql-studio-gov-add">
                    <label class="etlsql-studio-field-label" for="gov-routing-action">Route</label>
                    <select id="gov-routing-action" data-gov-routing-action>
                        ${(vocabulary.actions || []).map((action: StudioDynamic) => `<option value="${_escapeHtml(action)}">${_escapeHtml(action)}</option>`).join('')}
                    </select>
                    <input type="text" data-gov-routing-target placeholder="Target table, e.g. #rejected_orders" aria-label="Target table">
                    <input type="text" data-gov-routing-retention placeholder="Retention, e.g. 30 DAYS" aria-label="Retention">
                    <select data-gov-routing-handling aria-label="Handling">
                        ${(vocabulary.handling || []).map((mode: string) => `<option value="${_escapeHtml(mode)}">${_escapeHtml(mode)}</option>`).join('')}
                    </select>
                    <p class="etlsql-studio-outline-note">STEWARD keeps the rows after the run as a queue item to correct and replay. SCRIPT means this run deals with them and nothing is published for a person to act on.</p>
                    <button type="button" class="etlsql-studio-btn is-primary" data-gov-routing-apply>Set route</button>
                </div>
            </section>`;
    }

    function governanceRoutingSummary(clause: StudioDynamic) {
        const parts = [];
        if (clause.target) parts.push(`to ${clause.target}`);
        if (clause.retention) parts.push(`kept ${clause.retention}`);
        if (clause.target && clause.action === 'QUARANTINE') parts.push(clause.handling.toLowerCase());
        return parts.length ? parts.join(' · ') : 'no target';
    }

    /**
     * The link out to quarantine inspection and replay.
     *
     * <p>Only where there is one. The steward queue is a Portal view over persisted quarantine
     * evidence; the desktop host persists none, so it sends no URL and this says where the queue
     * lives instead of offering a link that goes nowhere.</p>
     */
    function governanceQuarantineLinkMarkup(governance: StudioDynamic, routing: StudioDynamic) {
        const quarantined = routing.filter((clause: StudioDynamic) => clause.action === 'QUARANTINE' && clause.target);
        if (!quarantined.length) return '';

        const targets = quarantined.map((clause: StudioDynamic) => clause.target).join(', ');
        if (!governance.stewardQueueUrl) {
            return `<p class="etlsql-studio-outline-note">Rows land in ${_escapeHtml(targets)}. Inspecting and replaying them is the Portal's steward queue, which reads the quarantine evidence a Portal run persists — this host does not persist any.</p>`;
        }
        return `<p class="etlsql-studio-outline-note">Rows land in ${_escapeHtml(targets)}. <a href="${_escapeHtml(governance.stewardQueueUrl)}" target="_blank" rel="noopener" data-gov-steward-link>Inspect and replay them in the steward queue</a>.</p>`;
    }

    function bindGovernanceQuality(scope: StudioDynamic) {
        const governance = hostContext.state.governance;
        if (!scope) return;

        const formSelect = queryElement(hostContext.sidebarContent, '[data-gov-rule-form]');
        const ruleText = queryElement(hostContext.sidebarContent, '[data-gov-rule-text]');
        const ruleHint = queryElement(hostContext.sidebarContent, '[data-gov-rule-hint]');
        const forms = governance?.qualityVocabulary?.ruleForms || [];
        const paintForm = () => {
            const form = forms[Number(formSelect?.value ?? -1)];
            if (!form || !ruleText || !ruleHint) return;
            ruleText.value = form.template;
            ruleHint.textContent = form.hint;
        };
        formSelect?.addEventListener('change', paintForm);
        paintForm();

        queryElement(hostContext.sidebarContent, '[data-gov-rule-apply]')?.addEventListener('click', () => {
            const rule = ruleText?.value ?? '';
            if (!rule.trim()) {
                _feedback.notify('A rule needs something to check.', { title: 'Nothing written', tone: 'error' });
                return;
            }
            // A template still carrying its placeholders was never filled in. Writing it would be
            // refused by the parser anyway; saying so here names the actual problem.
            if (rule.includes('«')) {
                _feedback.notify('Replace the «placeholders» with the values this rule checks.', { title: 'Nothing written', tone: 'error' });
                return;
            }
            void writeGovernanceQuality('Add rule', {
                op: 'rule',
                scopeId: scope.id,
                index: -1,
                rule,
                action: queryElement(hostContext.sidebarContent, '[data-gov-rule-action]')?.value || null,
            });
        });

        queryElements(hostContext.sidebarContent, '[data-gov-rule-remove]').forEach(button => {
            button.addEventListener('click', () => void writeGovernanceQuality('Remove rule', {
                op: 'rule',
                scopeId: scope.id,
                index: Number(button.getAttribute('data-gov-rule-remove')),
                remove: true,
            }));
        });

        queryElement(hostContext.sidebarContent, '[data-gov-routing-apply]')?.addEventListener('click', () => {
            const action = queryElement(hostContext.sidebarContent, '[data-gov-routing-action]')?.value || '';
            const target = queryElement(hostContext.sidebarContent, '[data-gov-routing-target]')?.value || '';
            const retention = queryElement(hostContext.sidebarContent, '[data-gov-routing-retention]')?.value || '';
            const handling = queryElement(hostContext.sidebarContent, '[data-gov-routing-handling]')?.value || '';
            void writeGovernanceQuality('Set failure routing', {
                op: 'routing',
                statementId: scope.table || scope.name,
                action,
                target: target.trim() || null,
                retention: retention.trim() || null,
                handling: action === 'QUARANTINE' ? handling : null,
            });
        });

        queryElements(hostContext.sidebarContent, '[data-gov-routing-remove]').forEach(button => {
            button.addEventListener('click', () => void writeGovernanceQuality('Remove failure routing', {
                op: 'routing',
                statementId: scope.table || scope.name,
                action: button.getAttribute('data-gov-routing-remove'),
                remove: true,
            }));
        });
    }

    async function writeGovernanceQuality(label: string, operation: StudioDynamic) {
        const result = await hostContext.canonicalScriptMutation(label, STUDIO_ROUTES.governance, operation);
        if (!result) return;
        hostContext.state.governance = result;
        if (hostContext.state.activeActivity === 'governance') hostContext.paintGovernancePanel();
    }

    return { governanceRulesMarkup, governanceRoutingMarkup, bindGovernanceQuality };
}
