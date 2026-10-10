import { importFresh } from '../util.js';

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

export function createGovernanceMock(mode = 'live') {
  let heldMethod = null, resume = null, reached = null, paused = null;
  const localAssets = mode === 'empty' ? [] : structuredClone(assets);
  const localFindings = mode === 'empty' ? [] : [...localAssets.flatMap(a => a.findings), ...structuredClone(suppressedFindings)];
  const localCategories = structuredClone(categories), localGlossary = mode === 'empty' ? [] : structuredClone(glossary);
  let localSettings = structuredClone(settings);
  let localScan = mode === 'never-scanned' ? null : structuredClone(completedScan), nextDecisionId = 99;
  const calls = [];
  const guard = () => {
    if (mode === 'unauthorized') throw httpError(403, 'Forbidden');
    if (mode === 'failed') throw httpError(503, 'The governance API could not be reached.');
  };
  const wait = async method => {
    if (heldMethod === method) {
      heldMethod = null;
      await new Promise(resolve => { resume = resolve; reached?.(); });
    }
  };
  const settle = async () => { guard(); if (mode === 'loading') await new Promise(() => {}); };
  const write = async (method, payload, action) => {
    guard(); calls.push({ method, payload: structuredClone(payload) });
    await wait(method);
    const receipt = action();
    if (mode === 'write-unavailable') throw httpError(503, 'Write outcome unavailable');
    return mode === 'bad-receipt' ? {} : structuredClone(receipt);
  };
  const scanFor = () => mode === 'scan-failed' ? failedScan
    : mode === 'scan-running' ? { ...completedScan, status: 'running', completedAtUtc: null } : localScan;
  return {
    calls,
    setMode(value) { mode = value; },
    setGlossaryFlags(term, flags) { Object.assign(localGlossary.find(item => item.term === term), flags); },
    pause(method) { heldMethod = method; paused = new Promise(resolve => { reached = resolve; }); },
    whenPaused() { return paused; },
    release() { resume?.(); resume = null; },
    async confirmChange(message, options) { calls.push({ method: 'confirm', payload: { message, options } }); await wait('confirm'); return true; },
    async dashboard({ scope = 'all' } = {}) {
      await settle();
      const rows = localAssets.filter(row => scope !== 'mine' || row.steward === 'chuck').map(row => ({ ...row,
        governed: row.score >= localSettings.targetScore, findings: localFindings.filter(f => f.assetKey === row.assetKey) }));
      const result = { summary: { ...summaryFor(rows, localFindings), targetScore: localSettings.targetScore },
        assets: mode === 'malformed' ? null : rows, lastScan: scanFor() };
      await wait('dashboard'); return structuredClone(result);
    },
    async findings() { await settle(); return structuredClone(localFindings); },
    async categories() { await settle(); return structuredClone(localCategories); },
    async glossary() { await settle(); return structuredClone(localGlossary); },
    async settings() { await settle(); return structuredClone(localSettings); },
    scan: () => write('scan', null, () => {
      localScan = { ...completedScan, id: (localScan?.id ?? 0) + 1 }; return localScan;
    }),
    decideFinding: (id, body) => write('decideFinding', { id, ...body }, () => {
      const finding = localFindings.find(item => item.id === id);
      finding.status = body.decision === 'ignore' ? 'ignored' : body.decision === 'accept-risk' ? 'accepted-risk' : 'reopened';
      if (body.decision !== 'reopen') finding.assetVersion = body.assetVersion;
      finding.decisions.unshift({ id: nextDecisionId++, ...body, categoryValue: body.categoryValue ?? null, assetVersion: body.assetVersion,
        decidedAtUtc: '2026-10-08T10:00:00Z', decidedBy: 'chuck' });
      return finding;
    }),
    reviewAsset: body => write('reviewAsset', body, () => {
      const asset = localAssets.find(item => item.assetKey === body.assetKey);
      asset.reviewedVersion = body.assetVersion; asset.reviewedAtUtc = '2026-10-08T10:00:00Z';
      return { assetKey: body.assetKey, assetVersion: body.assetVersion, reviewedAtUtc: asset.reviewedAtUtc };
    }),
    assignBadge: body => write('assignBadge', body, () => {
      const asset = localAssets.find(item => item.assetKey === body.assetKey);
      if (!asset.assignedBadges.includes(body.badge)) asset.assignedBadges.push(body.badge);
      return { assetKey: body.assetKey, badge: body.badge };
    }),
    removeBadge: body => write('removeBadge', body, () => {
      const asset = localAssets.find(item => item.assetKey === body.assetKey);
      asset.assignedBadges = asset.assignedBadges.filter(value => value !== body.badge); return null;
    }),
    saveSettings: body => write('saveSettings', body, () => {
      localSettings = { ...localSettings, ...body, version: localSettings.version + 1, updatedAtUtc: '2026-10-08T10:00:00Z' };
      return localSettings;
    }),
    saveCategory: body => write('saveCategory', body, () => {
      let category = localCategories.find(item => item.value === body.value);
      if (!category) { category = { id: 99 }; localCategories.push(category); }
      Object.assign(category, body); return category;
    }),
    disableCategory: value => write('disableCategory', value, () => {
      localCategories.find(item => item.value === value).disabled = true; return null;
    }),
    saveGlossaryTerm: body => write('saveGlossaryTerm', body, () => {
      let term = localGlossary.find(item => item.term === body.term);
      if (!term) { term = { id: 99 }; localGlossary.push(term); }
      Object.assign(term, body, { steward: body.steward ?? null, updatedAtUtc: '2026-10-08T10:00:00Z' }); return term;
    }),
    deleteGlossaryTerm: term => write('deleteGlossaryTerm', term, () => {
      const index = localGlossary.findIndex(item => item.term === term); if (index >= 0) localGlossary.splice(index, 1); return null;
    }),
  };
}

