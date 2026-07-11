// Admin → Shared Connections surface driven by a fixture-backed fake of api/admin/connections.
import { importFresh } from '../util.js';

function makeFakeApi(seed, { unresolvable = [] } = {}) {
  let entries = seed.map((e) => ({ ...e, options: { ...e.options } }));
  const err = (status, body) => Object.assign(new Error(body.error || body.status || 'error'), { status, body });
  const summary = (e) => ({
    alias: e.alias, connectorType: e.connectorType, disabled: e.disabled, environmentScope: e.environmentScope,
    createdAtUtc: e.createdAtUtc, updatedAtUtc: e.updatedAtUtc, lastUsedAtUtc: e.lastUsedAtUtc,
    lastVerifiedAtUtc: e.lastVerifiedAtUtc, version: e.version,
  });
  return {
    async list() { return entries.map(summary); },
    async detail(alias) {
      const e = entries.find((x) => x.alias === alias);
      if (!e) throw err(404, { error: `Shared connection '${alias}' does not exist.` });
      return { summary: summary(e), target: e.target, options: { ...e.options } };
    },
    async set(alias, entry) {
      const raw = Object.entries(entry.options || {}).find(([k, v]) =>
        ['PASSWORD', 'TOKEN', 'SECRET_KEY', 'ACCESS_KEY'].includes(k.toUpperCase())
        && !/^\s*(SECRET|ENC):/i.test(v));
      if (raw) throw err(400, { error: `Field '${raw[0]}' holds a raw credential value. The catalog stores references only: store the value in the secret store and reference it as SECRET:name.` });
      const now = new Date().toISOString();
      const existing = entries.find((x) => x.alias === alias);
      if (existing) Object.assign(existing, entry, { disabled: false, updatedAtUtc: now, version: existing.version + 1 });
      else entries.push({ alias, ...entry, disabled: false, createdAtUtc: now, updatedAtUtc: now, version: 1 });
      return {};
    },
    async verify(alias) {
      const e = entries.find((x) => x.alias === alias);
      if (!e) throw err(404, { error: `Shared connection '${alias}' does not exist.` });
      if (e.disabled) throw err(409, { status: 'disabled' });
      if (unresolvable.includes(alias)) throw err(409, { status: 'unresolvable', error: 'SECRET reference missing' });
      e.lastVerifiedAtUtc = new Date().toISOString();
      const refs = Object.values(e.options).filter((v) => /^SECRET:/i.test(v)).length;
      return { alias, status: 'ok', secretReferences: refs };
    },
    async disable(alias) { entries.find((x) => x.alias === alias).disabled = true; return {}; },
    async enable(alias) { entries.find((x) => x.alias === alias).disabled = false; return {}; },
    async remove(alias) { entries = entries.filter((x) => x.alias !== alias); return {}; },
    async exportAll() {
      return entries.map((e) => ({
        alias: e.alias, connectorType: e.connectorType, target: e.target,
        options: { ...e.options }, environmentScope: e.environmentScope, disabled: e.disabled,
      }));
    },
    async importAll(list) {
      let created = 0, updated = 0;
      for (const entry of list) {
        const existed = entries.some((x) => x.alias === entry.alias);
        await this.set(entry.alias, entry);
        if (existed) updated++; else created++;
      }
      return { created, updated };
    },
    async listAcl(alias) { return (acls[alias] || []).map((a) => ({ ...a })); },
    async grantAcl(alias, groupId) {
      acls[alias] = acls[alias] || [];
      if (!acls[alias].some((a) => a.groupId === groupId)) {
        const group = fakeGroups.find((g) => g.id === groupId);
        acls[alias].push({ groupId, groupName: group?.name ?? `group-${groupId}`, permission: 'Use' });
      }
      return {};
    },
    async revokeAcl(alias, groupId) {
      acls[alias] = (acls[alias] || []).filter((a) => a.groupId !== groupId);
      return {};
    },
  };
}

const fakeGroups = [
  { id: 1, name: 'Analysts' },
  { id: 2, name: 'Finance' },
  { id: 3, name: 'ETL-Operators' },
];
const fakeAdminApi = { async listGroups() { return fakeGroups.map((g) => ({ ...g })); } };
let acls = {};

const seed = [
  {
    alias: 'sales_dw', connectorType: 'MSSQL', disabled: false, environmentScope: 'Prod',
    target: null,
    options: { SERVER: 'sql01', DATABASE: 'Sales', USER: 'etl_worker', PASSWORD: 'SECRET:sales_db_password' },
    createdAtUtc: '2026-06-01T09:00:00Z', updatedAtUtc: '2026-07-08T10:00:00Z',
    lastUsedAtUtc: '2026-07-10T02:15:00Z', lastVerifiedAtUtc: '2026-07-09T08:00:00Z', version: 4,
  },
  {
    alias: 'archive_s3', connectorType: 'S3', disabled: false, environmentScope: 'Prod',
    target: null,
    options: { BUCKET: 'archive-bucket', ACCESS_KEY: 'SECRET:archive_access_key', SECRET_KEY: 'SECRET:archive_secret_key' },
    createdAtUtc: '2026-06-12T11:00:00Z', updatedAtUtc: '2026-06-12T11:00:00Z',
    lastUsedAtUtc: null, lastVerifiedAtUtc: null, version: 1,
  },
  {
    alias: 'legacy_ftp', connectorType: 'SFTP', disabled: true, environmentScope: 'Dev',
    target: 'Host=ftp01;Password=********',
    options: {},
    createdAtUtc: '2025-11-03T08:00:00Z', updatedAtUtc: '2026-05-20T16:45:00Z',
    lastUsedAtUtc: '2026-05-19T04:00:00Z', lastVerifiedAtUtc: null, version: 9,
  },
];

export default {
  id: 'connections-admin',
  title: 'Connections Admin',
  subtitle: 'api/admin/connections catalog manager',
  fixtures: [
    { id: 'healthy', label: 'Cataloged connections' },
    { id: 'unresolvable', label: 'Broken secret references' },
    { id: 'empty', label: 'Empty catalog' },
  ],
  async mount(stage, fixtureId, ctx) {
    const { createConnectionsAdmin } = await importFresh('/src/ETL-SQL.ReportPortal/wwwroot/js/connections-admin.js');
    stage.classList.add('portal-page');

    acls = fixtureId === 'healthy'
      ? { sales_dw: [{ groupId: 2, groupName: 'Finance', permission: 'Use' }] }
      : {};
    const connectionsApi =
      fixtureId === 'empty' ? makeFakeApi([]) :
      fixtureId === 'unresolvable' ? makeFakeApi(seed, { unresolvable: ['sales_dw', 'archive_s3'] }) :
      makeFakeApi(seed);

    const surface = createConnectionsAdmin({ host: stage, connectionsApi, adminApi: fakeAdminApi });
    await surface.load();
    ctx.stat('createConnectionsAdmin() — detail shows use grants; grant/revoke mutate the fake in-memory ACLs');
    return { dispose() {}, resize() {} };
  },
};
