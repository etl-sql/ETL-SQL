import { isObject } from './http-error.js';
import type { DesignerSnapshotPackage, DesignerState } from '../designer/designer-context.js';

const string = (value: unknown): value is string => typeof value === 'string';
const boolean = (value: unknown): value is boolean => typeof value === 'boolean';
const integer = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value);
const number = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
const date = (value: unknown): value is string => string(value) && Number.isFinite(Date.parse(value));
function nullable<T>(value: unknown, guard: (value: unknown) => value is T): value is T | null | undefined {
    return value == null || guard(value);
}
function array<T>(value: unknown, guard: (value: unknown) => value is T): value is T[] {
    return Array.isArray(value) && value.every(guard);
}
function dictionary<T>(value: unknown, guard: (value: unknown) => value is T): value is Record<string, T> {
    return isObject(value) && Object.values(value).every(guard);
}
function optionalStrings(value: Record<string, unknown>, fields: readonly string[]): boolean {
    return fields.every(field => nullable(value[field], string));
}
function version(value: unknown): value is number { return integer(value) && value > 0; }

export class PortalAuthoringError extends Error {
    readonly status: number;
    readonly payload: unknown;
    constructor(status: number, payload: unknown, fallback: string) {
        super(isObject(payload) && string(payload.error) ? payload.error : fallback);
        this.status = status;
        this.payload = payload;
    }
}

export function readStudioSession(value: unknown): StudioSessionDto {
    if (!isObject(value) || !string(value.mode) || !array(value.capabilities, string)
        || !boolean(value.sourceControlEnabled) || !boolean(value.draftRecovery))
        throw new TypeError('Invalid Studio session response.');
    return { mode: value.mode, capabilities: value.capabilities,
        sourceControlEnabled: value.sourceControlEnabled, draftRecovery: value.draftRecovery,
        privateDrafts: value.privateDrafts === true,
        maxScriptCharacters: integer(value.maxScriptCharacters) && value.maxScriptCharacters > 0 ? value.maxScriptCharacters : 200_000 };
}

function studioPrivateDraftSummary(value: unknown): value is StudioPrivateDraftSummaryDto {
    return isObject(value) && integer(value.id) && string(value.name) && string(value.kind)
        && version(value.version) && date(value.updatedAt) && date(value.expiresAt);
}
export function readStudioPrivateDrafts(value: unknown): StudioPrivateDraftSummaryDto[] {
    if (!array(value, studioPrivateDraftSummary)) throw new TypeError('Invalid private draft list response.');
    return value.map(draft => ({ id: draft.id, name: draft.name, kind: draft.kind, version: draft.version,
        updatedAt: draft.updatedAt, expiresAt: draft.expiresAt }));
}
export function readStudioPrivateDraft(value: unknown): StudioPrivateDraftDto {
    if (!studioPrivateDraftSummary(value) || !string((value as { content?: unknown }).content))
        throw new TypeError('Invalid private draft response.');
    const draft = value as StudioPrivateDraftDto;
    return { id: draft.id, name: draft.name, kind: draft.kind, version: draft.version, content: draft.content,
        updatedAt: draft.updatedAt, expiresAt: draft.expiresAt };
}

