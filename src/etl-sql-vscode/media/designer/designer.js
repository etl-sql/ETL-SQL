/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
 * Edit the canonical source, then run: .\scripts\sync-assets.ps1
 */
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

const _TYPE_COLOR = {
    dataset:     '#10b981',
    visual:      '#3b82f6',
    page:        '#8b5cf6',
    table:       '#64748b',
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
    return 'circle';
}

function _nodeSize(type) {
    return type === 'page' ? 44 : 36;
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

    const LAYER_H = 130;
    const NODE_W  = 200;
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

    const pos = _computeLayout(nodes, edges);

    const eNodes = nodes.map(n => ({
        id:         n.id,
        name:       n.label,
        x:          pos[n.id]?.x ?? 0,
        y:          pos[n.id]?.y ?? 0,
        symbol:     _nodeSymbol(n.type),
        symbolSize: _nodeSize(n.type),
        label:      { show: true, formatter: '{b}', fontSize: 11, overflow: 'truncate', width: 140 },
        itemStyle:  { color: _nodeColor(n.type), borderColor: 'transparent' },
        emphasis:   { itemStyle: { borderColor: '#fff', borderWidth: 2 } },
        tooltip:    {
            formatter: () => {
                const meta = n.meta ? Object.entries(n.meta)
                    .map(([k, v]) => `<br/><span style="color:#94a3b8">${k}:</span> ${v}`)
                    .join('') : '';
                return `<strong>${n.label}</strong><br/>
                    <span style="color:#94a3b8">type:</span> ${n.type ?? ''}${meta}`;
            },
        },
        _meta: n.meta,
    }));

    const eEdges = (edges ?? []).map(e => ({
        source:     e.source,
        target:     e.target,
        label:      e.label ? { show: true, formatter: e.label, fontSize: 10, color: '#94a3b8' } : { show: false },
        lineStyle:  { color: '#94a3b8', width: 1.5 },
    }));

    const chart = ec.init(container, null, { renderer: 'canvas' });

    chart.setOption({
        tooltip: { show: true, confine: true },
        series: [{
            type:         'graph',
            layout:       'none',
            nodes:        eNodes,
            edges:        eEdges,
            roam:         true,
            zoom:         0.9,
            center:       ['50%', '50%'],
            edgeSymbol:   ['none', 'arrow'],
            edgeSymbolSize: 8,
            lineStyle:    { curveness: 0.15 },
            label:        { position: 'inside', color: '#fff' },
            emphasis:     { focus: 'adjacency' },
        }],
    });

    if (options.onNodeClick) {
        chart.on('click', params => {
            if (params.dataType === 'node')
                options.onNodeClick(params.data.id, params.data._meta);
        });
    }

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
 * Renders a four-zone shell (top bar, visual picker sidebar, canvas, properties
 * panel) and wires up the Script ↔ Designer toggle.
 *
 * @param {HTMLElement} container
 * @param {Object}      [opts]
 * @param {Object|null} [opts.designState=null]  Parsed DesignState JSON (null = new report).
 * @param {string}      [opts.apiBase='']        Portal API base URL (empty = same origin).
 * @param {string}      [opts.host='portal']     'portal' | 'vscode'
 * @param {Function}    [opts.onSave]            Called with DesignState on save.
 * @param {Function}    [opts.onCancel]          Called when user cancels.
 * @returns {{ dispose: Function }}
 */
export function createDesigner(container, opts = {}) {
    // Implemented in Phase 4.
    void container; void opts;
    return { dispose: () => {} };
}
