import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const pages = await Promise.all([
  'src/ETL-SQL.Portal/wwwroot/index.html',
  'src/ETL-SQL.Portal/wwwroot/admin.html',
  'src/ETL-SQL.Portal/wwwroot/docs.html',
  'src/ETL-SQL.Portal/wwwroot/orchestrator.html',
  'src/ETL-SQL.Portal/wwwroot/studio.html',
  'src/ETL-SQL.Portal/wwwroot/designer.html'
].map(read));
const branding = await read('src/ETL-SQL.Portal/wwwroot/js/branding.js');
const dialogA11y = await read('src/ETL-SQL.Portal/wwwroot/js/dialog-a11y.js');
const headerSource = await read('src/ETL-SQL.Portal/wwwroot/js/portal-header.js');
const css = await read('src/ETL-SQL.Portal/wwwroot/css/portal.css');

for (const page of pages) {
  assert.match(page, /<header data-portal-header data-active="[^"]+"/);
  assert.match(page, /portal-header\.js/);
  assert.match(page, /renderPortalHeader\(\)/);
  assert.doesNotMatch(page, /class="topbar-nav"/);
}
assert.match(headerSource, /id="mobileMenuBtn"[^>]+aria-label="Open navigation menu"/);
assert.match(headerSource, /'studioNav'/);
assert.match(headerSource, /'docsNav'/);
assert.match(headerSource, /'orchestratorNav'/);
assert.match(headerSource, /'adminNav'/);
assert.match(branding, /aria-modal', 'true'/);
assert.match(branding, /setBackgroundInert\(true\)/);
assert.match(branding, /installDialogAccessibility\(\)/);
assert.doesNotMatch(branding, /drawer\.addEventListener\('keydown'/);
assert.match(dialogA11y, /event\.key === 'Escape'/);
assert.match(dialogA11y, /event\.key !== 'Tab'/);
assert.match(dialogA11y, /returnTo\?\.isConnected/);
assert.match(branding, /aria-expanded', 'true'/);
assert.match(css, /\.shell-nav-overlay\.open/);
assert.match(css, /\.topbar-nav, \.topbar-user/);
assert.match(css, /width: min\(320px, 88vw\)/);
assert.match(css, /\[id\$="TableWrap"\]/);
assert.match(css, /\.admin-tabs, \.orch-detail-tabs/);
assert.match(css, /\.orch-stats-bar \{ display: grid/);
assert.match(css, /\.docs-main-content \{ padding: 16px !important/);
assert.match(css, /\.docs-article \{ padding: 18px !important/);

console.log('Portal shared header and responsive dialog contract passed for all six shell pages.');