function studioReport(value: unknown): value is StudioReportDto {
    return isObject(value) && integer(value.id) && integer(value.folderId) && string(value.folderPath)
        && string(value.name) && nullable(value.description, string) && date(value.updatedAt)
        && version(value.version) && string(value.kind);
}
export function readStudioReport(value: unknown): StudioReportDto {
    if (!studioReport(value)) throw new TypeError('Invalid Studio report response.');
    return value;
}
export function readStudioReports(value: unknown): StudioReportDto[] {
    if (!array(value, studioReport)) throw new TypeError('Invalid Studio report catalog response.');
    return value;
}
function studioFolder(value: unknown): value is StudioFolderDto {
    return isObject(value) && integer(value.id) && string(value.path) && string(value.name);
}
export function readStudioCatalogPage(value: unknown): StudioCatalogPageDto & { nextAfterId: number | null } {
    if (!isObject(value) || !nullable(value.nextAfterId, integer)) throw new TypeError('Invalid Studio catalog page response.');
    const items = readStudioReports(value.items);
    if (items.length > 50) throw new TypeError('Studio catalog page exceeded its limit.');
    return { items, nextAfterId: value.nextAfterId ?? null };
}
export function readStudioFolders(value: unknown): StudioFolderDto[] {
    if (!array(value, studioFolder)) throw new TypeError('Invalid Studio folder catalog response.');
    return value;
}
export function readDesignerReport(value: unknown): Pick<ReportDto, 'id' | 'name' | 'folderId' | 'version'> {
    if (!isObject(value) || !integer(value.id) || !integer(value.folderId) || !string(value.name) || !version(value.version))
        throw new TypeError('Invalid designer report response.');
    return { id: value.id, folderId: value.folderId, name: value.name, version: value.version };
}
export function readScriptContent(value: unknown): ScriptContentResponse {
    if (!isObject(value) || !string(value.scriptText) || !version(value.version)
        || !nullable(value.sourceRevision, string) || !boolean(value.sourceControlEnabled))
        throw new TypeError('Invalid report script response.');
    return { scriptText: value.scriptText, version: value.version,
        sourceRevision: value.sourceRevision, sourceControlEnabled: value.sourceControlEnabled };
}
export function readDesignerLease(value: unknown): DesignerLeaseResponse {
    if (!isObject(value) || !boolean(value.acquired) || !string(value.owner) || !date(value.expiresAt))
        throw new TypeError('Invalid report edit-lease response.');
    return { acquired: value.acquired, owner: value.owner, expiresAt: value.expiresAt };
}
export function readDesignerSave(value: unknown): SaveDesignerResponse {
    if (!isObject(value) || !version(value.version) || !nullable(value.sourceRevision, string))
        throw new TypeError('Invalid report save response.');
    return { version: value.version, sourceRevision: value.sourceRevision };
}
export function readStudioDraft(value: unknown): StudioRecoveryDraftDto {
    if (!isObject(value) || !string(value.content) || !nullable(value.baseVersion, version)
        || !nullable(value.baseSourceRevision, string) || !date(value.updatedAt))
        throw new TypeError('Invalid report recovery-draft response.');
    return { content: value.content, baseVersion: value.baseVersion,
        baseSourceRevision: value.baseSourceRevision, updatedAt: value.updatedAt };
}

export function readPipelineRunSubmitted(value: unknown): PipelineRunSubmittedDto {
    if (!isObject(value) || !version(value.runId) || !integer(value.pipelineId) || !version(value.version)
        || !string(value.scriptHash) || !string(value.jobName))
        throw new TypeError('Invalid pipeline run submission response.');
    return { runId: value.runId, pipelineId: value.pipelineId, version: value.version,
        scriptHash: value.scriptHash, jobName: value.jobName };
}
export function readPipelineRunCancelRequested(value: unknown): PipelineRunCancelRequestedDto {
    if (!isObject(value) || !version(value.runId) || !integer(value.pipelineId))
        throw new TypeError('Invalid pipeline run cancellation response.');
    return { runId: value.runId, pipelineId: value.pipelineId };
}
export function readPipelineSchedule(value: unknown): PipelineScheduleDto {
    if (!isObject(value) || !integer(value.pipelineId) || !boolean(value.scheduled) || !boolean(value.enabled)
        || !nullable(value.cron, string) || !nullable(value.timeZone, string) || !nullable(value.nextRun, date)
        || !nullable(value.lastRun, date) || !nullable(value.scheduledScriptHash, string)
        || !boolean(value.scheduledRevisionIsSaved) || !version(value.savedVersion))
        throw new TypeError('Invalid pipeline schedule response.');
    return { pipelineId: value.pipelineId, scheduled: value.scheduled, cron: value.cron, timeZone: value.timeZone,
        enabled: value.enabled, nextRun: value.nextRun, lastRun: value.lastRun,
        scheduledScriptHash: value.scheduledScriptHash, scheduledRevisionIsSaved: value.scheduledRevisionIsSaved,
        savedVersion: value.savedVersion };
}
function pipelineRunStatement(value: unknown): value is PipelineRunStatementDto {
    return isObject(value) && string(value.statement) && number(value.durationMs)
        && number(value.rowsProcessed) && boolean(value.failed);
}
export function readPipelineRun(value: unknown): PipelineRunDto {
    if (!isObject(value) || !version(value.runId) || !integer(value.pipelineId) || !string(value.status)
        || !boolean(value.isTerminal) || !date(value.startTime) || !nullable(value.endTime, date)
        || !nullable(value.errorMessage, string) || !number(value.rowsProcessed)
        || !nullable(value.scriptHash, string) || !boolean(value.logTruncated)
        || !array(value.statements, pipelineRunStatement))
        throw new TypeError('Invalid pipeline run response.');
    return { runId: value.runId, pipelineId: value.pipelineId, status: value.status,
        isTerminal: value.isTerminal, startTime: value.startTime, endTime: value.endTime,
        errorMessage: value.errorMessage, rowsProcessed: value.rowsProcessed, scriptHash: value.scriptHash,
        logTruncated: value.logTruncated, statements: value.statements };
}

