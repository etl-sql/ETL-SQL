import { createGovernancePortal } from '../../../src/ETL-SQL.Portal/wwwroot/js/governance-portal.js';

// This story drives the *real* governance module against a mock API. It used to be a 2,200-line
// re-implementation with its own hard-coded assets, which meant the sandbox could look perfect
// while the shipped module was broken — and, worse, that the mock data sat in the repo as a
// template someone could paste into production. Importing the canonical module is the point of
// the harness.
//
// Fixtures cover the states that are easy to get wrong precisely because they all show very
// little: unauthorized, API failure, never-scanned, and genuinely-empty render similarly, and
// collapsing them is exactly how a governance dashboard ends up lying.

const version = 'loads/sales.etlsql@2026-08-01T02:00:00.0000000Z';

const assets = [
  {
    assetKey: 'sales.orders.customer_id',
    assetVersion: version,
    scriptPath: 'loads/sales.etlsql',
    owner: 'chuck',
    steward: 'chuck',
    domain: 'Sales',
    classification: null,
    score: 85,
    governed: true,
    deductions: [
      { ruleKey: 'missing-metadata', points: 5, reason: 'Missing required metadata: contact' },
      { ruleKey: 'changed-since-review', points: 10, reason: 'No lineage in 30 days' },
    ],
    automaticBadges: ['Needs Metadata', 'Stale Lineage'],
    assignedBadges: ['Trusted'],
    reviewedAtUtc: '2026-07-20T09:00:00Z',
    reviewedVersion: 'loads/sales.etlsql@2026-07-20T02:00:00.0000000Z',
    findings: [
      {
        id: 1, assetKey: 'sales.orders.customer_id', ruleKey: 'missing-metadata',
        assetVersion: version, detail: 'Missing required metadata: contact', status: 'open',
        firstSeenUtc: '2026-08-01T02:05:00Z', lastSeenUtc: '2026-08-01T02:05:00Z',
        resolvedAtUtc: null, suppressedUntilUtc: null, decisions: [],
      },
    ],
  },
  {
    assetKey: 'hr.salaries.base_pay',
    assetVersion: 'loads/hr.etlsql@2026-08-01T02:00:00.0000000Z',
    scriptPath: 'loads/hr.etlsql',
    owner: 'dana',
    steward: 'dana',
    domain: 'Human Resources',
    classification: null,
    score: 45,
    governed: false,
    deductions: [
      { ruleKey: 'missing-metadata', points: 5, reason: 'Missing required metadata: contact, domain' },
      { ruleKey: 'untagged-protected-data', points: 10, reason: 'Protected data present with no @classification tag' },
      { ruleKey: 'below-threshold', points: 40, reason: 'Score 45 is below the threshold of 80' },
    ],
    automaticBadges: ['Needs Metadata', 'Protected Data', 'Untagged Protected Data'],
    assignedBadges: [],
    reviewedAtUtc: null,
    reviewedVersion: null,
    findings: [
      {
        id: 2, assetKey: 'hr.salaries.base_pay', ruleKey: 'untagged-protected-data',
        assetVersion: 'loads/hr.etlsql@2026-08-01T02:00:00.0000000Z',
        detail: 'Protected data present with no @classification tag', status: 'open',
        firstSeenUtc: '2026-08-01T02:05:00Z', lastSeenUtc: '2026-08-01T02:05:00Z',
        resolvedAtUtc: null, suppressedUntilUtc: null, decisions: [],
      },
    ],
  },
];

const suppressedFindings = [
  {
    id: 3, assetKey: 'stage.customer_temp', ruleKey: 'missing-metadata',
    assetVersion: 'loads/stage.etlsql@2026-07-23T02:00:00.0000000Z',
    detail: 'Missing required metadata: owner, steward', status: 'accepted-risk',
    firstSeenUtc: '2026-07-23T02:05:00Z', lastSeenUtc: '2026-08-01T02:05:00Z',
    resolvedAtUtc: null, suppressedUntilUtc: '2026-10-21T00:00:00Z',
    decisions: [{
      id: 10, decision: 'accept-risk', categoryValue: 'temporary',
      reason: 'Scratch table, removed next sprint.',
      assetVersion: 'loads/stage.etlsql@2026-07-23T02:00:00.0000000Z',
      decidedAtUtc: '2026-07-23T10:00:00Z', decidedBy: 'chuck',
    }],
  },
  {
    id: 4, assetKey: 'bi.report_debug', ruleKey: 'below-threshold',
    assetVersion: 'reports/debug.rptsql@2026-07-21T02:00:00.0000000Z',
    detail: 'Score 55 is below the threshold of 80', status: 'ignored',
    firstSeenUtc: '2026-07-21T02:05:00Z', lastSeenUtc: '2026-08-01T02:05:00Z',
    resolvedAtUtc: null, suppressedUntilUtc: null,
    decisions: [{
      id: 11, decision: 'ignore', categoryValue: 'false-positive',
      reason: 'Developer sandbox dashboard with no production connection.',
      assetVersion: 'reports/debug.rptsql@2026-07-21T02:00:00.0000000Z',
      decidedAtUtc: '2026-07-21T11:00:00Z', decidedBy: 'chuck',
    }],
  },
];

