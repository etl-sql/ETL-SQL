import { isObject } from './http-error.js';

const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const text = (value: unknown): value is string => typeof value === 'string';
const nonempty = (value: unknown): value is string => text(value) && !!value.trim();
const date = (value: unknown): value is string => text(value) && Number.isFinite(Date.parse(value));
const nullableText = (value: unknown) => value == null || text(value);
export type AdminReportInput = Pick<PublishReportRequest, 'name' | 'folderId' | 'description' | 'scriptPath'>;
export type AdminReportTarget = Pick<AdminReportDto, 'id' | 'version'>;
export type AdminReportReceipt = Pick<ReportDto, 'id' | 'folderId' | 'name' | 'scriptPath' | 'description' | 'version'>;
export type AdminReportValidation = Pick<ReportScriptValidationDto, 'isValid' | 'scriptPath' | 'hash' | 'lastModified' | 'metadata' | 'parameters' | 'errors'>;

function isReport(value: unknown): value is AdminReportDto {
  return isObject(value) && positive(value.id) && positive(value.folderId) && positive(value.version)
    && nonempty(value.name) && nullableText(value.description) && nonempty(value.scriptPath)
    && nonempty(value.folderName) && nonempty(value.folderPath) && value.folderPath.startsWith('/')
    && date(value.createdAt) && date(value.updatedAt);
}
export function readAdminReportCatalog(value: unknown): AdminReportDto[] {
  if (!Array.isArray(value) || !value.every(isReport) || new Set(value.map(item => item.id)).size !== value.length)
    throw new TypeError('The report catalog is unavailable or incomplete.');
  return value;
}
// The server resolves a relative script key to its configured physical root.
// Compare normalized components, retaining POSIX casing and Windows casing semantics.
export function matchesReportScript(actual: string, requested: string): boolean {
  const normalize = (path: string) => {
    const parts: string[] = [];
    for (const part of path.replaceAll('\\', '/').split('/')) {
      if (!part || part === '.') continue;
      if (part === '..' && parts.length && parts.at(-1) !== '..') parts.pop();
      else parts.push(part);
    }
    return parts.join('/');
  };
  let result = normalize(actual), input = normalize(requested);
  if (/^[a-z]:[\\/]|^\\\\/i.test(actual)) { result = result.toLowerCase(); input = input.toLowerCase(); }
  const rooted = /^[a-z]:[\\/]|^[\\/]/i.test(requested);
  return result === input || (!rooted && !!input && result.endsWith('/' + input));
}
export function readAdminReportIdentity(value: unknown, id: number | null): AdminReportReceipt {
  if (!isObject(value) || !positive(value.id) || (id !== null && value.id !== id) || !positive(value.folderId)
    || !positive(value.version) || !nonempty(value.name) || !nonempty(value.scriptPath) || !nullableText(value.description))
    throw new TypeError('The report response is unavailable or does not match its target.');
  return { id: value.id, folderId: value.folderId, name: value.name, scriptPath: value.scriptPath,
    description: text(value.description) ? value.description : null, version: value.version };
}
export function readAdminReportReceipt(value: unknown, body: AdminReportInput, previous: AdminReportTarget | null): AdminReportReceipt {
  const receipt = readAdminReportIdentity(value, previous?.id ?? null);
  if (receipt.name !== body.name || receipt.folderId !== body.folderId || !matchesReportScript(receipt.scriptPath, body.scriptPath)
    || (previous ? receipt.version <= previous.version : receipt.version !== 1)
    || (body.description?.trim() && receipt.description !== body.description))
    throw new TypeError('The report change was not confirmed by its receipt.');
  return receipt;
}
function isParameter(value: unknown): value is ReportParameterDto {
  return isObject(value) && nonempty(value.name) && nonempty(value.type) && typeof value.required === 'boolean'
    && nullableText(value.default) && nullableText(value.description);
}
export function readAdminReportValidation(value: unknown, scriptPath: string): AdminReportValidation {
  if (!isObject(value) || typeof value.isValid !== 'boolean' || !nonempty(value.scriptPath)
    || !matchesReportScript(value.scriptPath, scriptPath) || !nullableText(value.hash)
    || !(value.lastModified == null || date(value.lastModified)) || !isObject(value.metadata)
    || !Object.values(value.metadata).every(text) || !Array.isArray(value.parameters) || !value.parameters.every(isParameter)
    || new Set(value.parameters.map(parameter => parameter.name)).size !== value.parameters.length
    || !Array.isArray(value.errors) || !value.errors.every(nonempty)
    || (value.isValid ? !text(value.hash) || !/^sha256:[0-9a-f]{64}$/i.test(value.hash) || value.errors.length !== 0 : value.errors.length === 0))
    throw new TypeError('The script validation is unavailable, incomplete, or belongs to another path.');
  return { isValid: value.isValid, scriptPath: value.scriptPath, hash: text(value.hash) ? value.hash : null,
    lastModified: date(value.lastModified) ? value.lastModified : null, metadata: value.metadata as Record<string, string>,
    parameters: value.parameters, errors: value.errors };
}
export function readAdminReportScripts(value: unknown): string[] {
  if (!Array.isArray(value) || !value.every((path): path is string => nonempty(path) && /\.rptsql$/i.test(path))
    || new Set(value).size !== value.length) throw new TypeError('The available-script catalog is unavailable or incomplete.');
  return value;
}
