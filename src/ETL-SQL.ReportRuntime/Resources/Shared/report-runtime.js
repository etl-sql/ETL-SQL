/**
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
    const pendingParameters = {}; // Phase 2: Staged Mode
    let isStagedMode = false;     // Phase 2: Staged Mode
    let _refreshTimers = [];
    let _lastActivePage = null;
    let _drillInFlight = false;
    const _drillHistory = [];
    const _registeredMaps = new Set();
    const _crossFilterStates = {}; // Keyed by page element ID; persists across renderManifest re-builds

    /**
     * Entry point: obtain manifest and render all visuals + pages.
     */
    async function boot() {
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
                const res = await fetch(apiBase + '/manifest');
                manifest  = await res.json();
            } catch (e) {
                document.getElementById('root').innerHTML =
                    '<p class="error">Failed to load manifest: ' + e.message + '</p>';
                return;
            }
        } else {
            document.getElementById('root').innerHTML =
                '<p class="error">No manifest available.</p>';
            return;
        }

        // Phase 4: Intercept execution if required parameters are missing
        if (!checkRequiredParameters(manifest)) {
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
        // Phase 2: Detect Staged Mode (presence of APPLY_PARAMETERS action)
        isStagedMode = false;
        (manifest.buttons || []).forEach(b => {
            if ((b.actions || []).some(a => a.type === 'APPLY_PARAMETERS')) isStagedMode = true;
        });
        (manifest.visuals || []).forEach(v => {
            if ((v.actions || []).some(a => a.type === 'APPLY_PARAMETERS')) isStagedMode = true;
        });
        if (!isStagedMode) {
             // Clear pending if we exited staged mode (unlikely but safe)
             for (let k in pendingParameters) delete pendingParameters[k];
        }

        // Cancel any running per-page auto-refresh timers before rebuilding.
        _refreshTimers.forEach(id => clearInterval(id));
        _refreshTimers = [];

        const root = document.getElementById('root');
        if (!root) return;
        root.innerHTML = ''; // Clear for full rebuild
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
                if (target) {
                    target.style.display = 'block';
                    resizeChartsIn(target);
                }

                // Update active class
                nav.querySelectorAll('.' + itemClass).forEach(e => e.classList.remove('active'));
                el.classList.add('active');
                _lastActivePage = pageName;

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

    function renderPage(manifest, page, pageSections, pageTheme) {
        console.debug(`[Layout] Rendering Page: ${page.name}`);
        const div = document.createElement('div');
        div.className = 'page';
        if (page.name) div.id = 'page-' + page.name.toLowerCase();

        const content = document.createElement('div');
        content.className = 'page-grid';
        div.appendChild(content);

        pageSections[page.name] = div;
        renderLayout(content, page, manifest, pageTheme);
        return div;
    }

    function renderContainer(container, containerDef, manifest, pageTheme) {
        const div = document.createElement('div');
        const isScroll = (containerDef.containerType || '').toUpperCase() === 'SCROLL';
        div.className = isScroll ? 'container-scroll' : 'container-box';
        div.setAttribute('data-name', containerDef.name);
        
        const tag = getOption(containerDef.options, 'TAG') || getStyle(containerDef.styles, 'TAG');
        if (tag) div.setAttribute('data-tag', tag);
        const styles = containerDef.styles || {};
        const containerTheme = getStyle(styles, 'THEME') || pageTheme;

        if (isScroll) {
            const height = getStyle(styles, 'HEIGHT') || '400px';
            div.style.maxHeight = height;
        }

        renderLayout(div, containerDef, manifest, containerTheme);
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
                        if (nested.isCollapsible) {
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
                        if (nested.isCollapsible) {
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

        if (width)   card.style.width  = width;
        if (height)  card.style.height = height;
        if (tooltip) card.title        = tooltip;

        const title = document.createElement('h3');
        title.textContent = visual.name;
        
        // Hide redundant header if chart/card has its own title/label
        const specificTitle = getOption(visual.options, 'TITLE') || getOption(visual.options, 'mapping:label');
        if (specificTitle) title.style.display = 'none';

        card.appendChild(title);

        if (visual.error) {
            card.appendChild(errorEl(visual.error));
            container.appendChild(card);
            return;
        }

        const type = (visual.visualType || '').toUpperCase();

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

        // Deferred visuals (VISIBLE = OFF) show a placeholder until the user clicks Run
        if (visual.isHidden) {
            card.classList.add('deferred-visual');
            const ph = document.createElement('div');
            ph.className = 'deferred-placeholder';
            ph.textContent = 'Configure parameters above and click Run to load data.';
            card.appendChild(ph);
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

    // ── Chart (ECharts — BAR / LINE / HBAR / SCATTER / PIE / DONUT / BOXPLOT / TREEMAP / HEATMAP / GAUGE / FUNNEL / WATERFALL / BUBBLE / RADAR / CANDLESTICK / MAP) ──

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
                if (parent) parent.appendChild(noDataEl('Map load failed: ' + err.message));
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
        const crossFilter  = isOn((visual.options || {})['CROSS_FILTER']) 
                           || !!(visual.options || {})['CROSS_VISUAL_ACTION'];
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
                } else if (cfState && cfState.selections.length > 0 && !visual.highlightRows) {
                    // Cross-filter active but server returned no highlight (dimension mismatch):
                    // ghost all bars to show the filter is active on this visual too.
                    applySourceChartOpacity(option, []);
                }
            }

            chart.setOption(option);
            wrapper._echartsInst = chart;

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

                    // CROSS_FILTER handling
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

        if (drillDowns.length > 0) {
            const sep = document.createElement('div');
            sep.className = 'ctx-sep';
            menu.appendChild(sep);
        }

        const exportItem = document.createElement('div');
        exportItem.className = 'ctx-item';
        exportItem.innerHTML = `<span>&#x2913;</span> Export to CSV`;
        exportItem.addEventListener('click', () => { exportCsv(visual); hideCtxMenu(); });
        menu.appendChild(exportItem);

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

        const clickActions  = actionsFor(visual, 'ON_CLICK');
        const isClickable   = clickActions.length > 0;
        const crossFilter   = isOn((visual.options || {})['CROSS_FILTER']);

        // Register as a cross-filter target so applyPageCrossFilter can find it
        if (crossFilter) {
            container.setAttribute('data-cross-filter', '1');
            container._visualData = visual;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'table-wrapper' + (isClickable ? ' clickable' : '');
        
        let heightOpt = visual.styles ? (visual.styles['HEIGHT'] || visual.styles['height']) : null;
        if (heightOpt) { wrapper.style.maxHeight = heightOpt; }

        const table = document.createElement('table');
        const thead = document.createElement('thead');
        const headerRow = document.createElement('tr');
        visual.columns.forEach(col => {
            const th = document.createElement('th');
            th.textContent = col;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        (visual.rows || []).forEach((row, rowIndex) => {
            const tr = document.createElement('tr');
            if (isClickable) tr.style.cursor = 'pointer';
            const rowColor = Array.isArray(visual.rowStyles) ? visual.rowStyles[rowIndex] : null;
            if (rowColor) { tr.style.color = rowColor; }

            visual.columns.forEach((col, ci) => {
                const td = document.createElement('td');
                const cellVal = row[ci] != null ? String(row[ci]) : '';
                const format  = (visual.options || {})['FORMAT'];
                td.textContent = formatValue(cellVal, format);
                tr.appendChild(td);

            });
            if (isClickable || crossFilter) {
                tr.addEventListener('click', (e) => {
                    if (crossFilter) {
                        const xMappingCol = (visual.options || {})['mapping:x'] || (visual.columns && visual.columns[0]);
                        const xIdx = xMappingCol ? (visual.columns || []).findIndex(c => c.toLowerCase() === xMappingCol.toLowerCase()) : 0;
                        const clickedValue = row[xIdx];
                        applyPageCrossFilter(container, String(clickedValue), xMappingCol, visual.name, e);
                    } else {
                        clickActions.forEach(action => executeAction(action, row, visual.columns, visual.name, visual));
                    }
                });
            }
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        wrapper.appendChild(table);

        // Right-click → Drill Down & Export
        wrapper.addEventListener('contextmenu', e => {
            e.preventDefault();
            const tr = e.target.closest('tr');
            const idx = tr ? Array.from(tbody.rows).indexOf(tr) : -1;
            const rowData = idx >= 0 ? (visual.rows || [])[idx] : null;
            showCtxMenu(e.clientX, e.clientY, visual, rowData);
        });

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

    // ── Card ────────────────────────────────────────────────────────────────

    function renderCard(container, visual) {
        const opts = visual.options || {};
        // Title comes from the TITLE option (stored as lowercase "title" in manifest)
        const cardTitle    = getOption(opts, 'title') || visual.name;
        const cardSubtitle = getOption(opts, 'subtitle') || '';

        const valueColName = getOption(opts, 'mapping:value');
        const valIdx = valueColName
            ? (visual.columns || []).findIndex(c => c.toLowerCase() === valueColName.toLowerCase())
            : 0;

        const row = visual.rows && visual.rows[0] ? visual.rows[0] : null;
        let displayValue = row ? (row[valIdx >= 0 ? valIdx : 0] ?? '0') : 'No data';

        const formatOpt = getOption(opts, 'format');
        if (formatOpt && displayValue !== 'No data') {
            displayValue = formatValue(displayValue, formatOpt);
        }

        const cardEl = document.createElement('div');
        cardEl.className = 'card-value';
        cardEl.innerHTML =
            `<div class="card-label">${escHtml(cardTitle)}</div>` +
            (cardSubtitle ? `<div class="card-subtitle">${escHtml(cardSubtitle)}</div>` : '') +
            `<div class="card-number">${escHtml(String(displayValue))}</div>`;
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
        if (min) datePicker.min = min;
        if (max) datePicker.max = max;
        // Synchronize initial value and parameter name for tests/sync
        if (def && /^\d{4}-\d{2}-\d{2}$/.test(def)) datePicker.value = def;
        if (param) datePicker.setAttribute('data-parameter', param);
        datePicker.style.cssText = 'position:absolute; opacity:0; width:0; height:0; pointer-events:none;';

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'reldate-btn';
        btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
        btn.addEventListener('click', () => {
            if (typeof datePicker.showPicker === 'function') datePicker.showPicker();
            else datePicker.focus();
        });

        datePicker.addEventListener('change', () => {
            textInput.value = datePicker.value;
            textInput.dispatchEvent(new Event('change'));
        });

        wrapper.appendChild(textInput);
        wrapper.appendChild(datePicker);
        wrapper.appendChild(btn);

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
        if (min) hiddenDate.min = min;
        if (max) hiddenDate.max = max;
        // Synchronize initial value and parameter name for tests/sync
        if (def && /^\d{4}-\d{2}-\d{2}$/.test(def)) hiddenDate.value = def;
        if (param) hiddenDate.setAttribute('data-parameter', param);
        hiddenDate.style.cssText = 'position:absolute; opacity:0; width:0; height:0; pointer-events:none;';

        const calBtn = document.createElement('button');
        calBtn.type = 'button';
        calBtn.className = 'reldate-btn';
        calBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
        calBtn.title = 'Pick a date (writes ISO date)';
        calBtn.addEventListener('click', () => {
            if (typeof hiddenDate.showPicker === 'function') hiddenDate.showPicker();
            else hiddenDate.focus();
        });

        calBtn.style.borderTopRightRadius = '0';
        calBtn.style.borderBottomRightRadius = '0';

        const infoBtn = document.createElement('button');
        infoBtn.type = 'button';
        infoBtn.className = 'reldate-btn';
        infoBtn.style.borderLeft = 'none';
        infoBtn.style.color = '#5470c6';
        infoBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';
        infoBtn.title = 'View Relative Date Syntax Help';
        infoBtn.addEventListener('click', showRelDateHelpModal);

        hiddenDate.addEventListener('change', () => {
            textInput.value = hiddenDate.value;
            textInput.dispatchEvent(new Event('change'));
        });

        inputRow.appendChild(textInput);
        inputRow.appendChild(hiddenDate);
        inputRow.appendChild(calBtn);
        inputRow.appendChild(infoBtn);

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

        const type = (btn.buttonType || '').toUpperCase();
        // Mark RUN buttons so updateStagedUI can target them precisely
        if ((btn.actions || []).some(a => a.type === 'APPLY_PARAMETERS')) {
            btnEl.dataset.isRunBtn = 'true';
        }
        btnEl.addEventListener('click', () => {
            if (type === 'BACK') {
                window.history.back();
            } else if (type === 'EXPORT_CSV' || type === 'EXPORT_EXCEL') {
                const targetName = (btn.options || {})['TARGET'];
                const visual = targetName ? findVisualData(targetName) : null;
                if (!visual) { console.warn('EXPORT button: no TARGET visual found:', targetName); return; }
                if (type === 'EXPORT_CSV') exportCsv(visual);
                else exportExcel(visual);
            } else if (type === 'REFRESH') {
                if (isInteractive) {
                    fetch(apiBase + '/manifest')
                        .then(r => r.json())
                        .then(m => renderManifest(m))
                        .catch(e => console.error('Refresh failed:', e));
                }
            } else if (type === 'CLEAR_FILTERS') {
                if (vscode) {
                    vscode.postMessage({ type: 'refreshReport', parameters: {} });
                } else if (isWebMode) {
                    postParameters({}).then(m => { if (m) renderManifest(m); });
                }
            } else {
                // Custom button — execute ON_CLICK actions
                const clickActions = actionsFor(btn, 'ON_CLICK');
                
                // Special check for CLEAR_FILTERS / APPLY_PARAMETERS actions
                if (clickActions.some(a => a.type === 'CLEAR_FILTERS')) {
                    executeAction({ type: 'CLEAR_FILTERS' }, null, []);
                    return;
                }
                if (clickActions.some(a => a.type === 'APPLY_PARAMETERS')) {
                    executeAction({ type: 'APPLY_PARAMETERS' }, null, []);
                    return;
                }

                if (clickActions.length === 0) return;

                const setParamActions  = clickActions.filter(a => a.type === 'SET_PARAMETER');
                const drillDownActions = clickActions.filter(a => a.type === 'DRILL_DOWN');

                // Try page navigation first (DRILL_DOWN on a button = navigate to the target page)
                if (drillDownActions.length > 0 && !isWebMode) {
                    drillDownActions.forEach(a => {
                        const navItem = document.querySelector(`[data-page="${a.targetVisual}"]`);
                        if (navItem) navItem.click();
                    });
                    return;
                }

                // In web mode, batch SET_PARAMETER and DRILL_DOWN parameter updates
                const batch = {};
                drillDownActions.forEach(a => {
                    (a.keyColumns || []).forEach(k => { batch['@' + k] = ''; });
                });
                setParamActions.forEach(a => {
                    if (a.parameterName) batch[a.parameterName] = resolveActionValue(a, [], []);
                });

                if (Object.keys(batch).length > 0 && isWebMode) {
                    postParameters(batch).then(m => {
                        if (m) {
                            renderManifest(m);
                            // After re-render, navigate to DRILL_DOWN target page if specified
                            drillDownActions.forEach(a => {
                                if (a.targetVisual) {
                                    const navItem = document.querySelector(`[data-page="${a.targetVisual}"]`);
                                    if (navItem) navItem.click();
                                }
                            });
                        }
                    });
                } else if (drillDownActions.length > 0) {
                    // Navigate without parameter change
                    drillDownActions.forEach(a => {
                        if (a.targetVisual) {
                            const navItem = document.querySelector(`[data-page="${a.targetVisual}"]`);
                            if (navItem) navItem.click();
                        }
                    });
                }
            }
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
            _postParametersInternal(batch).then(m => { if (m) renderManifest(m); });
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
                    const container = el.closest('.collapsible-drawer') || el.closest('.report-container') || el;
                    if (isCollapsed) container.classList.add('collapsed');
                    else container.classList.remove('collapsed');
                    
                    // Specific logic for drawers
                    if (container.classList.contains('collapsible-drawer')) {
                        if (isCollapsed) container.classList.remove('open');
                        else container.classList.add('open');
                    }
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
    async function postParameters(params, isInteraction = false) {
        if (isStagedMode && !isInteraction) {
            // Stage the change
            Object.assign(pendingParameters, params);
            updateStagedUI();
            return Promise.resolve(null);
        }
        return _postParametersInternal(params, isInteraction);
    }

    async function _postParametersInternal(params, isInteraction = false) {
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
                isInteraction: isInteraction 
            });
            return null;
        }

        try {
            const res = await fetch(apiBase + '/parameters', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ 
                    params: paramList,
                    isInteraction: isInteraction
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
        window.__reportRuntime__ = { isOn, renderCard, renderDatePicker, renderSlider, renderSearch, renderButton, renderChart };
    }
})();
