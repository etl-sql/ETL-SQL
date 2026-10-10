import assert from 'node:assert/strict';
import vm from 'node:vm';
import ts from './typecheck/node_modules/typescript/lib/typescript.js';
import { readPortalPageMarkup, readPortalPageModule } from './lib/portal-page.mjs';
import { hasRole } from '../src/ETL-SQL.Portal/wwwroot/js/session-identity.js';

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
assert.ok(flat.includes('let canGovernanceOverview = false;'), 'canGovernanceOverview does not start closed.');
assert.ok(flat.includes('let canQuarantine = false;'), 'canQuarantine does not start closed.');
// Exercise the actual permission expressions. An exact source-string check had accepted a
// role list that omitted the Portal's seeded stewardship roles and rejected reusing that list.
const syntax = ts.createSourceFile('index.ts', pageModule, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
let governanceExpression;
const permissions = new Map();
function inspect(node) {
  if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.name.text === 'isGovUser') {
    governanceExpression = node.initializer?.getText(syntax);
  }
  if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.EqualsToken
      && ts.isIdentifier(node.left) && ['canGovernanceOverview', 'canQuarantine'].includes(node.left.text)) {
    permissions.set(node.left.text, node.right.getText(syntax));
  }
  ts.forEachChild(node, inspect);
}
inspect(syntax);
assert.ok(governanceExpression, 'Governance navigation has no permission expression.');
for (const [role, overview, quarantine] of [
  ['Admin', true, true], ['GovernanceManager', true, false], ['StewardshipManager', true, false],
  ['DataSteward', true, true], ['GovernanceViewer', true, false], ['StewardshipViewer', true, false],
  ['Viewer', false, false], ['Publisher', false, false], ['OrchestratorManager', false, false],
  ['OrchestratorViewer', false, false], ['', false, false],
]) {
  for (const [flag, expected] of [['canGovernanceOverview', overview], ['canQuarantine', quarantine]]) {
    const expression = permissions.get(flag);
    assert.ok(expression, `${flag} has no permission assignment.`);
    const actual = vm.runInNewContext(`const isGovUser = ${governanceExpression}; (${expression});`,
      { identity: { roles: role ? [role] : [] }, hasRole });
    assert.equal(actual, expected, `${role || 'No role'} has an incorrect ${flag} permission.`);
  }
}

const gatedViews = {
  overview: 'canGovernanceOverview',
  workqueue: 'canGovernanceOverview',
  exceptions: 'canGovernanceOverview',
  glossary: 'canGovernanceOverview',
  quarantine: 'canQuarantine',
  quality: 'canGovernanceOverview',
  settings: 'canGovernanceOverview',
};

for (const [view, flag] of Object.entries(gatedViews)) {
  const navId = `govNav${view[0].toUpperCase()}${view.slice(1)}`;
  assert.ok(
    flat.includes(`if (subView === '${view}') { mode = ${flag} ? '${view}' : 'lineage'; if (!${flag}) setReportHash('governance/lineage'); }`),
    `Governance view '${view}' is not gated on ${flag} with a redirect to lineage.`);
  assert.ok(
    flat.includes(`document.getElementById('${navId}').style.display = '';`),
    `Governance nav '${navId}' is never revealed, so its gate is unreachable and the entry is dead.`);
}

// Falling back to lineage is the closed position, so it must not itself need a role.
assert.ok(
  flat.includes("function defaultGovernanceMode() { if (canGovernanceOverview) return 'overview'; if (canQuarantine) return 'quarantine'; return 'lineage'; }"),
  'The default governance mode no longer closes to lineage.');

console.log(`Production Governance gates ${Object.keys(gatedViews).length} views behind roles and opens only Lineage Search.`);
