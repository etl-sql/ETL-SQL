// Known failures of release-candidate jobs that are NOT defects in the candidate, so the candidate
// supervisor reruns the failed jobs on unchanged inputs instead of stopping the release.
// Anything that matches none of these is treated as a real defect.
//
// kind:
//   infra      GitHub or runner trouble. Rerun up to twice (run attempts 1 and 2).
//   flaky      A known timing-sensitive test. Rerun once; a second failure is real.
//   dependent  Fails because another workflow's job failed. Rerun once that job has succeeded.
// source: 'log' (the job log) or 'annotation' (check-run annotations; jobs that never started
// have no log).
// Add an entry only with the run that proved it, so the list stays reviewable.
export const candidateFailureSignatures = [
  {
    id: 'checkout-timeout', kind: 'infra', source: 'log',
    pattern: /The action 'Run actions\/checkout@v\d+' has timed out after \d+ minutes/,
    evidence: 'MSI 41710848250 attempts 1-2, 2026-11-03, during a GitHub Git Operations degradation',
  },
  {
    id: 'runner-not-acquired', kind: 'infra', source: 'annotation',
    pattern: /The job was not acquired by Runner of type hosted even after multiple attempts/,
    evidence: 'Release Certification 41676286923 and 41710847926 (macOS arm64 capacity), 2026-11-03',
  },
  {
    id: 'runner-lost', kind: 'infra', source: 'annotation',
    pattern: /The runner has received a shutdown signal|lost communication with the server|The operation was canceled because the runner/,
    evidence: 'v0.21.0 MSI rounds lost their hosted runner (docs/releases/recovery-acceptance.md)',
  },
  {
    id: 'hdiutil-busy', kind: 'infra', source: 'log',
    pattern: /hdiutil: (?:create|attach|detach) failed - Resource busy/,
    evidence: 'Native Package Certification 41676292135 osx-x64, 2026-11-03',
  },
  {
    id: 'windows-payload-dependency', kind: 'dependent', source: 'log',
    pattern: /Shared Windows payload selection failed|Windows payload publication failed/,
    waitFor: { workflow: 'MSI In-Place Upgrade', job: 'Publish Windows payload' },
    evidence: 'Native Package Certification 41710853145 win-x64 after MSI payload checkout timeout, 2026-11-03',
  },
  {
    id: 'studio-performance-budget', kind: 'flaky', source: 'log',
    pattern: /Studio startup measured [\d.]+ ms; budget is [\d.]+ ms/,
    evidence: 'Release Certification 41710847926 Portal Browser Journey 1/3, 2026-11-03',
  },
  {
    id: 'playwright-wait', kind: 'flaky', source: 'log',
    pattern: /Microsoft\.Playwright\.PlaywrightException : (?:Locator expected|Timeout \d+ms exceeded)/,
    evidence: 'Release Certification 41710847926 Portal Browser Journey 2/3, 2026-11-03',
  },
  {
    id: 'first-run-sign-in-wait', kind: 'flaky', source: 'log',
    pattern: /waiting for Locator\("#changeForm"\) to be visible/,
    evidence: 'CI 41744076759 Portal Browser Journey 3/3, 2026-11-04 (same shard passed on 2026-11-03 rounds 4 and 5)',
  },
  {
    id: 'preflight-check-watchdog', kind: 'flaky', source: 'log',
    pattern: /FAIL [\w-]+ \(60\.0s\)/,
    evidence: 'CI 41676267442 source preflight test-workflows hit its 60s watchdog, 2026-11-03',
  },
];

const limits = { infra: 3, flaky: 2, dependent: 3 };

// Returns the first matching signature for a failed job, or null for a real defect.
export function classifyFailure({ log = '', annotations = [] } = {}) {
  const annotationText = annotations.join('\n');
  return candidateFailureSignatures.find(signature =>
    signature.pattern.test(signature.source === 'annotation' ? annotationText : log)) ?? null;
}

// The highest run attempt at which a failure of this kind may still be rerun.
export function maxAttemptFor(signature) { return limits[signature.kind]; }
