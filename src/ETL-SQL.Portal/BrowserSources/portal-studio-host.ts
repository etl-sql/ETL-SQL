/** Catalog persistence callbacks for the shared Studio workbench. */
import { PortalAuthoringError, readDesignerLease, readDesignerSave, readPipelineRun, readPipelineRunCancelRequested, readPipelineRunSubmitted, readPipelineSchedule, readScriptContent, readStudioDraft, readStudioPrivateDraft, readStudioPrivateDrafts, readStudioReport } from './authoring-responses.js';
import { asError, isObject } from './http-error.js';
import { readAuthoringJson } from './portal-authoring.js';
import type { AuthoringFetch } from './portal-authoring.js';
import type { DraftRecord, DraftSaveResult } from '../designer/studio-lifecycle.js';

interface PortalDocument {
    reportId?: string | number;
    privateDraftId?: number;
    version?: number | null;
    sourceRevision?: string | number | null;
    lease?: { acquired?: boolean };
}

/** Where the Portal keeps a document's unsaved edits, or null when it has nowhere to keep them. */
function recoveryDraftUrl(document: PortalDocument): string | null {
    if (document.privateDraftId) return `/api/studio/private-drafts/${document.privateDraftId}/unsaved`;
    return document.reportId ? `/api/studio/drafts/${reportId(document)}` : null;
}

function reportId(document: PortalDocument): number {
    const id = Number(document.reportId);
    if (!Number.isSafeInteger(id) || id <= 0) throw new Error('This document is not attached to a Portal catalog report.');
    return id;
}

