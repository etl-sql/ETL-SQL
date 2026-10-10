// Admin → Data Gateways surface driven by a fixture-backed fake of api/admin/gateways.
import { importFresh } from '../util.js';

export function makeFakeApi(seed, mode = 'healthy') {
  let gateways = seed.map((g) => ({
    ...g,
    nodes: g.nodes ? g.nodes.map((n) => ({ ...n })) : []
  }));

  const calls = [];
  const queued = [];
  let pause = false;
  function answer(kind, value, args = []) {
    calls.push({ kind, args });
    if (!pause) return Promise.resolve(value);
    pause = false;
    return new Promise(resolve => queued.push(() => resolve(value)));
  }
  return {
    calls,
    snapshot() { return structuredClone(gateways); },
    setFleet(value) { gateways = structuredClone(value); },
    setMode(value) { mode = value; },
    pauseNext() { pause = true; },
    release() { for (const resolve of queued.splice(0)) resolve(); },
    async list() {
      return answer('list', mode === 'unavailable' ? null : mode === 'invalid' ? [{ gatewayId: 'incomplete' }] : structuredClone(gateways));
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
    async enroll(gatewayId, expirationMinutes = 60) {
      const now = new Date();
      const expiresUtc = new Date(now.getTime() + expirationMinutes * 60 * 1000).toISOString();
      const enrollmentId = `fixture-${gateways.length}`;
      gateways.unshift({
        enrollmentId, gatewayId, createdUtc: now.toISOString(), expiresUtc,
        state: 'Pending', consumedUtc: null, workloadPublicKeyThumbprint: null,
        isOnline: false, activeNodes: 0, totalNodes: 0, nodes: [], publishedResources: null
      });
      return answer('enroll', mode === 'invalid-token' ? {} : {
        enrollmentId, gatewayId, tenantId: 'portal-host', oneTimeToken: 'a'.repeat(64), expiresUtc
      }, [gatewayId, expirationMinutes]);
    },
    async revoke(id) {
      const matching = gateways.filter((g) => g.gatewayId === id);
      if (!matching.length) {
        const err = new Error(`Gateway '${id}' not found`);
        err.status = 404;
        throw err;
      }
      for (const gw of matching) {
        gw.state = 'Revoked'; gw.isOnline = false;
        gw.activeNodes = 0; gw.totalNodes = 0; gw.nodes = []; gw.publishedResources = null;
      }
      return answer('revoke', mode === 'invalid-revoke' ? {} : { success: true, gatewayId: id }, [id]);
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

function canonicalSeed(seed) {
  return seed.map((gateway, index) => ({
    enrollmentId: `fixture-${index}-${gateway.gatewayId}`, gatewayId: gateway.gatewayId,
    state: gateway.state, createdUtc: gateway.issuedAtUtc, expiresUtc: gateway.expiresAtUtc,
    consumedUtc: gateway.consumedAtUtc, workloadPublicKeyThumbprint: gateway.workloadPublicKeyThumbprint,
    isOnline: gateway.isOnline, activeNodes: gateway.activeNodes, totalNodes: gateway.totalNodes,
    publishedResources: gateway.isOnline ? [{
      resourceId: 'sales_read', connectorType: 'MSSQL', allowedOperations: 'Read', state: 'Approved',
      isOnline: true, lastSeenUtc: '2026-10-07T12:00:00Z'
    }] : null,
    nodes: gateway.nodes.map(node => ({
      tenantId: 'portal-host', gatewayId: gateway.gatewayId, nodeId: node.nodeId,
      workloadPublicKeyThumbprint: gateway.workloadPublicKeyThumbprint,
      connectedUtc: node.connectedAtUtc, isActive: node.status === 'Active'
    }))
  }));
}
export const gatewayFixtures = { healthy: canonicalSeed(healthyFleetSeed), offline: canonicalSeed(singleOfflineSeed) };

export function makeWizardApi(fleetApi) {
  let invalid = false;
  const calls = [];
  let pause = false, release;
  function answer(kind, value) {
    calls.push({ kind });
    if (!pause) return Promise.resolve(value);
    pause = false;
    return new Promise(resolve => { release = () => { release = null; resolve(value); }; });
  }
  return {
    calls, setInvalid(value) { invalid = value; }, pauseNext() { pause = true; }, release() { release?.(); },
    schemas: () => answer('schemas', invalid ? null : [{ connectorType: 'MSSQL', aliases: [],
      description: 'SQL Server', isFileBased: false, isDataWarehouse: false, commandTimeoutSeconds: 30, options: [] }]),
    gateways: () => answer('gateways', fleetApi.snapshot().map(gateway => ({
      id: gateway.gatewayId, name: gateway.gatewayId, isOnline: gateway.isOnline,
      activeNodes: gateway.activeNodes, totalNodes: gateway.totalNodes, region: null, status: null,
      resources: gateway.publishedResources
    }))),
    secrets: () => answer('secrets', []),
    resources: id => answer('resources', fleetApi.snapshot().find(gateway => gateway.gatewayId === id)?.publishedResources ?? []),
    test: request => answer('test', { connection: request.alias, connectorType: request.connectorType,
      succeeded: true, steps: [{ layer: 'Gateway', status: 'Skipped', detail: 'Fixture diagnostic.', remedy: null }] }),
    save: () => answer('save', undefined)
  };
}

export default {
  id: 'gateways-admin',
  title: 'Data Gateways Admin',
  subtitle: 'api/admin/gateways & cluster fleet management',
  category: 'Admin & Fleet',
  fixtures: [
    { id: 'healthy-fleet', label: 'Healthy Fleet (Multi-Node Clusters)' },
    { id: 'single-offline', label: 'Offline Gateway' },
    { id: 'empty', label: 'Empty State (No Gateways Enrolled)' },
    { id: 'unavailable', label: 'Missing fleet response' },
    { id: 'invalid', label: 'Incomplete fleet response' },
    { id: 'invalid-token', label: 'Unconfirmed enrollment' },
    { id: 'invalid-revoke', label: 'Unconfirmed revocation' }
  ],
  async mount(stage, fixtureId, ctx) {
    const { createGatewaysAdmin } = await importFresh('/src/ETL-SQL.Portal/wwwroot/js/gateways-admin.js');
    stage.classList.add('portal-page');

    const seed =
      fixtureId === 'empty' ? [] :
      fixtureId === 'single-offline' ? gatewayFixtures.offline :
      gatewayFixtures.healthy;

    const gatewaysApi = makeFakeApi(seed, fixtureId);
    const wizardApi = makeWizardApi(gatewaysApi);
    const surface = createGatewaysAdmin({ host: stage, gatewaysApi, wizardApi });
    window.__gateways = { api: gatewaysApi, surface, wizardApi };
    await surface.load();

    ctx.stat('createGatewaysAdmin() — Active-Active clusters, node health inspection & instant token enrollment');
    return {
      dispose() { surface.dispose?.(); delete window.__gateways; },
      resize() {}
    };
  }
};
