/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/portal-studio-host.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
/** Catalog persistence callbacks for the shared Studio workbench. */
import { PortalAuthoringError, readDesignerLease, readDesignerSave, readScriptContent, readStudioDraft, readStudioReport } from './authoring-responses.js';
import { asError, isObject } from './http-error.js';
import { readAuthoringJson } from './portal-authoring.js';
function reportId(document) {
    const id = Number(document.reportId);
    if (!Number.isSafeInteger(id) || id <= 0)
        throw new Error('This document is not attached to a Portal catalog report.');
    return id;
}
export function createPortalStudioHost(session, fetch, createReport) {
    const capabilities = new Set(session.capabilities);
    const readJson = async (url, options) => readAuthoringJson(await fetch(url, options));
    const acquireLease = async (id) => readDesignerLease(await readJson('/api/designer/lease', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reportId: id, force: false })
    }));
    return {
        onOpenDocument: async (catalogReport) => {
            const report = readStudioReport(catalogReport);
            const script = readScriptContent(await readJson(`/api/reports/${report.id}/script-content`));
            const extension = report.kind.toLowerCase() === 'pipeline' ? '.etlsql' : '.rptsql';
            const filename = /\.(etlsql|rptsql)$/i.test(report.name) ? report.name : report.name + extension;
            let lease = { acquired: false };
            let canSave = false;
            let readOnlyReason = capabilities.has('ScriptSave') ? undefined
                : 'Your deployment mode or permissions allow viewing this report, but not saving it.';
            if (capabilities.has('ScriptSave')) {
                try {
                    lease = await acquireLease(report.id);
                    canSave = lease.acquired;
                }
                catch (error) {
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
        onCreateDocument: async (request) => {
            const folderId = Number(request.folderId);
            if (!Number.isSafeInteger(folderId) || folderId <= 0 || typeof request.name !== 'string'
                || typeof request.scriptText !== 'string' || (request.kind != null && typeof request.kind !== 'string'))
                throw new TypeError('Invalid Portal document creation request.');
            return readStudioReport(await createReport({ folderId, name: request.name,
                scriptText: request.scriptText, description: null, kind: request.kind || 'Report' }));
        },
        onRenewDocument: (doc) => acquireLease(reportId(doc)),
        ...(session.draftRecovery ? {
            onLoadDraft: async (doc) => {
                if (!doc.reportId)
                    return null;
                const response = await fetch(`/api/studio/drafts/${reportId(doc)}`);
                return response.ok ? readStudioDraft(await readAuthoringJson(response)) : null;
            },
            onSaveDraft: async (doc, draft, { keepalive = false } = {}) => {
                if (!doc.reportId)
                    return 'unavailable';
                const response = await fetch(`/api/studio/drafts/${reportId(doc)}`, {
                    method: 'PUT', keepalive, headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ content: draft.content, baseVersion: draft.baseVersion ?? null,
                        baseSourceRevision: draft.baseSourceRevision == null ? null : String(draft.baseSourceRevision) })
                });
                return response.status === 422 ? 'refused' : response.ok ? 'kept' : 'unavailable';
            },
            onRemoveDraft: (doc) => doc.reportId
                ? fetch(`/api/studio/drafts/${reportId(doc)}`, { method: 'DELETE' }) : Promise.resolve()
        } : {}),
        onReacquireDocument: async (doc) => {
            if (!doc.reportId)
                return null;
            const id = reportId(doc);
            const lease = await acquireLease(id);
            let script = null;
            try {
                script = readScriptContent(await readJson(`/api/reports/${id}/script-content`));
            }
            catch { /* Script content is best-effort for the server version check. */ }
            return { lease, version: script?.version ?? null, sourceRevision: script?.sourceRevision ?? null,
                content: script?.scriptText ?? '' };
        },
        onCloseDocument: async (doc, { keepalive = false } = {}) => {
            if (!doc.reportId || !doc.lease?.acquired)
                return;
            const response = await fetch(`/api/designer/lease/${reportId(doc)}`, { method: 'DELETE', keepalive });
            if (!response.ok && !keepalive)
                throw new Error(`The edit lease could not be released (${response.status}).`);
            doc.lease.acquired = false;
        },
        onSave: async (content, _filePath, doc) => {
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
