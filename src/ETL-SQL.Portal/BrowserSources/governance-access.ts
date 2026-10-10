import { hasRole } from './session-identity.js';
import type { SessionIdentity } from './session-identity.js';

/**
 * Which governance views a reader is offered, and where a route they may not use sends them.
 *
 * Lineage is open to every signed-in reader: tracing where a number came from is what a report
 * consumer needs it for. Every other view is revealed only to the roles its API accepts, because a
 * view that answers 403 reads as the product being broken rather than as a missing permission.
 * The closed position is lineage, so it must never itself need a role.
 */
export type GovernanceMode = 'overview' | 'workqueue' | 'exceptions' | 'glossary' | 'quality' | 'quarantine' | 'lineage' | 'impact' | 'settings';
export interface GovernanceAccess { overview: boolean; quarantine: boolean }

/** The access a reader has before, or without, a readable identity. */
export const NO_GOVERNANCE_ACCESS: GovernanceAccess = { overview: false, quarantine: false };

const OVERVIEW_MODES: readonly GovernanceMode[] = ['overview', 'workqueue', 'exceptions', 'glossary', 'quality', 'settings'];
const MODES: readonly GovernanceMode[] = [...OVERVIEW_MODES, 'quarantine', 'lineage', 'impact'];

export function governanceAccess(identity: SessionIdentity): GovernanceAccess {
    return {
        overview: hasRole(identity, 'Admin', 'GovernanceManager', 'StewardshipManager', 'DataSteward', 'GovernanceViewer', 'StewardshipViewer'),
        quarantine: hasRole(identity, 'Admin', 'DataSteward')
    };
}

export function mayUseGovernanceMode(mode: GovernanceMode, access: GovernanceAccess): boolean {
    return mode === 'quarantine' ? access.quarantine : OVERVIEW_MODES.includes(mode) ? access.overview : true;
}

export function defaultGovernanceMode(access: GovernanceAccess): GovernanceMode {
    return access.overview ? 'overview' : access.quarantine ? 'quarantine' : 'lineage';
}

/** The view a governance route opens: the one asked for when it is allowed, the default when none was named, lineage otherwise. */
export function resolveGovernanceRoute(requested: string, access: GovernanceAccess): { mode: GovernanceMode; redirected: boolean } {
    const known = MODES.find(mode => mode === requested);
    if (!known) return { mode: defaultGovernanceMode(access), redirected: false };
    return mayUseGovernanceMode(known, access) ? { mode: known, redirected: false } : { mode: 'lineage', redirected: true };
}

/** The sidebar entries to reveal; lineage is not listed because the page never hides it. */
export function revealedGovernanceNav(access: GovernanceAccess): string[] {
    return [...(access.overview ? ['govNavOverview', 'govNavWorkqueue', 'govNavExceptions', 'govNavGlossary', 'govNavQuality', 'govNavSettings'] : []),
        ...(access.quarantine ? ['govNavQuarantine'] : [])];
}

/** Whether a mode is one of the views the governance dashboard owns, as opposed to quarantine or lineage. */
export const isDashboardMode = (mode: GovernanceMode): boolean => OVERVIEW_MODES.includes(mode);