function textFormatting(value: unknown): value is DesignerTextFormattingDto {
    return isObject(value) && optionalStrings(value, ['text', 'color', 'font', 'size', 'weight', 'align']);
}
function fieldFormatting(value: unknown): value is DesignerFieldFormattingDto {
    return isObject(value) && boolean(value.dataBar)
        && optionalStrings(value, ['format', 'align', 'displayName', 'dataBarColor', 'colorScaleFrom', 'colorScaleTo']);
}
function conditionalRule(value: unknown): value is DesignerConditionalFormattingRuleDto {
    return isObject(value) && string(value.condition) && string(value.backgroundColor) && nullable(value.fontColor, string);
}
function formatting(value: unknown): value is DesignerVisualFormattingDto {
    return isObject(value) && nullable(value.title, textFormatting) && nullable(value.subtitle, textFormatting)
        && nullable(value.xAxis, v => dictionary(v, string)) && nullable(value.yAxis, v => dictionary(v, string))
        && nullable(value.palette, v => array(v, string)) && nullable(value.conditionalRules, v => array(v, conditionalRule))
        && nullable(value.fields, v => dictionary(v, fieldFormatting));
}
function visual(value: unknown): value is DesignerVisualDto {
    return isObject(value) && ['id', 'name', 'type'].every(key => string(value[key]))
        && ['gridCol', 'gridRow', 'gridColSpan', 'gridRowSpan'].every(key => integer(value[key]))
        && optionalStrings(value, ['title', 'dataset', 'containerId']) && dictionary(value.mappings, string)
        && dictionary(value.options, string) && nullable(value.formatting, formatting);
}
function layout(value: unknown): value is DesignerPageLayoutDto {
    return isObject(value) && optionalStrings(value, ['pageSize', 'orientation', 'units', 'overflow'])
        && ['marginTop', 'marginRight', 'marginBottom', 'marginLeft', 'customWidth', 'customHeight']
            .every(key => nullable(value[key], number));
}
function page(value: unknown): value is DesignerPageDto {
    return isObject(value) && ['id', 'name', 'mode'].every(key => string(value[key]))
        && array(value.visuals, visual) && nullable(value.printLayout, layout);
}
function dataset(value: unknown): value is DesignerDatasetDto {
    return isObject(value) && ['id', 'name', 'query'].every(key => string(value[key])) && nullable(value.ttl, string);
}
function bookmark(value: unknown): value is DesignerBookmarkDto {
    return isObject(value) && string(value.id) && string(value.name) && boolean(value.isDefault)
        && optionalStrings(value, ['title', 'page'])
        && nullable(value.parameters, v => array(v, (p): p is DesignerBookmarkParameterDto =>
            isObject(p) && string(p.name) && string(p.value)))
        && nullable(value.state, v => array(v, (s): s is DesignerBookmarkStateDto =>
            isObject(s) && string(s.objectName) && string(s.property) && boolean(s.on)));
}
function parameter(value: unknown): value is DesignerParameterDto {
    return isObject(value) && string(value.name) && string(value.dataType) && nullable(value.initialValue, string)
        && ['isInput', 'isOutput', 'isRequired', 'isSensitive', 'isBlockScoped'].every(key => boolean(value[key]));
}
function state(value: unknown): value is DesignerStateDto {
    return isObject(value) && array(value.pages, page) && array(value.datasets, dataset)
        && nullable(value.reportStyle, (v): v is DesignerReportStyleDto =>
            isObject(v) && optionalStrings(v, ['theme', 'accent', 'background', 'surface', 'text']))
        && nullable(value.bookmarks, v => array(v, bookmark)) && nullable(value.parameters, v => array(v, parameter))
        && nullable(value.connections, v => array(v, (c): c is DesignerConnectionDto =>
            isObject(c) && string(c.name) && string(c.text)));
}

