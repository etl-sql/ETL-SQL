// Admin -> Policy Authority surface driven by a fixture-backed fake API.
import { importFresh } from '../util.js';

function makeFakeApi({ configured = true, empty = false } = {}) {
  let versions = empty ? [] : [
    {
      tenant: 'acme', environment: 'prod', policyVersion: '2026.07.10.1',
      policyHash: '5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f80',
      issuedAtUtc: '2026-07-10T10:00:00Z', expiresAtUtc: '2026-08-10T10:00:00Z',
      author: 'admin', reviewer: 'steward', rolloutState: 'Superseded',
    },
    {
      tenant: 'acme', environment: 'prod', policyVersion: '2026.07.11.1',
      policyHash: '8a9b0c1d2e3f405162738495a6b7c8d9e0f1a2b3',
      issuedAtUtc: '2026-07-11T09:00:00Z', expiresAtUtc: '2026-08-11T09:00:00Z',
      author: 'admin', reviewer: 'steward', rolloutState: 'Active',
    },
    {
      tenant: 'acme', environment: 'prod', policyVersion: '2026.07.12.1',
      policyHash: 'aabbccddeeff00112233445566778899aabbccdd',
      issuedAtUtc: '2026-07-11T11:30:00Z', expiresAtUtc: '2026-08-12T11:30:00Z',
      author: 'admin', reviewer: null, rolloutState: 'Staged',
    },
    {
      tenant: 'acme', environment: 'prod', policyVersion: '2026.07.12.2-canary',
      policyHash: 'ccddeeff00112233445566778899aabbccddeeff',
      issuedAtUtc: '2026-07-12T08:00:00Z', expiresAtUtc: '2026-08-12T08:00:00Z',
      author: 'admin', reviewer: 'steward', rolloutState: 'Canary',
      canaryGroup: null, canaryPercentage: 10,
    },
  ];
  let machines = empty ? [] : [
    {
      machineId: '11111111111111111111111111111111',
      enrollmentId: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
      tenant: 'acme', environment: 'prod', requiresClientCertificate: true,
      revoked: false, revokedAtUtc: null, revokedReason: null,
      registeredAtUtc: '2026-07-09T14:00:00Z', lastSeenAtUtc: '2026-07-11T12:15:00Z',
    },
    {
      machineId: '22222222222222222222222222222222',
      enrollmentId: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
      tenant: 'acme', environment: 'prod', requiresClientCertificate: false,
      revoked: true, revokedAtUtc: '2026-07-10T20:00:00Z', revokedReason: 'reimaged',
      registeredAtUtc: '2026-07-01T09:00:00Z', lastSeenAtUtc: '2026-07-10T19:55:00Z',
    },
  ];
  const err = (message) => Object.assign(new Error(message), { status: 400, body: { error: message } });
  return {
    async status() {
      return configured
        ? { configured: true, signingPublicKeyPem: '-----BEGIN PUBLIC KEY-----\\n...\\n-----END PUBLIC KEY-----' }
        : { configured: false, error: 'Policy authority signing certificate is not configured.' };
    },
    async validate(policyJson) {
      try {
        JSON.parse(policyJson);
        return { isValid: true, errors: [] };
      } catch (e) {
        return { isValid: false, errors: [e.message] };
      }
    },
    async versions(tenant, environment) {
      return versions.filter((v) => v.tenant === tenant && v.environment === environment);
    },
    async publish(body) {
      if (!configured) throw err('Policy authority signing certificate is not configured.');
      if (!body.policyVersion) throw err('Policy version is required.');
      const row = {
        tenant: body.tenant, environment: body.environment, policyVersion: body.policyVersion,
        policyHash: Math.random().toString(16).slice(2).padEnd(40, '0').slice(0, 40),
        issuedAtUtc: new Date().toISOString(), expiresAtUtc: body.expiresAtUtc,
        author: 'admin', reviewer: body.reviewer, rolloutState: body.staged ? 'Staged' : 'Active',
      };
      if (!body.staged) versions = versions.map((v) => v.rolloutState === 'Active' ? { ...v, rolloutState: 'Superseded' } : v);
      versions.push(row);
      return row;
    },
    async activate(tenant, environment, policyVersion) {
      versions = versions.map((v) => v.tenant === tenant && v.environment === environment
        ? { ...v, rolloutState: v.policyVersion === policyVersion ? 'Active' : (v.rolloutState === 'Active' ? 'Superseded' : v.rolloutState) }
        : v);
      return versions.find((v) => v.policyVersion === policyVersion);
    },
    async rollback(body) {
      const target = versions.find((v) => v.policyVersion === body.targetPolicyVersion);
      if (!target) throw err('Target version was not found.');
      versions = versions.map((v) => v.rolloutState === 'Active' ? { ...v, rolloutState: 'RolledBack' } : v);
      const row = { ...target, policyVersion: body.newPolicyVersion, issuedAtUtc: new Date().toISOString(), expiresAtUtc: body.expiresAtUtc, rolloutState: 'Active' };
      versions.push(row);
      return row;
    },
    async canary(tenant, environment) {
      const c = versions.find((v) => v.tenant === tenant && v.environment === environment && v.rolloutState === 'Canary');
      if (!c) throw err('No canary in progress for this scope.');
      return c;
    },
    async publishCanary(body) {
      if (!configured) throw err('Policy authority signing certificate is not configured.');
      if (!body.policyVersion) throw err('Canary version is required.');
      if (versions.some((v) => v.rolloutState === 'Canary')) throw err('A canary is already in progress; promote or halt it first.');
      const row = {
        tenant: body.tenant, environment: body.environment, policyVersion: body.policyVersion,
        policyHash: Math.random().toString(16).slice(2).padEnd(40, '0').slice(0, 40),
        issuedAtUtc: new Date().toISOString(), expiresAtUtc: body.expiresAtUtc,
        author: 'admin', reviewer: body.reviewer, rolloutState: 'Canary',
        canaryGroup: body.canaryGroup ?? null, canaryPercentage: body.canaryPercentage ?? null,
      };
      versions.push(row);
      return row;
    },
    async promoteCanary(tenant, environment, policyVersion) {
      versions = versions.map((v) => v.tenant === tenant && v.environment === environment
        ? { ...v, rolloutState: v.policyVersion === policyVersion ? 'Active' : (v.rolloutState === 'Active' ? 'Superseded' : v.rolloutState) }
        : v);
      return versions.find((v) => v.policyVersion === policyVersion);
    },
    async haltCanary(tenant, environment, policyVersion) {
      const active = versions.find((v) => v.rolloutState === 'Active');
      versions = versions.map((v) => v.policyVersion === policyVersion ? { ...v, rolloutState: 'RolledBack' } : v);
      versions = versions.map((v) => v.rolloutState === 'Active' ? { ...v, rolloutState: 'Superseded' } : v);
      const row = {
        ...(active || { tenant, environment, author: 'admin', expiresAtUtc: new Date().toISOString() }),
        policyVersion: `${active ? active.policyVersion : '1.0.0'}+halt`,
        policyHash: Math.random().toString(16).slice(2).padEnd(40, '0').slice(0, 40),
        issuedAtUtc: new Date().toISOString(), rolloutState: 'Active',
        canaryGroup: null, canaryPercentage: null,
      };
      versions.push(row);
      return row;
    },
    async machines(tenant, environment) {
      return machines.filter((m) => (!tenant || m.tenant === tenant) && (!environment || m.environment === environment));
    },
    async registerMachine(body) {
      const row = {
        machineId: body.machineId, enrollmentId: body.enrollmentId,
        tenant: body.tenant, environment: body.environment,
        requiresClientCertificate: !!body.clientCertificateThumbprint,
        revoked: false, revokedAtUtc: null, revokedReason: null,
        registeredAtUtc: new Date().toISOString(), lastSeenAtUtc: null,
      };
      machines = machines.filter((m) => m.machineId !== row.machineId).concat(row);
      return row;
    },
    async revokeMachine(machineId, reason) {
      machines = machines.map((m) => m.machineId === machineId
        ? { ...m, revoked: true, revokedAtUtc: new Date().toISOString(), revokedReason: reason }
        : m);
      return machines.find((m) => m.machineId === machineId);
    },
  };
}

export default {
  id: 'policy-authority-admin',
  title: 'Policy Authority Admin',
  subtitle: 'api/admin/policy-authority workflow',
  fixtures: [
    { id: 'configured', label: 'Configured authority' },
    { id: 'unconfigured', label: 'Missing signing cert' },
    { id: 'empty', label: 'Empty scope' },
  ],
  async mount(stage, fixtureId, ctx) {
    const { createPolicyAuthorityAdmin } = await importFresh('/src/ETL-SQL.ReportPortal/wwwroot/js/policy-authority-admin.js');
    stage.classList.add('portal-page');
    const policyAuthorityApi = makeFakeApi({
      configured: fixtureId !== 'unconfigured',
      empty: fixtureId === 'empty',
    });
    const surface = createPolicyAuthorityAdmin({ host: stage, policyAuthorityApi });
    await surface.load();
    ctx.stat('createPolicyAuthorityAdmin() - publish, activate, rollback, canary promote/halt, machine revocation');
    return { dispose() {}, resize() {} };
  },
};
