import assert from 'node:assert/strict';
import { activityQuery, createActivityLoader, loadScheduledJobCatalog, readScheduledJobActivity } from '../src/ETL-SQL.Portal/wwwroot/js/orchestrator-activity.js';
import { renderScheduledJobsTable } from '../src/ETL-SQL.Portal/wwwroot/js/orchestrator-job-table.js';

const request = { filter: 'all', status: 'all', search: '', limit: 25, offset: 0 };
function job(index = 0) {
    return { id: `job-${index}`, name: `Job ${index}`, script: 'SELECT 1;', interval: 0, unit: 'HOUR',
        isEnabled: true, maxRetries: 0, retryDelaySeconds: 30, hashPolicy: 'Warn', version: 1, jobType: 0 };
}
function page(items = [job()], options = {}) {
    return { asOfUtc: '2026-10-07T12:00:00Z', dayStartUtc: '2026-10-07T00:00:00Z',
        counts: { active: 1, queued: 0, completedToday: 1, failedToday: 1 }, total: items.length, offset: 0, limit: 25,
        items: items.map(job => ({ job, runState: 'Running', completedToday: true, failedToday: true })), ...options };
}
assert.equal(readScheduledJobActivity(page()).items[0].job.id, 'job-0');
for (const invalid of [
    null, [], page([], { counts: { active: -1 } }), page([], { dayStartUtc: '2026-10-07T01:00:00Z' }),
    page([], { total: 1 }), page([job(), job()]), page([], { limit: 0 }), page([], { offset: -1 }),
    page([{ ...job(), isEnabled: 'true' }]), page([{ ...job(), id: null }]),
    { ...page(), items: [{ ...page().items[0], runState: { toString: () => 'Running' } }] }
]) assert.throws(() => readScheduledJobActivity(invalid), /Invalid scheduled-job/);
const query = new URLSearchParams(activityQuery({ ...request, filter: 'failedToday', search: 'warehouse + & café' }));
assert.equal(query.get('search'), 'warehouse + & café');
assert.equal(query.get('filter'), 'failedToday');

const pending = [];
const loader = createActivityLoader((request, signal) => new Promise((resolve, reject) => pending.push({ request, signal, resolve, reject })));
const first = loader.load(request);
const second = loader.load({ ...request, filter: 'active' });
assert.equal(pending[0].signal.aborted, true);
pending[1].resolve(page([job(2)]));
assert.equal((await second).items[0].job.id, 'job-2');
pending[0].reject(new Error('Old denied request'));
assert.equal(await first, null);
const disposed = loader.load(request);
loader.dispose();
assert.equal(pending[2].signal.aborted, true);
pending[2].resolve(page());
assert.equal(await disposed, null);
assert.equal(await loader.load(request), null);
const mismatched = createActivityLoader(async () => page([], { limit: 100 }));
await assert.rejects(() => mismatched.load(request), /pagination does not match/);
const denied = createActivityLoader(async () => { throw new Error('Access denied'); });
await assert.rejects(() => denied.load(request), /Access denied/);

const catalogRequests = [];
const catalog = await loadScheduledJobCatalog(async request => {
    catalogRequests.push(request);
    const size = Math.min(1000, 1005 - request.offset);
    return page(Array.from({ length: size }, (_, index) => job(request.offset + index)),
        { total: 1005, limit: 1000, offset: request.offset });
}, new AbortController().signal);
assert.equal(catalog.length, 1005);
assert.deepEqual(catalogRequests.map(q => q.offset), [0, 1000]);
await assert.rejects(() => loadScheduledJobCatalog(async request =>
    page(Array.from({ length: request.offset ? 4 : 1000 }, (_, index) => job(request.offset + index)),
        { total: request.offset ? 1004 : 1005, limit: 1000, offset: request.offset }), new AbortController().signal), /catalog changed/);
const aborted = new AbortController();
aborted.abort();
await assert.rejects(() => loadScheduledJobCatalog(async () => page(), aborted.signal), { name: 'AbortError' });

const html = renderScheduledJobsTable([{ ...job(), name: '<img src=x onerror=alert(1)>', createdBy: 'user:<script>',
    targetPath: 'bundle://<&>', options: '{"SandboxProfile":"<script>"}' }], 'job-0');
assert.ok(!html.includes('<img'));
assert.ok(!html.includes('<script>'));
assert.match(html, /&lt;img/);
assert.match(html, /job-row selected/);
assert.match(html, /📦 Bundle/);
assert.match(html, /data-action="trigger"/);
assert.match(renderScheduledJobsTable([]), /No jobs match/);
console.log('Scheduled-job activity validation, request lifetime, catalog pagination, and table rendering checks passed.');
