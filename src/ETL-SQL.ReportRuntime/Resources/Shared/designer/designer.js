/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Designer — shared vanilla-JS component
 *
 * Three exported surface areas, implemented across phases:
 *   renderDag()          Phase 2 — read-only DAG / lineage visualization
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
 * Render a read-only directed graph inside `container`.
 *
 * @param {HTMLElement} container   DOM element to render into. Must have a defined height.
 * @param {Object}      graph
 * @param {Array}       graph.nodes [{ id: string, label: string, type?: string, meta?: object }]
 * @param {Array}       graph.edges [{ source: string, target: string, label?: string }]
 * @param {Object}      [options]
 * @param {string}      [options.theme='portal']   'portal' | 'vscode' — affects colour palette
 * @param {Function}    [options.onNodeClick]       Called with (nodeId, nodeMeta) on click
 * @returns {{ dispose: Function, resize: Function, showDetail: Function }}
 *   dispose() — removes DOM listeners and clears the rendered graph
 *   resize()  — re-fits the chart to the current container size (call on panel resize)
 *   showDetail(id) — opens a node detail panel programmatically (used by tests/sandbox)
 */
export function renderDag(container, { nodes, edges }, options = {}) {
    const graphNodes = (nodes ?? []).map(n => ({ ...n, type: n.type || 'table' }));
    const graphEdges = edges ?? [];
    if (!graphNodes.length) {
        container.innerHTML = '<div class="etlsql-dag-empty">No structure data available.</div>';
        return { dispose: () => {}, resize: () => {}, showDetail: () => {} };
    }

    const nodeById = Object.fromEntries(graphNodes.map(n => [n.id, n]));
    const hiddenTypes = new Set();
    let focusedNode = null;
    let focusSet = null;
    let activeColumnPathSet = null;
    let activeColumnLabel = null;
    let panX = 0;
    let panY = 0;
    let zoom = graphNodes.length > 40 ? 0.45 : 0.75;
    let disposed = false;
    let positions = _computeLayout(graphNodes, graphEdges);
    let searchMatches = [];
    let searchIdx = -1;
    const dragRemovers = [];

    container.style.position = container.style.position || 'relative';
    container.innerHTML = '';
    container.style.display = 'flex';
    container.style.flexDirection = 'column';
    container.classList.add('etlsql-dag-container');

    const toolbar = document.createElement('div');
    toolbar.className = 'etlsql-dag-toolbar';
    container.appendChild(toolbar);

    const chips = document.createElement('div');
    chips.className = 'etlsql-dag-chips';
    toolbar.appendChild(chips);

    const search = document.createElement('div');
    search.className = 'etlsql-dag-search';
    const searchInput = document.createElement('input');
    searchInput.type = 'search';
    searchInput.placeholder = 'Find node...';
    searchInput.setAttribute('aria-label', 'Find node');
    const searchCount = document.createElement('span');
    searchCount.className = 'etlsql-dag-search-count';
    search.append(searchInput, searchCount);
    toolbar.appendChild(search);

    const badge = document.createElement('button');
    badge.type = 'button';
    badge.className = 'etlsql-dag-focusbadge';
    badge.style.display = 'none';
    badge.addEventListener('click', clearFocus);
    toolbar.appendChild(badge);

    const body = document.createElement('div');
    body.className = 'etlsql-dag-body';
    container.appendChild(body);

    const canvas = document.createElement('div');
    canvas.className = 'etlsql-dag-canvas';
    body.appendChild(canvas);

    const viewport = document.createElement('div');
    viewport.className = 'etlsql-dag-viewport';
    canvas.appendChild(viewport);

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'etlsql-dag-svg');
    viewport.appendChild(svg);

    const badgeLayer = document.createElement('div');
    badgeLayer.className = 'etlsql-dag-badge-container';
    viewport.appendChild(badgeLayer);

    const cardLayer = document.createElement('div');
    cardLayer.className = 'etlsql-dag-card-layer';
    viewport.appendChild(cardLayer);

    const panel = document.createElement('div');
    panel.className = 'etlsql-dag-panel';
    panel.style.display = 'none';
    body.appendChild(panel);

    const zoomControls = document.createElement('div');
    zoomControls.className = 'etlsql-dag-zoom-controls';
    body.appendChild(zoomControls);
    zoomControls.append(
        zoomButton('+', 'Zoom in', () => setZoom(Math.min(2, zoom * 1.2))),
        zoomButton('-', 'Zoom out', () => setZoom(Math.max(0.1, zoom / 1.2))),
        zoomButton('Reset', 'Reset view', () => { panX = 0; panY = 0; zoom = graphNodes.length > 40 ? 0.45 : 0.75; updateViewport(); })
    );

    const presentTypes = [...new Set(graphNodes.map(n => n.type))].sort();
    buildChips();
    render();

    searchInput.addEventListener('input', () => {
        const term = searchInput.value.trim().toLowerCase();
        searchMatches = term ? visibleNodes().filter(n => String(n.label ?? '').toLowerCase().includes(term)).map(n => n.id) : [];
        searchIdx = -1;
        updateSearchCount();
        if (searchMatches.length) nextMatch();
    });
    searchInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); nextMatch(); }
        if (e.key === 'Escape') { searchInput.value = ''; searchMatches = []; searchIdx = -1; updateSearchCount(); }
    });

    let isPanning = false;
    let panStartX = 0;
    let panStartY = 0;
    canvas.addEventListener('wheel', e => {
        e.preventDefault();
        const rect = canvas.getBoundingClientRect();
        const mx = e.clientX - rect.left;
        const my = e.clientY - rect.top;
        const before = screenToGraph(mx, my);
        const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
        zoom = Math.max(0.1, Math.min(2, zoom * factor));
        panX = mx - canvas.clientWidth / 2 - before.x * zoom;
        panY = my - canvas.clientHeight * 0.4 - before.y * zoom;
        updateViewport();
    }, { passive: false });
    canvas.addEventListener('mousedown', e => {
        if (e.target !== canvas && e.target !== viewport && e.target !== svg) return;
        isPanning = true;
        panStartX = e.clientX - panX;
        panStartY = e.clientY - panY;
        canvas.style.cursor = 'grabbing';
    });
    const onDocMove = e => {
        if (!isPanning) return;
        panX = e.clientX - panStartX;
        panY = e.clientY - panStartY;
        updateViewport(false);
    };
    const onDocUp = () => {
        if (!isPanning) return;
        isPanning = false;
        canvas.style.cursor = '';
        drawConnections();
    };
    document.addEventListener('mousemove', onDocMove);
    document.addEventListener('mouseup', onDocUp);

    function visibleNodes() {
        return graphNodes.filter(n => !hiddenTypes.has(n.type));
    }

    function visibleEdges() {
        const ids = new Set(visibleNodes().map(n => n.id));
        return graphEdges.filter(e => ids.has(e.source) && ids.has(e.target));
    }

    function buildChips() {
        chips.replaceChildren();
        for (const type of presentTypes) {
            const chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'etlsql-dag-chip' + (hiddenTypes.has(type) ? ' is-off' : '');
            chip.title = hiddenTypes.has(type) ? `Show ${type}` : `Hide ${type}`;
            const dot = document.createElement('span');
            dot.className = 'etlsql-dag-chip-dot';
            dot.style.background = _nodeColor(type);
            const text = document.createElement('span');
            text.textContent = `${type} ${graphNodes.filter(n => n.type === type).length}`;
            chip.append(dot, text);
            chip.addEventListener('click', () => {
                if (hiddenTypes.has(type)) hiddenTypes.delete(type); else hiddenTypes.add(type);
                buildChips();
                focusedNode = null;
                focusSet = null;
                activeColumnPathSet = null;
                activeColumnLabel = null;
                render();
            });
            chips.appendChild(chip);
        }
    }

    function render() {
        if (disposed) return;
        const nodesToRender = visibleNodes();
        positions = { ...positions, ..._computeLayout(nodesToRender, visibleEdges()) };
        cardLayer.replaceChildren();
        for (const node of nodesToRender) renderCard(node);
        updateFocusBadge();
        updateViewport();
        options.onNodeClick?.(focusedNode, focusedNode ? nodeById[focusedNode]?.meta : null);
    }

    function renderCard(node) {
        const p = positions[node.id] ?? { x: 0, y: 0 };
        const card = document.createElement('div');
        card.id = `node__${node.id}`;
        card.className = 'etlsql-dag-card';
        card.style.left = `${p.x - 130}px`;
        card.style.top = `${p.y}px`;
        card.style.width = '260px';
        card.style.border = `1px solid ${_nodeColor(node.type)}`;
        card.dataset.nodeId = node.id;

        const header = document.createElement('div');
        header.className = 'etlsql-dag-card-header';
        const title = document.createElement('span');
        title.textContent = node.label ?? node.id;
        title.style.overflow = 'hidden';
        title.style.textOverflow = 'ellipsis';
        title.style.whiteSpace = 'nowrap';
        const kind = document.createElement('span');
        kind.textContent = node.type;
        kind.style.color = _nodeColor(node.type);
        header.append(title, kind);
        card.appendChild(header);

        const rows = node.meta?.mappings?.length
            ? node.meta.mappings.map(m => ({ id: `${node.id}__map__${m.role}`, label: `${m.role}: ${m.column}`, column: cleanColumn(m.column) }))
            : (node.meta?.columns ?? []).map(c => ({ id: `${node.id}__col__${c}`, label: c, column: c }));

        for (const row of rows.slice(0, 16)) {
            const line = document.createElement('div');
            line.id = row.id;
            line.className = 'etlsql-dag-col-row';
            line.dataset.nodeId = node.id;
            line.dataset.column = row.column;
            const left = document.createElement('span');
            left.className = 'port-left';
            const label = document.createElement('span');
            label.className = 'col-label-span';
            label.textContent = row.label;
            label.style.overflow = 'hidden';
            label.style.textOverflow = 'ellipsis';
            label.style.whiteSpace = 'nowrap';
            const right = document.createElement('span');
            right.className = 'port-right';
            line.append(left, label, right);
            line.addEventListener('click', e => {
                e.stopPropagation();
                isolateColumn(node.id, row.column, `${node.label} / ${row.column}`);
            });
            card.appendChild(line);
        }

        if (rows.length > 16) {
            const more = document.createElement('div');
            more.className = 'etlsql-dag-col-row';
            more.textContent = `+ ${rows.length - 16} more`;
            card.appendChild(more);
        }

        const leftPort = document.createElement('span');
        leftPort.className = 'card-port-left';
        leftPort.style.background = _nodeColor(node.type);
        const rightPort = document.createElement('span');
        rightPort.className = 'card-port-right';
        rightPort.style.background = _nodeColor(node.type);
        card.append(leftPort, rightPort);

        header.addEventListener('mousedown', e => startNodeDrag(e, node.id, card));
        card.addEventListener('click', () => focusNode(node.id));
        card.addEventListener('dblclick', e => { e.stopPropagation(); showNodeDetails(node); });
        cardLayer.appendChild(card);
        applyCardState(card, node);
    }

    function startNodeDrag(e, nodeId, card) {
        e.preventDefault();
        const startX = e.clientX;
        const startY = e.clientY;
        const original = positions[nodeId] ?? { x: 0, y: 0 };
        card.style.cursor = 'grabbing';
        const move = me => {
            positions[nodeId] = {
                x: original.x + (me.clientX - startX) / zoom,
                y: original.y + (me.clientY - startY) / zoom,
            };
            card.style.left = `${positions[nodeId].x - 130}px`;
            card.style.top = `${positions[nodeId].y}px`;
            drawConnections();
        };
        const up = () => {
            card.style.cursor = '';
            document.removeEventListener('mousemove', move);
            document.removeEventListener('mouseup', up);
        };
        document.addEventListener('mousemove', move);
        document.addEventListener('mouseup', up);
        dragRemovers.push(() => {
            document.removeEventListener('mousemove', move);
            document.removeEventListener('mouseup', up);
        });
    }

    function focusNode(nodeId) {
        if (focusedNode === nodeId && !activeColumnPathSet) {
            clearFocus();
            return;
        }
        focusedNode = nodeId;
        focusSet = _lineageReach(nodeId, visibleEdges(), visibleNodes());
        activeColumnPathSet = null;
        activeColumnLabel = null;
        showNodeDetails(nodeById[nodeId]);
        render();
    }

    function clearFocus() {
        focusedNode = null;
        focusSet = null;
        activeColumnPathSet = null;
        activeColumnLabel = null;
        panel.style.display = 'none';
        render();
    }

    function isolateColumn(nodeId, column, label) {
        activeColumnPathSet = new Set();
        traceColumnPath(nodeId, column, activeColumnPathSet, 'both');
        activeColumnLabel = label;
        focusedNode = nodeId;
        focusSet = new Set([...activeColumnPathSet].filter(id => !id.includes('__col__') && !id.includes('__map__')));
        updateFocusBadge();
        container.querySelectorAll('.etlsql-dag-card').forEach(card => applyCardState(card, nodeById[card.dataset.nodeId]));
        drawConnections();
    }

    function traceColumnPath(nodeId, column, pathSet, direction) {
        const key = `${nodeId}__col__${column}`;
        if (pathSet.has(key)) return;
        pathSet.add(key);
        pathSet.add(nodeId);
        const node = nodeById[nodeId];
        if (!node) return;
        if (direction === 'both' || direction === 'up') {
            for (const src of (node.meta?.columnLineage?.[column]?.sources ?? [])) {
                const srcNode = graphNodes.find(n => n.label === src.table || n.id === src.table);
                if (srcNode) traceColumnPath(srcNode.id, src.column, pathSet, 'up');
            }
        }
        if (direction === 'both' || direction === 'down') {
            for (const other of graphNodes) {
                for (const [otherColumn, lineage] of Object.entries(other.meta?.columnLineage ?? {})) {
                    if ((lineage.sources ?? []).some(src => (src.table === node.label || src.table === node.id) && src.column === column)) {
                        traceColumnPath(other.id, otherColumn, pathSet, 'down');
                    }
                }
                for (const mapping of (other.meta?.mappings ?? [])) {
                    if (graphEdges.some(e => e.source === nodeId && e.target === other.id) && cleanColumn(mapping.column) === column) {
                        pathSet.add(other.id);
                        pathSet.add(`${other.id}__map__${mapping.role}`);
                    }
                }
            }
        }
    }

    function showNodeDetails(node) {
        if (!node) return;
        panel.style.display = 'block';
        panel.replaceChildren();
        const head = document.createElement('div');
        head.className = 'etlsql-dag-panel-head';
        const dot = document.createElement('span');
        dot.className = 'etlsql-dag-panel-dot';
        dot.style.background = _nodeColor(node.type);
        const title = document.createElement('strong');
        title.className = 'etlsql-dag-panel-title';
        title.textContent = node.label ?? node.id;
        const close = document.createElement('button');
        close.className = 'etlsql-dag-panel-x';
        close.type = 'button';
        close.textContent = 'x';
        close.addEventListener('click', () => { panel.style.display = 'none'; });
        head.append(dot, title, close);
        panel.appendChild(head);
        const sub = document.createElement('div');
        sub.className = 'etlsql-dag-panel-sub';
        sub.textContent = `Type: ${node.type}`;
        panel.appendChild(sub);
        appendPanelList('Metadata', Object.entries(node.meta ?? {}).filter(([_, v]) => typeof v !== 'object').map(([k, v]) => ({ k, v })), 'No scalar metadata.');
        appendPanelList('Columns', (node.meta?.columns ?? []).map(c => ({ v: c })), 'No columns captured.');
        appendPanelList('Mappings', (node.meta?.mappings ?? []).map(m => ({ k: m.role, v: m.column })), 'No visual mappings captured.');
    }

    function appendPanelList(title, items, emptyText) {
        const h = document.createElement('div');
        h.className = 'etlsql-dag-panel-h';
        h.textContent = title;
        panel.appendChild(h);
        if (!items.length) {
            const empty = document.createElement('div');
            empty.className = 'etlsql-dag-panel-empty';
            empty.textContent = emptyText;
            panel.appendChild(empty);
            return;
        }
        const ul = document.createElement('ul');
        ul.className = 'etlsql-dag-panel-list';
        for (const item of items) {
            const li = document.createElement('li');
            li.className = 'etlsql-dag-panel-li';
            if (item.k) {
                const k = document.createElement('span');
                k.className = 'etlsql-dag-panel-k';
                k.textContent = `${item.k}:`;
                li.appendChild(k);
            }
            const v = document.createElement('span');
            v.className = 'etlsql-dag-panel-v';
            v.textContent = String(item.v ?? '');
            li.appendChild(v);
            ul.appendChild(li);
        }
        panel.appendChild(ul);
    }

    function applyCardState(card, node) {
        const inFocus = !focusSet || focusSet.has(node.id);
        card.style.opacity = inFocus ? '1' : '0.12';
        card.style.borderColor = node.id === focusedNode ? '#f8fafc' : _nodeColor(node.type);
        for (const row of card.querySelectorAll('.etlsql-dag-col-row')) {
            const rowId = row.id;
            const active = !activeColumnPathSet || activeColumnPathSet.has(rowId) || activeColumnPathSet.has(node.id);
            row.style.opacity = active ? '1' : '0.14';
            const label = row.querySelector('.col-label-span');
            if (label) label.style.color = activeColumnPathSet?.has(rowId) ? '#34d399' : '#cbd5e1';
        }
    }

    function drawConnections() {
        svg.replaceChildren();
        badgeLayer.replaceChildren();
        const rect = viewport.getBoundingClientRect();
        for (const edge of visibleEdges()) drawEdge(edge, rect);
        drawColumnEdges(rect);
    }

    function drawEdge(edge, rect) {
        const from = document.getElementById(`node__${edge.source}`);
        const to = document.getElementById(`node__${edge.target}`);
        if (!from || !to) return;
        const fromPort = from.querySelector('.card-port-right');
        const toPort = to.querySelector('.card-port-left');
        if (!fromPort || !toPort) return;
        const a = centerOf(fromPort, rect);
        const b = centerOf(toPort, rect);
        const inPath = !focusSet || (focusSet.has(edge.source) && focusSet.has(edge.target));
        drawLink(a.x, a.y, b.x, b.y, inPath ? '#64748b' : 'rgba(71,85,105,0.08)', inPath ? 1.8 : 0.8);
        if (edge.label && inPath) drawEdgeBadge(a.x, a.y, b.x, b.y, edge.label, false, false);
    }

    function drawColumnEdges(rect) {
        for (const node of visibleNodes()) {
            for (const [targetColumn, lineage] of Object.entries(node.meta?.columnLineage ?? {})) {
                for (const src of (lineage.sources ?? [])) {
                    const srcNode = graphNodes.find(n => n.label === src.table || n.id === src.table);
                    if (!srcNode || hiddenTypes.has(srcNode.type)) continue;
                    const from = document.getElementById(`${srcNode.id}__col__${src.column}`);
                    const to = document.getElementById(`${node.id}__col__${targetColumn}`);
                    if (!from || !to) continue;
                    const a = centerOf(from.querySelector('.port-right'), rect);
                    const b = centerOf(to.querySelector('.port-left'), rect);
                    const fromKey = `${srcNode.id}__col__${src.column}`;
                    const toKey = `${node.id}__col__${targetColumn}`;
                    const inPath = activeColumnPathSet && activeColumnPathSet.has(fromKey) && activeColumnPathSet.has(toKey);
                    const dim = activeColumnPathSet && !inPath;
                    drawLink(a.x, a.y, b.x, b.y, inPath ? '#10b981' : (dim ? 'rgba(16,185,129,0.05)' : 'rgba(16,185,129,0.35)'), inPath ? 3 : 1, !inPath);
                    if (lineage.transform && !dim) drawEdgeBadge(a.x, a.y, b.x, b.y, transformLabel(lineage.transform), inPath, dim);
                }
            }
        }
    }

    function drawLink(x1, y1, x2, y2, color, width, dashed = false) {
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        const dx = Math.abs(x2 - x1) * 0.45;
        path.setAttribute('d', `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`);
        path.setAttribute('stroke', color);
        path.setAttribute('stroke-width', width);
        path.setAttribute('fill', 'none');
        if (dashed) path.setAttribute('stroke-dasharray', '4 4');
        svg.appendChild(path);
    }

    function drawEdgeBadge(x1, y1, x2, y2, text, inPath, dim) {
        const badgeEl = document.createElement('div');
        badgeEl.className = 'etlsql-dag-edge-badge';
        badgeEl.style.left = `${(x1 + x2) / 2}px`;
        badgeEl.style.top = `${(y1 + y2) / 2}px`;
        badgeEl.style.background = inPath ? '#0f766e' : '#1e293b';
        badgeEl.style.border = inPath ? '1px solid #14b8a6' : '1px solid #3b82f6';
        badgeEl.style.color = inPath ? '#ccfbf1' : '#93c5fd';
        badgeEl.style.opacity = dim ? '0.12' : '1';
        badgeEl.textContent = text;
        badgeLayer.appendChild(badgeEl);
    }

    function centerOf(el, viewportRect) {
        if (!el) return { x: 0, y: 0 };
        const r = el.getBoundingClientRect();
        return { x: (r.left + r.width / 2 - viewportRect.left) / zoom, y: (r.top + r.height / 2 - viewportRect.top) / zoom };
    }

    function updateViewport(redraw = true) {
        viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
        if (redraw) requestAnimationFrame(drawConnections);
    }

    function setZoom(value) {
        zoom = value;
        updateViewport();
    }

    function screenToGraph(x, y) {
        return { x: (x - canvas.clientWidth / 2 - panX) / zoom, y: (y - canvas.clientHeight * 0.4 - panY) / zoom };
    }

    function updateFocusBadge() {
        if (!focusedNode && !activeColumnLabel) {
            badge.style.display = 'none';
            return;
        }
        const label = activeColumnLabel || nodeById[focusedNode]?.label || focusedNode;
        badge.replaceChildren(document.createTextNode(`Focused: ${label}  x clear`));
        badge.style.display = 'flex';
    }

    function updateSearchCount() {
        if (!searchInput.value.trim()) {
            searchCount.textContent = '';
            searchCount.classList.remove('is-empty');
            return;
        }
        searchCount.textContent = searchMatches.length ? `${searchIdx + 1}/${searchMatches.length}` : 'none';
        searchCount.classList.toggle('is-empty', searchMatches.length === 0);
    }

    function nextMatch() {
        if (!searchMatches.length) return;
        searchIdx = (searchIdx + 1) % searchMatches.length;
        updateSearchCount();
        const id = searchMatches[searchIdx];
        const p = positions[id];
        if (!p) return;
        panX = -p.x * zoom;
        panY = -p.y * zoom;
        updateViewport();
    }

    function cleanColumn(value) {
        return String(value ?? '').replace(/.*\((.*)\)/, '$1');
    }

    function transformLabel(value) {
        const text = String(value ?? 'PASS');
        return text.includes('(') ? text.slice(0, text.indexOf('(')) : text;
    }

    function zoomButton(text, title, handler) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'etlsql-dag-zoom-btn';
        button.textContent = text;
        button.title = title;
        button.setAttribute('aria-label', title);
        button.addEventListener('click', handler);
        return button;
    }

    return {
        dispose() {
            disposed = true;
            document.removeEventListener('mousemove', onDocMove);
            document.removeEventListener('mouseup', onDocUp);
            for (const remove of dragRemovers) remove();
            container.innerHTML = '';
        },
        resize() { updateViewport(); },
        showDetail(id) { showNodeDetails(nodeById[id]); },
    };
}
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

