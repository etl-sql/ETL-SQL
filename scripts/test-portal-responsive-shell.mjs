import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const pages = await Promise.all([
  'src/ETL-SQL.Portal/wwwroot/index.html',
  'src/ETL-SQL.Portal/wwwroot/admin.html',
  'src/ETL-SQL.Portal/wwwroot/docs.html',
  'src/ETL-SQL.Portal/wwwroot/orchestrator.html'
].map(read));
const branding = await read('src/ETL-SQL.Portal/wwwroot/js/branding.js');
const css = await read('src/ETL-SQL.Portal/wwwroot/css/portal.css');

for (const page of pages) {
  assert.match(page, /id="mobileMenuBtn"[^>]+aria-label="Open navigation menu"/);
}
assert.match(branding, /aria-modal', 'true'/);
assert.match(branding, /setBackgroundInert\(true\)/);
assert.match(branding, /event\.key === 'Escape'/);
assert.match(branding, /event\.key !== 'Tab'/);
assert.match(branding, /restoreFocus\?\.focus/);
assert.match(branding, /aria-expanded', 'true'/);
assert.match(css, /\.shell-nav-overlay\.open/);
assert.match(css, /\.topbar-nav, \.topbar-user/);
assert.match(css, /width: min\(320px, 88vw\)/);
assert.match(css, /\[id\$="TableWrap"\]/);
assert.match(css, /\.admin-tabs, \.orch-detail-tabs/);
assert.match(css, /\.orch-stats-bar \{ display: grid/);
assert.match(css, /\.docs-main-content \{ padding: 16px !important/);
assert.match(css, /\.docs-article \{ padding: 18px !important/);

console.log('Portal responsive global drawer contract passed for Reports, Admin, Docs, and Orchestrator.');
