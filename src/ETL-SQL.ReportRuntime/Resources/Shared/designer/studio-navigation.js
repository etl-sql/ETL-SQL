/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-navigation.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Studio shell, home screen, projection, activity navigation, and modal focus.
 */
import { _documentKindLabel, _escapeHtml, _feedback, _fileIcon, _studioIcon, asHtml, queryElement, queryElements } from './studio-context.js';
export function createStudioNavigation(hostContext) {
    function mountShell() {
        hostContext.container.innerHTML = `
        <div class="etlsql-studio-shell">
            <!-- Studio Header Toolbar -->
            <header class="etlsql-studio-header">
                <div class="etlsql-studio-brand">
                    <span class="etlsql-studio-logo">${_studioIcon('palette', 18)}</span>
                    <span class="etlsql-studio-title">ETL-SQL Studio</span>
                </div>

                <!-- Document Tabs Area -->
                <div class="etlsql-studio-tabbar" data-studio-tabbar>
                    <button type="button" class="etlsql-studio-tab-scroll-btn" data-studio-scroll="left" title="Scroll tabs left" aria-label="Scroll tabs left" style="display:none;">${_studioIcon('chevronLeft', 12)}</button>
                    <div class="etlsql-studio-tabs" data-studio-tabs role="tablist" aria-label="Open documents"></div>
                    <button type="button" class="etlsql-studio-tab-scroll-btn" data-studio-scroll="right" title="Scroll tabs right" aria-label="Scroll tabs right" style="display:none;">${_studioIcon('chevronRight', 12)}</button>
                    <div class="etlsql-tab-new-wrapper">
                        <button type="button" class="etlsql-studio-tab-new" data-studio-new-tab title="New File or Pipeline (Ctrl+N)">${_studioIcon('plus', 14)}</button>
                        <button type="button" class="etlsql-studio-tab-overflow-btn" data-studio-overflow-btn title="Open Tabs List" aria-label="Show open tabs dropdown">${_studioIcon('chevronDown', 12)}</button>
                    </div>
                    <div class="etlsql-studio-tab-dropdown" data-studio-tab-dropdown hidden></div>
                </div>

                <div class="etlsql-studio-header-spacer"></div>

                <!-- Projection View Toggles -->
                <div class="etlsql-studio-projection-group" role="group" aria-label="View Projection">
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="canvas" title="Canvas View (WYSIWYG Layout)">
                        <span class="etlsql-icon">${_studioIcon('canvas', 14)}</span> Canvas
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle active" data-projection="split" title="Split View (Visual + Code)">
                        <span class="etlsql-icon">${_studioIcon('split', 14)}</span> Split
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="code" title="Code View (CodeMirror 6)">
                        <span class="etlsql-icon">${_studioIcon('code', 14)}</span> Code
                    </button>
                    <button type="button" class="etlsql-studio-btn-toggle" data-projection="model" title="Data Model View (Connections, Tables, Relationships)">
                        <span class="etlsql-icon">${_studioIcon('catalog', 14)}</span> Model
                    </button>
                </div>

                <div class="etlsql-studio-header-divider"></div>

                <!-- Global Action Controls -->
                <div class="etlsql-studio-actions">
                    <button type="button" class="etlsql-studio-btn" data-action="theme" title="Toggle Theme">
                        ${_studioIcon('theme', 14)}
                    </button>
                    <button type="button" class="etlsql-studio-btn" data-action="save" title="Save File (Ctrl+S)">
                        ${_studioIcon('save', 14)} Save
                    </button>
                    <button type="button" class="etlsql-studio-btn" data-action="publish" title="Publish report to Portal Catalog" ${hostContext.opts.onCreateDocument ? '' : 'style="display:none;"'}>
                        ${_studioIcon('catalog', 14)} Publish
                    </button>
                    <button type="button" class="etlsql-studio-btn btn-primary" data-action="run" title="Run Script (Ctrl+Shift+Enter)">
                        ${_studioIcon('run', 14)} Run
                    </button>
                    <button type="button" class="etlsql-studio-btn btn-danger" data-action="stop" title="Stop Script Execution" style="display:none;">
                        ${_studioIcon('stop', 14)} Stop
                    </button>
                    ${hostContext.opts.onExit ? `<button type="button" class="etlsql-studio-btn" data-action="exit" title="Exit Studio and stop this project host">
                        ${_studioIcon('close', 14)} Exit Studio
                    </button>` : ''}
                </div>
            </header>

            <div class="etlsql-studio-preview-banner" data-studio-preview-banner role="status" hidden></div>

            <!-- Workbench Body -->
            <div class="etlsql-studio-body">
                <!-- Far-Left Activity Rail -->
                <nav class="etlsql-studio-activity-rail" aria-label="Activity Rail">
                    <button type="button" class="etlsql-studio-rail-btn active" data-activity="explorer" title="Explorer (Files)">
                        ${_studioIcon('explorer', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="catalog" title="Data Catalog (Connections)">
                        ${_studioIcon('catalog', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="palette" title="Visual Palette (Add Components)">
                        ${_studioIcon('palette', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="outline" title="Outline (Pages, Containers, Visuals)">
                        ${_studioIcon('outline', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="engine" title="Engine State (Scope and Query Plan)">
                        ${_studioIcon('engine', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="governance" title="Governance (Tags, Inherited Lineage, Policy)">
                        ${_studioIcon('governance', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="filters" title="Filter Pane (Slicers & Ranges)">
                        ${_studioIcon('filters', 18)}
                    </button>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="git" title="Source Control (Git)">
                        ${_studioIcon('git', 18)}
                    </button>
                    <div class="etlsql-studio-rail-spacer"></div>
                    <button type="button" class="etlsql-studio-rail-btn" data-activity="settings" title="Settings">
                        ${_studioIcon('settings', 18)}
                    </button>
                </nav>

                <!-- Activity Sidebar Panel -->
                <aside class="etlsql-studio-sidebar" data-studio-sidebar>
                    <div class="etlsql-studio-sidebar-header">
                        <span data-sidebar-title>Explorer</span>
                        <button type="button" class="etlsql-studio-sidebar-close" data-sidebar-close title="Close Sidebar">${_studioIcon('close', 12)}</button>
                    </div>
                    <div class="etlsql-studio-sidebar-content" data-sidebar-content></div>
                    <div class="etlsql-studio-inspector" data-studio-inspector>
                        <button type="button" class="etlsql-studio-properties-back" data-properties-back>${_studioIcon('back', 13)} Visual library</button>
                        <div class="etlsql-studio-property-fields" data-property-fields></div>
                        <div data-properties-host></div>
                    </div>
                </aside>

                <aside class="etlsql-studio-sidebar etlsql-studio-filter-sidebar collapsed" data-filter-sidebar aria-label="Filters">
                    <div class="etlsql-studio-sidebar-header">
                        <span>Filters</span>
                        <button type="button" class="etlsql-studio-sidebar-close" data-filter-sidebar-close title="Close Filters" aria-label="Close Filters">${_studioIcon('close', 12)}</button>
                    </div>
                    <div class="etlsql-studio-sidebar-content" data-filter-sidebar-content></div>
                </aside>

                <!-- Center Multi-Projection Stage -->
                <main class="etlsql-studio-stage" data-studio-stage>
                    <!-- Home / Welcome Stage -->
                    <div class="etlsql-studio-home-stage" data-home-stage style="display:none; flex:1; width:100%; height:100%; overflow:hidden;"></div>

                    <!-- Visual Stage Area (Report Builder Canvas & Pipeline DAG) -->
                    <div class="etlsql-studio-visual-stage" data-visual-stage style="display:flex; flex-direction:column; flex:1; height:100%; overflow:hidden; position:relative;">
                        <div class="etlsql-studio-workflow-bar" data-workflow-bar hidden></div>
                        <div class="etlsql-studio-designer-container" data-canvas-grid-container style="flex:1; width:100%; height:100%; overflow:hidden; position:relative;"></div>
                    </div>

                    <!-- Split Resizer Bar -->
                    <div class="etlsql-studio-stage-resizer" data-stage-resizer role="separator" aria-orientation="horizontal" tabindex="0" aria-label="Split view proportion resizer" aria-valuenow="50" aria-valuemin="15" aria-valuemax="85" title="Drag or use arrow keys to resize split panes"></div>

                    <!-- CodeMirror 6 Stage Area -->
                    <div class="etlsql-studio-code-stage" data-code-stage>
                        <div class="etlsql-studio-code-toolbar" role="toolbar" aria-label="Script actions">
                            <strong>Script</strong><span class="etlsql-studio-code-status">Live projection</span><span class="etlsql-studio-code-toolbar-spacer"></span>
                            <button type="button" class="etlsql-studio-btn etlsql-studio-btn-syntax" data-action="syntax-bridge" title="Visual-to-Script Learning Bridge: see generated syntax and canonical language help" style="display:none;">${_studioIcon('syntax', 14)} Syntax Helper</button>
                            <button type="button" class="etlsql-studio-btn" data-action="code-format">${_studioIcon('format', 14)} Format</button>
                            <button type="button" class="etlsql-studio-btn" data-action="run-selected">${_studioIcon('runSelected', 14)} Run selected</button>
                            <button type="button" class="etlsql-studio-btn btn-primary" data-action="code-run">${_studioIcon('run', 14)} Run all</button>
                            <button type="button" class="etlsql-studio-btn btn-danger" data-action="code-stop" title="Stop Script Execution" style="display:none;">${_studioIcon('stop', 14)} Stop</button>
                        </div>
                        <div class="etlsql-studio-syntax-bridge" data-syntax-bridge style="display:none;"></div>
                        <div class="etlsql-studio-editor-host etlsql-editor-container" data-editor-host></div>
                        <div class="etlsql-studio-results-host" data-results-host></div>
                    </div>
                </main>
            </div>

            <!-- Save / Secret Passphrase Modal Container -->
            <div class="etlsql-studio-modal-backdrop" data-modal-backdrop hidden>
                <div class="etlsql-studio-modal" data-modal-box></div>
            </div>
        </div>
    `;
    }
    function applySplitPct(pct) {
        hostContext.currentSplitPct = Math.max(15, Math.min(85, Math.round(pct)));
        hostContext.visualStage.style.flex = `0 0 ${hostContext.currentSplitPct}%`;
        hostContext.codeStage.style.flex = `0 0 ${100 - hostContext.currentSplitPct}%`;
        hostContext.resizer?.setAttribute('aria-valuenow', String(hostContext.currentSplitPct));
    }
    function setupModalAccessibility(box, backdrop, onClose, titleId) {
        const previouslyFocused = document.activeElement;
        box.setAttribute('role', 'dialog');
        box.setAttribute('aria-modal', 'true');
        if (titleId) {
            box.setAttribute('aria-labelledby', titleId);
        }
        backdrop.hidden = false;
        const onKeyDown = (event) => {
            if (event.key === 'Escape') {
                event.stopPropagation();
                event.preventDefault();
                cleanup();
                onClose();
                return;
            }
            if (event.key === 'Tab') {
                const focusable = Array.from(box.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')).map(asHtml).filter(el => el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement);
                if (focusable.length === 0) {
                    event.preventDefault();
                    return;
                }
                const first = asHtml(focusable[0]);
                const last = asHtml(focusable[focusable.length - 1]);
                if (event.shiftKey) {
                    if (document.activeElement === first || !box.contains(document.activeElement)) {
                        event.preventDefault();
                        last.focus();
                    }
                }
                else {
                    if (document.activeElement === last || !box.contains(document.activeElement)) {
                        event.preventDefault();
                        first.focus();
                    }
                }
            }
        };
        const onBackdropClick = (event) => {
            if (event.target === backdrop) {
                cleanup();
                onClose();
            }
        };
        let cleaned = false;
        const cleanup = () => {
            if (cleaned)
                return;
            cleaned = true;
            document.removeEventListener('keydown', onKeyDown, true);
            backdrop.removeEventListener('click', onBackdropClick);
            backdrop.hidden = true;
            box.removeAttribute('role');
            box.removeAttribute('aria-modal');
            box.removeAttribute('aria-labelledby');
            box.removeAttribute('aria-label');
            asHtml(previouslyFocused)?.focus?.();
        };
        document.addEventListener('keydown', onKeyDown, true);
        backdrop.addEventListener('click', onBackdropClick);
        return cleanup;
    }
    function setProjection(mode) {
        if (hostContext.state.activeDocId === '__home__')
            return;
        hostContext.homeStage.style.display = 'none';
        const doc = hostContext.getActiveDoc();
        // Model is the only projection that replaces what the stage holds rather than resizing it,
        // so crossing that boundary in either direction has to repaint. Repainting on every toggle
        // instead would re-run the pipeline projection's fetches for a change that only moved a
        // splitter.
        const wasModel = doc?.projection === 'model';
        if (doc)
            doc.projection = mode;
        const crossesModelBoundary = wasModel !== (mode === 'model');
        queryElements(hostContext.shell, '[data-projection]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.projection === mode);
        });
        if (mode === 'canvas' || mode === 'model') {
            hostContext.visualStage.style.display = 'flex';
            hostContext.visualStage.style.flex = '1';
            hostContext.codeStage.style.display = 'none';
            hostContext.resizer.style.display = 'none';
        }
        else if (mode === 'code') {
            hostContext.visualStage.style.display = 'none';
            hostContext.codeStage.style.display = 'flex';
            hostContext.codeStage.style.flex = '1';
            hostContext.resizer.style.display = 'none';
        }
        else {
            hostContext.visualStage.style.display = 'flex';
            hostContext.codeStage.style.display = 'flex';
            hostContext.resizer.style.display = 'block';
            applySplitPct(hostContext.currentSplitPct);
        }
        if (crossesModelBoundary)
            hostContext.renderVisualStage();
        if (hostContext.state.editorInstance?.focus) {
            hostContext.state.editorInstance.focus();
        }
    }
    function renderStudioHome() {
        hostContext.visualStage.style.display = 'none';
        hostContext.codeStage.style.display = 'none';
        hostContext.resizer.style.display = 'none';
        hostContext.homeStage.style.display = 'flex';
        const catalogMode = Boolean(hostContext.opts.onOpenDocument);
        const files = catalogMode
            ? hostContext.state.catalogReports.map((report) => {
                const filename = /\.rptsql$/i.test(report.name || '') ? (report.name || '') : `${report.name || ''}.rptsql`;
                return { ...report, path: `${report.folderPath || ''}/${filename}`.replace(/^\//, '') };
            })
            : hostContext.state.workspaceFiles || [];
        hostContext.homeStage.innerHTML = `
            <div class="etlsql-studio-home">
                <section class="etlsql-studio-home-hero">
                    <div class="etlsql-studio-home-hero-content">
                        <div class="etlsql-studio-kicker">Unified Authoring Workbench</div>
                        <h1>ETL-SQL Studio</h1>
                        <p>Design interactive report dashboards, author cross-source data pipelines, and manage database connections in a portable, zero-trust workspace.</p>
                    </div>
                    <div class="etlsql-studio-home-quick-actions">
                        <button type="button" class="etlsql-home-action-card primary" data-create-from-home="dashboard" data-seed-sample>
                            <span class="etlsql-home-card-icon">${_studioIcon('canvas', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Start with sample data</strong>
                                <span>Opens a working dashboard on the built-in MOCKDB sample connector &mdash; no database or connection needed. The best place to start.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card primary secondary" data-create-from-home="etl" data-seed-sample>
                            <span class="etlsql-home-card-icon">${_studioIcon('catalog', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Sample ETL pipeline</strong>
                                <span>Guided multi-step pipeline on built-in MOCKDB: stage into #temp tables, transform, validate, and cleanup &mdash; runs in practice mode.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card workflow-dashboard" data-create-from-home="dashboard">
                            <span class="etlsql-home-card-icon"><span class="etlsql-home-dashboard-glyph" aria-hidden="true"><i></i><i></i><i></i></span></span>
                            <div class="etlsql-home-card-info">
                                <strong>New Dashboard</strong>
                                <span>Responsive visual board for charts, KPI cards, tables, slicers, cross-filters, and freeform layout.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card workflow-paginated" data-create-from-home="paginated">
                            <span class="etlsql-home-card-icon"><span class="etlsql-home-page-glyph" aria-hidden="true"><i></i><i></i><i></i></span></span>
                            <div class="etlsql-home-card-info">
                                <strong>New Paginated Report</strong>
                                <span>Physical pages with parameters, groups, detail rows, totals, headers, footers, breaks, preview, and export.</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card secondary ${catalogMode ? 'etlsql-home-card-disabled' : ''}" data-create-from-home="etl" ${catalogMode ? 'aria-disabled="true" title="Catalog pipeline authoring is coming soon. Open in Workstation Editor for .etlsql pipelines."' : ''}>
                            <span class="etlsql-home-card-icon">${_studioIcon('catalog', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Blank pipeline (.etlsql)${catalogMode ? ' <span class="etlsql-card-type-pill" style="font-size:9px; vertical-align:middle; margin-left:6px; opacity:0.85;">Workstation only</span>' : ''}</strong>
                                <span>${catalogMode ? 'Catalog pipeline authoring is coming soon. Use the Workstation Editor or VS Code extension for ETL pipeline development.' : 'Multi-step data movement: stage into #temp tables, transform, validate, and load. Opens with the pipeline canvas.'}</span>
                            </div>
                        </button>
                        <button type="button" class="etlsql-home-action-card tertiary ${catalogMode ? 'etlsql-home-card-disabled' : ''}" data-create-from-home="sql" ${catalogMode ? 'aria-disabled="true" title="Catalog query authoring is coming soon. Open in Workstation Editor for .etlsql scripts."' : ''}>
                            <span class="etlsql-home-card-icon">${_studioIcon('code', 24)}</span>
                            <div class="etlsql-home-card-info">
                                <strong>Blank query (.etlsql)${catalogMode ? ' <span class="etlsql-card-type-pill" style="font-size:9px; vertical-align:middle; margin-left:6px; opacity:0.85;">Workstation only</span>' : ''}</strong>
                                <span>${catalogMode ? 'Catalog query authoring is coming soon. Use the Workstation Editor or VS Code extension for ETL script authoring.' : 'Same file type as a pipeline, opened straight into the script editor with no canvas.'}</span>
                            </div>
                        </button>
                    </div>
                </section>

                <section class="etlsql-studio-home-recent">
                    <div class="etlsql-studio-recent-header">
                        <h2>${catalogMode ? 'Catalog Reports' : 'Workspace Files'}</h2>
                        <span class="etlsql-recent-count">${files.length} available ${catalogMode ? 'report' : 'script'}${files.length === 1 ? '' : 's'}</span>
                    </div>
                    ${files.length === 0 ? `
                        <div style="padding:24px; text-align:center; color:var(--portal-text-soft,#8b949e); background:var(--portal-surface,#161b22); border:1px dashed var(--portal-border,#30363d); border-radius:8px;">
                            <p style="margin:0 0 12px; font-size:0.875rem;">No existing ${catalogMode ? 'reports are available in the catalog' : 'scripts found in this workspace directory'}.</p>
                            <p style="margin:0; font-size:0.75rem; color:var(--portal-muted,#8b949e);">Choose <strong>New Dashboard</strong>, <strong>New Paginated Report</strong>, or <strong>New ETL Pipeline</strong> above.</p>
                        </div>
                    ` : `
                        <div class="etlsql-studio-recent-grid">
                            ${files.map((f) => {
            const ext = (f.path || '').split('.').pop()?.toLowerCase();
            const isRpt = ext === 'rptsql';
            const isEtl = ext === 'etlsql';
            const openDoc = hostContext.state.documents.find(document => document.path === f.path) || null;
            const typePill = _documentKindLabel(f.path || '', openDoc);
            const name = (f.path || '').split('/').pop()?.split('\\').pop() || '';
            const sizeKb = f.size ? `${(f.size / 1024).toFixed(1)} KB` : '';
            return `
                                    <div class="etlsql-studio-recent-card">
                                        <div class="etlsql-recent-card-top">
                                            <span class="etlsql-card-type-pill" style="font-size:9px;" title="${_escapeHtml(isRpt ? 'Report-SQL (.rptsql)' : isEtl ? 'ETL-SQL (.etlsql)' : 'SQL')}">${typePill}</span>
                                            <div class="etlsql-recent-card-meta">
                                                ${sizeKb ? `<span style="font-size:10px; color:var(--portal-muted,#8b949e);">${sizeKb}</span>` : ''}
                                                ${catalogMode ? '' : `<button type="button" class="etlsql-recent-card-dismiss" data-dismiss-file="${_escapeHtml(f.path)}" title="Remove from Studio Home" aria-label="Remove ${_escapeHtml(name)} from Studio Home">${_studioIcon('close', 10)}</button>`}
                                            </div>
                                        </div>
                                        <div class="etlsql-recent-card-title" title="${_escapeHtml(f.path)}">
                                            <span>${_fileIcon(f.path || '')}</span>
                                            <span>${_escapeHtml(name)}</span>
                                        </div>
                                        <div class="etlsql-recent-card-path" title="${_escapeHtml(f.path)}">${_escapeHtml(f.path)}</div>
                                        <div class="etlsql-recent-card-actions">
                                            <button type="button" class="etlsql-recent-card-btn" ${catalogMode ? `data-open-report="${_escapeHtml(f.id)}"` : `data-open-file="${_escapeHtml(f.path)}"`} data-open-proj="split">
                                                ${_studioIcon('canvas', 12)} Design
                                            </button>
                                            <button type="button" class="etlsql-recent-card-btn" ${catalogMode ? `data-open-report="${_escapeHtml(f.id)}"` : `data-open-file="${_escapeHtml(f.path)}"`} data-open-proj="code">
                                                ${_studioIcon('code', 12)} Code
                                            </button>
                                        </div>
                                    </div>
                                `;
        }).join('')}
                        </div>
                    `}
                </section>
            </div>
        `;
        queryElements(hostContext.homeStage, '[data-create-from-home]').forEach(b => {
            b.addEventListener('click', () => {
                if (b.getAttribute('aria-disabled') === 'true') {
                    _feedback.notify('Portal catalog currently supports Report-SQL (.rptsql) documents. Use the Workstation Editor or VS Code extension for ETL pipeline (.etlsql) authoring.', { title: 'Catalog Support Coming Soon', tone: 'info' });
                    return;
                }
                hostContext.createNewFile(b.dataset.createFromHome, { seed: b.hasAttribute('data-seed-sample') });
            });
        });
        queryElements(hostContext.homeStage, '[data-open-file]').forEach(b => {
            b.addEventListener('click', async () => {
                const filePath = b.dataset.openFile;
                const proj = b.dataset.openProj || 'split';
                await hostContext.openWorkspaceFile(filePath, proj);
            });
        });
        queryElements(hostContext.homeStage, '[data-open-report]').forEach(b => {
            b.addEventListener('click', async () => {
                const report = hostContext.state.catalogReports.find((item) => String(item.id) === b.dataset.openReport);
                if (report)
                    await hostContext.openCatalogReport(report, b.dataset.openProj || 'split');
            });
        });
        queryElements(hostContext.homeStage, '[data-dismiss-file]').forEach(button => {
            button.addEventListener('click', () => {
                const filePath = button.dataset.dismissFile;
                hostContext.state.workspaceFiles = hostContext.state.workspaceFiles.filter((file) => file.path !== filePath);
                renderStudioHome();
                _feedback.notify('Removed from Studio Home. The file was not deleted.', { title: 'Recent File Removed', tone: 'info' });
            });
        });
    }
    function setFilterSidebar(open) {
        hostContext.state.filterSidebarOpen = Boolean(open);
        hostContext.filterSidebar.classList.toggle('collapsed', !hostContext.state.filterSidebarOpen);
        queryElement(hostContext.shell, '[data-activity="filters"]')?.classList.toggle('active', hostContext.state.filterSidebarOpen);
        if (hostContext.state.filterSidebarOpen)
            hostContext.renderFilterPanel();
    }
    function setContextualRailVisibility() {
        const paletteBtn = queryElement(hostContext.shell, '[data-activity="palette"]');
        const filtersBtn = queryElement(hostContext.shell, '[data-activity="filters"]');
        const projectionGroup = queryElement(hostContext.shell, '.etlsql-studio-projection-group');
        if (hostContext.state.activeDocId === '__home__') {
            if (paletteBtn)
                paletteBtn.style.display = 'none';
            if (filtersBtn)
                filtersBtn.style.display = 'none';
            setFilterSidebar(false);
            if (projectionGroup)
                projectionGroup.style.opacity = '0.4';
            const publishBtn = queryElement(hostContext.shell, '[data-action="publish"]');
            if (publishBtn && hostContext.opts.onCreateDocument) {
                publishBtn.disabled = true;
                publishBtn.style.opacity = '0.5';
            }
            return;
        }
        if (projectionGroup)
            projectionGroup.style.opacity = '1';
        const doc = hostContext.getActiveDoc();
        const isRpt = doc ? (doc.path || '').endsWith('.rptsql') : false;
        const publishBtn = queryElement(hostContext.shell, '[data-action="publish"]');
        if (publishBtn && hostContext.opts.onCreateDocument) {
            const canPublish = Boolean(doc && !doc.reportId);
            publishBtn.disabled = !canPublish;
            publishBtn.style.opacity = canPublish ? '1' : '0.5';
            publishBtn.title = doc?.reportId
                ? `'${doc.name}' is published in the catalog.`
                : 'Publish this draft report to the Portal Catalog';
        }
        if (paletteBtn) {
            paletteBtn.style.display = isRpt ? 'flex' : 'none';
        }
        if (filtersBtn) {
            filtersBtn.style.display = isRpt ? 'flex' : 'none';
        }
        if (!isRpt)
            setFilterSidebar(false);
        if (!isRpt && hostContext.state.activeActivity === 'palette') {
            setActivity('explorer');
        }
    }
    function setActivity(activity) {
        if (hostContext.state.activeActivity === activity && hostContext.state.sidebarOpen) {
            hostContext.state.sidebarOpen = false;
            hostContext.sidebar.classList.add('collapsed');
            queryElements(hostContext.shell, '.etlsql-studio-rail-btn:not([data-activity="filters"])').forEach(b => b.classList.remove('active'));
            return;
        }
        hostContext.state.activeActivity = activity;
        hostContext.state.sidebarOpen = true;
        hostContext.sidebar.classList.remove('collapsed');
        queryElements(hostContext.shell, '.etlsql-studio-rail-btn:not([data-activity="filters"])').forEach(b => {
            b.classList.toggle('active', b.dataset.activity === activity);
        });
        renderSidebarContent(activity);
    }
    function renderSidebarContent(activity) {
        if (hostContext.state.filterSidebarOpen && activity !== 'filters')
            hostContext.renderFilterPanel();
        hostContext.sidebarContent.style.display = '';
        hostContext.inspector.style.display = 'none';
        if (activity === 'catalog') {
            hostContext.renderDataWorkflow();
            return;
        }
        if (activity === 'filters') {
            setFilterSidebar(true);
            return;
        }
        if (activity === 'palette') {
            hostContext.renderVisualLibrary();
            return;
        }
        if (activity === 'outline') {
            hostContext.renderOutlineTree();
            return;
        }
        if (activity === 'engine') {
            void hostContext.renderEnginePanel();
            return;
        }
        if (activity === 'governance') {
            void hostContext.renderGovernancePanel();
            return;
        }
        if (activity === 'explorer') {
            hostContext.sidebarTitle.textContent = 'Explorer';
            const workspaceMarkup = hostContext.hasWorkspaceHost ? `
                <div class="etlsql-sidebar-section-header etlsql-explorer-header">
                    <span>Workspace</span>
                    ${hostContext.opts.onCreateWorkspaceFolder ? `<button type="button" class="etlsql-explorer-header-action" data-explorer-new-folder="" title="New folder" aria-label="New folder">${_studioIcon('plus', 12)}</button>` : ''}
                </div>
                <div class="etlsql-studio-file-item etlsql-explorer-root" data-explorer-root-drop aria-label="Workspace root drop target"><span class="etlsql-explorer-spacer" aria-hidden="true"></span><span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span><span class="etlsql-file-name">Workspace root</span></div>
                <div class="etlsql-studio-explorer-tree" aria-label="Workspace files">
                    ${hostContext.workspaceTreeMarkup() || '<div class="etlsql-studio-empty-guidance"><strong>Empty workspace</strong><span>Create a folder or save a script to get started.</span></div>'}
                </div>` : '';
            hostContext.sidebarContent.innerHTML = `${workspaceMarkup}
                <div class="etlsql-sidebar-section-header"><span>Open Documents</span></div>
                <div class="etlsql-studio-explorer-list">
                    <div class="etlsql-studio-file-item ${hostContext.state.activeDocId === '__home__' ? 'active' : ''}" data-open-doc="__home__"><span class="etlsql-file-icon">${_studioIcon('explorer', 14)}</span><span class="etlsql-file-name">Home</span></div>
                    ${hostContext.state.documents.map(d => `<div class="etlsql-studio-file-item ${d.id === hostContext.state.activeDocId ? 'active' : ''}" data-open-doc="${d.id}"><span class="etlsql-file-icon">${_fileIcon(d.path)}</span><span class="etlsql-file-name">${_escapeHtml(d.name)}</span></div>`).join('')}
                </div>`;
            if (hostContext.hasWorkspaceHost)
                hostContext.bindWorkspaceExplorer();
            queryElements(hostContext.sidebarContent, '[data-open-doc]').forEach(el => {
                el.addEventListener('click', () => hostContext.switchDoc(el.dataset.openDoc));
            });
        }
        else if (activity === 'git') {
            void hostContext.renderGitSidebar();
        }
        else if (activity === 'settings') {
            hostContext.sidebarTitle.textContent = 'Settings';
            hostContext.sidebarContent.innerHTML = `
                <div class="etlsql-studio-capability-state" data-capability-state="settings" role="status">
                    <span class="etlsql-studio-capability-label">Host capability</span>
                    <strong>Settings are unavailable</strong>
                    <p>This Studio host does not expose editable workspace settings.</p>
                </div>
            `;
        }
    }
    return { mountShell, applySplitPct, setupModalAccessibility, setProjection, renderStudioHome, setFilterSidebar, setContextualRailVisibility, setActivity, renderSidebarContent };
}
