/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Workspace, connection schema, session data, and Git sidebar operations.
 */

import { asButton, asHtml, asInput } from './workbench-context.js';

import { _feedback, escapeHtml } from './designer-util.js';
import { buildDataPreviewPayload, type ScriptResultsPanel } from './run-results.js';
import type { ScriptEditorHandle } from './script-editor.js';
import type { GitStatusResult, SchemaColumnEntry, SchemaMetadataResult, SchemaTableEntry, ScriptWorkbenchOptions, TreeNodeOptions, WorkspaceFileEntry } from './workbench-context.js';

export interface WorkbenchSidebarContext {
    activeFileHandle: any;
    currentFilePath: string;
    dataPreviewAbort: AbortController | null;
    readonly editor: ScriptEditorHandle;
    readonly getDocumentUri: () => string;
    readonly hasSidebar: boolean;
    readonly opts: ScriptWorkbenchOptions;
    readonly resultsPanel: ScriptResultsPanel;
    readonly root: HTMLElement;
    readonly showSchema: boolean;
    readonly showSession: boolean;
}

export function createWorkbenchSidebar(context: WorkbenchSidebarContext) {
    let sidebarRefreshTimer: any = null;

    let sessionSignature: string | null = null;

    let schemaSignature: string | null = null;

    const SNIPPET_MIME = 'application/x-etlsql-snippet';

    async function loadFile(filePath: string): Promise<void> {
        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const url = `/api/files?path=${encodeURIComponent(filePath)}`;
            const res = await fetcher(url);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            if (data && typeof data.content === 'string') {
                context.editor.setValue(data.content);
                context.currentFilePath = filePath;
                const titleEl = context.root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = filePath;
                }
                if (context.hasSidebar) {
                    loadSchema();
                    loadSession();
                    loadGit();
                }
            }
        } catch (err: any) {
            console.error(err);
            _feedback?.notify?.(`Error loading file: ${err.message}`, { title: 'File not loaded', tone: 'error' });
        }
    }

    async function loadFiles(): Promise<void> {
        const filesEl = context.root.querySelector('[data-sidebar-files]');
        if (!filesEl) return;
        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const res = await fetcher(context.opts.workspaceUrl || '/api/workspace');
            if (!res.ok) throw new Error(`Workspace listing unavailable (HTTP ${res.status})`);
            const data = await res.json();
            if (data && data.files) {
                filesEl.innerHTML = '';
                data.files.forEach((f: { path: string; size: number }) => {
                    const item = document.createElement('div');
                    item.className = 'etlsql-sidebar-file';
                    item.innerHTML = `<span class="etlsql-tree-label">${escapeHtml(f.path)}</span><span class="etlsql-tree-type">${Math.round(f.size / 10.24) / 100} KB</span>`;

                    if (f.path === context.currentFilePath) item.classList.add('active');

                    item.addEventListener('click', async () => {
                        filesEl.querySelectorAll('.etlsql-sidebar-file').forEach(e => e.classList.remove('active'));
                        item.classList.add('active');
                        if (context.opts.onFileSelect) {
                            await context.opts.onFileSelect(f.path);
                        } else {
                            await loadFile(f.path);
                        }
                    });
                    filesEl.appendChild(item);
                });
            } else {
                filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">No files.</div>';
            }
        } catch (err: any) {
            filesEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    async function renderDirectoryTree(dirHandle: any): Promise<void> {
        const filesEl = context.root.querySelector('[data-sidebar-files]');
        if (!filesEl) return;
        filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">Loading...</div>';
        try {
            const files: WorkspaceFileEntry[] = [];
            async function traverse(handle: any, relativePath = ''): Promise<void> {
                for await (const entry of handle.values()) {
                    const fullPath = relativePath ? `${relativePath}/${entry.name}` : entry.name;
                    if (entry.kind === 'file') {
                        if (entry.name.endsWith('.etlsql') || entry.name.endsWith('.rptsql') || entry.name.endsWith('.sql')) {
                            files.push({
                                path: fullPath,
                                name: entry.name,
                                handle: entry
                            });
                        }
                    } else if (entry.kind === 'directory') {
                        await traverse(entry, fullPath);
                    }
                }
            }
            await traverse(dirHandle);
            files.sort((a, b) => a.path.localeCompare(b.path));
            if (files.length === 0) {
                filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">No script files.</div>';
                return;
            }
            filesEl.innerHTML = '';
            files.forEach(f => {
                const item = document.createElement('div');
                item.className = 'etlsql-sidebar-file';
                item.innerHTML = `<span class="etlsql-tree-label">${escapeHtml(f.path)}</span>`;
                if (f.path === context.currentFilePath) item.classList.add('active');
                item.addEventListener('click', async () => {
                    filesEl.querySelectorAll('.etlsql-sidebar-file').forEach(e => e.classList.remove('active'));
                    item.classList.add('active');
                    try {
                        const file = await f.handle.getFile();
                        const content = await file.text();
                        context.editor.setValue(content);
                        context.currentFilePath = f.path;
                        context.activeFileHandle = f.handle;
                        const titleEl = context.root.querySelector('.etlsql-script-workbench-toolbar strong');
                        if (titleEl) titleEl.textContent = f.name || f.path;
                    } catch (e: any) {
                        _feedback?.notify?.('Failed to read file: ' + e.message, { title: 'File not loaded', tone: 'error' });
                    }
                });
                filesEl.appendChild(item);
            });
        } catch (err: any) {
            filesEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    function metadataApiBase(): string {
        const runUrl = context.opts.runUrl || '';
        return runUrl.includes('/api/designer/run') ? runUrl.split('/api/designer/run')[0] : '';
    }

    function makeDraggable(el: HTMLElement, snippet: string): void {
        el.draggable = true;
        el.title = `Drag into the editor to insert "${snippet}"`;
        el.addEventListener('dragstart', (event: DragEvent) => {
            event.stopPropagation();
            if (event.dataTransfer) {
                event.dataTransfer.setData(SNIPPET_MIME, snippet);
                event.dataTransfer.setData('text/plain', snippet);
                event.dataTransfer.effectAllowed = 'copy';
            }
            el.classList.add('dragging');
        });
        el.addEventListener('dragend', () => el.classList.remove('dragging'));
    }

    function makeColumnRow(column: SchemaColumnEntry, snippet: string): HTMLElement {
        const row = document.createElement('div');
        row.className = 'etlsql-tree-row etlsql-tree-column';
        const type = column.type ?? column.dataType ?? '';
        row.innerHTML = `<span class="etlsql-tree-indent"></span><span class="etlsql-tree-label">${escapeHtml(column.name)}</span>`
            + (type ? `<span class="etlsql-tree-type">${escapeHtml(type)}</span>` : '');
        makeDraggable(row, snippet);
        return row;
    }

    // Builds a collapsible node. `loadChildren` runs once, on first expand.
    function makeTreeNode(treeOpts: TreeNodeOptions): HTMLElement {
        const { label, icon, className, snippet, loadChildren, preview } = treeOpts;
        const node = document.createElement('div');
        node.className = 'etlsql-tree-node';

        const header = document.createElement('div');
        header.className = `etlsql-tree-row etlsql-tree-header ${className || ''}`;
        header.innerHTML = `<span class="etlsql-tree-caret">▶</span><span class="etlsql-tree-icon">${icon || ''}</span><span class="etlsql-tree-label">${escapeHtml(label)}</span>`;

        if (preview && context.opts.dataPreviewUrl) {
            const action = document.createElement('button');
            action.type = 'button';
            action.className = 'etlsql-tree-preview-action';
            action.textContent = 'Preview rows';
            action.title = `Preview bounded rows from ${label}`;
            action.setAttribute('aria-label', `Preview rows from ${label}`);
            action.addEventListener('click', async (event) => {
                event.preventDefault();
                event.stopPropagation();
                await previewRows(preview, action);
            });
            header.appendChild(action);
        }

        const children = document.createElement('div');
        children.className = 'etlsql-tree-children';

        let loaded = false;
        header.addEventListener('click', async (event) => {
            event.stopPropagation();
            const expanded = node.classList.toggle('expanded');
            if (expanded && !loaded) {
                loaded = true;
                children.innerHTML = '<div class="etlsql-tree-note">Loading…</div>';
                try {
                    await loadChildren?.(children);
                } catch (err: any) {
                    children.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
                    loaded = false;
                }
            }
        });

        if (snippet) makeDraggable(header, snippet);
        node.append(header, children);
        return node;
    }

    async function previewRows(source: any, action: HTMLElement): Promise<void> {
        if (context.dataPreviewAbort) {
            context.dataPreviewAbort.abort();
            return;
        }

        const abort = new AbortController();
        context.dataPreviewAbort = abort;
        const originalText = action.textContent || '';
        action.textContent = 'Cancel';
        action.classList.add('is-loading');
        context.resultsPanel.clear();
        context.resultsPanel.startElapsed();
        context.resultsPanel.replay([
            { type: 'status', status: 'Previewing' },
            { type: 'message', level: 'sys', text: `Reading bounded rows from ${source.tempTable || `${source.connection}.${source.table}`}…` },
        ]);

        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const response = await fetcher(context.opts.dataPreviewUrl!, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: abort.signal,
                body: JSON.stringify(buildDataPreviewPayload(source, context.editor.getValue(), context.getDocumentUri())),
            });
            if (!response?.ok) {
                let detail = `HTTP ${response?.status ?? 0}`;
                try {
                    const problem = await response.json();
                    detail = problem?.error || detail;
                } catch { /* keep the status */ }
                throw new Error(detail);
            }

            const result = await response.json();
            const label = result.sourceKind === 'temp' ? 'Session temp' : 'Governed source';
            context.resultsPanel.replay([
                {
                    type: 'results',
                    columns: result.columns || [],
                    rows: result.rows || [],
                    context: {
                        kind: result.sourceKind,
                        label,
                        source: result.source,
                        elapsedMs: result.elapsedMs,
                        capped: result.capped,
                        byteCapped: result.byteCapped,
                    },
                },
                { type: 'message', level: 'info', text: result.message || 'Preview complete.' },
                { type: 'done', exitCode: 0, status: result.message || 'Preview complete' },
            ]);
        } catch (error: any) {
            const cancelled = abort.signal.aborted || error?.name === 'AbortError';
            context.resultsPanel.replay([
                { type: 'message', level: cancelled ? 'sys' : 'error', text: cancelled ? 'Preview cancelled.' : `Preview failed: ${error.message}` },
                { type: 'done', exitCode: cancelled ? 0 : 1, status: cancelled ? 'Preview cancelled' : 'Preview failed' },
            ]);
        } finally {
            context.resultsPanel.stopElapsed();
            if (context.dataPreviewAbort === abort) context.dataPreviewAbort = null;
            action.textContent = originalText;
            action.classList.remove('is-loading');
        }
    }

    function scheduleSidebarRefresh(): void {
        if (!context.showSchema && !context.showSession) return;
        clearTimeout(sidebarRefreshTimer);
        sidebarRefreshTimer = setTimeout(() => {
            if (context.showSchema) loadSchema();
            if (context.showSession) loadSession();
        }, 200);
    }

    async function loadSchema(): Promise<void> {
        const schemaEl = context.root.querySelector('[data-sidebar-schema]');
        if (!schemaEl) return;
        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const docUri = context.getDocumentUri();

            const res = await fetcher(`${apiBase}/api/session/metadata?documentUri=${encodeURIComponent(docUri)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data: SchemaMetadataResult = await res.json();
            const signature = JSON.stringify(data?.connections ?? []);
            if (signature === schemaSignature) return;
            schemaSignature = signature;
            if (data && data.connections && data.connections.length > 0) {
                schemaEl.innerHTML = '';
                data.connections.forEach(conn => {
                    schemaEl.appendChild(makeTreeNode({
                        label: conn,
                        icon: '🔌',
                        className: 'etlsql-tree-connection',
                        loadChildren: async (host: HTMLElement) => {
                            const schemaRes = await fetcher(`${apiBase}/api/designer/schema?connection=${encodeURIComponent(conn)}&documentUri=${encodeURIComponent(docUri)}`);
                            if (!schemaRes.ok) throw new Error(`HTTP ${schemaRes.status}`);
                            const schemaData = await schemaRes.json();
                            const tables: SchemaTableEntry[] = schemaData?.tables ?? [];
                            if (!tables.length) {
                                host.innerHTML = '<div class="etlsql-tree-note">No tables or views.</div>';
                                return;
                            }
                            host.innerHTML = '';
                            for (const table of tables) {
                                host.appendChild(makeTreeNode({
                                    label: table.name,
                                    icon: '▤',
                                    className: 'etlsql-tree-table',
                                    snippet: `${conn}.${table.name}`,
                                    preview: { sourceKind: 'connection', connection: conn, table: table.name },
                                    loadChildren: (columnHost: HTMLElement) => {
                                        const columns = (table.columns ?? []).map(c => typeof c === 'string' ? { name: c } : c);
                                        if (!columns.length) {
                                            columnHost.innerHTML = '<div class="etlsql-tree-note">No columns</div>';
                                            return;
                                        }
                                        columnHost.innerHTML = '';
                                        for (const column of columns) {
                                            columnHost.appendChild(makeColumnRow(column, column.name));
                                        }
                                    },
                                }));
                            }
                        },
                    }));
                });
            } else {
                schemaEl.innerHTML = '<div class="etlsql-tree-note">No active connections.</div>';
            }
        } catch (err: any) {
            schemaSignature = null;
            schemaEl.innerHTML = `<div style="color:var(--portal-danger, #ff7b72);">${escapeHtml(err.message)}</div>`;
        }
    }

    async function loadSession(): Promise<void> {
        const varsEl = context.root.querySelector('[data-sidebar-variables]');
        if (!varsEl) return;
        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const docUri = context.getDocumentUri();

            const res = await fetcher(`${apiBase}/api/session/metadata?documentUri=${encodeURIComponent(docUri)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data: SchemaMetadataResult = await res.json();
            const signature = JSON.stringify([data?.variables ?? [], data?.tempTables ?? []]);
            if (signature === sessionSignature) return;
            sessionSignature = signature;
            const variables = data?.variables ?? [];
            const tempTables = data?.tempTables ?? [];
            varsEl.innerHTML = '';

            for (const variable of variables) {
                const row = document.createElement('div');
                row.className = 'etlsql-tree-row etlsql-tree-variable';
                row.innerHTML = `<span class="etlsql-tree-icon">@</span><span class="etlsql-tree-label">${escapeHtml(variable.name)}</span>`
                    + `<span class="etlsql-tree-value">${escapeHtml(String(variable.value ?? ''))}</span>`
                    + (variable.type ? `<span class="etlsql-tree-type">${escapeHtml(variable.type)}</span>` : '');
                makeDraggable(row, variable.name);
                varsEl.appendChild(row);
            }

            // Temp tables expand to their columns exactly like a schema table does.
            for (const table of tempTables) {
                varsEl.appendChild(makeTreeNode({
                    label: table.name,
                    icon: '▦',
                    className: 'etlsql-tree-temp',
                    snippet: table.name,
                    preview: { sourceKind: 'temp', connection: context.opts.connectionRef || null, tempTable: table.name },
                    loadChildren: (columnHost: HTMLElement) => {
                        const columns = (table.columns ?? []).map(c => (typeof c === 'string' ? { name: c, type: '' } : c));
                        if (!columns.length) {
                            columnHost.innerHTML = '<div class="etlsql-tree-note">No columns</div>';
                            return;
                        }
                        columnHost.innerHTML = '';
                        for (const column of columns) {
                            columnHost.appendChild(makeColumnRow(column, column.name));
                        }
                    },
                }));
            }

            if (!variables.length && !tempTables.length) {
                varsEl.innerHTML = '<div class="etlsql-tree-note">No variables/temp tables.</div>';
            }
        } catch (err: any) {
            sessionSignature = null;
            varsEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    function hideGitSection(): void {
        // Not every host exposes source control (see the Git Integration item in the
        // Unified Script Editor Roadmap). Hide the section rather than parking a fetch
        // error in the sidebar.
        context.root.querySelector('[data-sidebar-git]')?.remove();
        context.root.querySelector('[data-sidebar-git-header]')?.remove();
    }

    async function loadGit(): Promise<void> {
        const gitEl = context.root.querySelector('[data-sidebar-git]');
        const branchBadge = context.root.querySelector('[data-workbench-branch]');
        if (!gitEl) return;
        try {
            const fetcher = context.opts.authFetch ?? fetch;
            const res = await fetcher(context.opts.gitStatusUrl || '/api/git/status');
            if (!res.ok) { hideGitSection(); if (branchBadge) asHtml(branchBadge).style.display = 'none'; return; }
            const data: GitStatusResult = await res.json();
            if (data && (data.branch || data.isGitRepository !== false)) {
                const branchName = data.branch || context.opts.gitStatus?.branch || '';
                if (branchBadge) {
                    if (branchName) {
                        branchBadge.textContent = `🌿 ${branchName}`;
                        asHtml(branchBadge).style.display = 'inline-block';
                    } else {
                        asHtml(branchBadge).style.display = 'none';
                    }
                }

                let gitHtml = `<div class="etlsql-tree-row etlsql-tree-header">🌿 ${escapeHtml(branchName)}</div>`;
                if (data.staged && data.staged.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Staged</div>';
                    gitHtml += data.staged.map(f => `<div class="etlsql-tree-row" style="color:var(--portal-success, #117853);">✓ ${escapeHtml(f)}</div>`).join('');
                }
                if (data.modified && data.modified.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Modified</div>';
                    gitHtml += data.modified.map(f => `<div class="etlsql-tree-row" style="color:var(--portal-warning, #a05a00);">📝 ${escapeHtml(f)}</div>`).join('');
                }
                if (data.untracked && data.untracked.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Untracked</div>';
                    gitHtml += data.untracked.map(f => `<div class="etlsql-tree-row">➕ ${escapeHtml(f)}</div>`).join('');
                }
                gitHtml += `
                    <input type="text" data-git-comment placeholder="Commit message..." style="background:var(--portal-surface, #0f141b); color:var(--portal-text, #e6edf3); border:1px solid var(--portal-border, #30363d); padding:4px 6px; border-radius:4px; font-size:11px; margin-top:6px; outline:none; width: 100%;">
                    <button type="button" class="btn btn-sm btn-primary" data-git-commit style="margin-top:4px; font-size:11px; font-weight:600; padding:4px; width: 100%;">Commit Changes</button>
                `;
                gitEl.innerHTML = gitHtml;

                const commitBtn = asButton(gitEl.querySelector('[data-git-commit]'));
                const commentInput = asInput(gitEl.querySelector('[data-git-comment]'));
                commitBtn?.addEventListener('click', async () => {
                    const comment = commentInput?.value || '';
                    if (!comment.trim()) {
                        _feedback?.notify?.('Enter a commit message before committing.', { title: 'Commit message required', tone: 'warning' });
                        commentInput?.focus();
                        return;
                    }
                    if (commitBtn) commitBtn.disabled = true;
                    try {
                        const cRes = await fetcher('/api/git/commit', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ comment })
                        });
                        const cData = await cRes.json();
                        if (cData.committed) {
                            _feedback?.notify?.(`Revision ${cData.sourceRevision || cData.rev || ''} was committed.`, { title: 'Commit completed', tone: 'success', auditAction: 'designer.source.commit' });
                            await loadGit();
                            await loadFiles();
                        } else {
                            _feedback?.notify?.(cData.message || 'Nothing to commit.', { title: 'No commit created', tone: 'info' });
                        }
                    } catch (e: any) {
                        _feedback?.notify?.('Commit failed: ' + e.message, { title: 'Commit failed', tone: 'error' });
                    } finally {
                        if (commitBtn) commitBtn.disabled = false;
                    }
                });
            } else {
                hideGitSection();
                if (branchBadge) asHtml(branchBadge).style.display = 'none';
            }
        } catch {
            hideGitSection();
            if (branchBadge) asHtml(branchBadge).style.display = 'none';
        }
    }

    return { loadFiles, renderDirectoryTree, metadataApiBase, scheduleSidebarRefresh, loadSchema, loadSession, loadGit };
}
