// Story for the Orchestrator Job Administration UI components:
// Schedules catalog, Notifications catalog, Watermark state inspector,
// Definition Change Log (Audit Trail), and 7-Day Run Calendar Timeline.

import {
  renderSchedulesTable,
  renderNotificationsTable,
  renderWatermarksTable,
  renderJobAuditTrail,
  renderCalendarTimeline,
  filterAndPaginateJobs
} from '../../../src/ETL-SQL.Portal/wwwroot/js/orchestrator-admin-ui.js';

const SAMPLE_SCHEDULES = [
  { Name: 'daily_midnight_utc', DisplayName: 'Daily at Midnight (UTC)', Cron: '0 0 * * *', TimeZone: 'UTC', IsEnabled: true, CreatedBy: 'user:admin', Version: 1 },
  { Name: 'hourly_us_east', DisplayName: 'Hourly Sync (Eastern US)', Cron: '0 * * * *', TimeZone: 'America/New_York', IsEnabled: true, CreatedBy: 'user:ops_lead', Version: 2 },
  { Name: 'mon_fri_business', DisplayName: 'Mon-Fri 08:00 (London)', Cron: '0 8 * * 1-5', TimeZone: 'Europe/London', IsEnabled: false, CreatedBy: 'user:data_eng', Version: 1 },
  { Name: 'quarterly_recalc', DisplayName: 'Quarterly Ledger Refresh', Cron: '0 0 1 1,4,7,10 *', TimeZone: 'UTC', IsEnabled: true, CreatedBy: 'user:finance_lead', Version: 3 }
];

const SAMPLE_NOTIFICATIONS = [
  { Name: 'slack_data_alerts', DisplayName: 'Slack #data-ops Channel', ConnectionName: 'slack_prod_webhook', Recipient: '#data-ops', IsEnabled: true, CreatedBy: 'user:admin' },
  { Name: 'email_oncall_pager', DisplayName: 'On-Call PagerDuty Email', ConnectionName: 'smtp_corporate', Recipient: 'pagerduty@company.com', IsEnabled: true, CreatedBy: 'user:ops_lead' },
  { Name: 'teams_exec_summary', DisplayName: 'Teams Executive Updates', ConnectionName: 'teams_exec_webhook', Recipient: 'Executive Reports', IsEnabled: false, CreatedBy: 'user:admin' }
];

const SAMPLE_WATERMARKS = [
  { StateKey: 'last_extracted_id', StateValue: '94827105', UpdatedAt: new Date(Date.now() - 15 * 60 * 1000).toISOString() },
  { StateKey: 'max_order_timestamp', StateValue: '2026-08-16T16:30:00.000Z', UpdatedAt: new Date(Date.now() - 45 * 60 * 1000).toISOString() },
  { StateKey: 'customer_change_cursor', StateValue: 'cs_cursor_8f3a9b1c', UpdatedAt: new Date(Date.now() - 2 * 3600 * 1000).toISOString() }
];

const SAMPLE_AUDIT = [
  { Action: 'ALTER_JOB', ActorId: 'user:admin', Timestamp: new Date(Date.now() - 10 * 60 * 1000).toISOString(), Detail: 'Updated max retries from 0 to 3 with 60s delay' },
  { Action: 'ATTACH_SCHEDULE', ActorId: 'user:ops_lead', Timestamp: new Date(Date.now() - 4 * 3600 * 1000).toISOString(), Detail: "Attached shared schedule 'daily_midnight_utc'" },
  { Action: 'SET_WATERMARK', ActorId: 'user:admin', Timestamp: new Date(Date.now() - 18 * 3600 * 1000).toISOString(), Detail: "Reset key 'last_extracted_id' for historical backfill" },
  { Action: 'CREATE_JOB', ActorId: 'user:alice', Timestamp: new Date(Date.now() - 48 * 3600 * 1000).toISOString(), Detail: 'Created pipeline with SandboxProfile=Hardened' }
];

