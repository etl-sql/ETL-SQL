import assert from 'node:assert/strict';
import { workflowTimeoutFindings } from '../check-workflow-timeouts.mjs';

const workflow = job => `name: Fixture\non: push\njobs:\n${job}\n`;
const physical = value => workflow(`  build:\n    runs-on: ubuntu-latest\n${value}\n    steps:\n      - run: echo ready\n        timeout-minutes: 1`);
for (const limit of [1, 15, 45, 90, 165, 180]) {
  for (const newline of ['\n', '\r\n']) assert.deepEqual(workflowTimeoutFindings(physical(`    timeout-minutes: ${limit} # bounded`).replaceAll('\n', newline)), []);
}
for (const value of ['', '    timeout-minutes: 0', '    timeout-minutes: -1', '    timeout-minutes: 360',
  '    timeout-minutes: "45"', '    timeout-minutes: 1.5', '    timeout-minutes: ${{ inputs.deadline }}',
  '    timeout-minutes: 30\n    timeout-minutes: 45', '    steps:\n      - timeout-minutes: 1']) {
  assert.ok(workflowTimeoutFindings(physical(value)).some(issue => issue.includes('timeout-minutes')), value);
}
assert.deepEqual(workflowTimeoutFindings(workflow('  certify:\n    uses: ./.github/workflows/ci.yml\n    with:\n      candidate_sha: ${{ github.sha }}')), []);
for (const source of ['name: NoJobs', 'jobs: { build: { runs-on: ubuntu-latest } }',
  workflow('    build:\n      runs-on: ubuntu-latest\n      timeout-minutes: 30'),
  workflow('  "build":\n    runs-on: ubuntu-latest\n    timeout-minutes: 30'),
  workflow('  build: { runs-on: ubuntu-latest, timeout-minutes: 30 }'),
  workflow('  build:\n    <<: *defaults')]) assert.ok(workflowTimeoutFindings(source).length, source);
assert.deepEqual(workflowTimeoutFindings(workflow('  first:\n    runs-on: ubuntu-latest\n    timeout-minutes: 30\n    steps:\n      - run: echo ready\n        timeout-minutes: 1\n  next:\n    uses: ./.github/workflows/ci.yml')), []);
assert.ok(workflowTimeoutFindings(workflow('  first:\n    runs-on: ubuntu-latest\n    timeout-minutes: 30\n  next:\n    runs-on: windows-latest')).some(issue => issue.includes('/next:')));
for (const indent of [4, 6]) {
  const prefix = ' '.repeat(indent), property = ' '.repeat(indent + 2);
  const bounded = limit => workflow(`  build:\n    runs-on: ubuntu-latest\n    timeout-minutes: 30\n    steps:\n${prefix}- name: Command\n${property}run: echo ready\n${limit}`);
  assert.deepEqual(workflowTimeoutFindings(bounded(`${property}timeout-minutes: 5`)), []);
  for (const limit of ['', `${property}timeout-minutes: 31`, `${property}timeout-minutes: 0`,
    `${property}timeout-minutes: 5\n${property}timeout-minutes: 5`]) assert.ok(workflowTimeoutFindings(bounded(limit)).some(issue => issue.includes('/step-')));
  assert.ok(workflowTimeoutFindings(bounded('').replace('run: echo ready', 'uses: actions/checkout@v6')).some(issue => issue.includes('/step-')));
  assert.deepEqual(workflowTimeoutFindings(bounded('').replace('run: echo ready', 'uses: actions/setup-node@v7')), []);
}
console.log('Workflow deadlines: missing, invalid, oversized, duplicate and unsupported layouts reject; reusable calls inherit bounded runner jobs.');