const categories = [
  { id: 1, value: 'risk', label: 'Durable Bypass (Security Risk)', color: 'risk', expiryDays: null, disabled: false },
  { id: 2, value: 'false-positive', label: 'False Positive', color: 'false-positive', expiryDays: null, disabled: false },
  { id: 3, value: 'temporary', label: 'Temporary', color: 'noise', expiryDays: 90, disabled: false },
  // Disabled rather than deleted: decisions above still cite categories by value.
  { id: 4, value: 'legacy', label: 'Legacy (retired)', color: 'noise', expiryDays: null, disabled: true },
];

const glossary = [
  {
    id: 1, term: 'revenue', dataType: 'DECIMAL(18,2)', aliases: 'rev, gross_sales, turnover',
    description: 'Sales intake before deductions.', formula: 'SUM(sales_amount)',
    steward: 'chuck', disabled: false, updatedAtUtc: '2026-07-20T09:00:00Z',
  },
  {
    id: 2, term: 'length_of_stay', dataType: 'INT', aliases: 'los, stay_duration',
    description: 'Calendar days hospitalized, for care audit reports.',
    formula: 'DATEDIFF(DAY, admission_date, discharge_date)',
    steward: 'dana', disabled: false, updatedAtUtc: '2026-07-22T09:00:00Z',
  },
];

const settings = {
  targetScore: 80,
  enableMetadataCheck: true,
  enableProtectedDataCheck: true,
  enableGlossaryCheck: false,
  enableStalenessCheck: true,
  deductMetadata: 5,
  deductProtectedData: 10,
  deductGlossary: 5,
  deductStaleness: 15,
  staleAfterDays: 30,
  policyLevel: 'scored',
  updatedAtUtc: '2026-08-01T00:00:00Z',
  version: 3,
};

const completedScan = {
  id: 7, trigger: 'manual',
  startedAtUtc: '2026-08-01T02:05:00Z', completedAtUtc: '2026-08-01T02:05:04Z',
  status: 'completed', error: null,
  assetsScanned: 2, findingsOpened: 2, findingsResolved: 1, findingsReopened: 0,
};

const failedScan = {
  ...completedScan,
  status: 'failed',
  error: 'The lineage catalog was unreachable.',
};

const summaryFor = (rows, findings) => ({
  totalAssets: rows.length,
  governedAssets: rows.filter(a => a.governed).length,
  belowThreshold: rows.filter(a => !a.governed).length,
  openFindings: findings.filter(f => f.status === 'open' || f.status === 'reopened').length,
  ignoredFindings: findings.filter(f => f.status === 'ignored').length,
  acceptedRisks: findings.filter(f => f.status === 'accepted-risk').length,
  targetScore: settings.targetScore,
});

/** Rejects the way the portal's fetch wrapper does, so the module's 403 branch is really exercised. */
const httpError = (status, message) => Object.assign(new Error(message), { status });

function mockApi(mode) {
  const allFindings = [...assets.flatMap(a => a.findings), ...suppressedFindings];
  const rows = mode === 'empty' ? [] : assets;
  const findings = mode === 'empty' ? [] : allFindings;

  const scanFor = () => {
    if (mode === 'never-scanned') return null;
    if (mode === 'scan-failed') return failedScan;
    return completedScan;
  };

  const guard = () => {
    if (mode === 'unauthorized') throw httpError(403, 'Forbidden');
    if (mode === 'failed') throw httpError(503, 'The governance API could not be reached.');
  };

  const settle = async () => {
    guard();
    // Never resolves, pinning the loading state so it can be inspected.
    if (mode === 'loading') await new Promise(() => { });
  };

  return {
    async dashboard() {
      await settle();
      return { summary: summaryFor(rows, findings), assets: rows, lastScan: scanFor() };
    },
    async findings() { await settle(); return findings; },
    async categories() { await settle(); return categories; },
    async glossary() { await settle(); return mode === 'empty' ? [] : glossary; },
    async settings() { await settle(); return settings; },
    async scan() { guard(); return completedScan; },
    async decideFinding() { guard(); return allFindings[0]; },
    async reviewAsset() { guard(); return {}; },
    async assignBadge() { guard(); return {}; },
    async removeBadge() { guard(); return {}; },
    async saveSettings() { guard(); return settings; },
    async saveCategory() { guard(); return categories[0]; },
    async disableCategory() { guard(); return {}; },
    async saveGlossaryTerm() { guard(); return glossary[0]; },
    async deleteGlossaryTerm() { guard(); return {}; },
  };
}

