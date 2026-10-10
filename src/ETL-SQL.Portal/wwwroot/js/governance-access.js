/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/governance-access.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { hasRole } from './session-identity.js';
/** The access a reader has before, or without, a readable identity. */
export const NO_GOVERNANCE_ACCESS = { overview: false, quarantine: false };
const OVERVIEW_MODES = ['overview', 'workqueue', 'exceptions', 'glossary', 'quality', 'settings'];
const MODES = [...OVERVIEW_MODES, 'quarantine', 'lineage', 'impact'];
export function governanceAccess(identity) {
    return {
        overview: hasRole(identity, 'Admin', 'GovernanceManager', 'StewardshipManager', 'DataSteward', 'GovernanceViewer', 'StewardshipViewer'),
        quarantine: hasRole(identity, 'Admin', 'DataSteward')
    };
}
export function mayUseGovernanceMode(mode, access) {
    return mode === 'quarantine' ? access.quarantine : OVERVIEW_MODES.includes(mode) ? access.overview : true;
}
export function defaultGovernanceMode(access) {
    return access.overview ? 'overview' : access.quarantine ? 'quarantine' : 'lineage';
}
/** The view a governance route opens: the one asked for when it is allowed, the default when none was named, lineage otherwise. */
export function resolveGovernanceRoute(requested, access) {
    const known = MODES.find(mode => mode === requested);
    if (!known)
        return { mode: defaultGovernanceMode(access), redirected: false };
    return mayUseGovernanceMode(known, access) ? { mode: known, redirected: false } : { mode: 'lineage', redirected: true };
}
/** The sidebar entries to reveal; lineage is not listed because the page never hides it. */
export function revealedGovernanceNav(access) {
    return [...(access.overview ? ['govNavOverview', 'govNavWorkqueue', 'govNavExceptions', 'govNavGlossary', 'govNavQuality', 'govNavSettings'] : []),
        ...(access.quarantine ? ['govNavQuarantine'] : [])];
}
/** Whether a mode is one of the views the governance dashboard owns, as opposed to quarantine or lineage. */
export const isDashboardMode = (mode) => OVERVIEW_MODES.includes(mode);