export function createPortalStudioHost(session: StudioSessionDto, fetch: AuthoringFetch,
    createReport: (request: CreateStudioReportRequest) => Promise<unknown>) {
    const capabilities = new Set(session.capabilities);
    const readJson = async (url: string, options?: RequestInit) => readAuthoringJson(await fetch(url, options));
    const acquireLease = async (id: number) => readDesignerLease(await readJson('/api/designer/lease', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reportId: id, force: false })
    }));
    return {
        onOpenDocument: async (catalogReport: unknown) => {
            const report = readStudioReport(catalogReport);
            const script = readScriptContent(await readJson(`/api/reports/${report.id}/script-content`));
            const extension = report.kind.toLowerCase() === 'pipeline' ? '.etlsql' : '.rptsql';
            const filename = /\.(etlsql|rptsql)$/i.test(report.name) ? report.name : report.name + extension;
            let lease: { acquired: boolean; owner?: string; expiresAt?: string } = { acquired: false };
            let canSave = false;
            let readOnlyReason: string | undefined = capabilities.has('ScriptSave') ? undefined
                : 'Your deployment mode or permissions allow viewing this report, but not saving it.';
            if (capabilities.has('ScriptSave')) {
                try {
                    lease = await acquireLease(report.id);
                    canSave = lease.acquired;
                } catch (error) {
                    const payload = error instanceof PortalAuthoringError && isObject(error.payload) ? error.payload : {};
                    const owner = typeof payload.owner === 'string' && payload.owner ? payload.owner : 'Another author';
                    const expires = typeof payload.expiresAt === 'string' ? new Date(payload.expiresAt) : null;
                    const expiry = expires && Number.isFinite(expires.valueOf()) ? ` until ${expires.toLocaleTimeString()}` : '';
                    readOnlyReason = error instanceof PortalAuthoringError && error.status === 409
                        ? `${owner} is editing this report${expiry}. Saving is unavailable.`
                        : `The edit lease could not be acquired: ${asError(error).message}`;
                }
            }
            return { id: `report-${report.id}`, reportId: report.id, folderId: report.folderId,
                folderPath: report.folderPath, version: script.version, sourceRevision: script.sourceRevision ?? null,
                sourceControlEnabled: script.sourceControlEnabled, path: `${report.folderPath || 'reports'}/${filename}`,
                name: filename, content: script.scriptText, lease, canSave, readOnlyReason };
        },
        onCreateDocument: async (request: Record<string, unknown>) => {
            const folderId = Number(request.folderId);
            if (!Number.isSafeInteger(folderId) || folderId <= 0 || typeof request.name !== 'string'
                || typeof request.scriptText !== 'string' || (request.kind != null && typeof request.kind !== 'string'))
                throw new TypeError('Invalid Portal document creation request.');
            return readStudioReport(await createReport({ folderId, name: request.name,
                scriptText: request.scriptText, description: null, kind: request.kind || 'Report' }));
        },
        onRenewDocument: (doc: PortalDocument) => acquireLease(reportId(doc)),
        // A saved pipeline runs in the Orchestrator. Only its saved version is named here; the
        // Portal reads that revision itself, so text in the editor is never what runs.
        ...(capabilities.has('ScriptRun') ? {
            pipelineRuns: {
                submit: async (doc: PortalDocument) => {
                    if (!Number.isSafeInteger(doc.version) || (doc.version ?? 0) <= 0)
                        throw new Error('This pipeline has no saved version to run.');
                    return readPipelineRunSubmitted(await readJson(`/api/studio/pipelines/${reportId(doc)}/runs`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ version: doc.version })
                    }));
                },
                read: async (doc: PortalDocument, runId: number) =>
                    readPipelineRun(await readJson(`/api/studio/pipelines/${reportId(doc)}/runs/${runId}`)),
                // Resolves when the Orchestrator has accepted the request, which is not yet the
                // outcome. A refusal carries the server's reason as the error message.
                cancel: async (doc: PortalDocument, runId: number) => {
                    readPipelineRunCancelRequested(await readJson(
                        `/api/studio/pipelines/${reportId(doc)}/runs/${runId}/cancel`, { method: 'POST' }));
                }
            },
            // The same handoff with a cadence: the Orchestrator pins the saved version and owns the
            // schedule. A catalog pipeline has no path a CREATE JOB statement could name.
            pipelineSchedule: {
                read: async (doc: PortalDocument) =>
                    readPipelineSchedule(await readJson(`/api/studio/pipelines/${reportId(doc)}/schedule`)),
                set: async (doc: PortalDocument, cron: string, timeZone: string | null) =>
                    readPipelineSchedule(await readJson(`/api/studio/pipelines/${reportId(doc)}/schedule`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ version: doc.version, cron, timeZone })
                    })),
                remove: async (doc: PortalDocument) =>
                    readPipelineSchedule(await readJson(`/api/studio/pipelines/${reportId(doc)}/schedule`, { method: 'DELETE' }))
            }
        } : {}),
        // Saved, unpublished work the Portal keeps for this user alone. It needs no publish right
        // and no folder; the Portal answers for any draft that is not the caller's as for none.
        ...(session.privateDrafts && capabilities.has('ScriptRead') ? {
            privateDrafts: {
                list: async () => readStudioPrivateDrafts(await readJson('/api/studio/private-drafts')),
                create: async (request: { name: string; kind: string; content: string }) =>
                    readStudioPrivateDraft(await readJson('/api/studio/private-drafts', {
                        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request)
                    })),
                open: async (id: number) => readStudioPrivateDraft(await readJson(`/api/studio/private-drafts/${id}`)),
                save: async (id: number, content: string, baseVersion: number) =>
                    readStudioPrivateDraft(await readJson(`/api/studio/private-drafts/${id}`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ content, baseVersion })
                    })),
                remove: async (id: number) => {
                    const response = await fetch(`/api/studio/private-drafts/${id}`, { method: 'DELETE' });
                    if (!response.ok) throw new Error(`The private draft could not be deleted (${response.status}).`);
                }
            }
        } : {}),
        ...(session.draftRecovery ? {
            onLoadDraft: async (doc: PortalDocument) => {
                const url = recoveryDraftUrl(doc);
                if (!url) return null;
                const response = await fetch(url);
                return response.ok ? readStudioDraft(await readAuthoringJson(response)) : null;
            },
            onSaveDraft: async (doc: PortalDocument, draft: DraftRecord, { keepalive = false } = {}): Promise<DraftSaveResult> => {
                const url = recoveryDraftUrl(doc);
                if (!url) return 'unavailable';
                const response = await fetch(url, {
                    method: 'PUT', keepalive, headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ content: draft.content, baseVersion: draft.baseVersion ?? null,
                        baseSourceRevision: draft.baseSourceRevision == null ? null : String(draft.baseSourceRevision) })
                });
                return response.status === 422 ? 'refused' : response.ok ? 'kept' : 'unavailable';
            },
            onRemoveDraft: (doc: PortalDocument) => {
                const url = recoveryDraftUrl(doc);
                return url ? fetch(url, { method: 'DELETE' }) : Promise.resolve();
            }
        } : {}),
        onReacquireDocument: async (doc: PortalDocument) => {
            if (!doc.reportId) return null;
            const id = reportId(doc);
            const lease = await acquireLease(id);
            let script: ScriptContentResponse | null = null;
            try { script = readScriptContent(await readJson(`/api/reports/${id}/script-content`)); }
            catch { /* Script content is best-effort for the server version check. */ }
            return { lease, version: script?.version ?? null, sourceRevision: script?.sourceRevision ?? null,
                content: script?.scriptText ?? '' };
        },
        onCloseDocument: async (doc: PortalDocument, { keepalive = false } = {}) => {
            if (!doc.reportId || !doc.lease?.acquired) return;
            const response = await fetch(`/api/designer/lease/${reportId(doc)}`, { method: 'DELETE', keepalive });
            if (!response.ok && !keepalive)
                throw new Error(`The edit lease could not be released (${response.status}).`);
            doc.lease.acquired = false;
        },
        onSave: async (content: string, _filePath: string, doc?: PortalDocument) => {
            if (doc?.privateDraftId) {
                // A private draft has one editor, so the version it started from is its whole lock.
                const saved = readStudioPrivateDraft(await readJson(`/api/studio/private-drafts/${doc.privateDraftId}`, {
                    method: 'PUT', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ content, baseVersion: doc.version })
                }));
                return { version: saved.version };
            }
            if (!doc?.reportId || !Number.isSafeInteger(doc.version) || (doc.version ?? 0) <= 0)
                throw new Error('This document is not attached to a versioned Portal catalog report.');
            if (!capabilities.has('ScriptSave') || !doc.lease?.acquired)
                throw new Error('Saving requires ScriptSave capability and an active edit lease.');
            return readDesignerSave(await readJson('/api/designer/save', {
                method: 'POST', headers: { 'Content-Type': 'application/json', 'If-Match': `"${doc.version}"` },
                body: JSON.stringify({ reportId: reportId(doc), scriptText: content, baseRevision: doc.sourceRevision || null })
            }));
        }
    };
}
