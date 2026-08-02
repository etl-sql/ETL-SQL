import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [admin, module, api, css, reports, adminController, story] = await Promise.all([
  read('src/ETL-SQL.Portal/wwwroot/admin.html'),
  read('src/ETL-SQL.Portal/wwwroot/js/operations-admin.js'),
  read('src/ETL-SQL.Portal/wwwroot/js/api.js'),
  read('src/ETL-SQL.Portal/wwwroot/css/portal.css'),
  read('src/ETL-SQL.Portal/Controllers/ReportsController.cs'),
  read('src/ETL-SQL.Portal/Controllers/AdminController.cs'),
  read('tools/ui-sandbox/stories/portal-operations.story.js'),
]);

assert.match(admin, /data-tab="operations"/);
assert.match(admin, /createOperationsAdmin/);
assert.match(module, /Promise\.allSettled/);
assert.match(module, /One-time credential/);
assert.match(module, /account-history/);
assert.match(module, /revokeAnonymousReportAccess/);
assert.match(module, /adminServiceHistory/);
assert.doesNotMatch(module, /\b(?:alert|confirm|prompt)\s*\(/);
assert.match(api, /listServiceAccounts/);
assert.match(api, /pendingAccessRequests/);
assert.match(api, /fleetStatus/);
assert.match(css, /\.ops-signal-rail/);
assert.match(reports, /RevokeAnonymousReportAccess/);
assert.match(reports, /ADMIN_REVOKE_REPORT_SHARE_LINK/);
assert.match(adminController, /resourceType/);
assert.match(adminController, /resourceId/);
assert.match(story, /signal-to-owner control room/);

console.log('Portal Administration Operations hub contract passed.');
