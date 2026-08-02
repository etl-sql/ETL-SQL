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

assert.match(api, /jobStatus: \(jobId\).*\/api\/jobs\//);
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