function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
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
 * @param {string}      [opts.analyzeUrl]      Optional endpoint for real parser/linter diagnostics.
 * @param {string}      [opts.completeUrl]     Optional endpoint for context-aware completions.
 * @param {string}      [opts.connectionRef]   Optional shared connection alias for schema completions.
 * @param {Function}    [opts.authFetch]       Optional fetch wrapper used for analyzeUrl/completeUrl.
 * @param {Function}    [opts.onDiagnostics]   Called with returned diagnostics.
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
        autocompletion, completionKeymap,
        linter, lintGutter,
    } = cm;

    const analyzeUrl = opts.analyzeUrl || null;
    const completeUrl = opts.completeUrl || null;
    const analyzeFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const completeFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const debounceMs = Number.isFinite(opts.analyzeDebounceMs) ? opts.analyzeDebounceMs : 450;
    const hasCmLint = Boolean(analyzeUrl && typeof linter === 'function');
    const completionKeys = Array.isArray(completionKeymap) ? completionKeymap : [];
    const acceptCompletionKey = completionKeys.find(binding => binding?.key === 'Enter' && typeof binding.run === 'function');
    const keymaps = [
        ...(acceptCompletionKey ? [{ ...acceptCompletionKey, key: 'Tab' }] : []),
        ...completionKeys,
        indentWithTab,
        ...(Array.isArray(defaultKeymap) ? defaultKeymap : []),
        ...(Array.isArray(historyKeymap) ? historyKeymap : []),
        ...(Array.isArray(searchKeymap) ? searchKeymap : []),
    ];
    const extensions = [
        lineNumbers(),
        highlightActiveLine(),
        highlightActiveLineGutter(),
        drawSelection(),
        history(),
        bracketMatching(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        highlightSelectionMatches(),
        keymap.of(keymaps),
        _getRptsqlLang(cm),
        EditorState.readOnly.of(opts.readOnly ?? false),
    ];
    if (hasCmLint && typeof lintGutter === 'function') extensions.push(lintGutter());

    let analyzeTimer = null;
    let analyzeAbort = null;
    let analyzeRequest = null;
    let analyzeRequestScript = null;
    let view = null;
    let diagPanel = null;

    function completionKind(kind) {
        switch (String(kind ?? '').toLowerCase()) {
            case 'keyword': return 'keyword';
            case 'function': return 'function';
            case 'table': return 'class';
            case 'column': return 'property';
            case 'variable': return 'variable';
            case 'alias': return 'variable';
            case 'connection': return 'namespace';
            case 'connector': return 'namespace';
            case 'path': return 'file';
            case 'optionname': return 'property';
            case 'optionvalue': return 'constant';
            case 'snippet': return 'text';
            default: return 'text';
        }
    }

    function cursorLineColumn(state, pos) {
        const line = state.doc.lineAt(pos);
        return { line: line.number - 1, column: pos - line.from };
    }

    function currentStatement() {
        if (!view) return '';
        const script = view.state.doc.toString();
        const pos = view.state.selection.main.head;
        let start = script.lastIndexOf(';', Math.max(0, pos - 1));
        let end = script.indexOf(';', pos);
        start = start < 0 ? 0 : start + 1;
        end = end < 0 ? script.length : end;
        return script.slice(start, end).trim();
    }

    function createCompletionSource() {
        if (!completeUrl || typeof autocompletion !== 'function') return null;
        return async (context) => {
            const word = context.matchBefore(/[\w@#&$.*]+/);
            const previous = context.state.sliceDoc(Math.max(0, context.pos - 1), context.pos);
            if (!word && !context.explicit) {
                return null;
            }
            if (word && word.from === word.to && !context.explicit && !/[\s.]/.test(previous)) {
                return null;
            }

            const { line, column } = cursorLineColumn(context.state, context.pos);
            const res = await completeFetch(completeUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    script: context.state.doc.toString(),
                    line,
                    column,
                    connectionRef: opts.connectionRef || null,
                    documentUri: opts.documentUri || 'portal-designer',
                }),
            });
            if (!res?.ok) return null;

            const data = await res.json();
            const items = Array.isArray(data?.items) ? data.items : [];
            const defaultFrom = word?.from ?? context.pos;
            return {
                from: defaultFrom,
                options: items.map(item => ({
                    label: item.label,
                    apply: (editorView, _completion, from, to) => {
                        const docLine = editorView.state.doc.line(line + 1);
                        const startColumn = Number.isFinite(item.startColumn) ? item.startColumn : null;
                        const endColumn = Number.isFinite(item.endColumn) ? item.endColumn : null;
                        const applyFrom = startColumn === null ? from : Math.min(docLine.to, docLine.from + Math.max(0, startColumn));
                        const applyTo = endColumn === null ? to : Math.min(docLine.to, docLine.from + Math.max(0, endColumn));
                        editorView.dispatch({
                            changes: { from: applyFrom, to: applyTo, insert: item.insertText || item.label },
                            selection: { anchor: applyFrom + String(item.insertText || item.label).length },
                            scrollIntoView: true,
                            userEvent: 'input.complete',
                        });
                    },
                    type: completionKind(item.kind),
                    detail: item.detail || item.kind || '',
                    info: item.documentation || undefined,
                })),
            };
        };
    }

    function setDiagnosticsStatus(text, kind = 'neutral') {
        if (!diagPanel) return;
        const status = diagPanel.querySelector('.etlsql-editor-diagnostics-status');
        status.textContent = text;
        status.dataset.kind = kind;
    }

    function diagnosticSeverity(d) {
        const severity = String(d?.severity ?? '').toLowerCase();
        if (severity.includes('error') || d?.severity === 0) return 'error';
        if (severity.includes('info') || severity.includes('hint')) return 'info';
        return 'warning';
    }

    function diagnosticOffset(doc, line, column) {
        const safeLine = Math.max(1, Math.min(doc.lines, (Number.isFinite(line) ? line : 0) + 1));
        const docLine = doc.line(safeLine);
        return Math.min(docLine.to, docLine.from + Math.max(0, Number.isFinite(column) ? column : 0));
    }

    function toCodeMirrorDiagnostic(doc, d) {
        const from = diagnosticOffset(doc, d.startLine, d.startColumn);
        const endLine = Number.isFinite(d.endLine) ? d.endLine : d.startLine;
        const endColumn = Number.isFinite(d.endColumn) ? d.endColumn : d.startColumn + 1;
        let to = diagnosticOffset(doc, endLine, endColumn);
        if (to <= from) to = Math.min(doc.length, from + 1);
        return {
            from,
            to,
            severity: diagnosticSeverity(d),
            source: d.source || d.code || 'ETL-SQL',
            message: d.message || d.code || 'Diagnostic',
        };
    }

    function renderDiagnostics(diagnostics) {
        opts.onDiagnostics?.(diagnostics);
        if (!diagPanel) return;
        const list = diagPanel.querySelector('.etlsql-editor-diagnostics-list');
        list.innerHTML = '';
        if (!diagnostics.length) {
            setDiagnosticsStatus('No diagnostics', 'ok');
            return;
        }
        const errors = diagnostics.filter(d => String(d.severity).toLowerCase().includes('error') || d.severity === 0).length;
        setDiagnosticsStatus(`${diagnostics.length} diagnostic${diagnostics.length === 1 ? '' : 's'}${errors ? ` · ${errors} error${errors === 1 ? '' : 's'}` : ''}`, errors ? 'error' : 'warn');
        for (const d of diagnostics.slice(0, 50)) {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'etlsql-editor-diagnostic';
            item.dataset.severity = String(d.severity ?? 'Warning').toLowerCase();
            const line = Number.isFinite(d.startLine) ? d.startLine + 1 : 1;
            const column = Number.isFinite(d.startColumn) ? d.startColumn + 1 : 1;
            item.innerHTML = `<span class="etlsql-editor-diagnostic-code">${escapeHtml(d.code || d.source || 'diagnostic')}</span><span class="etlsql-editor-diagnostic-pos">${line}:${column}</span><span class="etlsql-editor-diagnostic-msg">${escapeHtml(d.message || '')}</span>`;
            item.addEventListener('click', () => {
                if (!view) return;
                const safeLine = Math.max(1, Math.min(view.state.doc.lines, line));
                const docLine = view.state.doc.line(safeLine);
                const pos = Math.min(docLine.to, docLine.from + Math.max(0, column - 1));
                view.dispatch({ selection: { anchor: pos }, effects: EditorView.scrollIntoView(pos, { y: 'center' }) });
                view.focus();
            });
            list.appendChild(item);
        }
    }

    async function fetchDiagnostics(script) {
        if (!analyzeUrl) return [];
        if (analyzeRequest && analyzeRequestScript === script) return await analyzeRequest;

        analyzeAbort?.abort();
        const controller = new AbortController();
        analyzeAbort = controller;
        analyzeRequestScript = script;
        analyzeRequest = (async () => {
            const res = await analyzeFetch(analyzeUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: opts.documentUri || 'portal-designer' }),
                signal: controller.signal,
            });
            if (!res?.ok) throw new Error(res?.statusText || 'Analyze request failed');
            const data = await res.json();
            return data?.diagnostics ?? [];
        })();

        try {
            return await analyzeRequest;
        } finally {
            if (analyzeAbort === controller) analyzeAbort = null;
            if (analyzeRequestScript === script) {
                analyzeRequest = null;
                analyzeRequestScript = null;
            }
        }
    }

    async function runAnalysis(script) {
        if (!analyzeUrl) return;
        setDiagnosticsStatus('Analyzing...', 'neutral');
        try {
            renderDiagnostics(await fetchDiagnostics(script));
        } catch (err) {
            if (err?.name === 'AbortError') return;
            renderDiagnostics([{
                startLine: 0,
                startColumn: 0,
                severity: 'Error',
                code: 'ANALYZE_REQUEST',
                source: 'Portal editor',
                message: err?.message || 'Analyze request failed',
            }]);
        }
    }

    function scheduleAnalysis(script) {
        if (!analyzeUrl) return;
        clearTimeout(analyzeTimer);
        analyzeTimer = setTimeout(() => runAnalysis(script), debounceMs);
    }

    if (hasCmLint) {
        extensions.push(linter(async editorView => {
            try {
                const diagnostics = await fetchDiagnostics(editorView.state.doc.toString());
                renderDiagnostics(diagnostics);
                return diagnostics.map(d => toCodeMirrorDiagnostic(editorView.state.doc, d));
            } catch (err) {
                if (err?.name === 'AbortError') return [];
                const diagnostic = {
                    startLine: 0,
                    startColumn: 0,
                    severity: 'Error',
                    code: 'ANALYZE_REQUEST',
                    source: 'Portal editor',
                    message: err?.message || 'Analyze request failed',
                };
                renderDiagnostics([diagnostic]);
                return [toCodeMirrorDiagnostic(editorView.state.doc, diagnostic)];
            }
        }, { delay: debounceMs }));
    }

    const completionSource = createCompletionSource();
    if (completionSource) {
        extensions.push(autocompletion({ override: [completionSource] }));
    }

    if (opts.onChange) {
        extensions.push(EditorView.updateListener.of(update => {
            if (!update.docChanged) return;
            const text = update.state.doc.toString();
            opts.onChange(text);
            if (!hasCmLint) scheduleAnalysis(text);
        }));
    } else if (analyzeUrl && !hasCmLint) {
        extensions.push(EditorView.updateListener.of(update => {
            if (update.docChanged) scheduleAnalysis(update.state.doc.toString());
        }));
    }

    const state = EditorState.create({ doc: opts.value ?? '', extensions });
    view = new EditorView({ state, parent: container });

    if (analyzeUrl) {
        container.classList.add('has-diagnostics');
        diagPanel = document.createElement('div');
        diagPanel.className = 'etlsql-editor-diagnostics';
        diagPanel.innerHTML = '<div class="etlsql-editor-diagnostics-status" data-kind="neutral">Diagnostics pending</div><div class="etlsql-editor-diagnostics-list"></div>';
        container.appendChild(diagPanel);
        if (opts.analyzeOnLoad !== false) scheduleAnalysis(opts.value ?? '');
    }

    return {
        getValue: () => view.state.doc.toString(),
        getSelection: () => {
            const ranges = view.state.selection.ranges
                .filter(range => !range.empty)
                .map(range => view.state.doc.sliceString(range.from, range.to));
            return ranges.join('\n');
        },
        getCurrentStatement: () => currentStatement(),
        setValue: (text) => view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: text },
        }),
        analyze: () => runAnalysis(view.state.doc.toString()),
        dispose: () => {
            clearTimeout(analyzeTimer);
            analyzeAbort?.abort();
            view.destroy();
            diagPanel?.remove();
        },
    };
}