const SAMPLE_CALENDAR_JOBS = [
  { Name: 'daily_sales_ingest', NextRun: new Date(Date.now() + 2 * 3600 * 1000).toISOString(), IsEnabled: true },
  { Name: 'hourly_inventory_sync', NextRun: new Date(Date.now() + 45 * 60 * 1000).toISOString(), IsEnabled: true },
  { Name: 'dim_customer_rebuild', NextRun: new Date(Date.now() + 26 * 3600 * 1000).toISOString(), IsEnabled: true },
  { Name: 'security_audit_sweep', NextRun: new Date(Date.now() + 50 * 3600 * 1000).toISOString(), IsEnabled: false },
  { Name: 'weekly_fact_snapshot', NextRun: new Date(Date.now() + 96 * 3600 * 1000).toISOString(), IsEnabled: true }
];

const FIXTURES = {
  schedules: {
    label: 'Shared Schedules Catalog',
    note: 'Shows named cron schedules with timezones, active toggles, and linked job counters.',
    render: () => renderSchedulesTable(SAMPLE_SCHEDULES, {
      daily_midnight_utc: 8,
      hourly_us_east: 14,
      mon_fri_business: 2,
      quarterly_recalc: 1
    })
  },
  notifications: {
    label: 'Notification Destinations',
    note: 'Shows delivery channels (Slack, SMTP, Teams) with test dispatch trigger buttons.',
    render: () => renderNotificationsTable(SAMPLE_NOTIFICATIONS, {
      slack_data_alerts: 12,
      email_oncall_pager: 6,
      teams_exec_summary: 1
    })
  },
  watermarks: {
    label: 'Watermark State Inspector & Reset',
    note: 'Surfaces incremental high-water mark keys with safe reset/backfill controls.',
    render: () => `
      <div style="max-width:540px;padding:16px;background:var(--portal-surface);border-radius:8px;border:1px solid var(--portal-border);">
        <h4 style="margin:0 0 10px;font-size:0.9em;">High-Water Marks: <code>daily_sales_etl</code></h4>
        ${renderWatermarksTable(SAMPLE_WATERMARKS, 'daily_sales_etl')}
      </div>`
  },
  audit: {
    label: 'Definition Change Log (Audit Trail)',
    note: 'Chronological event cards detailing who edited scripts, changed schedules, or reset state.',
    render: () => `
      <div style="max-width:540px;padding:16px;background:var(--portal-surface);border-radius:8px;border:1px solid var(--portal-border);">
        <h4 style="margin:0 0 10px;font-size:0.9em;">Audit History: <code>fact_orders_pipeline</code></h4>
        ${renderJobAuditTrail(SAMPLE_AUDIT)}
      </div>`
  },
  calendar: {
    label: '7-Day Run Calendar Timeline',
    note: 'Multi-day calendar grid grouping upcoming scheduled pipeline executions by day.',
    render: () => `
      <div style="padding:16px;background:var(--portal-surface);border-radius:8px;border:1px solid var(--portal-border);">
        <h4 style="margin:0 0 10px;font-size:0.9em;">7-Day Execution Timeline</h4>
        ${renderCalendarTimeline(SAMPLE_CALENDAR_JOBS, 7)}
      </div>`
  }
};

export default {
  id: 'orchestrator-admin-ui',
  title: 'Orchestrator — Job Administration UI',
  category: 'Orchestrator',
  fixtures: FIXTURES,
  render(container, key = 'schedules') {
    const fixture = FIXTURES[key] || FIXTURES.schedules;
    container.innerHTML = `
      <div style="padding:20px;max-width:1100px;margin:0 auto;font-family:var(--portal-font, system-ui, sans-serif);">
        <div style="margin-bottom:16px;">
          <h2 style="margin:0 0 6px;font-size:1.25em;">${fixture.label}</h2>
          <p style="font-size:0.85em;color:var(--portal-muted);margin:0;">${fixture.note}</p>
        </div>
        <div id="fixtureHost">
          ${fixture.render()}
        </div>
      </div>`;
  }
};
