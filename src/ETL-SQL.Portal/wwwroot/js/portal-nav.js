// The Portal's top-level navigation, applied from one server-computed answer.
//
// Every page used to work this out for itself, from JWT claims, in five different spellings of the
// same decision. That is survivable for the entries a claim can actually answer — Admin and
// Orchestrator are role checks — and wrong for the two that it cannot:
//
//   Docs    depends on whether the Documentation module is enabled. There is no claim for that, so
//           every page offered a Docs link that 404s wherever the module is off.
//   Studio  depends on a Studio capability. Pages revealed it whenever the capability *probe*
//           succeeded — but that probe was deliberately opened to every authenticated user so that
//           asking "what may I do in Studio?" would stop being an error for the roles that may do
//           nothing. The probe answering is not the answer being yes, so Studio was offered to
//           everybody, including roles holding no Studio capability at all.
//
// Both are the same defect: a navigation that offers what it cannot deliver reads as the product
// being broken rather than as a permission the user lacks. Neither is fixed by being more careful
// in six copies of the rule.
//
// The server sends `[{ id, visible, reason }]`; this module only applies it. Deliberately no
// fallback rule here — a client-side guess is what this replaces, and a wrong guess that shows an
// entry is worse than an entry briefly missing.

import { navigationApi } from './api.js';

/**
 * Reveals or hides each destination the server named.
 *
 * Hidden is the markup default (`style="display:none"`), so a destination the server does not
 * mention is left alone rather than assumed permitted.
 *
 * `display = ''` rather than a hard-coded `inline-block`: the value belongs to the stylesheet, and
 * at narrow viewports the whole `.topbar-nav` is hidden in favour of the drawer, which mirrors
 * whatever is visible here.
 *
 * @param {Document} doc
 * @returns {Promise<Record<string, boolean>>} what was applied, keyed by element id.
 */
export async function applyNavigation(doc = document) {
    const destinations = await navigationApi.destinations();
    const applied = {};

    for (const { id, visible, reason } of destinations) {
        const element = doc.getElementById(id);
        applied[id] = !!visible;
        if (!element) continue;

        element.style.display = visible ? '' : 'none';
        // Kept for diagnosis only. Never rendered: telling a user which capability they lack is a
        // different decision from telling an operator, and this is the operator's answer.
        if (!visible && reason) element.dataset.navHiddenReason = reason;
        else delete element.dataset.navHiddenReason;
    }

    // Marks the answer as applied. A test — or anything else waiting to read the navigation — needs
    // to distinguish "hidden because you may not have it" from "not decided yet", and those look
    // identical in the DOM: both are the markup default. Without this an absence check races the
    // fetch and passes for the wrong reason, which is the failure mode that never gets noticed
    // because it is green.
    doc.body?.setAttribute('data-nav-applied', 'true');
    return applied;
}

/**
 * Applies navigation without letting a failure take the page down with it.
 *
 * A page whose navigation could not be resolved is still a working page; one that threw during
 * init is not. On failure every gated entry stays hidden, which is the safe direction — the
 * failure mode this module exists to prevent is showing an entry that does not work.
 */
export async function applyNavigationSafely(doc = document) {
    try {
        return await applyNavigation(doc);
    } catch {
        // Still marked applied: the answer is settled — it is "nothing extra" — and a caller
        // waiting on it should stop waiting rather than hang until a timeout.
        doc.body?.setAttribute('data-nav-applied', 'true');
        return {};
    }
}
