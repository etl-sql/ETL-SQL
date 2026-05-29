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
    // Implemented in Phase 2.
    void container; void nodes; void edges; void options;
    return {
        dispose: () => {},
        resize: () => {},
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 3 — Script Editor
// ─────────────────────────────────────────────────────────────────────────────

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
    // Implemented in Phase 3.
    void container; void opts;
    return {
        getValue: () => opts.value ?? '',
        setValue: (_v) => {},
        dispose: () => {},
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
