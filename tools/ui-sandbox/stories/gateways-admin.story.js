// Admin → Data Gateways surface driven by a fixture-backed fake of api/admin/gateways.
import { importFresh } from '../util.js';

function makeFakeApi(seed) {
  let gateways = seed.map((g) => ({
    ...g,
    nodes: g.nodes ? g.nodes.map((n) => ({ ...n })) : []
  }));

  return {
    async list() {
      return gateways.map((g) => ({
        ...g,
        nodes: g.nodes.map((n) => ({ ...n }))
      }));
    },
    async get(id) {
      const gw = gateways.find((g) => g.gatewayId === id);
      if (!gw) {
        const err = new Error(`Gateway '${id}' not found`);
        err.status = 404;
        throw err;
      }
      return { ...gw, nodes: gw.nodes.map((n) => ({ ...n })) };
    },
    async enroll({ gatewayId, expirationMinutes = 60 }) {
      const now = new Date();
      const expires = new Date(now.getTime() + expirationMinutes * 60 * 1000);
      const token = `gw-token-${Math.random().toString(36).substring(2, 10)}${Math.random().toString(36).substring(2, 10)}`;

      const newGw = {
        tenantId: 'default',
        gatewayId: gatewayId,
        issuedBy: 'admin',
        issuedAtUtc: now.toISOString(),
        expiresAtUtc: expires.toISOString(),
        state: 'Pending',
        consumedAtUtc: null,
        consumedByNode: null,
        workloadPublicKeyThumbprint: null,
        revokedAtUtc: null,
        revocationReason: null,
        isOnline: false,
        activeNodes: 0,
        totalNodes: 0,
        nodes: []
      };
      gateways.unshift(newGw);

      return {
        gatewayId: gatewayId,
        oneTimeToken: token,
        expiresAtUtc: expires.toISOString(),
        instructions: 'Run `etlsql gateway setup` on target host to complete enrollment.'
      };
    },
    async revoke(id, reason = 'Revoked by administrator') {
      const gw = gateways.find((g) => g.gatewayId === id);
      if (!gw) {
        const err = new Error(`Gateway '${id}' not found`);
        err.status = 404;
        throw err;
      }
      gw.state = 'Revoked';
      gw.revokedAtUtc = new Date().toISOString();
      gw.revocationReason = reason;
      gw.isOnline = false;
      gw.activeNodes = 0;
      gw.nodes = [];
      return { status: 'ok', gatewayId: id };
    }
  };
}

const healthyFleetSeed = [
  {
    tenantId: 'default',
    gatewayId: 'corp-onprem-gw',
    issuedBy: 'admin',
    issuedAtUtc: '2026-07-01T08:00:00Z',
    expiresAtUtc: '2026-07-01T09:00:00Z',
    state: 'Consumed',
    consumedAtUtc: '2026-07-01T08:12:00Z',
    consumedByNode: 'PROD-GW-01',
    workloadPublicKeyThumbprint: '9f8e7d6c5b4a3928170e1d2c3b4a596877889900',
    revokedAtUtc: null,
    revocationReason: null,
    isOnline: true,
    activeNodes: 2,
    totalNodes: 2,
    nodes: [
      {
        nodeId: 'PROD-GW-01',
        connectedAtUtc: '2026-08-19T06:15:00Z',
        remoteEndpoint: '10.200.14.52:49812',
        status: 'Active'
      },
      {
        nodeId: 'PROD-GW-02',
        connectedAtUtc: '2026-08-19T06:16:30Z',
        remoteEndpoint: '10.200.14.53:51204',
        status: 'Active'
      }
    ]
  },
  {
    tenantId: 'default',
    gatewayId: 'eu-west-gw',
    issuedBy: 'ops-lead',
    issuedAtUtc: '2026-07-15T10:00:00Z',
    expiresAtUtc: '2026-07-15T11:00:00Z',
    state: 'Consumed',
    consumedAtUtc: '2026-07-15T10:20:00Z',
    consumedByNode: 'EU-GW-01',
    workloadPublicKeyThumbprint: '112233445566778899aabbccddeeff0011223344',
    revokedAtUtc: null,
    revocationReason: null,
    isOnline: true,
    activeNodes: 1,
    totalNodes: 1,
    nodes: [
      {
        nodeId: 'EU-GW-01',
        connectedAtUtc: '2026-08-19T08:00:00Z',
        remoteEndpoint: '172.16.40.10:38910',
        status: 'Active'
      }
    ]
  },
  {
    tenantId: 'default',
    gatewayId: 'staging-gw',
    issuedBy: 'admin',
    issuedAtUtc: '2026-08-19T14:00:00Z',
    expiresAtUtc: '2026-08-19T15:00:00Z',
    state: 'Pending',
    consumedAtUtc: null,
    consumedByNode: null,
    workloadPublicKeyThumbprint: null,
    revokedAtUtc: null,
    revocationReason: null,
    isOnline: false,
    activeNodes: 0,
    totalNodes: 0,
    nodes: []
  },
  {
    tenantId: 'default',
    gatewayId: 'legacy-dr-gw',
    issuedBy: 'admin',
    issuedAtUtc: '2026-01-10T11:00:00Z',
    expiresAtUtc: '2026-01-10T12:00:00Z',
    state: 'Revoked',
    consumedAtUtc: '2026-01-10T11:45:00Z',
    consumedByNode: 'OLD-SRV-99',
    workloadPublicKeyThumbprint: 'aabbccddeeff00112233445566778899aabbccdd',
    revokedAtUtc: '2026-06-30T18:00:00Z',
    revocationReason: 'Decommissioned during datacenter migration',
    isOnline: false,
    activeNodes: 0,
    totalNodes: 0,
    nodes: []
  }
];

const singleOfflineSeed = [
  {
    tenantId: 'default',
    gatewayId: 'branch-office-gw',
    issuedBy: 'admin',
    issuedAtUtc: '2026-05-12T09:00:00Z',
    expiresAtUtc: '2026-05-12T10:00:00Z',
    state: 'Consumed',
    consumedAtUtc: '2026-05-12T09:18:00Z',
    consumedByNode: 'BRANCH-SRV',
    workloadPublicKeyThumbprint: '33445566778899001122aabbccddeeff11223344',
    revokedAtUtc: null,
    revocationReason: null,
    isOnline: false,
    activeNodes: 0,
    totalNodes: 0,
    nodes: []
  }
];

export default {
  id: 'gateways-admin',
  title: 'Data Gateways Admin',
  subtitle: 'api/admin/gateways & cluster fleet management',
  category: 'Admin & Operations',
  fixtures: [
    { id: 'healthy-fleet', label: 'Healthy Fleet (Multi-Node Clusters)' },
    { id: 'single-offline', label: 'Offline Gateway' },
    { id: 'empty', label: 'Empty State (No Gateways Enrolled)' }
  ],
  async mount(stage, fixtureId, ctx) {
    const { createGatewaysAdmin } = await importFresh('/src/ETL-SQL.Portal/wwwroot/js/gateways-admin.js');
    stage.classList.add('portal-page');

    const seed =
      fixtureId === 'empty' ? [] :
      fixtureId === 'single-offline' ? singleOfflineSeed :
      healthyFleetSeed;

    const gatewaysApi = makeFakeApi(seed);
    const surface = createGatewaysAdmin({ host: stage, gatewaysApi });
    await surface.load();

    ctx.stat('createGatewaysAdmin() — Active-Active clusters, node health inspection & instant token enrollment');
    return {
      dispose() {},
      resize() {}
    };
  }
};
