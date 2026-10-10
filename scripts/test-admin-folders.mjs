import assert from 'node:assert/strict';
import { readAdminFolderTree, readAdminFolderReceipt, readAdminFolderAcl, readAdminFolderGrant,
  readAdminFolderRevoke } from '../src/ETL-SQL.Portal/wwwroot/js/admin-folders-responses.js';

const child = { id: 21, parentId: 20, name: 'Child', path: '/Reports/Child', version: 1, children: [] };
const folder = { id: 20, parentId: null, name: 'Reports', path: '/Reports', version: 2, children: [child] };
const tree = [folder];
assert.deepEqual(readAdminFolderTree(tree).folders.map(item => item.id), [20, 21]);
// A scoped tree may expose a child without exposing its parent.
assert.equal(readAdminFolderTree([child]).folders[0].parentId, 20);
for (const bad of [null, {}, [folder, folder], [{ ...folder, version: 0 }],
  [{ ...folder, children: [{ ...child, parentId: null }] }], [folder, child],
  [{ ...folder, children: [{ ...child, id: 20 }] }], [{ ...folder, children: [{ ...child, children: null }] }],
  [{ ...folder, children: [{ ...child, path: null }] }]]) assert.throws(() => readAdminFolderTree(bad));
const body = { name: 'Renamed', parentId: null };
const renamed = { ...folder, name: body.name, path: '/Renamed', children: [], version: 3 };
assert.equal(readAdminFolderReceipt(renamed, body, folder).version, 3);
assert.equal(readAdminFolderReceipt({ ...child, version: 1 }, { name: 'Child', parentId: 20 }, null).parentId, 20);
for (const bad of [folder, { ...renamed, id: 21 }, { ...renamed, version: 2 }, { ...renamed, name: 'Wrong' },
  { ...renamed, parentId: 21 }, { ...renamed, path: '/Wrong' }, { ...renamed, children: [child] }])
  assert.throws(() => readAdminFolderReceipt(bad, body, folder));
assert.throws(() => readAdminFolderReceipt({ ...renamed, version: 2 }, body, null));
const permissions = [{ permission: 2, name: 'Manage', rank: 3 }, { permission: 3, name: 'Author', rank: 2 },
  { permission: 1, name: 'Execute', rank: 1 }, { permission: 0, name: 'Read', rank: 0 }];
const acl = { folderId: 20, folderName: 'Reports', version: 2,
  items: [{ groupId: 10, groupName: 'Finance', permission: 2 }], permissions };
assert.deepEqual(readAdminFolderAcl(acl, 20).permissions.map(option => [option.name, option.permission]),
  [['Read', 0], ['Execute', 1], ['Author', 3], ['Manage', 2]]);
for (const bad of [null, {}, { ...acl, folderId: 21 }, { ...acl, version: 0 },
  { ...acl, items: [acl.items[0], acl.items[0]] }, { ...acl, items: [{ ...acl.items[0], permission: 99 }] },
  { ...acl, permissions: permissions.slice(1) }, { ...acl, permissions: [permissions[0], ...permissions.slice(0, 3)] },
  { ...acl, permissions: permissions.map(option => ({ ...option, rank: 0 })) },
  { ...acl, permissions: permissions.map(option => ({ ...option, name: 'Duplicate' })) }])
  assert.throws(() => readAdminFolderAcl(bad, 20));
const grant = { folderId: 20, groupId: 10, permission: 3, version: 3 };
assert.equal(readAdminFolderGrant(grant, 20, 10, 3, 2).permission, 3);
for (const bad of [{ ...grant, folderId: 21 }, { ...grant, groupId: 11 }, { ...grant, permission: 2 },
  { ...grant, version: 2 }, { ...grant, version: '3' }]) assert.throws(() => readAdminFolderGrant(bad, 20, 10, 3, 2));
assert.equal(readAdminFolderRevoke(grant, 20, 10, 2).version, 3);
assert.throws(() => readAdminFolderRevoke({ ...grant, groupId: 11 }, 20, 10, 2));

const calls = [];
let reply = () => Response.json(tree);
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminFoldersApi, AdminFolderOutcomeUnconfirmed } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-folders-api.js');
assert.equal((await adminFoldersApi.tree()).folders.length, 2);
reply = () => Response.json({ ...folder, children: [] });
assert.equal((await adminFoldersApi.folder(20)).id, 20);
await assert.rejects(() => adminFoldersApi.folder(21), TypeError);
reply = () => Response.json(acl);
assert.equal((await adminFoldersApi.acl(20)).version, 2);
assert.ok(String(calls.at(-1).url).endsWith('/api/folders/20/acl/catalog'));
reply = () => Response.json({ ...renamed, version: 1 }, { status: 201 });
await adminFoldersApi.create(body);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), body);
reply = () => Response.json(renamed);
await adminFoldersApi.update(folder, body);
assert.equal(new Headers(calls.at(-1).options.headers).get('If-Match'), '"2"');
reply = () => Response.json(grant);
await adminFoldersApi.grant(20, { groupId: 10, permission: 3 }, 2);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), { groupId: 10, permission: 3 });
assert.equal(new Headers(calls.at(-1).options.headers).get('If-Match'), '"2"');
await adminFoldersApi.revoke(20, 10, 2);
assert.equal(calls.at(-1).options.method, 'DELETE');
reply = () => new Response(null, { status: 204 });
await adminFoldersApi.delete(folder);
assert.ok(String(calls.at(-1).url).endsWith('/api/folders/20?cascade=true'));
for (const response of [() => Response.json({ deleted: true }), () => new Response(null, { status: 202 }),
  () => new Response(null, { status: 500 }), () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminFoldersApi.delete(folder), error => error instanceof AdminFolderOutcomeUnconfirmed && error.folderId === 20);
}
reply = () => Response.json({ error: 'Version conflict' }, { status: 409 });
await assert.rejects(() => adminFoldersApi.update(folder, body), error => !(error instanceof AdminFolderOutcomeUnconfirmed) && error.status === 409);
reply = () => Response.json({ ...grant, folderId: 21 });
await assert.rejects(() => adminFoldersApi.grant(20, { groupId: 10, permission: 3 }, 2), AdminFolderOutcomeUnconfirmed);
reply = () => new Response(null, { status: 204 });
await assert.rejects(() => adminFoldersApi.revoke(20, 10, 2), AdminFolderOutcomeUnconfirmed);
console.log('admin folder hierarchy, permissions, scoped receipt, and mutation checks passed');
