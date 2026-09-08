import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [queue, api, story, css] = await Promise.all([
  read('src/ETL-SQL.Portal/wwwroot/js/data-quality-queue.js'),
  read('src/ETL-SQL.Portal/wwwroot/js/api.js'),
  read('tools/ui-sandbox/stories/data-quality-queue.story.js'),
  read('src/ETL-SQL.Portal/wwwroot/css/portal.css'),
]);

// This asserted `/api/jobs/{id}` until that turned out to be the defect. Data-quality submissions
// are IJobChannel jobs and were never in the report-execution namespace, so polling it answered 404
// forever, which the client read as a transient outage and retried every second for the life of the
// tab. The endpoint is asserted whole, and the old one by its absence, so the fix cannot be undone
// by a plausible-looking edit.
const flatApi = api.split(/\s+/).join(' ');
assert.ok(
  flatApi.includes("jobStatus: (jobId) => contracted( 'dataQualitySubmissionStatus', apiJson(`/api/data-quality/jobs/${encodeURIComponent(jobId)}`))"),
  'dataQualityApi.jobStatus no longer polls the data-quality job namespace.');

// The absence is checked inside dataQualityApi only, because `/api/jobs/` is the right namespace
// for report executions and pollJob elsewhere in this file uses it correctly.
const dataQualityBlock = flatApi.slice(flatApi.indexOf('export const dataQualityApi'));
assert.ok(dataQualityBlock.startsWith('export const dataQualityApi'), 'dataQualityApi is no longer exported from api.js.');
assert.ok(
  !dataQualityBlock.slice(0, dataQualityBlock.indexOf('export const', 1)).includes('apiJson(`/api/jobs/'),
  'dataQualityApi is back on the report-execution namespace, which never holds data-quality job ids.');
assert.match(api, /qualityRules: \(jobName\).*\/api\/data-quality\/rules/);
assert.match(queue, /TERMINAL_JOB_STATUSES/);
assert.match(queue, /sessionStorage/);
assert.match(queue, /trackJob\(result\.jobId, 'Replay'/);
assert.match(queue, /trackJob\(result\.jobId, 'Disposition'/);
assert.match(queue, /dataQualityApi\.jobStatus\(jobId\)/);
assert.match(queue, /dataQualityApi\.qualityRules\(jobName\)/);
assert.match(queue, /Rules protecting columns/);
assert.match(queue, /pollTimers\.forEach\(timer => clearTimeout\(timer\)\)/);
assert.match(story, /id: 'job-status'/);
assert.match(story, /status: 'Completed'/);
assert.match(css, /\.dq-job-row/);

console.log('Data-quality replay/disposition terminal job tracking contract passed.');
