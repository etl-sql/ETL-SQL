// Shared vocabulary for the states every Portal surface has to render.
//
// Four states, kept distinct on purpose, because collapsing them is how a surface misleads:
//
//   loading      — we do not know yet, so we claim nothing
//   denied       — you are not permitted to see this, which is not "there is nothing here"
//   failed       — we asked and could not find out; never a fabricated stand-in
//   empty        — we asked, we know, and the answer is genuinely nothing
//
// They look almost identical on screen — a mostly blank panel — which is exactly why they get
// conflated, and why the difference has to be carried by wording rather than by layout. A user who
// cannot tell "you may not see this" from "the service is down" from "there is nothing here" will
// read all three as the last, because that is the only one that needs no action.
//
// Each renders a `data-portal-state` marker so tests can assert *which* state a surface reached
// rather than inferring it from whatever text happens to be present.

const esc = s => String(s ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;')
  .replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/**
 * A state's heading is an `<h2>`, not bold text. It is usually the most important sentence on the
 * page, and heading navigation is how a screen-reader user finds it.
 */
// `title` and `body` arrive already escaped by the caller. Escaping here instead would put the
// rule two frames away from the interpolation it protects, and would double-escape anything a
// caller had sensibly escaped itself — so the rule is simply: escape at the point of use.
const shell = (state, variant, title, body, extra = '') => `
  <div class="portal-state portal-state-${variant}" data-portal-state="${state}">
    <h2 class="portal-state-title">${title}</h2>
    ${body ? `<p>${body}</p>` : ''}
    ${extra}
  </div>`;

/** We do not know yet. Claim nothing. */
export const loadingState = (message = 'Loading…') => `
  <div class="portal-state portal-state-loading" data-portal-state="loading">
    <span class="portal-spinner"></span> ${esc(message)}
  </div>`;

/**
 * Not permitted. Naming the roles that would grant access turns a dead end into a request someone
 * can actually make.
 */
export const deniedState = ({ title, body, roles = [] } = {}) => shell(
  'unauthorized', 'denied',
  esc(title ?? 'You do not have access to this view.'),
  roles.length
    ? `${esc(body ?? 'Access needs one of these roles:')} ${esc(roles.join(', '))}. `
      + 'This is not an empty view — it is one you cannot see.'
    : esc(body ?? 'This is not an empty view — it is one you cannot see.'));

/**
 * We asked and could not find out. Nothing is shown in place of the real answer: invented content
 * on screen is indistinguishable from real content.
 */
export const failedState = ({ title, body, retryId } = {}) => shell(
  'failed', 'error',
  esc(title ?? 'This data is unavailable.'),
  esc(body ?? 'The service could not be reached.'),
  `<p class="portal-state-note">Nothing is shown in place of the real data. Retry once the service
     is reachable.</p>`
  + (retryId ? `<button class="btn btn-outline btn-xs" id="${esc(retryId)}" type="button">Retry</button>` : ''));

/** We asked, we know, and the answer is genuinely nothing. */
export const emptyState = ({ title, body, action = '' } = {}) => shell(
  'empty', 'empty', esc(title ?? 'Nothing here yet.'), body ? esc(body) : body, action);

/**
 * A status chip. The label is always rendered as text rather than conveyed by colour alone —
 * someone with a colour-vision deficiency, or reading in forced-colours mode, gets nothing from a
 * chip whose only content is its background.
 */
export const statusChip = (label, tone = 'neutral') =>
  `<span class="portal-chip portal-chip-${esc(tone)}">${esc(label)}</span>`;

/** Styles for the vocabulary. Injected once per document by `installPortalStateStyles()`. */
const STYLES = `
.portal-state{border-radius:8px;padding:14px 16px;font-size:13px;line-height:1.5;border:1px solid}
.portal-state p{margin:6px 0 0}
.portal-state-title{margin:0;font-size:14px;font-weight:700}
.portal-state-note{color:var(--portal-muted,#9ca3af);font-size:12px}
.portal-state-loading{border-color:var(--portal-border,#374151);color:var(--portal-muted,#9ca3af);display:flex;align-items:center;gap:10px}
.portal-state-denied{border-color:#7c3aed;background:rgba(124,58,237,.12)}
.portal-state-error{border-color:#dc2626;background:rgba(220,38,38,.12)}
.portal-state-empty{border-color:var(--portal-border,#374151);color:var(--portal-muted,#9ca3af)}
.portal-spinner{width:14px;height:14px;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;display:inline-block;animation:portal-spin .7s linear infinite}
@keyframes portal-spin{to{transform:rotate(360deg)}}
@media (prefers-reduced-motion:reduce){.portal-spinner{animation:none}}
.portal-chip{font-size:10px;border-radius:999px;padding:2px 8px;border:1px solid var(--portal-border,#374151);display:inline-flex;align-items:center;gap:4px}
.portal-chip-ok{color:#10b981;border-color:#10b981}
.portal-chip-warn{color:#f59e0b;border-color:#f59e0b}
.portal-chip-risk{color:#ef4444;border-color:#ef4444}
.portal-chip-neutral{color:var(--portal-muted,#9ca3af)}
`;

/** Idempotent: a page and a module can both call it without duplicating the stylesheet. */
export function installPortalStateStyles(doc = document) {
  if (doc.getElementById('portal-state-styles')) return;
  const style = doc.createElement('style');
  style.id = 'portal-state-styles';
  style.textContent = STYLES;
  doc.head.appendChild(style);
}
