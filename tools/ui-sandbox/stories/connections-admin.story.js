// Admin → Shared Connections surface driven by a fixture-backed fake of api/admin/connections.
import { importFresh } from '../util.js';
import { makeWizardApi } from './gateways-admin.story.js';

export function makeFakeApi(seed, { unresolvable = [], mode = 'healthy' } = {}) {
  let entries = seed.map((e) => ({ ...e, options: { ...e.options }, sensitiveFields: [...(e.sensitiveFields || [])] }));
  const err = (status, body) => Object.assign(new Error(body.error || body.status || 'error'), { status, body });
  const summary = (e) => ({
    alias: e.alias, connectorType: e.connectorType, disabled: e.disabled, environmentScope: e.environmentScope,
    createdAtUtc: e.createdAtUtc, updatedAtUtc: e.updatedAtUtc, lastUsedAtUtc: e.lastUsedAtUtc ?? null,
    lastVerifiedAtUtc: e.lastVerifiedAtUtc ?? null, version: e.version, gateway: e.gateway ?? null,
  });
  const api = {
    async list() { return entries.map(summary); },
    async detail(alias) {
      const e = entries.find((x) => x.alias === alias);
      if (!e) throw err(404, { error: `Shared connection '${alias}' does not exist.` });
      const options = { ...e.options };
      for (const field of e.sensitiveFields || []) if (field in options && !/^(SECRET|ENC):/i.test(options[field])) options[field] = '********';
      return { summary: summary(e), target: e.target ?? null, options, sensitiveFields: [...(e.sensitiveFields || [])], gateway: e.gateway ?? null };
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
    async test(alias) {
      const e = entries.find((x) => x.alias === alias);
      if (!e) throw err(404, { error: `Shared connection '${alias}' does not exist.` });
      if (e.disabled) throw err(409, { status: 'disabled' });
      const policyOk = { layer: 'POLICY', status: 'ok', detail: 'Destination permitted by active security policy.', remedy: null };
      if (unresolvable.includes(alias)) {
        return { alias, succeeded: false, steps: [
          policyOk,
          { layer: 'DNS', status: 'failed', detail: `Could not resolve host 'sql01'.`, remedy: 'Verify the hostname is resolvable from this machine (DNS / hosts file / VPN).' },
          { layer: 'TCP', status: 'skipped', detail: 'Not attempted — DNS resolution did not succeed.', remedy: null },
          { layer: 'TLS', status: 'skipped', detail: 'Not attempted — an earlier layer did not succeed.', remedy: null },
          { layer: 'AUTH', status: 'skipped', detail: 'Not attempted — an earlier layer did not succeed.', remedy: null },
        ] };
      }
      if (e.connectorType === 'MSSQL') {
        return { alias, succeeded: true, steps: [
          policyOk,
          { layer: 'DNS', status: 'ok', detail: `'sql01' resolved to 10.2.4.9.`, remedy: null },
          { layer: 'TCP', status: 'ok', detail: 'Port 1433 on sql01 is reachable.', remedy: null },
          { layer: 'TLS', status: 'skipped', detail: 'Connector does not expect transport TLS on this port.', remedy: null },
          { layer: 'AUTH', status: 'skipped', detail: 'Credential authentication was not probed.', remedy: 'Run a query against the connection to confirm the credentials are accepted.' },
        ] };
      }
      return { alias, succeeded: true, steps: [
        policyOk,
        { layer: 'NETWORK', status: 'skipped', detail: `'${e.connectorType}' is a local/file connector — no network diagnostics apply.`, remedy: null },
      ] };
    },
    async disable(alias) { entries.find((x) => x.alias === alias).disabled = true; },
    async enable(alias) { entries.find((x) => x.alias === alias).disabled = false; },
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
    async remove(alias) { entries = entries.filter((x) => x.alias !== alias); },
    async exportAll() {
      return entries.map((e) => ({
        alias: e.alias, connectorType: e.connectorType, target: e.target,
        options: { ...e.options }, environmentScope: e.environmentScope, disabled: e.disabled,
        sensitiveFields: [...(e.sensitiveFields || [])], gateway: e.gateway ?? null,
      }));
    },
    async importAll(list) {
      let created = 0, updated = 0;
      for (const entry of list) {
        const existed = entries.some((x) => x.alias === entry.alias);
        await this.set(entry.alias, entry);
        entries.find(value => value.alias === entry.alias).disabled = entry.disabled;
        if (existed) updated++; else created++;
      }
      return { created, updated };
    },
    async getHelp(type) {
      if (type === 'FLATFILE') {
        return {
          content: `# FLATFILE (CSV) Options\n\nConfigure flat file ingestion options:\n- **PATH** — Absolute path to the flat file.\n- **DELIMITER** — Column delimiter character (default: \`,\`).\n- **HEADER** — Set to \`ON\` to use the first line as column header mappings.\n- **SKIP** — Number of rows to skip at the top of the file.\n\nExample:\n\`\`\`\nPATH=C:\\\\data\\\\sales.csv\nDELIMITER=,\nHEADER=ON\n\`\`\``
        };
      }
      if (type === 'MSSQL') {
        return {
          content: `# MSSQL Options\n\nMicrosoft SQL Server connection options:\n\n> [!NOTE]\n> Store passwords in the secret provider and use a \`SECRET:name\` reference.\n\n| Option | Purpose |\n| --- | --- |\n| **SERVER** | Host name or IP address of the database server. |\n| **DATABASE** | Database catalog name. |\n| **TRUSTED_CONNECTION** | Set to \`TRUE\` to use Windows authentication. |\n| **ENCRYPT** | Set to \`TRUE\` to encrypt the SQL transport channel. |\n\nExample:\n\`\`\`sql\nSERVER=sql-prod-01\nDATABASE=Sales\nENCRYPT=TRUE\n\`\`\``
        };
      }
      return {
        content: `# ${type} Options\n\nStandard option configuration for the **${type}** connector.\n- Consult the [Data Connectors](/docs/reference/connectors/README.md) for the full parameter schema.\n- Mask sensitive parameters by referencing \`SECRET:secret_name\`.`
      };
    },
  };
  const calls = [], queued = [], waiters = [];
  let paused = null;
  return {
    calls, setMode(value) { mode = value; },
    pause(kind) { paused = kind; },
    whenPaused(kind) { return queued.some(value => value.kind === kind) ? Promise.resolve() : new Promise(resolve => waiters.push({ kind, resolve })); },
    release() { for (const value of queued.splice(0)) value.resolve(); },
    snapshot() { return structuredClone(entries); },
    ...Object.fromEntries(Object.entries(api).map(([kind, implementation]) => [kind, async (...args) => {
      calls.push({ kind, args: structuredClone(args.filter(value => !(value instanceof AbortSignal))) });
      let value = await implementation.apply(api, args);
      if (mode === 'unavailable' && kind === 'list') value = null;
      if (mode === 'invalid-diagnostic' && kind === 'test') value = { alias: args[0], succeeded: true };
      if (mode === 'invalid-import' && kind === 'importAll') value = { created: 0, updated: 0 };
      if (mode === 'invalid-verify' && kind === 'verify') value = { alias: 'other', status: 'ok', secretReferences: 2 };
      const snapshot = structuredClone(value);
      if (paused !== kind) return snapshot;
      paused = null;
      return new Promise(resolve => {
        queued.push({ kind, resolve: () => resolve(snapshot) });
        for (const waiting of waiters.filter(value => value.kind === kind)) waiting.resolve();
      });
    }]))
  };
}

export const connectionFixtures = [
  {
    alias: 'gateway_sales', connectorType: 'MSSQL', disabled: false, environmentScope: 'Prod',
    target: null, options: {}, gateway: { gatewayId: 'hq-gateway', resourceId: 'sales_read' },
    createdAtUtc: '2026-06-01T09:00:00Z', updatedAtUtc: '2026-07-08T10:00:00Z', version: 1,
  },
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

export function makeConnectionWizardApi() {
  const api = makeWizardApi({ snapshot: () => [{ gatewayId: 'hq-gateway', isOnline: true,
    activeNodes: 1, totalNodes: 1, publishedResources: [{ resourceId: 'sales_read', connectorType: 'MSSQL',
      state: 'Approved', allowedOperations: 'Read', isOnline: true, lastSeenUtc: null }] }] });
  return { ...api, async parse(request) {
    api.calls.push({ kind: 'parse', request });
    return { detectedProvider: 'MSSQL', options: { SERVER: 'sql01', DATABASE: 'Sales' }, extractedCredential: null, suggestedSecretKey: null };
  } };
}

export default {
  id: 'connections-admin',
  title: 'Connections Admin',
  subtitle: 'api/admin/connections catalog manager',
  fixtures: [
    { id: 'healthy', label: 'Cataloged connections' },
    { id: 'unresolvable', label: 'Broken secret references' },
    { id: 'empty', label: 'Empty catalog' },
    { id: 'unavailable', label: 'Missing catalog evidence' },
  ],
  async mount(stage, fixtureId, ctx) {
    const { createConnectionsAdmin } = await importFresh('/src/ETL-SQL.Portal/wwwroot/js/connections-admin.js');
    stage.classList.add('portal-page');

    const connectionsApi =
      fixtureId === 'empty' ? makeFakeApi([]) :
      fixtureId === 'unresolvable' ? makeFakeApi(connectionFixtures, { unresolvable: ['sales_dw', 'archive_s3'] }) :
      makeFakeApi(connectionFixtures, { mode: fixtureId });

    const wizardApi = makeConnectionWizardApi();
    const surface = createConnectionsAdmin({ host: stage, connectionsApi, wizardApi });
    window.__connections = { api: connectionsApi, surface, wizardApi };
    await surface.load();
    ctx.stat('createConnectionsAdmin() — detail masks non-reference credentials server-side; save rejects raw credentials');
    return { dispose() { surface.dispose?.(); delete window.__connections; }, resize() {} };
  },
};
