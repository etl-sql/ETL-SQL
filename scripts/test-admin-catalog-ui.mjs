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