function normalizeText(value: DesignerTextFormattingDto | null | undefined) {
    if (!value) return undefined;
    return { text: value.text ?? undefined, color: value.color ?? undefined, font: value.font ?? undefined,
        size: value.size ?? undefined, weight: value.weight ?? undefined, align: value.align ?? undefined };
}
function normalizeFormatting(value: DesignerVisualFormattingDto | null | undefined) {
    if (!value) return undefined;
    return { title: normalizeText(value.title), subtitle: normalizeText(value.subtitle),
        xAxis: value.xAxis ?? undefined, yAxis: value.yAxis ?? undefined, palette: value.palette ?? undefined,
        conditionalRules: value.conditionalRules?.map(rule => ({ ...rule, fontColor: rule.fontColor ?? undefined })),
        fields: value.fields ? Object.fromEntries(Object.entries(value.fields).map(([name, field]) => [name,
            { ...field, format: field.format ?? undefined, dataBarColor: field.dataBarColor ?? undefined }])) : undefined };
}

export function readDesignerParse(value: unknown): DesignerState {
    if (!isObject(value) || !nullable(value.error, string))
        throw new TypeError('Invalid designer parse response.');
    if (value.error) throw new Error(value.error);
    if (!state(value.designState)) throw new TypeError('Invalid designer parse response.');
    const source = value.designState;
    return { ...source, pages: source.pages.map(p => ({ ...p,
        visuals: p.visuals.map(v => ({ ...v, formatting: normalizeFormatting(v.formatting) })) })),
        reportStyle: source.reportStyle ?? undefined, bookmarks: source.bookmarks ?? undefined,
        parameters: source.parameters ?? undefined, connections: source.connections ?? undefined };
}

export function readDesignerSnapshot(value: unknown): DesignerSnapshotPackage {
    if (!isObject(value) || !string(value.reportName) || !date(value.builtAt)
        || !dictionary(value.sampleRows, v => array(v, row => array(row, (cell): cell is string | null => cell === null || string(cell))))
        || !dictionary(value.columns, v => array(v, string)) || !isObject(value.metadata)
        || !boolean(value.metadata.isSampled) || !boolean(value.metadata.rlsEnforced)
        || !integer(value.metadata.totalRows) || !integer(value.metadata.returnedRows))
        throw new TypeError('Invalid designer snapshot response.');
    const response: DesignerSnapshotResponse = {
        reportName: value.reportName, builtAt: value.builtAt, sampleRows: value.sampleRows, columns: value.columns,
        metadata: { isSampled: value.metadata.isSampled, rlsEnforced: value.metadata.rlsEnforced,
            totalRows: value.metadata.totalRows, returnedRows: value.metadata.returnedRows }
    };
    return { sampleRows: response.sampleRows, columnsByVisual: response.columns,
        metadata: { ...response.metadata } };
}
