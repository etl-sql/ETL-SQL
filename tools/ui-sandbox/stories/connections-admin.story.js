// Admin → Shared Connections surface driven by a fixture-backed fake of api/admin/connections.
import { importFresh } from '../util.js';

function makeFakeApi(seed, { unresolvable = [] } = {}) {
  let entries = seed.map((e) => ({ ...e, options: { ...e.options }, sensitiveFields: [...(e.sensitiveFields || [])] }));
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
      return { summary: summary(e), target: e.target, options: { ...e.options }, sensitiveFields: [...(e.sensitiveFields || [])] };
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
    async impact(alias) {
      return {
        reference: `SHARED:${alias}`,
        consumerCount: 3,
        consumers: [
          { type: 'Report', name: 'Sales Overview', detail: 'reports/sales.rptsql', lastUsedAtUtc: null, useCount: null },
          { type: 'ScheduledJob', name: 'nightly-load', detail: 'jobs/nightly.etlsql', lastUsedAtUtc: '2026-07-11T01:00:00Z', useCount: null },
          { type: 'Consumer', name: 'ann', detail: 'Recorded at SHARED: resolution', lastUsedAtUtc: '2026-07-10T02:15:00Z', useCount: 42 },
        ],
      };
    },
    async remove(alias) { entries = entries.filter((x) => x.alias !== alias); return {}; },
    async exportAll() {
      return entries.map((e) => ({
        alias: e.alias, connectorType: e.connectorType, target: e.target,
        options: { ...e.options }, environmentScope: e.environmentScope, disabled: e.disabled,
        sensitiveFields: [...(e.sensitiveFields || [])],
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
  };
}

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
    sensitiveFields: ['BUCKET'],
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

    const connectionsApi =
      fixtureId === 'empty' ? makeFakeApi([]) :
      fixtureId === 'unresolvable' ? makeFakeApi(seed, { unresolvable: ['sales_dw', 'archive_s3'] }) :
      makeFakeApi(seed);

    const surface = createConnectionsAdmin({ host: stage, connectionsApi });
    await surface.load();
    ctx.stat('createConnectionsAdmin() — detail masks non-reference credentials server-side; save rejects raw credentials');
    return { dispose() {}, resize() {} };
  },
};
