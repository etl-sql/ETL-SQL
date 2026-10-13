import assert from 'node:assert/strict';
import { readGovernanceDashboard, readGovernanceFindings, readGovernanceScan, readGovernanceSettings,
  readGovernanceCategories, readGovernanceGlossary, readGovernanceQualityRules,
  confirmGovernanceScan, confirmGovernanceReview, confirmGovernanceBadge, confirmGovernanceFinding,
  confirmGovernanceSettings, confirmGovernanceCategory, confirmGovernanceGlossary, confirmGovernanceNoContent,
  GovernanceScanFailed } from '../../src/ETL-SQL.Portal/wwwroot/js/governance-responses.js';
import { readQualityJobs, readQualityTrend } from '../../src/ETL-SQL.Portal/wwwroot/js/data-quality-responses.js';
import { createGovernanceMock, createGovernanceQualityMock } from '../../tools/ui-sandbox/stories/governance.story.js';

const api = createGovernanceMock();
const dashboard = readGovernanceDashboard(await api.dashboard());
assert.equal(dashboard.assets.length, 2);
for (const bad of [null, {}, { ...dashboard, assets: null },
  { ...dashboard, summary: { ...dashboard.summary, totalAssets: 0 } },
  { ...dashboard, assets: [dashboard.assets[0], dashboard.assets[0]] },
  { ...dashboard, assets: [{ ...dashboard.assets[0], deductions: null }] },
  { ...dashboard, lastScan: {} }]) assert.throws(() => readGovernanceDashboard(bad));
assert.equal(readGovernanceDashboard(await createGovernanceMock('empty').dashboard()).assets.length, 0);
assert.equal(readGovernanceDashboard(await createGovernanceMock('never-scanned').dashboard()).lastScan, null);
assert.equal(readGovernanceDashboard(await createGovernanceMock('scan-failed').dashboard()).lastScan.status, 'failed');
assert.equal(readGovernanceDashboard(await api.dashboard({ scope: 'mine' })).assets.length, 1);

const findings = readGovernanceFindings(await api.findings());
assert.equal(findings.length, 4);
assert.deepEqual(readGovernanceFindings([]), []);
for (const bad of [null, [...findings, findings[0]], [{ ...findings[0], decisions: null }],
  [{ ...findings[0], status: 'success' }], [{ ...findings[2], decisions: [{ ...findings[2].decisions[0], reason: '' }] }]]) {
  assert.throws(() => readGovernanceFindings(bad));
}
assert.equal(readGovernanceScan(await api.scan()).status, 'completed');
assert.throws(() => readGovernanceScan({ status: 'completed' }));
const settings = readGovernanceSettings(await api.settings());
for (const bad of [{ ...settings, targetScore: 101 }, { ...settings, enableGlossaryCheck: 'false' },
  { ...settings, deductMetadata: -1 }, { ...settings, staleAfterDays: 0 }, { ...settings, policyLevel: 'unknown' },
  { ...settings, version: 1.5 }]) assert.throws(() => readGovernanceSettings(bad));
const categories = readGovernanceCategories(await api.categories());
assert.equal(categories.length, 4);
assert.throws(() => readGovernanceCategories([...categories, categories[0]]));
assert.throws(() => readGovernanceCategories([{ ...categories[0], expiryDays: 0 }]));
const glossary = readGovernanceGlossary(await api.glossary());
assert.equal(glossary.length, 2);
assert.throws(() => readGovernanceGlossary([{ ...glossary[0], disabled: 'false' }]));
assert.throws(() => readGovernanceGlossary([...glossary, glossary[0]]));
const quality = createGovernanceQualityMock();
const jobs = readQualityJobs(await quality.qualityJobs());
for (const job of jobs) assert.equal(readQualityTrend(await quality.qualityTrend({ jobName: job.name }), job.name).runCount, 1);
assert.equal(readGovernanceQualityRules(await quality.qualityRulesAll()).length, 2);
assert.throws(() => readGovernanceQualityRules([{ jobName: 'nightly_import' }]));

