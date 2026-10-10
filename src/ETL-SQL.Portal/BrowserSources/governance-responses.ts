import { isObject } from './http-error.js';

const text = (value: unknown): value is string => typeof value === 'string';
const name = (value: unknown): value is string => text(value) && value.trim().length > 0;
const nullableText = (value: unknown) => value === null || text(value);
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const id = (value: unknown): value is number => count(value) && value > 0;
const score = (value: unknown) => count(value) && value <= 100;
const date = (value: unknown) => name(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value: unknown) => value === null || date(value);
const names = (value: unknown): value is string[] => Array.isArray(value) && value.every(name)
  && new Set(value).size === value.length;

function decision(value: unknown): value is GovernanceDecisionDto {
  return isObject(value) && id(value.id) && ['ignore', 'accept-risk', 'reopen'].includes(String(value.decision))
    && nullableText(value.categoryValue) && name(value.reason) && nullableText(value.assetVersion)
    && date(value.decidedAtUtc) && nullableText(value.decidedBy);
}
function finding(value: unknown): value is StewardshipFindingDto {
  return isObject(value) && id(value.id) && name(value.assetKey) && name(value.ruleKey)
    && nullableText(value.assetVersion) && nullableText(value.detail)
    && ['open', 'reopened', 'resolved', 'ignored', 'accepted-risk'].includes(String(value.status))
    && date(value.firstSeenUtc) && date(value.lastSeenUtc) && nullableDate(value.resolvedAtUtc)
    && nullableDate(value.suppressedUntilUtc) && Array.isArray(value.decisions) && value.decisions.every(decision)
    && new Set(value.decisions.map(item => item.id)).size === value.decisions.length;
}
export function readGovernanceFindings(value: unknown): StewardshipFindingDto[] {
  if (!Array.isArray(value) || !value.every(finding) || new Set(value.map(item => item.id)).size !== value.length) {
    throw new TypeError('Governance findings are unavailable or incomplete.');
  }
  return value;
}
export function readGovernanceScan(value: unknown): StewardshipScanDto {
  if (!isObject(value) || !id(value.id) || !name(value.trigger) || !date(value.startedAtUtc)
    || !nullableDate(value.completedAtUtc) || !['running', 'completed', 'failed'].includes(String(value.status))
    || !nullableText(value.error) || !['assetsScanned', 'findingsOpened', 'findingsResolved', 'findingsReopened'].every(key => count(value[key]))) {
    throw new TypeError('Governance scan evidence is unavailable or incomplete.');
  }
  if (value.status !== 'running' && !date(value.completedAtUtc)) {
    throw new TypeError('Governance terminal scan has no completion evidence.');
  }
  return value as unknown as StewardshipScanDto;
}
function asset(value: unknown): value is GovernanceAssetDto {
  return isObject(value) && name(value.assetKey) && name(value.assetVersion)
    && ['scriptPath', 'owner', 'steward', 'domain', 'classification', 'reviewedVersion'].every(key => nullableText(value[key]))
    && score(value.score) && typeof value.governed === 'boolean' && nullableDate(value.reviewedAtUtc)
    && names(value.automaticBadges) && names(value.assignedBadges) && Array.isArray(value.deductions)
    && value.deductions.every(item => isObject(item) && name(item.ruleKey) && count(item.points) && name(item.reason))
    && Array.isArray(value.findings) && value.findings.every(item => finding(item) && item.assetKey === value.assetKey);
}
export function readGovernanceDashboard(value: unknown): GovernanceDashboardDto {
  if (!isObject(value) || !isObject(value.summary) || !Array.isArray(value.assets) || !value.assets.every(asset)
    || new Set(value.assets.map(item => item.assetKey)).size !== value.assets.length
    || !['totalAssets', 'governedAssets', 'belowThreshold', 'openFindings', 'ignoredFindings', 'acceptedRisks'].every(key => count(value.summary && isObject(value.summary) ? value.summary[key] : undefined))
    || !score(value.summary.targetScore)) throw new TypeError('Governance dashboard is unavailable or incomplete.');
  const summary = value.summary;
  if (summary.totalAssets !== value.assets.length || summary.governedAssets !== value.assets.filter(item => item.governed).length
    || summary.belowThreshold !== value.assets.filter(item => !item.governed).length) {
    throw new TypeError('Governance totals do not match the returned asset inventory.');
  }
  if (value.lastScan !== null) readGovernanceScan(value.lastScan);
  return value as unknown as GovernanceDashboardDto;
}
export function readGovernanceCategories(value: unknown): GovernanceCategoryDto[] {
  if (!Array.isArray(value) || !value.every(item => isObject(item) && id(item.id) && name(item.value)
    && name(item.label) && name(item.color) && (item.expiryDays === null || id(item.expiryDays))
    && typeof item.disabled === 'boolean') || new Set(value.map(item => item.value)).size !== value.length) {
    throw new TypeError('Governance categories are unavailable or incomplete.');
  }
  return value as GovernanceCategoryDto[];
}
export function readGovernanceGlossary(value: unknown): StewardshipGlossaryTermDto[] {
  if (!Array.isArray(value) || !value.every(item => isObject(item) && id(item.id) && name(item.term)
    && name(item.dataType) && name(item.aliases) && name(item.description) && nullableText(item.formula)
    && nullableText(item.steward) && typeof item.disabled === 'boolean' && date(item.updatedAtUtc))
    || new Set(value.map(item => item.term)).size !== value.length) {
    throw new TypeError('Governance glossary is unavailable or incomplete.');
  }
  return value as StewardshipGlossaryTermDto[];
}
export function readGovernanceSettings(value: unknown): StewardshipSettingsDto {
  if (!isObject(value) || !['targetScore', 'deductMetadata', 'deductProtectedData', 'deductGlossary', 'deductStaleness'].every(key => score(value[key]))
    || !['enableMetadataCheck', 'enableProtectedDataCheck', 'enableGlossaryCheck', 'enableStalenessCheck'].every(key => typeof value[key] === 'boolean')
    || !id(value.staleAfterDays) || !['visible', 'suggestion', 'scored', 'certification-gate'].includes(String(value.policyLevel))
    || !date(value.updatedAtUtc) || !count(value.version)) throw new TypeError('Governance settings are unavailable or incomplete.');
  return value as unknown as StewardshipSettingsDto;
}
export function readGovernanceQualityRules(value: unknown): DataQualityRuleDefinitionDto[] {
  if (!Array.isArray(value) || !value.every(item => isObject(item) && name(item.jobName) && name(item.targetTable)
    && nullableText(item.targetColumn) && name(item.ruleClause) && name(item.rule) && name(item.action)
    && nullableText(item.sourceFile) && count(item.line))) throw new TypeError('Quality rules are unavailable or incomplete.');
  return value as DataQualityRuleDefinitionDto[];
}

