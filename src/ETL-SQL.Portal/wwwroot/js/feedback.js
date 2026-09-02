/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/feedback.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/*
 * ETL-SQL shared feedback system.
 * Loaded as a classic script so Portal, ReportPlayer, Workstation, and editor webviews can share
 * the same accessible toast, confirmation, and validated-input dialogs without dependencies.
 */
(function installFeedback(global) {
    'use strict';
    if (global.ETLSQLFeedback) return;

    const doc = global.document;
    let sequence = 0;
    let toastRegion;

    function ensureStyles() {
        if (!doc || doc.getElementById('etlsql-feedback-styles')) return;
        const style = doc.createElement('style');
        style.id = 'etlsql-feedback-styles';
        style.textContent = `
.etlsql-feedback-toasts{position:fixed;right:20px;bottom:20px;z-index:100000;display:grid;gap:10px;width:min(390px,calc(100vw - 32px));pointer-events:none}
.etlsql-feedback-toast{pointer-events:auto;display:grid;grid-template-columns:4px 1fr auto;gap:12px;align-items:start;padding:14px 14px 14px 0;border:1px solid var(--portal-border,#d7dee8);border-radius:10px;background:var(--portal-surface,#fff);color:var(--portal-text,#172033);box-shadow:0 14px 36px rgba(15,23,42,.18)}
.etlsql-feedback-toast::before{content:'';align-self:stretch;border-radius:999px;background:#2563eb}.etlsql-feedback-toast[data-tone=success]::before{background:#16815d}.etlsql-feedback-toast[data-tone=warning]::before{background:#b76b00}.etlsql-feedback-toast[data-tone=error]::before{background:#c43232}
.etlsql-feedback-toast strong{display:block;margin-bottom:3px}.etlsql-feedback-toast p{margin:0;line-height:1.4;overflow-wrap:anywhere}.etlsql-feedback-close{border:0;background:transparent;color:inherit;font:inherit;font-size:20px;line-height:1;cursor:pointer}
.etlsql-feedback-action{margin-top:9px;min-height:30px;padding:5px 12px;border:1px solid var(--portal-border,#b8c3d1);border-radius:7px;background:var(--portal-surface,#fff);color:inherit;font:inherit;font-weight:650;cursor:pointer}.etlsql-feedback-action:hover{border-color:#2563eb;color:#2563eb}.etlsql-feedback-action:focus-visible{outline:3px solid rgba(37,99,235,.34);outline-offset:2px}
.etlsql-feedback-backdrop{position:fixed;inset:0;z-index:100001;display:grid;place-items:center;padding:20px;background:rgba(15,23,42,.56);backdrop-filter:blur(2px)}
.etlsql-feedback-dialog{width:min(520px,100%);max-height:min(720px,calc(100vh - 40px));overflow:auto;border:1px solid var(--portal-border,#d7dee8);border-radius:12px;background:var(--portal-surface,#fff);color:var(--portal-text,#172033);box-shadow:0 24px 64px rgba(15,23,42,.28)}
.etlsql-feedback-header,.etlsql-feedback-body,.etlsql-feedback-actions{padding:18px 20px}.etlsql-feedback-header{border-bottom:1px solid var(--portal-border-soft,#e8edf4)}.etlsql-feedback-header h2{margin:0;font-size:1.08rem}.etlsql-feedback-body p{margin:0;line-height:1.55;white-space:pre-wrap;overflow-wrap:anywhere}.etlsql-feedback-impact{margin-top:14px!important;padding:11px 12px;border-left:3px solid #b76b00;background:rgba(183,107,0,.09);font-size:.9rem}.etlsql-feedback-field{display:grid;gap:7px;margin-top:16px;font-weight:650}.etlsql-feedback-field input,.etlsql-feedback-field textarea{box-sizing:border-box;width:100%;padding:10px 11px;border:1px solid var(--portal-border,#b8c3d1);border-radius:7px;background:var(--portal-bg,#fff);color:inherit;font:inherit;font-weight:400}.etlsql-feedback-field textarea{min-height:96px;resize:vertical}.etlsql-feedback-error{min-height:1.2em;margin-top:6px;color:#c43232;font-size:.84rem}
.etlsql-feedback-actions{display:flex;justify-content:flex-end;gap:9px;border-top:1px solid var(--portal-border-soft,#e8edf4);background:var(--portal-surface-subtle,#f7f9fc)}.etlsql-feedback-btn{min-height:38px;padding:8px 14px;border:1px solid var(--portal-border,#b8c3d1);border-radius:7px;background:var(--portal-surface,#fff);color:inherit;font:inherit;font-weight:650;cursor:pointer}.etlsql-feedback-btn-primary{border-color:#2563eb;background:#2563eb;color:#fff}.etlsql-feedback-btn-danger{border-color:#b4232c;background:#b4232c;color:#fff}.etlsql-feedback-btn:focus-visible,.etlsql-feedback-close:focus-visible,.etlsql-feedback-field input:focus-visible,.etlsql-feedback-field textarea:focus-visible{outline:3px solid rgba(37,99,235,.34);outline-offset:2px}
@media(max-width:560px){.etlsql-feedback-toasts{right:16px;bottom:16px}.etlsql-feedback-backdrop{align-items:end;padding:0}.etlsql-feedback-dialog{width:100%;max-height:90vh;border-radius:14px 14px 0 0}.etlsql-feedback-actions{position:sticky;bottom:0}}
@media(prefers-reduced-motion:no-preference){.etlsql-feedback-toast{animation:etlsql-feedback-in .18s ease-out}.etlsql-feedback-dialog{animation:etlsql-dialog-in .16s ease-out}@keyframes etlsql-feedback-in{from{opacity:0;transform:translateY(8px)}}@keyframes etlsql-dialog-in{from{opacity:0;transform:translateY(10px) scale(.985)}}}`;
        doc.head.appendChild(style);
    }

    function emit(kind, detail) {
        doc?.dispatchEvent(new CustomEvent('etlsql:feedback', { detail: { kind, ...detail } }));
    }

    /**
     * Shows a toast. Returns a function that dismisses it, so a caller whose action repeats — one
     * undo offer per click in a filter list — can replace its own previous toast instead of stacking
     * a column of them over the panel the reader is working in.
     */
    function notify(message, options = {}) {
        if (!doc) return () => {};
        ensureStyles();
        if (!toastRegion) {
            toastRegion = doc.createElement('div');
            toastRegion.className = 'etlsql-feedback-toasts';
            toastRegion.setAttribute('role', 'region');
            toastRegion.setAttribute('aria-label', 'Notifications');
            toastRegion.setAttribute('aria-live', options.tone === 'error' ? 'assertive' : 'polite');
            doc.body.appendChild(toastRegion);
        }
        const tone = ['success', 'warning', 'error'].includes(options.tone) ? options.tone : 'info';
        const toast = doc.createElement('div');
        toast.className = 'etlsql-feedback-toast';
        toast.dataset.tone = tone;
        toast.setAttribute('role', tone === 'error' ? 'alert' : 'status');
        const content = doc.createElement('div');
        if (options.title) { const title = doc.createElement('strong'); title.textContent = options.title; content.appendChild(title); }
        const text = doc.createElement('p'); text.textContent = String(message ?? ''); content.appendChild(text);
        const close = doc.createElement('button'); close.type = 'button'; close.className = 'etlsql-feedback-close'; close.setAttribute('aria-label', 'Dismiss notification'); close.textContent = '×';
        const remove = () => toast.remove(); close.addEventListener('click', remove);
        // An offer the reader can act on — Undo, most of all — has to be a real focusable button in
        // the toast, not a sentence telling them where to look for one. It is dismissible either
        // way: taking the action and ignoring it both end with the toast gone.
        const action = options.action && typeof options.action.onSelect === 'function' ? options.action : null;
        if (action) {
            const button = doc.createElement('button');
            button.type = 'button';
            button.className = 'etlsql-feedback-action';
            button.textContent = action.label || 'Undo';
            button.addEventListener('click', () => { remove(); action.onSelect(); });
            content.appendChild(button);
        }
        toast.append(doc.createElement('span'), content, close); toastRegion.appendChild(toast);
        // An actionable toast that vanishes on the usual timer is an offer nobody can accept, so it
        // stays until it is used or dismissed unless the caller names its own duration.
        const duration = Number.isFinite(options.duration) ? options.duration : (action ? 12000 : tone === 'error' ? 8000 : 4500);
        if (duration > 0) global.setTimeout(remove, duration);
        emit('notification', { tone, action: options.auditAction || null });
        return remove;
    }

    function openDialog(message, options = {}, promptOptions = null) {
        if (!doc) return Promise.resolve(promptOptions ? null : false);
        ensureStyles();
        return new Promise(resolve => {
            const id = `etlsql-feedback-${++sequence}`;
            const previousFocus = doc.activeElement;
            const backdrop = doc.createElement('div'); backdrop.className = 'etlsql-feedback-backdrop';
            const dialog = doc.createElement('section'); dialog.className = 'etlsql-feedback-dialog'; dialog.setAttribute('role', 'dialog'); dialog.setAttribute('aria-modal', 'true'); dialog.setAttribute('aria-labelledby', `${id}-title`); dialog.setAttribute('aria-describedby', `${id}-message`);
            const header = doc.createElement('div'); header.className = 'etlsql-feedback-header';
            const title = doc.createElement('h2'); title.id = `${id}-title`; title.textContent = options.title || (promptOptions ? 'Provide details' : 'Confirm action'); header.appendChild(title);
            const body = doc.createElement('div'); body.className = 'etlsql-feedback-body';
            const text = doc.createElement('p'); text.id = `${id}-message`; text.textContent = String(message ?? ''); body.appendChild(text);
            if (options.impact) { const impact = doc.createElement('p'); impact.className = 'etlsql-feedback-impact'; impact.textContent = options.impact; body.appendChild(impact); }
            let input = null; let error = null;
            if (promptOptions) {
                const field = doc.createElement('label'); field.className = 'etlsql-feedback-field'; field.textContent = promptOptions.label || 'Value';
                input = doc.createElement(promptOptions.multiline ? 'textarea' : 'input');
                if (!promptOptions.multiline) input.type = promptOptions.secret ? 'password' : 'text';
                input.value = promptOptions.value || ''; input.autocomplete = promptOptions.autocomplete || 'off';
                field.appendChild(input); body.appendChild(field);
                error = doc.createElement('div'); error.className = 'etlsql-feedback-error'; error.setAttribute('role', 'alert'); body.appendChild(error);
            }
            const actions = doc.createElement('div'); actions.className = 'etlsql-feedback-actions';
            const cancel = doc.createElement('button'); cancel.type = 'button'; cancel.className = 'etlsql-feedback-btn'; cancel.textContent = options.cancelLabel || 'Cancel';
            const accept = doc.createElement('button'); accept.type = 'button'; accept.className = `etlsql-feedback-btn ${options.danger ? 'etlsql-feedback-btn-danger' : 'etlsql-feedback-btn-primary'}`; accept.textContent = options.confirmLabel || 'Continue';
            actions.append(cancel, accept); dialog.append(header, body, actions); backdrop.appendChild(dialog); doc.body.appendChild(backdrop);
            const finish = value => { backdrop.remove(); previousFocus?.focus?.(); emit(promptOptions ? 'prompt' : 'confirmation', { accepted: value !== false && value !== null, action: options.auditAction || null }); resolve(value); };
            cancel.addEventListener('click', () => finish(promptOptions ? null : false));
            accept.addEventListener('click', () => {
                if (!promptOptions) { finish(true); return; }
                const value = input.value.trim();
                if (promptOptions.required && !value) { error.textContent = promptOptions.requiredMessage || 'This field is required.'; input.focus(); return; }
                if (promptOptions.minLength && value.length < promptOptions.minLength) { error.textContent = `Enter at least ${promptOptions.minLength} characters.`; input.focus(); return; }
                if (promptOptions.pattern && !promptOptions.pattern.test(value)) { error.textContent = promptOptions.patternMessage || 'Enter a valid value.'; input.focus(); return; }
                finish(value);
            });
            backdrop.addEventListener('click', event => { if (event.target === backdrop) finish(promptOptions ? null : false); });
            backdrop.addEventListener('keydown', event => {
                if (event.key === 'Escape') { event.preventDefault(); finish(promptOptions ? null : false); return; }
                if (event.key !== 'Tab') return;
                const focusable = [...dialog.querySelectorAll('button,input,textarea')].filter(element => !element.disabled);
                const first = focusable[0], last = focusable[focusable.length - 1];
                if (event.shiftKey && doc.activeElement === first) { event.preventDefault(); last.focus(); }
                else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus(); }
            });
            global.setTimeout(() => (input || (options.danger ? cancel : accept)).focus(), 0);
        });
    }

    global.ETLSQLFeedback = Object.freeze({
        notify,
        confirm: (message, options = {}) => openDialog(message, options, null),
        prompt: (message, options = {}) => openDialog(message, options, options),
    });
})(typeof window === 'undefined' ? globalThis : window);
