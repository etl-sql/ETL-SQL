/**
 * ETL-SQL Designer — shared vanilla-JS component
 *
 * Three exported surface areas, implemented across phases:
 *   renderDag()          Phase 2 — read-only DAG / lineage visualization (ECharts)
 *   createScriptEditor() Phase 3 — CodeMirror rptsql editor
 *   createDesigner()     Phase 4 — full WYSIWYG report designer
 *
 * Hosted in two places via sync-assets.ps1:
 *   Portal   → src/ETL-SQL.ReportPortal/wwwroot/designer/designer.js
 *   VS Code  → src/etl-sql-vscode/media/designer/designer.js
 *
 * Both hosts load this as a plain ES module:
 *   <script type="module" src="designer/designer.js"></script>
 *
 * ECharts must be available at window.echarts (already loaded in both hosts).
 * CodeMirror bundle loaded on demand: designer/codemirror/codemirror-bundle.min.js
 */

// ─────────────────────────────────────────────────────────────────────────────
// Phase 2 — DAG Visualization
// ─────────────────────────────────────────────────────────────────────────────

function _h(str) {
    return String(str ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

const _TYPE_COLOR = {
    dataset:     '#10b981',
    visual:      '#3b82f6',
    page:        '#8b5cf6',
    table:       '#64748b',
    column:      '#94a3b8',
    statement:   '#475569',
    conditional: '#f59e0b',
    loop:        '#f97316',
    io:          '#14b8a6',
    procedure:   '#a855f7',
    connection:  '#0ea5e9',
};

function _nodeColor(type) {
    return _TYPE_COLOR[type] ?? '#94a3b8';
}

function _nodeSymbol(type) {
    if (type === 'visual')                          return 'diamond';
    if (type === 'page')                            return 'roundRect';
    if (type === 'dataset' || type === 'table')     return 'roundRect';
    if (type === 'column')                          return 'circle';
    return 'circle';
}

function _nodeSize(type) {
    if (type === 'page')   return 44;
    if (type === 'column') return 18;
    return 36;
}

/**
 * Assign x,y positions to nodes using a top-down layered (Sugiyama-inspired) layout.
 * Returns a map of { [nodeId]: { x, y } }.
 */
function _computeLayout(nodes, edges) {
    const ids     = nodes.map(n => n.id);
    const inDeg   = Object.fromEntries(ids.map(id => [id, 0]));
    const children = Object.fromEntries(ids.map(id => [id, []]));

    for (const e of edges) {
        if (inDeg[e.target] !== undefined)  inDeg[e.target]++;
        if (children[e.source])             children[e.source].push(e.target);
    }

    // BFS from roots to assign layers
    const layer = {};
    const queue = ids.filter(id => inDeg[id] === 0);
    for (const id of queue) layer[id] = 0;

    while (queue.length > 0) {
        const id  = queue.shift();
        const cur = layer[id] ?? 0;
        for (const child of children[id] || []) {
            if (layer[child] === undefined || layer[child] <= cur) {
                layer[child] = cur + 1;
                queue.push(child);
            }
        }
    }
    // Any unreached nodes (isolated or cycles) get layer 0
    for (const id of ids) if (layer[id] === undefined) layer[id] = 0;

    // Group by layer, preserving original node order within each layer
    const byLayer = {};
    for (const id of ids) {
        const l = layer[id];
        (byLayer[l] = byLayer[l] || []).push(id);
    }

    const LAYER_H = 160;
    const NODE_W  = 240;
    const pos = {};
    for (const [l, layerIds] of Object.entries(byLayer)) {
        const count = layerIds.length;
        layerIds.forEach((id, i) => {
            pos[id] = {
                x: (i - (count - 1) / 2) * NODE_W,
                y: +l * LAYER_H,
            };
        });
    }
    return pos;
}

/**
 * Render a read-only directed graph inside `container` using ECharts.
 *
 * @param {HTMLElement} container   DOM element to render into. Must have a defined height.
 * @param {Object}      graph
 * @param {Array}       graph.nodes [{ id: string, label: string, type?: string, meta?: object }]
 * @param {Array}       graph.edges [{ source: string, target: string, label?: string }]
 * @param {Object}      [options]
 * @param {string}      [options.theme='portal']   'portal' | 'vscode' — affects colour palette
 * @param {Function}    [options.onNodeClick]       Called with (nodeId, nodeMeta) on click
 * @returns {{ dispose: Function, resize: Function }}
 *   dispose() — destroys the ECharts instance and removes DOM listeners
 *   resize()  — re-fits the chart to the current container size (call on panel resize)
 */
export function renderDag(container, { nodes, edges }, options = {}) {
    const ec = window.echarts;

    if (!ec) {
        container.innerHTML = '<div class="etlsql-dag-empty">ECharts not loaded.</div>';
        return { dispose: () => {}, resize: () => {} };
    }

    if (!nodes?.length) {
        container.innerHTML = '<div class="etlsql-dag-empty">No structure data available.</div>';
        return { dispose: () => {}, resize: () => {} };
    }

    const expandedNodes = new Set();

    function buildGraph() {
        const allNodes = [...nodes];
        const allEdges = [...(edges ?? [])];

        // Inject column sub-nodes for expanded table/dataset nodes
        const pos = _computeLayout(nodes, edges);
        const COL_SPREAD = 160;
        const COL_OFFSET_Y = 90;

        for (const n of nodes) {
            if (!expandedNodes.has(n.id)) continue;
            const cols = n.meta?.columns;
            if (!cols?.length) continue;

            const parentPos = pos[n.id] ?? { x: 0, y: 0 };
            const count = cols.length;
            cols.forEach((col, i) => {
                const colId = `${n.id}__col__${col}`;
                allNodes.push({
                    id: colId, label: col, type: 'column',
                    meta: { parent: n.id, column: col },
                });
                pos[colId] = {
                    x: parentPos.x + (i - (count - 1) / 2) * (COL_SPREAD / Math.max(count, 4)),
                    y: parentPos.y + COL_OFFSET_Y,
                };
            });

            // Column-to-column edges from colEdges metadata
            for (const ce of (n.meta?.colEdges ?? [])) {
                const srcNodeId = Object.keys(pos).find(id => {
                    const nd = allNodes.find(x => x.id === id);
                    return nd && (nd.label === ce.srcTable || nd.id === `ds:${ce.srcTable}` || nd.id === `table:${ce.srcTable}`);
                });
                if (!srcNodeId || !expandedNodes.has(srcNodeId)) continue;
                const srcColId = `${srcNodeId}__col__${ce.srcCol}`;
                const tgtColId = `${n.id}__col__${ce.tgtCol}`;
                if (allNodes.find(x => x.id === srcColId) && allNodes.find(x => x.id === tgtColId))
                    allEdges.push({ source: srcColId, target: tgtColId, label: null });
            }
        }

        return { allNodes, allEdges, pos };
    }

    function toECharts({ allNodes, allEdges, pos }) {
        const eNodes = allNodes.map(n => ({
            id:         n.id,
            name:       n.label,
            x:          pos[n.id]?.x ?? 0,
            y:          pos[n.id]?.y ?? 0,
            symbol:     _nodeSymbol(n.type),
            symbolSize: _nodeSize(n.type),
            label:      {
                show: true, formatter: '{b}', fontSize: n.type === 'column' ? 9 : 11,
                overflow: 'truncate', width: n.type === 'column' ? 80 : 140,
            },
            itemStyle: {
                color: _nodeColor(n.type),
                borderColor: n.meta?.columns?.length && !expandedNodes.has(n.id)
                    ? '#10b981' : 'transparent',
                borderWidth: 2,
            },
            emphasis:  { itemStyle: { borderColor: '#fff', borderWidth: 2 } },
            tooltip:   {
                formatter: () => {
                    if (n.type === 'column') return `<strong>${_h(n.label)}</strong><br/><span style="color:#94a3b8">column of</span> ${_h(n.meta?.parent)}`;
                    const cols = n.meta?.columns;
                    const hint = cols?.length
                        ? `<br/><span style="color:#10b981">${expandedNodes.has(n.id) ? '▲ click to collapse' : '▼ click to expand columns'}</span>`
                        : '';
                    const meta = n.meta ? Object.entries(n.meta)
                        .filter(([k]) => k !== 'columns' && k !== 'colEdges')
                        .map(([k, v]) => `<br/><span style="color:#94a3b8">${_h(k)}:</span> ${_h(v)}`)
                        .join('') : '';
                    return `<strong>${_h(n.label)}</strong><br/><span style="color:#94a3b8">type:</span> ${_h(n.type)}${meta}${hint}`;
                },
            },
            _meta: n.meta,
        }));

        const eEdges = allEdges.map(e => ({
            source:    e.source,
            target:    e.target,
            label:     e.label ? { show: true, formatter: e.label, fontSize: 10, color: '#94a3b8' } : { show: false },
            lineStyle: {
                color: e.source?.includes('__col__') || e.target?.includes('__col__') ? '#cbd5e1' : '#94a3b8',
                width: e.source?.includes('__col__') ? 1 : 1.5,
                type:  e.source?.includes('__col__') ? 'dashed' : 'solid',
            },
        }));

        return { eNodes, eEdges };
    }

    const chart = ec.init(container, null, { renderer: 'canvas' });

    function render() {
        const graph = buildGraph();
        const { eNodes, eEdges } = toECharts(graph);

        // Fit zoom: scale so all nodes are visible on first render regardless of graph size
        let fitZoom = 0.65;
        const positions = Object.values(graph.pos);
        if (positions.length > 1) {
            const xs = positions.map(p => p.x);
            const ys = positions.map(p => p.y);
            const dataW = Math.max(...xs) - Math.min(...xs) + 280;
            const dataH = Math.max(...ys) - Math.min(...ys) + 120;
            const cw = container.clientWidth  || 900;
            const ch = container.clientHeight || 600;
            fitZoom = Math.max(Math.min(cw / dataW, ch / dataH, 1.0), 0.15);
        }

        chart.setOption({
            tooltip: { show: true, confine: true },
            series: [{
                type:           'graph',
                layout:         'none',
                nodes:          eNodes,
                edges:          eEdges,
                roam:           true,
                zoom:           fitZoom,
                center:         ['50%', '50%'],
                edgeSymbol:     ['none', 'arrow'],
                edgeSymbolSize: 8,
                lineStyle:      { curveness: 0.15 },
                label:          { position: 'inside', color: '#fff' },
                emphasis:       { focus: 'adjacency' },
            }],
        }, true);
    }

    render();

    chart.on('click', params => {
        if (params.dataType !== 'node') return;
        const meta = params.data._meta;
        // Expand/collapse nodes that have column data
        if (meta?.columns?.length) {
            const nodeId = params.data.id;
            if (expandedNodes.has(nodeId)) expandedNodes.delete(nodeId);
            else expandedNodes.add(nodeId);
            render();
        }
        if (options.onNodeClick)
            options.onNodeClick(params.data.id, meta);
    });

    return {
        dispose: () => chart.dispose(),
        resize:  () => chart.resize(),
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3 — Script Editor
// ─────────────────────────────────────────────────────────────────────────────

// rptsql token classification sets — sourced from LanguageMetadata.cs
const _KW = new Set([
    'SELECT','FROM','WHERE','INSERT','UPDATE','DELETE','SET','INTO','VALUES',
    'ORDER','BY','GROUP','HAVING','LIMIT','OFFSET','TOP','DISTINCT','ALL',
    'AS','ON','CASE','WHEN','THEN','ELSE','END','WITH','OUTPUT',
    'CREATE','TABLE','DATASET','VISUAL','PAGE','SECTION','COLUMN','INDEX',
    'PROCEDURE','CONNECTION','DROP','ALTER','ADD','CONSTRAINT',
    'PRIMARY','KEY','FOREIGN','REFERENCES','DEFAULT','UNIQUE',
    'IF','WHILE','FOR','FOREACH','BEGIN','RETURN','BREAK',
    'CONTINUE','TRY','CATCH','THROW','DECLARE','PRINT','EXEC','EXECUTE',
    'JOIN','INNER','LEFT','RIGHT','FULL','OUTER','CROSS','APPLY','UNION',
    'INTERSECT','EXCEPT',
    'AND','OR','NOT','LIKE','IN','IS','BETWEEN','EXISTS','ANY','SOME',
    'NULL','TRUE','FALSE',
    'REQUIRE','VERSION','RUN','SCRIPT','USE','LOAD','SAVE','EXPORT','IMPORT',
]);

const _FUNC = new Set([
    'CAST','CONVERT','COUNT','SUM','AVG','MIN','MAX','FIRST','LAST',
    'ROW_NUMBER','RANK','DENSE_RANK','NTILE','LAG','LEAD',
    'FIRST_VALUE','LAST_VALUE','PERCENTILE_CONT','PERCENTILE_DISC',
    'COALESCE','NULLIF','IIF','ISNULL','NVL',
    'UPPER','LOWER','TRIM','LTRIM','RTRIM','LEN','LENGTH','SUBSTRING',
    'REPLACE','STUFF','CHARINDEX','PATINDEX','CONCAT','FORMAT',
    'YEAR','MONTH','DAY','DATEPART','DATEDIFF','DATEADD','GETDATE','NOW',
    'SYSDATETIME','CURRENT_TIMESTAMP',
    'ABS','CEILING','FLOOR','ROUND','POWER','SQRT','SIGN','RAND',
    'NEWID','CHECKSUM','HASHBYTES',
    'STRING_AGG','LISTAGG','ARRAY_AGG','JSON_VALUE','JSON_QUERY',
]);

const _TYPE = new Set([
    'INT','INTEGER','TINYINT','SMALLINT','BIGINT',
    'DECIMAL','NUMERIC','FLOAT','REAL','MONEY','SMALLMONEY',
    'VARCHAR','NVARCHAR','CHAR','NCHAR','TEXT','NTEXT',
    'DATETIME','DATETIME2','DATE','TIME','DATETIMEOFFSET','TIMESTAMP',
    'BIT','BINARY','VARBINARY','IMAGE',
    'UNIQUEIDENTIFIER','XML','JSON','CURSOR','VARIANT',
]);

// Lazy-load the CodeMirror bundle once; subsequent calls reuse the same promise.
let _cmPromise = null;
function _loadCm() {
    if (!_cmPromise) _cmPromise = import('./codemirror/codemirror-bundle.min.js');
    return _cmPromise;
}

// Cached rptsql StreamLanguage instance (shared across all editor instances).
let _rptsqlLang = null;
function _getRptsqlLang(cm) {
    if (_rptsqlLang) return _rptsqlLang;
    const { StreamLanguage, tags: t } = cm;
    _rptsqlLang = StreamLanguage.define({
        name: 'rptsql',
        token(stream) {
            if (stream.eatSpace()) return null;
            if (stream.match('--'))  { stream.skipToEnd(); return 'lineComment'; }
            if (stream.match('/*'))  {
                while (!stream.eol()) { if (stream.match('*/')) break; stream.next(); }
                return 'blockComment';
            }
            const ch = stream.peek();
            if (ch === "'" || ch === '"') {
                stream.next();
                while (!stream.eol() && stream.next() !== ch) {}
                return 'string';
            }
            if (ch === '[') {
                stream.next();
                while (!stream.eol() && stream.next() !== ']') {}
                return 'quotedId';
            }
            if (stream.match(/^[0-9]+\.?[0-9]*/)) return 'number';
            if (stream.match(/^[a-zA-Z_@#][a-zA-Z0-9_@#$]*/)) {
                const word = stream.current().toUpperCase();
                if (_KW.has(word))   return 'keyword';
                if (_FUNC.has(word)) return 'fn';
                if (_TYPE.has(word)) return 'typeName';
                return null;
            }
            if (stream.match(/^(<>|!=|>=|<=|=>|->|::)/)) return 'op';
            if (stream.match(/^[=<>!+\-*\/&|^~%]/))      return 'op';
            stream.next();
            return null;
        },
        tokenTable: {
            lineComment:  t.lineComment,
            blockComment: t.blockComment,
            string:       t.string,
            number:       t.number,
            keyword:      t.keyword,
            fn:           t.function(t.variableName),
            typeName:     t.typeName,
            quotedId:     t.special(t.variableName),
            op:           t.operator,
        },
        languageData: {
            commentTokens: { line: '--', block: { open: '/*', close: '*/' } },
        },
    });
    return _rptsqlLang;
}

/**
 * Mount a CodeMirror 6 rptsql editor into `container`.
 *
 * Dynamically loads designer/codemirror/codemirror-bundle.min.js the first
 * time it is called, then initialises a CodeMirror EditorView with the custom
 * rptsql language mode.
 *
 * @param {HTMLElement} container
 * @param {Object}      [opts]
 * @param {string}      [opts.value='']       Initial script content.
 * @param {boolean}     [opts.readOnly=false]
 * @param {Function}    [opts.onChange]        Called with the full new value on each change.
 * @returns {Promise<{ getValue: Function, setValue: Function, dispose: Function }>}
 *   Returns a promise so callers can await the dynamic bundle load.
 */
export async function createScriptEditor(container, opts = {}) {
    const cm = await _loadCm();
    const {
        EditorState,
        EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection,
        defaultKeymap, history, historyKeymap, indentWithTab,
        syntaxHighlighting, defaultHighlightStyle, bracketMatching,
        searchKeymap, highlightSelectionMatches,
    } = cm;

    const extensions = [
        lineNumbers(),
        highlightActiveLine(),
        highlightActiveLineGutter(),
        drawSelection(),
        history(),
        bracketMatching(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        highlightSelectionMatches(),
        keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap, ...searchKeymap]),
        _getRptsqlLang(cm),
        EditorState.readOnly.of(opts.readOnly ?? false),
    ];

    if (opts.onChange) {
        extensions.push(EditorView.updateListener.of(update => {
            if (update.docChanged) opts.onChange(update.state.doc.toString());
        }));
    }

    const state = EditorState.create({ doc: opts.value ?? '', extensions });
    const view  = new EditorView({ state, parent: container });

    return {
        getValue: () => view.state.doc.toString(),
        setValue: (text) => view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: text },
        }),
        dispose: () => view.destroy(),
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 4 — Report Designer
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Mount the full WYSIWYG report designer into `container`.
 *
 * Four zones: top bar (page tabs, script toggle, save/cancel), left sidebar
 * (visual palette, datasets, component tree), canvas (12-col CSS grid),
 * properties panel (selected-visual editor).
 *
 * @param {HTMLElement} container
 * @param {Object}      [opts]
 * @param {Object|null} [opts.designState=null]   Parsed DesignState JSON (null = new report).
 * @param {number|null} [opts.reportId=null]       Existing report ID for save.
 * @param {string}      [opts.reportName='New Report']
 * @param {number|null} [opts.folderId=null]
 * @param {string}      [opts.apiBase='']          Portal API base URL.
 * @param {Function}    [opts.authFetch]            (url, fetchInit) → Promise<Response>. Falls back to plain fetch.
 * @param {Function}    [opts.onSaveScript]         (script: string) → Promise. VS Code host override — bypasses portal API save.
 * @param {Function}    [opts.onSave]               Called after successful save.
 * @param {Function}    [opts.onCancel]             Called on back/cancel.
 * @returns {{ dispose: Function }}
 */
export function createDesigner(container, opts = {}) {

    // ── State ────────────────────────────────────────────────────────────────
    const state = opts.designState
        ? JSON.parse(JSON.stringify(opts.designState))
        : { pages: [], datasets: [] };
    if (!state.pages?.length)
        state.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
    if (!state.datasets) state.datasets = [];

    let pageIdx     = 0;
    let selVisualId = null;
    let scriptEditor = null;
    let reportName  = opts.reportName ?? 'New Report';
    const reportId  = opts.reportId   ?? null;
    const folderId  = opts.folderId   ?? null;
    const apiBase   = opts.apiBase    ?? '';
    const _fetch    = opts.authFetch  ?? ((url, o) => fetch(url, o));

    // ── Visual type registry ──────────────────────────────────────────────────
    const VTYPES = [
        ['BAR','#3b82f6'],['LINE','#06b6d4'],['AREA','#0891b2'],['PIE','#8b5cf6'],
        ['SCATTER','#6366f1'],['GAUGE','#a855f7'],['FUNNEL','#d946ef'],['TREEMAP','#ec4899'],
        ['TABLE','#64748b'],['CARD','#10b981'],['TEXT','#f59e0b'],['SLICER','#f97316'],
    ];
    const VCOLOR = Object.fromEntries(VTYPES.map(([t, c]) => [t, c]));
    const ROLES  = ['X', 'Y', 'VALUE', 'CATEGORY', 'SERIES', 'LABEL', 'TOOLTIP'];

    // ── API helper ────────────────────────────────────────────────────────────
    async function apiJson(url, method = 'GET', body = null) {
        const init = { method, headers: {} };
        if (body !== null) {
            init.headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(body);
        }
        const res = await _fetch(apiBase + url, init);
        if (!res) return null;
        if (!res.ok) { const e = await res.json().catch(() => ({})); throw new Error(e.error || res.statusText); }
        if (res.status === 204) return null;
        return res.json();
    }

    // ── Utilities ─────────────────────────────────────────────────────────────
    const uid       = () => 'v_' + Math.random().toString(36).slice(2, 8);
    const curPage   = () => state.pages[pageIdx];
    const curVis    = () => curPage()?.visuals ?? [];
    const findVis   = id => { for (const p of state.pages) for (const v of p.visuals ?? []) if (v.id === id) return v; return null; };
    const maxRow    = vs => vs.length ? Math.max(...vs.map(v => (v.gridRow || 1) + (v.gridRowSpan || 4) - 1)) : 0;
    const esc       = s  => String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

    // ── DOM scaffold ──────────────────────────────────────────────────────────
    container.innerHTML = '';
    const root = document.createElement('div');
    root.className = 'etlsql-designer';
    container.appendChild(root);

    // Top bar
    const topbar = document.createElement('div');
    topbar.className = 'etlsql-designer-topbar';
    topbar.innerHTML = `
        <button class="btn btn-sm" id="dsgn-back">← Reports</button>
        <input id="dsgn-name" class="etlsql-dsgn-name-input" type="text" placeholder="Report name" />
        <div class="etlsql-designer-pages" id="dsgn-pages"></div>
        <button class="btn btn-sm" id="dsgn-add-page">+ Page</button>
        <button class="btn btn-sm" id="dsgn-script-toggle">⌨ Script</button>
        <span class="etlsql-preview-badge">Preview: VS Code only</span>
        <button class="btn btn-sm btn-primary" id="dsgn-save">Save</button>
        <button class="btn btn-sm" id="dsgn-cancel">Cancel</button>
    `;
    root.appendChild(topbar);
    topbar.querySelector('#dsgn-name').value = reportName;

    // Sidebar
    const sidebar = document.createElement('div');
    sidebar.className = 'etlsql-designer-sidebar';
    sidebar.innerHTML = `
        <div class="etlsql-dsgn-section">
            <div class="etlsql-dsgn-section-hdr">Add Visual</div>
            <div class="etlsql-dsgn-palette" id="dsgn-palette"></div>
        </div>
        <div class="etlsql-dsgn-section">
            <div class="etlsql-dsgn-section-hdr">
                Datasets <button class="btn btn-xs" id="dsgn-add-ds">+</button>
            </div>
            <div id="dsgn-ds-list"></div>
        </div>
        <div class="etlsql-dsgn-section">
            <div class="etlsql-dsgn-section-hdr">On This Page</div>
            <div id="dsgn-tree"></div>
        </div>
    `;
    for (const [type, color] of VTYPES) {
        const btn = document.createElement('button');
        btn.className = 'etlsql-dsgn-palette-btn';
        btn.dataset.vtype = type;
        btn.textContent = type;
        btn.style.setProperty('--vc', color);
        sidebar.querySelector('#dsgn-palette').appendChild(btn);
    }
    root.appendChild(sidebar);

    // Canvas
    const canvasWrap = document.createElement('div');
    canvasWrap.className = 'etlsql-designer-canvas';
    const canvasGrid = document.createElement('div');
    canvasGrid.className = 'etlsql-dsgn-grid';
    canvasWrap.appendChild(canvasGrid);
    root.appendChild(canvasWrap);

    // Properties panel
    const propsPanel = document.createElement('div');
    propsPanel.className = 'etlsql-designer-props';
    root.appendChild(propsPanel);

    // Script overlay
    const scriptOverlay = document.createElement('div');
    scriptOverlay.className = 'etlsql-designer-script-overlay';
    scriptOverlay.innerHTML = `
        <div class="etlsql-designer-script-toolbar">
            <strong style="flex:1">Script</strong>
            <button class="btn btn-sm btn-primary" id="dsgn-script-apply">↺ Update Designer</button>
            <button class="btn btn-sm" id="dsgn-script-close">✕ Close</button>
        </div>
        <div class="etlsql-designer-script-body etlsql-editor-container" id="dsgn-script-host"></div>
    `;
    root.appendChild(scriptOverlay);

    // Save-as modal
    const saveModal = document.createElement('div');
    saveModal.className = 'etlsql-dsgn-modal-bg';
    saveModal.innerHTML = `
        <div class="etlsql-dsgn-modal-card">
            <div class="etlsql-dsgn-modal-hdr">Save Report</div>
            <label class="etlsql-dsgn-label">Name<input id="dsgn-modal-name" class="form-control" /></label>
            <label class="etlsql-dsgn-label" style="margin-top:8px">Folder ID (optional)
                <input id="dsgn-modal-folder" class="form-control" type="number" />
            </label>
            <div class="etlsql-dsgn-modal-actions">
                <button class="btn btn-sm" id="dsgn-modal-cancel">Cancel</button>
                <button class="btn btn-sm btn-primary" id="dsgn-modal-ok">Save</button>
            </div>
        </div>
    `;
    root.appendChild(saveModal);

    // ── Render ────────────────────────────────────────────────────────────────

    function renderPageTabs() {
        const strip = topbar.querySelector('#dsgn-pages');
        strip.innerHTML = '';
        state.pages.forEach((p, i) => {
            const tab = document.createElement('button');
            tab.className = 'etlsql-designer-page-tab' + (i === pageIdx ? ' active' : '');
            tab.textContent = p.name || `Page ${i + 1}`;
            tab.dataset.idx = String(i);
            strip.appendChild(tab);
        });
    }

    function renderCanvas() {
        canvasGrid.innerHTML = '';
        const visuals = curVis();
        if (!visuals.length) {
            const ph = document.createElement('div');
            ph.className = 'etlsql-dsgn-canvas-empty';
            ph.textContent = 'Click a visual type in the sidebar to add it here';
            canvasGrid.appendChild(ph);
            return;
        }
        const rows = maxRow(visuals) + 2;
        canvasGrid.style.gridTemplateRows = `repeat(${rows}, 60px)`;
        for (const v of visuals) {
            const card = document.createElement('div');
            card.className = 'etlsql-dsgn-visual-card' + (v.id === selVisualId ? ' selected' : '');
            card.dataset.vid = v.id;
            card.style.gridColumn = `${v.gridCol || 1} / span ${v.gridColSpan || 12}`;
            card.style.gridRow    = `${v.gridRow || 1} / span ${v.gridRowSpan || 4}`;
            card.style.setProperty('--vc', VCOLOR[v.type] || '#64748b');
            card.innerHTML = `
                <div class="etlsql-dsgn-vcard-badge">${v.type}</div>
                <div class="etlsql-dsgn-vcard-name">${esc(v.title || v.name)}</div>
                <button class="etlsql-dsgn-vcard-del" data-del="${v.id}">✕</button>
            `;
            canvasGrid.appendChild(card);
        }
    }

    function renderTree() {
        const tree = sidebar.querySelector('#dsgn-tree');
        tree.innerHTML = '';
        for (const v of curVis()) {
            const item = document.createElement('div');
            item.className = 'etlsql-dsgn-tree-item' + (v.id === selVisualId ? ' selected' : '');
            item.dataset.vid = v.id;
            item.textContent = `${v.name} (${v.type})`;
            tree.appendChild(item);
        }
    }

    function renderDatasets() {
        const list = sidebar.querySelector('#dsgn-ds-list');
        list.innerHTML = '';
        for (const ds of state.datasets) {
            const row = document.createElement('div');
            row.className = 'etlsql-dsgn-ds-item';
            row.innerHTML = `<span>#${esc(ds.name)}</span><button data-dsid="${ds.id}" title="Remove">✕</button>`;
            list.appendChild(row);
        }
    }

    function renderProps() {
        propsPanel.innerHTML = '';
        const v = selVisualId ? findVis(selVisualId) : null;
        if (!v) {
            propsPanel.innerHTML = '<p class="etlsql-dsgn-props-empty">Select a visual on the canvas to edit its properties.</p>';
            return;
        }
        const mappings = v.mappings || {};
        const dsOpts = state.datasets
            .map(d => `<option value="${esc(d.name)}"${v.dataset === d.name ? ' selected' : ''}>#${esc(d.name)}</option>`)
            .join('');

        propsPanel.innerHTML = `
            <div class="etlsql-dsgn-props-section">
                <div class="etlsql-dsgn-props-hdr">Properties</div>
                <label class="etlsql-dsgn-label">Name<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                <label class="etlsql-dsgn-label">Type
                    <select id="pp-type" class="form-control">
                        ${VTYPES.map(([t]) => `<option${v.type === t ? ' selected' : ''}>${t}</option>`).join('')}
                    </select>
                </label>
                <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                <label class="etlsql-dsgn-label">Dataset
                    <select id="pp-ds" class="form-control">
                        <option value="">— none —</option>${dsOpts}
                    </select>
                </label>
            </div>
            <div class="etlsql-dsgn-props-section">
                <div class="etlsql-dsgn-props-hdr">Mappings</div>
                ${ROLES.map(r => `
                    <div class="etlsql-dsgn-map-row">
                        <span>${r}</span>
                        <input type="text" data-role="${r}" class="form-control" value="${esc(mappings[r] || '')}" placeholder="column">
                    </div>`).join('')}
            </div>
            <div class="etlsql-dsgn-props-section">
                <div class="etlsql-dsgn-props-hdr">Grid Position</div>
                <div class="etlsql-dsgn-grid4">
                    <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                    <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                    <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                    <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                </div>
                <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Visual</button>
            </div>
        `;

        const on = (sel, fn) => propsPanel.querySelector(sel)?.addEventListener('change', fn);
        on('#pp-name',  e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
        on('#pp-type',  e => { v.type  = e.target.value; renderCanvas(); renderTree(); });
        on('#pp-title', e => { v.title = e.target.value; renderCanvas(); });
        on('#pp-ds',    e => { v.dataset = e.target.value || null; });
        on('#pp-col',   e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
        on('#pp-row',   e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
        on('#pp-cspan', e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
        on('#pp-rspan', e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });

        for (const role of ROLES) {
            propsPanel.querySelector(`[data-role="${role}"]`)?.addEventListener('change', ev => {
                if (!v.mappings) v.mappings = {};
                if (ev.target.value) v.mappings[role] = ev.target.value;
                else delete v.mappings[role];
            });
        }
        propsPanel.querySelector('#pp-delete')?.addEventListener('click', () => deleteVisual(v.id));
    }

    function renderAll() {
        renderPageTabs();
        renderCanvas();
        renderTree();
        renderDatasets();
        renderProps();
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    function selectVisual(id) {
        selVisualId = id;
        renderCanvas();
        renderTree();
        renderProps();
    }

    function deleteVisual(id) {
        for (const page of state.pages) {
            const i = (page.visuals || []).findIndex(v => v.id === id);
            if (i >= 0) { page.visuals.splice(i, 1); break; }
        }
        if (selVisualId === id) selVisualId = null;
        renderCanvas();
        renderTree();
        renderProps();
    }

    function addVisual(type) {
        const page = curPage();
        if (!page.visuals) page.visuals = [];
        const newId = uid();
        page.visuals.push({
            id: newId,
            name: type.toLowerCase() + '_' + newId.slice(2),
            type,
            gridCol: 1,
            gridRow: maxRow(page.visuals) + 1,
            gridColSpan: 12,
            gridRowSpan: 4,
            title: '',
            dataset: null,
            mappings: {},
            options: {},
        });
        selVisualId = newId;
        renderCanvas();
        renderTree();
        renderProps();
    }

    function addPage() {
        const n = state.pages.length + 1;
        state.pages.push({ id: `p${n}_${Date.now()}`, name: `Page ${n}`, mode: 'Dashboard', visuals: [] });
        pageIdx = state.pages.length - 1;
        selVisualId = null;
        renderAll();
    }

    function addDataset() {
        const name = prompt('Dataset name (used as #name in rptsql):');
        if (!name?.trim()) return;
        state.datasets.push({ id: 'ds_' + uid(), name: name.trim(), query: 'SELECT 1 AS Placeholder' });
        renderDatasets();
        renderProps();
    }

    // ── Script overlay ────────────────────────────────────────────────────────

    async function openScript() {
        let text = '';
        try {
            const r = await apiJson('/api/designer/generate', 'POST', { designState: state });
            text = r?.script ?? '';
        } catch { text = '-- Failed to generate script\n'; }
        scriptOverlay.classList.add('active');
        const host = scriptOverlay.querySelector('#dsgn-script-host');
        host.innerHTML = '';
        scriptEditor = await createScriptEditor(host, { value: text });
    }

    function closeScript() {
        scriptOverlay.classList.remove('active');
        scriptEditor?.dispose();
        scriptEditor = null;
    }

    async function applyScript() {
        if (!scriptEditor) return;
        try {
            const r = await apiJson('/api/designer/parse', 'POST', { script: scriptEditor.getValue() });
            if (r?.designState?.pages?.length) {
                Object.assign(state, r.designState);
                if (!state.datasets) state.datasets = [];
                pageIdx = 0;
                selVisualId = null;
                closeScript();
                renderAll();
            } else {
                alert(r?.error || 'Could not parse script.');
            }
        } catch (e) { alert(e.message); }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    async function saveReport() {
        reportName = topbar.querySelector('#dsgn-name').value.trim() || reportName;
        try {
            const r = await apiJson('/api/designer/generate', 'POST', { designState: state });
            const script = r?.script ?? '';
            if (opts.onSaveScript) {
                await opts.onSaveScript(script);
                opts.onSave?.();
                return;
            }
            if (reportId) {
                await apiJson(`/api/reports/${reportId}/script-content`, 'PUT', { scriptText: script });
                opts.onSave?.();
            } else {
                saveModal.querySelector('#dsgn-modal-name').value   = reportName;
                saveModal.querySelector('#dsgn-modal-folder').value = folderId ?? '';
                saveModal._script = script;
                saveModal.style.display = 'flex';
            }
        } catch (e) { alert('Save failed: ' + e.message); }
    }

    async function saveAsNew() {
        const name   = saveModal.querySelector('#dsgn-modal-name').value.trim() || 'New Report';
        const folder = parseInt(saveModal.querySelector('#dsgn-modal-folder').value, 10) || null;
        const script = saveModal._script;
        try {
            const up = await apiJson('/api/scripts/upload', 'POST', { fileName: name + '.rptsql', scriptText: script });
            await apiJson('/api/reports', 'POST', {
                name, folderId: folder,
                scriptPath: up?.path ?? up?.scriptPath ?? name + '.rptsql',
                isPublished: false,
            });
            saveModal.style.display = 'none';
            opts.onSave?.();
        } catch (e) { alert('Save failed: ' + e.message); }
    }

    // ── Event wiring ──────────────────────────────────────────────────────────

    topbar.querySelector('#dsgn-back').addEventListener('click',    () => opts.onCancel?.());
    topbar.querySelector('#dsgn-cancel').addEventListener('click',  () => opts.onCancel?.());
    topbar.querySelector('#dsgn-save').addEventListener('click',    saveReport);
    topbar.querySelector('#dsgn-add-page').addEventListener('click', addPage);
    topbar.querySelector('#dsgn-name').addEventListener('change',   e => { reportName = e.target.value; });
    topbar.querySelector('#dsgn-script-toggle').addEventListener('click', () =>
        scriptOverlay.classList.contains('active') ? closeScript() : openScript());

    topbar.querySelector('#dsgn-pages').addEventListener('click', e => {
        const tab = e.target.closest('.etlsql-designer-page-tab');
        if (tab) { pageIdx = +tab.dataset.idx; selVisualId = null; renderAll(); }
    });

    sidebar.querySelector('#dsgn-palette').addEventListener('click', e => {
        const btn = e.target.closest('.etlsql-dsgn-palette-btn');
        if (btn) addVisual(btn.dataset.vtype);
    });

    canvasGrid.addEventListener('click', e => {
        const del = e.target.closest('[data-del]');
        if (del) { deleteVisual(del.dataset.del); return; }
        const card = e.target.closest('.etlsql-dsgn-visual-card');
        if (card) selectVisual(card.dataset.vid);
        else { selVisualId = null; renderCanvas(); renderTree(); renderProps(); }
    });

    sidebar.querySelector('#dsgn-tree').addEventListener('click', e => {
        const item = e.target.closest('.etlsql-dsgn-tree-item');
        if (item) selectVisual(item.dataset.vid);
    });

    sidebar.querySelector('#dsgn-add-ds').addEventListener('click', addDataset);
    sidebar.querySelector('#dsgn-ds-list').addEventListener('click', e => {
        const del = e.target.closest('[data-dsid]');
        if (del) { state.datasets = state.datasets.filter(d => d.id !== del.dataset.dsid); renderDatasets(); renderProps(); }
    });

    scriptOverlay.querySelector('#dsgn-script-close').addEventListener('click',  closeScript);
    scriptOverlay.querySelector('#dsgn-script-apply').addEventListener('click',  applyScript);
    saveModal.querySelector('#dsgn-modal-cancel').addEventListener('click', () => { saveModal.style.display = 'none'; });
    saveModal.querySelector('#dsgn-modal-ok').addEventListener('click', () => saveAsNew().catch(e => alert(e.message)));

    // ── Initial render ────────────────────────────────────────────────────────
    renderAll();

    return { dispose: () => { closeScript(); container.innerHTML = ''; } };
}
