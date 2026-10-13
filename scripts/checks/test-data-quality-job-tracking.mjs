import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [view, story, css] = await Promise.all([
  read('src/ETL-SQL.Portal/wwwroot/js/data-quality-view.js'),
  read('tools/ui-sandbox/stories/data-quality-queue.story.js'),
  read('src/ETL-SQL.Portal/wwwroot/css/portal.css'),
]);

// Data-quality submissions and report executions have different job namespaces. Exercise the
// actual client so TypeScript's emitted formatting does not decide whether this check passes.
const calls = [];
let payload = { jobId: 'job/fixture', kind: 'Replay', quarantineTarget: 'fixture', status: 'Completed', isTerminal: true };
let responseStatus = 200;
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/index.html' },
  fetch: async url => { calls.push(String(url)); return Response.json(payload, { status: responseStatus }); } };
const { dataQualityApi } = await import('../../src/ETL-SQL.Portal/wwwroot/js/api.js');
assert.deepEqual(await dataQualityApi.jobStatus('job/fixture'), payload);
assert.equal(calls.pop(), '/api/data-quality/jobs/job%2Ffixture');
payload = { ...payload, isTerminal: 'true' };
await assert.rejects(dataQualityApi.jobStatus('job/fixture'), /dataQualitySubmissionStatus/);
await dataQualityApi.qualityRules('job/fixture');
assert.equal(calls.pop(), '/api/data-quality/rules?jobName=job%2Ffixture');
// A 200 body does not acknowledge the channel's accepted-submission contract.
await assert.rejects(dataQualityApi.replayQuarantine('fixture', 'job/fixture'), /accepted acknowledgement/);
responseStatus = 202;
payload = { jobId: 'job/fixture', replayStatement: 'REPLAY QUARANTINE fixture;' };
assert.deepEqual(await dataQualityApi.replayQuarantine('fixture', 'job/fixture'), payload);
assert.equal(calls.pop(), '/api/data-quality/quarantine/replay');
assert.match(view, /Rules protecting columns/);
assert.match(story, /id: 'job-status'/);
assert.match(story, /status: 'Completed'/);
assert.match(css, /\.dq-job-row/);

console.log('Data-quality replay/disposition terminal job tracking contract passed.');
