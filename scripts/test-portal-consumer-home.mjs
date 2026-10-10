import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { readPortalPage, readPortalPageModule } from './lib/portal-page.mjs';

const root = new URL('../', import.meta.url);
const index = readPortalPage('index');
const api = await readFile(new URL('src/ETL-SQL.Portal/wwwroot/js/api.js', root), 'utf8');
const css = await readFile(new URL('src/ETL-SQL.Portal/wwwroot/css/portal.css', root), 'utf8');

const moduleSource = readPortalPageModule('index');
assert.ok(moduleSource, 'Portal page module script was not found.');
new Function(moduleSource.replace(/^import .*;$/gm, ''));

// The home, search, and report cards live in the report library owner and its view module; the page
// only mounts them. ReportLibrarySandboxTests drives the behaviour; this keeps the wording in step.
const library = await readFile(new URL('src/ETL-SQL.Portal/wwwroot/js/report-library.js', root), 'utf8')
  + await readFile(new URL('src/ETL-SQL.Portal/wwwroot/js/report-library-view.js', root), 'utf8');
assert.match(api, /consumerHome: \(limit = 8\)/);
assert.match(index, /createReportLibrary\(/);
assert.match(library, /\/api\/catalog\/consumer-home\?limit=8/);
for (const section of ['Favorites', 'Recently viewed', 'Featured', 'Popular']) {
  assert.match(library, new RegExp(`consumerSection\\('${section}'`));
}
assert.match(library, /\/api\/catalog\/search\?q=\$\{encodeURIComponent\(wanted\)\}&limit=50/);
assert.match(library, /Search every folder, description, tag, owner, domain, steward, certification, and lineage term/);
assert.match(library, /export function activityLine\(report/);
assert.doesNotMatch(index + library, /Awaiting first run|Not viewed yet|badge badge-neutral">Not run/);
assert.match(css, /\.consumer-card-grid/);
assert.match(css, /\.consumer-report-icon/);

console.log('Portal consumer home, fuzzy global discovery, and concise report-card status contract passed.');
