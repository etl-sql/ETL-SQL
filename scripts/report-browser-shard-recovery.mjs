import { appendFileSync, mkdirSync, writeFileSync } from 'node:fs';

const { GITHUB_REPOSITORY: repository, GITHUB_RUN_ID: runId, GITHUB_RUN_ATTEMPT: attempt,
  SOURCE_SHA: sha, BROWSER_SHARD: shard, GITHUB_STEP_SUMMARY: summary } = process.env;
if (process.argv.length !== 2 || !/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') ||
    !/^[1-9]\d*$/.test(runId ?? '') || !/^[1-9]\d*$/.test(attempt ?? '') ||
    !/^[a-f0-9]{40}$/.test(sha ?? '') || !/^[123]$/.test(shard ?? '')) {
  throw new Error('Browser recovery reporting requires valid workflow, candidate and shard identity.');
}
const report = [
  `### Browser shard ${shard}/3 failed`, '',
  `Candidate: \`${sha}\`. Run: ${runId}, attempt ${attempt}.`,
  `Diagnostic artifact: \`browser-shard-${shard}-${runId}-${attempt}\`.`, '',
  'Inspect the failure and retained diagnostics. After the run finishes, rerun failed jobs',
  'in the same run to preserve successful shards and their evidence:', '',
  '```sh',
  `gh run view ${runId} --repo ${repository} --log-failed`,
  `gh run rerun ${runId} --repo ${repository} --failed`,
  '```', '',
  'Diagnose the failure before retrying; repeated retries do not establish test stability.',
  'A source fix requires a new candidate and fresh validation. Partial diagnostic runs do not',
  'satisfy the full browser coverage or release certification gates.', '',
].join('\n');
mkdirSync('artifacts/browser-shard', { recursive: true });
writeFileSync('artifacts/browser-shard/recovery.md', `${report}\n`);
console.log(report);
if (summary) appendFileSync(summary, `${report}\n`);
