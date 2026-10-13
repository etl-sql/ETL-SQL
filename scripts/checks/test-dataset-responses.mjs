import assert from 'node:assert/strict';
import { readDataset, readDatasetCatalog, readDatasetRows, readDatasetStats, readDatasetValues,
  readDatasetAcl, readDatasetGroups, readDatasetLineage, readDatasetRefresh, readDatasetVersion } from '../../src/ETL-SQL.Portal/wwwroot/js/dataset-responses.js';
import { createDatasetMock } from '../../tools/ui-sandbox/stories/datasets-admin.story.js';

const api = createDatasetMock();
const catalog = readDatasetCatalog(await api.list());
assert.equal(catalog.length, 3);
assert.throws(() => readDatasetCatalog(null), /catalog/);
assert.throws(() => readDatasetCatalog([...catalog, catalog[0]]), /catalog/);
assert.throws(() => readDatasetCatalog([{ ...catalog[0], version: undefined }]), /catalog/);
assert.deepEqual(readDatasetCatalog([]), []);
assert.equal(readDataset(catalog[0], 1).name, 'sales_summary');
assert.throws(() => readDataset(catalog[0], 2), /match/);
const rows = readDatasetRows(await api.data(1, { page: 1, pageSize: 25 }), 1, 25);
assert.equal(rows.rows.length, 25);
for (const invalid of [null, { columns: rows.columns, rows: [] }, { ...rows, page: 2 },
  { ...rows, filteredCount: 100 }, { ...rows, rows: rows.rows.slice(1) },
  { ...rows, columns: [...rows.columns, rows.columns[0]] }]) {
  assert.throws(() => readDatasetRows(invalid, 1, 25), /rows/);
}
const missingColumn = { ...rows.rows[0] }; delete missingColumn.region;
assert.throws(() => readDatasetRows({ ...rows, rows: [missingColumn, ...rows.rows.slice(1)] }, 1, 25), /missing declared/);
assert.equal(readDatasetRows(await api.data(1, { search: 'unmatched-value', page: 1, pageSize: 25 }), 1, 25).filteredCount, 0);
assert.equal(readDatasetStats(await api.stats(1)).length, 7);
assert.equal(readDatasetStats(await api.stats(1), rows.columns).length, 7);
const statistics = await api.stats(1);
assert.throws(() => readDatasetStats(statistics.slice(1), rows.columns), /statistics/);
assert.throws(() => readDatasetStats([{ name: 'value', nullCount: undefined }]), /statistics/);
const values = readDatasetValues(await api.columnValues(1, 'region', { limit: 50 }), 50);
assert.equal(values.totalDistinct, 4);
assert.throws(() => readDatasetValues({ values: [1, 2], totalDistinct: 1 }, 50), /values/);
assert.equal(readDatasetAcl(await api.listAcl(1)).length, 2);
assert.equal(readDatasetAcl([{ ...(await api.listAcl(1))[0], permission: 'Refresh' }])[0].permission, 'Refresh');
assert.throws(() => readDatasetAcl([{ principalKind: 'User', groupId: 0, groupName: '', permission: 'Owner' }]), /permissions/);
assert.equal(readDatasetAcl([{ principalKind: 'User', groupId: 0, groupName: '', permission: 'Owner', userId: 7, userName: null }])[0].userId, 7);
assert.throws(() => readDatasetGroups([{ id: 1, name: 'partial' }]), /directory/);
assert.throws(() => readDatasetLineage(null), /lineage/);
assert.equal(readDatasetRefresh(await api.refresh(1)).triggered, true);
assert.equal(readDatasetRefresh({ triggered: false, message: 'Marked stale.' }).triggered, false);
assert.throws(() => readDatasetRefresh({ triggered: true, jobId: 42 }), /acknowledgement/);
assert.equal(readDatasetVersion(await api.grantAcl(1, 5, 'Viewer', 1), 1), 2);
assert.throws(() => readDatasetVersion({ version: 1 }, 1), /advance/);
const requests = [];
let responseStatus = 200, payload = { triggered: true, jobId: 'refresh-id' };
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (url, options) => { requests.push({ url: String(url), options });
    return responseStatus === 204 ? new Response(null, { status: 204 }) : Response.json(payload, { status: responseStatus }); } };
const { datasetsApi } = await import('../../src/ETL-SQL.Portal/wwwroot/js/api.js');
await assert.rejects(datasetsApi.refresh(1), /unexpected HTTP status/);
responseStatus = 202;
assert.equal(readDatasetRefresh(await datasetsApi.refresh(1)).jobId, 'refresh-id');
assert.equal(requests.at(-1).url, '/api/datasets/1/refresh');
await assert.rejects(datasetsApi.update(1, { accessLevel: 'Private', ttl: null }, 1), /unexpected HTTP status/);
responseStatus = 200; payload = { ...catalog[0], accessLevel: 'Private', ttl: null, version: 2 };
assert.equal(readDataset(await datasetsApi.update(1, { accessLevel: 'Private', ttl: null }, 1), 1).version, 2);
assert.equal(new Headers(requests.at(-1).options.headers).get('If-Match'), '"1"');
await assert.rejects(datasetsApi.delete(1, 2), /unexpected HTTP status/);
responseStatus = 204;
assert.equal(await datasetsApi.delete(1, 2), undefined);
console.log('Dataset response completeness, paging, own columns, grant identity, and version/refresh checks passed.');
