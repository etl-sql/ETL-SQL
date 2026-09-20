// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-workspace-explorer.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-workspace-explorer.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Workspace tree and file/folder lifecycle operations.
 */
import { _escapeHtml, _feedback, _fileIcon, _studioIcon, closestElement, errorMessage, queryElements } from './studio-context.js';
export function createStudioWorkspaceExplorer(hostContext) {
    function normalizeWorkspacePath(path) {
        return String(path || '').replace(/\\/g, '/').replace(/^\/+|\/+$/g, '');
    }
    function workspaceParentPath(path) {
        const normalized = normalizeWorkspacePath(path);
        const slash = normalized.lastIndexOf('/');
        return slash < 0 ? '' : normalized.slice(0, slash);
    }
    function workspaceBaseName(path) {
        const normalized = normalizeWorkspacePath(path);
        return normalized.slice(normalized.lastIndexOf('/') + 1);
    }
    function applyWorkspaceSnapshot(snapshot) {
        hostContext.state.workspaceFiles = [...(snapshot?.files || [])];
        hostContext.state.workspaceFolders = [...(snapshot?.folders || [])];
    }
    function updateOpenDocumentPaths(oldPath, newPath, isDirectory) {
        const normalizedOld = normalizeWorkspacePath(oldPath);
        const prefix = `${normalizedOld}/`;
        hostContext.state.documents.forEach(doc => {
            const path = normalizeWorkspacePath(doc.path);
            if (path !== normalizedOld && !(isDirectory && path.startsWith(prefix)))
                return;
            doc.path = isDirectory ? `${newPath}${path.slice(normalizedOld.length)}` : newPath;
            doc.name = workspaceBaseName(doc.path);
        });
    }
    function workspaceTreeMarkup(parentPath = '', depth = 0) {
        const folders = hostContext.state.workspaceFolders
            .map((folder) => normalizeWorkspacePath(typeof folder === 'string' ? folder : folder?.path || ''))
            .filter((path) => workspaceParentPath(path) === parentPath)
            .sort((left, right) => left.localeCompare(right, undefined, { sensitivity: 'base' }));
        const files = hostContext.state.workspaceFiles
            .filter((file) => workspaceParentPath(file.path || '') === parentPath)
            .sort((left, right) => (left.path || '').localeCompare(right.path || '', undefined, { sensitivity: 'base' }));
        const canMutate = Boolean(hostContext.opts.onRenameWorkspaceEntry && hostContext.opts.onDeleteWorkspaceEntry);
        return [
            ...folders.map((path) => {
                const expanded = hostContext.state.explorerExpanded.has(path);
                return `<div class="etlsql-explorer-node" data-explorer-node="${_escapeHtml(path)}">
                    <div class="etlsql-studio-file-item etlsql-explorer-folder" data-explorer-folder="${_escapeHtml(path)}" data-depth="${depth}" style="--explorer-depth:${depth}">
                        <button type="button" class="etlsql-explorer-toggle" data-explorer-toggle="${_escapeHtml(path)}" aria-label="${expanded ? 'Collapse' : 'Expand'} ${_escapeHtml(workspaceBaseName(path))}" aria-expanded="${expanded}">${expanded ? '▾' : '▸'}</button>
                        <span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span>
                        <span class="etlsql-file-name">${_escapeHtml(workspaceBaseName(path))}</span>
                        ${canMutate ? `<span class="etlsql-explorer-actions"><button type="button" data-explorer-new-folder="${_escapeHtml(path)}" title="New subfolder" aria-label="New folder in ${_escapeHtml(path)}">${_studioIcon('plus', 11)}</button><button type="button" data-explorer-rename="${_escapeHtml(path)}" data-entry-directory="true" title="Rename folder" aria-label="Rename ${_escapeHtml(path)}">${_studioIcon('edit', 11)}</button><button type="button" data-explorer-delete="${_escapeHtml(path)}" data-entry-directory="true" title="Delete folder" aria-label="Delete ${_escapeHtml(path)}">${_studioIcon('trash', 11)}</button></span>` : ''}
                    </div>
                    ${expanded ? `<div class="etlsql-explorer-children">${workspaceTreeMarkup(path, depth + 1)}</div>` : ''}
                </div>`;
            }),
            ...files.map((file) => {
                const path = normalizeWorkspacePath(file.path || '');
                const active = hostContext.state.documents.some(doc => doc.id === hostContext.state.activeDocId && normalizeWorkspacePath(doc.path) === path);
                return `<div class="etlsql-studio-file-item etlsql-explorer-file ${active ? 'active' : ''}" data-explorer-file="${_escapeHtml(path)}" draggable="${Boolean(hostContext.opts.onMoveWorkspaceFile)}" style="--explorer-depth:${depth}">
                    <span class="etlsql-explorer-spacer" aria-hidden="true"></span>
                    <span class="etlsql-file-icon">${_fileIcon(path)}</span>
                    <span class="etlsql-file-name" title="${_escapeHtml(path)}">${_escapeHtml(workspaceBaseName(path))}</span>
                    ${canMutate ? `<span class="etlsql-explorer-actions"><button type="button" data-explorer-rename="${_escapeHtml(path)}" data-entry-directory="false" title="Rename file" aria-label="Rename ${_escapeHtml(path)}">${_studioIcon('edit', 11)}</button><button type="button" data-explorer-delete="${_escapeHtml(path)}" data-entry-directory="false" title="Delete file" aria-label="Delete ${_escapeHtml(path)}">${_studioIcon('trash', 11)}</button></span>` : ''}
                </div>`;
            })
        ].join('');
    }
    async function createWorkspaceFolder(parentPath) {
        const name = await _feedback.prompt('Choose a name for the new folder.', { title: 'New Folder', label: 'Folder name', required: true, confirmLabel: 'Create' });
        if (!name?.trim())
            return;
        const path = [normalizeWorkspacePath(parentPath), name.trim()].filter(Boolean).join('/');
        try {
            const snapshot = await hostContext.opts.onCreateWorkspaceFolder?.(path);
            applyWorkspaceSnapshot(snapshot);
            hostContext.state.explorerExpanded.add(normalizeWorkspacePath(parentPath));
            hostContext.state.explorerExpanded.add(normalizeWorkspacePath(snapshot?.result?.path || path));
            hostContext.renderSidebarContent('explorer');
            _feedback.notify(`Created folder ${snapshot?.result?.path || path}`, { title: 'Folder Created', tone: 'success' });
        }
        catch (error) {
            _feedback.notify(errorMessage(error) || 'The folder could not be created.', { title: 'Create Folder Failed', tone: 'error' });
        }
    }
    async function renameWorkspaceEntry(entry) {
        const currentName = workspaceBaseName(entry.path || '');
        const name = await _feedback.prompt(`Rename ${currentName}.`, { title: entry.isDirectory ? 'Rename Folder' : 'Rename File', label: 'Name', value: currentName, required: true, confirmLabel: 'Rename' });
        if (!name?.trim() || name.trim() === currentName)
            return;
        try {
            const snapshot = await hostContext.opts.onRenameWorkspaceEntry?.(entry, name.trim());
            const newPath = normalizeWorkspacePath(snapshot?.result?.path);
            if (!newPath)
                throw new Error('The host did not return the renamed path.');
            updateOpenDocumentPaths(entry.path || '', newPath, entry.isDirectory);
            if (entry.isDirectory && hostContext.state.explorerExpanded.delete(normalizeWorkspacePath(entry.path || '')))
                hostContext.state.explorerExpanded.add(newPath);
            applyWorkspaceSnapshot(snapshot);
            hostContext.renderTabs();
            hostContext.renderSidebarContent('explorer');
            if (hostContext.state.activeDocId === '__home__')
                hostContext.renderStudioHome();
            _feedback.notify(`Renamed ${entry.path || ''} to ${newPath}`, { title: 'Workspace Entry Renamed', tone: 'success' });
        }
        catch (error) {
            _feedback.notify(errorMessage(error) || 'The workspace entry could not be renamed.', { title: 'Rename Failed', tone: 'error' });
        }
    }
    async function deleteWorkspaceEntry(entry) {
        const normalized = normalizeWorkspacePath(entry.path || '');
        const affected = hostContext.state.documents.filter(doc => normalizeWorkspacePath(doc.path) === normalized || (entry.isDirectory && normalizeWorkspacePath(doc.path).startsWith(`${normalized}/`)));
        if (affected.some(doc => doc.isDirty)) {
            _feedback.notify('Save or close modified files before deleting them from the workspace.', { title: 'Delete Blocked', tone: 'warning' });
            return;
        }
        const confirmed = await _feedback.confirm(`Delete ${entry.path || ''}${entry.isDirectory ? ' and everything inside it' : ''}? This cannot be undone.`, { title: entry.isDirectory ? 'Delete Folder' : 'Delete File', confirmLabel: 'Delete', danger: true });
        if (!confirmed)
            return;
        try {
            const snapshot = await hostContext.opts.onDeleteWorkspaceEntry?.(entry);
            const removedIds = new Set(affected.map(doc => doc.id));
            hostContext.state.documents = hostContext.state.documents.filter(doc => !removedIds.has(doc.id));
            applyWorkspaceSnapshot(snapshot);
            if (removedIds.has(hostContext.state.activeDocId))
                await hostContext.switchDoc('__home__');
            else {
                hostContext.renderTabs();
                hostContext.renderSidebarContent('explorer');
            }
            if (hostContext.state.activeDocId === '__home__')
                hostContext.renderStudioHome();
            _feedback.notify(`Deleted ${entry.path || ''}`, { title: entry.isDirectory ? 'Folder Deleted' : 'File Deleted', tone: 'success' });
        }
        catch (error) {
            _feedback.notify(errorMessage(error) || 'The workspace entry could not be deleted.', { title: 'Delete Failed', tone: 'error' });
        }
    }
    async function moveWorkspaceFile(filePath, destinationFolder) {
        if (workspaceParentPath(filePath) === normalizeWorkspacePath(destinationFolder))
            return;
        try {
            const snapshot = await hostContext.opts.onMoveWorkspaceFile?.(filePath, normalizeWorkspacePath(destinationFolder));
            const newPath = normalizeWorkspacePath(snapshot?.result?.path);
            if (!newPath)
                throw new Error('The host did not return the moved file path.');
            updateOpenDocumentPaths(filePath, newPath, false);
            applyWorkspaceSnapshot(snapshot);
            hostContext.renderTabs();
            hostContext.renderSidebarContent('explorer');
            if (hostContext.state.activeDocId === '__home__')
                hostContext.renderStudioHome();
            _feedback.notify(`Moved ${filePath} to ${newPath}`, { title: 'File Moved', tone: 'success' });
        }
        catch (error) {
            _feedback.notify(errorMessage(error) || 'The file could not be moved.', { title: 'Move Failed', tone: 'error' });
        }
    }
    function bindWorkspaceExplorer() {
        queryElements(hostContext.sidebarContent, '[data-explorer-toggle]').forEach(button => button.addEventListener('click', event => {
            event.stopPropagation();
            const path = normalizeWorkspacePath(button.dataset.explorerToggle);
            if (hostContext.state.explorerExpanded.has(path))
                hostContext.state.explorerExpanded.delete(path);
            else
                hostContext.state.explorerExpanded.add(path);
            hostContext.renderSidebarContent('explorer');
        }));
        queryElements(hostContext.sidebarContent, '[data-explorer-file]').forEach(row => {
            row.addEventListener('click', event => {
                if (!closestElement(event, '.etlsql-explorer-actions'))
                    void hostContext.openWorkspaceFile(row.dataset.explorerFile);
            });
            row.addEventListener('dragstart', (event) => {
                event.dataTransfer?.setData('application/x-etlsql-workspace-file', row.dataset.explorerFile || '');
                if (event.dataTransfer)
                    event.dataTransfer.effectAllowed = 'move';
            });
        });
        queryElements(hostContext.sidebarContent, '[data-explorer-folder], [data-explorer-root-drop]').forEach(target => {
            target.addEventListener('dragover', (event) => {
                if (!event.dataTransfer?.types.includes('application/x-etlsql-workspace-file'))
                    return;
                event.preventDefault();
                event.dataTransfer.dropEffect = 'move';
                target.classList.add('drag-over');
            });
            target.addEventListener('dragleave', () => target.classList.remove('drag-over'));
            target.addEventListener('drop', (event) => {
                const filePath = event.dataTransfer?.getData('application/x-etlsql-workspace-file');
                if (!filePath)
                    return;
                event.preventDefault();
                event.stopPropagation();
                target.classList.remove('drag-over');
                void moveWorkspaceFile(filePath, target.dataset.explorerFolder || '');
            });
        });
        queryElements(hostContext.sidebarContent, '[data-explorer-new-folder]').forEach(button => button.addEventListener('click', event => { event.stopPropagation(); void createWorkspaceFolder(button.dataset.explorerNewFolder || ''); }));
        queryElements(hostContext.sidebarContent, '[data-explorer-rename]').forEach(button => button.addEventListener('click', event => { event.stopPropagation(); void renameWorkspaceEntry({ path: button.dataset.explorerRename, isDirectory: button.dataset.entryDirectory === 'true' }); }));
        queryElements(hostContext.sidebarContent, '[data-explorer-delete]').forEach(button => button.addEventListener('click', event => { event.stopPropagation(); void deleteWorkspaceEntry({ path: button.dataset.explorerDelete, isDirectory: button.dataset.entryDirectory === 'true' }); }));
    }
    return { workspaceTreeMarkup, bindWorkspaceExplorer };
}
