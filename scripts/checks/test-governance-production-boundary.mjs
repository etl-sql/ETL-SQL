import assert from 'node:assert/strict';
import { readPortalPageMarkup, readPortalPageModule } from '../lib/portal-page.mjs';
import { defaultGovernanceMode, governanceAccess, mayUseGovernanceMode, NO_GOVERNANCE_ACCESS, resolveGovernanceRoute, revealedGovernanceNav }
  from '../../src/ETL-SQL.Portal/wwwroot/js/governance-access.js';

// What this check used to assert, and why it no longer can:
//
// The governance dashboard was a visual prototype that kept its findings, decisions and glossary in
// browser memory and substituted a hard-coded estate whenever its API threw. It was excluded from
// the production Portal for that reason, and this check enforced the exclusion by asserting that
// index.html mentioned neither `createGovernancePortal` nor any of its nav ids.
//
// `5c8285327` made that surface durable, authorized and audited, and shipped it. The exclusion is
// gone on purpose. What replaced it is a narrower boundary, and that is the one worth guarding:
// Lineage Search is open to any authenticated user, because tracing where a number came from is
// exactly what a report consumer needs it for, and every other governance view is revealed only to
// the roles its API accepts. `2198b5f9d` exists because two views shipped past that gate — a view
// that answers 403 reads as the product being broken rather than as a permission the reader lacks.
//
// It also read index.html alone. The page's code now lives in js/pages/index.js, so half of what it
// was checking had moved out from under it; both halves are read here.
const markup = readPortalPageMarkup('index');
const pageModule = readPortalPageModule('index');

// Compared with whitespace flattened, so that reformatting the module does not read as a removed
// gate — and a removed gate does not hide behind a line break.
const flat = pageModule.split(/\s+/).join(' ');

// ── Half one: nothing but Lineage Search is offered before a role is known ───────────────────────
const navItems = [...markup.matchAll(/<a [^>]*id="(govNav[A-Za-z]+)"([^>]*)>/g)]
  .map(([, id, rest]) => ({ id, hidden: rest.includes('style="display:none"') }));

assert.ok(navItems.length >= 8, `Expected the governance rail, found ${navItems.length} nav items.`);
const lineage = navItems.find(item => item.id === 'govNavLineage');
assert.ok(lineage, 'Lineage Search is missing from the governance rail.');
assert.ok(navItems.some(item => item.id === 'govNavQuarantine'), 'Quarantine Queue is missing from the rail.');
assert.ok(!lineage.hidden, 'Lineage Search is hidden; it is open to any authenticated user.');

const offeredBeforeRole = navItems.filter(item => item.id !== 'govNavLineage' && !item.hidden).map(item => item.id);
assert.deepEqual(offeredBeforeRole, [], `Offered before any role is known: ${offeredBeforeRole.join(', ')}`);

// ── Half two: every gated route redirects to lineage rather than rendering a 403 ─────────────────
// The rules live in governance-access.js and are exercised directly, for every role and route. An
// exact source-string check had accepted a role list that omitted the Portal's seeded stewardship
// roles, and could not tell a reformatted gate from a removed one.
const gatedViews = { overview: 'overview', workqueue: 'overview', exceptions: 'overview', glossary: 'overview',
  quality: 'overview', settings: 'overview', quarantine: 'quarantine' };
assert.deepEqual(NO_GOVERNANCE_ACCESS, { overview: false, quarantine: false }, 'Governance access does not start closed.');
assert.deepEqual(revealedGovernanceNav(NO_GOVERNANCE_ACCESS), [], 'Something is revealed before a role is known.');
assert.equal(defaultGovernanceMode(NO_GOVERNANCE_ACCESS), 'lineage', 'The default governance mode no longer closes to lineage.');

for (const [role, overview, quarantine] of [
  ['Admin', true, true], ['GovernanceManager', true, false], ['StewardshipManager', true, false],
  ['DataSteward', true, true], ['GovernanceViewer', true, false], ['StewardshipViewer', true, false],
  ['Viewer', false, false], ['Publisher', false, false], ['OrchestratorManager', false, false],
  ['OrchestratorViewer', false, false], ['', false, false],
]) {
  const access = governanceAccess({ roles: role ? [role] : [] });
  assert.deepEqual(access, { overview, quarantine }, `${role || 'No role'} has incorrect governance access.`);
  assert.equal(defaultGovernanceMode(access), overview ? 'overview' : quarantine ? 'quarantine' : 'lineage');
  const revealed = revealedGovernanceNav(access);
  for (const [view, gate] of Object.entries(gatedViews)) {
    const allowed = access[gate], navId = `govNav${view[0].toUpperCase()}${view.slice(1)}`;
    assert.deepEqual(resolveGovernanceRoute(view, access), allowed ? { mode: view, redirected: false } : { mode: 'lineage', redirected: true },
      `${role || 'No role'}: governance view '${view}' is not gated with a redirect to lineage.`);
    assert.equal(mayUseGovernanceMode(view, access), allowed);
    // A view that is allowed but never revealed is a dead entry; one revealed but refused answers 403.
    assert.equal(revealed.includes(navId), allowed, `${role || 'No role'}: nav '${navId}' and its gate disagree.`);
    assert.ok(navItems.some(item => item.id === navId), `Governance nav '${navId}' is missing from the rail.`);
  }
  // Lineage and impact need no role, and a route naming no known view opens the reader's default.
  for (const open of ['lineage', 'impact'])
    assert.deepEqual(resolveGovernanceRoute(open, access), { mode: open, redirected: false });
  assert.deepEqual(resolveGovernanceRoute('', access), { mode: defaultGovernanceMode(access), redirected: false });
  assert.ok(!revealed.includes('govNavLineage'));
}

// The page must take its answers from those rules and start from the closed position.
for (const expected of ['access = NO_GOVERNANCE_ACCESS', 'access = governanceAccess(identity)', 'revealedGovernanceNav(access)',
  'resolveGovernanceRoute(requested, access)', "if (redirected) setHash('governance/lineage')"])
  assert.ok(flat.includes(expected), `The catalog page no longer applies the governance access rules: ${expected}`);
assert.ok(!/hasRole\(identity, [^)]*Governance/.test(flat), 'The catalog page decides governance access with its own role list.');
console.log(`Production Governance gates ${Object.keys(gatedViews).length} views behind roles and opens only Lineage Search.`);