const receiptRequired = (matches: boolean) => {
  if (!matches) throw new TypeError('Governance acknowledgement does not match the requested change.');
};

export class GovernanceScanFailed extends Error {}

export function confirmGovernanceScan(value: unknown, observedId = 0): void {
  const scan = readGovernanceScan(value);
  receiptRequired(scan.trigger === 'manual' && scan.id > observedId);
  if (scan.status === 'failed') throw new GovernanceScanFailed('The scan failed. Refresh to inspect the recorded findings.');
  receiptRequired(scan.status === 'completed' && scan.completedAtUtc !== null);
}
export function confirmGovernanceReview(value: unknown, request: ReviewAssetRequest): void {
  receiptRequired(isObject(value) && value.assetKey === request.assetKey && value.assetVersion === request.assetVersion
    && date(value.reviewedAtUtc));
  // Checked against the reflected server receipt, without inventing a browser-only wire contract.
  const receipt = value as GovernanceAssetReviewDto;
  receiptRequired(name(receipt.assetKey));
}
export function confirmGovernanceBadge(value: unknown, request: AssignBadgeRequest): void {
  receiptRequired(isObject(value) && value.assetKey === request.assetKey && value.badge === request.badge
    && (value.assignedAtUtc === undefined || nullableDate(value.assignedAtUtc)));
  const receipt = value as GovernanceBadgeAssignmentDto;
  receiptRequired(name(receipt.badge));
}
export function confirmGovernanceFinding(value: unknown, findingId: number, request: DecideFindingRequest, observedDecisionIds: readonly number[]): void {
  const receipt = readGovernanceFindings([value])[0];
  const status = request.decision === 'ignore' ? 'ignored' : request.decision === 'accept-risk' ? 'accepted-risk' : 'reopened';
  receiptRequired(receipt.id === findingId && receipt.status === status
    && (request.decision === 'reopen' || receipt.assetVersion === request.assetVersion)
    && receipt.decisions.some(item => !observedDecisionIds.includes(item.id) && item.decision === request.decision
      && item.reason === request.reason && item.categoryValue === (request.categoryValue ?? null)
      && item.assetVersion === request.assetVersion));
}
export function confirmGovernanceSettings(value: unknown, request: UpdateStewardshipSettingsRequest, observedVersion: number): void {
  const receipt = readGovernanceSettings(value);
  receiptRequired(receipt.version > observedVersion && Object.entries(request).every(([key, expected]) =>
    isObject(receipt) && receipt[key] === expected));
}
export function confirmGovernanceCategory(value: unknown, request: SaveGovernanceCategoryRequest): void {
  const receipt = readGovernanceCategories([value])[0];
  receiptRequired(receipt.value === request.value && receipt.label === request.label && receipt.color === request.color
    && receipt.expiryDays === request.expiryDays && receipt.disabled === request.disabled);
}
export function confirmGovernanceGlossary(value: unknown, request: SaveGlossaryTermRequest): void {
  const receipt = readGovernanceGlossary([value])[0];
  receiptRequired(Object.entries(request).every(([key, expected]) => isObject(receipt) && receipt[key] === expected));
}
export function confirmGovernanceNoContent(value: unknown): void { receiptRequired(value === null); }
