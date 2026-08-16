// Unit tests for the Orchestrator Admin UI rendering helper components
// (src/ETL-SQL.Portal/wwwroot/js/orchestrator-admin-ui.js).

import path from 'node:path';
import { pathToFileURL } from 'node:url';

const mod = await import(
    pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js/orchestrator-admin-ui.js')).href);

const {
    escHtml, fmtDt, fmtTimeAgo,
    filterAndPaginateJobs,
    renderSchedulesTable,
    renderNotificationsTable,
    renderWatermarksTable,
    renderJobAuditTrail,
    renderCalendarTimeline
} = mod;

function assert(cond, msg) { if (!cond) throw new Error('FAIL: ' + msg); }

// ── 1. escHtml tests ──────────────────────────────────────────────────────────
assert(escHtml(null) === '', 'null escapes to empty string');
assert(escHtml(undefined) === '', 'undefined escapes to empty string');
assert(escHtml('hello & <world> "test" \'quote\'') === 'hello &amp; &lt;world&gt; &quot;test&quot; &#39;quote&#39;',
    'special HTML characters are escaped');

// ── 2. Date formatting tests ──────────────────────────────────────────────────
assert(fmtDt(null) === '—', 'null date returns dash');
assert(fmtDt('invalid-date') === '—', 'invalid date returns dash');
const nowIso = new Date().toISOString();
assert(fmtDt(nowIso) !== '—', 'valid date formats to string');
assert(fmtTimeAgo(null) === '—', 'null timeAgo returns dash');
assert(typeof fmtTimeAgo(nowIso) === 'string', 'valid timeAgo returns relative string');

// ── 3. Filter and Paginate Jobs tests ─────────────────────────────────────────
const sampleJobs = [
    { Name: 'daily_sales', DisplayName: 'Daily Sales Load', TargetPath: 'sales.etlsql', IsEnabled: true, CreatedBy: 'user:alice', Interval: 1, Unit: 'DAY' },
    { Name: 'hourly_orders', DisplayName: 'Orders Sync', TargetPath: 'orders.etlsql', IsEnabled: true, CreatedBy: 'user:bob', Interval: 1, Unit: 'HOUR' },
    { Name: 'weekly_audit', DisplayName: 'Security Audit', TargetPath: 'audit.etlsql', IsEnabled: false, CreatedBy: 'user:charlie', Interval: 1, Unit: 'WEEK' },
    { Name: 'inventory_snapshot', DisplayName: 'Inventory ETL', TargetPath: 'bundle://inventory/v1', IsEnabled: true, CreatedBy: 'user:alice', Interval: 6, Unit: 'HOUR' },
];

// Search filter
const searchResult = filterAndPaginateJobs(sampleJobs, { search: 'orders', status: 'all', page: 1, pageSize: 10 });
assert(searchResult.total === 1, 'search matches single job');
assert(searchResult.items[0].Name === 'hourly_orders', 'matched expected job');

// Status filter
const enabledResult = filterAndPaginateJobs(sampleJobs, { search: '', status: 'enabled', page: 1, pageSize: 10 });
assert(enabledResult.total === 3, 'enabled filter returned 3 active jobs');
assert(enabledResult.items.every(j => j.IsEnabled === true), 'all returned jobs are enabled');

const disabledResult = filterAndPaginateJobs(sampleJobs, { search: '', status: 'disabled', page: 1, pageSize: 10 });
assert(disabledResult.total === 1, 'disabled filter returned 1 disabled job');
assert(disabledResult.items[0].Name === 'weekly_audit', 'matched disabled job');

// Pagination slice
const page1 = filterAndPaginateJobs(sampleJobs, { search: '', status: 'all', page: 1, pageSize: 2 });
assert(page1.items.length === 2, 'page 1 has 2 items');
assert(page1.totalPages === 2, 'total pages is 2');
assert(page1.startIdx === 1 && page1.endIdx === 2, 'indices match bounds');

const page2 = filterAndPaginateJobs(sampleJobs, { search: '', status: 'all', page: 2, pageSize: 2 });
assert(page2.items.length === 2, 'page 2 has 2 items');
assert(page2.page === 2, 'current page is 2');
assert(page2.startIdx === 3 && page2.endIdx === 4, 'indices match page 2 bounds');

// ── 4. Schedules Table Rendering tests ────────────────────────────────────────
const emptySchedulesHtml = renderSchedulesTable([]);
assert(emptySchedulesHtml.includes('empty-state'), 'empty schedules list renders empty state');

const schedules = [
    { Name: 'nightly_utc', DisplayName: 'Nightly at Midnight', Cron: '0 0 * * *', TimeZone: 'UTC', IsEnabled: true, CreatedBy: 'user:admin', Version: 1 },
    { Name: 'hourly_us', DisplayName: 'Hourly US', Cron: '0 * * * *', TimeZone: 'America/New_York', IsEnabled: false, CreatedBy: 'user:ops', Version: 2 }
];
const schedulesHtml = renderSchedulesTable(schedules, { nightly_utc: 3, hourly_us: 0 });
assert(schedulesHtml.includes('Nightly at Midnight'), 'renders schedule display name');
assert(schedulesHtml.includes('0 0 * * *'), 'renders cron expression');
assert(schedulesHtml.includes('America/New_York'), 'renders timezone');
assert(schedulesHtml.includes('3 jobs'), 'renders linked jobs count');
assert(schedulesHtml.includes('data-action="edit-schedule"'), 'renders edit action');
assert(schedulesHtml.includes('data-action="delete-schedule"'), 'renders delete action');

// ── 5. Notifications Table Rendering tests ────────────────────────────────────
const emptyNotifsHtml = renderNotificationsTable([]);
assert(emptyNotifsHtml.includes('empty-state'), 'empty notifications list renders empty state');

const notifications = [
    { Name: 'slack_ops', DisplayName: 'Ops Slack Channel', ConnectionName: 'slack_webhook', Recipient: '#ops-data', IsEnabled: true, CreatedBy: 'user:admin' }
];
const notifsHtml = renderNotificationsTable(notifications, { slack_ops: 2 });
assert(notifsHtml.includes('Ops Slack Channel'), 'renders notification display name');
assert(notifsHtml.includes('slack_webhook'), 'renders connection name');
assert(notifsHtml.includes('#ops-data'), 'renders recipient');
assert(notifsHtml.includes('data-action="dispatch-notification"'), 'renders test dispatch action button');

// ── 6. Watermarks Table Rendering tests ───────────────────────────────────────
const emptyWatermarksHtml = renderWatermarksTable([], 'daily_sales');
assert(emptyWatermarksHtml.includes('empty-state'), 'empty watermark state renders empty state');
assert(emptyWatermarksHtml.includes('id="addStateBtn"'), 'empty watermark state has add key button');

const watermarks = [
    { StateKey: 'last_order_id', StateValue: '100520', UpdatedAt: '2026-08-16T12:00:00Z' },
    { StateKey: 'max_timestamp', StateValue: '2026-08-16T10:00:00Z', UpdatedAt: '2026-08-16T12:00:00Z' }
];
const watermarksHtml = renderWatermarksTable(watermarks, 'daily_sales');
assert(watermarksHtml.includes('last_order_id'), 'renders state key');
assert(watermarksHtml.includes('100520'), 'renders state value');
assert(watermarksHtml.includes('data-state-action="reset"'), 'renders reset button');
assert(watermarksHtml.includes('data-state-action="edit"'), 'renders edit button');

// ── 7. Change Log (Audit) Rendering tests ─────────────────────────────────────
const emptyAuditHtml = renderJobAuditTrail([]);
assert(emptyAuditHtml.includes('empty-state'), 'empty audit trail renders empty state');

const auditEntries = [
    { Action: 'JobScriptEdited', ActorId: 'user:admin', Timestamp: '2026-08-16T14:00:00Z', Detail: 'Script text updated' },
    { Action: 'ScheduleAttached', ActorId: 'user:ops', Timestamp: '2026-08-16T15:00:00Z', Detail: 'Attached nightly_utc' }
];
const auditHtml = renderJobAuditTrail(auditEntries);
assert(auditHtml.includes('JobScriptEdited'), 'renders action badge');
assert(auditHtml.includes('user:admin'), 'renders actor');
assert(auditHtml.includes('Attached nightly_utc'), 'renders detail text');

// ── 8. Calendar Timeline Rendering tests ──────────────────────────────────────
const calendarHtml = renderCalendarTimeline(sampleJobs, 7);
assert(calendarHtml.includes('orch-calendar-grid'), 'renders calendar grid container');
assert(calendarHtml.includes('Today'), 'renders Today badge on current day');

console.log('ALL orchestrator-admin-ui unit tests passed successfully.');