const scan = await api.scan();
confirmGovernanceScan(scan);
assert.throws(() => confirmGovernanceScan(scan, scan.id));
assert.throws(() => confirmGovernanceScan({ ...scan, status: 'running', completedAtUtc: null }));
assert.throws(() => confirmGovernanceScan({ ...scan, status: 'failed' }), GovernanceScanFailed);
assert.throws(() => confirmGovernanceScan({ ...scan, trigger: 'automatic' }));
assert.throws(() => readGovernanceScan({ ...scan, completedAtUtc: null }));
const reviewRequest = { assetKey: dashboard.assets[0].assetKey, assetVersion: dashboard.assets[0].assetVersion };
const reviewReceipt = await api.reviewAsset(reviewRequest);
confirmGovernanceReview(reviewReceipt, reviewRequest);
assert.throws(() => confirmGovernanceReview({ ...reviewReceipt, assetVersion: 'another-version' }, reviewRequest));
assert.throws(() => confirmGovernanceReview({ ...reviewReceipt, reviewedAtUtc: null }, reviewRequest));
const badgeRequest = { ...reviewRequest, badge: 'Certified' };
const badgeReceipt = await api.assignBadge(badgeRequest);
confirmGovernanceBadge(badgeReceipt, badgeRequest);
confirmGovernanceBadge({ ...badgeReceipt, assignedAtUtc: '2026-10-08T10:00:00Z' }, badgeRequest);
assert.throws(() => confirmGovernanceBadge({ ...badgeReceipt, assignedAtUtc: 'not-a-date' }, badgeRequest));
assert.throws(() => confirmGovernanceBadge({ ...badgeReceipt, badge: 'Trusted' }, badgeRequest));
for (const decision of ['ignore', 'accept-risk', 'reopen']) {
  const decisionApi = createGovernanceMock();
  const findingRequest = { decision, reason: 'Behavior evidence.', categoryValue: null, assetVersion: decision === 'reopen' ? '' : reviewRequest.assetVersion };
  const findingReceipt = await decisionApi.decideFinding(1, findingRequest);
  confirmGovernanceFinding(findingReceipt, 1, findingRequest, []);
  assert.throws(() => confirmGovernanceFinding(findingReceipt, 2, findingRequest, []));
  assert.throws(() => confirmGovernanceFinding(findingReceipt, 1, findingRequest, findingReceipt.decisions.map(item => item.id)));
  assert.throws(() => confirmGovernanceFinding({ ...findingReceipt, decisions: [] }, 1, findingRequest, []));
}
const { version: observedVersion, updatedAtUtc: _updatedAt, ...settingsRequest } = settings;
const settingsReceipt = await api.saveSettings(settingsRequest);
confirmGovernanceSettings(settingsReceipt, settingsRequest, observedVersion);
assert.throws(() => confirmGovernanceSettings(settingsReceipt, settingsRequest, settingsReceipt.version));
assert.throws(() => confirmGovernanceSettings({ ...settingsReceipt, targetScore: 99 }, settingsRequest, observedVersion));
const { id: _categoryId, ...categoryRequest } = { ...categories[0], disabled: true };
const categoryReceipt = await api.saveCategory(categoryRequest);
confirmGovernanceCategory(categoryReceipt, categoryRequest);
assert.throws(() => confirmGovernanceCategory({ ...categoryReceipt, disabled: false }, categoryRequest));
const { id: _termId, updatedAtUtc: _termUpdated, ...termRequest } = { ...glossary[0], disabled: true };
const termReceipt = await api.saveGlossaryTerm(termRequest);
confirmGovernanceGlossary(termReceipt, termRequest);
assert.throws(() => confirmGovernanceGlossary({ ...termReceipt, steward: null }, termRequest));
assert.throws(() => confirmGovernanceGlossary({ ...termReceipt, disabled: false }, termRequest));
confirmGovernanceNoContent(null);
for (const receipt of [undefined, {}, { success: true }]) assert.throws(() => confirmGovernanceNoContent(receipt));

const calls = [];
let reply = () => Response.json([]);
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/index.html' },
  fetch: async (url, options) => { calls.push({ url: String(url), options }); return reply(); } };
const { governanceApi, dataQualityApi } = await import('../../src/ETL-SQL.Portal/wwwroot/js/api.js');
const controller = new AbortController();
await governanceApi.dashboard({}, controller.signal);
await governanceApi.findings({}, controller.signal);
await governanceApi.categories(controller.signal);
await governanceApi.glossary(controller.signal);
await governanceApi.settings(controller.signal);
await dataQualityApi.qualityRulesAll(controller.signal);
assert.equal(calls.length, 6);
assert.ok(calls.every(call => call.options.signal === controller.signal));
const writes = [
  [() => governanceApi.scan(), null],
  [() => governanceApi.saveSettings(settingsRequest), settingsRequest],
  [() => governanceApi.saveCategory(categoryRequest), categoryRequest],
  [() => governanceApi.saveGlossaryTerm(termRequest), termRequest],
  [() => governanceApi.reviewAsset(reviewRequest), reviewRequest],
  [() => governanceApi.assignBadge(badgeRequest), badgeRequest],
  [() => governanceApi.decideFinding(1, { decision: 'ignore', reason: 'Evidence', categoryValue: null, assetVersion: 'v1' }), { decision: 'ignore', reason: 'Evidence', categoryValue: null, assetVersion: 'v1' }],
];
for (const [write, body] of writes) {
  reply = () => Response.json({});
  await write();
  if (body) {
    assert.equal(calls.at(-1).options.headers.get('Content-Type'), 'application/json');
    assert.deepEqual(JSON.parse(calls.at(-1).options.body), body);
  }
  for (const status of [201, 202, 204]) {
    reply = () => status === 204 ? new Response(null, { status }) : Response.json({}, { status });
    await assert.rejects(write, /unconfirmed/);
  }
}
for (const write of [() => governanceApi.disableCategory('space / category'), () => governanceApi.deleteGlossaryTerm('space / term'),
  () => governanceApi.removeBadge({ assetKey: 'space / asset', badge: 'Trusted' })]) {
  reply = () => new Response(null, { status: 204 });
  assert.equal(await write(), null);
  reply = () => Response.json({});
  await assert.rejects(write, /unconfirmed/);
}
reply = () => Response.json({ error: 'Forbidden' }, { status: 403 });
await assert.rejects(() => governanceApi.scan(), error => error.status === 403);
console.log('governance response and transport contracts passed');
