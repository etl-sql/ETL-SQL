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

assert.match(api, /consumerHome: \(limit = 8\)/);
assert.match(index, /catalogApi\.consumerHome\(8\)/);
for (const section of ['Favorites', 'Recently viewed', 'Featured', 'Popular']) {
  assert.match(index, new RegExp(`renderConsumerSection\\('${section}'`));
}
assert.match(index, /catalogApi\.search\(q, 50\)/);
assert.match(index, /Search every folder, description, tag, owner, domain, steward, certification, and lineage term/);
assert.match(index, /function reportActivityLine\(report\)/);
assert.doesNotMatch(index, /Awaiting first run|Not viewed yet|badge badge-neutral">Not run/);
assert.match(css, /\.consumer-card-grid/);
assert.match(css, /\.consumer-report-icon/);

console.log('Portal consumer home, fuzzy global discovery, and concise report-card status contract passed.');