function normalizeRunTrace(result, script) {
    if (Array.isArray(result?.trace)) return result.trace;
    const rows = Array.isArray(result?.rows) ? result.rows : [];
    const columns = Array.isArray(result?.columns) ? result.columns : [];
    const elapsedMs = Number.isFinite(result?.elapsedMs) ? result.elapsedMs : 0;
    const message = result?.message || (rows.length ? `Returned ${rows.length} rows.` : 'No rows returned.');
    return [
        { type: 'clear', resetHistory: true },
        { type: 'status', status: 'running' },
        { type: 'message', level: 'sys', text: 'Designer run started.' },
        { type: 'progress', data: [
            { id: '1', name: 'Execute current statement', status: 'Completed', rowsProcessed: rows.length, durationMs: elapsedMs, isParallelBlock: false, children: [] },
        ] },
        { type: 'message', level: rows.length ? 'info' : 'warn', text: message },
        { type: 'message', level: 'sys', text: String(script || '').trim().replace(/\s+/g, ' ').slice(0, 180) },
        { type: 'results', columns, rows },
        { type: 'performance', metrics: {
            executionMs: elapsedMs,
            rowsProcessed: rows.length,
            memoryMb: 0,
            statements: [{ type: 'SELECT', totalMs: elapsedMs }],
        } },
        { type: 'done', exitCode: 0 },
    ];
}

