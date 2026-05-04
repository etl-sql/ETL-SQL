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
    
    function getOption(options, key) {
        if (!options) return null;
        const lookup = key.toLowerCase();
        for (let k in options) {
            if (k.toLowerCase() === lookup) return options[k];
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

    // In multi-report mode the server injects window.__API_BASE__ = '/reports/{name}/api'.
    // Single-report and VS Code modes default to '/api'.
    const apiBase = (window.__API_BASE__ || '/api').replace(/\/$/, '');

    // Current report parameters (for interactive controls)
    const parameters = {};
    let _refreshTimers = [];
    const _registeredMaps = new Set();

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
        renderManifest(manifest);
    }

    function renderManifest(manifest) {
        // Cancel any running per-page auto-refresh timers before rebuilding.
        _refreshTimers.forEach(id => clearInterval(id));
        _refreshTimers = [];

        const root = document.getElementById('root');
        if (!root) return;
        root.innerHTML = ''; // Clear for full rebuild

        // Register custom themes before any echarts.init() calls
        if (typeof echarts !== 'undefined' && manifest.customThemes) {
            manifest.customThemes.forEach(t => {
                if (t.name && t.config) echarts.registerTheme(t.name, t.config);
            });
        }

        // Update local parameters from manifest
        if (manifest.parameters) {
            Object.keys(manifest.parameters).forEach(k => {
                parameters[k] = manifest.parameters[k];
            });
            syncParameters(manifest.parameters);
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
                const pageStyles = page.styles || {};
                const pageTheme = pageStyles['THEME'] || pageStyles['theme'] || null;
                const section = renderPage(manifest, page, pageSections, pageTheme);
                root.appendChild(section);

                // Hidden pages start invisible; DRILL_DOWN can show them programmatically.
                if (page.isHidden || (navDef && page.name !== defaultPageName)) {
                    section.style.display = 'none';
                }
            });

            if (navDef) {
                renderNavBar(root, navDef, pageSections, manifest.pages);
            }
        } else {
            (manifest.visuals || []).forEach(v => renderVisual(root, v, null));
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

            const isDefault = pageName === defaultPageName;
            if (isDefault) el.classList.add('active');

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
            });

            nav.appendChild(el);
        });

        // Show default page, hide others; resize charts that initialised hidden
        pages.forEach(p => {
            const s = pageSections[p.name];
            if (!s) return;
            if (p.name === defaultPageName) {
                s.style.display = 'block';
                resizeChartsIn(s);
            } else {
                s.style.display = 'none';
            }
        });
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
                        if (t.value !== val) t.value = val;
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
        const div = document.createElement('div');
        div.className = 'page';
        if (page.name) div.id = 'page-' + page.name.toLowerCase();

        const heading = document.createElement('h2');
        heading.textContent = page.name;
        div.appendChild(heading);

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

        if (isScroll) {
            const styles = containerDef.styles || {};
            const height = styles['HEIGHT'] || styles['height'] || '400px';
            div.style.maxHeight = height;
        }

        renderLayout(div, containerDef, manifest, pageTheme);
        container.appendChild(div);
    }

    function renderLayout(container, layoutDef, manifest, pageTheme) {
        if (layoutDef.structure) {
            container.style.display = 'grid';
            // CSS grid-template-areas needs each row quoted: "A A" "B C"
            const rows = layoutDef.structure.split('/')
                .map(r => r.trim().split(/\s+/).filter(s => s).join(' '))
                .filter(r => r.length > 0);
            
            container.style.gridTemplateAreas = rows.map(r => `"${r}"`).join(' ');

            if (rows.length > 0) {
                const cols = rows[0].split(/\s+/).length;
                container.style.gridTemplateColumns = `repeat(${cols}, 1fr)`;
                container.style.gridTemplateRows    = `repeat(${rows.length}, minmax(360px, auto))`;
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
                        renderContainer(wrapper, nested, manifest, pageTheme);
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
                        renderContainer(container, nested, manifest, pageTheme);
                    } else {
                        const btn = (manifest.buttons || []).find(b => b.name.toLowerCase() === item.toLowerCase());
                        if (btn) renderButton(container, btn);
                    }
                }
            });
        }
    }

    // Filter types that render without requiring rows
    const FILTER_TYPES = new Set(['SLICER', 'TABLE', 'CARD', 'TEXT', 'DATEPICKER', 'SLIDER', 'MULTISELECT', 'SEARCH']);

    function renderVisual(container, visual, pageTheme, manifest) {
        const card = document.createElement('div');
        card.className = 'visual-card';
        card.setAttribute('data-visual-name', visual.name);
        card._visualData = visual;

        // Apply WIDTH / HEIGHT / TOOLTIP from styles
        const vstyles = visual.styles || {};
        const width   = vstyles['WIDTH'] || vstyles['width'];
        const height  = vstyles['HEIGHT'] || vstyles['height'];
        const tooltip = vstyles['TOOLTIP'] || vstyles['tooltip'] || visual.tooltip;

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

        // Resolve effective theme: visual-level overrides page-level
        const effectiveTheme = vstyles['THEME'] || vstyles['theme'] || pageTheme || null;

        switch (type) {
            case 'TABLE':       renderTable(card, visual);                        break;
            case 'CARD':        renderCard(card, visual);                         break;
            case 'SLICER':      renderSlicer(card, visual, manifest);             break;
            case 'TEXT':        renderText(card, visual);                         break;
            case 'DATEPICKER':  renderDatePicker(card, visual, manifest);          break;
            case 'SLIDER':      renderSlider(card, visual, manifest);             break;
            case 'MULTISELECT': renderSlicer(card, visual, manifest);             break;
            case 'SEARCH':      renderSearch(card, visual, manifest);             break;
            case 'IMAGE':       renderImage(card, visual);                        break;
            default:            renderChart(card, visual, effectiveTheme);        break;
        }

        container.appendChild(card);
    }

    // ── Chart (ECharts — BAR / LINE / HBAR / SCATTER / PIE / DONUT / BOXPLOT / TREEMAP / HEATMAP / GAUGE / FUNNEL / WATERFALL / BUBBLE / RADAR / CANDLESTICK / MAP) ──

    // Cross-filter state: { filterValue, filterColumn }. Stored per page section.
    function getPageState(container) {
        let el = container;
        while (el && !el.classList.contains('page')) el = el.parentElement;
        if (!el) return null;
        if (!el._crossFilterState) el._crossFilterState = {};
        return el._crossFilterState;
    }

    function applyPageCrossFilter(container, filterValue, filterColumn) {
        let pageEl = container;
        while (pageEl && !pageEl.classList.contains('page')) pageEl = pageEl.parentElement;
        if (!pageEl) return;
        const state = pageEl._crossFilterState || (pageEl._crossFilterState = {});
        // Toggle: clicking same value clears filter
        if (state.filterValue === filterValue && state.filterColumn === filterColumn) {
            state.filterValue  = null;
            state.filterColumn = null;
        } else {
            state.filterValue  = filterValue;
            state.filterColumn = filterColumn;
        }
        // Re-render all cross-filter TABLE visuals on this page
        pageEl.querySelectorAll('[data-cross-filter]').forEach(el => {
            const visual = el._visualData;
            if (!visual) return;
            const wrapper = el.querySelector('.table-wrapper');
            if (!wrapper) return;
            const tbody = wrapper.querySelector('tbody');
            if (!tbody) return;
            const ci = (visual.columns || []).findIndex(
                c => c.toLowerCase() === (state.filterColumn || '').toLowerCase());
            Array.from(tbody.rows).forEach(tr => {
                if (!state.filterValue || ci < 0) {
                    tr.style.display = '';
                } else {
                    const cellVal = tr.cells[ci] ? tr.cells[ci].textContent : '';
                    tr.style.display = cellVal === state.filterValue ? '' : 'none';
                }
            });
        });
    }

    function registerMapThenRender(mapKey, mapFile, onReady, wrapper) {
        if (_registeredMaps.has(mapKey)) { onReady(); return; }
        const url = mapFile
            ? `/maps/custom?path=${encodeURIComponent(mapFile)}`
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

    function renderChart(container, visual, effectiveTheme) {
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
            // Auto-fix formatting for gauges and high-precision labels
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
                            if (typeof v === 'number' && !Number.isInteger(v)) return v.toFixed(1);
                            return v;
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
            chart.setOption(option);
            wrapper._echartsInst = chart;

            const clickActions = actionsFor(visual, 'ON_CLICK');
            const crossFilter  = isOn((visual.options || {})['CROSS_FILTER']);
            const xMappingCol  = (visual.options || {})['mapping:x'];

            if (clickActions.length > 0 || crossFilter) {
                chart.on('click', params => {
                    const idx     = params.dataIndex != null ? params.dataIndex : -1;
                    const rowData = idx >= 0 ? (visual.rows || [])[idx] || [] : [];
                    clickActions.forEach(action => executeAction(action, rowData, visual.columns || []));
                    if (crossFilter) {
                        const clickedValue = params.name || params.value || (rowData.length > 0 ? rowData[0] : null);
                        const colName = xMappingCol || (visual.columns && visual.columns[0]);
                        if (clickedValue != null && colName) {
                            applyPageCrossFilter(container, String(clickedValue), colName);
                        }
                    }
                });
            }
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

    // Lightweight singleton context menu for table right-click export
    let _ctxMenu = null;
    function showCtxMenu(x, y, visual) {
        hideCtxMenu();
        const menu = document.createElement('div');
        menu.style.cssText = 'position:fixed;z-index:9999;background:#fff;border:1px solid #ccc;' +
            'border-radius:4px;box-shadow:0 2px 8px rgba(0,0,0,0.18);padding:4px 0;font-size:13px;';
        menu.style.left = x + 'px';
        menu.style.top  = y + 'px';

        const item = document.createElement('div');
        item.textContent = '⬇ Export to CSV';
        item.style.cssText = 'padding:6px 16px;cursor:pointer;white-space:nowrap;';
        item.addEventListener('mouseenter', () => item.style.background = '#f0f4ff');
        item.addEventListener('mouseleave', () => item.style.background = '');
        item.addEventListener('click', () => { exportCsv(visual); hideCtxMenu(); });
        menu.appendChild(item);

        document.body.appendChild(menu);
        _ctxMenu = menu;

        // Close on any outside click
        setTimeout(() => document.addEventListener('click', hideCtxMenu, { once: true }), 0);
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
        const fmtRules      = visual.formattingRules || [];
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
        (visual.rows || []).forEach(row => {
            const tr = document.createElement('tr');
            if (isClickable) tr.style.cursor = 'pointer';
            // Evaluate table conditional formatting per row
            let rowColor = null;
            for (const rule of fmtRules) {
                if (!rule.condition) continue;
                try {
                    const fn = new Function(...visual.columns, "return " + rule.condition + ";");
                    const parsedRow = row.map(v => isNaN(parseFloat(v)) ? v : parseFloat(v));
                    if (fn(...parsedRow)) {
                        rowColor = rule.color;
                        break;
                    }
                } catch(e) {}
            }
            if (rowColor) { tr.style.color = rowColor; }

            visual.columns.forEach((col, ci) => {
                const td = document.createElement('td');
                const cellVal = row[ci] != null ? String(row[ci]) : '';
                const format  = (visual.options || {})['FORMAT'];
                td.textContent = formatValue(cellVal, format);
                tr.appendChild(td);

            });
            if (isClickable) {
                tr.addEventListener('click', () => {
                    clickActions.forEach(action => executeAction(action, row, visual.columns));
                });
            }
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        wrapper.appendChild(table);

        // Right-click → Export to CSV
        wrapper.addEventListener('contextmenu', e => {
            e.preventDefault();
            showCtxMenu(e.clientX, e.clientY, visual);
        });

        container.appendChild(wrapper);
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
        
        // Find the parameter name from actions
        const action = visual.actions.find(a => a.type === 'SET_PARAMETER');
        const paramName = action ? action.parameterName : null;

        const select = document.createElement('select');
        // Attach parameter info to the interactive element for syncParameters
        if (paramName) select.setAttribute('data-parameter', paramName);
        
        // Optional: Multi-select support
        if (visual.visualType.toLowerCase() === 'multiselect') {
            select.multiple = true;
        }

        // Add options
        const valCol = (visual.options['mapping:value'] || 'value').toLowerCase();
        const lblCol = (visual.options['mapping:label'] || 'label').toLowerCase();
        
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

        // Set initial value from manifest parameters (case-insensitive)
        if (paramName && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, paramName);
            if (current !== undefined) select.value = current;
        }

        const changeActions = actionsFor(visual, 'ON_CHANGE')
            .filter(a => a.type === 'SET_PARAMETER');

        if (isWebMode && changeActions.length > 0) {
            select.addEventListener('change', () => {
                changeActions.forEach(action => {
                    postParameter(action.parameterName, select.value)
                        .then(manifest => { if (manifest) renderManifest(manifest); });
                });
            });

            wrapper.appendChild(select);
        } else {
            const note = document.createElement('p');
            note.className = 'slicer-note';
            const paramNames = changeActions.map(a => a.parameterName).filter(Boolean).join(', ');
            note.textContent = paramNames
                ? '[Slicer \u2192 ' + paramNames + ' \u2014 interactive in ReportPlayer only]'
                : '[Slicer \u2014 interactive in ReportPlayer only]';
            wrapper.appendChild(note);
        }

        container.appendChild(wrapper);
    }

    // ── Text ────────────────────────────────────────────────────────────────

    function renderText(container, visual) {
        const opts  = visual.options || {};
        const rawValue = opts['VALUE'] || opts['value'] || visual.defaultValue || '';
        const value = String(rawValue).replace(/\\n/g, '\n');
        const align = (opts['ALIGN'] || opts['align'] || 'left').toLowerCase();

        const div = document.createElement('div');
        div.className = 'text-visual';
        div.style.textAlign = align;
        div.innerHTML = simpleMarkdown(value);
        container.appendChild(div);
    }

    // Minimal markdown → HTML: bold, italic, inline code, headers, line breaks.
    function simpleMarkdown(src) {
        return escHtml(src)
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/\*(.+?)\*/g,     '<em>$1</em>')
            .replace(/`(.+?)`/g,       '<code>$1</code>')
            .replace(/^### (.+)$/gm,   '<h3>$1</h3>')
            .replace(/^## (.+)$/gm,    '<h2>$1</h2>')
            .replace(/^# (.+)$/gm,     '<h1>$1</h1>')
            .replace(/\n/g,            '<br>');
    }

    // ── DatePicker ──────────────────────────────────────────────────────────

    function renderDatePicker(container, visual, manifest) {
        const opts          = visual.options || {};
        // Parameter binding comes from ACTIONS (ON_CHANGE = SET_PARAMETER), not from OPTIONS
        const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
        const param         = changeActions.length > 0 ? changeActions[0].parameterName : null;
        const min           = opts['MIN'] || opts['min'] || '';
        const max           = opts['MAX'] || opts['max'] || '';

        // Initial value: current parameter value > DEFAULT clause > fallback empty
        let def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || '';
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined) def = current;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const input = document.createElement('input');
        input.type  = 'date';
        if (min) input.min = min;
        if (max) input.max = max;
        if (def) input.value = def;
        if (param) input.setAttribute('data-parameter', param);
        wrapper.appendChild(input);

        if (isWebMode && changeActions.length > 0) {
            input.addEventListener('change', () => {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = input.value; return o; }, {});
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

    function renderMultiSelect(container, visual) {
        const opts  = visual.options || {};
        const param = opts['PARAMETER'] || opts['parameter'] || null;

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const select = document.createElement('select');
        select.multiple = true;

        (visual.rows || []).forEach(row => {
            const opt = document.createElement('option');
            opt.value       = String(row[0] ?? '');
            opt.textContent = String(row[0] ?? '');
            select.appendChild(opt);
        });

        wrapper.appendChild(select);

        if (isWebMode && param) {
            const applyBtn = document.createElement('button');
            applyBtn.className   = 'filter-apply';
            applyBtn.textContent = 'Apply';
            applyBtn.addEventListener('click', () => {
                const selected = Array.from(select.selectedOptions).map(o => o.value).join(',');
                postParameter(param, selected)
                    .then(m => { if (m) renderManifest(m); });
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
        const placeholder   = opts['PLACEHOLDER'] || opts['placeholder'] || 'Search…';

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
        if (btn.tooltip && btn.tooltip.text) btnEl.title = btn.tooltip.text;

        // Apply inline styles from STYLE definition
        const bg  = styles['BACKGROUND'] || styles['background'];
        const fg  = styles['COLOR']      || styles['color'];
        const pad = styles['PADDING']    || styles['padding'];
        if (bg)  btnEl.style.background = bg;
        if (fg)  btnEl.style.color      = fg;
        if (pad) btnEl.style.padding    = pad;
        btnEl.style.cursor      = 'pointer';
        btnEl.style.borderRadius = '4px';
        btnEl.style.border       = 'none';
        btnEl.style.fontWeight   = '600';

        const type = (btn.buttonType || '').toUpperCase();
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
                if (isWebMode) {
                    fetch(apiBase + '/manifest')
                        .then(r => r.json())
                        .then(m => renderManifest(m))
                        .catch(e => console.error('Refresh failed:', e));
                }
            } else {
                // Custom button — execute ON_CLICK actions
                const clickActions = actionsFor(btn, 'ON_CLICK');
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
                    if (a.keyColumn) batch['@' + a.keyColumn] = '';
                });
                setParamActions.forEach(a => {
                    if (a.parameterName) batch[a.parameterName] = a.valueExpression || '';
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

    // ── Actions ─────────────────────────────────────────────────────────────

    function actionsFor(visual, trigger) {
        return (visual.actions || []).filter(a => a.trigger === trigger);
    }

    function executeAction(action, rowData, columns) {
        if (action.type === 'DRILL_DOWN') {
            const colIdx = columns.findIndex(
                c => c.toLowerCase() === (action.keyColumn || '').toLowerCase());
            const value  = colIdx >= 0 ? rowData[colIdx] : null;
            if (value == null) return;
            postParameter('@' + action.keyColumn, String(value))
                .then(manifest => { if (manifest) renderManifest(manifest); });

        } else if (action.type === 'SET_PARAMETER') {
            const expr   = action.valueExpression || '';
            const colIdx = columns.findIndex(c => c.toLowerCase() === expr.toLowerCase());
            const value  = colIdx >= 0 ? rowData[colIdx] : expr;
            postParameter(action.parameterName, String(value ?? ''))
                .then(manifest => { if (manifest) renderManifest(manifest); });
        }
    }

    async function postParameter(name, value) {
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
    async function postParameters(params) {
        try {
            const res = await fetch(apiBase + '/parameters', {
                method:  'POST',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ params })
            });
            if (!res.ok) return null;
            return await res.json();
        } catch {
            return null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    function noDataEl(msg) {
        const p = document.createElement('p');
        p.className = 'no-data';
        p.textContent = msg;
        return p;
    }

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

    // Boot on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }

    // Test escape hatch: exposes pure functions for automated testing.
    // Harmless in production (just sets a window property that nothing reads).
    if (typeof window !== 'undefined') {
        window.__reportRuntime__ = { isOn, renderCard, renderDatePicker, renderSlider, renderSearch, renderButton, renderChart };
    }
})();
