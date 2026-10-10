import { isObject } from './http-error.js';

const text = (value: unknown): value is string => typeof value === 'string';
const name = (value: unknown): value is string => text(value) && value.trim().length > 0;
const date = (value: unknown): value is string => name(value) && Number.isFinite(Date.parse(value));
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const optionalText = (value: unknown) => value == null || text(value);
const optionalDate = (value: unknown) => value == null || date(value);
export type GatewayFleetEntry = GatewayEnrollmentDto & { nodes: GatewaySessionInfo[] };

function isResource(value: unknown): value is GatewayDiscoveredResourceDto {
  return isObject(value) && ['resourceId', 'connectorType', 'allowedOperations'].every(field => name(value[field]))
    && value.state === 'Approved' && typeof value.isOnline === 'boolean' && optionalDate(value.lastSeenUtc);
}
function isNode(value: unknown): value is GatewaySessionInfo {
  return isObject(value) && ['tenantId', 'gatewayId', 'nodeId', 'workloadPublicKeyThumbprint'].every(field => name(value[field]))
    && date(value.connectedUtc) && typeof value.isActive === 'boolean';
}
function isEnrollment(value: unknown): value is GatewayFleetEntry {
  if (!isObject(value) || !name(value.enrollmentId) || !name(value.gatewayId)
    || !['Pending', 'Consumed', 'Revoked'].includes(String(value.state))
    || !date(value.createdUtc) || !date(value.expiresUtc) || !optionalDate(value.consumedUtc)
    || !optionalText(value.workloadPublicKeyThumbprint) || typeof value.isOnline !== 'boolean'
    || !count(value.activeNodes) || !count(value.totalNodes) || !Array.isArray(value.nodes) || !value.nodes.every(isNode)
    || value.totalNodes !== value.nodes.length || value.activeNodes !== value.nodes.filter(node => node.isActive).length
    || value.isOnline !== (value.activeNodes > 0)
    || value.publishedResources != null && (!Array.isArray(value.publishedResources) || !value.publishedResources.every(isResource))) return false;
  return value.nodes.every(node => node.gatewayId === value.gatewayId)
    && new Set(value.nodes.map(node => node.tenantId)).size <= 1
    && new Set(value.nodes.map(node => node.nodeId.toUpperCase())).size === value.nodes.length;
}
export function readGatewayFleet(value: unknown): GatewayFleetEntry[] {
  if (!Array.isArray(value) || !value.every(isEnrollment)
    || new Set(value.map(enrollment => enrollment.enrollmentId)).size !== value.length
    || new Set(value.flatMap(enrollment => enrollment.nodes.map(node => node.tenantId))).size > 1) {
    throw new TypeError('Gateway fleet metadata is unavailable or incomplete.');
  }
  return value;
}
/** Match the durable store's current enrollment choice; history rows are not extra clusters. */
export function currentGatewayFleet(enrollments: GatewayFleetEntry[]): GatewayFleetEntry[] {
  const groups = new Map<string, GatewayFleetEntry>();
  for (const entry of enrollments) {
    const existing = groups.get(entry.gatewayId);
    if (!existing || (entry.state === 'Consumed' && existing.state !== 'Consumed')
      || (entry.state === 'Consumed') === (existing.state === 'Consumed') && Date.parse(entry.createdUtc) > Date.parse(existing.createdUtc)) {
      groups.set(entry.gatewayId, entry);
    }
  }
  return [...groups.values()];
}
function isIssued(value: unknown): value is IssuedGatewayEnrollmentDto {
  return isObject(value) && ['enrollmentId', 'gatewayId', 'tenantId'].every(field => name(value[field]))
    && typeof value.oneTimeToken === 'string' && /^[a-zA-Z0-9_-]{32,}$/.test(value.oneTimeToken) && date(value.expiresUtc);
}
export function readIssuedGateway(value: unknown, gatewayId: string, now = Date.now()): IssuedGatewayEnrollmentDto {
  if (!isIssued(value) || value.gatewayId !== gatewayId || Date.parse(value.expiresUtc) <= now) {
    throw new TypeError('Enrollment outcome is unconfirmed. Refresh and inspect the gateway before issuing another token.');
  }
  return value;
}
export function readGatewayRevocation(value: unknown, gatewayId: string): GatewayRevocationDto {
  if (!isObject(value) || value.success !== true || value.gatewayId !== gatewayId) {
    throw new TypeError('Gateway revocation is unconfirmed. Refresh before retrying.');
  }
  return { success: true, gatewayId };
}
export function readGatewayResources(value: unknown): GatewayDiscoveredResourceDto[] {
  if (!Array.isArray(value) || !value.every(isResource)
    || new Set(value.map(resource => resource.resourceId)).size !== value.length) throw new TypeError('Gateway resources are unavailable or incomplete.');
  return value;
}
function isDiscoveredGateway(value: unknown): value is DiscoveredGatewayDto {
  return isObject(value) && name(value.id) && name(value.name) && typeof value.isOnline === 'boolean'
    && count(value.activeNodes) && count(value.totalNodes) && value.activeNodes <= value.totalNodes
    && value.isOnline === (value.activeNodes > 0) && optionalText(value.region) && optionalText(value.status)
    && (value.resources == null || Array.isArray(value.resources) && value.resources.every(isResource));
}
export function readDiscoveredGateways(value: unknown): DiscoveredGatewayDto[] {
  if (!Array.isArray(value) || !value.every(isDiscoveredGateway)) throw new TypeError('Gateway discovery is unavailable or incomplete.');
  // Discovery currently includes every non-revoked enrollment; newest entries arrive first.
  const current = new Map<string, DiscoveredGatewayDto>();
  for (const gateway of value) if (!current.has(gateway.id)) current.set(gateway.id, gateway);
  return [...current.values()];
}
function isOption(value: unknown): value is ConnectorOptionDescriptor {
  return isObject(value) && name(value.name) && typeof value.type === 'number' && [0, 1, 2, 3, 4, 5].includes(value.type)
    && typeof value.isMandatory === 'boolean' && text(value.category)
    && ['defaultValue', 'description', 'mutuallyExclusiveGroup'].every(field => optionalText(value[field]))
    && (value.allowedValues == null || Array.isArray(value.allowedValues) && value.allowedValues.every(text));
}
function isSchema(value: unknown): value is ConnectorSchemaDescriptor {
  return isObject(value) && name(value.connectorType) && Array.isArray(value.aliases) && value.aliases.every(text)
    && text(value.description) && typeof value.isFileBased === 'boolean' && typeof value.isDataWarehouse === 'boolean'
    && count(value.commandTimeoutSeconds) && Array.isArray(value.options) && value.options.every(isOption);
}
export function readConnectorSchemas(value: unknown): ConnectorSchemaDescriptor[] {
  if (!Array.isArray(value) || !value.every(isSchema) || new Set(value.map(schema => schema.connectorType)).size !== value.length) {
    throw new TypeError('Connector metadata is unavailable or incomplete.');
  }
  return value;
}
function isDiagnosticStep(value: unknown): value is DiagnosticStep {
  return isObject(value) && text(value.layer) && ['Ok', 'Failed', 'Skipped', 'Denied'].includes(String(value.status))
    && text(value.detail) && optionalText(value.remedy);
}
function isDiagnostic(value: unknown): value is DiagnosticReport {
  return isObject(value) && text(value.connection) && name(value.connectorType) && typeof value.succeeded === 'boolean'
    && Array.isArray(value.steps) && value.steps.every(isDiagnosticStep)
    && value.succeeded === value.steps.every(step => step.status === 'Ok' || step.status === 'Skipped');
}
export function readConnectionDiagnostic(value: unknown): DiagnosticReport {
  if (!isDiagnostic(value)) throw new TypeError('Connection diagnostic outcome is unavailable or incomplete.');
  return value;
}
