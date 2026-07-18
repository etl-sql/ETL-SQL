// Unit tests for the Admin publish-form folder helpers (src/ETL-SQL.Portal/wwwroot/js/publish-folders.js).
// Covers the two reported bugs: nested folders weren't selectable / a new folder needed a page
// reload to appear, and there was no inline "create folder" path.
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const mod = await import(
    pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js/publish-folders.js')).href);
const { flattenFolders, folderOptionsHtml, populateFolderSelects, createFolderInline } = mod;

function assert(cond, msg) { if (!cond) throw new Error('FAIL: ' + msg); }
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

// GET /api/folders returns roots with nested children.
const tree = [
    { id: 1, name: 'Finance', path: '/Finance', children: [
        { id: 2, name: 'Reports', path: '/Finance/Reports', children: [] },
    ] },
    { id: 3, name: 'Ops', path: '/Ops', children: [] },
];

// 1. flatten surfaces nested folders, depth-first.
const flat = flattenFolders(tree);
assert(flat.length === 3, `flatten should surface nested folders (got ${flat.length})`);
assert(flat.map(f => f.id).join(',') === '1,2,3', 'flatten should be depth-first');

// 2. options markup includes the nested folder by full path.
const html = folderOptionsHtml(flat, esc);
assert(html.includes('value="2"') && html.includes('/Finance/Reports'), 'nested folder option present');

// 3. populate always fetches fresh, fills both selects, and applies selectedId.
let listCalls = 0;
const foldersApi = {
    list: async () => { listCalls++; return tree; },
    create: async (name, parentId) => ({ id: 99, name, parentId, path: (parentId ? '/Finance/' : '/') + name, children: [] }),
};
const select = { innerHTML: '', value: '' };
const parentSelect = { innerHTML: '' };
const returned = await populateFolderSelects({ foldersApi, esc, select, parentSelect, selectedId: 2 });
assert(listCalls === 1, 'populate fetches a fresh list');
assert(select.innerHTML.includes('value="2"'), 'destination select populated');
assert(select.value === '2', 'selectedId applied to destination select');
assert(parentSelect.innerHTML.startsWith('<option value="">— Root —</option>'), 'parent select has Root option first');
assert(returned.length === 3, 'returns the flat list');

// 4. createFolderInline trims the name, coerces the parent to a number, returns the DTO.
const created = await createFolderInline({ foldersApi, name: '  Q3  ', parentId: '1' });
assert(created.id === 99 && created.name === 'Q3', 'create trims name and returns the DTO');
let threw = false;
try { await createFolderInline({ foldersApi, name: '   ' }); } catch { threw = true; }
assert(threw, 'create throws on an empty name');

// 5. End-to-end: create a folder, then re-populate — it appears and is selected, no page reload.
const treeAfter = [...tree, { id: 99, name: 'Q3', path: '/Q3', children: [] }];
let calls = 0;
const api2 = {
    list: async () => { calls++; return calls === 1 ? tree : treeAfter; },
    create: foldersApi.create,
};
const sel2 = { innerHTML: '', value: '' };
await populateFolderSelects({ foldersApi: api2, esc, select: sel2 });
assert(!sel2.innerHTML.includes('value="99"'), 'new folder absent before creation');
const newFolder = await createFolderInline({ foldersApi: api2, name: 'Q3', parentId: '' });
await populateFolderSelects({ foldersApi: api2, esc, select: sel2, selectedId: newFolder.id });
assert(sel2.innerHTML.includes('value="99"'), 'new folder appears after re-populate (no page reload)');
assert(sel2.value === '99', 'new folder is auto-selected');

console.log('publish-folders tests passed');
