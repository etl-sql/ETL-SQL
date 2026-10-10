import { isObject } from './http-error.js';

const text = (value: unknown): value is string => typeof value === 'string';
const name = (value: unknown): value is string => text(value) && value.trim().length > 0;
const nullableText = (value: unknown) => value == null || text(value);
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const date = (value: unknown): value is string => name(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value: unknown) => value == null || date(value);
const fields = (value: unknown): value is string[] => Array.isArray(value) && value.every(name);
const options = (value: unknown): value is Record<string, string> => isObject(value) && Object.entries(value).every(([key, entry]) => name(key) && text(entry));
const gateway = (value: unknown): value is GatewayResourceBinding => isObject(value) && name(value.gatewayId) && name(value.resourceId);
const nullableGateway = (value: unknown) => value == null || gateway(value);

function isSummary(value: unknown): value is PortalSharedConnectionSummary {
  return isObject(value) && name(value.alias) && name(value.connectorType) && typeof value.disabled === 'boolean'
    && nullableText(value.environmentScope) && date(value.createdAtUtc) && date(value.updatedAtUtc)
    && nullableDate(value.lastUsedAtUtc) && nullableDate(value.lastVerifiedAtUtc)
    && count(value.version) && value.version > 0 && nullableGateway(value.gateway);
}
export function readConnectionSummaries(value: unknown): PortalSharedConnectionSummary[] {
  if (!Array.isArray(value) || !value.every(isSummary) || new Set(value.map(entry => entry.alias)).size !== value.length) {
    throw new TypeError('Connection catalog is unavailable or incomplete.');
  }
  return value;
}
export function readConnectionDetail(value: unknown, alias: string): PortalSharedConnectionDetail {
  if (!isObject(value) || !isSummary(value.summary) || value.summary.alias !== alias || !nullableText(value.target)
    || !options(value.options) || !fields(value.sensitiveFields) || !nullableGateway(value.gateway)
    || (value.gateway?.gatewayId ?? null) !== (value.summary.gateway?.gatewayId ?? null)
    || (value.gateway?.resourceId ?? null) !== (value.summary.gateway?.resourceId ?? null)) {
    throw new TypeError('Connection detail is unavailable or incomplete.');
  }
  return value as unknown as PortalSharedConnectionDetail;
}
function isExport(value: unknown): value is PortalSharedConnectionExport {
  return isObject(value) && name(value.alias) && name(value.connectorType) && nullableText(value.target)
    && options(value.options) && nullableText(value.environmentScope) && typeof value.disabled === 'boolean'
    && (value.sensitiveFields == null || fields(value.sensitiveFields)) && nullableGateway(value.gateway);
}
export function readConnectionExports(value: unknown): PortalSharedConnectionExport[] {
  if (!Array.isArray(value) || !value.every(isExport) || new Set(value.map(entry => entry.alias)).size !== value.length) {
    throw new TypeError('Expected complete, unique connection definitions.');
  }
  return value;
}
export function readConnectionVerification(value: unknown, alias: string): SharedConnectionVerificationDto {
  if (!isObject(value) || value.alias !== alias || value.status !== 'ok' || !count(value.secretReferences)) {
    throw new TypeError('Connection verification outcome is unavailable or incomplete.');
  }
  return value as unknown as SharedConnectionVerificationDto;
}
function isStep(value: unknown): value is SharedConnectionDiagnosticStepDto {
  return isObject(value) && name(value.layer) && ['ok', 'failed', 'skipped', 'denied'].includes(String(value.status))
    && text(value.detail) && nullableText(value.remedy);
}
export function readSharedConnectionDiagnostic(value: unknown, alias: string): SharedConnectionDiagnosticDto {
  if (!isObject(value) || value.alias !== alias || typeof value.succeeded !== 'boolean'
    || !Array.isArray(value.steps) || !value.steps.length || !value.steps.every(isStep)
    || value.succeeded !== value.steps.every(step => step.status === 'ok' || step.status === 'skipped')) {
    throw new TypeError('Connection diagnostic outcome is unavailable or incomplete.');
  }
  return value as unknown as SharedConnectionDiagnosticDto;
}
export function readConnectionImpact(value: unknown, alias: string): ImpactReport {
  const consumer = (entry: unknown): entry is ImpactConsumer => isObject(entry) && name(entry.type) && name(entry.name)
    && nullableText(entry.detail) && nullableDate(entry.lastUsedAtUtc) && (entry.useCount == null || count(entry.useCount));
  if (!isObject(value) || value.reference !== `SHARED:${alias}` || !count(value.consumerCount)
    || !Array.isArray(value.consumers) || !value.consumers.every(consumer) || value.consumerCount !== value.consumers.length) {
    throw new TypeError('Connection impact is unavailable or incomplete.');
  }
  return value as unknown as ImpactReport;
}
export function readConnectionImport(value: unknown, expected: number): SharedConnectionImportDto {
  if (!isObject(value) || !count(value.created) || !count(value.updated) || value.created + value.updated !== expected) {
    throw new TypeError('Connection import outcome is unconfirmed.');
  }
  return value as unknown as SharedConnectionImportDto;
}
export function readConnectionHelp(value: unknown): ConnectionHelpDto {
  if (!isObject(value) || !name(value.content)) throw new TypeError('Connector documentation is unavailable.');
  return { content: value.content };
}
export function readParsedConnection(value: unknown): ParsedConnectionStringResult {
  if (!isObject(value) || !nullableText(value.detectedProvider) || !options(value.options)
    || !nullableText(value.extractedCredential) || !nullableText(value.suggestedSecretKey)) {
    throw new TypeError('Connection string parsing is unavailable.');
  }
  return value as unknown as ParsedConnectionStringResult;
}

/** Report the line number without copying a possible credential into feedback. */
export function parseConnectionOptions(value: string): Record<string, string> {
  const result: Record<string, string> = Object.create(null);
  for (const [index, line] of value.split('\n').entries()) {
    if (!line.trim()) continue;
    const separator = line.indexOf('='), key = line.slice(0, separator).trim().toUpperCase();
    if (separator <= 0 || !key) throw new TypeError(`Option line ${index + 1} must use KEY=VALUE.`);
    if (key in result) throw new TypeError(`Option line ${index + 1} repeats an existing key.`);
    if (key === '__GATEWAY_ID' || key === '__GATEWAY_RESOURCE_ID') throw new TypeError('Use the Connection Wizard to select a gateway resource.');
    result[key] = line.slice(separator + 1).trim();
  }
  return result;
}