function createScriptResultsPanel(container) {
    let messages = [];
    let progress = [];
    let resultSets = [];
    let performance = null;
    let activeTab = 'results';
    let status = 'idle';

    container.className = 'etlsql-script-results';
    container.innerHTML = `
        <div class="etlsql-script-results-tabs">
            <button type="button" data-tab="results">Results</button>
            <button type="button" data-tab="messages">Messages</button>
            <button type="button" data-tab="pipeline">Pipeline</button>
            <button type="button" data-tab="performance">Performance</button>
            <span class="etlsql-script-results-status" data-status>Idle</span>
        </div>
        <div class="etlsql-script-results-body" data-body></div>`;

    const body = container.querySelector('[data-body]');
    const statusEl = container.querySelector('[data-status]');

    function setTab(tab) {
        activeTab = tab;
        render();
    }

    function escape(value) {
        return escapeHtml(value);
    }

    function renderResults() {
        const latest = resultSets[resultSets.length - 1];
        if (!latest) return '<div class="etlsql-script-results-empty">No results yet.</div>';
        const columns = Array.isArray(latest.columns) ? latest.columns : [];
        const rows = Array.isArray(latest.rows) ? latest.rows : [];
        if (!columns.length) return '<div class="etlsql-script-results-empty">No result grid.</div>';
        const head = columns.map(c => `<th>${escape(c)}</th>`).join('');
        const dataRows = rows.map(row => `<tr>${columns.map(c => `<td>${escape(formatResultCell(row?.[c]))}</td>`).join('')}</tr>`).join('');
        return `<div class="etlsql-script-results-count">${rows.length} row${rows.length === 1 ? '' : 's'}</div><table><thead><tr>${head}</tr></thead><tbody>${dataRows || `<tr><td colspan="${columns.length}">No rows</td></tr>`}</tbody></table>`;
    }

    function renderMessages() {
        if (!messages.length) return '<div class="etlsql-script-results-empty">No messages yet.</div>';
        return `<div class="etlsql-script-message-list">${messages.map(m => `<div class="etlsql-script-message" data-level="${escape(m.level || 'info')}"><span>${escape(m.level || 'info')}</span>${escape(m.text || '')}</div>`).join('')}</div>`;
    }

    function renderPipelineRows(nodes, depth = 0) {
        return (nodes || []).map(node => `
            <tr>
                <td style="padding-left:${8 + depth * 18}px">${escape(node.name || node.id || 'Step')}</td>
                <td>${escape(node.status || '')}</td>
                <td>${Number(node.rowsProcessed || 0).toLocaleString()}</td>
                <td>${Number(node.durationMs || 0).toLocaleString()} ms</td>
            </tr>${renderPipelineRows(node.children, depth + 1)}`).join('');
    }

    function renderPipeline() {
        if (!progress.length) return '<div class="etlsql-script-results-empty">No pipeline events yet.</div>';
        const latest = progress[progress.length - 1] || [];
        return `<table><thead><tr><th>Step</th><th>Status</th><th>Rows</th><th>Duration</th></tr></thead><tbody>${renderPipelineRows(latest)}</tbody></table>`;
    }

    function renderPerformance() {
        const metrics = performance?.metrics || performance;
        if (!metrics) return '<div class="etlsql-script-results-empty">No performance metrics yet.</div>';
        const statements = Array.isArray(metrics.statements) ? metrics.statements : [];
        return `
            <div class="etlsql-script-perf-summary">
                <div><strong>${Number(metrics.executionMs || 0).toLocaleString()} ms</strong><span>Execution</span></div>
                <div><strong>${Number(metrics.rowsProcessed || 0).toLocaleString()}</strong><span>Rows</span></div>
                <div><strong>${Number(metrics.memoryMb || 0).toLocaleString()} MB</strong><span>Memory</span></div>
            </div>
            <table><thead><tr><th>Statement</th><th>Total</th></tr></thead><tbody>${statements.map(s => `<tr><td>${escape(s.type || 'Statement')}</td><td>${Number(s.totalMs || 0).toLocaleString()} ms</td></tr>`).join('')}</tbody></table>`;
    }

    function render() {
        container.querySelectorAll('[data-tab]').forEach(btn => btn.classList.toggle('active', btn.dataset.tab === activeTab));
        statusEl.textContent = status;
        if (activeTab === 'messages') body.innerHTML = renderMessages();
        else if (activeTab === 'pipeline') body.innerHTML = renderPipeline();
        else if (activeTab === 'performance') body.innerHTML = renderPerformance();
        else body.innerHTML = renderResults();
    }

    function clear() {
        messages = [];
        progress = [];
        resultSets = [];
        performance = null;
        status = 'Idle';
        render();
    }

    function post(message) {
        switch (message?.type) {
            case 'clear':
                clear();
                break;
            case 'status':
                status = message.status || status;
                break;
            case 'message':
                messages.push(message);
                break;
            case 'progress':
                progress.push(Array.isArray(message.data) ? message.data : []);
                break;
            case 'results':
                resultSets.push({ columns: message.columns || [], rows: message.rows || [] });
                activeTab = 'results';
                break;
            case 'performance':
                performance = message;
                break;
            case 'done':
                status = message.exitCode === 0 ? 'Complete' : 'Failed';
                break;
            default:
                break;
        }
        render();
    }

    container.querySelectorAll('[data-tab]').forEach(btn => btn.addEventListener('click', () => setTab(btn.dataset.tab)));
    clear();
    return {
        replay(trace) {
            for (const message of (Array.isArray(trace) ? trace : [])) post(message);
        },
        clear,
        dispose() {
            container.replaceChildren();
        },
    };
}