const settleTick = () => new Promise(resolve => setTimeout(resolve, 0));

const STATE_FIXTURES = ['never-scanned', 'empty', 'unauthorized', 'failed', 'scan-failed', 'loading'];

const STATE_NOTES = {
  'never-scanned': 'never scanned — tiles read zero because nothing has been computed',
  empty: 'scanned, genuinely nothing found — distinct from never scanned',
  unauthorized: '403 — a view you cannot see, not an empty estate',
  failed: 'API unreachable — nothing is invented to fill the gap',
  'scan-failed': 'last scan failed — findings shown are stale and say so',
  loading: 'loading — no claim is made until the answer arrives',
};

export default {
  id: 'portal-governance',
  title: 'Portal Governance Module',
  subtitle: 'Durable dashboard, workqueue, and the four honest states',
  fixtures: [
    { id: 'overview', label: 'Overview (live data)' },
    { id: 'workqueue', label: 'Workqueue (scores + why)' },
    { id: 'exceptions', label: 'Exceptions (suppressed, with reasons)' },
    { id: 'glossary', label: 'Glossary' },
    { id: 'settings', label: 'Settings (scoring + categories)' },
    { id: 'never-scanned', label: 'Never scanned (not "no findings")' },
    { id: 'empty', label: 'Empty estate (scanned, nothing found)' },
    { id: 'unauthorized', label: 'Unauthorized (403)' },
    { id: 'failed', label: 'API failure' },
    { id: 'scan-failed', label: 'Last scan failed' },
    { id: 'loading', label: 'Loading' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const host = document.createElement('div');
    stage.replaceChildren(host);

    const mode = STATE_FIXTURES.includes(fixtureId) ? fixtureId : 'live';
    const tab = STATE_FIXTURES.includes(fixtureId)
      ? (fixtureId === 'empty' ? 'workqueue' : 'overview')
      : fixtureId;

    const portal = createGovernancePortal({
      host,
      governanceApi: mockApi(mode),
      dataQualityApi: {
        qualityJobs: async () => [
          { name: 'nightly_import', displayName: 'Nightly Import', description: 'Loads raw user logs and stages them.' },
          { name: 'finance_load', displayName: 'Finance Load', description: 'Daily aggregation of finance records.' }
        ],
        qualityTrend: async () => ({
          jobName: 'nightly_import',
          runCount: 5,
          totalRowsProcessed: 5000,
          totalRowsQuarantined: 200,
          totalRowsWarned: 10,
          latestQuarantineRate: 0.04,
          averageQuarantineRate: 0.04,
          quarantineRateDelta: 0,
          runs: [
            { startTime: '2026-08-04T12:00:00Z', endTime: '2026-08-04T12:05:00Z', status: 'Completed', rowsProcessed: 1000, rowsQuarantined: 40, rowsWarned: 2, quarantineRate: 0.04 }
          ],
          topRuleFailures: []
        }),
        qualityRules: async () => [],
        qualityRulesAll: async () => [
          { jobName: 'nightly_import', targetTable: 'staging_users', targetColumn: 'email', ruleTag: '@expect', rule: 'LIKE %_@_%._%', action: 'QUARANTINE', sourceFile: 'import.etlsql', line: 12 },
          { jobName: 'finance_load', targetTable: 'prod_transactions', targetColumn: 'amount', ruleTag: '@fail', rule: '> 0', action: 'WARN', sourceFile: 'finance.etlsql', line: 45 }
        ],
        quarantineQueue: async () => []
      },
      // Keep sandbox runs quiet and non-blocking; the portal supplies the real implementations.
      notify: (message, o) => ctx.stat(`${o?.title || 'Governance'}: ${message}`),
      confirm: async () => true,
    });

    portal.state.tab = tab;
    const rendered = portal.render();
    if (mode !== 'loading') await rendered;
    await settleTick();

    const shown = host.querySelector('[data-gov-state]')?.getAttribute('data-gov-state');
    ctx.stat(STATE_NOTES[mode] || `${assets.length} assets · state=${shown || 'ready'}`);

    return { dispose() { portal.dispose?.(); }, resize() { } };
  },
};
