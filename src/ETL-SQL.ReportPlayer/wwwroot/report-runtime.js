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
    const isWebMode = !!window.__IS_WEB__;

    // In multi-report mode the server injects window.__API_BASE__ = '/reports/{name}/api'.
    // Single-report and VS Code modes default to '/api'.
    const apiBase = (window.__API_BASE__ || '/api').replace(/\/$/, '');

    /**
     * Entry point: obtain manifest and render all visuals + pages.
     */
    async function boot() {
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
        const root = document.getElementById('root');
        if (!root) return;
        root.innerHTML = '';

        // Navigation bar
        const navDef = manifest.navigations && manifest.navigations.length > 0
            ? manifest.navigations[0] : null;

        if (manifest.pages && manifest.pages.length > 0) {
            // Render all pages (hidden by default when nav exists)
            const pageSections = {};
            manifest.pages.forEach(page => {
                const section = renderPage(root, page, manifest, !!navDef);
                pageSections[page.name] = section;
            });

            if (navDef) {
                renderNavBar(root, navDef, pageSections, manifest.pages);
            }
        } else {
            (manifest.visuals || []).forEach(v => renderVisual(root, v, null));
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

            el.addEventListener('click', () => {
                // Hide all, show clicked
                navDef.pages.forEach(n => {
                    const s = pageSections[n];
                    if (s) s.style.display = 'none';
                });
                const target = pageSections[pageName];
                if (target) target.style.display = 'block';

                // Update active class
                nav.querySelectorAll('.' + itemClass).forEach(e => e.classList.remove('active'));
                el.classList.add('active');
            });

            nav.appendChild(el);
        });

        // Show default page, hide others
        pages.forEach(p => {
            const s = pageSections[p.name];
            if (s) s.style.display = p.name === defaultPageName ? 'block' : 'none';
        });
    }

    function renderPage(container, page, manifest, hideByDefault) {
        const section = document.createElement('section');
        section.className = 'page' + (hideByDefault ? '' : ' active');
        if (hideByDefault) section.style.display = 'none';

        const heading = document.createElement('h2');
        heading.textContent = page.name;
        section.appendChild(heading);

        // Extract page-level theme for cascading to charts
        const pageStyles = page.styles || {};
        const pageTheme  = pageStyles['THEME'] || pageStyles['theme'] || null;

        // Build CSS grid content div if structure is present
        const contentDiv = document.createElement('div');
        if (page.structure) {
            contentDiv.className = 'page-grid';
            // Parse STRUCTURE: 'A A / B C' → grid-template-areas
            const rows = page.structure.split('/').map(r => '"' + r.trim() + '"');
            contentDiv.style.gridTemplateAreas = rows.join(' ');
            // Determine unique area letters to set grid-template-columns/rows
            const uniquePerRow = page.structure.split('/').map(r => r.trim().split(/\s+/).filter((v, i, a) => a.indexOf(v) === i).length);
            contentDiv.style.gridTemplateColumns = 'repeat(' + Math.max(...uniquePerRow) + ', 1fr)';
        }
        section.appendChild(contentDiv);

        // Render visuals in slot order
        const containers = manifest.containers || [];
        const uniqueSlotValues = [...new Set(Object.values(page.slotMap || {}))];
        uniqueSlotValues.forEach(slotValue => {
            // Find the slot letter(s) for this value
            const slotLetters = Object.keys(page.slotMap || {}).filter(k => page.slotMap[k] === slotValue);
            const gridArea = slotLetters[0]; // use first letter as grid-area

            // Check if slotValue refers to a container
            const containerDef = containers.find(c => c.name.toLowerCase() === slotValue.toLowerCase());
            if (containerDef) {
                const wrapper = document.createElement('div');
                wrapper.style.gridArea = gridArea;
                renderContainer(wrapper, containerDef, manifest, pageTheme);
                contentDiv.appendChild(wrapper);
                return;
            }

            // Otherwise it's a visual
            const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === slotValue.toLowerCase());
            if (visual) {
                const wrapper = document.createElement('div');
                wrapper.style.gridArea = gridArea;
                renderVisual(wrapper, visual, pageTheme);
                contentDiv.appendChild(wrapper);
            }
        });

        container.appendChild(section);
        return section;
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

        (containerDef.visuals || []).forEach(visualName => {
            const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === visualName.toLowerCase());
            if (visual) renderVisual(div, visual, pageTheme);
        });

        container.appendChild(div);
    }

    // Filter types that render without requiring rows
    const FILTER_TYPES = new Set(['SLICER', 'TABLE', 'CARD', 'TEXT', 'DATEPICKER', 'SLIDER', 'MULTISELECT', 'SEARCH']);

    function renderVisual(container, visual, pageTheme) {
        const card = document.createElement('div');
        card.className = 'visual-card';

        // Apply WIDTH / HEIGHT from styles
        const vstyles = visual.styles || {};
        if (vstyles['WIDTH'] || vstyles['width'])
            card.style.width = vstyles['WIDTH'] || vstyles['width'];
        if (vstyles['HEIGHT'] || vstyles['height'])
            card.style.height = vstyles['HEIGHT'] || vstyles['height'];

        const title = document.createElement('h3');
        title.textContent = visual.name;
        card.appendChild(title);

        if (visual.error) {
            card.appendChild(errorEl(visual.error));
            container.appendChild(card);
            return;
        }

        const type = (visual.visualType || '').toUpperCase();

        if (!FILTER_TYPES.has(type) && (!visual.rows || visual.rows.length === 0)) {
            card.appendChild(noDataEl('No data available'));
            container.appendChild(card);
            return;
        }

        // Resolve effective theme: visual-level overrides page-level
        const effectiveTheme = vstyles['THEME'] || vstyles['theme'] || pageTheme || null;

        switch (type) {
            case 'TABLE':       renderTable(card, visual);                        break;
            case 'CARD':        renderCard(card, visual);                         break;
            case 'SLICER':      renderSlicer(card, visual);                       break;
            case 'TEXT':        renderText(card, visual);                         break;
            case 'DATEPICKER':  renderDatePicker(card, visual);                   break;
            case 'SLIDER':      renderSlider(card, visual);                       break;
            case 'MULTISELECT': renderMultiSelect(card, visual);                  break;
            case 'SEARCH':      renderSearch(card, visual);                       break;
            default:            renderChart(card, visual, effectiveTheme);        break;
        }

        container.appendChild(card);
    }

    // ── Chart (ECharts — BAR / LINE / HBAR / SCATTER / PIE / DONUT / BOXPLOT / TREEMAP / HEATMAP / GAUGE / FUNNEL / WATERFALL) ──

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

        // effectiveTheme: visual-level THEME, falling back to page-level THEME
        const chart = echarts.init(wrapper, effectiveTheme || null);
        chart.setOption(option);

        const clickActions  = actionsFor(visual, 'ON_CLICK');
        const crossFilter   = (visual.options || {})['CROSS_FILTER'] === 'true';
        const xMappingCol   = (visual.options || {})['mapping:x'];

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

    // ── Table ───────────────────────────────────────────────────────────────

    function renderTable(container, visual) {
        if (!visual.columns || visual.columns.length === 0) {
            container.appendChild(noDataEl('No data available'));
            return;
        }

        const clickActions  = actionsFor(visual, 'ON_CLICK');
        const isClickable   = clickActions.length > 0;
        const fmtRules      = visual.formattingRules || [];
        const crossFilter   = (visual.options || {})['CROSS_FILTER'] === 'true';

        // Register as a cross-filter target so applyPageCrossFilter can find it
        if (crossFilter) {
            container.setAttribute('data-cross-filter', '1');
            container._visualData = visual;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'table-wrapper' + (isClickable ? ' clickable' : '');

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
            visual.columns.forEach((col, ci) => {
                const td = document.createElement('td');
                const cellVal = row[ci] != null ? String(row[ci]) : '';
                td.textContent = cellVal;
                // Apply conditional formatting rules for this column
                for (const rule of fmtRules) {
                    if (rule.column.toLowerCase() !== col.toLowerCase()) continue;
                    const num = parseFloat(cellVal);
                    const thr = parseFloat(rule.threshold);
                    let match = false;
                    if (!isNaN(num) && !isNaN(thr)) {
                        match = rule.operator === '<'  ? num < thr
                              : rule.operator === '>'  ? num > thr
                              : rule.operator === '<=' ? num <= thr
                              : rule.operator === '>=' ? num >= thr
                              : rule.operator === '='  ? num === thr
                              : rule.operator === '<>' ? num !== thr : false;
                    } else {
                        match = rule.operator === '='  ? cellVal === rule.threshold
                              : rule.operator === '<>' ? cellVal !== rule.threshold : false;
                    }
                    if (match) { td.style.color = rule.color; break; }
                }
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
        container.appendChild(wrapper);
    }

    // ── Card ────────────────────────────────────────────────────────────────

    function renderCard(container, visual) {
        const cardEl = document.createElement('div');
        cardEl.className = 'card-value';
        const label = visual.columns && visual.columns[0] ? visual.columns[0] : visual.name;
        const value = visual.rows && visual.rows[0] && visual.rows[0][0] != null
            ? String(visual.rows[0][0])
            : 'No data';
        cardEl.innerHTML = '<span class="card-label">' + escHtml(label) + '</span>' +
                           '<span class="card-number">' + escHtml(value) + '</span>';
        container.appendChild(cardEl);
    }

    // ── Slicer ──────────────────────────────────────────────────────────────

    function renderSlicer(container, visual) {
        const changeActions = actionsFor(visual, 'ON_CHANGE')
            .filter(a => a.type === 'SET_PARAMETER');

        const wrapper = document.createElement('div');
        wrapper.className = 'slicer-wrapper';

        if (isWebMode && changeActions.length > 0) {
            const select = document.createElement('select');

            // Blank "All" option
            const blank = document.createElement('option');
            blank.value = '';
            blank.textContent = '— All —';
            select.appendChild(blank);

            (visual.rows || []).forEach(row => {
                const opt = document.createElement('option');
                opt.value       = String(row[0] ?? '');
                opt.textContent = String(row[0] ?? '');
                select.appendChild(opt);
            });

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
        const value = opts['VALUE'] || opts['value'] || '';
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

    function renderDatePicker(container, visual) {
        const opts  = visual.options || {};
        const param = opts['PARAMETER'] || opts['parameter'] || null;
        const min   = opts['MIN']  || opts['min']  || '';
        const max   = opts['MAX']  || opts['max']  || '';
        const def   = opts['DEFAULT'] || opts['default'] || '';

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const input = document.createElement('input');
        input.type  = 'date';
        if (min) input.min = min;
        if (max) input.max = max;
        if (def) input.value = def;
        wrapper.appendChild(input);

        if (isWebMode && param) {
            input.addEventListener('change', () => {
                postParameter(param, input.value)
                    .then(m => { if (m) renderManifest(m); });
            });
        }
        container.appendChild(wrapper);
    }

    // ── Slider ──────────────────────────────────────────────────────────────

    function renderSlider(container, visual) {
        const opts  = visual.options || {};
        const param = opts['PARAMETER'] || opts['parameter'] || null;
        const min   = opts['MIN']  || opts['min']  || '0';
        const max   = opts['MAX']  || opts['max']  || '100';
        const step  = opts['STEP'] || opts['step'] || '1';
        const def   = opts['DEFAULT'] || opts['default'] || min;

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const input = document.createElement('input');
        input.type  = 'range';
        input.min   = min;
        input.max   = max;
        input.step  = step;
        input.value = def;

        const valueLabel = document.createElement('span');
        valueLabel.className   = 'range-value';
        valueLabel.textContent = def;

        input.addEventListener('input', () => { valueLabel.textContent = input.value; });

        if (isWebMode && param) {
            input.addEventListener('change', () => {
                postParameter(param, input.value)
                    .then(m => { if (m) renderManifest(m); });
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

    function renderSearch(container, visual) {
        const opts        = visual.options || {};
        const param       = opts['PARAMETER']   || opts['parameter']   || null;
        const placeholder = opts['PLACEHOLDER'] || opts['placeholder'] || 'Search…';

        const wrapper = document.createElement('div');
        wrapper.className = 'filter-wrapper';

        const input         = document.createElement('input');
        input.type          = 'search';
        input.placeholder   = placeholder;

        wrapper.appendChild(input);

        if (isWebMode && param) {
            let debounceTimer = null;
            input.addEventListener('input', () => {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(() => {
                    postParameter(param, input.value)
                        .then(m => { if (m) renderManifest(m); });
                }, 350);
            });
        }

        container.appendChild(wrapper);
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

    // Boot on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
