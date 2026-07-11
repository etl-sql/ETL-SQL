/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
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

    const LAYER_H    = 300;
    const SUB_ROW_H  = 180;
    const NODE_W     = 360;
    const MAX_PER_ROW = 6;

    const pos = {};
    let yBase = 0;
    const sortedLayers = Object.keys(byLayer).map(Number).sort((a, b) => a - b);
    for (const l of sortedLayers) {
        const layerIds = byLayer[l];
        const count    = layerIds.length;
        const numRows  = Math.ceil(count / MAX_PER_ROW);
        layerIds.forEach((id, i) => {
            const row        = Math.floor(i / MAX_PER_ROW);
            const colInRow   = i % MAX_PER_ROW;
            const rowCount   = Math.min(MAX_PER_ROW, count - row * MAX_PER_ROW);
            pos[id] = {
                x: (colInRow - (rowCount - 1) / 2) * NODE_W,
                y: yBase + row * SUB_ROW_H,
            };
        });
        yBase += (numRows - 1) * SUB_ROW_H + LAYER_H;
    }
    return pos;
}

/**
 * Union of a node's ancestors and descendants over directed edges — the lineage
 * path that flows through it. Drives focus mode: everything else is dimmed.
 * Returns a Set of node ids to keep lit (always includes `rootId`).
 */
function _lineageReach(rootId, allEdges, allNodes) {
    const down = {}, up = {};
    for (const e of allEdges) {
        (down[e.source] ??= []).push(e.target);
        (up[e.target]   ??= []).push(e.source);
    }
    const keep = new Set([rootId]);
    const walk = (adj) => {
        const stack = [rootId];
        while (stack.length) {
            const id = stack.pop();
            for (const nxt of (adj[id] ?? [])) if (!keep.has(nxt)) { keep.add(nxt); stack.push(nxt); }
        }
    };
    walk(down);  // descendants
    walk(up);    // ancestors
    // Keep expanded column children whose parent node is in focus.
    for (const n of allNodes) if (n.meta?.parent && keep.has(n.meta.parent)) keep.add(n.id);
    return keep;
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
 * @returns {{ dispose: Function, resize: Function, showDetail: Function }}
 *   dispose() — destroys the ECharts instance and removes DOM listeners
 *   resize()  — re-fits the chart to the current container size (call on panel resize)
 *   showDetail(id) — opens a node detail panel programmatically (used by tests/sandbox)
 */
export function renderDag(container, { nodes, edges }, options = {}) {

    if (!nodes?.length) {
        container.innerHTML = '<div class="etlsql-dag-empty">No structure data available.</div>';
        return { dispose: () => {}, resize: () => {} };
    }

    const hiddenTypes    = new Set();   // node types toggled off via the filter chips
    const collapsedPages = new Set();   // page ids folded into nodes (Collapse-pages button)
    let   soloedPage     = null;   // page id drilled into — only its slice is shown
    let   focusedNode    = null;   // node id whose lineage is isolated, or null
    let   focusSet       = null;   // Set of node ids kept lit while focused
    let   hasInitializedView = false;
    let   viewDimensionsValid = false;
    let   currentZoom    = 1;
    let   currentCenter  = [0, 0];
    let   lastGraph      = null;   // most recent { allNodes, allEdges, pos } for search/minimap
    let   searchMatches  = [];     // node ids matching the current search term
    let   searchIdx      = -1;     // index into searchMatches of the current jump target

    // Page→visual membership, derived once from the base edges.
    const _nodeById    = Object.fromEntries(nodes.map(n => [n.id, n]));
    const pageOfVisual = {};   // visualId -> pageId
    const pageChildren = {};   // pageId   -> [visualId, ...]
    for (const e of (edges ?? [])) {
        const s = _nodeById[e.source], t = _nodeById[e.target];
        if (s?.type === 'page' && t?.type === 'visual') {
            pageOfVisual[e.target] = e.source;
            (pageChildren[e.source] ??= []).push(e.target);
        }
    }
    const childCount = id => (pageChildren[id]?.length ?? 0);

    // Solo a page: keep the page, its visuals, and every visual's upstream data
    // lineage — and nothing else. Reuses the focus-mode reachability walk.
    function soloKeep(pageId) {
        const keep = new Set([pageId]);
        for (const v of (pageChildren[pageId] ?? [])) {
            keep.add(v);
            for (const id of _lineageReach(v, edges ?? [], nodes)) keep.add(id);
        }
        return keep;
    }

    function buildGraph() {
        // 1. Type filters — hiding a type re-fits the rest to fill the canvas.
        let baseNodes = nodes.filter(n => !hiddenTypes.has(n.type));

        // 2. Solo — drill into one page: keep only its slice, hide everything else.
        if (soloedPage) {
            const keep = soloKeep(soloedPage);
            baseNodes = baseNodes.filter(n => keep.has(n.id));
        }

        // 3. Collapse pages — fold each collapsed page's visuals into the page
        //    node; their data-source edges roll up to the page so lineage stays
        //    visible even when the report half is folded away. The soloed page is
        //    never folded (you're drilling into it, so its visuals must show).
        const collapsedVisuals = new Set();
        if (collapsedPages.size) {
            for (const vid in pageOfVisual) {
                const pid = pageOfVisual[vid];
                if (collapsedPages.has(pid) && pid !== soloedPage) collapsedVisuals.add(vid);
            }
            baseNodes = baseNodes.filter(n => !collapsedVisuals.has(n.id));
        }

        // Annotate page nodes with their child count (and a collapsed label).
        baseNodes = baseNodes.map(n => {
            if (n.type !== 'page') return n;
            const kids = childCount(n.id);
            const collapsed = collapsedPages.has(n.id) && n.id !== soloedPage;
            return { ...n, label: collapsed && kids ? `${n.label} (${kids})` : n.label, _kids: kids, _collapsed: collapsed, _soloed: n.id === soloedPage };
        });

        const visibleIds = new Set(baseNodes.map(n => n.id));

        // 3. Edges — roll collapsed-visual targets up to their page, drop the rest.
        const allEdges = [];
        const seenEdge = new Set();
        for (const e of (edges ?? [])) {
            let source = e.source, target = e.target, label = e.label;
            if (collapsedVisuals.has(source)) continue;            // visuals have no outgoing lineage
            if (collapsedVisuals.has(target)) {
                const pid = pageOfVisual[target];
                if (!pid || source === pid) continue;              // the page→visual edge itself
                target = pid; label = null;                        // source → page (aggregated)
            }
            if (!visibleIds.has(source) || !visibleIds.has(target)) continue;
            const key = JSON.stringify([source, target]);
            if (seenEdge.has(key)) continue;
            seenEdge.add(key);
            allEdges.push({ source, target, label });
        }

        const allNodes = [...baseNodes];
        const pos = _computeLayout(baseNodes, allEdges);

        // Column-level detail is shown in the side panel on double-click, not as
        // in-graph nodes (keeps the graph legible at scale).

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
                color: (focusSet && !focusSet.has(n.id)) ? 'rgba(148,163,184,0.25)' : '#fff',
            },
            itemStyle: (() => {
                const dim    = focusSet && !focusSet.has(n.id);
                const isRoot = n.id === focusedNode;
                return {
                    color:       _nodeColor(n.type),
                    opacity:     dim ? 0.12 : 1,
                    borderColor: isRoot ? '#fff'
                        : (n._collapsed || n._soloed ? '#c4b5fd'
                        : ((n.meta?.columns?.length || n.meta?.mappings?.length) ? '#10b981' : 'transparent')),
                    borderWidth: isRoot ? 3 : 2,
                };
            })(),
            emphasis:  { itemStyle: { borderColor: '#fff', borderWidth: 2 } },
            tooltip:   {
                formatter: () => {
                    const nCols = n.meta?.columns?.length;
                    const nMaps = n.meta?.mappings?.length;
                    const detailHint = (nCols || nMaps)
                        ? `<br/><span style="color:#10b981">▤ double-click for ${nCols ? `${nCols} column${nCols === 1 ? '' : 's'}` : `${nMaps} field${nMaps === 1 ? '' : 's'}`}</span>`
                        : '';
                    const pageHint = (n.type === 'page' && n._kids)
                        ? `<br/><span style="color:#c4b5fd">${n._soloed ? '◳ double-click to show all' : `▣ double-click to solo this page (${n._kids} visual${n._kids === 1 ? '' : 's'})`}</span>`
                        : '';
                    const focusHint = `<br/><span style="color:#64748b">${n.id === focusedNode ? 'click to clear focus' : 'click to isolate lineage'}</span>`;
                    const meta = n.meta ? Object.entries(n.meta)
                        .filter(([k, v]) => !['columns', 'colEdges', 'mappings'].includes(k) && (typeof v !== 'object'))
                        .map(([k, v]) => `<br/><span style="color:#94a3b8">${_h(k)}:</span> ${_h(v)}`)
                        .join('') : '';
                    return `<strong>${_h(n.label)}</strong><br/><span style="color:#94a3b8">type:</span> ${_h(n.type)}${meta}${detailHint}${pageHint}${focusHint}`;
                },
            },
            _meta: n.meta,
            _type: n.type,
        }));

        const eEdges = allEdges.map(e => {
            const dim = focusSet && !(focusSet.has(e.source) && focusSet.has(e.target));
            return {
                source:    e.source,
                target:    e.target,
                label:     (e.label && !dim) ? { show: true, formatter: e.label, fontSize: 10, color: '#94a3b8' } : { show: false },
                lineStyle: {
                    opacity: dim ? 0.06 : 0.9,
                    color: e.source?.includes('__col__') || e.target?.includes('__col__') ? '#cbd5e1' : '#94a3b8',
                    width: e.source?.includes('__col__') ? 1 : 1.5,
                    type:  e.source?.includes('__col__') ? 'dashed' : 'solid',
                },
            };
        });

        return { eNodes, eEdges };
    }

    // Lay the container out as a column: a chrome toolbar on top, the ECharts
    // canvas filling the rest. The canvas gets its own measurable div; chrome
    // (filter chips, focus badge) lives in the toolbar.
    container.style.position = container.style.position || 'relative';
    container.innerHTML = '';
    container.style.display = 'flex';
    container.style.flexDirection = 'column';
    container.classList.add('etlsql-dag-container');

    const toolbar = document.createElement('div');
    toolbar.className = 'etlsql-dag-toolbar';

    const chips = document.createElement('div');
    chips.className = 'etlsql-dag-chips';
    toolbar.appendChild(chips);

    // Search box — find a node by label, Enter cycles matches.
    const search = document.createElement('div');
    search.className = 'etlsql-dag-search';
    const searchInput = document.createElement('input');
    searchInput.type = 'search';
    searchInput.placeholder = 'Find node…';
    searchInput.setAttribute('aria-label', 'Find node');
    const searchCount = document.createElement('span');
    searchCount.className = 'etlsql-dag-search-count';
    search.append(searchInput, searchCount);
    toolbar.appendChild(search);

    const soloBadge = document.createElement('button');
    soloBadge.type = 'button';
    soloBadge.className = 'etlsql-dag-focusbadge etlsql-dag-solobadge';
    soloBadge.style.display = 'none';
    soloBadge.addEventListener('click', () => { soloedPage = null; render(); });
    toolbar.appendChild(soloBadge);

    const badge = document.createElement('button');
    badge.type = 'button';
    badge.className = 'etlsql-dag-focusbadge';
    badge.style.display = 'none';
    badge.addEventListener('click', () => { focusedNode = null; render(); });
    toolbar.appendChild(badge);
