// Always-running CI merge gate (RG01): fails unless every lane selected for the change passed.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const manifest = JSON.parse(readFileSync(new URL('./ci-impact.json', import.meta.url), 'utf8'));

export const GATE_JOBS = [
  'build-and-test',
  'browser-consumer-contracts',
  'build-vscode',
  'sample-scripts',
  'portal-browser-journey',
  'portal-browser-gate',
  'studio-performance-budgets',
  'enterprise-certification',
  'docker-integration-tests',
  'engine-certification',
  'spill-certification',
];

const DIRECT_LANES = [
  'build-and-test',
  'browser-consumer-contracts',
  'build-vscode',
  'sample-scripts',
  'studio-performance-budgets',
];

const PUSH_ONLY_LANES = new Set([
  'enterprise-certification',
  'docker-integration-tests',
]);

function parseJson(raw, label) {
  if (!raw || !raw.trim()) throw new Error(`Missing ${label}.`);
  try {
    return JSON.parse(raw);
  } catch (error) {
    throw new Error(`Invalid JSON for ${label}: ${error.message}`);
  }
}

export function evaluateMergeGate({
  eventName,
  preflightResult,
  selectedLanesJson,
  browserShardsJson,
  laneResultsJson,
}) {
  if (!eventName) throw new Error('Missing GitHub event name.');
  if (preflightResult !== 'success') {
    throw new Error(`Source preflight did not pass (result=${preflightResult || 'missing'}).`);
  }

  const selectedLanes = parseJson(selectedLanesJson, 'selected lanes');
  if (!Array.isArray(selectedLanes) || !selectedLanes.includes('source-preflight') ||
      selectedLanes.some(lane => !manifest.lanes.includes(lane))) {
    throw new Error(`Invalid selected lanes: ${JSON.stringify(selectedLanes)}`);
  }

  const browserShards = parseJson(browserShardsJson, 'browser shards');
  if (!Array.isArray(browserShards) || browserShards.length === 0 ||
      browserShards.some(shard => ![1, 2, 3].includes(shard))) {
    throw new Error(`Invalid browser shards: ${JSON.stringify(browserShards)}`);
  }

  const results = parseJson(laneResultsJson, 'lane results');
  if (!results || typeof results !== 'object' || Array.isArray(results)) {
    throw new Error('Lane results must be a JSON object.');
  }
  for (const job of GATE_JOBS) {
    if (typeof results[job] !== 'string' || !results[job]) {
      throw new Error(`Missing job result for '${job}'.`);
    }
  }

  const selected = new Set(selectedLanes);
  const isPullRequest = eventName === 'pull_request';
  const failures = [];
  const expectResult = (job, expected, reason) => {
    if (results[job] !== expected) {
      failures.push(`${job}: expected '${expected}' (${reason}), got '${results[job]}'`);
    }
  };

  for (const lane of DIRECT_LANES) {
    expectResult(lane, selected.has(lane) ? 'success' : 'skipped',
      selected.has(lane) ? 'lane selected' : 'lane not selected');
  }

  for (const lane of PUSH_ONLY_LANES) {
    const shouldRun = selected.has(lane) && !isPullRequest;
    expectResult(lane, shouldRun ? 'success' : 'skipped',
      shouldRun ? `lane selected on ${eventName}` : (selected.has(lane) ? 'excluded on pull_request' : 'lane not selected'));
  }

  if (selected.has('engine-certification')) {
    expectResult('engine-certification', 'success', 'lane selected');
    expectResult('spill-certification', isPullRequest ? 'skipped' : 'success',
      isPullRequest ? 'excluded on pull_request' : `lane selected on ${eventName}`);
  } else {
    expectResult('engine-certification', 'skipped', 'lane not selected');
    expectResult('spill-certification', 'skipped', 'lane not selected');
  }

  const allBrowserShards = browserShards.length === 3 &&
    browserShards[0] === 1 && browserShards[1] === 2 && browserShards[2] === 3;
  if (selected.has('portal-browser-journey')) {
    if (!allBrowserShards && eventName !== 'workflow_dispatch') {
      failures.push(`portal-browser-journey: partial browser shards ${JSON.stringify(browserShards)} are forbidden on ${eventName}`);
    } else {
      expectResult('portal-browser-journey', 'success', 'lane selected');
      expectResult('portal-browser-gate', allBrowserShards ? 'success' : 'skipped',
        allBrowserShards ? 'all browser shards selected' : 'diagnostic shard run');
    }
  } else {
    expectResult('portal-browser-journey', 'skipped', 'lane not selected');
    expectResult('portal-browser-gate', 'skipped', 'lane not selected');
  }

  if (failures.length > 0) {
    throw new Error(`Merge gate blocked:\n- ${failures.join('\n- ')}`);
  }

  return {
    eventName,
    selectedLanes,
    verifiedJobs: GATE_JOBS.filter(job => results[job] === 'success'),
    skippedJobs: GATE_JOBS.filter(job => results[job] === 'skipped'),
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const outcome = evaluateMergeGate({
    eventName: process.env.CI_EVENT,
    preflightResult: process.env.PREFLIGHT_RESULT,
    selectedLanesJson: process.env.SELECTED_LANES,
    browserShardsJson: process.env.BROWSER_SHARDS,
    laneResultsJson: process.env.LANE_RESULTS,
  });
  console.log(`Merge gate passed for ${outcome.eventName}: ${outcome.verifiedJobs.length} passed (${outcome.verifiedJobs.join(', ') || 'preflight only'}), ${outcome.skippedJobs.length} skipped.`);
}