export function createGovernanceQualityMock() {
  let unavailable = false;
  let jobNames = ['nightly_import', 'finance_load'];
  const failedJobs = new Set();
  let heldJob = null, reached = null, resume = null, paused = null;
  const guard = () => { if (unavailable) throw httpError(503, 'Quality evidence unavailable'); };
  return {
    setUnavailable(value) { unavailable = value; },
    setJobNames(value) { jobNames = value; },
    failTrend(jobName) { failedJobs.add(jobName); },
    pauseTrend(jobName) { heldJob = jobName; paused = new Promise(resolve => { reached = resolve; }); },
    whenPaused() { return paused; },
    release() { resume?.(); resume = null; },
    qualityJobs: async () => {
      guard();
      return jobNames.map(name => ({ name, displayName: name === 'nightly_import' ? 'Nightly Import' : name === 'finance_load' ? 'Finance Load' : name,
        description: 'Recorded quality pipeline.', script: '' }));
    },
    qualityTrend: async ({ jobName }) => {
      guard();
      if (failedJobs.has(jobName)) throw httpError(503, 'Trend evidence unavailable');
      if (heldJob === jobName) {
        heldJob = null;
        await new Promise(resolve => { resume = resolve; reached?.(); });
      }
      return {
        jobName, runCount: 1, totalRowsProcessed: 1000, totalRowsQuarantined: 40, totalRowsWarned: 2,
        latestQuarantineRate: 0.04, averageQuarantineRate: 0.04, quarantineRateDelta: null,
        runs: [{ historyId: 7, jobName, startTime: '2026-08-04T12:00:00Z', endTime: '2026-08-04T12:05:00Z',
          status: 'Completed', rowsProcessed: 1000, rowsQuarantined: 40, rowsWarned: 2,
          quarantineRate: 0.04, warnRate: 0.002, ruleFailures: [] }],
        topRuleFailures: [],
      };
    },
    qualityRules: async () => { guard(); return []; },
    qualityRulesAll: async () => {
      guard();
      return [
        { jobName: 'nightly_import', targetTable: 'staging_users', targetColumn: 'email', ruleClause: 'EXPECT', rule: "MATCHES '^[^@]+@[^@]+$'", action: 'QUARANTINE', sourceFile: 'import.etlsql', line: 12 },
        { jobName: 'finance_load', targetTable: 'prod_transactions', targetColumn: 'amount', ruleClause: 'EXPECT', rule: '> 0', action: 'WARN', sourceFile: 'finance.etlsql', line: 45 },
      ];
    },
    quarantineQueue: async () => { guard(); return []; },
  };
}

const settleTick = () => new Promise(resolve => setTimeout(resolve, 0));

const STATE_FIXTURES = ['never-scanned', 'empty', 'unauthorized', 'failed', 'scan-failed', 'scan-running', 'loading'];

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
    { id: 'quality', label: 'Data quality (recorded outcomes)' },
    { id: 'never-scanned', label: 'Never scanned (not "no findings")' },
    { id: 'empty', label: 'Empty estate (scanned, nothing found)' },
    { id: 'unauthorized', label: 'Unauthorized (403)' },
    { id: 'failed', label: 'API failure' },
    { id: 'scan-failed', label: 'Last scan failed' },
    { id: 'scan-running', label: 'Scan still running' },
    { id: 'loading', label: 'Loading' },
  ],
  async mount(stage, fixtureId, ctx) {
    const { createGovernancePortal } = await importFresh('/src/ETL-SQL.Portal/wwwroot/js/governance-portal.js');
    stage.classList.add('portal-page');
    const host = document.createElement('div');
    stage.replaceChildren(host);

    const mode = STATE_FIXTURES.includes(fixtureId) ? fixtureId : 'live';
    const tab = STATE_FIXTURES.includes(fixtureId)
      ? (fixtureId === 'empty' ? 'workqueue' : 'overview')
      : fixtureId;

    const governanceApi = createGovernanceMock(mode);
    const dataQualityApi = createGovernanceQualityMock();
    const messages = [];
    const portal = createGovernancePortal({
      host,
      governanceApi,
      dataQualityApi,
      // Keep sandbox runs quiet and non-blocking; the portal supplies the real implementations.
      notifyFn: (message, o) => { messages.push({ message, options: o }); return ctx.stat(`${o?.title || 'Governance'}: ${message}`); },
      confirmFn: (message, options) => governanceApi.confirmChange(message, options),
    });

    portal.state.tab = tab;
    const rendered = portal.render();
    if (mode !== 'loading') await rendered;
    await settleTick();
    window.__governance = { portal, governanceApi, dataQualityApi, host, messages };

    const shown = host.querySelector('[data-gov-state]')?.getAttribute('data-gov-state');
    ctx.stat(STATE_NOTES[mode] || `${assets.length} assets · state=${shown || 'ready'}`);

    return { dispose() { portal.dispose?.(); }, resize() { } };
  },
};
