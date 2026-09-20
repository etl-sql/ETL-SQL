// Unit tests for admin catalog query, selection, and pager rendering helpers.
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const mod = await import(pathToFileURL(path.resolve(
    'src/ETL-SQL.Portal/wwwroot/js/admin-catalog-ui.js')).href);
const { catalogQuery, headerSelectionCell, selectionCell } = mod;

function assert(condition, message) {
    if (!condition) throw new Error(`FAIL: ${message}`);
}

const query = catalogQuery({ q: 'finance ops', status: 'active', page: 2, empty: '' });
assert(query.includes('q=finance+ops'), 'search terms are encoded');
assert(query.includes('status=active') && query.includes('page=2'), 'filters and page are included');
assert(!query.includes('empty='), 'empty filters are omitted');

const row = selectionCell(42, 'user finance_read');
assert(row.includes('data-select-id="42"'), 'row selection id renders');
assert(row.includes('Select user finance_read'), 'row selection label renders');

const header = headerSelectionCell('users');
assert(header.includes('data-select-all'), 'select-all control renders');
assert(header.includes('Select all users on this page'), 'select-all label renders');

console.log('admin-catalog-ui tests passed');

// Server-provided pagination values must never become HTML markup.
const pagerRoot = { innerHTML: '', querySelectorAll: () => [] };
const payload = '<img src=x onerror=alert(1)>';
mod.renderCatalogPager(pagerRoot, { total: payload, page: payload, pageSize: payload }, () => {});
assert(!pagerRoot.innerHTML.includes('<img'), 'pager rejects markup in all numeric fields');
assert(pagerRoot.innerHTML.includes('0-0 of 0'), 'invalid totals fall back to zero');
assert(pagerRoot.innerHTML.includes('Page 1 of 1'), 'invalid page values use safe defaults');
mod.renderCatalogPager(pagerRoot, { total: '52.9', page: '2.9', pageSize: '25.9' }, () => {});
assert(pagerRoot.innerHTML.includes('26-50 of 52'), 'pager truncates numeric server values');
assert(pagerRoot.innerHTML.includes('Page 2 of 3'), 'pager preserves valid pagination');