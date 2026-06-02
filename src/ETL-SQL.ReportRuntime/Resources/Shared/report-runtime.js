/**
 * Copyright (c) 2026 Charles Clemens
 * Licensed under the PolyForm Noncommercial License 1.0.0
 * Commercial use of this software requires a separate license.
 * Contact etlsqlsoftware@gmail.com for commercial inquiries.
 *
 * report-runtime.js — Phase 9E
 *
 * Dual-mode bootstrap:
 *   - VS Code WebviewPanel: reads `window.__MANIFEST__` (injected by reportPreviewPanel.ts)
 *   - Web (Phase 9D+): calls /api/manifest; supports interactive slicers, drill-down,
 *     SET_PARAMETER, DATEPICKER, SLIDER, MULTISELECT, SEARCH, and batched parameter updates.
 */
(function () {
    'use strict';

    // Web mode  (single or multi-report server): window.__IS_WEB__ = true
    // VS Code mode (webview preview):           window.__MANIFEST__ set, no __IS_WEB__
    const isWebMode = window.__IS_WEB__ || window.location.protocol.startsWith('http');
    const vscode    = (typeof acquireVsCodeApi === 'function') ? acquireVsCodeApi() : null;
    const isInteractive = isWebMode || vscode;
    
    let baselineManifest = null;
    
    function getOption(options, key) {
        if (!options) return null;
        const lookup = key.toLowerCase();
        for (let k in options) {
            if (k.toLowerCase() === lookup) return options[k];
        }
        return null;
    }

    function getStyle(styles, key) {
        if (!styles) return null;
        const lookup = key.toLowerCase();
        for (let k in styles) {
            if (k.toLowerCase() === lookup) return styles[k];
        }
        return null;
    }

    function getParam(params, name) {
        if (!params || !name) return undefined;
        const lookup = name.toLowerCase();
        for (let k in params) {
            if (k.toLowerCase() === lookup) return params[k];
        }
        return undefined;
    }

    function noDataEl(msg) {
        const div = document.createElement('div');
        div.className = 'no-data';
        div.textContent = msg;
        return div;
    }

    // Accepts "ON", "TRUE", "1" (case-insensitive) — mirrors server-side IsOn()
    function isOn(val) {
        if (!val) return false;
        const v = String(val).toUpperCase();
        return v === 'ON' || v === 'TRUE' || v === '1';
    }

    function isOff(val) {
        if (val === null || val === undefined) return false;
        const v = String(val).toUpperCase();
        return v === 'OFF' || v === 'FALSE' || v === '0';
    }

    function inputTypeForParameter(meta) {
        const type = (meta && meta.type ? String(meta.type) : '').toUpperCase();
        if (['INT', 'INTEGER', 'BIGINT', 'SMALLINT', 'TINYINT', 'DECIMAL', 'NUMERIC', 'FLOAT', 'DOUBLE', 'REAL', 'MONEY'].includes(type)) {
            return 'number';
        }
        if (['BOOL', 'BOOLEAN', 'BIT'].includes(type)) {
            return 'checkbox';
        }
        if (['DATE', 'DATETIME', 'DATETIME2', 'DATETIMEOFFSET'].includes(type)) {
            return 'date';
        }
        return 'text';
    }

    // In multi-report mode the server injects window.__API_BASE__ = '/reports/{name}/api'.
    // Single-report and VS Code modes default to '/api'.
    const apiBase = (window.__API_BASE__ || '/api').replace(/\/$/, '');

    // Current report parameters (for interactive controls)
    const parameters = {};
    const pendingParameters = {}; // Paginated page staged parameters
    let _refreshTimers = [];
    let _lastActivePage = null;
    let _drillInFlight = false;
    const _drillHistory = [];
    const _registeredMaps = new Set();
    const _crossFilterStates = {}; // Keyed by page element ID; persists across renderManifest re-builds
    const _uiStates = {};          // Keyed by object name; persists across re-renders (e.g. collapsed: true)
    let _lastManifest = null;
    let _maximizedVisualCard = null;
    let _exportReadyGeneration = 0;
    let _exportReadyPromise = Promise.resolve();
    let _exportReadyResolve = null;

    function publishExportState(status, detail) {
        const state = {
            status,
            ready: status === 'ready',
            timestamp: new Date().toISOString(),
            ...(detail || {})
        };
        window.__etlSqlReportExportReady = state.ready;
        window.__etlSqlReportExportState = state;
        window.dispatchEvent(new CustomEvent('etl-sql-report-export-state', { detail: state }));
        if (state.ready) {
            window.dispatchEvent(new CustomEvent('etl-sql-report-export-ready', { detail: state }));
        }
        return state;
    }

    function markExportNotReady(reason, detail) {
        _exportReadyGeneration++;
        _exportReadyPromise = new Promise(resolve => {
            _exportReadyResolve = resolve;
        });
        publishExportState('rendering', { reason, ...(detail || {}) });
    }

    function waitForImagesToSettle() {
        const images = Array.from(document.images || []);
        const pending = images
            .filter(img => !img.complete)
            .map(img => {
                if (typeof img.decode === 'function') {
                    return img.decode().catch(() => {});
                }
                return new Promise(resolve => {
                    img.addEventListener('load', resolve, { once: true });
                    img.addEventListener('error', resolve, { once: true });
                });
            });
        return Promise.all(pending);
    }

    function markExportReady(manifest) {
        const generation = _exportReadyGeneration;
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                waitForImagesToSettle().then(() => {
                    if (generation !== _exportReadyGeneration) return;
                    const pageCount = manifest && manifest.pages ? manifest.pages.length : 0;
                    const visualCount = manifest && manifest.visuals ? manifest.visuals.length : 0;
                    const state = publishExportState('ready', { pageCount, visualCount });
                    if (_exportReadyResolve) _exportReadyResolve(state);
                    _exportReadyResolve = null;
                });
            });
        });
    }

    window.__etlSqlReportWhenExportReady = function (timeoutMs) {
        const timeout = Number(timeoutMs || 0);
        if (window.__etlSqlReportExportReady) {
            return Promise.resolve(window.__etlSqlReportExportState);
        }
        if (timeout <= 0) return _exportReadyPromise;
        return Promise.race([
            _exportReadyPromise,
            new Promise((_, reject) => {
                setTimeout(() => reject(new Error('Report export readiness timed out.')), timeout);
            })
        ]);
    };

    /**
     * Entry point: obtain manifest and render all visuals + pages.
     */
    async function boot() {
        markExportNotReady('boot');
        if (window.__IS_PREVIEW__) {
            document.body.classList.add('preview-mode');
        }
        let manifest;
        if (window.__MANIFEST__) {
            // Pre-embedded (single-report web mode or VS Code preview)
            manifest = window.__MANIFEST__;
        } else if (isWebMode) {
            // Multi-report web mode: fetch from API
            try {
                const qs = window.location.search;
                const res = await fetch(apiBase + '/manifest' + qs);
                manifest  = await res.json();
            } catch (e) {
                document.getElementById('root').innerHTML =
                    '<p class="error">Failed to load manifest: ' + e.message + '</p>';
                publishExportState('error', { reason: 'manifest-load-failed', message: e.message });
                return;
            }
        } else {
            document.getElementById('root').innerHTML =
                '<p class="error">No manifest available.</p>';
            publishExportState('error', { reason: 'manifest-missing' });
            return;
        }

        // Phase 4: Intercept execution if required parameters are missing
        if (!checkRequiredParameters(manifest)) {
            publishExportState('blocked', { reason: 'required-parameters' });
            return; // Modal is showing, wait for user input
        }

        renderManifest(manifest);
    }

    function checkRequiredParameters(manifest) {
        if (!manifest.parameterMetadata) return true;
        
        const missing = [];
        const required = [];
        for (const name in manifest.parameterMetadata) {
            const meta = manifest.parameterMetadata[name];
            if (meta.isRequired) {
                required.push(meta);
                const val = getParam(manifest.parameters, name);
                if (val === undefined || val === null || val === "" || val === "null") {
                    missing.push(meta);
                }
            }
        }

        if (missing.length > 0) {
            // Show all REQUIRED parameters in the modal, not just the missing ones,
            // to provide full context to the user.
            showRequiredParametersModal(required, manifest);
            return false;
        }
        return true;
    }

    function showRequiredParametersModal(requiredList, manifest) {
        const modal = document.createElement('div');
        modal.className = 'required-params-modal';
        
        const content = document.createElement('div');
        content.className = 'modal-content';
        
        const title = document.createElement('h2');
        title.textContent = 'Required Parameters';
        content.appendChild(title);
        
        const desc = document.createElement('p');
        desc.textContent = 'Please provide values for the following mandatory fields to run this report:';
        content.appendChild(desc);
        
        const grid = document.createElement('div');
        grid.className = 'params-grid';
        
        const inputs = {};
        requiredList.forEach(meta => {
            const label = document.createElement('label');
            label.textContent = meta.name.startsWith('@') ? meta.name.substring(1) : meta.name;
            
            const input = document.createElement('input');
            input.type = inputTypeForParameter(meta);
            const currentValue = getParam(manifest.parameters, meta.name) || '';
            if (input.type === 'checkbox') input.checked = isOn(currentValue);
            else input.value = currentValue;
            input.placeholder = meta.defaultValue || '';
            input.className = 'modal-input';
            
            grid.appendChild(label);
            grid.appendChild(input);
            inputs[meta.name] = input;
        });
        content.appendChild(grid);
        
        const footer = document.createElement('div');
        footer.className = 'modal-footer';
        
        const runBtn = document.createElement('button');
        runBtn.className = 'header-btn primary';
        runBtn.textContent = 'Run Report';
        runBtn.onclick = () => {
            const updates = { ...parameters }; // Start with current global state
            let allOk = true;
            for (const name in inputs) {
                const input = inputs[name];
                const val = input.type === 'checkbox'
                    ? (input.checked ? 'TRUE' : 'FALSE')
                    : input.value;
                const meta = manifest.parameterMetadata[name];
                if (meta.isRequired && !val) {
                    input.classList.add('error');
                    allOk = false;
                } else {
                    input.classList.remove('error');
                    updates[name] = val;
                }
            }
            
            if (allOk) {
                modal.remove();
                postParameters(updates, false).then(m => {
                    if (m) renderManifest(m);
                });
            }
        };
        
        footer.appendChild(runBtn);
        content.appendChild(footer);
        modal.appendChild(content);
        document.body.appendChild(modal);
    }

    function renderManifest(manifest) {
        markExportNotReady('render-manifest');
        _lastManifest = manifest;

        // Cancel any running per-page auto-refresh timers before rebuilding.
        _refreshTimers.forEach(id => clearInterval(id));
        _refreshTimers = [];

        const root = document.getElementById('root');
        if (!root) {
            publishExportState('error', { reason: 'root-missing' });
            return;
        }
        root.replaceChildren(); // Clear without reparsing HTML.
        renderHeader(root, manifest);

        // Register custom themes before any echarts.init() calls
        if (typeof echarts !== 'undefined' && manifest.customThemes) {
            manifest.customThemes.forEach(t => {
                if (t.name && t.config) echarts.registerTheme(t.name, t.config);
            });
        }

        // Cache baseline manifest (the first one with no parameters set)
        if (!baselineManifest && (!manifest.parameters || Object.keys(manifest.parameters).length === 0)) {
            baselineManifest = JSON.parse(JSON.stringify(manifest));
        }
        window.__CURRENT_MANIFEST__ = manifest;

        // Update local parameters from manifest
        if (manifest.parameters) {
            Object.keys(manifest.parameters).forEach(k => {
                parameters[k] = manifest.parameters[k];
            });
        }

        // Navigation bar
        const navDef = manifest.navigations && manifest.navigations.length > 0
            ? manifest.navigations[0] : null;

        if (manifest.pages && manifest.pages.length > 0) {
            const pageSections = {};
            const firstVisible = manifest.pages.find(p => !p.isHidden);
            const defaultPageName = navDef
                ? (navDef.defaultPage || (firstVisible && firstVisible.name))
                : (firstVisible && firstVisible.name);

            manifest.pages.forEach(page => {
                const reportStyles = manifest.styles || {};
                const pageStyles = page.styles || {};
                const pageTheme = getStyle(pageStyles, 'THEME') || getStyle(reportStyles, 'THEME') || manifest.theme || null;
                const section = renderPage(manifest, page, pageSections, pageTheme);
                root.appendChild(section);

                // Hidden pages start invisible; DRILL_DOWN can show them programmatically.
                if (page.isHidden || (navDef && page.name !== defaultPageName)) {
                    section.style.display = 'none';
                }
            });

            if (navDef) {
                renderNavBar(root, navDef, pageSections, manifest.pages);
            } else if (manifest.pages.length > 0) {
                // No navigation — show the first page by default
                const firstPage = manifest.pages.find(p => !p.isHidden) || manifest.pages[0];
                const section = pageSections[firstPage.name];
                if (section) {
                    section.style.display = 'block';
                    resizeChartsIn(section);
                }
            }
        } else {
            (manifest.visuals || []).forEach(v => renderVisual(root, v, manifest.theme, manifest));
        }

        // Synchronize parameter values to any newly rendered controls
        if (manifest.parameters) {
            syncParameters(manifest.parameters);
        }

        // Cross-filter state management across re-renders:
        // - Non-interaction rebuild (slicer/param change): clear all selection state.
        // - Interaction rebuild (chart click): re-apply dimming/source CSS so the visual
        //   feedback survives the full DOM rebuild that renderManifest does.
        if (!manifest.isInteraction) {
            for (let k in _crossFilterStates) delete _crossFilterStates[k];
        } else {
            reApplyCrossFilterStyling();
        }

        // Set up per-page auto-refresh timers (web mode only; VS Code preview ignores).
        if (isWebMode && manifest.pages) {
            manifest.pages.forEach(page => {
                if (!page.refreshIntervalSeconds || page.refreshIntervalSeconds <= 0) return;
                const id = setInterval(() => {
                    // Only refresh when the page section is visible.
                    const section = document.getElementById('page-' + page.name.toLowerCase());
                    if (!section || section.style.display === 'none') return;
                    fetch(apiBase + '/manifest')
                        .then(r => r.ok ? r.json() : null)
                        .then(m => { if (m) renderManifest(m); })
                        .catch(() => {});
                }, page.refreshIntervalSeconds * 1000);
                _refreshTimers.push(id);
            });
        }

        renderFooter(root, manifest);
        renderPipelineConsole(root, manifest);
        renderAutoPanel(root, manifest);
        markExportReady(manifest);
    }

    function renderAutoPanel(container, manifest) {
        if (!manifest.parameterMetadata) return;
        
        // Identify parameters that are marked as INPUT but don't have a corresponding visual SLICER
        const visuals = manifest.visuals || [];
        const visualParams = new Set();
        visuals.forEach(v => {
            const type = (v.visualType || '').toUpperCase();
            if ([
                'SLICER', 'MULTISELECT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER',
                'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX'
            ].includes(type)) {
                const p = v.options && (v.options['data-parameter'] || v.options['PARAMETER'] || v.options['parameter']);
                if (p) visualParams.add(p.toLowerCase());
                
                // Also check ACTIONS for SET_PARAMETER
                (v.actions || []).forEach(a => {
                    if (a.type === 'SET_PARAMETER' && a.parameterName) {
                        visualParams.add(a.parameterName.toLowerCase());
                    }
                });
            }
        });

        const autoParams = [];
        for (const name in manifest.parameterMetadata) {
            if (!visualParams.has(name.toLowerCase())) {
                autoParams.push(manifest.parameterMetadata[name]);
            }
        }

        if (autoParams.length === 0) return;

        const panel = document.createElement('div');
        panel.className = 'auto-parameter-panel collapsed';
        
        const toggle = document.createElement('div');
        toggle.className = 'panel-toggle';
        toggle.innerHTML = '<span>&#x2699;</span>';
        toggle.onclick = () => panel.classList.toggle('collapsed');
        panel.appendChild(toggle);
        
        const content = document.createElement('div');
        content.className = 'panel-content';
        
        const title = document.createElement('h4');
        title.textContent = 'Report Parameters';
        content.appendChild(title);
        
        const list = document.createElement('div');
        list.className = 'panel-list';
        
        autoParams.forEach(meta => {
            const item = document.createElement('div');
            item.className = 'panel-item';
            
            const label = document.createElement('label');
            label.textContent = meta.name.startsWith('@') ? meta.name.substring(1) : meta.name;
            item.appendChild(label);
            
            const inputGroup = document.createElement('div');
            inputGroup.className = 'input-group';
            
            const input = document.createElement('input');
            input.type = inputTypeForParameter(meta);
            const currentValue = getParam(manifest.parameters, meta.name) || '';
            if (input.type === 'checkbox') input.checked = isOn(currentValue);
            else input.value = currentValue;
            input.placeholder = meta.defaultValue || '';
            inputGroup.appendChild(input);
            
            const applyBtn = document.createElement('button');
            applyBtn.innerHTML = '&#x2713;';
            applyBtn.onclick = () => {
                const updates = { ...parameters }; // Batch everything
                updates[meta.name] = input.type === 'checkbox'
                    ? (input.checked ? 'TRUE' : 'FALSE')
                    : input.value;
                postParameters(updates, false).then(m => {
                    if (m) renderManifest(m);
                });
            };
            inputGroup.appendChild(applyBtn);
            
            item.appendChild(inputGroup);
            list.appendChild(item);
        });
        
        content.appendChild(list);
        panel.appendChild(content);
        container.appendChild(panel);
    }

    // ── Header & Actions ──────────────────────────────────────────────────

    function renderHeader(container, manifest) {
        if (!vscode) return; // Only show in VS Code mode (preview)

        const header = document.createElement('header');
        header.className = 'report-header';

        const left = document.createElement('div');
        left.className = 'header-left';
        left.innerHTML = `
            <div class="header-title">${escHtml(manifest.title || 'ETL-SQL Report')}</div>
            <div class="header-subtitle">${escHtml(manifest.description || 'Interactive Data Insight')}</div>
        `;

        const actions = document.createElement('div');
        actions.className = 'header-actions';

        const openBtn = document.createElement('button');
        openBtn.className = 'header-btn primary';
        openBtn.title = 'Open interactive report in browser';
        openBtn.textContent = 'Open';
        openBtn.addEventListener('click', () => {
            vscode.postMessage({ type: 'serve' });
        });
        actions.appendChild(openBtn);

        const pdfBtn = document.createElement('button');
        pdfBtn.className = 'header-btn';
        pdfBtn.title = 'Export to PDF';
        pdfBtn.textContent = 'PDF';
        pdfBtn.addEventListener('click', () => {
            vscode.postMessage({ type: 'exportReport', format: 'pdf' });
        });
        actions.appendChild(pdfBtn);

        const mdBtn = document.createElement('button');
        mdBtn.className = 'header-btn';
        mdBtn.title = 'Export to Markdown';
        mdBtn.textContent = 'MD';
        mdBtn.addEventListener('click', () => {
            vscode.postMessage({ type: 'exportReport', format: 'markdown' });
        });
        actions.appendChild(mdBtn);

        const publishBtn = document.createElement('button');
        publishBtn.className = 'header-btn';
        publishBtn.title = 'Publish to Report Portal';
        publishBtn.textContent = 'Publish';
        publishBtn.addEventListener('click', () => vscode.postMessage({ type: 'publish' }));
        actions.appendChild(publishBtn);

        header.appendChild(left);
        header.appendChild(actions);
        container.appendChild(header);
    }

    function renderNavBar(container, navDef, pageSections, pages) {
        const nav = document.createElement('nav');
        nav.className = 'nav-bar';

        // Insert nav before the first page section
        const firstPage = pages.length > 0 ? pageSections[pages[0].name] : null;
        if (firstPage) {
            container.insertBefore(nav, firstPage);
        } else {
            container.prepend(nav);
        }

        const defaultPageName = navDef.defaultPage || (pages.length > 0 ? pages[0].name : null);
        // Track the current page between refreshes/manifest updates. 
        // We prioritize _lastActivePage (dynamic) over window.__INITIAL_PAGE__ (set at load).
        const requestedPage  = (_lastActivePage || window.__INITIAL_PAGE__ || '').trim();
        const pageToShow     = (requestedPage && pageSections[requestedPage]) ? requestedPage : defaultPageName;
        const itemClass = navDef.navType === 'TAB' ? 'nav-tab' :
                          navDef.navType === 'BUTTON' ? 'nav-btn' : 'nav-link';
        const isLink = navDef.navType === 'LINK';

        navDef.pages.forEach((pageName, idx) => {
            if (isLink && idx > 0) {
                const sep = document.createElement('span');
                sep.className = 'nav-sep';
                sep.textContent = ' | ';
                nav.appendChild(sep);
            }

            const el = document.createElement('span');
            el.className = itemClass;
            el.textContent = pageName;

            if (pageName === pageToShow) el.classList.add('active');

            el.dataset.page = pageName; // allows programmatic navigation
            el.addEventListener('click', () => {
                // Hide all, show clicked
                navDef.pages.forEach(n => {
                    const s = pageSections[n];
                    if (s) s.style.display = 'none';
                });
                const target = pageSections[pageName];
                if (target) target.style.display = 'block';

                // Set active state BEFORE resize so it is never racing against
                // event callbacks (e.g. ECharts force-layout rendering) that fire
                // during chart.resize() and may themselves trigger re-renders.
                nav.querySelectorAll('.' + itemClass).forEach(e => e.classList.remove('active'));
                el.classList.add('active');
                _lastActivePage = pageName;

                // Defer resize to the next frame so the active class renders first.
                if (target) requestAnimationFrame(() => resizeChartsIn(target));

                // Notify portal of user-driven tab change so it can push a history entry
                if (window.parent && window.parent !== window) {
                    window.parent.postMessage({ type: 'etl-page-changed', page: pageName, userTriggered: true }, '*');
                }
            });

            nav.appendChild(el);
        });

        // Show the target page, hide others
        pages.forEach(p => {
            const s = pageSections[p.name];
            if (!s) return;
            if (p.name === pageToShow) {
                s.style.display = 'block';
                resizeChartsIn(s);
            } else {
                s.style.display = 'none';
            }
        });

        // Announce initial page to portal (uses replaceState — no new history entry)
        if (window.parent && window.parent !== window) {
            window.parent.postMessage({ type: 'etl-page-changed', page: pageToShow }, '*');
        }
    }

    function syncParameters(params) {
        if (!params) return;
        for (let name in params) {
            const val = params[name];
            const elements = document.querySelectorAll(`[data-parameter]`);
            elements.forEach(el => {
                const paramKey = el.getAttribute('data-parameter');
                if (paramKey && paramKey.toLowerCase() === name.toLowerCase()) {
                    const targets = (el.tagName === 'SELECT' || el.tagName === 'INPUT') 
                                    ? [el] 
                                    : Array.from(el.querySelectorAll('select, input'));
                    targets.forEach(t => {
                        if (t.multiple && t.tagName === 'SELECT') {
                            const csvValues = (String(val || '')).split(',').map(v => v.trim());
                            Array.from(t.options).forEach(opt => {
                                opt.selected = csvValues.includes(opt.value);
                            });
                        } else if (t.value !== val) {
                            t.value = val;
                        }
                    });
                }
            });
        }
    }

    function resizeChartsIn(section) {
        section.querySelectorAll('.chart-wrapper').forEach(w => {
            if (w._echartsInst) w._echartsInst.resize();
        });
    }

    const NON_MAXIMIZABLE_CONTROL_TYPES = new Set([
        'SLICER', 'MULTISELECT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER',
        'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX'
    ]);

    function shouldShowVisualToolbar(type, styles) {
        const allowMaximize = getStyle(styles, 'ALLOW_MAXIMIZE');
        if (isOn(allowMaximize)) return true;
        if (isOff(allowMaximize)) return false;
        return !NON_MAXIMIZABLE_CONTROL_TYPES.has(type);
    }

    function addVisualToolbar(card) {
        card.classList.add('has-visual-toolbar');
        const toolbar = document.createElement('div');
        toolbar.className = 'visual-toolbar';

        const maxBtn = document.createElement('button');
        maxBtn.type = 'button';
        maxBtn.className = 'visual-tool-btn';
        maxBtn.textContent = '[]';
        maxBtn.title = 'Maximize visual';
        maxBtn.setAttribute('aria-label', 'Maximize visual');
        maxBtn.addEventListener('click', e => {
            e.stopPropagation();
            toggleVisualMaximize(card, maxBtn);
        });

        toolbar.appendChild(maxBtn);
        card.appendChild(toolbar);
    }

    function toggleVisualMaximize(card, button) {
        if (_maximizedVisualCard && _maximizedVisualCard !== card) {
            closeMaximizedVisual();
        }

        const isOpening = !card.classList.contains('visual-maximized');
        if (!isOpening) {
            closeMaximizedVisual();
            return;
        }

        _maximizedVisualCard = card;
        // Teleport card to <body> so position:fixed anchors to viewport regardless of
        // any CSS transform/contain on ancestor elements in the portal layout.
        card._maxOriginalParent = card.parentElement;
        card._maxNextSibling    = card.nextSibling;
        document.body.appendChild(card);
        card.classList.add('visual-maximized');
        // Override any transparent/glass inline background so maximized card is fully opaque.
        card._maxOrigBg      = card.style.backgroundColor;
        card._maxOrigBgImage = card.style.backgroundImage;
        card.style.backgroundColor = card.classList.contains('theme-dark') ? '#1e1e1e' : '#fff';
        card.style.backgroundImage = 'none';
        document.body.classList.add('visual-maximize-active');
        if (button) {
            button.textContent = 'x';
            button.title = 'Restore visual';
            button.setAttribute('aria-label', 'Restore visual');
        }
        setTimeout(() => resizeChartsIn(card), 50);
    }

    function closeMaximizedVisual() {
        if (!_maximizedVisualCard) return;

        const card = _maximizedVisualCard;
        card.classList.remove('visual-maximized');
        document.body.classList.remove('visual-maximize-active');

        // Restore card to its original position in the layout
        if (card._maxOriginalParent) {
            card._maxOriginalParent.insertBefore(card, card._maxNextSibling || null);
            card._maxOriginalParent = null;
            card._maxNextSibling    = null;
        }

        // Reset inline dimensions on chart container divs to let layout reflow correctly
        card.querySelectorAll('.chart-wrapper > div').forEach(el => {
            el.style.width = '100%';
            el.style.height = '100%';
        });

        // Restore original background
        card.style.backgroundColor = card._maxOrigBg      || '';
        card.style.backgroundImage = card._maxOrigBgImage || '';
        card._maxOrigBg      = null;
        card._maxOrigBgImage = null;

        const button = card.querySelector('.visual-tool-btn');
        if (button) {
            button.textContent = '[]';
            button.title = 'Maximize visual';
            button.setAttribute('aria-label', 'Maximize visual');
        }

        _maximizedVisualCard = null;
        setTimeout(() => resizeChartsIn(card), 50);
    }

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') closeMaximizedVisual();
    });

    function renderPage(manifest, page, pageSections, pageTheme) {
        console.debug(`[Layout] Rendering Page: ${page.name}`);
        const div = document.createElement('div');
        div.className = 'page';
        if (page.name) div.id = 'page-' + page.name.toLowerCase();
        div.dataset.pageName = page.name || '';
        div.dataset.pageMode = (page.mode || 'DASHBOARD').toUpperCase();

        const content = document.createElement('div');
        content.className = 'page-grid';
        div.appendChild(content);

        pageSections[page.name] = div;
        renderLayout(content, page, manifest, pageTheme);
        return div;
    }

    function renderContainer(container, containerDef, manifest, pageTheme) {
        const div = document.createElement('div');
        const containerTypeName = (containerDef.containerType || '').toUpperCase();
        const isScroll = containerTypeName === 'SCROLL';
        const isLayer  = containerTypeName === 'LAYER';
        div.className = isScroll ? 'container-scroll' : isLayer ? 'container-layer' : 'container-box';

        // LAYER: stack children as absolutely-positioned overlapping panels
        if (isLayer) {
            div.setAttribute('data-name', containerDef.name);
            const height = (containerDef.styles || {})['HEIGHT'] || (containerDef.styles || {})['height'];
            if (height) div.style.height = height;
            const slotMap = containerDef.slotMap || {};
            const uniqueItems = [...new Set(Object.values(slotMap))];
            uniqueItems.forEach((item, i) => {
                const wrapper = document.createElement('div');
                wrapper.className = 'layer-slot';
                wrapper.style.zIndex = String(i + 1);
                const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === item.toLowerCase());
                if (visual) {
                    renderVisual(wrapper, visual, pageTheme, manifest);
                } else {
                    const nested = (manifest.containers || []).find(c => c.name.toLowerCase() === item.toLowerCase());
                    if (nested) renderContainer(wrapper, nested, manifest, pageTheme);
                }
                div.appendChild(wrapper);
            });
            container.appendChild(div);
            setTimeout(() => resizeChartsIn(div), 50);
            return;
        }
        div.setAttribute('data-name', containerDef.name);
        
        const tag = getOption(containerDef.options, 'TAG') || getStyle(containerDef.styles, 'TAG');
        if (tag) div.setAttribute('data-tag', tag);
        const styles = containerDef.styles || {};
        const containerTheme = getStyle(styles, 'THEME') || pageTheme;
        const isCollapsible = containerDef.isCollapsible;

        if (isScroll) {
            const height = getStyle(styles, 'HEIGHT') || '400px';
            div.style.maxHeight = height;
        }

        if (isCollapsible) {
            div.classList.add('collapsible-inline');
            const header = document.createElement('div');
            header.className = 'container-header';
            
            const title = document.createElement('span');
            title.className = 'container-title';
            title.textContent = containerDef.title || containerDef.name;
            header.appendChild(title);
            
            const chevron = document.createElement('span');
            chevron.className = 'container-chevron';
            chevron.innerHTML = '&#x25B2;'; // UP
            header.appendChild(chevron);
            
            const name = containerDef.name;
            const persisted = _uiStates[name];
            if (persisted && persisted.collapsed) {
                div.classList.add('collapsed');
                chevron.innerHTML = '&#x25BC;'; // DOWN
            }

            header.onclick = () => {
                const isCollapsed = div.classList.toggle('collapsed');
                chevron.innerHTML = isCollapsed ? '&#x25BC;' : '&#x25B2;'; // DOWN : UP
                setTimeout(() => {
                    resizeChartsIn(div);
                    const grid = getPageContainer(div)?.querySelector('.page-grid');
                    if (grid) resizeChartsIn(grid);
                }, 350);
            };
            
            div.appendChild(header);
            
            const content = document.createElement('div');
            content.className = 'container-content';
            renderLayout(content, containerDef, manifest, containerTheme);
            div.appendChild(content);
        } else {
            renderLayout(div, containerDef, manifest, containerTheme);
        }
        
        container.appendChild(div);
    }

    function getPageContainer(el) {
        while (el && el !== document.body && !el.classList.contains('page')) el = el.parentElement;
        return el;
    }

    function renderCollapsibleContainer(gridContainer, containerDef, manifest, pageTheme, slotWrapper) {
        const page = getPageContainer(gridContainer);
        if (!page) {
            // Fallback: if no page found, render normally
            renderContainer(slotWrapper, containerDef, manifest, pageTheme);
            return;
        }

        // 1. Create Rail if not exists
        let rail = page.querySelector('.drawer-rail-left');
        if (!rail) {
            rail = document.createElement('div');
            rail.className = 'drawer-rail-left';
            page.appendChild(rail);
        }

        // 2. Create Trigger
        const trigger = document.createElement('div');
        trigger.className = 'drawer-trigger';
        trigger.title = containerDef.title || containerDef.name;
        
        let iconHtml = '&#x2699;'; // Default GEAR
        if (containerDef.icon) {
            const icon = containerDef.icon.toUpperCase();
            if (icon === 'GEAR') iconHtml = '&#x2699;';
            else if (icon === 'FILTER') iconHtml = '&#x1F50D;';
            else if (icon === 'INFO') iconHtml = '&#x2139;';
            else if (containerDef.icon.includes('.') || containerDef.icon.includes('/')) {
                iconHtml = `<img src="${containerDef.icon}" style="width:24px;height:24px;">`;
            } else {
                iconHtml = containerDef.icon;
            }
        }
        trigger.innerHTML = iconHtml;
        rail.appendChild(trigger);

        // 3. Create Drawer
        const drawer = document.createElement('div');
        drawer.className = 'collapsible-drawer';
        drawer.setAttribute('data-name', containerDef.name);
        const tag = getOption(containerDef.options, 'TAG') || getStyle(containerDef.styles, 'TAG');
        if (tag) drawer.setAttribute('data-tag', tag);

        const styles = containerDef.styles || {};
        const containerTheme = getStyle(styles, 'THEME') || pageTheme;
        
        const header = document.createElement('div');
        header.className = 'drawer-header';
        
        const title = document.createElement('div');
        title.className = 'drawer-title';
        title.textContent = containerDef.title || containerDef.name;
        header.appendChild(title);
        
        const actions = document.createElement('div');
        actions.className = 'drawer-actions';
        
        if (containerDef.isPinnable !== false) {
            const pinBtn = document.createElement('span');
            pinBtn.className = 'drawer-action-btn';
            pinBtn.innerHTML = '&#x1F4CC;'; // Pin
            pinBtn.title = 'Pin Panel';
            pinBtn.onclick = (e) => {
                e.stopPropagation();
                const isPinned = drawer.classList.toggle('pinned');
                pinBtn.classList.toggle('active');
                gridContainer.classList.toggle('has-pinned-left');
                if (isPinned) drawer.classList.add('open');
                setTimeout(() => resizeChartsIn(gridContainer), 350);
            };
            actions.appendChild(pinBtn);
        }
        
        const closeBtn = document.createElement('span');
        closeBtn.className = 'drawer-action-btn';
        closeBtn.innerHTML = '&times;';
        closeBtn.onclick = () => {
            drawer.classList.remove('open');
            if (drawer.classList.contains('pinned')) {
                drawer.classList.remove('pinned');
                const pinBtn = actions.querySelector('.drawer-action-btn');
                if (pinBtn) pinBtn.classList.remove('active');
                gridContainer.classList.remove('has-pinned-left');
                setTimeout(() => resizeChartsIn(gridContainer), 350);
            }
        };
        actions.appendChild(closeBtn);
        
        header.appendChild(actions);
        drawer.appendChild(header);
        
        const content = document.createElement('div');
        content.className = 'drawer-content';
        renderLayout(content, containerDef, manifest, containerTheme);
        drawer.appendChild(content);
        
        page.appendChild(drawer);

        trigger.onclick = () => {
            drawer.classList.toggle('open');
            if (!drawer.classList.contains('open') && drawer.classList.contains('pinned')) {
                 // If closing while pinned, unpin
                 closeBtn.click();
            }
        };

        if (slotWrapper) slotWrapper.classList.add('grid-slot-collapsed');
    }


    function renderLayout(container, layoutDef, manifest, pageTheme) {
        if (layoutDef.structure) {
            container.style.display = 'grid';
            // CSS grid-template-areas needs each row quoted: "A A" "B C"
            const rows = layoutDef.structure.split('/')
                .map(r => r.trim().split(/\s+/).filter(s => s))
                .filter(r => r.length > 0);
            
            const maxCols = Math.max(...rows.map(r => r.length));
            const normalizedRows = rows.map(r => {
                while (r.length < maxCols) r.push('.');
                return r.join(' ');
            });
            
            container.style.gridTemplateAreas = normalizedRows.map(r => `"${r}"`).join(' ');

            if (rows.length > 0) {
                container.style.gridTemplateRows = `repeat(${rows.length}, auto)`;
                container.style.gridTemplateColumns = `repeat(${maxCols}, 1fr)`;
            }

            const slotMap = layoutDef.slotMap || {};
            Object.keys(slotMap).forEach(slotLetter => {
                const item = slotMap[slotLetter];
                if (!item) return;

                const wrapper = document.createElement('div');
                wrapper.style.gridArea = slotLetter;

                // Item could be a visual or another container
                const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === item.toLowerCase());
                if (visual) {
                    renderVisual(wrapper, visual, pageTheme, manifest);
                } else {
                    const nested = (manifest.containers || []).find(c => c.name.toLowerCase() === item.toLowerCase());
                    if (nested) {
                        const mode = (getStyle(nested.styles, 'COLLAPSE_MODE') || 'DRAWER').toUpperCase();
                        if (nested.isCollapsible && mode === 'DRAWER') {
                            renderCollapsibleContainer(container, nested, manifest, pageTheme, wrapper);
                        } else {
                            renderContainer(wrapper, nested, manifest, pageTheme);
                        }
                    } else {

                        const btn = (manifest.buttons || []).find(b => b.name.toLowerCase() === item.toLowerCase());
                        if (btn) renderButton(wrapper, btn);
                    }
                }
                container.appendChild(wrapper);
            });
        } else {
            const slotMap = layoutDef.slotMap || {};
            const uniqueItems = [...new Set(Object.values(slotMap))];
            uniqueItems.forEach(item => {
                const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === item.toLowerCase());
                if (visual) {
                    renderVisual(container, visual, pageTheme, manifest);
                } else {
                    const nested = (manifest.containers || []).find(c => c.name.toLowerCase() === item.toLowerCase());
                    if (nested) {
                        const mode = (getStyle(nested.styles, 'COLLAPSE_MODE') || 'DRAWER').toUpperCase();
                        if (nested.isCollapsible && mode === 'DRAWER') {
                            renderCollapsibleContainer(container, nested, manifest, pageTheme, null);
                        } else {
                            renderContainer(container, nested, manifest, pageTheme);
                        }
                    } else {

                        const btn = (manifest.buttons || []).find(b => b.name.toLowerCase() === item.toLowerCase());
                        if (btn) renderButton(container, btn);
                    }
                }
            });
        }
    }

    // Filter types that render without requiring rows
    const FILTER_TYPES = new Set(['SLICER', 'TABLE', 'CARD', 'TEXT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER', 'MULTISELECT', 'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX']);

    function renderVisual(container, visual, pageTheme, manifest) {
        const card = document.createElement('div');
        card.className = 'visual-card';
        card.setAttribute('data-name', visual.name);
        card.setAttribute('data-visual-name', visual.name); // Compatibility
        
        const tag = getOption(visual.options, 'TAG');
        if (tag) card.setAttribute('data-tag', tag);
        
        card._visualData = visual;

        // Apply WIDTH / HEIGHT / TOOLTIP from styles
        const vstyles = visual.styles || {};
        const width   = getStyle(vstyles, 'WIDTH');
        const height  = getStyle(vstyles, 'HEIGHT');
        const tooltip = getStyle(vstyles, 'TOOLTIP') || visual.tooltip;

        const opacity = getStyle(vstyles, 'OPACITY');
        const bgColor = getStyle(vstyles, 'BACKGROUND-COLOR') || getStyle(vstyles, 'BACKGROUND');

        if (width)   card.style.width   = width;
        if (height)  card.style.height  = height;
        if (opacity) card.style.opacity = opacity;
        if (bgColor) {
            const normalized = bgColor.trim().toLowerCase();
            const isTransparent = normalized === 'transparent' || normalized === 'rgba(0,0,0,0)' || normalized === 'rgba(0, 0, 0, 0)';
            if (isTransparent) {
                card.style.backgroundColor = 'transparent';
                card.style.backgroundImage = 'none';
            } else {
                // Layer over the CSS theme base color; keeps #1e1e1e dark base intact for dark-themed cards.
                card.style.backgroundImage = `linear-gradient(${bgColor}, ${bgColor})`;
            }
        }
        if (tooltip) card.title = tooltip;
        if (isOff(getOption(visual.options, 'VISIBLE'))) {
            card.style.display = 'none';
        }

        const title = document.createElement('h3');
        title.textContent = visual.name;
        
        // Hide redundant header if chart/card has its own title/label
        const specificTitle = getOption(visual.options, 'TITLE') || getOption(visual.options, 'mapping:label');
        if (specificTitle) title.style.display = 'none';

        card.appendChild(title);

        const type = (visual.visualType || '').toUpperCase();
        if (shouldShowVisualToolbar(type, vstyles)) {
            addVisualToolbar(card);
        }

        if (visual.error) {
            card.appendChild(errorEl(visual.error));
            container.appendChild(card);
            return;
        }

        // Deferred ON_RUN visuals show a placeholder until the paginated page is run.
        if (visual.isHidden) {
            card.classList.add('deferred-visual');
            const ph = document.createElement('div');
            ph.className = 'deferred-placeholder';
            ph.textContent = 'Configure parameters above and click Run to load data.';
            card.appendChild(ph);
            container.appendChild(card);
            return;
        }

        // Empty state handling: If not a filter/text type and no data rows, show "No Data" icon + message.
        if (!FILTER_TYPES.has(type) && (!visual.rows || visual.rows.length === 0)) {
            const empty = document.createElement('div');
            empty.className = 'empty-state';
            empty.innerHTML = '<div class="empty-icon">\u2205</div>' + 
                              '<p>No data matches the current filters.</p>';
            card.appendChild(empty);
            container.appendChild(card);
            return;
        }

        // Resolve effective theme: visual-level overrides page-level
        const effectiveTheme = getStyle(vstyles, 'THEME') || pageTheme || null;
        if (effectiveTheme) card.classList.add('theme-' + effectiveTheme.toLowerCase());

        switch (type) {
            case 'TABLE':       renderTable(card, visual);                        break;
            case 'CARD':        renderCard(card, visual);                         break;
            case 'SLICER':
            case 'MULTISELECT': renderSlicer(card, visual, manifest);             break;
            case 'TEXT':        renderText(card, visual);                         break;
            case 'DATEPICKER':    renderDatePicker(card, visual, manifest);        break;
            case 'RELDATEPICKER': renderRelDatePicker(card, visual, manifest);     break;
            case 'SLIDER':      renderSlider(card, visual, manifest);             break;
            case 'SEARCH':      renderSearch(card, visual, manifest);             break;
            case 'CHECKBOX':    renderCheckbox(card, visual, manifest);           break;
            case 'TEXTBOX':     renderTextbox(card, visual, manifest);            break;
            case 'NUMBERBOX':   renderNumberbox(card, visual, manifest);          break;
            case 'IMAGE':       renderImage(card, visual);                        break;
            case 'MATRIX':      renderMatrix(card, visual);                        break;
            default:            renderChart(card, visual, manifest, effectiveTheme); break;
        }

        // DRILL_IN breadcrumb: shown when visual has an active drill state
        if (visual.drillState?.hierarchy?.length > 0) {
            const bc = document.createElement('div');
            bc.className = 'drill-breadcrumb';
            // Segments: root label (hierarchy[0]) + each path segment
            const segs = [{ label: visual.drillState.hierarchy[0], depth: 0 }]
                .concat((visual.drillState.path || []).map((s, i) => ({ label: s.value, depth: i + 1 })));
            segs.forEach((seg, i) => {
                const sp = document.createElement('span');
                const isActive = i === segs.length - 1;
                sp.className = 'bc-seg' + (isActive ? ' bc-seg-active' : ' bc-seg-link');
                sp.textContent = seg.label;
                if (!isActive) {
                    sp.addEventListener('click', () => postDrillUp(visual.name, seg.depth));
                }
                bc.appendChild(sp);
                if (!isActive) {
                    const sep = document.createElement('span');
                    sep.className = 'bc-sep';
                    sep.textContent = ' › ';
                    bc.appendChild(sep);
                }
            });
            card.insertBefore(bc, card.firstChild);
        }

        // Drill-through affordance: cursor + badge when visual has DRILL_DOWN actions
        if ((visual.actions || []).some(a => a.type === 'DRILL_DOWN')) {
            card.classList.add('has-drill-down');
            const badge = document.createElement('span');
            badge.className = 'drill-badge';
            badge.title = 'Right-click to drill through';
            badge.textContent = '⬇';
            card.appendChild(badge);
        }

        // Drill-in affordance: cursor + badge when visual has DRILL_IN actions
        if ((visual.actions || []).some(a => a.type === 'DRILL_IN')) {
            card.classList.add('has-drill-in');
            const badge = document.createElement('span');
            badge.className = 'drill-badge';
            badge.title = 'Click to drill in';
            badge.textContent = '↧';
            card.appendChild(badge);
        }

        container.appendChild(card);
    }

    // ── Chart (ECharts — BAR / LINE / HBAR / SCATTER / PIE / DONUT / BOXPLOT / TREEMAP / HEATMAP / GAUGE / FUNNEL / WATERFALL / BUBBLE / RADAR / CANDLESTICK / MAP / GANTT / SANKEY / SUNBURST / NETWORK / TRELLIS) ──

    // Cross-filter state: { filterValue, filterColumn }. Stored per page section.
    function getPageState(container) {
        let el = container;
        while (el && !el.classList.contains('page')) el = el.parentElement;
        return el;
    }

    function applyPageCrossFilter(container, filterValue, filterColumn, sourceVisualName, event) {
        const pageEl = getPageState(container);
        if (!pageEl) return;

        // Use module-level state keyed by page ID so it survives renderManifest DOM rebuilds
        const pageKey = pageEl.id || 'default';
        const state = _crossFilterStates[pageKey] || (_crossFilterStates[pageKey] = { selections: [] });
        const isMulti = event && (event.ctrlKey || event.metaKey);

        // Update state
        if (isMulti) {
            const idx = state.selections.findIndex(s => s.value === filterValue && s.column === filterColumn);
            if (idx >= 0) state.selections.splice(idx, 1);
            else state.selections.push({ value: filterValue, column: filterColumn, visual: sourceVisualName });
        } else {
            if (state.selections.length === 1 && state.selections[0].value === filterValue && state.selections[0].visual === sourceVisualName) {
                state.selections = [];
            } else {
                state.selections = [{ value: filterValue, column: filterColumn, visual: sourceVisualName }];
            }
        }

        // Mark the source card with a border indicator; strip the marker from all others.
        pageEl.querySelectorAll('.visual-card').forEach(card => {
            const v = card._visualData;
            if (!v) return;
            if (state.selections.length > 0 && state.selections.some(s => s.visual === v.name)) {
                card.classList.add('cross-filter-source');
            } else {
                card.classList.remove('cross-filter-source');
            }
        });

        if (state.selections.length === 0) {
            // Deselect: post an empty non-interaction batch to force the server to re-evaluate
            // without any interactionValues, returning a clean manifest with no highlightRows.
            state.lastBatch = {};
            postParameters({}, false).then(m => { if (m) renderManifest(m); });
            return;
        }

        // Build interaction batch for the active selection
        const batch = {};
        const groups = {};
        state.selections.forEach(s => {
            const k = '@' + s.column;
            if (!groups[k]) groups[k] = [];
            groups[k].push(s.value);
        });
        Object.keys(groups).forEach(k => { batch[k] = groups[k].join(','); });
        state.lastBatch = batch;

        postParameters(batch, true).then(m => { if (m) renderManifest(m); });
    }

    function reApplyCrossFilterStyling() {
        document.querySelectorAll('.page').forEach(pageEl => {
            const state = _crossFilterStates[pageEl.id];
            if (!state || state.selections.length === 0) return;
            const activeVisuals = new Set(state.selections.map(s => s.visual));
            pageEl.querySelectorAll('.visual-card').forEach(card => {
                const v = card._visualData;
                if (!v) return;
                if (activeVisuals.has(v.name)) {
                    card.classList.add('cross-filter-source');
                } else {
                    card.classList.remove('cross-filter-source');
                }
            });
        });
    }

    function registerMapThenRender(mapKey, mapFile, onReady, wrapper) {
        if (_registeredMaps.has(mapKey)) { onReady(); return; }
        const url = mapFile
            ? `/api/maps/custom?path=${encodeURIComponent(mapFile)}`
            : `/maps/${mapKey}.geojson`;
        fetch(url)
            .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
            .then(geojson => {
                echarts.registerMap(mapKey, geojson);
                _registeredMaps.add(mapKey);
                onReady();
            })
            .catch(err => {
                const parent = wrapper.parentElement;
                if (parent) {
                    const msg = !isWebMode && vscode 
                        ? 'Maps only work in the Portal' 
                        : 'Map load failed: ' + err.message;
                    parent.appendChild(noDataEl(msg));
                }
            });
    }

    function renderChart(container, visual, manifest, themeOverride) {
        console.debug(`[Chart] Rendering ${visual.name} (HighlightRows: ${visual.highlightRows?.length || 0})`);
        const effectiveTheme = themeOverride || (manifest && manifest.theme) || window.__THEME__ || 'light';
        if (!visual.chartConfig) {
            container.appendChild(noDataEl('No chart config available'));
            return;
        }

        let option;
        try {
            option = typeof visual.chartConfig === 'string'
                ? JSON.parse(visual.chartConfig)
                : visual.chartConfig;
        } catch (e) {
            container.appendChild(noDataEl('Invalid chart config: ' + e.message));
            return;
        }

        if (typeof echarts === 'undefined') {
            container.appendChild(noDataEl('ECharts not loaded'));
            return;
        }

        const clickActions = actionsFor(visual, 'ON_CLICK');
        const interactionMode = ((visual.interactions || {})['ON_SELECT'] || '').toUpperCase();
        const crossFilter  = !!interactionMode && interactionMode !== 'NONE';
        const xMappingCol  = (visual.options || {})['mapping:x'];

        const wrapper = document.createElement('div');
        wrapper.className = 'chart-wrapper';
        container.appendChild(wrapper);

        // Extract MAP metadata markers (not valid ECharts properties — delete before setOption)
        const mapKey  = option.__mapKey;
        const matchBy = (option.__matchBy || 'NAME').toUpperCase();
        const mapFile = option.__mapFile;
        delete option.__mapKey;
        delete option.__matchBy;
        delete option.__mapFile;

        // BUBBLE: __bubbleSymbolSize on root option → wire symbolSize function on scatter series
        if (option.__bubbleSymbolSize) {
            delete option.__bubbleSymbolSize;
            (option.series || []).forEach(s => {
                if (s.type === 'scatter') s.symbolSize = val => val[2];
            });
        }
        // MAP POINTS: __pointsSymbolSize on series object → same treatment
        (option.series || []).forEach(s => {
            if (s.__pointsSymbolSize) {
                delete s.__pointsSymbolSize;
                if (s.type === 'scatter') s.symbolSize = val => val[2];
            }
        });

        // GANTT: __ganttRenderItem on root option → wire renderItem function on custom series
        if (option.__ganttRenderItem) {
            delete option.__ganttRenderItem;
            (option.series || []).forEach(s => {
                if (s.type === 'custom') {
                    s.renderItem = function(params, api) {
                        const categoryIndex = api.value(0);
                        const start = api.coord([api.value(1), categoryIndex]);
                        const end   = api.coord([api.value(2), categoryIndex]);
                        const height = api.size([0, 1])[1] * 0.6; // Bar height 60% of category height
                        
                        return {
                            type: 'rect',
                            shape: {
                                x: start[0],
                                y: start[1] - height / 2,
                                width: Math.max(end[0] - start[0], 2), // Min 2px width
                                height: height
                            },
                            style: api.style({
                                fill: api.value(4) || '#5470c6'
                            })
                        };
                    };
                }
            });
        }

        // SCATTER BRUSH: __brushParam → wire brushSelected event to set a parameter
        const brushParam = option.__brushParam;
        const brushType  = option.__brushType || 'rect';
        if (brushParam) {
            delete option.__brushParam;
            delete option.__brushType;
            if (!option.brush) option.brush = {};
            if (!option.toolbox) option.toolbox = { feature: {} };
            if (!option.toolbox.feature) option.toolbox.feature = {};
            option.toolbox.feature.brush = { type: [brushType, 'keep', 'clear'] };
        }

        // FIPS matching: tell ECharts to use the 'fips' property instead of default 'name'
        if (matchBy === 'FIPS') {
            (option.series || []).forEach(s => {
                if (s.type === 'map') s.nameProperty = 'fips';
            });
        }

        function finalize() {
            // Global tooltip value formatter for decimals
            if (!option.tooltip) option.tooltip = { show: true };
            if (option.tooltip.show !== false && !option.tooltip.valueFormatter && !option.tooltip.formatter) {
                option.tooltip.valueFormatter = (v) => (typeof v === 'number' && !Number.isInteger(v)) ? v.toFixed(2) : v;
            }

            if (option.series) {
                option.series.forEach(s => {
                    if (s.type === 'gauge') {
                        if (s.detail && (s.detail.formatter === '{value}' || s.detail.formatter === '{value:.1f}')) {
                            s.detail.formatter = (v) => (typeof v === 'number') ? v.toFixed(1) : v;
                        }
                    }
                    if (s.label && s.label.show && !s.label.formatter) {
                        s.label.formatter = (params) => {
                            let v = params.value;
                            if (Array.isArray(v)) v = v[v.length - 1];
                            return (typeof v === 'number' && !Number.isInteger(v)) ? v.toFixed(2) : v;
                        };
                    }
                });
            }

            if (window.__IS_PREVIEW__) {
                option.tooltip = { show: false };
                option.toolbox = { show: false };
                option.animation = false;
                (option.series || []).forEach(s => { if (s.roam) s.roam = false; });
            }

            const chart = echarts.init(wrapper, effectiveTheme || null);

            // Auto-resize whenever the wrapper changes dimensions (maximize, restore, window resize).
            // ResizeObserver is more reliable than the 50ms setTimeout heuristic.
            if (typeof ResizeObserver !== 'undefined') {
                const ro = new ResizeObserver(() => chart.resize());
                ro.observe(wrapper);
            }

            // Enable universal highlight/downplay support for cross-filtering
            if (option.series) {
                option.series.forEach(s => {
                    if (!s.emphasis) s.emphasis = {};
                    // Disable hover focus/dimming as per user request
                    s.emphasis.focus = 'none'; 
                });
            }

            // Cross-highlighting: source chart gets per-item opacity; child HIGHLIGHT charts get ghost overlay.
            // Note: the card is not yet in the DOM here so getPageState() would fail — iterate all states instead.
            {
                let cfState = null;
                for (const k in _crossFilterStates) {
                    const s = _crossFilterStates[k];
                    if (s && s.selections && s.selections.length > 0) { cfState = s; break; }
                }
                const isSource = cfState && cfState.selections.some(s => s.visual === visual.name);

                if (isSource) {
                    applySourceChartOpacity(option, cfState.selections);
                } else if (visual.highlightRows && visual.highlightRows.length > 0) {
                    mergeHighlightData(visual, option);
                } else if (cfState && cfState.selections.length > 0 && !visual.highlightRows && visualCanReflectSelections(visual, cfState.selections)) {
                    // Cross-filter active but server returned no highlight (dimension mismatch):
                    // ghost all bars to show the filter is active on this visual too.
                    applySourceChartOpacity(option, []);
                }
            }

            chart.setOption(option);
            wrapper._echartsInst = chart;

            // SCATTER BRUSH: wire brushSelected to set parameter
            if (brushParam) {
                chart.on('brushSelected', function(params) {
                    const selected = params.batch && params.batch[0] ? params.batch[0].selected : [];
                    const indices  = selected.flatMap ? selected.flatMap(s => s.dataIndex) : [];
                    const values   = indices.map(idx => {
                        const row = (visual.rows || [])[idx];
                        const xIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === (xMappingCol || '').toLowerCase());
                        return row ? row[xIdx >= 0 ? xIdx : 0] : null;
                    }).filter(v => v != null);
                    setParameter(brushParam, values.join(','));
                });
            }

            let lastHoveredRow = null;
            chart.on('mousemove', params => {
                const idx = params.dataIndex != null ? params.dataIndex : -1;
                lastHoveredRow = (visual.rows || [])[idx] || null;
            });

            if (clickActions.length > 0 || crossFilter) {
                chart.on('click', params => {
                    const idx     = params.dataIndex != null ? params.dataIndex : -1;
                    let rowData   = (visual.rows || [])[idx] || [];

                    // Robust row lookup for charts
                    if (params.name && visual.rows && visual.rows.length > 0) {
                        const xIdx = xMappingCol ? (visual.columns || []).findIndex(c => c.toLowerCase() === xMappingCol.toLowerCase()) : 0;
                        const match = visual.rows.find(r => String(r[xIdx] || '') === String(params.name));
                        if (match) rowData = match;
                    }

                    // INTERACTIONS handling
                    if (crossFilter) {
                        const clickedValue = params.name || params.value || (rowData.length > 0 ? rowData[0] : null);
                        const colName = xMappingCol || (visual.columns && visual.columns[0]);
                        console.debug(`[Chart] Click on ${visual.name} | Value: ${clickedValue} | Col: ${colName}`);
                        if (clickedValue != null && colName) {
                            applyPageCrossFilter(container, String(clickedValue), colName, visual.name, params.event?.event);
                        }
                    } else {
                        // ON_CLICK actions (Drill Down, etc)
                        clickActions.forEach(action => executeAction(action, rowData, visual.columns || [], visual.name, visual));
                    }
                });
            }
            
            wrapper.addEventListener('contextmenu', e => {
                const drillDowns = (visual.actions || []).filter(a => a.type === 'DRILL_DOWN');
                if (drillDowns.length > 0) {
                    e.preventDefault();
                    if (wrapper._echartsInst) wrapper._echartsInst.dispatchAction({ type: 'hideTip' });
                    showCtxMenu(e.clientX, e.clientY, visual, lastHoveredRow);
                }
            });

            // ── Phase 3: Legend Interaction ──
            chart.on('legendselectchanged', function (params) {
                console.debug(`[Interaction] Legend changed on ${visual.name}:`, params);
                // In ECharts, params.name is the series name that was toggled
                // We map this to a cross-filter action if possible
                const seriesName = params.name;
                const mappingCol = (visual.options || {})['mapping:x'] || (visual.columns && visual.columns[0]);
                
                // For simple charts, series might correspond to a category
                // This is a heuristic: if they clicked a legend item, they want to filter by that value
                applyPageCrossFilter(container, seriesName, mappingCol, visual.name, { ctrlKey: false });
            });
        }

        if (mapKey) {
            registerMapThenRender(mapKey, mapFile, finalize, wrapper);
        } else {
            finalize();
        }
    }

    // ── CSV export ──────────────────────────────────────────────────────────

    function exportCsv(visual) {
        const cols = visual.columns || [];
        const rows = visual.rows    || [];
        const escape = v => '"' + String(v ?? '').replace(/"/g, '""') + '"';
        const lines  = [cols.map(escape).join(',')];
        rows.forEach(r => lines.push(cols.map((_, i) => escape(r[i])).join(',')));
        const blob = new Blob([lines.join('\r\n')], { type: 'text/csv' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = (visual.name || 'export') + '.csv';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    function exportExcel(visual) {
        const cols = visual.columns || [];
        const rows = visual.rows    || [];
        const esc  = v => escHtml(String(v ?? ''));
        let html = '<html xmlns:o="urn:schemas-microsoft-com:office:office" ' +
                   'xmlns:x="urn:schemas-microsoft-com:office:excel">' +
                   '<head><meta charset="UTF-8"></head><body><table>';
        html += '<tr>' + cols.map(c => `<th>${esc(c)}</th>`).join('') + '</tr>';
        rows.forEach(r => {
            html += '<tr>' + cols.map((_, i) => `<td>${esc(r[i])}</td>`).join('') + '</tr>';
        });
        html += '</table></body></html>';
        const blob = new Blob([html], { type: 'application/vnd.ms-excel' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = (visual.name || 'export') + '.xls';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    // Prefer a real .xlsx from the server (typed cells, one sheet, no "format
    // mismatch" warning). Falls back to the lightweight client-side .xls when no
    // export API is reachable (e.g. VS Code preview or a host without the endpoint).
    async function exportExcelDownload(visual) {
        const base = window.__API_BASE__;
        if (base) {
            try {
                const res = await fetch(base + '/export/xlsx?visual=' + encodeURIComponent(visual.name || ''));
                if (res.ok) {
                    const blob = await res.blob();
                    const url  = URL.createObjectURL(blob);
                    const a    = document.createElement('a');
                    a.href     = url;
                    a.download = (visual.name || 'export') + '.xlsx';
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                    return;
                }
            } catch (e) { /* fall through to client-side export */ }
        }
        exportExcel(visual);
    }

    function findVisualData(targetName) {
        const el = document.querySelector(`[data-visual-name="${CSS.escape(targetName)}"]`);
        return el ? el._visualData : null;
    }

    // Drill-through back-navigation stack
    function showDrillBackButton() {
        let btn = document.getElementById('drill-back-btn');
        if (!btn) {
            btn = document.createElement('button');
            btn.id = 'drill-back-btn';
            btn.className = 'drill-back-btn';
            btn.addEventListener('click', () => {
                if (_drillHistory.length === 0) return;
                const prevParams = _drillHistory.pop();
                // Restore all previous params; blank out any keys added by the drill
                const restoreBatch = Object.assign({}, prevParams);
                Object.keys(parameters).forEach(k => {
                    if (!(k in prevParams)) restoreBatch[k] = '';
                });
                if (_drillHistory.length === 0) hideDrillBackButton();
                else btn.innerHTML = '← Back' + (_drillHistory.length > 1 ? ` (${_drillHistory.length})` : '');
                if (vscode) {
                    vscode.postMessage({ type: 'refreshReport', parameters: restoreBatch });
                } else {
                    postParameters(restoreBatch).then(m => { if (m) renderManifest(m); });
                }
            });
            document.body.appendChild(btn);
        }
        btn.innerHTML = '← Back' + (_drillHistory.length > 1 ? ` (${_drillHistory.length})` : '');
        btn.style.display = 'flex';
    }

    function hideDrillBackButton() {
        const btn = document.getElementById('drill-back-btn');
        if (btn) btn.style.display = 'none';
    }

    // Lightweight singleton context menu for DRILL_DOWN and Export
    let _ctxMenu = null;
    function showCtxMenu(x, y, visual, rowData) {
        hideCtxMenu();
        const menu = document.createElement('div');
        menu.className = 'report-ctx-menu';
        menu.style.left = x + 'px';
        menu.style.top  = y + 'px';

        const drillDowns = (visual.actions || []).filter(a => a.type === 'DRILL_DOWN');
        const drillReports = (visual.actions || []).filter(a => a.type === 'DRILL_REPORT');

        drillDowns.forEach(action => {
            const item = document.createElement('div');
            item.className = 'ctx-item';
            const target = action.targetVisual || action.targetPage || 'Details';
            item.innerHTML = `<span>&#x21AA;</span> Drill down to <b>${escHtml(target)}</b>`;
            item.addEventListener('click', () => {
                executeAction(action, rowData || [], visual.columns || [], visual.name, visual);
                hideCtxMenu();
            });
            menu.appendChild(item);
        });

        drillReports.forEach(action => {
            const item = document.createElement('div');
            item.className = 'ctx-item';
            const target = action.targetReport || 'Report';
            // Clean up filename for display
            const displayName = target.replace(/\.[^/.]+$/, "").replace(/^.*[\\\/]/, '');
            item.innerHTML = `<span>&#x2197;</span> Open <b>${escHtml(displayName)}</b>`;
            item.addEventListener('click', () => {
                executeAction(action, rowData || [], visual.columns || [], visual.name, visual);
                hideCtxMenu();
            });
            menu.appendChild(item);
        });

        if (drillDowns.length > 0 || drillReports.length > 0) {
            const sep = document.createElement('div');
            sep.className = 'ctx-sep';
            menu.appendChild(sep);
        }

        const exportItem = document.createElement('div');
        exportItem.className = 'ctx-item';
        exportItem.innerHTML = `<span>&#x2913;</span> Export to CSV`;
        exportItem.addEventListener('click', () => { exportCsv(visual); hideCtxMenu(); });
        menu.appendChild(exportItem);

        const excelItem = document.createElement('div');
        excelItem.className = 'ctx-item';
        excelItem.innerHTML = `<span>&#x2913;</span> Export to Excel`;
        excelItem.addEventListener('click', () => { exportExcelDownload(visual); hideCtxMenu(); });
        menu.appendChild(excelItem);

        document.body.appendChild(menu);
        _ctxMenu = menu;

        // Close on any outside click
        setTimeout(() => document.addEventListener('click', hideCtxMenu, { once: true }), 10);
    }

    function hideCtxMenu() {
        if (_ctxMenu) { _ctxMenu.remove(); _ctxMenu = null; }
    }

    // ── Table ───────────────────────────────────────────────────────────────

    function renderTable(container, visual) {
        if (!visual.columns || visual.columns.length === 0) {
            container.appendChild(noDataEl('No data available'));
            return;
        }

        const opts          = visual.options || {};
        const colMeta       = visual.columnMeta || [];
        const allRows       = visual.rows || [];
        const pageSize      = parseInt(opts['PAGE_SIZE'] || opts['page_size'] || '50', 10) || 50;
        const showSearch    = (opts['SEARCH'] || opts['search'] || 'ON').toUpperCase() !== 'OFF';
        const striped       = (opts['STRIPED'] || opts['striped'] || 'ON').toUpperCase() !== 'OFF';
        const clickActions  = actionsFor(visual, 'ON_CLICK');
        const isClickable   = clickActions.length > 0;
        const interactionMode = ((visual.interactions || {})['ON_SELECT'] || '').toUpperCase();
        const crossFilter   = !!interactionMode && interactionMode !== 'NONE';
        const stateKey      = 'table:' + (visual.name || visual.id || '');
        const state         = _uiStates[stateKey] || (_uiStates[stateKey] = { sortCol: -1, sortDir: 'asc', page: 0, search: '' });

        if (crossFilter) {
            container.setAttribute('data-cross-filter', '1');
            container._visualData = visual;
        }

        function getFilteredRows() {
            const q = state.search.toLowerCase();
            let rows = q
                ? allRows.filter(row => row.some(c => c != null && String(c).toLowerCase().includes(q)))
                : allRows;
            if (state.sortCol >= 0) {
                rows = rows.slice().sort((a, b) => {
                    const av = a[state.sortCol] ?? '', bv = b[state.sortCol] ?? '';
                    const an = parseFloat(av), bn = parseFloat(bv);
                    const cmp = !isNaN(an) && !isNaN(bn) ? an - bn : String(av).localeCompare(String(bv));
                    return state.sortDir === 'asc' ? cmp : -cmp;
                });
            }
            return rows;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'table-wrapper' + (isClickable ? ' clickable' : '');

        let heightOpt = visual.styles ? (visual.styles['HEIGHT'] || visual.styles['height']) : null;
        if (heightOpt) wrapper.style.maxHeight = heightOpt;

        // Search box
        if (showSearch) {
            const searchRow = document.createElement('div');
            searchRow.className = 'table-search-row';
            const searchInput = document.createElement('input');
            searchInput.type = 'text';
            searchInput.placeholder = 'Search…';
            searchInput.className = 'table-search-input';
            searchInput.value = state.search;
            searchInput.addEventListener('input', () => {
                state.search = searchInput.value;
                state.page = 0;
                rebuildBody();
            });
            searchRow.appendChild(searchInput);
            wrapper.appendChild(searchRow);
        }

        const table  = document.createElement('table');
        const thead  = document.createElement('thead');
        const headerRow = document.createElement('tr');

        visual.columns.forEach((col, ci) => {
            const th   = document.createElement('th');
            th.className = 'sortable';
            const meta = colMeta[ci] || {};
            if (meta.align) th.style.textAlign = meta.align;

            const label = document.createElement('span');
            label.textContent = col;
            th.appendChild(label);

            const arrow = document.createElement('span');
            arrow.className = 'sort-arrow';
            th.appendChild(arrow);

            th.addEventListener('click', () => {
                if (state.sortCol === ci) {
                    state.sortDir = state.sortDir === 'asc' ? 'desc' : 'asc';
                } else {
                    state.sortCol = ci;
                    state.sortDir = 'asc';
                }
                state.page = 0;
                rebuildBody();
            });
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        table.appendChild(tbody);

        // Summary footer
        if (visual.summaryData) {
            const tfoot = document.createElement('tfoot');
            if (visual.summaryData.grandTotals) {
                const tr = document.createElement('tr');
                visual.columns.forEach((col, ci) => {
                    const td  = document.createElement('td');
                    td.className = 'summary-cell';
                    const meta = colMeta[ci] || {};
                    const val  = visual.summaryData.grandTotals[col] ?? '';
                    td.textContent = val ? formatValue(val, meta.format) : '';
                    if (meta.align) td.style.textAlign = meta.align;
                    tr.appendChild(td);
                });
                tfoot.appendChild(tr);
            }
            if (visual.summaryData.aggregates && visual.summaryData.aggregates.length > 0) {
                const tr = document.createElement('tr');
                const td = document.createElement('td');
                td.colSpan = visual.columns.length;
                td.className = 'summary-aggregates';
                visual.summaryData.aggregates.forEach(agg => {
                    const sp = document.createElement('span');
                    sp.textContent = (agg.alias || (agg.aggregate + '(' + agg.column + ')')) + ' = ' + agg.value;
                    td.appendChild(sp);
                });
                tr.appendChild(td);
                tfoot.appendChild(tr);
            }
            table.appendChild(tfoot);
        }

        wrapper.appendChild(table);

        const paginationRow = document.createElement('div');
        paginationRow.className = 'table-pagination';
        wrapper.appendChild(paginationRow);

        function updateSortArrows() {
            Array.from(headerRow.children).forEach((th, ci) => {
                const arrow = th.querySelector('.sort-arrow');
                if (arrow) arrow.textContent = state.sortCol === ci ? (state.sortDir === 'asc' ? ' ▲' : ' ▼') : '';
            });
        }

        function rebuildBody() {
            const filtered   = getFilteredRows();
            const totalPages = pageSize > 0 ? Math.max(1, Math.ceil(filtered.length / pageSize)) : 1;
            if (state.page >= totalPages) state.page = Math.max(0, totalPages - 1);

            const start    = pageSize > 0 ? state.page * pageSize : 0;
            const pageRows = pageSize > 0 ? filtered.slice(start, start + pageSize) : filtered;

            tbody.innerHTML = '';
            pageRows.forEach((row, localIdx) => {
                const origIdx = allRows.indexOf(row);
                const tr = document.createElement('tr');
                if (isClickable) tr.style.cursor = 'pointer';

                // Striped
                if (striped && (start + localIdx) % 2 === 1) tr.classList.add('table-row-alt');

                // Row background / font color from FORMATTING rules
                const rowBg   = Array.isArray(visual.rowStyles)     ? visual.rowStyles[origIdx]     : null;
                const rowFont = Array.isArray(visual.rowFontStyles)  ? visual.rowFontStyles[origIdx] : null;
                if (rowBg)   tr.style.backgroundColor = rowBg;
                if (rowFont) tr.style.color = rowFont;

                visual.columns.forEach((col, ci) => {
                    const td   = document.createElement('td');
                    const meta = colMeta[ci] || {};
                    const rawVal = row[ci] != null ? String(row[ci]) : '';
                    const fmtVal = formatValue(rawVal, meta.format || opts['FORMAT']);
                    if (meta.align) td.style.textAlign = meta.align;

                    // COLOR_SCALE: gradient background based on column min/max
                    if (meta.colorScaleFrom && meta.colorScaleTo && meta.colorScaleMax !== undefined) {
                        const num = parseFloat(rawVal);
                        if (!isNaN(num)) {
                            const range = (meta.colorScaleMax - meta.colorScaleMin) || 1;
                            const t = Math.max(0, Math.min(1, (num - meta.colorScaleMin) / range));
                            td.style.backgroundColor = interpolateColor(meta.colorScaleFrom, meta.colorScaleTo, t);
                        }
                    }

                    // DATA_BAR: proportional fill bar behind cell text
                    if (meta.dataBar && meta.dataBarMax !== undefined) {
                        const num = parseFloat(rawVal);
                        if (!isNaN(num) && meta.dataBarMax > meta.dataBarMin) {
                            const pct = Math.max(0, Math.min(100, (num - meta.dataBarMin) / (meta.dataBarMax - meta.dataBarMin) * 100));
                            td.style.position = 'relative';
                            td.style.padding = '0';
                            const bar = document.createElement('div');
                            bar.className = 'data-bar-fill';
                            bar.style.width = pct.toFixed(1) + '%';
                            bar.style.backgroundColor = meta.dataBarColor || '#4472C4';
                            td.appendChild(bar);
                            const span = document.createElement('span');
                            span.className = 'data-bar-label';
                            span.textContent = fmtVal;
                            td.appendChild(span);
                        } else {
                            td.textContent = fmtVal;
                        }
                    } else if (meta.cellRenderer === 'image') {
                        // IMAGE: render <img> from URL value
                        if (rawVal) {
                            const img = document.createElement('img');
                            img.src = rawVal;
                            img.alt = '';
                            img.style.maxHeight = (meta.imageWidth || 32) + 'px';
                            img.style.maxWidth  = (meta.imageWidth ? meta.imageWidth * 3 : 96) + 'px';
                            img.style.verticalAlign = 'middle';
                            td.appendChild(img);
                        }
                    } else if (meta.cellRenderer === 'hyperlink') {
                        // HYPERLINK: render <a> — only allow http/https to prevent injection
                        const href = rawVal || '';
                        const a = document.createElement('a');
                        a.href = /^https?:\/\//i.test(href) ? href : '#';
                        a.target = '_blank';
                        a.rel = 'noopener noreferrer';
                        a.textContent = meta.hyperlinkLabel || href;
                        td.appendChild(a);
                    } else if (meta.cellRenderer === 'sparkline') {
                        // SPARKLINE: inline SVG mini-chart from JSON array value
                        const svg = rawVal ? buildSparklineSvg(rawVal, meta.sparklineType || 'line', null) : '';
                        if (svg) {
                            td.innerHTML = svg;
                            td.style.verticalAlign = 'middle';
                            td.style.lineHeight = '0';
                        } else {
                            td.textContent = '';
                        }
                    } else {
                        td.textContent = fmtVal;
                    }
                    tr.appendChild(td);
                });

                if (isClickable || crossFilter) {
                    tr.addEventListener('click', (e) => {
                        if (crossFilter) {
                            const xCol = opts['mapping:x'] || (visual.columns && visual.columns[0]);
                            const xIdx = xCol ? visual.columns.findIndex(c => c.toLowerCase() === xCol.toLowerCase()) : 0;
                            applyPageCrossFilter(container, String(row[xIdx]), xCol, visual.name, e);
                        } else {
                            clickActions.forEach(action => executeAction(action, row, visual.columns, visual.name, visual));
                        }
                    });
                }
                tbody.appendChild(tr);
            });

            updateSortArrows();

            // Pagination controls
            paginationRow.innerHTML = '';
            if (pageSize > 0 && totalPages > 1) {
                const prev = document.createElement('button');
                prev.textContent = '◀';
                prev.disabled = state.page === 0;
                prev.addEventListener('click', () => { state.page--; rebuildBody(); });

                const info = document.createElement('span');
                info.className = 'pagination-info';
                info.textContent = `${start + 1}–${Math.min(start + pageSize, filtered.length)} of ${filtered.length}`;

                const next = document.createElement('button');
                next.textContent = '▶';
                next.disabled = state.page >= totalPages - 1;
                next.addEventListener('click', () => { state.page++; rebuildBody(); });

                paginationRow.append(prev, info, next);
            }
        }

        // Right-click → Drill Down & Export
        wrapper.addEventListener('contextmenu', e => {
            e.preventDefault();
            const tr  = e.target.closest('tr');
            const idx = tr ? Array.from(tbody.rows).indexOf(tr) : -1;
            const filtered = getFilteredRows();
            const start = pageSize > 0 ? state.page * pageSize : 0;
            const rowData = idx >= 0 ? (pageSize > 0 ? filtered : allRows)[start + idx] : null;
            showCtxMenu(e.clientX, e.clientY, visual, rowData);
        });

        rebuildBody();
        container.appendChild(wrapper);
    }

    // ── MATRIX (Pivot / Cross-tab) ────────────────────────────────────────────
    // chartConfig carries JSON with { __matrix, rowHeaders, colHeaders, colParts, rows, grandTotals }.

    function renderMatrix(container, visual) {
        let meta;
        try { meta = visual.chartConfig ? JSON.parse(visual.chartConfig) : null; } catch(e) { meta = null; }
        if (!meta || !meta.__matrix) {
            container.appendChild(noDataEl('No pivot data available'));
            return;
        }

        const sep = '\u001F';
        const rowHeaders = meta.rowHeaders || [];
        const rows = meta.rows || [];
        const grandTotals = meta.grandTotals || null;
        const matrixAggregate = String(meta.aggregate || 'SUM').toUpperCase();
        const colParts = Array.isArray(meta.colParts) && meta.colParts.length > 0
            ? meta.colParts.map(p => Array.isArray(p) ? p.map(v => String(v ?? '')) : [String(p ?? '')])
            : (meta.colValues || []).map(v => [String(v ?? '')]);
        const colHeaders = meta.colHeaders && meta.colHeaders.length > 0
            ? meta.colHeaders
            : (colParts[0] || ['Column']).map((_, i) => i === 0 ? 'Column' : `Column ${i + 1}`);
        const colDepth = Math.max(1, colHeaders.length, ...colParts.map(p => p.length));
        const rowDepth = Math.max(1, rowHeaders.length);
        const valueHeaders = Array.isArray(meta.valueHeaders) ? meta.valueHeaders : null;
        const valueCount = valueHeaders ? valueHeaders.length : 1;
        const subtotalsEnabled = !!meta.subtotalsEnabled;
        const stateKey = `matrix:${visual.name || visual.id || ''}`;
        const state = _uiStates[stateKey] || (_uiStates[stateKey] = { collapsedRows: {}, collapsedCols: {} });
        state.collapsedRows = state.collapsedRows || {};
        state.collapsedCols = state.collapsedCols || {};

        const wrapper = document.createElement('div');
        wrapper.className = 'table-wrapper';
        let heightOpt = visual.styles ? (visual.styles['HEIGHT'] || visual.styles['height']) : null;
        if (heightOpt) wrapper.style.maxHeight = heightOpt;

        const table = document.createElement('table');
        table.className = 'matrix-table';

        const leaves = colParts.map((parts, index) => ({
            index,
            parts: Array.from({ length: colDepth }, (_, i) => parts[i] || ''),
            key: Array.from({ length: colDepth }, (_, i) => parts[i] || '').join(sep)
        }));

        function colPrefixKey(parts, level) {
            return parts.slice(0, level + 1).join(sep);
        }

        function rowPrefixKey(parts, level) {
            return parts.slice(0, level + 1).join(sep);
        }

        function hasColumnChildren(parts, level) {
            if (level >= colDepth - 1) return false;
            const key = colPrefixKey(parts, level);
            const nextValues = new Set(leaves
                .filter(leaf => colPrefixKey(leaf.parts, level) === key)
                .map(leaf => leaf.parts[level + 1]));
            return nextValues.size > 0;
        }

        function buildColumnNodes(level, prefix, sourceLeaves) {
            if (level >= colDepth) return [];
            const buckets = new Map();
            sourceLeaves.forEach(leaf => {
                const label = leaf.parts[level] || '';
                if (!buckets.has(label)) buckets.set(label, []);
                buckets.get(label).push(leaf);
            });
            return Array.from(buckets, ([label, bucket]) => {
                const parts = prefix.concat(label);
                return {
                    label,
                    level,
                    parts,
                    key: parts.join(sep),
                    leaves: bucket,
                    children: buildColumnNodes(level + 1, parts, bucket)
                };
            });
        }

        function flattenColumns(nodes, output = []) {
            nodes.forEach(node => {
                const hasChildren = node.children.length > 0;
                if (!hasChildren || state.collapsedCols[node.key]) {
                    output.push(node);
                } else {
                    flattenColumns(node.children, output);
                }
            });
            return output;
        }

        const visibleColumns = flattenColumns(buildColumnNodes(0, [], leaves));

        // Expanded columns = visibleColumns × valueCount (interleaved: col0v0, col0v1, col1v0, col1v1, ...)
        const expandedCols = [];
        visibleColumns.forEach(col => {
            for (let vi = 0; vi < valueCount; vi++) expandedCols.push({ col, vi });
        });

        function numericCell(value) {
            const n = parseFloat(String(value ?? '').replace(/,/g, ''));
            return Number.isFinite(n) ? n : null;
        }

        function formatMatrixNumber(total, sawAny) {
            if (!sawAny) return '';
            return Number.isInteger(total) ? String(total) : String(Number(total.toFixed(6)));
        }

        function aggregateNumbers(values) {
            if (!values.length) return '';
            if (matrixAggregate === 'MIN') return formatMatrixNumber(Math.min(...values), true);
            if (matrixAggregate === 'MAX') return formatMatrixNumber(Math.max(...values), true);
            if (matrixAggregate === 'AVG') return formatMatrixNumber(values.reduce((a, b) => a + b, 0) / values.length, true);
            return formatMatrixNumber(values.reduce((a, b) => a + b, 0), true);
        }

        function aggregateColumn(row, col, vi) {
            const values = [];
            col.leaves.forEach(leaf => {
                const n = numericCell(row[rowDepth + leaf.index * valueCount + (vi || 0)]);
                if (n != null) values.push(n);
            });
            return aggregateNumbers(values);
        }

        function aggregateRows(sourceRows, col, vi) {
            const values = [];
            sourceRows.forEach(row => {
                col.leaves.forEach(leaf => {
                    const n = numericCell(row[rowDepth + leaf.index * valueCount + (vi || 0)]);
                    if (n != null) values.push(n);
                });
            });
            return aggregateNumbers(values);
        }

        function buildRowNodes(level, sourceRows) {
            const buckets = new Map();
            sourceRows.forEach(row => {
                const label = String(row[level] ?? '');
                if (!buckets.has(label)) buckets.set(label, []);
                buckets.get(label).push(row);
            });
            return Array.from(buckets, ([label, bucket]) => ({
                label,
                level,
                parts: bucket[0].slice(0, level + 1).map(v => String(v ?? '')),
                rows: bucket,
                children: level < rowDepth - 1 ? buildRowNodes(level + 1, bucket) : []
            }));
        }

        function appendToggle(cell, key, isCollapsed, onClick) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'matrix-toggle';
            button.textContent = isCollapsed ? '+' : '-';
            button.setAttribute('aria-label', isCollapsed ? 'Expand' : 'Collapse');
            button.addEventListener('click', e => {
                e.preventDefault();
                e.stopPropagation();
                onClick();
                renderManifest(_lastManifest);
            });
            cell.appendChild(button);
        }

        function appendRowNode(tbody, node) {
            const isLeaf = node.children.length === 0;
            const key = rowPrefixKey(node.parts, node.level);
            const isCollapsed = !!state.collapsedRows[key];

            const tr = document.createElement('tr');
            tr.className = isLeaf ? 'matrix-leaf-row' : 'matrix-group-row';

            for (let i = 0; i < rowDepth; i++) {
                const td = document.createElement('td');
                td.className = 'matrix-dim';
                if (i === node.level) {
                    td.style.paddingLeft = `${10 + node.level * 18}px`;
                    if (!isLeaf) {
                        appendToggle(td, key, isCollapsed, () => {
                            state.collapsedRows[key] = !isCollapsed;
                        });
                    }
                    td.appendChild(document.createTextNode(node.label));
                } else if (isLeaf) {
                    td.textContent = String(node.rows[0][i] ?? '');
                }
                tr.appendChild(td);
            }

            expandedCols.forEach(({ col, vi }) => {
                const td = document.createElement('td');
                td.className = 'matrix-val';
                const val = isLeaf ? aggregateColumn(node.rows[0], col, vi) : aggregateRows(node.rows, col, vi);
                td.textContent = formatValue(val, null);
                tr.appendChild(td);
            });

            tbody.appendChild(tr);
            if (!isLeaf && !isCollapsed) {
                node.children.forEach(child => appendRowNode(tbody, child));
                if (subtotalsEnabled) {
                    const subtr = document.createElement('tr');
                    subtr.className = 'matrix-subtotal-row';
                    for (let i = 0; i < rowDepth; i++) {
                        const td = document.createElement('td');
                        td.className = i === node.level ? 'matrix-dim matrix-subtotal-label' : 'matrix-dim';
                        if (i === node.level) td.textContent = node.label + ' Total';
                        subtr.appendChild(td);
                    }
                    expandedCols.forEach(({ col, vi }) => {
                        const td = document.createElement('td');
                        td.className = 'matrix-val matrix-subtotal-val';
                        td.textContent = formatValue(aggregateRows(node.rows, col, vi), null);
                        subtr.appendChild(td);
                    });
                    tbody.appendChild(subtr);
                }
            }
        }

        function appendColumnHeaderButton(th, node) {
            const canCollapse = node.leaves.length > 1 || hasColumnChildren(node.parts, node.level);
            if (!canCollapse) return;
            const key = node.key;
            appendToggle(th, key, !!state.collapsedCols[key], () => {
                state.collapsedCols[key] = !state.collapsedCols[key];
            });
        }

        // Header rows
        const thead = document.createElement('thead');
        const totalHeaderRows = colDepth + (valueCount > 1 ? 1 : 0);
        for (let level = 0; level < colDepth; level++) {
            const headerRow = document.createElement('tr');
            if (level === 0) {
                rowHeaders.forEach(h => {
                    const th = document.createElement('th');
                    th.textContent = h;
                    th.className = 'matrix-dim-header';
                    th.rowSpan = totalHeaderRows;
                    headerRow.appendChild(th);
                });
            }
            visibleColumns.forEach(col => {
                const th = document.createElement('th');
                th.className = 'matrix-val-header';
                if (valueCount > 1) th.colSpan = valueCount;
                const label = col.parts[level] || '';
                if (label) {
                    const prefixParts = col.parts.slice(0, level + 1);
                    const prefixKey = prefixParts.join(sep);
                    const headerNode = {
                        parts: prefixParts,
                        level,
                        key: prefixKey,
                        leaves: leaves.filter(leaf => colPrefixKey(leaf.parts, level) === prefixKey)
                    };
                    appendColumnHeaderButton(th, headerNode);
                    th.appendChild(document.createTextNode(label));
                }
                headerRow.appendChild(th);
            });
            thead.appendChild(headerRow);
        }
        // Value sub-header row when multiple VALUE columns
        if (valueCount > 1) {
            const valHeaderRow = document.createElement('tr');
            visibleColumns.forEach(() => {
                valueHeaders.forEach(vh => {
                    const th = document.createElement('th');
                    th.className = 'matrix-val-header matrix-value-subheader';
                    th.textContent = vh;
                    valHeaderRow.appendChild(th);
                });
            });
            thead.appendChild(valHeaderRow);
        }
        table.appendChild(thead);

        // Data rows
        const tbody = document.createElement('tbody');
        buildRowNodes(0, rows).forEach(node => appendRowNode(tbody, node));

        // Grand total row
        if (grandTotals && grandTotals.length > 0) {
            const tr = document.createElement('tr');
            tr.className = 'matrix-grand-total';
            for (let i = 0; i < rowDepth; i++) {
                const td = document.createElement('td');
                td.textContent = i === 0 ? 'Grand Total' : '';
                td.className = 'matrix-dim matrix-total-label';
                tr.appendChild(td);
            }
            expandedCols.forEach(({ col, vi }) => {
                const td = document.createElement('td');
                const values = [];
                col.leaves.forEach(leaf => {
                    const n = numericCell(grandTotals[rowDepth + leaf.index * valueCount + vi]);
                    if (n != null) values.push(n);
                });
                td.textContent = formatValue(aggregateNumbers(values), null);
                td.className = 'matrix-val matrix-total-val';
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        }

        table.appendChild(tbody);
        wrapper.appendChild(table);
        container.appendChild(wrapper);
    }

    function mergeHighlightData(currentVisual, option) {
        if (!currentVisual.highlightRows || currentVisual.highlightRows.length === 0) {
            console.debug(`[Ghost] No highlightRows for ${currentVisual.name}`);
            return;
        }

        const type = (currentVisual.visualType || '').toUpperCase();
        const isCartesian = ['BAR', 'HBAR', 'HORIZONTALBAR', 'LINE', 'COMBO'].includes(type);
        const isCircular  = ['PIE', 'DONUT'].includes(type);
        const isScatter   = ['SCATTER', 'BUBBLE'].includes(type);

        if (!isCartesian && !isCircular && !isScatter) return;

        // Ensure we have mappings
        const xCol = (currentVisual.options || {})['mapping:x'] || currentVisual.columns[0];
        const yCol = (currentVisual.options || {})['mapping:y'] || (currentVisual.columns.length > 1 ? currentVisual.columns[1] : null);
        const seriesCol = (currentVisual.options || {})['mapping:series'];
        
        const xIdx = currentVisual.columns.indexOf(xCol);
        const yIdx = yCol ? currentVisual.columns.indexOf(yCol) : -1;
        const sIdx = seriesCol ? currentVisual.columns.indexOf(seriesCol) : -1;

        if (xIdx < 0) return;

        // Map "Universe" values by X
        const universeMap = {};
        currentVisual.rows.forEach(r => { universeMap[String(r[xIdx])] = yIdx >= 0 ? (parseFloat(r[yIdx]) || 0) : 1; });

        // Map "Selection" values by X
        const selectionMap = {};
        currentVisual.highlightRows.forEach(r => { selectionMap[String(r[xIdx])] = yIdx >= 0 ? (parseFloat(r[yIdx]) || 0) : 1; });

        if (!option.series || option.series.length === 0) return;

        // ── Strategy A: Cartesian Overlay (Bar / Single-Series Line) ──────────
        if (isCartesian && option.series.length === 1 && (type === 'BAR' || type === 'HBAR' || type === 'HORIZONTALBAR')) {
            const categories = (option.xAxis && option.xAxis.data) || (option.yAxis && option.yAxis.data) || [];
            if (categories.length > 0) {
                // Snapshot original data items (which carry per-item itemStyle.color from COLORS mapping)
                const origData = option.series[0].data.slice();

                // Ghost = universe bars: preserve each bar's original color but dim to 30% opacity
                const ghostSeries = JSON.parse(JSON.stringify(option.series[0]));
                ghostSeries.name = 'Total (Universe)';
                ghostSeries.data = ghostSeries.data.map(item => {
                    if (typeof item === 'object' && item !== null) {
                        const c = item.itemStyle && item.itemStyle.color;
                        return { ...item, itemStyle: c ? { color: c, opacity: 0.3 } : { opacity: 0.3 } };
                    }
                    return { value: item, itemStyle: { opacity: 0.3 } };
                });
                delete ghostSeries.itemStyle; // remove any series-level color override
                ghostSeries.emphasis = { disabled: true };
                ghostSeries.z = 1;

                // Active = selection bars: use the selection value (East's portion) so the bar height is
                // proportional to the selected slice, not the full universe.  Preserve per-item color.
                const activeSeries = option.series[0];
                activeSeries.name = 'Filtered';
                activeSeries.data = categories.map((cat, i) => {
                    const selVal = selectionMap[String(cat)];
                    if (selVal !== undefined) {
                        const orig = origData[i];
                        const color = (typeof orig === 'object' && orig !== null && orig.itemStyle?.color) ? orig.itemStyle.color : null;
                        return color ? { value: selVal, itemStyle: { color } } : selVal;
                    }
                    return { value: 0, itemStyle: { opacity: 0 } };
                });
                activeSeries.barGap = '-100%';
                activeSeries.z = 2;

                option.series = [ghostSeries, activeSeries];
                if (!option.legend) option.legend = { show: true };
                return;
            }
        }

        // ── Strategy B: Dimming (Pie / Scatter / Multi-Series Line) ──────────
        // Instead of new series, we modify the existing ones to dim non-selected items.
        
        // Build a Set of selected X values for fast lookup
        const selectedKeys = new Set(currentVisual.highlightRows.map(r => String(r[xIdx])));

        if (isCircular) {
            // For Pie/Donut, items are in series[0].data: [{name, value}, ...]
            // Dim non-selected slices by reducing opacity only — preserve original slice color
            (option.series || []).forEach(s => {
                if (s.type !== 'pie') return;
                s.data.forEach(item => {
                    if (!selectedKeys.has(String(item.name))) {
                        item.itemStyle = Object.assign({}, item.itemStyle, { opacity: 0.2 });
                        item.label = { show: false };
                    }
                });
            });
        } 
        else if (isScatter) {
            // For Scatter, data is usually [x, y, ...]
            // This is harder because we don't have item names. 
            // We'll rely on dataIndex if rows match exactly, or just dim everything not in selectionMap.
            (option.series || []).forEach(s => {
                s.data = s.data.map((point, idx) => {
                    const row = (currentVisual.rows || [])[idx];
                    const isMatch = row && selectedKeys.has(String(row[xIdx]));
                    if (!isMatch) {
                        return {
                            value: point,
                            itemStyle: { color: '#e0e0e0', opacity: 0.1 }
                        };
                    }
                    return point;
                });
            });
        }
        else if (isCartesian) {
            // Multi-series Bar or Line: dim entire series not in the selection,
            // and dim individual data points on active series that don't match the X filter.
            // Preserve original colors throughout — only opacity changes.
            (option.series || []).forEach(s => {
                const sName = s.name;
                const hasMatch = currentVisual.highlightRows.some(r => sIdx >= 0 && String(r[sIdx]) === sName);

                if (sIdx >= 0 && !hasMatch) {
                    // Dim the entire series that isn't selected
                    if (s.type === 'line') {
                        s.lineStyle = Object.assign({}, s.lineStyle, { opacity: 0.15 });
                        s.itemStyle = Object.assign({}, s.itemStyle, { opacity: 0 });
                    } else {
                        s.itemStyle = Object.assign({}, s.itemStyle, { opacity: 0.2 });
                    }
                } else {
                    // Active series: dim individual points that don't match the selected X values
                    const categories = (option.xAxis && option.xAxis.data) || (option.yAxis && option.yAxis.data) || [];
                    if (categories.length > 0) {
                        s.data = s.data.map((val, idx) => {
                            const cat = String(categories[idx]);
                            if (!selectedKeys.has(cat)) {
                                // Preserve original per-item color if present, just lower opacity
                                const origColor = (typeof val === 'object' && val !== null && val.itemStyle?.color)
                                    ? val.itemStyle.color : null;
                                const origVal = (typeof val === 'object' && val !== null) ? val.value : val;
                                return {
                                    value: origVal,
                                    itemStyle: origColor ? { color: origColor, opacity: 0.2 } : { opacity: 0.2 }
                                };
                            }
                            return val;
                        });
                    }
                }
            });
        }
    }

    // Dims non-selected bars in the source chart while keeping selected bars at full opacity.
    // Operates on per-item itemStyle.opacity so original colors are always preserved.
    function applySourceChartOpacity(option, selections) {
        const selectedKeys = new Set(selections.map(s => s.value));
        const categories = (option.xAxis && option.xAxis.data) || (option.yAxis && option.yAxis.data) || [];
        if (categories.length === 0 || !option.series || option.series.length === 0) return;

        option.series[0].data = (option.series[0].data || []).map((item, i) => {
            const cat = String(categories[i] || '');
            const opacity = selectedKeys.has(cat) ? 1.0 : 0.2;
            if (typeof item === 'object' && item !== null) {
                const color = item.itemStyle && item.itemStyle.color;
                return { ...item, itemStyle: color ? { color, opacity } : { opacity } };
            }
            return { value: item, itemStyle: { opacity } };
        });
    }

    function visualCanReflectSelections(visual, selections) {
        if (!visual || !selections || selections.length === 0) return false;
        const names = new Set((visual.columns || []).map(c => String(c || '').toLowerCase()));
        for (const key in (visual.options || {})) {
            if (key.toLowerCase().startsWith('mapping:')) {
                names.add(String(visual.options[key] || '').toLowerCase());
            }
        }
        return selections.some(s => names.has(String(s.column || '').toLowerCase()));
    }

    // ── Card ────────────────────────────────────────────────────────────────

    function abbreviateNumber(num, formatHint) {
        const abs = Math.abs(num);
        let suffix = '', divisor = 1;
        if (abs >= 1e9)      { suffix = 'B'; divisor = 1e9; }
        else if (abs >= 1e6) { suffix = 'M'; divisor = 1e6; }
        else if (abs >= 1e3) { suffix = 'K'; divisor = 1e3; }
        const isCurrency = formatHint && formatHint.charAt(0).toUpperCase() === 'C';
        const prefix = isCurrency ? '$' : '';
        const abbreviated = num / divisor;
        const decimals = suffix ? 2 : 0;
        const sign = num < 0 ? '-' : '';
        return sign + prefix + abbreviated.toFixed(decimals) + suffix;
    }

    function renderCard(container, visual) {
        const opts = visual.options || {};
        const cardTitle    = getOption(opts, 'title') || visual.name;
        const cardSubtitle = getOption(opts, 'subtitle') || '';

        // ── Value ──────────────────────────────────────────────────────────
        const valueColName = getOption(opts, 'mapping:value');
        const valIdx = valueColName
            ? (visual.columns || []).findIndex(c => c.toLowerCase() === valueColName.toLowerCase())
            : 0;
        const row = visual.rows && visual.rows[0] ? visual.rows[0] : null;
        const rawValue = row ? parseFloat(row[valIdx >= 0 ? valIdx : 0] ?? '0') : null;

        const formatOpt    = getOption(opts, 'format');
        const doAbbreviate = isOn(getOption(opts, 'abbreviate'));
        const prefix       = getOption(opts, 'prefix') || '';
        const suffix       = getOption(opts, 'suffix') || '';

        let displayValue;
        if (rawValue === null) {
            displayValue = 'No data';
        } else if (doAbbreviate) {
            displayValue = prefix + abbreviateNumber(rawValue, formatOpt) + suffix;
        } else if (formatOpt) {
            displayValue = prefix + formatValue(rawValue, formatOpt) + suffix;
        } else {
            displayValue = prefix + String(rawValue) + suffix;
        }

        // ── Goal ───────────────────────────────────────────────────────────
        const goalColName = getOption(opts, 'mapping:goal');
        let goalValue = null;
        if (goalColName && row) {
            const gIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === goalColName.toLowerCase());
            if (gIdx >= 0) goalValue = parseFloat(row[gIdx] ?? '0');
        }
        if (goalValue === null) {
            const goalOpt = getOption(opts, 'goal');
            if (goalOpt !== null) goalValue = parseFloat(goalOpt);
        }

        // ── Status ─────────────────────────────────────────────────────────
        const closePct = parseFloat(getOption(opts, 'close_pct') ?? '0.80');
        const metPct   = parseFloat(getOption(opts, 'met_pct')   ?? '1.00');
        let status = null, ratio = null;
        if (goalValue !== null && rawValue !== null && goalValue !== 0) {
            ratio = rawValue / goalValue;
            if      (ratio >= metPct)   status = 'met';
            else if (ratio >= closePct) status = 'close';
            else                        status = 'missed';
        }

        // ── Colors & icons ─────────────────────────────────────────────────
        const colors = {
            met:    getOption(opts, 'color_met')    || '#10b981',
            close:  getOption(opts, 'color_close')  || '#f59e0b',
            missed: getOption(opts, 'color_missed') || '#ef4444'
        };
        const iconSets = {
            TRAFFIC: { met: '🟢', close: '🟡', missed: '🔴' },
            ARROWS:  { met: '↑',  close: '→',  missed: '↓'  },
            CHECKS:  { met: '✓',  close: '~',  missed: '✗'  }
        };
        const iconSetName  = (getOption(opts, 'icon_set') || '').toUpperCase();
        const presetIcons  = iconSets[iconSetName] || null;
        const icons = {
            met:    getOption(opts, 'icon_met')    ?? (presetIcons ? presetIcons.met    : '✓'),
            close:  getOption(opts, 'icon_close')  ?? (presetIcons ? presetIcons.close  : '⚠'),
            missed: getOption(opts, 'icon_missed') ?? (presetIcons ? presetIcons.missed : '✗')
        };

        // ── Delta ──────────────────────────────────────────────────────────
        const deltaColName = getOption(opts, 'mapping:delta');
        let deltaAmount = null;
        if (deltaColName && row) {
            const dIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === deltaColName.toLowerCase());
            if (dIdx >= 0 && rawValue !== null) deltaAmount = rawValue - parseFloat(row[dIdx] ?? '0');
        }
        const deltaFormat = getOption(opts, 'delta_format') || formatOpt;
        const deltaLabel  = getOption(opts, 'delta_label')  || '';
        const trendDir    = (getOption(opts, 'trend_dir') || 'POSITIVE_UP').toUpperCase();

        // ── Status label override ──────────────────────────────────────────
        let subtitleText = cardSubtitle;
        if (status === 'met'    && getOption(opts, 'label_met'))    subtitleText = getOption(opts, 'label_met');
        if (status === 'close'  && getOption(opts, 'label_close'))  subtitleText = getOption(opts, 'label_close');
        if (status === 'missed' && getOption(opts, 'label_missed')) subtitleText = getOption(opts, 'label_missed');

        // ── Build HTML ─────────────────────────────────────────────────────
        // Status badge
        let badgeHtml = '';
        if (status) {
            badgeHtml = `<span class="card-status-badge" style="background:${escHtml(colors[status])}">${escHtml(icons[status])}</span>`;
        }

        // Delta row
        let deltaHtml = '';
        if (deltaAmount !== null) {
            const isPos    = deltaAmount >= 0;
            const isGood   = trendDir === 'POSITIVE_UP' ? isPos : !isPos;
            const arrow    = isPos ? '▲' : '▼';
            const clr      = isGood ? '#10b981' : '#ef4444';
            const absAmt   = Math.abs(deltaAmount);
            const deltaStr = deltaFormat ? formatValue(absAmt, deltaFormat) : String(absAmt);
            const sign     = isPos ? '+' : '-';
            deltaHtml = `<div class="card-delta" style="color:${clr}">` +
                `<span class="card-delta-arrow">${arrow}</span>` +
                `<span class="card-delta-value">${escHtml(sign + deltaStr)}</span>` +
                (deltaLabel ? `<span class="card-delta-label">${escHtml(deltaLabel)}</span>` : '') +
                `</div>`;
        }

        // Goal display line
        let goalLineHtml = '';
        if (goalValue !== null && isOn(getOption(opts, 'show_goal'))) {
            const gDisplay = doAbbreviate
                ? abbreviateNumber(goalValue, formatOpt)
                : (formatOpt ? formatValue(goalValue, formatOpt) : String(goalValue));
            goalLineHtml = `<div class="card-goal">Target: ${escHtml(gDisplay)}</div>`;
        }

        // % of goal line
        let goalPctHtml = '';
        if (ratio !== null && isOn(getOption(opts, 'show_percent_of_goal'))) {
            goalPctHtml = `<div class="card-goal-pct">${Math.round(ratio * 100)}% of target</div>`;
        }

        // Progress bar or ring
        let progressHtml = '';
        const showProgress  = isOn(getOption(opts, 'show_progress'));
        const progressStyle = (getOption(opts, 'progress_style') || 'BAR').toUpperCase();
        if (showProgress && ratio !== null && status) {
            const pct      = Math.min(ratio * 100, 100);
            const barColor = colors[status];
            if (progressStyle === 'RING') {
                const r = 18, circ = 2 * Math.PI * r;
                const dash = (pct / 100) * circ;
                progressHtml = `<div class="card-progress-ring">` +
                    `<svg width="48" height="48" viewBox="0 0 48 48">` +
                    `<circle cx="24" cy="24" r="${r}" fill="none" stroke="#e5e7eb" stroke-width="4"/>` +
                    `<circle cx="24" cy="24" r="${r}" fill="none" stroke="${escHtml(barColor)}" stroke-width="4"` +
                    ` stroke-dasharray="${dash.toFixed(2)} ${circ.toFixed(2)}" transform="rotate(-90 24 24)"/>` +
                    `</svg><span class="card-ring-pct">${Math.round(pct)}%</span></div>`;
            } else {
                progressHtml = `<div class="card-progress">` +
                    `<div class="card-progress-fill" style="width:${pct.toFixed(1)}%;background:${escHtml(barColor)}"></div>` +
                    `</div>`;
            }
        }

        const cardEl = document.createElement('div');
        cardEl.className = 'card-value' + (status ? ` card-status-${status}` : '');
        cardEl.innerHTML =
            `<div class="card-header-row"><div class="card-label">${escHtml(cardTitle)}</div>${badgeHtml}</div>` +
            (subtitleText ? `<div class="card-subtitle">${escHtml(subtitleText)}</div>` : '') +
            `<div class="card-number">${escHtml(String(displayValue))}</div>` +
            goalLineHtml + goalPctHtml + deltaHtml + progressHtml;
        container.appendChild(cardEl);
    }

    // ── Slicer ──────────────────────────────────────────────────────────────

    function renderSlicer(container, visual, manifest) {
        const wrapper = document.createElement('div');
        wrapper.className = 'slicer-wrapper';
        
        const action = visual.actions.find(a => a.type === 'SET_PARAMETER');
        const paramName = action ? action.parameterName : null;
        
        const typeStr = visual.visualType.toLowerCase();
        const isMulti = typeStr === 'multiselect' || isOn(visual.options['MULTIPLE'] || visual.options['multiple']);

        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const isInteractive = (isWebMode || vscode) && changeActions.length > 0;

        if (isMulti && isInteractive) {
            renderMultiSelectCheckboxes(wrapper, visual, manifest, paramName, changeActions);
        } else {
            const select = document.createElement('select');
            if (paramName) select.setAttribute('data-parameter', paramName);
            if (isMulti) select.multiple = true;

            const valCol = (visual.options['mapping:value'] || visual.columns[0] || 'value').toLowerCase();
            const lblCol = (visual.options['mapping:label'] || visual.columns[1] || visual.columns[0] || 'label').toLowerCase();
            const valIdx = visual.columns.findIndex(c => c.toLowerCase() === valCol);
            const lblIdx = visual.columns.findIndex(c => c.toLowerCase() === lblCol);
            const finalValIdx = valIdx >= 0 ? valIdx : 0;
            const finalLblIdx = lblIdx >= 0 ? lblIdx : (visual.columns.length > 1 ? 1 : 0);

            visual.rows.forEach(row => {
                const opt = document.createElement('option');
                opt.value = row[finalValIdx];
                opt.textContent = row[finalLblIdx];
                select.appendChild(opt);
            });

            if (paramName && manifest && manifest.parameters) {
                const current = getParam(manifest.parameters, paramName);
                if (current !== undefined) select.value = current;
            }

            if (isInteractive) {
                select.addEventListener('change', () => {
                    let val = select.value;
                    if (select.multiple) {
                        val = Array.from(select.selectedOptions).map(o => o.value).join(',');
                    }
                    const batch = {};
                    changeActions.forEach(action => {
                        batch[action.parameterName] = val;
                    });
                    postParameters(batch).then(m => { if (m) renderManifest(m); });
                });
                wrapper.appendChild(select);
            } else {
                const note = document.createElement('p');
                note.className = 'slicer-note';
                note.textContent = '[Slicer \u2014 interactive in ReportPlayer only]';
                wrapper.appendChild(note);
            }
        }
        container.appendChild(wrapper);
    }

    function renderMultiSelectCheckboxes(container, visual, manifest, paramName, changeActions) {
        const isDropdown = (getStyle(visual.styles, 'LAYOUT') || '').toUpperCase() === 'DROPDOWN';

        const list = document.createElement('div');
        list.className = isDropdown ? 'multiselect-popup' : 'multiselect-list';
        if (paramName) list.setAttribute('data-parameter', paramName);

        const valCol = (visual.options['mapping:value'] || visual.columns[0] || 'value').toLowerCase();
        const valIdx = visual.columns.findIndex(c => c.toLowerCase() === valCol);
        const finalValIdx = valIdx >= 0 ? valIdx : 0;

        const currentVal = getParam(manifest.parameters, paramName) || '';
        const selected = new Set(String(currentVal).split(',').map(v => v.trim()).filter(Boolean));

        let updateToggleText = null;

        const uniqueOptions = [...new Set(visual.rows.map(r => String(r[finalValIdx])))].sort();

        uniqueOptions.forEach(optVal => {
            const item = document.createElement('label');
            item.className = 'multiselect-item';
            
            const cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.value = optVal;
            cb.checked = selected.has(optVal);
            
            cb.addEventListener('change', () => {
                if (cb.checked) selected.add(optVal);
                else selected.delete(optVal);
                
                if (updateToggleText) updateToggleText();

                const val = Array.from(selected).join(',');
                changeActions.forEach(a => {
                    postParameters({ [a.parameterName]: val }).then(m => { if (m) renderManifest(m); });
                });
            });

            const span = document.createElement('span');
            span.textContent = optVal;

            item.appendChild(cb);
            item.appendChild(span);
            list.appendChild(item);
        });

        if (isDropdown) {
            const wrapper = document.createElement('div');
            wrapper.className = 'multiselect-dropdown';

            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'multiselect-toggle';
            
            updateToggleText = () => {
                if (selected.size === 0) toggle.innerHTML = '<span>All</span>';
                else if (selected.size === 1) toggle.innerHTML = `<span>${Array.from(selected)[0]}</span>`;
                else toggle.innerHTML = `<span>${selected.size} selected</span>`;
                toggle.innerHTML += '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>';
            };
            updateToggleText();

            toggle.addEventListener('click', (e) => {
                e.stopPropagation();
                const isOpen = list.classList.contains('open');
                document.querySelectorAll('.multiselect-popup.open').forEach(p => p.classList.remove('open'));
                if (!isOpen) list.classList.add('open');
            });

            list.addEventListener('click', e => e.stopPropagation());

            // Add global click listener once per dropdown
            document.addEventListener('click', () => {
                list.classList.remove('open');
            });

            wrapper.appendChild(toggle);
            wrapper.appendChild(list);
            container.appendChild(wrapper);
        } else {
            container.appendChild(list);
        }
    }

    // ── Text ────────────────────────────────────────────────────────────────

    function renderText(container, visual) {
        // Static content from CONTENT/DEFAULT clause; fall back to MAPPINGS(CONTENT=col) first row
        let content = visual.defaultValue || '';
        if (!content && visual.columns && visual.rows && visual.rows.length > 0) {
            const idx = visual.columns.findIndex(c => c.toLowerCase() === 'content');
            if (idx >= 0 && visual.rows[0][idx] != null) content = String(visual.rows[0][idx]);
        }

        const opts = visual.options || {};
        const align = (opts['ALIGN'] || opts['align'] || 'left').toLowerCase();
        const useMd = (opts['MARKDOWN'] || opts['markdown'] || 'ON').toUpperCase() !== 'OFF';

        const div = document.createElement('div');
        div.className = 'text-visual';
        div.style.textAlign = align;
        div.innerHTML = useMd ? simpleMarkdown(content) : escHtml(content).replace(/\n/g, '<br>');
        container.appendChild(div);
    }

    // Markdown → HTML renderer supporting: headers, bold, italic, inline code, links,
    // fenced code blocks, blockquotes, unordered/ordered lists, tables, horizontal rules.
    function simpleMarkdown(src) {
        if (!src) return '';

        // Unescape ETL-SQL escaped newlines
        const raw = String(src).replace(/\\n/g, '\n');

        // Phase 1: extract fenced code blocks to protect them from other processing
        const codeBlocks = [];
        const withoutCode = raw.replace(/```([^\n]*)\n([\s\S]*?)```/g, (_, lang, code) => {
            const escaped = escHtml(code.replace(/\n$/, ''));
            const cls = lang.trim() ? ` class="language-${escHtml(lang.trim())}"` : '';
            codeBlocks.push(`<pre><code${cls}>${escaped}</code></pre>`);
            return `\x00CODE${codeBlocks.length - 1}\x00`;
        });

        // Phase 2: process line-by-line blocks
        const lines = withoutCode.split('\n');
        const out = [];
        let i = 0;

        function inlineFormat(text) {
            return escHtml(text)
                .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
                .replace(/\*(.+?)\*/g,     '<em>$1</em>')
                .replace(/`(.+?)`/g,       '<code>$1</code>')
                .replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, url) => {
                    // Only allow safe protocols
                    const safe = /^(https?:|mailto:|\/)/i.test(url.trim());
                    if (!safe) return escHtml(label);
                    return `<a href="${escHtml(url)}" target="_blank" rel="noopener noreferrer">${escHtml(label)}</a>`;
                });
        }

        while (i < lines.length) {
            const line = lines[i];
            const trimmed = line.trim();

            // Code block placeholder
            if (/^\x00CODE\d+\x00$/.test(trimmed)) {
                const idx = parseInt(trimmed.replace(/\x00CODE(\d+)\x00/, '$1'), 10);
                out.push(codeBlocks[idx]);
                i++;
                continue;
            }

            // Horizontal rule: --- or *** or ___ (3+ chars, only that char)
            if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
                out.push('<hr>');
                i++;
                continue;
            }

            // ATX headings
            const hMatch = trimmed.match(/^(#{1,6})\s+(.+)$/);
            if (hMatch) {
                const level = hMatch[1].length;
                out.push(`<h${level}>${inlineFormat(hMatch[2])}</h${level}>`);
                i++;
                continue;
            }

            // Blockquote: collect consecutive > lines
            if (trimmed.startsWith('> ')) {
                const bqLines = [];
                while (i < lines.length && lines[i].trim().startsWith('> ')) {
                    bqLines.push(inlineFormat(lines[i].trim().replace(/^>\s?/, '')));
                    i++;
                }
                out.push(`<blockquote>${bqLines.join('<br>')}</blockquote>`);
                continue;
            }

            // Unordered list: collect consecutive - or * lines
            if (/^[-*]\s/.test(trimmed)) {
                const items = [];
                while (i < lines.length && /^[-*]\s/.test(lines[i].trim())) {
                    items.push(`<li>${inlineFormat(lines[i].trim().replace(/^[-*]\s/, ''))}</li>`);
                    i++;
                }
                out.push(`<ul>${items.join('')}</ul>`);
                continue;
            }

            // Ordered list: collect consecutive N. lines
            if (/^\d+\.\s/.test(trimmed)) {
                const items = [];
                while (i < lines.length && /^\d+\.\s/.test(lines[i].trim())) {
                    items.push(`<li>${inlineFormat(lines[i].trim().replace(/^\d+\.\s/, ''))}</li>`);
                    i++;
                }
                out.push(`<ol>${items.join('')}</ol>`);
                continue;
            }

            // Markdown table: lines starting with |
            if (trimmed.startsWith('|') && trimmed.includes('|', 1)) {
                const tableLines = [];
                while (i < lines.length && lines[i].trim().startsWith('|') && lines[i].trim().includes('|', 1)) {
                    tableLines.push(lines[i]);
                    i++;
                }
                let tableHtml = '<div class="md-table-wrapper"><table class="md-table">';
                tableLines.forEach((tl, idx) => {
                    if (/^\s*\|[\s|:-]+\|\s*$/.test(tl)) return; // separator row
                    const cells = tl.split('|').map(s => s.trim()).filter((_, ci, a) => ci > 0 && ci < a.length - 1);
                    const tag = idx === 0 ? 'th' : 'td';
                    tableHtml += '<tr>' + cells.map(c => `<${tag}>${inlineFormat(c)}</${tag}>`).join('') + '</tr>';
                });
                tableHtml += '</table></div>';
                out.push(tableHtml);
                continue;
            }

            // Blank line → paragraph break
            if (trimmed === '') {
                out.push('<br>');
                i++;
                continue;
            }

            // Plain text line with inline formatting
            out.push(inlineFormat(trimmed) + '<br>');
            i++;
        }

        return out.join('\n');
    }

    // ── DatePicker ──────────────────────────────────────────────────────────

    function renderDatePicker(container, visual, manifest) {
        const opts          = visual.options || {};
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param         = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const min           = opts['MIN'] || opts['min'] || '';
        const max           = opts['MAX'] || opts['max'] || '';

        let def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || '';
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper reldate-wrapper';

        const textInput = document.createElement('input');
        textInput.type = 'text';
        textInput.placeholder = 'YYYY-MM-DD or T-1...';
        textInput.value = def;
        if (param) textInput.setAttribute('data-parameter', param);

        const datePicker = document.createElement('input');
        datePicker.type = 'date';
        datePicker.className = 'reldate-native-picker';
        if (min) datePicker.min = min;
        if (max) datePicker.max = max;
        // Synchronize initial value and parameter name for tests/sync
        if (def && /^\d{4}-\d{2}-\d{2}$/.test(def)) datePicker.value = def;
        if (param) datePicker.setAttribute('data-parameter', param);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'reldate-btn';
        btn.title = 'Pick a date';
        btn.setAttribute('aria-label', 'Pick a date');
        btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
        btn.addEventListener('click', () => {
            if (typeof datePicker.showPicker === 'function') datePicker.showPicker();
            else datePicker.focus();
        });

        datePicker.addEventListener('change', () => {
            textInput.value = datePicker.value;
            textInput.dispatchEvent(new Event('change'));
        });

        const actions = document.createElement('div');
        actions.className = 'reldate-actions';
        const pickerSlot = document.createElement('span');
        pickerSlot.className = 'reldate-picker-slot';
        pickerSlot.appendChild(btn);
        pickerSlot.appendChild(datePicker);
        actions.appendChild(pickerSlot);

        wrapper.appendChild(textInput);
        wrapper.appendChild(actions);

        if (isWebMode && changeActions.length > 0) {
            textInput.addEventListener('change', () => {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = textInput.value; return o; }, {});
                postParameters(batch).then(m => { if (m) renderManifest(m); });
            });
        }
        container.appendChild(wrapper);
    }

    // ── RelDatePicker ────────────────────────────────────────────────────────

    function showRelDateHelpModal() {
        const modal = document.createElement('div');
        modal.className = 'required-params-modal'; // recycle the overlay styling
        modal.style.zIndex = '30000'; // above everything

        const content = document.createElement('div');
        content.className = 'modal-content';
        content.style.width = '550px';

        const title = document.createElement('h2');
        title.textContent = 'Relative Date Syntax';
        title.style.marginTop = '0';
        
        const desc = document.createElement('div');
        desc.style.fontSize = '14px';
        desc.style.lineHeight = '1.5';
        desc.innerHTML = `
            <p><code>RELDATE</code> parameters resolve to the exact local time at the moment of execution.</p>
            <table class="md-table" style="margin-top: 12px; margin-bottom: 16px;">
                <tr><th>Anchor</th><th>Resolves to</th></tr>
                <tr><td><strong>D</strong></td><td>Today at midnight</td></tr>
                <tr><td><strong>W</strong> / <strong>WS</strong></td><td>Start of current week</td></tr>
                <tr><td><strong>WE</strong></td><td>Last day of current week</td></tr>
                <tr><td><strong>M</strong> / <strong>MS</strong></td><td>1st of current month at midnight</td></tr>
                <tr><td><strong>ME</strong></td><td>Last day of current month at midnight</td></tr>
                <tr><td><strong>Y</strong> / <strong>YS</strong></td><td>Jan 1 of current year at midnight</td></tr>
                <tr><td><strong>YE</strong></td><td>Dec 31 of current year at midnight</td></tr>
                <tr><td><strong>N</strong></td><td>Exact current local datetime</td></tr>
            </table>
            <p><strong>Arithmetic:</strong> Append <code>-n</code> or <code>+n</code> to shift by <em>n</em> periods.</p>
            <ul>
                <li><code>D-1</code> = Yesterday</li>
                <li><code>M-1</code> = First day of last month</li>
                <li><code>ME-1</code> = Last day of last month</li>
            </ul>
            <p style="margin-top: 12px; margin-bottom: 8px;"><strong>Time Offsets (from N):</strong> Use <code>H</code> (hours), <code>I</code> (minutes), or <code>S</code> (seconds).</p>
            <ul style="margin-bottom: 0;">
                <li><code>N-2H</code> = Exactly 2 hours ago</li>
                <li><code>N-30I</code> = Exactly 30 minutes ago</li>
            </ul>
        `;

        const footer = document.createElement('div');
        footer.className = 'modal-footer';
        footer.style.marginTop = '24px';

        const closeBtn = document.createElement('button');
        closeBtn.className = 'header-btn primary';
        closeBtn.textContent = 'Got it';
        closeBtn.addEventListener('click', () => {
            document.body.removeChild(modal);
        });

        footer.appendChild(closeBtn);
        content.appendChild(title);
        content.appendChild(desc);
        content.appendChild(footer);
        modal.appendChild(content);
        document.body.appendChild(modal);
    }

    function renderRelDatePicker(container, visual, manifest) {
        const opts          = visual.options || {};
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param         = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const min           = opts['MIN'] || opts['min'] || '';
        const max           = opts['MAX'] || opts['max'] || '';

        let def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || '';
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        // ── Text input + calendar button row ──────────────────────────────
        const inputRow = document.createElement('div');
        inputRow.className = 'reldate-wrapper';

        const textInput = document.createElement('input');
        textInput.type = 'text';
        textInput.placeholder = 'D-7, M-1, Y-1 or YYYY-MM-DD';
        textInput.value = def;
        if (param) textInput.setAttribute('data-parameter', param);

        const hiddenDate = document.createElement('input');
        hiddenDate.type = 'date';
        hiddenDate.className = 'reldate-native-picker';
        if (min) hiddenDate.min = min;
        if (max) hiddenDate.max = max;
        // Synchronize initial value and parameter name for tests/sync
        if (def && /^\d{4}-\d{2}-\d{2}$/.test(def)) hiddenDate.value = def;
        if (param) hiddenDate.setAttribute('data-parameter', param);

        const calBtn = document.createElement('button');
        calBtn.type = 'button';
        calBtn.className = 'reldate-btn';
        calBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
        calBtn.title = 'Pick a date (writes ISO date)';
        calBtn.setAttribute('aria-label', 'Pick a date');
        calBtn.addEventListener('click', () => {
            if (typeof hiddenDate.showPicker === 'function') hiddenDate.showPicker();
            else hiddenDate.focus();
        });

        const infoBtn = document.createElement('button');
        infoBtn.type = 'button';
        infoBtn.className = 'reldate-btn';
        infoBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';
        infoBtn.title = 'View Relative Date Syntax Help';
        infoBtn.setAttribute('aria-label', 'View relative date syntax help');
        infoBtn.addEventListener('click', showRelDateHelpModal);

        hiddenDate.addEventListener('change', () => {
            textInput.value = hiddenDate.value;
            textInput.dispatchEvent(new Event('change'));
        });

        const actions = document.createElement('div');
        actions.className = 'reldate-actions';
        const pickerSlot = document.createElement('span');
        pickerSlot.className = 'reldate-picker-slot';
        pickerSlot.appendChild(calBtn);
        pickerSlot.appendChild(hiddenDate);
        actions.appendChild(pickerSlot);
        actions.appendChild(infoBtn);

        inputRow.appendChild(textInput);
        inputRow.appendChild(actions);

        // ── Quick-pick buttons ────────────────────────────────────────────
        const quickRow = document.createElement('div');
        quickRow.className = 'reldate-quick';

        const quickPicks = [
            { label: 'D',    value: 'D-0'  },
            { label: 'D-1',  value: 'D-1'  },
            { label: 'M',    value: 'M-0'  },
            { label: 'M-1',  value: 'M-1'  },
            { label: 'Y',    value: 'Y-0'  },
            { label: 'Y-1',  value: 'Y-1'  },
        ];

        quickPicks.forEach(qp => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'reldate-quick-btn' + (def === qp.value ? ' active' : '');
            btn.textContent = qp.label;
            btn.addEventListener('click', () => {
                textInput.value = qp.value;
                // update active state locally
                Array.from(quickRow.children).forEach(c => c.classList.remove('active'));
                btn.classList.add('active');
                textInput.dispatchEvent(new Event('change'));
            });
            quickRow.appendChild(btn);
        });

        wrapper.appendChild(inputRow);
        wrapper.appendChild(quickRow);

        if (isWebMode && changeActions.length > 0) {
            textInput.addEventListener('change', () => {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = textInput.value; return o; }, {});
                postParameters(batch).then(m => { if (m) renderManifest(m); });
            });
        }
        container.appendChild(wrapper);
    }

    // ── Slider ──────────────────────────────────────────────────────────────

    function renderSlider(container, visual, manifest) {
        const opts          = visual.options || {};
        // Parameter binding comes from ACTIONS (ON_CHANGE = SET_PARAMETER), not from OPTIONS
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param         = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const min           = opts['MIN']  || opts['min']  || '0';
        const max           = opts['MAX']  || opts['max']  || '100';
        const step          = opts['STEP'] || opts['step'] || '1';

        // Initial value: current parameter value > DEFAULT clause > min
        let def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || min;
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const input = document.createElement('input');
        input.type  = 'range';
        input.min   = min;
        input.max   = max;
        input.step  = step;
        input.value = def;
        if (param) input.setAttribute('data-parameter', param);

        const valueLabel = document.createElement('span');
        valueLabel.className   = 'range-value';
        valueLabel.textContent = def;

        input.addEventListener('input', () => { valueLabel.textContent = input.value; });

        if (isWebMode && changeActions.length > 0) {
            input.addEventListener('change', () => {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = input.value; return o; }, {});
                postParameters(batch).then(m => { if (m) renderManifest(m); });
            });
        }

        wrapper.appendChild(input);
        wrapper.appendChild(valueLabel);
        container.appendChild(wrapper);
    }

    // ── MultiSelect ─────────────────────────────────────────────────────────

    // ── MultiSelect ─────────────────────────────────────────────────────────

    function renderMultiSelect(container, visual, manifest) {
        const opts  = visual.options || {};
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param = changeActions.length > 0 ? changeActions[0].parameterName : null;

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper multiselect-wrapper';

        const list = document.createElement('div');
        list.className = 'checkbox-list';

        let currentValues = [];
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current) currentValues = String(current).split(',').map(v => v.trim());
        }

        (visual.rows || []).forEach(row => {
            const val = String(row[0] ?? '');
            const label = document.createElement('label');
            label.className = 'checkbox-item';
            
            const cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.value = val;
            cb.checked = currentValues.includes(val);
            cb.className = 'multiselect-cb';
            
            label.appendChild(cb);
            label.appendChild(document.createTextNode(' ' + val));
            list.appendChild(label);
        });

        wrapper.appendChild(list);

        if (isWebMode && param) {
            const applyBtn = document.createElement('button');
            applyBtn.className   = 'filter-apply';
            applyBtn.textContent = 'Apply';
            applyBtn.addEventListener('click', () => {
                const selected = Array.from(list.querySelectorAll('.multiselect-cb:checked')).map(cb => cb.value).join(',');
                changeActions.forEach(action => {
                    postParameters({ [action.parameterName]: selected })
                        .then(m => { if (m) renderManifest(m); });
                });
            });
            wrapper.appendChild(applyBtn);
        }

        container.appendChild(wrapper);
    }

    // ── Search ──────────────────────────────────────────────────────────────

    function renderSearch(container, visual, manifest) {
        const opts          = visual.options || {};
        // Parameter binding comes from ACTIONS (ON_CHANGE = SET_PARAMETER), not from OPTIONS
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param         = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const placeholder   = visual.placeholder || opts['PLACEHOLDER'] || opts['placeholder'] || 'Search…';

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const input       = document.createElement('input');
        input.type        = 'search';
        input.placeholder = placeholder;
        if (param) input.setAttribute('data-parameter', param);

        // Restore current value from manifest parameters
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current) input.value = current;
        }

        wrapper.appendChild(input);

        if (isWebMode && changeActions.length > 0) {
            let debounceTimer = null;
            input.addEventListener('input', () => {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(() => {
                    const batch = changeActions.reduce((o, a) => { o[a.parameterName] = input.value; return o; }, {});
                    postParameters(batch).then(m => { if (m) renderManifest(m); });
                }, 350);
            });
        }

        container.appendChild(wrapper);
    }

    // ── Checkbox ────────────────────────────────────────────────────────────

    function renderCheckbox(container, visual, manifest) {
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const labelPos = (visual.labelPosition || 'TOP').toUpperCase();

        let def = visual.defaultValue || 'FALSE';
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }
        const checked = isOn(def);

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper checkbox-wrapper pos-' + labelPos.toLowerCase();

        const input = document.createElement('input');
        input.type = 'checkbox';
        input.checked = checked;
        if (param) input.setAttribute('data-parameter', param);

        const label = document.createElement('label');
        label.textContent = visual.name;

        if (labelPos === 'TOP' || labelPos === 'LEFT') {
             wrapper.appendChild(label);
        }
        wrapper.appendChild(input);

        if (isWebMode && changeActions.length > 0) {
            input.addEventListener('change', () => {
                const val = input.checked ? 'TRUE' : 'FALSE';
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = val; return o; }, {});
                postParameters(batch).then(m => { if (m) renderManifest(m); });
            });
        }
        container.appendChild(wrapper);
    }

    // ── Textbox ─────────────────────────────────────────────────────────────

    function renderTextbox(container, visual, manifest) {
        const opts = visual.options || {};
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const labelPos = (visual.labelPosition || 'TOP').toUpperCase();
        const placeholder = visual.placeholder || opts['PLACEHOLDER'] || opts['placeholder'] || '';

        let def = visual.defaultValue || '';
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper textbox-wrapper pos-' + labelPos.toLowerCase();

        const input = document.createElement('input');
        input.type = 'text';
        input.value = def;
        input.placeholder = placeholder;
        if (param) input.setAttribute('data-parameter', param);

        const label = document.createElement('label');
        label.textContent = visual.name;

        if (labelPos === 'TOP' || labelPos === 'LEFT') {
             wrapper.appendChild(label);
        }
        wrapper.appendChild(input);

        if (isWebMode && changeActions.length > 0) {
            input.addEventListener('change', () => {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = input.value; return o; }, {});
                postParameters(batch).then(m => { if (m) renderManifest(m); });
            });
        }
        container.appendChild(wrapper);
    }

    // ── Numberbox ───────────────────────────────────────────────────────────

    function renderNumberbox(container, visual, manifest) {
        const opts = visual.options || {};
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const labelPos = (visual.labelPosition || 'TOP').toUpperCase();
        const min = visual.min;
        const max = visual.max;
        const decimals = visual.decimals || 0;
        const step = decimals > 0 ? Math.pow(10, -decimals).toFixed(decimals) : '1';

        let def = visual.defaultValue || '0';
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper numberbox-wrapper pos-' + labelPos.toLowerCase();

        const input = document.createElement('input');
        input.type = 'number';
        input.value = def;
        input.placeholder = visual.placeholder || opts['PLACEHOLDER'] || opts['placeholder'] || '';
        if (min !== undefined && min !== null) input.min = min;
        if (max !== null && max !== undefined) input.max = max;
        input.step = step;
        if (param) input.setAttribute('data-parameter', param);

        const label = document.createElement('label');
        label.textContent = visual.name;

        if (labelPos === 'TOP' || labelPos === 'LEFT') {
             wrapper.appendChild(label);
        }
        wrapper.appendChild(input);

        if (isWebMode && changeActions.length > 0) {
            input.addEventListener('change', () => {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = input.value; return o; }, {});
                postParameters(batch).then(m => { if (m) renderManifest(m); });
            });
        }
        container.appendChild(wrapper);
    }

    // ── Image ───────────────────────────────────────────────────────────────

    function renderImage(container, visual) {
        const opts = visual.options || {};
        const src  = opts['SRC'] || opts['src'] || '';
        const alt  = opts['ALT'] || opts['alt'] || '';
        const fit  = (opts['FIT'] || opts['fit'] || 'contain').toLowerCase();

        const wrapper = document.createElement('div');
        wrapper.style.width  = '100%';
        wrapper.style.height = '100%';
        wrapper.style.display = 'flex';
        wrapper.style.alignItems = 'center';
        wrapper.style.justifyContent = 'center';

        const img = document.createElement('img');
        img.src   = src;
        img.alt   = alt;
        img.style.maxWidth  = '100%';
        img.style.maxHeight = '100%';
        img.style.objectFit = fit;

        wrapper.appendChild(img);
        container.appendChild(wrapper);
    }

    // ── Button ──────────────────────────────────────────────────────────────

    function renderButton(container, btn) {
        const styles = btn.styles || {};
        const btnEl = document.createElement('button');
        btnEl.className = 'report-btn';
        btnEl.textContent = btn.title || btn.name;
        btnEl.setAttribute('data-name', btn.name);
        
        const tag = getOption(btn.options, 'TAG') || getStyle(styles, 'TAG');
        if (tag) btnEl.setAttribute('data-tag', tag);
        if (btn.tooltip && btn.tooltip.text) btnEl.title = btn.tooltip.text;

        // Apply inline styles from STYLE definition
        const bg   = getStyle(styles, 'BACKGROUND') || getStyle(styles, 'BACKGROUND-COLOR');
        const fg   = getStyle(styles, 'COLOR');
        const pad  = getStyle(styles, 'PADDING');
        const rad  = getStyle(styles, 'BORDER-RADIUS');
        const fw   = getStyle(styles, 'FONT-WEIGHT');
        const fs   = getStyle(styles, 'FONT-SIZE');
        const brd  = getStyle(styles, 'BORDER');
        const shd  = getStyle(styles, 'BOX-SHADOW');

        if (bg)   btnEl.style.background   = bg;
        if (fg)   btnEl.style.color        = fg;
        if (pad)  btnEl.style.padding      = pad;
        if (rad)  btnEl.style.borderRadius = rad;
        if (fw)   btnEl.style.fontWeight   = fw;
        if (fs)   btnEl.style.fontSize     = fs;
        if (brd)  btnEl.style.border       = brd;
        if (shd)  btnEl.style.boxShadow    = shd;

        btnEl.style.cursor      = 'pointer';
        if (!brd) btnEl.style.border = 'none';
        if (!fw)  btnEl.style.fontWeight = '600';

        // Mark RUN buttons so updateStagedUI can target them precisely
        if ((btn.actions || []).some(a => a.type === 'APPLY_PARAMETERS')) {
            btnEl.dataset.isRunBtn = 'true';
        }
        btnEl.addEventListener('click', () => {
            const clickActions = actionsFor(btn, 'ON_CLICK');
            if (clickActions.length === 0) return;

            clickActions.forEach(action => {
                executeAction(action, [], [], btn.name, btn);
            });
        });

        container.appendChild(btnEl);
    }


    // ── Footer ──────────────────────────────────────────────────────────────

    function renderFooter(container, manifest) {
        const footer = document.createElement('footer');
        const built  = manifest.builtAt ? new Date(manifest.builtAt).toLocaleString() : '';
        footer.innerHTML = '<small>Built: ' + escHtml(built) + '</small>';
        container.appendChild(footer);
    }

    function renderPipelineConsole(root, manifest) {
        if (window.__IS_PREVIEW__ || vscode) return;
        if (!manifest.messages?.length && !manifest.executionTree?.length && !manifest.error) return;

        const consoleWrapper = document.createElement('div');
        consoleWrapper.className = 'pipeline-console collapsed';
        
        const header = document.createElement('div');
        header.className = 'pipeline-header';
        
        let statusColor = 'gray';
        let statusText = 'Completed';
        if (manifest.error) {
            statusColor = 'red';
            statusText = 'Failed';
        }

        header.innerHTML = `
            <span>Pipeline Console</span>
            <span style="color: ${statusColor}; font-weight: normal;">
                ${statusText} 
                <span class="toggle-icon" style="margin-left: 8px;">&#x25B2;</span>
            </span>
        `;
        
        const body = document.createElement('div');
        body.className = 'pipeline-body';
        
        const leftPane = document.createElement('div');
        leftPane.className = 'pipeline-pane left-pane';
        leftPane.innerHTML = '<div class="pane-title">Execution Tree</div>';
        
        const rightPane = document.createElement('div');
        rightPane.className = 'pipeline-pane';
        rightPane.innerHTML = '<div class="pane-title">Messages</div>';
        
        body.appendChild(leftPane);
        body.appendChild(rightPane);
        
        consoleWrapper.appendChild(header);
        consoleWrapper.appendChild(body);
        
        let isCollapsed = true;
        header.addEventListener('click', () => {
            isCollapsed = !isCollapsed;
            consoleWrapper.classList.toggle('collapsed', isCollapsed);
            const icon = header.querySelector('.toggle-icon');
            icon.innerHTML = isCollapsed ? '&#x25B2;' : '&#x25BC;';
        });

        // Render Execution Tree
        if (manifest.executionTree) {
            const treeRoot = document.createElement('div');
            
            function renderNode(node, container) {
                const el = document.createElement('div');
                el.className = 'tree-node';
                
                const content = document.createElement('div');
                content.className = 'tree-node-content';
                
                const hasChildren = node.children && node.children.length > 0;
                const iconStr = hasChildren ? '&#x25BC;' : '&nbsp;';
                
                let timeStr = '';
                if (node.durationMs != null) timeStr = `[${node.durationMs}ms]`;
                
                let rowsStr = '';
                if (node.rowsProcessed != null) rowsStr = `(${node.rowsProcessed} rows)`;
                
                content.innerHTML = `
                    <span class="tree-icon" style="color:#888">${iconStr}</span>
                    <span class="node-name">${escHtml(node.name || 'Unnamed')}</span>
                    <span class="node-meta">
                        <span class="status-${node.status || 'Completed'}">${escHtml(node.status || 'Completed')}</span>
                        ${timeStr} ${rowsStr}
                    </span>
                `;
                
                el.appendChild(content);
                
                if (hasChildren) {
                    const childrenContainer = document.createElement('div');
                    childrenContainer.className = 'tree-children';
                    node.children.forEach(child => renderNode(child, childrenContainer));
                    el.appendChild(childrenContainer);
                    
                    content.querySelector('.tree-icon').addEventListener('click', (e) => {
                        e.stopPropagation();
                        const isHidden = childrenContainer.style.display === 'none';
                        childrenContainer.style.display = isHidden ? 'block' : 'none';
                        e.target.innerHTML = isHidden ? '&#x25BC;' : '&#x25B6;';
                    });
                }
                container.appendChild(el);
            }
            
            if (Array.isArray(manifest.executionTree)) {
                manifest.executionTree.forEach(rootNode => renderNode(rootNode, treeRoot));
            } else {
                renderNode(manifest.executionTree, treeRoot);
            }
            
            leftPane.appendChild(treeRoot);
        } else {
            leftPane.innerHTML += '<div class="no-data">No execution tree available.</div>';
        }

        // Render Messages
        if (manifest.messages && manifest.messages.length > 0) {
            manifest.messages.forEach(msg => {
                const entry = document.createElement('div');
                entry.className = 'log-entry';
                
                const time = new Date(msg.timestamp).toLocaleTimeString();
                const colorClass = msg.color ? `log-${msg.color}` : 'log-white';
                
                entry.innerHTML = `
                    <span class="log-time">[${time}]</span>
                    <span class="${colorClass}">${escHtml(msg.message)}</span>
                `;
                rightPane.appendChild(entry);
            });
        } else {
            rightPane.innerHTML += '<div class="no-data">No messages recorded.</div>';
        }

        if (manifest.error) {
            const errEntry = document.createElement('div');
            errEntry.className = 'log-entry log-red';
            errEntry.innerHTML = `<br/><b>Fatal Error:</b><br/><pre>${escHtml(manifest.error)}</pre>`;
            rightPane.appendChild(errEntry);
            
            // Auto-expand if there's an error
            isCollapsed = false;
            consoleWrapper.classList.remove('collapsed');
            header.querySelector('.toggle-icon').innerHTML = '&#x25BC;';
        }
        
        root.appendChild(consoleWrapper);
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    function actionsFor(visual, trigger) {
        return (visual.actions || []).filter(a => a.trigger === trigger);
    }

    function resolveActionValue(action, rowData, columns, controlValue) {
        const source = (action.valueSource || '').toUpperCase();
        if (source === 'CONTROL_VALUE') return controlValue ?? '';
        if (source === 'COLUMN') {
            const colIdx = columns.findIndex(
                c => c.toLowerCase() === (action.valueColumn || '').toLowerCase());
            return colIdx >= 0 ? rowData[colIdx] : '';
        }
        if (source === 'LITERAL') return action.literalValue ?? '';
        return action.literalValue ?? '';
    }

    function resolveActionParameters(action, rowData, columns) {
        const result = {};
        const columnParams = action.parameterColumns || {};
        const literalParams = action.literalParameters || {};

        Object.entries(columnParams).forEach(([name, column]) => {
            const colIdx = columns.findIndex(c => c.toLowerCase() === String(column).toLowerCase());
            result[name] = colIdx >= 0 ? String(rowData[colIdx] ?? '') : '';
        });

        Object.entries(literalParams).forEach(([name, value]) => {
            result[name] = String(value ?? '');
        });

        return result;
    }

    function navigateToPage(pageName) {
        if (!pageName) return;

        const navItem = document.querySelector(`[data-page="${CSS.escape(pageName)}"]`);
        if (navItem) {
            navItem.click();
            return;
        }

        const targetPage = document.getElementById('page-' + String(pageName).toLowerCase());
        if (!targetPage) return;

        document.querySelectorAll('.page').forEach(page => {
            page.style.display = page === targetPage ? 'block' : 'none';
        });

        document.querySelectorAll('[data-page]').forEach(item => item.classList.remove('active'));
        _lastActivePage = pageName;
        resizeChartsIn(targetPage);

        if (window.parent && window.parent !== window) {
            window.parent.postMessage({ type: 'etl-page-changed', page: pageName, userTriggered: true }, '*');
        }
    }

    function getActivePage() {
        return Array.from(document.querySelectorAll('.page'))
            .find(page => page.style.display !== 'none') || null;
    }

    function getActivePageName() {
        const page = getActivePage();
        return page ? (page.dataset.pageName || null) : null;
    }

    function isActivePagePaginated() {
        const page = getActivePage();
        return !!page && (page.dataset.pageMode || '').toUpperCase() === 'PAGINATED';
    }

    function executeAction(action, rowData, columns, visualName, visualCtx) {
        if (action.type === 'DRILL_IN') {
            const hierarchy = action.hierarchy || [];
            if (!hierarchy.length || !visualName) return;
            // Current level comes from the server-stamped drillState; fall back to hierarchy root.
            const curLevel = visualCtx?.drillState?.currentLevel || hierarchy[0];
            const colIdx   = columns.findIndex(c => c.toLowerCase() === curLevel.toLowerCase());
            const clicked  = colIdx >= 0 ? String(rowData?.[colIdx] ?? '') : '';
            if (!clicked) return;
            postDrillIn(visualName, clicked);
            return;
        }
        if (action.type === 'DRILL_DOWN') {
            const keyColumns = action.keyColumns || [];
            const params = {};
            for (const key of keyColumns) {
                const colIdx = columns.findIndex(c => c.toLowerCase() === key.toLowerCase());
                const value  = colIdx >= 0 ? rowData[colIdx] : null;
                if (value != null) params['@' + key] = String(value);
            }
            if (Object.keys(params).length === 0) return;

            // Push current parameter snapshot onto back-navigation stack
            _drillHistory.push(Object.assign({}, parameters));
            showDrillBackButton();
            
            // Visual feedback: pulse target or entire page if navigating
            const targetName = action.target || action.targetVisual || action.targetPage;
            if (targetName) {
                const targetEl = document.querySelector(`[data-visual-name="${CSS.escape(targetName)}"]`) 
                              || document.getElementById('page-' + targetName.toLowerCase());
                if (targetEl) {
                    targetEl.classList.add('drilled-down');
                    setTimeout(() => targetEl.classList.remove('drilled-down'), 1500);
                    
                    // If it's on the same page, scroll to it
                    targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });

                    // If it's a page, navigate to it
                    const navBtn = document.querySelector(`.nav-tab[data-page="${CSS.escape(targetName)}"]`);
                    if (navBtn) navBtn.click();
                }
            }

            if (vscode) {
                vscode.postMessage({ type: 'refreshReport', parameters: params });
            } else {
                postParameters(params).then(manifest => { if (manifest) renderManifest(manifest); });
            }

        } else if (action.type === 'SET_PARAMETER') {
            const value = resolveActionValue(action, rowData, columns);
            const params = { [action.parameterName]: String(value ?? '') };
            if (vscode) {
                vscode.postMessage({ type: 'refreshReport', parameters: params });
            } else {
                postParameters(params).then(manifest => { if (manifest) renderManifest(manifest); });
            }
        } else if (action.type === 'RUN_SCRIPT') {
            const scriptPath = action.scriptPath;
            const finalParams = resolveActionParameters(action, rowData, columns);

            if (isInteractive) {
                postRunScript(scriptPath, finalParams).then(res => {
                    if (res && res.message) alert(res.message);
                    if (res && res.refresh) {
                        fetchManifest().then(m => { if (m) renderManifest(m); });
                    }
                });
            } else {
                console.warn('RUN_SCRIPT is only supported in web mode.');
            }
        } else if (action.type === 'CLEAR_FILTERS') {
            // Reset all cross-filter states on all pages
            for (let k in _crossFilterStates) delete _crossFilterStates[k];
            document.querySelectorAll('.page').forEach(pageEl => {
                pageEl.querySelectorAll('.visual-card').forEach(card => {
                    card.classList.remove('cross-filter-source');
                });
            });
            // Reset parameters to baseline
            if (baselineManifest && baselineManifest.parameters) {
                const resetBatch = {};
                Object.keys(baselineManifest.parameters).forEach(k => {
                    resetBatch[k] = baselineManifest.parameters[k];
                });
                postParameters(resetBatch).then(m => { if (m) renderManifest(m); });
            } else {
                if (vscode) vscode.postMessage({ type: 'refreshReport', parameters: {} });
                else postParameters({}).then(m => { if (m) renderManifest(m); });
            }
        } else if (action.type === 'APPLY_PARAMETERS') {
            const batch = { ...pendingParameters };
            // Clear pending
            for (let k in pendingParameters) delete pendingParameters[k];
            updateStagedUI();
            
            // Flush to server
            _postParametersInternal(batch, false, getActivePageName()).then(m => { if (m) renderManifest(m); });
        } else if (action.type === 'BACK') {
            window.history.back();
        } else if (action.type === 'REFRESH') {
            if (isInteractive) {
                fetch(apiBase + '/manifest')
                    .then(r => r.json())
                    .then(m => renderManifest(m))
                    .catch(e => console.error('Refresh failed:', e));
            }
        } else if (action.type === 'REFRESH_VISUALS') {
            const targets = (action.targets || []).filter(Boolean);
            if (targets.length === 0) return;
            if (vscode) {
                vscode.postMessage({ type: 'refreshVisuals', visuals: targets });
            } else {
                postRefreshVisuals(targets).then(m => { if (m) renderManifest(m); });
            }
        } else if (action.type === 'EXPORT_CSV' || action.type === 'EXPORT_EXCEL') {
            const targetName = action.targetVisual || (visualCtx && visualCtx.options && visualCtx.options.TARGET);
            const visual = targetName ? findVisualData(targetName) : null;
            if (!visual) { console.warn('EXPORT action: no target visual found:', targetName); return; }
            if (action.type === 'EXPORT_CSV') exportCsv(visual);
            else exportExcelDownload(visual);
        } else if (action.type === 'EXPORT_PDF') {
            window.print();
        } else if (action.type === 'NAVIGATE_PAGE') {
            navigateToPage(action.targetPage);
        } else if (action.type === 'DRILL_REPORT') {
            const targetReport = resolveActionValue(action, rowData, columns) || action.targetReport;
            if (!targetReport) return;

            const finalParams = resolveActionParameters(action, rowData, columns);
            
            // Build query string
            const qs = Object.entries(finalParams)
                .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)
                .join('&');

            if (vscode) {
                vscode.postMessage({ 
                    type: 'drillReport', 
                    targetReport: targetReport, 
                    parameters: finalParams 
                });
            } else {
                // Determine target URL based on current environment
                let targetUrl = '';
                const reportName = targetReport.replace(/\.[^/.]+$/, "").replace(/^.*[\\\/]/, '');

                if (window.__API_BASE__) {
                    // Portal mode: navigate to sibling report
                    const parts = window.__API_BASE__.split('/'); // e.g. ["", "reports", "Summary", "api"]
                    if (parts.length >= 3) {
                        targetUrl = `/${parts[1]}/${encodeURIComponent(reportName)}`;
                    } else {
                        targetUrl = `/reports/${encodeURIComponent(reportName)}`;
                    }
                } else {
                    // Standalone mode: assume sibling file on same server
                    targetUrl = `/${encodeURIComponent(reportName)}`;
                }

                if (qs) targetUrl += (targetUrl.includes('?') ? '&' : '?') + qs;
                window.location.href = targetUrl;
            }
        } else if (action.type === 'SET_UI_STATE') {
            const targets = action.targets || [];
            const key = (action.key || '').toUpperCase();
            const value = action.value;

            // Resolve target elements
            const elements = [];
            targets.forEach(t => {
                if (t.startsWith('TAG:')) {
                    const tagName = t.substring(4);
                    document.querySelectorAll(`[data-tag="${tagName}"]`).forEach(el => elements.push(el));
                } else {
                    const el = document.getElementById(t) || document.querySelector(`[data-name="${t}"]`);
                    if (el) elements.push(el);
                }
            });

            elements.forEach(el => {
                if (key === 'VISIBLE') {
                    const isVisible = isOn(value);
                    el.style.display = isVisible ? '' : 'none';
                } else if (key === 'COLLAPSED') {
                    const isCollapsed = isOn(value);
                    const container = el.closest('.collapsible-drawer') || el.closest('.collapsible-inline') || el.closest('.report-container') || el;
                    
                    const name = container.getAttribute('data-name');
                    if (name) _uiStates[name] = { collapsed: isCollapsed };

                    if (isCollapsed) container.classList.add('collapsed');
                    else container.classList.remove('collapsed');

                    // Update chevrons for inline collapsible
                    if (container.classList.contains('collapsible-inline')) {
                        const chevron = container.querySelector('.container-chevron');
                        if (chevron) chevron.innerHTML = isCollapsed ? '&#x25BC;' : '&#x25B2;';
                    }
                    
                    // Specific logic for drawers
                    if (container.classList.contains('collapsible-drawer')) {
                        if (isCollapsed) container.classList.remove('open');
                        else container.classList.add('open');
                    }

                    // Trigger resize to handle grid reflow
                    setTimeout(() => {
                        const pageGrid = document.querySelector('.page-grid');
                        if (pageGrid) resizeChartsIn(pageGrid);
                    }, 350);
                } else if (key === 'BACKGROUND-COLOR') {
                    el.style.backgroundColor = value;
                } else if (key === 'COLOR') {
                    el.style.color = value;
                } else if (key === 'CLASS') {
                    if (value.startsWith('+')) el.classList.add(value.substring(1));
                    else if (value.startsWith('-')) el.classList.remove(value.substring(1));
                    else el.className = value;
                }
            });
        }
    }

    function postDrillIn(visualName, clickedValue) {
        if (_drillInFlight) return;
        _drillInFlight = true;
        if (vscode) {
            _drillInFlight = false;
            vscode.postMessage({ type: 'drillIn', visualName, clickedValue });
            return;
        }
        fetch(apiBase + '/drill', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ visualName, direction: 'IN', clickedValue })
        }).then(r => r.ok ? r.json() : null).then(m => {
            _drillInFlight = false;
            if (m) renderManifest(m);
        }).catch(() => { _drillInFlight = false; });
    }

    function postDrillUp(visualName, targetDepth) {
        if (vscode) {
            vscode.postMessage({ type: 'drillUp', visualName, targetDepth });
            return;
        }
        fetch(apiBase + '/drill', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ visualName, direction: 'UP', targetDepth })
        }).then(r => r.ok ? r.json() : null).then(m => { if (m) renderManifest(m); });
    }

    async function postParameter(name, value) {
        if (vscode) {
            vscode.postMessage({ type: 'refreshReport', parameters: { [name]: value } });
            return null;
        }
        try {
            const res = await fetch(apiBase + '/parameter', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ name, value })
            });
            if (!res.ok) return null;
            return await res.json();
        } catch {
            return null;
        }
    }

    // Batch-update multiple parameters in a single server round-trip.
    // Batch-update multiple parameters in a single server round-trip.
    // Batch-update multiple parameters in a single server round-trip.
    async function postParameters(params, isInteraction = false, stage = null) {
        const shouldStage = stage === null
            ? (!isInteraction && isActivePagePaginated())
            : !!stage;
        if (shouldStage && !isInteraction) {
            // Paginated pages stage prompt changes until APPLY_PARAMETERS.
            // Dashboard pages post immediately.
            Object.assign(pendingParameters, params);
            updateStagedUI();
            return Promise.resolve(null);
        }
        return _postParametersInternal(params, isInteraction, getActivePageName());
    }

    async function _postParametersInternal(params, isInteraction = false, pageName = null) {
        // Convert dictionary to required List<ParameterUpdateRequest> format
        const paramList = Object.entries(params).map(([name, value]) => ({
            name: name,
            value: String(value ?? '')
        }));

        console.debug('[ParameterUpdate] Sending:', { params: paramList, isInteraction });

        if (vscode) {
            vscode.postMessage({ 
                type: 'refreshReport', 
                parameters: params, // VS Code extension handles the dictionary
                isInteraction: isInteraction,
                pageName: pageName
            });
            return null;
        }

        try {
            const res = await fetch(apiBase + '/parameters', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ 
                    params: paramList,
                    isInteraction: isInteraction,
                    pageName: pageName
                })
            });
            if (!res.ok) {
                console.error('Parameter update failed:', res.status, await res.text());
                return null;
            }
            const manifest = await res.json();
            console.debug('[ParameterUpdate] Received new manifest');
            return manifest;
        } catch (e) {
            console.error('Parameter update request failed:', e);
            return null;
        }
    }

    async function postRunScript(scriptPath, parameters) {
        try {
            const res = await fetch(apiBase + '/run-script', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ scriptPath, parameters })
            });
            if (!res.ok) return { message: `Server error: ${res.status}` };
            return await res.json();
        } catch (e) {
            return { message: `Request failed: ${e.message}` };
        }
    }

    async function postRefreshVisuals(visuals) {
        try {
            const res = await fetch(apiBase + '/refresh-visuals', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ visuals })
            });
            if (!res.ok) {
                console.error('Visual refresh failed:', res.status, await res.text());
                return null;
            }
            return await res.json();
        } catch (e) {
            console.error('Visual refresh request failed:', e);
            return null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────


    function errorEl(detail) {
        const el = document.createElement('details');
        el.className = 'error-card';
        const summary = document.createElement('summary');
        summary.textContent = 'Error loading data';
        el.appendChild(summary);
        if (detail) {
            const pre = document.createElement('pre');
            pre.textContent = detail;
            el.appendChild(pre);
        }
        return el;
    }

    function escHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function parseHexColor(hex) {
        const h = hex.replace('#', '');
        const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
        return [parseInt(full.slice(0, 2), 16), parseInt(full.slice(2, 4), 16), parseInt(full.slice(4, 6), 16)];
    }

    function interpolateColor(fromHex, toHex, t) {
        const [r1, g1, b1] = parseHexColor(fromHex);
        const [r2, g2, b2] = parseHexColor(toHex);
        const r = Math.round(r1 + (r2 - r1) * t);
        const g = Math.round(g1 + (g2 - g1) * t);
        const b = Math.round(b1 + (b2 - b1) * t);
        return `rgb(${r},${g},${b})`;
    }

    function buildSparklineSvg(valuesJson, type, color) {
        let vals;
        try { vals = JSON.parse(valuesJson); } catch { return ''; }
        vals = vals.map(v => (v === null ? null : parseFloat(v))).filter(v => v !== null && !isNaN(v));
        if (vals.length < 2) return '';
        const W = 60, H = 20, PAD = 2;
        const mn = Math.min(...vals), mx = Math.max(...vals);
        const range = mx - mn || 1;
        const c = color || '#4472C4';
        const pts = vals.map((v, i) => {
            const x = PAD + (i / (vals.length - 1)) * (W - PAD * 2);
            const y = H - PAD - ((v - mn) / range) * (H - PAD * 2);
            return [x.toFixed(1), y.toFixed(1)];
        });
        if (type === 'bar') {
            const bw = Math.max(2, (W - PAD * 2) / vals.length - 1);
            const bars = pts.map(([x, y]) =>
                `<rect x="${(parseFloat(x) - bw / 2).toFixed(1)}" y="${y}" width="${bw.toFixed(1)}" height="${(H - PAD - parseFloat(y)).toFixed(1)}" fill="${c}"/>`
            ).join('');
            return `<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">${bars}</svg>`;
        }
        const ptStr = pts.map(p => p.join(',')).join(' ');
        if (type === 'area') {
            const [x0] = pts[0], [xn] = pts[pts.length - 1];
            const area = `<polygon points="${ptStr} ${xn},${H - PAD} ${x0},${H - PAD}" fill="${c}" fill-opacity="0.2" stroke="none"/>`;
            const line = `<polyline points="${ptStr}" fill="none" stroke="${c}" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round"/>`;
            return `<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg">${area}${line}</svg>`;
        }
        return `<svg width="${W}" height="${H}" xmlns="http://www.w3.org/2000/svg"><polyline points="${ptStr}" fill="none" stroke="${c}" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round"/></svg>`;
    }

    function formatValue(value, format) {
        if (value == null || value === '' || !format) return value;
        const num = parseFloat(value);
        if (isNaN(num)) return value;

        const type = format.charAt(0).toUpperCase();
        const prec = parseInt(format.substring(1));
        const precision = isNaN(prec) ? undefined : prec;

        try {
            switch (type) {
                case 'C':
                    return new Intl.NumberFormat('en-US', {
                        style: 'currency', currency: 'USD',
                        minimumFractionDigits: precision, maximumFractionDigits: precision
                    }).format(num);
                case 'N':
                    return new Intl.NumberFormat('en-US', {
                        minimumFractionDigits: precision, maximumFractionDigits: precision
                    }).format(num);
                case 'P':
                    // If the value is > 1.0, it might be already in percent (e.g. 85 instead of 0.85)
                    // But standard C# P format for 0.85 is 85%.
                    // We'll follow C# behavior: num * 100.
                    return new Intl.NumberFormat('en-US', {
                        style: 'percent',
                        minimumFractionDigits: precision, maximumFractionDigits: precision
                    }).format(num);
                default:
                    return value;
            }
        } catch (e) {
            return value;
        }
    }

    function updateStagedUI() {
        const hasPending = Object.keys(pendingParameters).length > 0;

        // 1. Update only RUN buttons (tagged with data-is-run-btn during renderButton)
        document.querySelectorAll('[data-is-run-btn]').forEach(btn => {
            if (hasPending) {
                btn.classList.add('pending-changes');
            } else {
                btn.classList.remove('pending-changes');
            }
        });

        // 2. Add/Update a "Pending" badge in the header if it exists
        const header = document.querySelector('.report-header');
        if (header) {
            let badge = header.querySelector('.pending-badge');
            if (hasPending) {
                if (!badge) {
                    badge = document.createElement('div');
                    badge.className = 'pending-badge';
                    badge.innerHTML = '&#x26A0; Changes Pending';
                    badge.style.background = '#fff3cd';
                    badge.style.color = '#856404';
                    badge.style.padding = '4px 8px';
                    badge.style.borderRadius = '4px';
                    badge.style.fontSize = '0.8em';
                    badge.style.fontWeight = 'bold';
                    header.appendChild(badge);
                }
            } else if (badge) {
                badge.remove();
            }
        }
    }

    // Boot on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }

    // VS Code message listener
    if (vscode) {
        window.addEventListener('message', event => {
            const message = event.data;
            if (message.type === 'reportManifest') {
                renderManifest(message);
            }
        });
    }

    // Test escape hatch: exposes pure functions for automated testing.
    // Harmless in production (just sets a window property that nothing reads).
    if (typeof window !== 'undefined') {
        window.__reportRuntime__ = { isOn, renderCard, renderDatePicker, renderSlider, renderSearch, renderButton, renderChart, abbreviateNumber };
    }
})();
