import { appendFileSync } from 'node:fs';

const { GITHUB_REPOSITORY: repository, GITHUB_RUN_ID: runId, GITHUB_RUN_ATTEMPT: attempt,
  SOURCE_SHA: sha, PACKAGE_PLATFORM: platform, PACKAGE_ARTIFACT_IDS: artifactIds = '', GITHUB_STEP_SUMMARY: summary } = process.env;
if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || !/^[1-9]\d*$/.test(runId ?? '') ||
    !/^[1-9]\d*$/.test(attempt ?? '') || !/^[a-f0-9]{40}$/.test(sha ?? '') || !['Windows', 'macOS'].includes(platform)) {
  throw new Error('Package recovery reporting requires valid workflow and candidate identity.');
}
const ids = artifactIds.split(',').map(id => /^[1-9]\d*$/.test(id) ? id : '(unavailable)');
const report = [
  `### ${platform} package upload failed`, '',
  `Candidate: \`${sha}\`. Run: ${runId}, attempt ${attempt}.`,
  `Retained package artifact IDs: ${ids.join(', ')}.`, '',
  'Inspect the failed jobs, then rerun failed jobs in this same run to preserve successful builds:', '',
  '```sh',
  `gh run view ${runId} --repo ${repository} --log-failed`,
  `gh run rerun ${runId} --repo ${repository} --failed`,
  '```', '',
  'Wait for this run to finish before rerunning it. Fix the upload issue first; changed or missing',
  'packages must pass artifact and hash verification before upload. A source fix requires a new candidate.', '',
].join('\n');
console.log(report);
if (summary) appendFileSync(summary, `${report}\n`);
