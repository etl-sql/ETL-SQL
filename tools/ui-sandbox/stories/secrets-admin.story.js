// Admin → Secrets surface driven by a fixture-backed fake of api/admin/secrets.
import { importFresh } from '../util.js';

function makeFakeApi(seed, { failVerify = [], keyRingBroken = false } = {}) {
  let secrets = seed.map((s) => ({ ...s }));
  const err = (status, body) => Object.assign(new Error(body.error || body.status || 'error'), { status, body });
  return {
    async list() { return secrets.map((s) => ({ ...s })); },
    async set(name, _value) {
      const existing = secrets.find((s) => s.name === name);
      const now = new Date().toISOString();
      if (existing) { existing.disabled = false; existing.updatedAtUtc = now; existing.version++; }
      else secrets.push({ name, disabled: false, createdAtUtc: now, updatedAtUtc: now, version: 1 });
      return {};
    },
    async verify(name) {
      const secret = secrets.find((s) => s.name === name);
      if (!secret) throw err(404, { error: `Secret '${name}' does not exist.` });
      if (secret.disabled) throw err(409, { status: 'disabled' });
      if (failVerify.includes(name)) throw err(409, { status: 'undecryptable' });
      return { name, status: 'ok' };
    },
    async verifyAll() {
      const failed = keyRingBroken ? secrets.length : failVerify.length;
      return { secretCount: secrets.length, failedCount: failed, firstFailedName: failed ? (failVerify[0] ?? secrets[0]?.name) : null };
    },
    async disable(name) { secrets.find((s) => s.name === name).disabled = true; return {}; },
    async enable(name) { secrets.find((s) => s.name === name).disabled = false; return {}; },
    async remove(name) { secrets = secrets.filter((s) => s.name !== name); return {}; },
  };
}

const seed = [
  { name: 'sales_db_password', disabled: false, createdAtUtc: '2026-06-01T09:00:00Z', updatedAtUtc: '2026-07-01T14:30:00Z', version: 3 },
  { name: 'archive_access_key', disabled: false, createdAtUtc: '2026-06-12T11:00:00Z', updatedAtUtc: '2026-06-12T11:00:00Z', version: 1 },
  { name: 'old_ftp_password', disabled: true, createdAtUtc: '2025-11-03T08:00:00Z', updatedAtUtc: '2026-05-20T16:45:00Z', version: 7 },
];

export default {
  id: 'secrets-admin',
  title: 'Secrets Admin',
  subtitle: 'api/admin/secrets manager',
  fixtures: [
    { id: 'healthy', label: 'Healthy store' },
    { id: 'keyring-broken', label: 'Wrong key ring (verify fails)' },
    { id: 'empty', label: 'Empty store' },
  ],
  async mount(stage, fixtureId, ctx) {
    const { createSecretsAdmin } = await importFresh('/src/ETL-SQL.ReportPortal/wwwroot/js/secrets-admin.js');
    stage.classList.add('portal-page');

    const secretsApi =
      fixtureId === 'empty' ? makeFakeApi([]) :
      fixtureId === 'keyring-broken' ? makeFakeApi(seed, { failVerify: ['sales_db_password', 'archive_access_key'], keyRingBroken: true }) :
      makeFakeApi(seed);

    const surface = createSecretsAdmin({ host: stage, secretsApi });
    await surface.load();
    ctx.stat('createSecretsAdmin() — values are write-only; verify/disable/delete mutate the fake in-memory store');
    return { dispose() {}, resize() {} };
  },
};
