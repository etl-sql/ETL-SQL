import assert from 'node:assert/strict';
import { readAdminGroupPage, readAdminMemberPage, readAdminGroupReceipt, readAdminGroupDeleteReceipt,
  readAdminMembersAdded, readAdminMembersRemoved, readAdminMemberRemoved } from '../src/ETL-SQL.Portal/wwwroot/js/admin-groups-responses.js';

const group = { id: 10, name: 'Finance', description: 'Finance team', memberCount: 1, provider: 'Local', adGroup: null, version: 2 };
const page = { items: [group], total: 1, page: 1, pageSize: 25 };
assert.equal(readAdminGroupPage(page, 1, 25).items[0].name, 'Finance');
for (const bad of [null, {}, { ...page, total: 2 }, { ...page, page: 2 }, { ...page, items: [group, group], total: 2 },
  { ...page, items: [{ ...group, version: null }] }, { ...page, items: [{ ...group, provider: {} }] }])
  assert.throws(() => readAdminGroupPage(bad, 1, 25));
const member = { id: 2, username: 'steward', email: null, isActive: true };
const members = { ...page, items: [member], groupId: 10, version: 2 };
assert.equal(readAdminMemberPage(members, 10, 1, 25).version, 2);
for (const bad of [{ ...members, groupId: 11 }, { ...members, version: 0 }, { ...members, total: 2 },
  { ...members, items: [{ ...member, isActive: null }] }, { ...members, items: [member, member], total: 2 }])
  assert.throws(() => readAdminMemberPage(bad, 10, 1, 25));
assert.equal(readAdminGroupReceipt({ ...group, version: 3 }, { name: group.name }, group).version, 3);
const created = { ...group, description: null, memberCount: 0, version: 1 };
assert.equal(readAdminGroupReceipt(created, { name: group.name }, null).id, 10);
for (const bad of [group, { ...created, memberCount: 1 }, { ...created, provider: 'AD' }, { ...created, adGroup: 'Changed' }])
  assert.throws(() => readAdminGroupReceipt(bad, { name: group.name }, null));
for (const bad of [{ ...group, id: 11, version: 3 }, group, { ...group, name: 'Wrong', version: 3 },
  { ...group, provider: 'AD', version: 3 }, { ...group, adGroup: 'Changed', version: 3 }])
  assert.throws(() => readAdminGroupReceipt(bad, { name: group.name }, group));
const targets = [{ id: 10, version: 2 }, { id: 11, version: 1 }];
const deletion = { deleted: 1, results: [{ id: 10, status: 'Deleted', currentVersion: null, error: null },
  { id: 11, status: 'Blocked', currentVersion: 1, error: 'Group has members.' }] };
assert.equal(readAdminGroupDeleteReceipt(deletion, targets).deleted, 1);
for (const bad of [{ ...deletion, deleted: 2 }, { ...deletion, results: [deletion.results[0]] },
  { ...deletion, results: [deletion.results[0], deletion.results[0]] },
  { ...deletion, results: [deletion.results[0], { ...deletion.results[1], currentVersion: null }] },
  { ...deletion, results: [deletion.results[0], { ...deletion.results[1], id: 99 }] }])
  assert.throws(() => readAdminGroupDeleteReceipt(bad, targets));
const receipt = { groupId: 10, version: 3, changedUserIds: [1], skippedUserIds: [2], added: 1 };
assert.deepEqual(readAdminMembersAdded(receipt, 10, [1, 2], 2).skippedUserIds, [2]);
assert.equal(readAdminMembersRemoved({ ...receipt, removed: 1 }, 10, [1, 2], 2).removed, 1);
for (const bad of [{ ...receipt, groupId: 11 }, { ...receipt, version: 2 }, { ...receipt, added: 2 },
  { ...receipt, changedUserIds: [1, 2] }, { ...receipt, skippedUserIds: [] }, { ...receipt, skippedUserIds: [99] },
  { ...receipt, changedUserIds: [0] }, { ...receipt, skippedUserIds: [1] }])
  assert.throws(() => readAdminMembersAdded(bad, 10, [1, 2], 2));
assert.equal(readAdminMemberRemoved({ groupId: 10, userId: 2, version: 3 }, 10, 2, 2).version, 3);
assert.throws(() => readAdminMemberRemoved({ groupId: 11, userId: 2, version: 3 }, 10, 2, 2));
assert.throws(() => readAdminMemberRemoved({ groupId: 10, userId: 1, version: 3 }, 10, 2, 2));

const calls = [];
let reply = () => new Response(null, { status: 204 });
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { calls.push({ url, options }); return reply(); } };
const { adminGroupsApi, AdminGroupOutcomeUnconfirmed } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-groups-api.js');
await adminGroupsApi.delete(group);
assert.equal(new Headers(calls[0].options.headers).get('If-Match'), '"2"');
for (const response of [() => Response.json({ deleted: true }), () => new Response(null, { status: 202 }),
  () => new Response(null, { status: 500 }), () => { throw new Error('Connection lost'); }]) {
  reply = response;
  await assert.rejects(() => adminGroupsApi.delete(group), error => error instanceof AdminGroupOutcomeUnconfirmed && error.groupId === 10);
}
reply = () => Response.json({ error: 'Version conflict' }, { status: 409 });
await assert.rejects(() => adminGroupsApi.delete(group), error => !(error instanceof AdminGroupOutcomeUnconfirmed) && error.status === 409);
reply = () => Response.json(receipt);
await adminGroupsApi.addMembers(10, [1, 2], 2);
assert.equal(new Headers(calls.at(-1).options.headers).get('If-Match'), '"2"');
assert.deepEqual(JSON.parse(calls.at(-1).options.body), { userIds: [1, 2] });
reply = () => Response.json({ ...receipt, groupId: 11 });
await assert.rejects(() => adminGroupsApi.addMembers(10, [1, 2], 2), AdminGroupOutcomeUnconfirmed);
reply = () => new Response(null, { status: 204 });
await assert.rejects(() => adminGroupsApi.removeMember(10, 2, 2), AdminGroupOutcomeUnconfirmed);
reply = () => Response.json({ groupId: 10, userId: 2, version: 3 });
await adminGroupsApi.removeMember(10, 2, 2);
reply = () => Response.json(deletion);
await adminGroupsApi.bulkDelete(targets);
assert.deepEqual(JSON.parse(calls.at(-1).options.body), { groups: targets, cascade: false });
console.log('admin group response, scoped receipt, and mutation checks passed');
