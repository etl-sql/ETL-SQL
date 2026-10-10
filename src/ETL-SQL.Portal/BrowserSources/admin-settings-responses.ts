import { isObject } from './http-error.js';

const nullableText = (value: unknown): value is string | null => value === null || typeof value === 'string';

export function readOrchestratorSettings(value: unknown): OrchestratorSettingsDto {
  if (!isObject(value) || !nullableText(value.apiUrl) || typeof value.hasApiKey !== 'boolean')
    throw new TypeError('Orchestrator settings are unavailable or incomplete.');
  return { apiUrl: value.apiUrl, hasApiKey: value.hasApiKey };
}

export function readPortalBranding(value: unknown): PortalBrandingDto {
  if (!isObject(value) || !nullableText(value.displayName) || !nullableText(value.footerText) || !nullableText(value.logoUrl))
    throw new TypeError('Portal branding is unavailable or incomplete.');
  return { displayName: value.displayName, footerText: value.footerText, logoUrl: value.logoUrl };
}

export function readOrchestratorConnectionStatus(value: unknown): OrchestratorConnectionStatusDto {
  if (!isObject(value) || typeof value.online !== 'boolean' || !Object.hasOwn(value, 'status'))
    throw new TypeError('Connection status could not be confirmed.');
  const status = value.status;
  if (!value.online ? status !== 'Offline' : status !== null && (!isObject(status)
    || typeof status.status !== 'string' || !status.status.trim()
    || typeof status.uptimeSeconds !== 'number' || !Number.isFinite(status.uptimeSeconds) || status.uptimeSeconds < 0
    || typeof status.processId !== 'number' || !Number.isSafeInteger(status.processId) || status.processId <= 0
    || typeof status.startedAt !== 'string' || !Number.isFinite(Date.parse(status.startedAt))
    || typeof status.version !== 'string' || !status.version.trim()))
    throw new TypeError('Connection status could not be confirmed.');
  return { online: value.online, status };
}
