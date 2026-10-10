/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/admin-reports.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { auth, PortalApiError } from './portal-http.js';
import { adminReportsApi, AdminReportOutcomeUnconfirmed } from './admin-reports-api.js';
import { adminFoldersApi, AdminFolderOutcomeUnconfirmed } from './admin-folders-api.js';
import { readAdminReportValidation } from './admin-reports-responses.js';
import { getSessionIdentity } from './session-identity.js';
import { asError } from './http-error.js';
import { failedState, loadingState } from './portal-states.js';
function element(doc, id, kind) {
    const value = doc.getElementById(id);
    if (!(value instanceof kind))
        throw new Error(`Missing admin report element: ${id}`);
    return value;
}
const escape = (value) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
/** Own report catalog reads, private opened versions, publication drafts, and script picking. */
export function createAdminReports(doc = document) {
    const feedback = window.ETLSQLFeedback;
    if (!feedback)
        throw new Error('Portal feedback must be initialized before report administration.');
    const wrap = element(doc, 'reportsTableWrap', HTMLDivElement), form = element(doc, 'publishReportForm', HTMLDivElement);
    const open = element(doc, 'openPublishBtn', HTMLButtonElement), save = element(doc, 'pr-saveBtn', HTMLButtonElement);
    const name = element(doc, 'pr-name', HTMLInputElement), path = element(doc, 'pr-path', HTMLInputElement);
    const description = element(doc, 'pr-desc', HTMLTextAreaElement), folder = element(doc, 'pr-folder', HTMLSelectElement);
    const editorError = element(doc, 'pr-error', HTMLDivElement), validationBox = element(doc, 'pr-validation', HTMLDivElement);
    const validateButton = element(doc, 'pr-validateBtn', HTMLButtonElement), browse = element(doc, 'pr-browseBtn', HTMLButtonElement);
    const inlineForm = element(doc, 'pr-newFolderRow', HTMLDivElement), inlineName = element(doc, 'pr-newFolderName', HTMLInputElement);
    const inlineParent = element(doc, 'pr-newFolderParent', HTMLSelectElement), inlineError = element(doc, 'pr-newFolderErr', HTMLDivElement);
    const inlineCreate = element(doc, 'pr-newFolderCreate', HTMLButtonElement), inlineToggle = element(doc, 'pr-newFolderToggle', HTMLButtonElement);
    const scriptModal = element(doc, 'scriptBrowserModal', HTMLDivElement), scriptList = element(doc, 'sb-list', HTMLDivElement);
    const scriptFilter = element(doc, 'sb-filter', HTMLInputElement), scriptError = element(doc, 'sb-error', HTMLDivElement);
    const mutationStatus = element(doc, 'reportsMutationStatus', HTMLDivElement);
    const sessionKey = () => {
        try {
            const current = getSessionIdentity(auth.getToken());
            return JSON.stringify([current.subject, current.roles, current.claims.tenant_id, current.claims.service_account_id]);
        }
        catch {
            return null;
        }
    };
    const identity = sessionKey(), listeners = new AbortController();
    let session = null, sessionTask = null;
    let reports = [], folders = [], scripts = [];
    let opened = null;
    let reportRead = 0, editorSequence = 0, folderRead = 0, validationRead = 0, scriptRead = 0, inlineSequence = 0, sessionRead = 0;
    let disposed = false, busy = false, foldersReady = false, reportsReady = false;
    let createNeedsReopen = null;
    let unconfirmed = null;
    const alive = () => !disposed && identity !== null && sessionKey() === identity;
    const listen = (target, event, handler) => target.addEventListener(event, handler, { signal: listeners.signal });
    const editing = () => form.style.display !== 'none';
    const sourcePublication = () => session?.mode === 'SourceControlled'
        && session.capabilities.includes('ReportPublish') && session.capabilities.includes('ScriptPreview');
    const showError = (box, message) => { box.textContent = message; box.classList.add('show'); };
    function updateControls() {
        const locked = busy || !!unconfirmed || !alive();
        open.hidden = !session || !(sourcePublication() || (session.mode === 'CatalogOnly' && session.capabilities.includes('StudioAccess')));
        open.style.display = open.hidden ? 'none' : '';
        open.textContent = session?.mode === 'CatalogOnly' ? 'Open Studio' : 'Publish Report';
        save.disabled = locked || createNeedsReopen === editorSequence || !reportsReady || !foldersReady || !sourcePublication()
            || !folders.some(item => String(item.id) === folder.value);
        folder.disabled = !foldersReady || busy;
        validateButton.disabled = busy || !alive() || !sourcePublication();
        browse.disabled = !alive() || !session?.capabilities.includes('ScriptRead');
        inlineToggle.disabled = locked || !foldersReady;
        inlineCreate.disabled = locked || !foldersReady
            || (!!inlineParent.value && !folders.some(item => String(item.id) === inlineParent.value));
        inlineParent.disabled = !foldersReady || busy;
        wrap.querySelectorAll('[data-action="delete-report"]').forEach(button => { button.disabled = locked; });
        mutationStatus.hidden = !unconfirmed;
        mutationStatus.textContent = unconfirmed?.message || '';
    }
    function resetValidation() { ++validationRead; validationBox.style.display = 'none'; validationBox.replaceChildren(); }
    function renderValidation(value) {
        validationBox.style.display = '';
        validationBox.className = `validation-panel ${value.isValid ? 'validation-ok' : 'validation-bad'}`;
        validationBox.innerHTML = value.isValid
            ? `<div class="validation-title">Script validated</div><div class="validation-line">Hash: <code>${escape(value.hash || '')}</code></div>
        <div class="validation-line">Parameters: ${escape(value.parameters.length ? value.parameters.map(parameter => parameter.name).join(', ') : 'None')}</div>`
            : `<div class="validation-title">Script validation failed</div><ul>${value.errors.map(error => `<li>${escape(error)}</li>`).join('')}</ul>`;
    }
    async function ensureSession(force = false) {
        if (!alive())
            return false;
        if (!sessionTask || force) {
            const sequence = ++sessionRead;
            sessionTask = (async () => {
                try {
                    const result = await adminReportsApi.session();
                    if (!alive() || sequence !== sessionRead)
                        return false;
                    session = result;
                    updateControls();
                    return true;
                }
                catch {
                    if (alive() && sequence === sessionRead) {
                        session = null;
                        updateControls();
                    }
                    return false;
                }
            })();
        }
        return sessionTask;
    }
    async function reload() {
        if (!alive())
            return false;
        const sequence = ++reportRead;
        reportsReady = false;
        wrap.dataset.reportsState = 'loading';
        wrap.innerHTML = loadingState('Loading reports…');
        updateControls();
        try {
            if (!await ensureSession())
                throw new Error('Studio policy could not be loaded. Refresh reports to retry.');
            const result = await adminReportsApi.catalog();
            if (!alive() || sequence !== reportRead)
                return false;
            reports = result;
            reportsReady = true;
            const external = session?.mode === 'SourceControlled', studio = !!session?.capabilities.includes('StudioAccess');
            wrap.innerHTML = `<table class="data-table"><thead><tr><th>Report Name</th><th>Folder</th>${external ? '<th>Script Path</th>' : ''}<th>Created</th><th>Actions</th></tr></thead><tbody>
        ${reports.map(report => `<tr><td>${escape(report.name)}</td><td class="text-sm text-muted">${escape(report.folderPath)}</td>
          ${external ? `<td class="text-sm text-muted"><code>${escape(report.scriptPath)}</code></td>` : ''}<td class="text-sm">${new Date(report.createdAt).toLocaleDateString()}</td>
          <td><div class="table-actions">${studio ? `<button class="btn btn-outline btn-sm" data-action="edit-report" data-id="${report.id}">${sourcePublication() ? 'Edit' : 'Open in Studio'}</button>` : ''}
          <button class="btn btn-danger btn-sm" data-action="delete-report" data-id="${report.id}">Delete</button></div></td></tr>`).join('')
                || `<tr><td colspan="${external ? 5 : 4}" class="empty-state">No reports published.</td></tr>`}</tbody></table>`;
            wrap.dataset.reportsState = 'ready';
            updateControls();
            return true;
        }
        catch (error) {
            if (!alive() || sequence !== reportRead)
                return false;
            reports = [];
            reportsReady = false;
            wrap.dataset.reportsState = 'failed';
            wrap.innerHTML = failedState({ title: 'Reports could not be loaded.', body: asError(error).message });
            updateControls();
            return false;
        }
    }
    function closeScripts() { ++scriptRead; scriptModal.style.display = 'none'; scripts = []; scriptList.replaceChildren(); }
    function closeInline() {
        ++inlineSequence;
        inlineForm.style.display = 'none';
        inlineName.value = '';
        inlineError.style.display = 'none';
        inlineError.textContent = '';
    }
    function closeEditor() {
        ++editorSequence;
        ++folderRead;
        opened = null;
        form.style.display = 'none';
        foldersReady = false;
        resetValidation();
        closeInline();
        closeScripts();
        updateControls();
    }
    async function loadFolders(context, selected = folder.value) {
        if (!alive() || context !== editorSequence || !editing())
            return false;
        const sequence = ++folderRead, previousParent = inlineParent.value;
        foldersReady = false;
        updateControls();
        try {
            const result = await adminFoldersApi.tree();
            if (!alive() || context !== editorSequence || sequence !== folderRead || !editing())
                return false;
            folders = result.folders;
            folder.replaceChildren(...folders.map(item => new Option(item.path, String(item.id))));
            inlineParent.replaceChildren(new Option('— Root —', ''), ...folders.map(item => new Option(item.path, String(item.id))));
            if (selected) {
                if (!folders.some(item => String(item.id) === selected)) {
                    const unavailable = new Option(`Unavailable folder: ${selected}`, selected);
                    unavailable.disabled = true;
                    folder.append(unavailable);
                }
                folder.value = selected;
            }
            if (previousParent && !folders.some(item => String(item.id) === previousParent)) {
                const unavailable = new Option(`Unavailable parent: ${previousParent}`, previousParent);
                unavailable.disabled = true;
                inlineParent.append(unavailable);
            }
            inlineParent.value = previousParent;
            foldersReady = true;
            updateControls();
            return true;
        }
        catch (error) {
            if (!alive() || context !== editorSequence || sequence !== folderRead || !editing())
                return false;
            folders = [];
            foldersReady = false;
            showError(editorError, `Destination folders could not be loaded. Refresh reports to retry. ${asError(error).message}`);
            updateControls();
            return false;
        }
    }
    async function openEditor(report) {
        const context = ++editorSequence;
        if (!alive() || !await ensureSession() || !alive() || context !== editorSequence)
            return;
        if (session?.mode === 'CatalogOnly' || (!sourcePublication() && session?.capabilities.includes('StudioAccess'))) {
            window.location.href = report ? `/designer.html?id=${report.id}` : '/studio.html';
            return;
        }
        if (!sourcePublication())
            return;
        opened = report ? structuredClone(report) : null;
        closeInline();
        closeScripts();
        resetValidation();
        editorError.classList.remove('show');
        name.value = report?.name || '';
        path.value = report?.scriptPath || '';
        description.value = report?.description || '';
        element(doc, 'pr-id', HTMLInputElement).value = report ? String(report.id) : '';
        element(doc, 'pr-title', HTMLHeadingElement).textContent = report ? 'Edit Report' : 'Publish New Report';
        save.textContent = report ? 'Save Changes' : 'Publish';
        folder.replaceChildren();
        inlineParent.replaceChildren(new Option('— Root —', ''));
        form.style.display = '';
        name.focus();
        await loadFolders(context, report ? String(report.folderId) : '');
    }
    function input() { return { name: name.value.trim(), scriptPath: path.value.trim(), description: description.value.trim(), folderId: Number(folder.value) }; }
    const unchanged = (context, body) => context === editorSequence && editing() && JSON.stringify(input()) === JSON.stringify(body);
    async function perform(action, context = null, inline = null, scriptPath = '') {
        if (!alive() || busy || unconfirmed)
            return;
        busy = true;
        ++reportRead;
        updateControls();
        try {
            await action();
        }
        catch (error) {
            if (!alive())
                return;
            if (error instanceof AdminReportOutcomeUnconfirmed || error instanceof AdminFolderOutcomeUnconfirmed)
                unconfirmed = error;
            if (context === editorSequence && editing()) {
                if (inline !== null && inline === inlineSequence) {
                    inlineError.textContent = asError(error).message;
                    inlineError.style.display = '';
                }
                else
                    showError(editorError, asError(error).message);
                if (error instanceof PortalApiError && path.value.trim() === scriptPath) {
                    try {
                        renderValidation(readAdminReportValidation(error.body, scriptPath));
                    }
                    catch { /* The rejection may be a version, policy, or authorization error instead of validation. */ }
                }
            }
            else
                feedback.notify(asError(error).message, { title: 'Request failed', tone: 'error' });
        }
        finally {
            busy = false;
            if (alive())
                updateControls();
        }
    }
    async function saveEditor() {
        if (!editing() || save.disabled)
            return;
        const context = editorSequence, report = opened, body = input();
        editorError.classList.remove('show');
        if (!body.name || !body.scriptPath) {
            showError(editorError, 'Name, Path, and Folder are required.');
            return;
        }
        await perform(async () => {
            const sequence = ++validationRead;
            const validation = await adminReportsApi.validate(body.scriptPath);
            if (!alive() || sequence !== validationRead || !unchanged(context, body))
                return;
            renderValidation(validation);
            if (!validation.isValid) {
                showError(editorError, 'Script validation failed.');
                return;
            }
            let receipt;
            try {
                receipt = report ? await adminReportsApi.update(report, body) : await adminReportsApi.create(body);
            }
            catch (error) {
                if (!report && error instanceof AdminReportOutcomeUnconfirmed && context === editorSequence)
                    createNeedsReopen = context;
                throw error;
            }
            if (!alive())
                return;
            if (unchanged(context, body))
                closeEditor();
            else if (context === editorSequence && editing()) {
                opened = { id: receipt.id, version: receipt.version };
                element(doc, 'pr-title', HTMLHeadingElement).textContent = 'Edit Report';
                save.textContent = 'Save Changes';
            }
            await reload();
            if (alive())
                feedback.notify(report ? 'The report was updated.' : 'The report was published.', {
                    title: report ? 'Report updated' : 'Report published', tone: 'success', auditAction: report ? 'admin.report.update' : 'admin.report.publish'
                });
        }, context, null, body.scriptPath);
    }
    async function validate() {
        if (!alive() || !editing() || busy || !sourcePublication())
            return;
        const context = editorSequence, scriptPath = path.value.trim(), sequence = ++validationRead;
        editorError.classList.remove('show');
        if (!scriptPath) {
            showError(editorError, 'Script path is required.');
            return;
        }
        try {
            const result = await adminReportsApi.validate(scriptPath);
            if (alive() && context === editorSequence && sequence === validationRead && editing() && path.value.trim() === scriptPath)
                renderValidation(result);
        }
        catch (error) {
            if (alive() && context === editorSequence && sequence === validationRead && editing() && path.value.trim() === scriptPath) {
                validationBox.style.display = 'none';
                showError(editorError, asError(error).message);
            }
        }
    }
    async function refresh() {
        if (!alive() || busy)
            return;
        const outcome = unconfirmed;
        await ensureSession(true);
        if (!await reload() || !alive() || busy)
            return;
        let confirmed = true;
        if (outcome instanceof AdminReportOutcomeUnconfirmed && outcome.reportId !== null) {
            try {
                await adminReportsApi.get(outcome.reportId);
            }
            catch (error) {
                confirmed = error instanceof PortalApiError && error.status === 404;
            }
        }
        if (editing())
            confirmed = await loadFolders(editorSequence) && confirmed;
        else if (outcome instanceof AdminFolderOutcomeUnconfirmed) {
            try {
                await adminFoldersApi.tree();
            }
            catch {
                confirmed = false;
            }
        }
        if (alive() && !busy && outcome === unconfirmed && confirmed) {
            unconfirmed = null;
            if (editing() && createNeedsReopen === editorSequence)
                showError(editorError, 'Reports refreshed. Reopen a published report from the catalog before saving this draft, or cancel to start a new publication.');
        }
        updateControls();
    }
    async function reportAction(button) {
        const report = reports.find(item => item.id === Number(button.dataset.id));
        if (!alive() || !report || !reportsReady)
            return;
        if (button.dataset.action === 'edit-report') {
            await openEditor(report);
            return;
        }
        await perform(async () => {
            if (!await feedback.confirm(`Delete ${report.name}?`, { title: 'Delete report',
                impact: 'Active subscriptions will be disabled. Attached refresh jobs must be removed before deletion.',
                confirmLabel: 'Delete report', danger: true, auditAction: 'admin.report.delete' }) || !alive())
                return;
            await adminReportsApi.delete(report);
            if (!alive())
                return;
            if (opened?.id === report.id)
                closeEditor();
            await reload();
        });
    }
    async function createInlineFolder() {
        if (inlineCreate.disabled || inlineForm.style.display === 'none')
            return;
        const context = editorSequence, inline = inlineSequence;
        const body = { name: inlineName.value.trim(), parentId: inlineParent.value ? Number(inlineParent.value) : null };
        inlineError.style.display = 'none';
        if (!body.name) {
            inlineError.textContent = 'Folder name is required.';
            inlineError.style.display = '';
            return;
        }
        await perform(async () => {
            const created = await adminFoldersApi.create(body);
            if (!alive())
                return;
            const same = context === editorSequence && inline === inlineSequence && inlineName.value.trim() === body.name
                && (inlineParent.value ? Number(inlineParent.value) : null) === body.parentId;
            if (same)
                closeInline();
            if (editing())
                await loadFolders(editorSequence, same ? String(created.id) : folder.value);
            if (!reportsReady)
                await reload();
        }, context, inline);
    }
    function renderScripts() {
        const query = scriptFilter.value.trim().toLowerCase(), visible = scripts.filter(script => script.toLowerCase().includes(query));
        scriptList.innerHTML = visible.map(script => `<div class="sb-item" role="button" tabindex="0" data-path="${escape(script)}">
      <span class="script-glyph" aria-hidden="true"></span> ${escape(script)}</div>`).join('')
            || '<div class="empty-state">No matching .rptsql files found in ScriptRoot.</div>';
    }
    async function loadScripts() {
        if (!alive() || !editing() || scriptModal.style.display === 'none')
            return;
        const context = editorSequence, sequence = ++scriptRead;
        scripts = [];
        scriptError.classList.remove('show');
        scriptList.innerHTML = loadingState('Loading scripts…');
        try {
            const result = await adminReportsApi.scripts();
            if (!alive() || context !== editorSequence || sequence !== scriptRead || !editing() || scriptModal.style.display === 'none')
                return;
            scripts = result;
            renderScripts();
        }
        catch (error) {
            if (!alive() || context !== editorSequence || sequence !== scriptRead || scriptModal.style.display === 'none')
                return;
            showError(scriptError, asError(error).message);
            scriptList.innerHTML = failedState({ title: 'Scripts could not be loaded.', body: 'Reload scripts to retry.' });
        }
    }
    function chooseScript(target) {
        const item = target instanceof Element ? target.closest('[data-path]') : null;
        if (!alive() || !editing() || !item || !scripts.includes(item.dataset.path || ''))
            return;
        path.value = item.dataset.path;
        resetValidation();
        closeScripts();
        path.focus();
    }
    listen(wrap, 'click', event => {
        const button = event.target instanceof Element ? event.target.closest('[data-action]') : null;
        if (button && wrap.contains(button))
            void reportAction(button);
    });
    listen(open, 'click', () => { void openEditor(null); });
    listen(element(doc, 'reportsRefreshBtn', HTMLButtonElement), 'click', () => { void refresh(); });
    listen(element(doc, 'pr-cancelBtn', HTMLButtonElement), 'click', closeEditor);
    listen(save, 'click', () => { void saveEditor(); });
    listen(validateButton, 'click', () => { void validate(); });
    listen(path, 'input', resetValidation);
    listen(folder, 'change', updateControls);
    listen(inlineParent, 'change', updateControls);
    listen(inlineToggle, 'click', () => {
        if (inlineForm.style.display !== 'none')
            closeInline();
        else {
            ++inlineSequence;
            inlineForm.style.display = '';
            inlineParent.value = folder.value;
            inlineName.focus();
        }
    });
    listen(element(doc, 'pr-newFolderCancelBtn', HTMLButtonElement), 'click', closeInline);
    listen(inlineCreate, 'click', () => { void createInlineFolder(); });
    listen(browse, 'click', () => { scriptModal.style.display = 'flex'; scriptFilter.value = ''; scriptFilter.focus(); void loadScripts(); });
    listen(element(doc, 'sb-retryBtn', HTMLButtonElement), 'click', () => { void loadScripts(); });
    listen(element(doc, 'sb-cancelBtn', HTMLButtonElement), 'click', closeScripts);
    listen(scriptFilter, 'input', renderScripts);
    listen(scriptList, 'click', event => chooseScript(event.target));
    listen(scriptList, 'keydown', event => {
        if (event instanceof KeyboardEvent && (event.key === 'Enter' || event.key === ' ')) {
            event.preventDefault();
            chooseScript(event.target);
        }
    });
    updateControls();
    return { reload, dispose() {
            if (disposed)
                return;
            disposed = true;
            ++reportRead;
            ++sessionRead;
            closeEditor();
            reports = [];
            folders = [];
            name.value = '';
            path.value = '';
            description.value = '';
            wrap.replaceChildren();
            listeners.abort();
        } };
}