function formatResultCell(value) {
    if (value == null) return '';
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
}

export async function createScriptEditorWorkbench(container, opts = {}) {
    container.innerHTML = `
        <div class="etlsql-script-workbench">
            <div class="etlsql-script-workbench-toolbar">
                <strong>${escapeHtml(opts.title || 'Script')}</strong>
                <span class="etlsql-script-workbench-spacer"></span>
                <button type="button" class="btn btn-sm" data-command-palette title="Command Palette (Ctrl+Shift+P)">Commands</button>
                <button type="button" class="btn btn-sm btn-primary" data-run>Run</button>
                ${opts.onApply ? '<button type="button" class="btn btn-sm btn-primary" data-apply>Update Designer</button>' : ''}
                ${opts.onSave ? '<button type="button" class="btn btn-sm" data-save>Save</button>' : ''}
                ${opts.onClose ? '<button type="button" class="btn btn-sm" data-close>Close</button>' : ''}
            </div>
            <div class="etlsql-script-workbench-editor etlsql-editor-container" data-editor></div>
            <div class="etlsql-script-workbench-splitter" data-splitter title="Drag to resize results"></div>
            <div class="etlsql-script-workbench-results" data-results></div>
            <div class="etlsql-script-command-palette" data-palette hidden>
                <div class="etlsql-script-command-box">
                    <input type="search" data-palette-filter placeholder="Run command" autocomplete="off">
                    <div data-palette-list></div>
                </div>
            </div>
        </div>`;

    const root = container.querySelector('.etlsql-script-workbench');
    const editorHost = container.querySelector('[data-editor]');
    const resultsHost = container.querySelector('[data-results]');
    const splitter = container.querySelector('[data-splitter]');
    const palette = container.querySelector('[data-palette]');
    const paletteFilter = container.querySelector('[data-palette-filter]');
    const paletteList = container.querySelector('[data-palette-list]');
    const resultsPanel = createScriptResultsPanel(resultsHost);
    const editor = await createScriptEditor(editorHost, opts.editor || {});
    let runAbort = null;

    splitter.addEventListener('pointerdown', (event) => {
        event.preventDefault();
        splitter.setPointerCapture(event.pointerId);
        const rootRect = root.getBoundingClientRect();
        const onMove = (moveEvent) => {
            const y = Math.max(rootRect.top + 220, Math.min(rootRect.bottom - 160, moveEvent.clientY));
            const editorHeight = Math.max(180, y - rootRect.top - 42);
            const resultHeight = Math.max(140, rootRect.bottom - y - 8);
            root.style.gridTemplateRows = `auto ${editorHeight}px 8px ${resultHeight}px`;
        };
        const onUp = () => {
            splitter.removeEventListener('pointermove', onMove);
            splitter.removeEventListener('pointerup', onUp);
        };
        splitter.addEventListener('pointermove', onMove);
        splitter.addEventListener('pointerup', onUp);
    });

    async function run() {
        if (!opts.runUrl && !opts.onRun) return;
        const script = editor.getValue();
        const selection = editor.getSelection?.() || '';
        const statement = editor.getCurrentStatement?.() || '';
        const runText = selection || statement || script;
        resultsPanel.replay([{ type: 'clear', resetHistory: true }, { type: 'status', status: 'running' }, { type: 'message', level: 'sys', text: 'Running selected statement.' }]);
        try {
            runAbort?.abort();
            runAbort = new AbortController();
            const result = opts.onRun
                ? await opts.onRun({ script, selection: runText, connectionRef: opts.connectionRef || null, signal: runAbort.signal })
                : await (async () => {
                    const fetcher = opts.authFetch ?? ((url, init) => fetch(url, init));
                    const res = await fetcher(opts.runUrl, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ script, selection: runText, connectionRef: opts.connectionRef || null, documentUri: opts.documentUri || 'portal-designer' }),
                        signal: runAbort.signal,
                    });
                    if (!res?.ok) throw new Error(await res.text());
                    return await res.json();
                })();
            resultsPanel.replay(normalizeRunTrace(result, runText));
        } catch (err) {
            if (err?.name === 'AbortError') return;
            resultsPanel.replay([
                { type: 'clear', resetHistory: true },
                { type: 'message', level: 'error', text: err?.message || 'Run failed.' },
                { type: 'done', exitCode: 1 },
            ]);
        }
    }

    async function save() {
        await opts.onSave?.(editor.getValue());
    }

    async function apply() {
        await opts.onApply?.(editor.getValue());
    }

    function commandItems() {
        return [
            { id: 'run', label: 'ETL-SQL: Run Selection or Current Statement', enabled: Boolean(opts.runUrl || opts.onRun), action: run },
            { id: 'analyze', label: 'ETL-SQL: Analyze Script', enabled: typeof editor.analyze === 'function', action: () => editor.analyze() },
            { id: 'apply', label: 'ETL-SQL: Update Designer from Script', enabled: Boolean(opts.onApply), action: apply },
            { id: 'save', label: 'ETL-SQL: Save Script', enabled: Boolean(opts.onSave), action: save },
            { id: 'format', label: 'ETL-SQL: Format Document', enabled: Boolean(opts.onFormat), action: () => opts.onFormat?.(editor.getValue()) },
            { id: 'close', label: 'ETL-SQL: Close Editor', enabled: Boolean(opts.onClose), action: () => opts.onClose?.() },
        ].filter(c => c.enabled);
    }

    function renderPalette() {
        const filter = String(paletteFilter.value || '').toLowerCase();
        const commands = commandItems().filter(c => !filter || c.label.toLowerCase().includes(filter));
        paletteList.innerHTML = commands.length
            ? commands.map((c, i) => `<button type="button" data-command="${escapeHtml(c.id)}" class="${i === 0 ? 'active' : ''}">${escapeHtml(c.label)}</button>`).join('')
            : '<div class="etlsql-script-results-empty">No commands</div>';
        paletteList.querySelectorAll('[data-command]').forEach(button => {
            button.addEventListener('click', async () => {
                const cmd = commands.find(c => c.id === button.dataset.command);
                closePalette();
                await cmd?.action();
            });
        });
    }

    function openPalette() {
        palette.hidden = false;
        paletteFilter.value = '';
        renderPalette();
        paletteFilter.focus();
    }

    function closePalette() {
        palette.hidden = true;
        editorHost.querySelector('.cm-editor')?.focus();
    }

    container.querySelector('[data-command-palette]')?.addEventListener('click', openPalette);
    container.querySelector('[data-run]')?.addEventListener('click', run);
    container.querySelector('[data-apply]')?.addEventListener('click', apply);
    container.querySelector('[data-save]')?.addEventListener('click', save);
    container.querySelector('[data-close]')?.addEventListener('click', () => opts.onClose?.());
    paletteFilter.addEventListener('input', renderPalette);
    paletteFilter.addEventListener('keydown', async (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            closePalette();
            return;
        }
        if (event.key === 'Enter') {
            event.preventDefault();
            const first = paletteList.querySelector('[data-command]');
            first?.click();
        }
    });
    palette.addEventListener('mousedown', event => {
        if (event.target === palette) closePalette();
    });
    root.addEventListener('keydown', async (event) => {
        const key = String(event.key || '').toLowerCase();
        const mod = event.ctrlKey || event.metaKey;
        if (mod && event.shiftKey && key === 'p') {
            event.preventDefault();
            openPalette();
        } else if (mod && key === 'enter') {
            event.preventDefault();
            await run();
        } else if (mod && key === 's' && opts.onSave) {
            event.preventDefault();
            await save();
        }
    });

    return {
        editor,
        resultsPanel,
        getValue: () => editor.getValue(),
        run,
        dispose() {
            runAbort?.abort();
            editor.dispose();
            resultsPanel.dispose();
        },
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
 * @param {string|null} [opts.sourceRevision=null] Current source-control revision, when configured.
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
    let sourceRevision = opts.sourceRevision ?? null;
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
    scriptOverlay.innerHTML = '<div class="etlsql-designer-script-body" id="dsgn-script-workbench-host"></div>';
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
        const host = scriptOverlay.querySelector('#dsgn-script-workbench-host');
        host.innerHTML = '';
        scriptEditor = await createScriptEditorWorkbench(host, {
            title: 'Script',
            authFetch: _fetch,
            runUrl: apiBase + '/api/designer/run',
            connectionRef: opts.connectionRef || null,
            documentUri: opts.documentUri || 'portal-designer',
            editor: {
                value: text,
                analyzeUrl: apiBase + '/api/designer/analyze',
                completeUrl: apiBase + '/api/designer/complete',
                authFetch: _fetch,
                connectionRef: opts.connectionRef || null,
                documentUri: opts.documentUri || 'portal-designer',
            },
            onApply: applyScriptText,
            onClose: closeScript,
        });
    }

    function closeScript() {
        scriptOverlay.classList.remove('active');
        scriptEditor?.dispose();
        scriptEditor = null;
    }

    async function applyScript() {
        if (!scriptEditor) return;
        await applyScriptText(scriptEditor.getValue());
    }

    async function applyScriptText(script) {
        try {
            const r = await apiJson('/api/designer/parse', 'POST', { script });
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
                    '/api/designer/save',
                    'POST',
                    { reportId, scriptText: script, baseRevision: sourceRevision },
                    reportVersion);
                reportVersion = saved?.version ?? reportVersion;
                sourceRevision = saved?.sourceRevision ?? sourceRevision;
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

    saveModal.querySelector('#dsgn-modal-cancel').addEventListener('click', () => { saveModal.style.display = 'none'; });
    saveModal.querySelector('#dsgn-modal-ok').addEventListener('click', () => saveAsNew().catch(e => alert(e.message)));

    // ── Initial render ────────────────────────────────────────────────────────
    renderAll();

    return { dispose: () => { closeScript(); container.innerHTML = ''; } };
}
