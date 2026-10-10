import { createConnectionWizard } from '../designer/connection-wizard.js';
import type { ConnectionWizardHandle, ConnectorSchemaDescriptor as WizardSchema,
  GatewayResource, GatewayCluster, DiagnosticReport as WizardDiagnostic } from '../designer/connection-wizard.js';
import { apiJson, apiFetch } from './portal-http.js';
import { readSecretSummaries } from './secret-responses.js';
import { readConnectorSchemas, readDiscoveredGateways, readGatewayResources, readConnectionDiagnostic } from './gateway-responses.js';
import { readParsedConnection } from './connection-responses.js';

export interface GatewayWizardApi {
  schemas(signal: AbortSignal): Promise<unknown>;
  gateways(signal: AbortSignal): Promise<unknown>;
  secrets(signal: AbortSignal): Promise<unknown>;
  resources(id: string, signal: AbortSignal): Promise<unknown>;
  test(request: TestConnectionRequest, signal: AbortSignal): Promise<unknown>;
  save(alias: string, request: SetConnectionRequest, signal: AbortSignal): Promise<void>;
  parse?(request: ParseConnectionStringRequest, signal: AbortSignal): Promise<unknown>;
}
export const connectionWizardApi: GatewayWizardApi = {
  schemas: signal => apiJson('/api/connectors/schema', { signal }),
  gateways: signal => apiJson('/api/connectors/gateways', { signal }),
  secrets: signal => apiJson('/api/admin/secrets', { signal }),
  resources: (id, signal) => apiJson(`/api/connectors/gateways/${encodeURIComponent(id)}/resources`, { signal }),
  test: (body, signal) => apiJson('/api/connectors/test', { method: 'POST', body, signal }),
  parse: (body, signal) => apiJson('/api/connectors/parse-string', { method: 'POST', body, signal }),
  async save(alias, body, signal) {
    const response = await apiFetch(`/api/admin/connections/${encodeURIComponent(alias)}`, { method: 'PUT', body, signal });
    if (response.status !== 204) throw new Error('Connection save outcome is unconfirmed. Inspect the catalog before retrying.');
  }
};
const resourceForWizard = (resource: GatewayDiscoveredResourceDto): GatewayResource => ({
  ...resource, lastSeenUtc: resource.lastSeenUtc ?? undefined
});
const schemaForWizard = (schema: ConnectorSchemaDescriptor): WizardSchema => ({
  ...schema, options: schema.options.map(option => ({ ...option,
    defaultValue: option.defaultValue ?? undefined, description: option.description ?? undefined,
    mutuallyExclusiveGroup: option.mutuallyExclusiveGroup ?? undefined,
    allowedValues: option.allowedValues ?? undefined
  }))
});
const gatewayForWizard = (gateway: DiscoveredGatewayDto): GatewayCluster => ({
  ...gateway, region: gateway.region ?? undefined, status: gateway.status ?? undefined,
  resources: gateway.resources?.map(resourceForWizard)
});
const diagnosticForWizard = (diagnostic: DiagnosticReport): WizardDiagnostic => ({
  ...diagnostic, steps: diagnostic.steps.map(step => ({ ...step, remedy: step.remedy ?? undefined }))
});

/** Validate server metadata before mounting; the shared wizard's built-in fallback is not fleet evidence. */
export function createConnectionWizardLauncher(host: HTMLElement, onError: (message: string) => void,
  onSaved: (alias: string) => void, api: GatewayWizardApi = connectionWizardApi) {
  let disposed = false, version = 0;
  let request: AbortController | null = null, wizard: ConnectionWizardHandle | null = null;
  function close(): void {
    version++;
    request?.abort(); request = null;
    const previous = wizard; wizard = null; previous?.close();
  }
  async function launch(gatewayId?: string): Promise<void> {
    if (disposed) return;
    close();
    const generation = version, controller = new AbortController();
    request = controller;
    const current = () => !disposed && generation === version && !controller.signal.aborted;
    const ensureCurrent = () => { if (!current()) throw new Error('Connection wizard has closed.'); };
    try {
      const responses = await Promise.all([api.schemas(controller.signal), api.gateways(controller.signal), api.secrets(controller.signal)]);
      if (!current()) return;
      const schemas = readConnectorSchemas(responses[0]), gateways = readDiscoveredGateways(responses[1]);
      const secrets = readSecretSummaries(responses[2]).filter(secret => !secret.disabled).map(secret => secret.name);
      if (!schemas.length || gatewayId && !gateways.some(gateway => gateway.id === gatewayId && gateway.isOnline)) {
        throw new Error('Gateway connection metadata is unavailable.');
      }
      wizard = createConnectionWizard({ host, mode: 'admin', initialGateway: gatewayId,
        schemas: schemas.map(schemaForWizard), gateways: gateways.map(gatewayForWizard), secrets,
        onClose: () => { if (current()) { version++; controller.abort(); request = null; wizard = null; } },
        fetchGatewayResources: async id => {
          ensureCurrent();
          const resources = readGatewayResources(await api.resources(id, controller.signal));
          ensureCurrent(); return resources.map(resourceForWizard);
        },
        onTest: async entry => {
          ensureCurrent();
          const options: Record<string, string> = {};
          for (const [key, value] of Object.entries(entry.options)) {
            if (typeof value !== 'string') throw new TypeError('Connection options must be strings.');
            options[key] = value;
          }
          const body: TestConnectionRequest = { ...entry, options };
          const diagnostic = readConnectionDiagnostic(await api.test(body, controller.signal));
          if (diagnostic.connection !== body.alias || diagnostic.connectorType !== body.connectorType) throw new TypeError('Connection diagnostic does not match the request.');
          ensureCurrent(); return diagnosticForWizard(diagnostic);
        },
        onParseString: api.parse ? async (connectionString, hintProvider) => {
          ensureCurrent();
          const parsed = readParsedConnection(await api.parse!({ connectionString, hintProvider }, controller.signal));
          ensureCurrent();
          return { ...parsed, detectedProvider: parsed.detectedProvider ?? undefined };
        } : undefined,
        onSave: async entry => {
          ensureCurrent();
          await api.save(entry.alias, { connectorType: entry.connectorType, target: entry.target,
            options: entry.options, gateway: entry.gateway, environmentScope: entry.environmentScope,
            sensitiveFields: [] }, controller.signal);
          ensureCurrent(); onSaved(entry.alias);
        }
      });
    } catch {
      if (current()) onError('Connection binding is unavailable. Refresh and verify gateway, connector, and secret metadata.');
    }
  }
  return { launch, close, dispose() { disposed = true; close(); } };
}

export const createGatewayWizardLauncher = createConnectionWizardLauncher;
