import assert from 'node:assert/strict';
import { readSubscriptionList, readSmtpAliases, renderSubscriptionRows } from '../../src/ETL-SQL.Portal/wwwroot/js/reader-subscriptions-view.js';

const subscription = { id: 11, reportId: 7, reportName: 'Regional sales', name: null, schedule: 'Daily', atTime: null, deliverOnRefresh: false,
  format: 'PDF', smtpAlias: 'smtp_ops', recipients: 'reader@example.test', lastSentAt: null, nextRunAt: '2026-10-10T06:00:00Z',
  failCount: 0, isActive: true, parameters: null, parameterSummary: null, version: 2 };

assert.deepEqual(readSubscriptionList([]), []);
assert.equal(readSubscriptionList([subscription])[0].id, 11);
for (const invalid of [null, {}, 'none', [null], [{ ...subscription, version: 0 }], [{ ...subscription, reportName: undefined }],
  [{ ...subscription, parameters: { Region: 4 } }], [{ ...subscription, nextRunAt: 'tomorrow' }], [subscription, subscription]])
  assert.throws(() => readSubscriptionList(invalid), /unavailable or incomplete/, JSON.stringify(invalid));

assert.deepEqual(readSmtpAliases(['smtp_ops']), ['smtp_ops']);
assert.deepEqual(readSmtpAliases([]), []);
for (const invalid of [null, {}, [''], [' '], [3], 'smtp_ops'])
  assert.throws(() => readSmtpAliases(invalid), /delivery connection list is unavailable or incomplete/);

// Every server-supplied string is text: nothing in a name, recipient, or summary becomes markup or an attribute.
const hostile = { ...subscription, name: '<img src=x onerror=1>', reportName: '"><script>alert(1)</script>',
  recipients: "o'brien@example.test", parameterSummary: 'Region=<b>North</b>', schedule: '<i>Daily</i>' };
const rows = renderSubscriptionRows([hostile, { ...subscription, id: 12, isActive: false, failCount: 1, format: 'Link', recipients: '' }], new Set([12]));
assert.doesNotMatch(rows, /<img|<script|<b>|<i>/);
assert.match(rows, /&lt;img src=x onerror=1&gt;/);
assert.match(rows, /o&#39;brien@example\.test/);
assert.match(rows, /Region=&lt;b&gt;North&lt;\/b&gt;/);
assert.match(rows, /1 failed send</);
assert.match(rows, /Portal link/);
// A subscription with a write in flight keeps its changing actions locked; reading its history stays available.
const locked = rows.slice(rows.indexOf('data-subscription-id="12"'));
assert.equal((locked.match(/ disabled>/g) ?? []).length, 3);
assert.match(locked, /data-action="history" data-id="12">History/);
assert.doesNotMatch(rows.slice(0, rows.indexOf('data-subscription-id="12"')), / disabled>/);
assert.equal(renderSubscriptionRows([], new Set()), '');

console.log('reader subscription checks passed');