container.appendChild(toolbar);

    // Body = canvas (fills) + a collapsible detail panel on the right.
    const body = document.createElement('div');
    body.className = 'etlsql-dag-body';
    container.appendChild(body);

    // Setup Card-based Lineage viewport and Canvas
    const chartDiv = document.createElement('div');
    chartDiv.className = 'etlsql-dag-canvas';
    chartDiv.style.position = 'relative';
    chartDiv.style.overflow = 'hidden';
    chartDiv.style.background = '#090d16';
    body.appendChild(chartDiv);

    const viewport = document.createElement('div');
    viewport.className = 'etlsql-dag-viewport';
    chartDiv.appendChild(viewport);

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'etlsql-dag-svg');
    viewport.appendChild(svg);

    const badgeContainer = document.createElement('div');
    badgeContainer.className = 'etlsql-dag-badge-container';
    viewport.appendChild(badgeContainer);

    let panX = 0;
    let panY = 0;
    let activeHighlightNode = null;
    let activeColumnPath = null;
    let activeColumnPathSet = null;
    let piiComplianceHighlight = false;
    const draggedPositions = {};
    let hasInitializedView = false;
    let viewDimensionsValid = false;

    const panel = document.createElement('div');
    panel.className = 'etlsql-dag-panel';
    panel.style.display = 'none';
    body.appendChild(panel);

    // ── Floating Zoom Controls (+, -, Reset) ──────────────────────────────
    const zoomControls = document.createElement('div');
    zoomControls.className = 'etlsql-dag-zoom-controls';

    const btnIn = document.createElement('button');
    btnIn.type = 'button';
    btnIn.className = 'etlsql-dag-zoom-btn';
    btnIn.innerHTML = '&#43;'; // +
    btnIn.title = 'Zoom In';
    btnIn.setAttribute('aria-label', 'Zoom In');
    btnIn.addEventListener('click', () => {
        currentZoom = Math.min(2.0, currentZoom * 1.25);
        updateViewportFromCenter();
        updateConnections();
        drawMinimap();
    });

    const btnOut = document.createElement('button');
    btnOut.type = 'button';
    btnOut.className = 'etlsql-dag-zoom-btn';
    btnOut.innerHTML = '&minus;'; // −
    btnOut.title = 'Zoom Out';
    btnOut.setAttribute('aria-label', 'Zoom Out');
    btnOut.addEventListener('click', () => {
        currentZoom = Math.max(0.08, currentZoom / 1.25);
        updateViewportFromCenter();
        updateConnections();
        drawMinimap();
    });

    const btnReset = document.createElement('button');
    btnReset.type = 'button';
    btnReset.className = 'etlsql-dag-zoom-btn';
    btnReset.innerHTML = '&#8634;'; // ⟲
    btnReset.title = 'Reset View';
    btnReset.setAttribute('aria-label', 'Reset View');
    btnReset.addEventListener('click', () => {
        hasInitializedView = false;
        render();
    });

    zoomControls.append(btnIn, btnOut, btnReset);
    body.appendChild(zoomControls);

    // ── Zero-Trust PII compliance toggle button ───────────────────────────
    const piiBtn = document.createElement('button');
    piiBtn.type = 'button';
    piiBtn.className = 'etlsql-dag-focusbadge';
    piiBtn.style.display = 'inline-block';
    piiBtn.style.background = '#1e293b';
    piiBtn.style.borderColor = '#ef4444';
    piiBtn.style.color = '#ef4444';
    piiBtn.style.borderWidth = '1px';
    piiBtn.style.borderStyle = 'solid';
    piiBtn.style.marginLeft = '8px';
    piiBtn.innerHTML = '🔒 PII';
    piiBtn.title = 'Highlight PII Compliance';
    piiBtn.addEventListener('click', () => {
        piiComplianceHighlight = !piiComplianceHighlight;
        if (piiComplianceHighlight) {
            piiBtn.style.background = '#991b1b';
            piiBtn.style.color = '#fff';
            focusedNode = null;
            activeColumnPath = null;
            activeColumnPathSet = null;
        } else {
            piiBtn.style.background = '#1e293b';
            piiBtn.style.color = '#ef4444';
        }
        render();
    });
    toolbar.appendChild(piiBtn);

    // ── Detail panel (columns for tables, field mappings for charts) ─────────
    const _ROLE_LABEL = {
        XAXIS: 'x-axis', YAXIS: 'y-axis', VALUES: 'values', SERIES: 'series',
        CATEGORY: 'category', FILTER: 'filter', COLUMN: 'column', SIZE: 'size',
        COLOR: 'color', LABEL: 'label',
    };
    const _el = (tag, cls) => { const e = document.createElement(tag); if (cls) e.className = cls; return e; };

    function closePanel() {
        panel.style.display = 'none';
        updateViewportFromCenter();
        updateConnections();
        drawMinimap();
    }

    function renderPanelList(title, items, emptyText) {
        const h = _el('div', 'etlsql-dag-panel-h');
        h.textContent = title;
        panel.appendChild(h);
        if (!items || !items.length) {
            const e = _el('div', 'etlsql-dag-panel-empty');
            e.textContent = emptyText;
            panel.appendChild(e);
            return;
        }
        const ul = _el('ul', 'etlsql-dag-panel-list');
        for (const it of items) {
            const li = _el('li', 'etlsql-dag-panel-li');
            if (it.k) { const k = _el('span', 'etlsql-dag-panel-k'); k.textContent = `${it.k}:`; li.append(k); }
            const v = _el('span', 'etlsql-dag-panel-v'); v.textContent = it.v; li.append(v);
            if (it.from) { const f = _el('span', 'etlsql-dag-panel-from'); f.textContent = `← ${it.from}`; li.append(f); }
            ul.appendChild(li);
        }
        panel.appendChild(ul);
    }

    // Resolve a source table name (as recorded in lineage) to a graph node id.
    function findTableNodeId(tableName) {
        if (_nodeById[`ds:${tableName}`])    return `ds:${tableName}`;
        if (_nodeById[`table:${tableName}`]) return `table:${tableName}`;
        const hit = nodes.find(n => (n.type === 'table' || n.type === 'dataset') && n.label === tableName);
        return hit ? hit.id : null;
    }

    function getConnectionDialect(node) {
        if (node.type === 'dataset') return 'Dataset';
        if (node.type === 'page' || node.type === 'visual') return null;
        
        const label = node.label;
        if (label.startsWith('Raw')) {
            if (label.includes('Sales') || label.includes('Returns')) return 'MSSQL';
            if (label.includes('Discount')) return 'Postgres';
            if (label.includes('Target')) return 'Snowflake';
            if (label.includes('Pipeline')) return 'CSV';
            if (label.includes('Inventory')) return 'Oracle';
            if (label.includes('Shipment')) return 'SFTP';
            return 'MSSQL';
        }
        if (label.startsWith('Stg') || label.startsWith('Enriched') || label.includes('Rollup') || label.includes('Plan') || label.includes('Log') || label.startsWith('Fact')) {
            return 'Engine';
        }
        return 'Database';
    }

    function _traceColumnPath(nodeId, colName, pathSet, direction = 'both') {
        const colKey = `${nodeId}__col__${colName}`;
        if (pathSet.has(colKey)) return;
        pathSet.add(colKey);
        pathSet.add(nodeId);

        const node = _nodeById[nodeId];
        if (!node) return;

        if (direction === 'both' || direction === 'up') {
            const cl = node.meta?.columnLineage?.[colName];
            if (cl?.sources) {
                cl.sources.forEach(src => {
                    const srcId = findTableNodeId(src.table);
                    if (srcId) {
                        _traceColumnPath(srcId, src.column, pathSet, 'up');
                    }
                });
            }
        }

        if (direction === 'both' || direction === 'down') {
            nodes.forEach(other => {
                if (other.meta?.columnLineage) {
                    Object.entries(other.meta.columnLineage).forEach(([otherCol, lin]) => {
                        if (lin.sources) {
                            lin.sources.forEach(src => {
                                const isMatch = src.table === node.label && src.column === colName;
                                if (isMatch) {
                                    _traceColumnPath(other.id, otherCol, pathSet, 'down');
                                }
                            });
                        }
                    });
                }
            });

            nodes.forEach(other => {
                if (other.type === 'visual' && other.meta?.mappings) {
                    const hasEdge = edges.some(e => e.source === nodeId && e.target === other.id);
                    if (hasEdge) {
                        other.meta.mappings.forEach(m => {
                            const cleanCol = m.column.replace(/.*\((.*)\)/, '$1');
                            if (cleanCol === colName) {
                                pathSet.add(other.id);
                                pathSet.add(`${other.id}__map__${m.role}`);
                            }
                        });
                    }
                }
            });
        }
    }

    function updateUIForPathIsolation() {
        nodes.forEach(n => {
            const card = document.getElementById(`node__${n.id}`);
            if (!card) return;

            if (activeColumnPathSet) {
                if (activeColumnPathSet.has(n.id)) {
                    card.style.opacity = '1';
                    card.style.borderColor = '#10b981';
                    
                    if (n.meta?.columns) {
                        n.meta.columns.forEach(c => {
                            const row = document.getElementById(`${n.id}__col__${c}`);
                            if (row) {
                                const inPath = activeColumnPathSet.has(`${n.id}__col__${c}`);
                                row.style.opacity = inPath ? '1' : '0.12';
                                const labelSpan = row.querySelector('.col-label-span');
                                if (labelSpan) labelSpan.style.color = inPath ? '#34d399' : '#4b5563';
                            }
                        });
                    }
                    if (n.meta?.mappings) {
                        n.meta.mappings.forEach(m => {
                            const row = document.getElementById(`${n.id}__map__${m.role}`);
                            if (row) {
                                const inPath = activeColumnPathSet.has(`${n.id}__map__${m.role}`);
                                row.style.opacity = inPath ? '1' : '0.12';
                                const labelSpan = row.querySelector('.col-label-span');
                                if (labelSpan) labelSpan.style.color = inPath ? '#34d399' : '#4b5563';
                            }
                        });
                    }
                } else {
                    card.style.opacity = '0.08';
                    card.style.borderColor = '#1f2937';
                }
            } else if (piiComplianceHighlight) {
                let nodeHasPii = false;
                if (n.meta?.columns) {
                    n.meta.columns.forEach(c => {
                        const cl = n.meta.columnLineage?.[c];
                        if (cl?.tags?.pii) nodeHasPii = true;
                    });
                }

                if (nodeHasPii) {
                    card.style.opacity = '1';
                    card.style.borderColor = '#ef4444';
                    
                    if (n.meta?.columns) {
                        n.meta.columns.forEach(c => {
                            const row = document.getElementById(`${n.id}__col__${c}`);
                            if (row) {
                                const cl = n.meta.columnLineage?.[c];
                                const isPii = !!cl?.tags?.pii;
                                row.style.opacity = isPii ? '1' : '0.15';
                                const labelSpan = row.querySelector('.col-label-span');
                                if (labelSpan) labelSpan.style.color = isPii ? '#f87171' : '#4b5563';
                            }
                        });
                    }
                } else {
                    card.style.opacity = '0.1';
                    card.style.borderColor = '#1f2937';
                }
            } else if (focusSet) {
                if (focusSet.has(n.id)) {
                    card.style.opacity = '1';
                    card.style.borderColor = '#4f46e5';
                } else {
                    card.style.opacity = '0.12';
                }
            } else {
                card.style.opacity = '1';
                card.style.borderColor = n.type === 'table' ? '#2563eb' : (n.type === 'dataset' ? '#7c3aed' : (n.type === 'visual' ? '#059669' : '#475569'));
                
                if (n.meta?.columns) {
                    n.meta.columns.forEach(c => {
                        const row = document.getElementById(`${n.id}__col__${c}`);
                        if (row) {
                            row.style.opacity = '1';
                            const labelSpan = row.querySelector('.col-label-span');
                            if (labelSpan) labelSpan.style.color = '#cbd5e1';
                        }
                    });
                }
                if (n.meta?.mappings) {
                    n.meta.mappings.forEach(m => {
                        const row = document.getElementById(`${n.id}__map__${m.role}`);
                        if (row) {
                            row.style.opacity = '1';
                            const labelSpan = row.querySelector('.col-label-span');
                            if (labelSpan) labelSpan.style.color = '#cbd5e1';
                        }
                    });
                }
            }
        });
    }

    const colConnections = [];
    nodes.forEach(n => {
        if (n.meta?.columnLineage) {
            Object.entries(n.meta.columnLineage).forEach(([tgtCol, lin]) => {
                if (lin.sources) {
                    lin.sources.forEach(src => {
                        const srcNode = nodes.find(x => x.label === src.table);
                        if (srcNode) {
                            colConnections.push({
                                from: `${srcNode.id}__col__${src.column}`,
                                to: `${n.id}__col__${tgtCol}`,
                                fromTable: srcNode.id,
                                toTable: n.id
                            });
                        }
                    });
                }
            });
        }
    });

    let activePaths = [];

    function updateConnections() {
        activePaths.forEach(p => p.remove());
        activePaths = [];
        badgeContainer.innerHTML = '';

        if (!lastGraph) return;

        const vRect = viewport.getBoundingClientRect();

        lastGraph.allEdges.forEach(e => {
            const fromNode = _nodeById[e.source];
            const toNode = _nodeById[e.target];

            if (!fromNode || !toNode) return;

            if (toNode.type === 'visual' && toNode.meta?.mappings && fromNode.meta?.columns) {
                let routedAny = false;
                toNode.meta.mappings.forEach(m => {
                    const cleanCol = m.column.replace(/.*\((.*)\)/, '$1');
                    if (fromNode.meta.columns.includes(cleanCol)) {
                        const fromEl = document.getElementById(`${e.source}__col__${cleanCol}`);
                        const toEl = document.getElementById(`${e.target}__map__${m.role}`);

                        if (fromEl && toEl) {
                            const fromPort = fromEl.querySelector('.port-right');
                            const toPort = toEl.querySelector('.port-left');
                            if (fromPort && toPort) {
                                const r1 = fromPort.getBoundingClientRect();
                                const r2 = toPort.getBoundingClientRect();
                                const x1 = (r1.left + r1.width / 2 - vRect.left) / currentZoom;
                                const y1 = (r1.top + r1.height / 2 - vRect.top) / currentZoom;
                                const x2 = (r2.left + r2.width / 2 - vRect.left) / currentZoom;
                                const y2 = (r2.top + r2.height / 2 - vRect.top) / currentZoom;

                                const inPath = activeColumnPathSet && activeColumnPathSet.has(`${e.source}__col__${cleanCol}`) && activeColumnPathSet.has(`${e.target}__map__${m.role}`);
                                const isDimmed = activeColumnPathSet && !inPath;
                                
                                const isHighlightActive = activeHighlightNode && (activeHighlightNode === e.source || activeHighlightNode === e.target);
                                const isDimmedHighlight = activeHighlightNode && !isHighlightActive;

                                const color = inPath ? '#06b6d4' : (isDimmed || isDimmedHighlight ? 'rgba(71,85,105,0.06)' : '#34d399');
                                const width = inPath ? 3.0 : (isDimmed || isDimmedHighlight ? 0.75 : 1.5);

                                const path = drawLink(x1, y1, x2, y2, color, width);
                                activePaths.push(path);
                                routedAny = true;

                                const edgeTransform = m.column.includes('(') ? m.column.split('(')[0] : 'SELECT';
                                drawEdgeBadge(x1, y1, x2, y2, edgeTransform, inPath, isDimmed || isDimmedHighlight);
                            }
                        }
                    }
                });
                if (routedAny) return;
            }

            const fromCard = document.getElementById(`node__${e.source}`);
            const toCard = document.getElementById(`node__${e.target}`);

            if (!fromCard || !toCard) return;

            const fromPort = fromCard.querySelector('.card-port-right');
            const toPort = toCard.querySelector('.card-port-left');

            if (!fromPort || !toPort) return;

            const r1 = fromPort.getBoundingClientRect();
            const r2 = toPort.getBoundingClientRect();

            const x1 = (r1.left + r1.width / 2 - vRect.left) / currentZoom;
            const y1 = (r1.top + r1.height / 2 - vRect.top) / currentZoom;
            const x2 = (r2.left + r2.width / 2 - vRect.left) / currentZoom;
            const y2 = (r2.top + r2.height / 2 - vRect.top) / currentZoom;

            const inPath = activeColumnPathSet && activeColumnPathSet.has(e.source) && activeColumnPathSet.has(e.target);
            const isDimmed = (activeColumnPathSet && !inPath) || (activeHighlightNode && activeHighlightNode !== e.source && activeHighlightNode !== e.target);
            
            const color = inPath ? '#06b6d4' : (isDimmed ? 'rgba(71,85,105,0.06)' : '#475569');
            const width = inPath ? 2.5 : (isDimmed ? 0.75 : 1.5);

            const path = drawLink(x1, y1, x2, y2, color, width);
            activePaths.push(path);

            if (e.label) {
                drawEdgeBadge(x1, y1, x2, y2, e.label, inPath, isDimmed);
            }
        });

        colConnections.forEach(c => {
            if (activeHighlightNode && activeHighlightNode !== c.fromTable && activeHighlightNode !== c.toTable) return;

            const inPath = activeColumnPathSet && activeColumnPathSet.has(c.from) && activeColumnPathSet.has(c.to);
            const isDimmed = activeColumnPathSet && !inPath;

            const fromEl = document.getElementById(c.from);
            const toEl = document.getElementById(c.to);

            if (!fromEl || !toEl) return;

            const fromPort = fromEl.querySelector('.port-right');
            const toPort = toEl.querySelector('.port-left');

            if (!fromPort || !toPort) return;

            const r1 = fromPort.getBoundingClientRect();
            const r2 = toPort.getBoundingClientRect();

            const x1 = (r1.left + r1.width / 2 - vRect.left) / currentZoom;
            const y1 = (r1.top + r1.height / 2 - vRect.top) / currentZoom;
            const x2 = (r2.left + r2.width / 2 - vRect.left) / currentZoom;
            const y2 = (r2.top + r2.height / 2 - vRect.top) / currentZoom;

            const color = inPath ? '#10b981' : (isDimmed ? 'rgba(16,185,129,0.04)' : (activeHighlightNode ? '#10b981' : 'rgba(16,185,129,0.35)'));
            const width = inPath ? 3.0 : (isDimmed ? 0.5 : (activeHighlightNode ? 2.5 : 1));

            const path = drawLink(x1, y1, x2, y2, color, width, !activeHighlightNode && !inPath);
            activePaths.push(path);

            const fromNode = _nodeById[c.toTable];
            const toColName = c.to.split('__col__')[1];
            const cl = fromNode?.meta?.columnLineage?.[toColName];
            if (cl?.transform && !isDimmed) {
                const transformLabel = cl.transform.includes('(') ? cl.transform.split('(')[0] : 'PASS';
                drawEdgeBadge(x1, y1, x2, y2, transformLabel, inPath, isDimmed);
            }
        });
    }

    function drawLink(x1, y1, x2, y2, color = '#64748b', width = 1.5, isDashed = false) {
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        const dx = Math.abs(x2 - x1) * 0.45;
        path.setAttribute('d', `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`);
        path.setAttribute('stroke', color);
        path.setAttribute('stroke-width', width);
        path.setAttribute('fill', 'none');
        path.setAttribute('opacity', '0.85');
        if (isDashed) {
            path.setAttribute('stroke-dasharray', '4 4');
        }
        svg.appendChild(path);
        return path;
    }

    function drawEdgeBadge(x1, y1, x2, y2, text, inPath, isDimmed) {
        if (!text) return;
        const mx = (x1 + x2) / 2;
        const my = (y1 + y2) / 2;

        const badge = document.createElement('div');
        badge.className = 'etlsql-dag-edge-badge';
        badge.style.left = `${mx}px`;
        badge.style.top = `${my}px`;
        
        badge.style.background = inPath ? '#0f766e' : (isDimmed ? 'rgba(17,24,39,0.05)' : '#1e293b');
        badge.style.border = inPath ? '1px solid #14b8a6' : (isDimmed ? '1px solid rgba(55,65,81,0.05)' : '1px solid #3b82f6');
        badge.style.color = inPath ? '#ccfbf1' : (isDimmed ? 'rgba(156,163,175,0.05)' : '#93c5fd');
        badge.style.opacity = isDimmed ? '0.1' : '1';
        badge.style.boxShadow = isDimmed ? 'none' : '0 2px 4px rgba(0,0,0,0.5)';
        badge.textContent = text;
        
        badgeContainer.appendChild(badge);
    }

    function updateViewportFromCenter() {
        const CX = chartDiv.clientWidth / 2;
        const CY = chartDiv.clientHeight / 2;
        panX = CX - currentCenter[0] * currentZoom;
        panY = CY - currentCenter[1] * currentZoom;
        viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${currentZoom})`;
    }

    // ── Mouse / Wheel Canvas Events ─────────────────────────────────────────
    chartDiv.addEventListener('wheel', e => {
        e.preventDefault();
        const rect = chartDiv.getBoundingClientRect();
        const mouseX = e.clientX - rect.left;
        const mouseY = e.clientY - rect.top;

        const CX = rect.width / 2;
        const CY = rect.height / 2;

        const vx = (mouseX - CX - panX) / currentZoom;
        const vy = (mouseY - CY - panY) / currentZoom;

        const factor = 1.15;
        if (e.deltaY < 0) {
            currentZoom = Math.min(2.0, currentZoom * factor);
        } else {
            currentZoom = Math.max(0.08, currentZoom / factor);
        }

        panX = mouseX - CX - vx * currentZoom;
        panY = mouseY - CY - vy * currentZoom;

        currentCenter = [
            (CX - panX) / currentZoom,
            (CY - panY) / currentZoom
        ];

        viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${currentZoom})`;
        updateConnections();
        drawMinimap();
    });

    let isPanning = false;
    let startX = 0;
    let startY = 0;

    chartDiv.addEventListener('mousedown', e => {
        if (e.target === chartDiv || e.target === viewport) {
            isPanning = true;
            startX = e.clientX - panX;
            startY = e.clientY - panY;
            chartDiv.style.cursor = 'grabbing';
        }
    });

    document.addEventListener('mousemove', e => {
        if (!isPanning) return;
        panX = e.clientX - startX;
        panY = e.clientY - startY;
        currentCenter = [
            (chartDiv.clientWidth / 2 - panX) / currentZoom,
            (chartDiv.clientHeight / 2 - panY) / currentZoom
        ];
        viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${currentZoom})`;
    });

    document.addEventListener('mouseup', () => {
        if (isPanning) {
            isPanning = false;
            chartDiv.style.cursor = 'default';
        }
    });

    // ── Search &goToNode flash helper ────────────────────────────────────────
    let _hlTimer = null;

    function recomputeMatches() {
        const t = searchInput.value.trim().toLowerCase();
        searchMatches = (t && lastGraph)
            ? lastGraph.allNodes.filter(n => String(n.label ?? '').toLowerCase().includes(t)).map(n => n.id)
            : [];
        if (searchIdx >= searchMatches.length) searchIdx = searchMatches.length - 1;
        updateSearchCount();
    }

    function updateSearchCount() {
        if (!searchInput.value.trim()) { searchCount.textContent = ''; searchCount.classList.remove('is-empty'); return; }
        searchCount.textContent = searchMatches.length ? `${searchIdx + 1}/${searchMatches.length}` : 'none';
        searchCount.classList.toggle('is-empty', searchMatches.length === 0);
    }

    function nextMatch() {
        if (!searchMatches.length) return;
        searchIdx = (searchIdx + 1) % searchMatches.length;
        updateSearchCount();
        goToNode(searchMatches[searchIdx]);
    }

    function goToNode(id) {
        if (!lastGraph) return;
        const p = lastGraph.pos[id];
        if (!p) return;
        currentZoom = Math.max(currentZoom, 1.25);
        currentCenter = [p.x, p.y];
        render();

        const card = document.getElementById(`node__${id}`);
        if (card) {
            card.style.boxShadow = '0 0 40px #ef4444';
            card.style.borderColor = '#ef4444';
            clearTimeout(_hlTimer);
            _hlTimer = setTimeout(() => {
                card.style.boxShadow = '';
                card.style.borderColor = '';
            }, 1800);
        }
    }

    searchInput.addEventListener('input', () => { recomputeMatches(); searchIdx = -1; nextMatch(); });
    searchInput.addEventListener('keydown', e => {
        if (e.key === 'Enter')       { e.preventDefault(); nextMatch(); }
        else if (e.key === 'Escape') { searchInput.value = ''; recomputeMatches(); }
    });

    // ── Minimap ──────────────────────────────────────────────────────────────
    const MINI_W = 190, MINI_H = 130, MINI_PAD = 8;
    const minimapWrapper = document.createElement('div');
    minimapWrapper.className = 'etlsql-dag-minimap-wrapper is-minimized';

    const minimapToggle = document.createElement('button');
    minimapToggle.type = 'button';
    minimapToggle.className = 'etlsql-dag-minimap-toggle';
    minimapToggle.innerHTML = '🗺️';
    minimapToggle.title = 'Toggle Minimap';
    minimapToggle.setAttribute('aria-label', 'Toggle Minimap');
    minimapWrapper.appendChild(minimapToggle);

    const miniCanvas = document.createElement('canvas');
    miniCanvas.className = 'etlsql-dag-minimap';
    miniCanvas.width = MINI_W;
    miniCanvas.height = MINI_H;
    miniCanvas.title = 'Overview — click to recentre';
    minimapWrapper.appendChild(miniCanvas);

    chartDiv.appendChild(minimapWrapper);

    minimapToggle.addEventListener('click', (e) => {
        e.stopPropagation();
        minimapWrapper.classList.toggle('is-minimized');
        if (!minimapWrapper.classList.contains('is-minimized')) {
            drawMinimap();
        }
    });

    const miniCtx = miniCanvas.getContext('2d');
    let _miniTx = null;   // { scale, minX, minY, offX, offY } for click→data mapping

    function drawMinimap() {
        miniCtx.clearRect(0, 0, MINI_W, MINI_H);
        if (!lastGraph) { _miniTx = null; return; }
        const pts = lastGraph.allNodes.map(n => lastGraph.pos[n.id]).filter(Boolean);
        if (!pts.length) { _miniTx = null; return; }

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const p of pts) {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
        }
        const dataW = Math.max(maxX - minX, 1), dataH = Math.max(maxY - minY, 1);
        const scale = Math.min((MINI_W - 2 * MINI_PAD) / dataW, (MINI_H - 2 * MINI_PAD) / dataH);
        const offX = (MINI_W - dataW * scale) / 2 - minX * scale;
        const offY = (MINI_H - dataH * scale) / 2 - minY * scale;
        _miniTx = { scale, offX, offY };
        const d2m = (x, y) => [x * scale + offX, y * scale + offY];

        for (const n of lastGraph.allNodes) {
            const p = lastGraph.pos[n.id];
            if (!p) continue;
            const [mx, my] = d2m(p.x, p.y);
            miniCtx.fillStyle = _nodeColor(n.type);
            miniCtx.beginPath();
            miniCtx.arc(mx, my, n.type === 'page' ? 2.6 : 1.8, 0, 6.2832);
            miniCtx.fill();
        }

        // Current viewport rectangle computed mathematically
        const tl = [ -panX / currentZoom, -panY / currentZoom ];
        const br = [ (chartDiv.clientWidth - panX) / currentZoom, (chartDiv.clientHeight - panY) / currentZoom ];
        if (tl && br) {
            const [x0, y0] = d2m(tl[0], tl[1]);
            const [x1, y1] = d2m(br[0], br[1]);
            miniCtx.strokeStyle = 'rgba(37,99,235,0.7)';
            miniCtx.lineWidth = 1.5;
            const vx = Math.min(x0, x1);
            const vy = Math.min(y0, y1);
            const vw = Math.abs(x1 - x0);
            const vh = Math.abs(y1 - y0);
            miniCtx.fillStyle = 'rgba(37,99,235,0.06)';
            miniCtx.fillRect(vx, vy, vw, vh);
            miniCtx.strokeRect(vx, vy, vw, vh);
        }
    }

    miniCanvas.addEventListener('click', e => {
        if (!_miniTx) return;
        const r = miniCanvas.getBoundingClientRect();
        const mx = (e.clientX - r.left) * (MINI_W / r.width);
        const my = (e.clientY - r.top) * (MINI_H / r.height);
        currentCenter = [
            (mx - _miniTx.offX) / _miniTx.scale,
            (my - _miniTx.offY) / _miniTx.scale
        ];
        render();
    });

    // ── Type filter chips ──────────────────────────────────────────────────
    const _TYPE_LABEL = {
        page: 'Pages', visual: 'Visuals', dataset: 'Datasets', table: 'Tables',
        column: 'Columns', io: 'I/O', statement: 'Statements', conditional: 'Branches',
        loop: 'Loops', procedure: 'Procedures', connection: 'Connections',
    };
    const _TYPE_ORDER = ['page', 'visual', 'dataset', 'table', 'column', 'io', 'statement', 'conditional', 'loop', 'procedure', 'connection'];

    const typeCounts = {};
    for (const n of nodes) typeCounts[n.type] = (typeCounts[n.type] ?? 0) + 1;

    const presentTypes = _TYPE_ORDER.filter(t => typeCounts[t] !== undefined)
        .concat(Object.keys(typeCounts).filter(t => !_TYPE_ORDER.includes(t)));

    function buildChips() {
        chips.replaceChildren();
        for (const t of presentTypes) {
            const chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'etlsql-dag-chip' + (hiddenTypes.has(t) ? ' is-off' : '');
            chip.title = hiddenTypes.has(t) ? `Show ${_TYPE_LABEL[t] ?? t}` : `Hide ${_TYPE_LABEL[t] ?? t}`;
            const dot = document.createElement('span');
            dot.className = 'etlsql-dag-chip-dot';
            dot.style.background = _nodeColor(t);
            const text = document.createElement('span');
            const count = typeCounts[t];
            text.textContent = count ? `${_TYPE_LABEL[t] ?? t} ${count}` : (_TYPE_LABEL[t] ?? t);
            chip.append(dot, text);
            chip.addEventListener('click', () => {
                if (hiddenTypes.has(t)) hiddenTypes.delete(t); else hiddenTypes.add(t);
                buildChips();
                render();
            });
            chips.appendChild(chip);
        }
    }
    buildChips();

    // ── Collapse / expand all pages ─────────────────────────────────────────
    const pageIds = nodes.filter(n => n.type === 'page' && childCount(n.id) > 0).map(n => n.id);
    if (pageIds.length) {
        const actions = document.createElement('div');
        actions.className = 'etlsql-dag-actions';
        const mkBtn = (txt, fn) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'etlsql-dag-btn';
            b.textContent = txt;
            b.addEventListener('click', fn);
            return b;
        };
        actions.append(
            mkBtn('Collapse pages', () => { pageIds.forEach(id => collapsedPages.add(id)); render(); }),
            mkBtn('Expand pages',   () => { collapsedPages.clear(); render(); }),
        );
        toolbar.insertBefore(actions, badge);
    }

    function updateFocusBadge() {
        if (!focusedNode) { badge.style.display = 'none'; return; }
        const n = nodes.find(x => x.id === focusedNode);
        const label = document.createElement('strong');
        label.textContent = n ? n.label : focusedNode;
        const prefix = document.createElement('span');
        prefix.textContent = 'Focused: ';
        const clear = document.createElement('span');
        clear.className = 'etlsql-dag-focusbadge-x';
        clear.textContent = '✕ clear';
        badge.replaceChildren(prefix, label, clear);
        badge.style.display = 'flex';
    }

    function updateSoloBadge() {
        if (!soloedPage) { soloBadge.style.display = 'none'; return; }
        const n = nodes.find(x => x.id === soloedPage);
        const prefix = document.createElement('span');
        prefix.textContent = 'Soloed: ';
        const label = document.createElement('strong');
        label.textContent = n ? n.label : soloedPage;
        const clear = document.createElement('span');
        clear.className = 'etlsql-dag-focusbadge-x';
        clear.textContent = '✕ show all';
        soloBadge.replaceChildren(prefix, label, clear);
        soloBadge.style.display = 'flex';
    }

}
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
 * @param {number|null} [opts.reportVersion=null]  Current optimistic concurrency version.
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
    let reportVersion = opts.reportVersion ?? null;
    const folderId  = opts.folderId   ?? null;
    const apiBase   = opts.apiBase    ?? '';
    const _fetch    = opts.authFetch  ?? ((url, o) => fetch(url, o));

    // ── Visual type registry ──────────────────────────────────────────────────
    const VCATEGORIES = [
        {
            name: 'Charts',
            types: [
                ['BAR','#3b82f6'],['LINE','#06b6d4'],['AREA','#0891b2'],['PIE','#8b5cf6'],
                ['DONUT','#a855f7'],['HBAR','#6366f1'],['SCATTER','#6366f1'],['GAUGE','#a855f7'],
                ['FUNNEL','#d946ef'],['TREEMAP','#ec4899'],['HEATMAP','#f43f5e'],['COMBO','#0ea5e9'],
                ['BOXPLOT','#14b8a6'],['WATERFALL','#10b981'],['BUBBLE','#06b6d4'],['RADAR','#8b5cf6'],
                ['CANDLESTICK','#f59e0b'],['MAP','#10b981'],['GANTT','#8b5cf6'],['SANKEY','#14b8a6'],
                ['SUNBURST','#d946ef'],['NETWORK','#6366f1'],['TRELLIS','#64748b'],['MATRIX','#475569']
            ]
        },
        {
            name: 'Data & Content',
            types: [
                ['TABLE','#64748b'],['CARD','#10b981'],['TEXT','#f59e0b'],['IMAGE','#ec4899']
            ]
        },
        {
            name: 'Filters & Inputs',
            types: [
                ['SLICER','#f97316'],['MULTISELECT','#f97316'],['DATEPICKER','#e11d48'],['RELDATEPICKER','#e11d48'],
                ['SLIDER','#f59e0b'],['SEARCH','#0ea5e9'],['CHECKBOX','#10b981'],['TEXTBOX','#64748b'],['NUMBERBOX','#64748b']
            ]
        },
        {
            name: 'Layout & Actions',
            types: [
                ['CONTAINER','#475569'],['BUTTON','#a855f7']
            ]
        }
    ];
    const VTYPES = VCATEGORIES.flatMap(c => c.types);
    const VCOLOR = Object.fromEntries(VTYPES.map(([t, c]) => [t, c]));
    const ROLES  = ['X', 'Y', 'VALUE', 'CATEGORY', 'SERIES', 'LABEL', 'TOOLTIP'];

    // ── API helper ────────────────────────────────────────────────────────────
    async function apiJson(url, method = 'GET', body = null, version = null) {
        const init = { method, headers: {} };
        if (version !== null)
            init.headers['If-Match'] = `"${version}"`;
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
    let sidebarHtml = '';
    for (const cat of VCATEGORIES) {
        sidebarHtml += `
            <div class="etlsql-dsgn-section">
                <div class="etlsql-dsgn-section-hdr">${cat.name}</div>
                <div class="etlsql-dsgn-palette">
                    ${cat.types.map(([type, color]) => `
                        <button class="etlsql-dsgn-palette-btn" data-vtype="${type}" style="--vc: ${color}" title="Add ${type}">
                            ${type}
                        </button>
                    `).join('')}
                </div>
            </div>
        `;
    }
    sidebarHtml += `
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
    sidebar.innerHTML = sidebarHtml;
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
                <button class="etlsql-dsgn-vcard-del" data-del="${v.id}" title="Remove visual">✕</button>
                <div class="etlsql-dsgn-vcard-resize" title="Drag to resize"></div>
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

        const on = (sel, fn) => propsPanel.querySelector(sel)?.addEventListener('change', fn);

        if (v.type === 'CONTAINER') {
            const containerType = v.options?.CONTAINER_TYPE || 'BOX';
            const ctypes = ['BOX', 'SCROLL', 'DRAWER', 'SIDEBAR', 'TABS', 'ACCORDION', 'MODAL', 'POPOVER'];
            propsPanel.innerHTML = `
                <div class="etlsql-dsgn-props-section">
                    <div class="etlsql-dsgn-props-hdr">Properties</div>
                    <label class="etlsql-dsgn-label">Name<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                    <label class="etlsql-dsgn-label">Container Type
                        <select id="pp-container-type" class="form-control">
                            ${ctypes.map(t => `<option${containerType === t ? ' selected' : ''}>${t}</option>`).join('')}
                        </select>
                    </label>
                    <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                </div>
                <div class="etlsql-dsgn-props-section">
                    <div class="etlsql-dsgn-props-hdr">Grid Position</div>
                    <div class="etlsql-dsgn-grid4">
                        <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                        <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                        <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                        <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                    </div>
                    <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Container</button>
                </div>
            `;
            on('#pp-name',  e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
            on('#pp-container-type', e => { if(!v.options) v.options = {}; v.options.CONTAINER_TYPE = e.target.value; });
            on('#pp-title', e => { v.title = e.target.value; renderCanvas(); });
            on('#pp-col',   e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-row',   e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-cspan', e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
            on('#pp-rspan', e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });
            propsPanel.querySelector('#pp-delete')?.addEventListener('click', () => deleteVisual(v.id));
            return;
        }

        if (v.type === 'BUTTON') {
            const buttonType = v.options?.BUTTON_TYPE || 'REFRESH';
            const btypes = ['REFRESH', 'BACK', 'HELP', 'SUBMIT', 'RESET', 'NAVIGATE', 'ACTION'];
            propsPanel.innerHTML = `
                <div class="etlsql-dsgn-props-section">
                    <div class="etlsql-dsgn-props-hdr">Properties</div>
                    <label class="etlsql-dsgn-label">Name<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                    <label class="etlsql-dsgn-label">Button Type
                        <select id="pp-button-type" class="form-control">
                            ${btypes.map(t => `<option${buttonType === t ? ' selected' : ''}>${t}</option>`).join('')}
                        </select>
                    </label>
                    <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                </div>
                <div class="etlsql-dsgn-props-section">
                    <div class="etlsql-dsgn-props-hdr">Grid Position</div>
                    <div class="etlsql-dsgn-grid4">
                        <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                        <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                        <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                        <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                    </div>
                    <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Button</button>
                </div>
            `;
            on('#pp-name',  e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
            on('#pp-button-type', e => { if(!v.options) v.options = {}; v.options.BUTTON_TYPE = e.target.value; });
            on('#pp-title', e => { v.title = e.target.value; renderCanvas(); });
            on('#pp-col',   e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-row',   e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
            on('#pp-cspan', e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
            on('#pp-rspan', e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });
            propsPanel.querySelector('#pp-delete')?.addEventListener('click', () => deleteVisual(v.id));
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
        const visual = {
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
        };
        if (type === 'CONTAINER') {
            visual.options.CONTAINER_TYPE = 'BOX';
        } else if (type === 'BUTTON') {
            visual.options.BUTTON_TYPE = 'REFRESH';
        }
        page.visuals.push(visual);
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
                const saved = await apiJson(
                    `/api/reports/${reportId}/script-content`,
                    'PUT',
                    { scriptText: script },
                    reportVersion);
                reportVersion = saved?.version ?? reportVersion;
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

    sidebar.addEventListener('click', e => {
        const btn = e.target.closest('.etlsql-dsgn-palette-btn');
        if (btn) addVisual(btn.dataset.vtype);
    });

    // ── Drag & Resize Interaction ────────────────────────────────────────────
    let isDragging = false;
    let isResizing = false;
    let activeId = null;
    let startX = 0, startY = 0;
    let startCol = 1, startRow = 1;
    let startColSpan = 12, startRowSpan = 4;
    let cardOffsetX = 0, cardOffsetY = 0;

    canvasGrid.addEventListener('mousedown', e => {
        const resizeHandle = e.target.closest('.etlsql-dsgn-vcard-resize');
        const card = e.target.closest('.etlsql-dsgn-visual-card');
        const delBtn = e.target.closest('[data-del]');

        if (delBtn) return; // Delete visual handler manages this

        if (card) {
            const vid = card.dataset.vid;
            const v = findVis(vid);
            if (!v) return;

            selectVisual(vid);

            startX = e.clientX;
            startY = e.clientY;
            activeId = vid;
            startCol = v.gridCol || 1;
            startRow = v.gridRow || 1;
            startColSpan = v.gridColSpan || 12;
            startRowSpan = v.gridRowSpan || 4;

            if (resizeHandle) {
                isResizing = true;
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            } else {
                isDragging = true;
                const rect = card.getBoundingClientRect();
                cardOffsetX = e.clientX - rect.left;
                cardOffsetY = e.clientY - rect.top;
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            }
        }
    });

    function handleMouseMove(e) {
        if (!activeId) return;
        const v = findVis(activeId);
        if (!v) return;

        const gridRect = canvasGrid.getBoundingClientRect();
        const gridW = gridRect.width - 32;
        const W_col = (gridW - 11 * 6) / 12;

        if (isDragging) {
            const cardX = e.clientX - gridRect.left - cardOffsetX - 16;
            const cardY = e.clientY - gridRect.top - cardOffsetY - 16;

            let newCol = Math.round(cardX / (W_col + 6)) + 1;
            newCol = Math.max(1, Math.min(13 - startColSpan, newCol));

            let newRow = Math.round(cardY / 66) + 1;
            newRow = Math.max(1, newRow);

            if (v.gridCol !== newCol || v.gridRow !== newRow) {
                v.gridCol = newCol;
                v.gridRow = newRow;
                renderCanvas();
                renderProps();
            }
        } else if (isResizing) {
            const cardRightX = e.clientX - gridRect.left - 16;
            const cardBottomY = e.clientY - gridRect.top - 16;

            const cardLeftX = (startCol - 1) * (W_col + 6);
            const cardTopY = (startRow - 1) * 66;

            let newColSpan = Math.round((cardRightX - cardLeftX + 6) / (W_col + 6));
            newColSpan = Math.max(1, Math.min(13 - startCol, newColSpan));

            let newRowSpan = Math.round((cardBottomY - cardTopY + 6) / 66);
            newRowSpan = Math.max(1, newRowSpan);

            if (v.gridColSpan !== newColSpan || v.gridRowSpan !== newRowSpan) {
                v.gridColSpan = newColSpan;
                v.gridRowSpan = newRowSpan;
                renderCanvas();
                renderProps();
            }
        }
    }

    function handleMouseUp(e) {
        isDragging = false;
        isResizing = false;
        activeId = null;
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
    }

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
