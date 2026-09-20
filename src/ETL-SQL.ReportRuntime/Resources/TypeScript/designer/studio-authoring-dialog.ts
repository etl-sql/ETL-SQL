/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Authoring dialog focus, validation, and blocker feedback.
 */

import { asButton, asHtml } from './studio-authoring-context.js';

import type { StudioAuthoringDialogAction, StudioAuthoringDialogApi, StudioAuthoringDialogElements, StudioAuthoringDialogRenderOptions, StudioAuthoringFeedback } from './studio-authoring-context.js';
import {
escapeHtml
} from './studio-authoring-ui.js';

export interface StudioAuthoringDialogContext {
    readonly dialog: StudioAuthoringDialogElements;
    readonly feedback: StudioAuthoringFeedback;
}

export function createStudioAuthoringDialog(hostContext: StudioAuthoringDialogContext) {

    function studioDialog<T = any>(
        { kicker, title, wide = false }: { kicker: string; title: string; wide?: boolean },
        controller: (api: StudioAuthoringDialogApi) => void
    ): Promise<T | null> {
        return new Promise(resolve => {
            let settled = false;
            const previouslyFocused = document.activeElement as HTMLElement | null;

            const close = (value?: any) => {
                if (settled) return;
                settled = true;
                document.removeEventListener('keydown', onKeyDown, true);
                hostContext.dialog.backdrop.removeEventListener('click', onBackdropClick);
                hostContext.dialog.backdrop.hidden = true;
                hostContext.dialog.box.removeAttribute('role');
                hostContext.dialog.box.removeAttribute('aria-modal');
                hostContext.dialog.box.removeAttribute('aria-labelledby');
                hostContext.dialog.box.innerHTML = '';
                hostContext.dialog.box.classList.remove('etlsql-studio-dialog-wide');
                resolve(value === undefined ? null : value);
                asHtml(previouslyFocused)?.focus?.();
            };
            const onKeyDown = (event: KeyboardEvent) => {
                if (event.key === 'Escape') {
                    event.stopPropagation();
                    event.preventDefault();
                    close(null);
                    return;
                }
                if (event.key === 'Tab') {
                    const focusable = (Array.from(
                        hostContext.dialog.box.querySelectorAll(
                            'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
                        )
                    ) as HTMLElement[]).map(asHtml).filter(el => el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement);

                    if (focusable.length === 0) {
                        event.preventDefault();
                        return;
                    }

                    const first = asHtml(focusable[0]);
                    const last = asHtml(focusable[focusable.length - 1]);

                    if (event.shiftKey) {
                        if (document.activeElement === first || !hostContext.dialog.box.contains(document.activeElement)) {
                            event.preventDefault();
                            last.focus();
                        }
                    } else {
                        if (document.activeElement === last || !hostContext.dialog.box.contains(document.activeElement)) {
                            event.preventDefault();
                            first.focus();
                        }
                    }
                }
            };
            const onBackdropClick = (event: MouseEvent) => {
                if (event.target === hostContext.dialog.backdrop) close(null);
            };

            hostContext.dialog.box.setAttribute('role', 'dialog');
            hostContext.dialog.box.setAttribute('aria-modal', 'true');
            hostContext.dialog.box.setAttribute('aria-labelledby', 'etlsql-studio-dialog-title');

            hostContext.dialog.box.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <div><span class="etlsql-studio-kicker">${escapeHtml(kicker)}</span><h2 data-dialog-title id="etlsql-studio-dialog-title">${escapeHtml(title)}</h2></div>
                    <button type="button" class="etlsql-studio-dialog-dismiss" data-dialog-dismiss aria-label="Close">&times;</button>
                </div>
                <div class="etlsql-studio-modal-body etlsql-studio-guided-body" data-dialog-body></div>
                <footer class="etlsql-studio-dialog-actions" data-dialog-actions></footer>`;
            if (wide) hostContext.dialog.box.classList.add('etlsql-studio-dialog-wide');
            hostContext.dialog.backdrop.hidden = false;
            document.addEventListener('keydown', onKeyDown, true);
            hostContext.dialog.backdrop.addEventListener('click', onBackdropClick);
            hostContext.dialog.box.querySelector('[data-dialog-dismiss]')?.addEventListener('click', () => close(null));

            const bodyHost = asHtml(hostContext.dialog.box.querySelector('[data-dialog-body]'));
            const actionHost = asHtml(hostContext.dialog.box.querySelector('[data-dialog-actions]'));

            // The footer buttons survive a re-render when the same actions are still on offer.
            //
            // A guided form repaints itself when a field changes, to keep its SQL preview and its
            // warnings true. `change` fires on blur — which is what clicking a footer button does —
            // so replacing the buttons on every repaint destroyed the very button being pressed
            // between its mousedown and its mouseup, and a click needs both on one element. The
            // first press after editing any field did nothing at all, silently, and the author had
            // to press again. `currentActions` is what the delegated handler reads, so the buttons
            // can stay put while what they do stays current.
            let currentActions: StudioAuthoringDialogAction[] = [];
            let actionSignature: string | null = null;
            actionHost.addEventListener('click', async event => {
                const button = asButton(asHtml(event.target)?.closest('[data-dialog-action]'));
                if (!button || button.disabled) return;
                try {
                    await currentActions.find(action => action.id === button.dataset.dialogAction)?.run?.();
                } catch (error: any) {
                    // A dropped promise here is invisible: the mutation may already have landed
                    // while the dialog silently stops responding. Say so instead.
                    hostContext.feedback.notify(error?.message || 'That action could not be completed.',
                        { title: 'Action failed', tone: 'error' });
                }
            });
            const api: StudioAuthoringDialogApi = {
                close,
                setTitle(next: string) {
                    const titleEl = hostContext.dialog.box.querySelector('[data-dialog-title]');
                    if (titleEl) titleEl.textContent = next;
                },
                // Every footer button is disabled while a request is in flight, so a slow schema read
                // cannot be double-submitted into two datasets.
                busy(flag: boolean) {
                    actionHost.querySelectorAll('button').forEach(button => { asButton(button).disabled = flag; });
                },
                /**
                 * @param {Object} [content]
                 * @param {string} [content.lede]  Pre-sanitized HTML. Callers MUST escape any
                 *     dynamic values with `escapeHtml` or produce markup via the safe builder
                 *     functions from studio-authoring-ui.js. Raw user-supplied strings must never
                 *     be passed directly.
                 * @param {string} [content.body]  Pre-sanitized HTML. Same contract as `lede`:
                 *     all dynamic content must go through `escapeHtml`, `sqlPreviewMarkup`,
                 *     `sampleGridMarkup`, `mutationExplanationMarkup`, or equivalent safe builders
                 *     before being interpolated into this string.
                 * @param {Array<*>} [content.actions]
                 * @param {Function} [content.wire] Called with the body once it is in the DOM.
                 */
                render({ lede = '', body = '', actions = [], wire }: StudioAuthoringDialogRenderOptions = {}) {
                    // Trusted-HTML insertion point. Both `lede` and `body` are pre-sanitized by
                    // every caller in this module (dynamic values go through escapeHtml or a safe
                    // HTML builder). Do NOT pass raw user-supplied strings here directly.
                    bodyHost.innerHTML = (lede ? `<p class="etlsql-studio-guided-lede">${lede}</p>` : '') + body;
                    currentActions = actions;
                    // Rebuilt only when the offer itself changed. `disabled` is not part of the
                    // signature: it flips while the author types, which is exactly when the buttons
                    // must not be replaced, so it is applied to the buttons already there.
                    const signature = actions
                        .map(action => [action.id, action.label, action.primary ? 1 : 0].join('|'))
                        .join('||');
                    if (signature !== actionSignature) {
                        actionSignature = signature;
                        actionHost.innerHTML = actions.map(action => `<button type="button"
                            class="etlsql-studio-btn${action.primary ? ' is-primary' : ''}"
                            data-dialog-action="${escapeHtml(action.id)}"
                            >${escapeHtml(action.label)}</button>`).join('');
                    }
                    for (const action of actions) {
                        const button = [...actionHost.children].find(node => asHtml(node).dataset?.dialogAction === action.id);
                        if (button) asButton(button).disabled = Boolean(action.disabled);
                    }
                    wire?.(bodyHost);
                    asHtml(bodyHost.querySelector('input:not([type=hidden]), select, textarea'))?.focus();
                },
            };
            controller(api);
        });
    }

    /**
     * Explains why a step cannot run and offers the control that unblocks it. Returns true when the
     * author took the remedy, so the caller can retry the step.
     */
    async function guidedBlocker({ kicker, title, lede, remedyLabel, remedy }: {
        kicker: string;
        title: string;
        lede: string;
        remedyLabel: string;
        remedy: () => Promise<any> | any;
    }): Promise<boolean> {
        const choice = await studioDialog({ kicker, title }, api => api.render({
            lede,
            actions: [
                { id: 'cancel', label: 'Not now', run: () => api.close(null) },
                { id: 'fix', label: remedyLabel, primary: true, run: () => api.close('fix') },
            ],
        }));
        if (choice !== 'fix') return false;
        await remedy();
        return true;
    }

    return { studioDialog, guidedBlocker };
}
