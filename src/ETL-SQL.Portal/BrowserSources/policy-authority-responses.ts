import { isObject } from './http-error.js';

export interface PolicyScope { tenant: string; environment: string }
const text = (value: unknown): value is string => typeof value === 'string';
const name = (value: unknown): value is string => text(value) && value.trim().length > 0;
const date = (value: unknown): value is string => name(value) && Number.isFinite(Date.parse(value));
const optionalText = (value: unknown) => value == null || text(value);
const optionalDate = (value: unknown) => value == null || date(value);
const sameScope = (value: PolicyScope, scope: PolicyScope) => value.tenant === scope.tenant && value.environment === scope.environment;

export function readPolicyAuthorityStatus(value: unknown): PolicyAuthorityStatusDto {
  if (!isObject(value) || typeof value.configured !== 'boolean'
    || !optionalText(value.error) || !optionalText(value.signingPublicKeyPem)
    || value.configured && !name(value.signingPublicKeyPem) || !value.configured && !name(value.error)) {
    throw new TypeError('Policy signer status is unavailable or incomplete.');
  }
  return { configured: value.configured, signingPublicKeyPem: value.signingPublicKeyPem as string | null | undefined,
    error: value.error as string | null | undefined };
}
function isValidation(value: unknown): value is PolicyValidationDto {
  return isObject(value) && typeof value.isValid === 'boolean' && Array.isArray(value.errors)
    && value.errors.every(name) && value.isValid === (value.errors.length === 0);
}
export function readPolicyValidation(value: unknown): PolicyValidationDto {
  if (!isValidation(value)) throw new TypeError('Policy validation is unavailable or incomplete.');
  return value;
}
function isVersion(value: unknown): value is PolicyVersionDto {
  if (!isObject(value) || !['tenant', 'environment', 'policyVersion', 'author'].every(field => name(value[field]))
    || !text(value.policyHash) || !/^[a-f0-9]{64}$/i.test(value.policyHash)
    || !['Staged', 'Active', 'Superseded', 'RolledBack', 'Canary'].includes(String(value.rolloutState))
    || !['issuedAtUtc', 'expiresAtUtc', 'publishedAtUtc'].every(field => date(value[field]))
    || !['reviewer', 'supersededVersion', 'canaryGroup'].every(field => optionalText(value[field]))) return false;
  const group = value.canaryGroup != null, percentage = value.canaryPercentage != null;
  return (!group || name(value.canaryGroup))
    && (!percentage || typeof value.canaryPercentage === 'number' && Number.isInteger(value.canaryPercentage)
      && value.canaryPercentage >= 1 && value.canaryPercentage <= 100)
    && !(group && percentage) && (value.rolloutState !== 'Canary' || group !== percentage);
}
export function readPolicyVersions(value: unknown, scope: PolicyScope): PolicyVersionDto[] {
  if (!Array.isArray(value) || !value.every(isVersion) || !value.every(version => sameScope(version, scope))
    || new Set(value.map(version => version.policyVersion)).size !== value.length) {
    throw new TypeError('Policy versions are unavailable or incomplete for this scope.');
  }
  return value;
}
export function readPolicyVersionReceipt(value: unknown, scope: PolicyScope, state: string, version?: string): PolicyVersionDto {
  if (!isVersion(value) || !sameScope(value, scope) || value.rolloutState !== state || version !== undefined && value.policyVersion !== version) {
    throw new TypeError('Policy outcome is unconfirmed. Refresh and inspect this scope before retrying.');
  }
  return value;
}
const guid = (value: unknown): value is string => text(value) && /^[a-f0-9]{32}$/i.test(value);
function isMachine(value: unknown): value is PolicyMachineDto {
  return isObject(value) && guid(value.machineId) && guid(value.enrollmentId) && name(value.tenant) && name(value.environment)
    && typeof value.requiresClientCertificate === 'boolean' && typeof value.revoked === 'boolean' && date(value.registeredAtUtc)
    && optionalDate(value.revokedAtUtc) && optionalDate(value.lastSeenAtUtc)
    && optionalText(value.revokedReason) && optionalText(value.canaryGroup);
}
export function readPolicyMachines(value: unknown, scope: PolicyScope): PolicyMachineDto[] {
  if (!Array.isArray(value) || !value.every(isMachine) || !value.every(machine => sameScope(machine, scope))
    || new Set(value.map(machine => machine.machineId)).size !== value.length) {
    throw new TypeError('Policy machines are unavailable or incomplete for this scope.');
  }
  return value;
}
export function readPolicyMachineReceipt(value: unknown, scope: PolicyScope, machineId: string, revoked: boolean): PolicyMachineDto {
  if (!isMachine(value) || !sameScope(value, scope) || value.machineId !== machineId || value.revoked !== revoked) {
    throw new TypeError('Machine outcome is unconfirmed. Refresh and inspect this scope before retrying.');
  }
  return value;
}
