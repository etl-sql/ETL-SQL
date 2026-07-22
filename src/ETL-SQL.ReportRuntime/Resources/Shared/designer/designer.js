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
 *   Portal   → src/ETL-SQL.Portal/wwwroot/designer/designer.js
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

// Cached rptsql highlight style (shared across all editor instances).
//
// CodeMirror's `defaultHighlightStyle` bakes light-mode colours into generated
// class names — its keyword purple (#708) is unreadable on a dark background and
// cannot be overridden from CSS. We map each tag to a stable class instead so the
// palette lives in designer.css and can follow the host's light/dark theme.
let _rptsqlHighlight = null;
function _getRptsqlHighlightStyle(cm) {
    if (_rptsqlHighlight) return _rptsqlHighlight;
    const { HighlightStyle, tags: t, defaultHighlightStyle } = cm;
    if (typeof HighlightStyle?.define !== 'function') return defaultHighlightStyle;
    _rptsqlHighlight = HighlightStyle.define([
        { tag: t.keyword,                  class: 'etlsql-tok-keyword' },
        { tag: t.typeName,                 class: 'etlsql-tok-type' },
        { tag: t.function(t.variableName), class: 'etlsql-tok-function' },
        { tag: t.string,                   class: 'etlsql-tok-string' },
        { tag: t.number,                   class: 'etlsql-tok-number' },
        { tag: t.operator,                 class: 'etlsql-tok-operator' },
        { tag: t.special(t.variableName),  class: 'etlsql-tok-quoted-id' },
        { tag: t.variableName,             class: 'etlsql-tok-variable' },
        { tag: t.propertyName,             class: 'etlsql-tok-property' },
        { tag: t.meta,                     class: 'etlsql-tok-meta' },
        { tag: [t.bool, t.null],           class: 'etlsql-tok-atom' },
        { tag: [t.comment, t.lineComment, t.blockComment], class: 'etlsql-tok-comment' },
    ]);
    return _rptsqlHighlight;
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
 * @param {string}      [opts.hoverUrl]        Optional endpoint for hover documentation.
 * @param {string}      [opts.connectionRef]   Optional shared connection alias for schema completions.
 * @param {Function}    [opts.authFetch]       Optional fetch wrapper used for analyzeUrl/completeUrl/hoverUrl.
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
        syntaxHighlighting, bracketMatching,
        searchKeymap, highlightSelectionMatches,
        autocompletion, completionKeymap,
        linter, lintGutter,
    } = cm;

    const analyzeUrl = opts.analyzeUrl || null;
    const completeUrl = opts.completeUrl || null;
    const hoverUrl = opts.hoverUrl || null;
    const analyzeFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const completeFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const hoverFetch = opts.authFetch ?? ((url, init) => fetch(url, init));
    const debounceMs = Number.isFinite(opts.analyzeDebounceMs) ? opts.analyzeDebounceMs : 450;
    const hasCmLint = Boolean(analyzeUrl && typeof linter === 'function');
    const completionKeys = Array.isArray(completionKeymap) ? completionKeymap : [];
    const acceptCompletionKey = completionKeys.find(binding => binding?.key === 'Enter' && typeof binding.run === 'function');
    // Reuse the bundle's Ctrl-Space -> startCompletion binding so the toolbar button and an
    // OS-safe alternate key can invoke completion without importing the minified internal.
    // Windows commonly swallows Ctrl-Space for IME/input-language switching, so we also bind Ctrl-.
    const startCompletionKey = completionKeys.find(binding => binding?.key === 'Ctrl-Space' && typeof binding.run === 'function');
    const keymaps = [
        ...(acceptCompletionKey ? [{ ...acceptCompletionKey, key: 'Tab' }] : []),
        ...(startCompletionKey ? [{ ...startCompletionKey, key: 'Ctrl-.' }] : []),
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
        syntaxHighlighting(_getRptsqlHighlightStyle(cm), { fallback: true }),
        highlightSelectionMatches(),
        keymap.of(keymaps),
        _getRptsqlLang(cm),
        // Accept schema/session explorer drags. Scoped to our private MIME type so
        // ordinary text drag-and-drop keeps CodeMirror's default behaviour.
        EditorView.domEventHandlers({
            dragover(event) {
                if (!event.dataTransfer?.types?.includes('application/x-etlsql-snippet')) return false;
                event.preventDefault();
                event.dataTransfer.dropEffect = 'copy';
                return true;
            },
            drop(event, view) {
                const snippet = event.dataTransfer?.getData('application/x-etlsql-snippet');
                if (!snippet) return false;
                event.preventDefault();
                const pos = view.posAtCoords({ x: event.clientX, y: event.clientY })
                    ?? view.state.selection.main.head;
                view.dispatch({
                    changes: { from: pos, insert: snippet },
                    selection: { anchor: pos + snippet.length },
                });
                view.focus();
                return true;
            },
        }),
        EditorState.readOnly.of(opts.readOnly ?? false),
    ];
    if (hasCmLint && typeof lintGutter === 'function') extensions.push(lintGutter());

    let analyzeTimer = null;
    let analyzeAbort = null;
    let analyzeRequest = null;
    let analyzeRequestScript = null;
    let view = null;
    let diagPanel = null;
    let hoverTimer = null;
    let hoverHideTimer = null;
    let hoverAbort = null;
    let hoverTip = null;
    let hoverKey = '';

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

    function currentDocumentUri() {
        return typeof opts.documentUri === 'function'
            ? opts.documentUri()
            : (opts.documentUri || 'portal-designer');
    }

    function currentStatement() {
        if (!view) return '';
        const script = view.state.doc.toString();
        const pos = view.state.selection.main.head;
        let start = script.lastIndexOf(';', Math.max(0, pos - 1));
        let end = script.indexOf(';', pos);
        start = start < 0 ? 0 : start + 1;
        // Keep the terminating ';' — the extracted text is parsed on its own and
        // statements like CREATE CONNECTION require it.
        end = end < 0 ? script.length : end + 1;
        return script.slice(start, end).trim();
    }

    function createCompletionSource() {
        if (!completeUrl || typeof autocompletion !== 'function') return null;
        return async (context) => {
            let word = context.matchBefore(/[\w@#&$.*]+/);
            const previous = context.state.sliceDoc(Math.max(0, context.pos - 1), context.pos);
            if (!word && context.explicit && (previous === '*' || previous === '.')) {
                word = { from: context.pos - 1, to: context.pos, text: previous };
            }
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
                    documentUri: currentDocumentUri(),
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
                    boost: item.label === 'Expand columns' ? 99 : 0,
                })),
            };
        };
    }

    function markdownToTooltipHtml(markdown) {
        const lines = String(markdown || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
        const html = [];
        let inCode = false;
        let codeLines = [];

        const renderInline = value => escapeHtml(value)
            .replace(/`([^`]+)`/g, '<code>$1</code>')
            .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

        const flushCode = () => {
            html.push(`<pre><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`);
            codeLines = [];
        };

        for (const line of lines) {
            if (line.trimStart().startsWith('```')) {
                if (inCode) {
                    flushCode();
                    inCode = false;
                } else {
                    inCode = true;
                    codeLines = [];
                }
                continue;
            }

            if (inCode) {
                codeLines.push(line);
                continue;
            }

            const heading = line.match(/^(#{1,6})\s+(.+)$/);
            if (heading) {
                const level = Math.min(6, heading[1].length);
                html.push(`<div class="etlsql-editor-hover-heading etlsql-editor-hover-heading-${level}">${renderInline(heading[2].trim())}</div>`);
                continue;
            }

            const bullet = line.match(/^\s*[-*]\s+(.+)$/);
            if (bullet) {
                html.push(`<div class="etlsql-editor-hover-bullet">${renderInline(bullet[1].trim())}</div>`);
                continue;
            }

            if (!line.trim()) {
                html.push('<div class="etlsql-editor-hover-gap"></div>');
                continue;
            }

            html.push(`<div class="etlsql-editor-hover-line">${renderInline(line.trim())}</div>`);
        }

        if (inCode) flushCode();
        return html.join('');
    }

    function wordAtPosition(state, pos) {
        const line = state.doc.lineAt(pos);
        const text = line.text;
        let offset = Math.max(0, Math.min(text.length, pos - line.from));
        const isWord = ch => /[\w@#&$]/.test(ch || '');
        if (!isWord(text[offset]) && offset > 0 && isWord(text[offset - 1])) offset--;
        if (!isWord(text[offset])) return null;

        let start = offset;
        let end = offset + 1;
        while (start > 0 && isWord(text[start - 1])) start--;
        while (end < text.length && isWord(text[end])) end++;

        return {
            word: text.slice(start, end),
            line: line.number - 1,
            column: start,
        };
    }

    function hideHover() {
        clearTimeout(hoverHideTimer);
        hoverTip?.remove();
        hoverTip = null;
        hoverKey = '';
    }

    function scheduleHideHover(delay = 180) {
        clearTimeout(hoverHideTimer);
        hoverHideTimer = setTimeout(() => hideHover(), delay);
    }

    async function showHover(evt) {
        if (!hoverUrl || !view) return;
        const pos = view.posAtCoords({ x: evt.clientX, y: evt.clientY });
        if (pos == null) {
            hideHover();
            return;
        }

        const word = wordAtPosition(view.state, pos);
        if (!word) {
            hideHover();
            return;
        }

        const nextKey = `${word.word}:${word.line}:${word.column}`;
        if (nextKey === hoverKey) return;
        hoverKey = nextKey;
        hoverAbort?.abort();
        hoverAbort = new AbortController();

        try {
            const res = await hoverFetch(hoverUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    word: word.word,
                    script: view.state.doc.toString(),
                    line: word.line,
                    column: word.column,
                    documentUri: currentDocumentUri(),
                }),
                signal: hoverAbort.signal,
            });
            if (!res?.ok) {
                hideHover();
                return;
            }

            const data = await res.json();
            if (!data?.markdown) {
                hideHover();
                return;
            }

            if (!hoverTip) {
                hoverTip = document.createElement('div');
                hoverTip.addEventListener('mouseenter', () => clearTimeout(hoverHideTimer));
                hoverTip.addEventListener('mouseleave', () => scheduleHideHover(120));
            }
            hoverTip.className = 'etlsql-editor-hover';
            hoverTip.innerHTML = markdownToTooltipHtml(data.markdown);
            document.body.appendChild(hoverTip);
            const left = Math.min(window.innerWidth - 24, evt.clientX + 14);
            const top = Math.min(window.innerHeight - 24, evt.clientY + 18);
            hoverTip.style.left = `${left}px`;
            hoverTip.style.top = `${top}px`;
        } catch (err) {
            if (err?.name !== 'AbortError') hideHover();
        }
    }

    function attachHover() {
        if (!hoverUrl) return;
        container.addEventListener('mousemove', evt => {
            clearTimeout(hoverTimer);
            hoverTimer = setTimeout(() => showHover(evt), 450);
        });
        container.addEventListener('mouseleave', () => {
            clearTimeout(hoverTimer);
            hoverAbort?.abort();
            scheduleHideHover();
        });
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
                body: JSON.stringify({ script, documentUri: currentDocumentUri() }),
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

    // The inline diagnostics list is for hosts with nowhere else to put diagnostics
    // (e.g. orchestrator.html). Hosts that own a Messages surface pass
    // `diagnosticsPanel: false` and consume opts.onDiagnostics instead — the lint
    // gutter and inline underlines already mark the offending lines in the editor.
    if (analyzeUrl && opts.diagnosticsPanel !== false) {
        container.classList.add('has-diagnostics');
        diagPanel = document.createElement('div');
        diagPanel.className = 'etlsql-editor-diagnostics';
        diagPanel.innerHTML = '<div class="etlsql-editor-diagnostics-status" data-kind="neutral" style="cursor:pointer; display:flex; align-items:center; justify-content:space-between;"><span>Diagnostics pending</span><span style="font-size:10px; color:var(--portal-text-muted, #9da7b1); padding-left:8px;">Toggle ▼</span></div><div class="etlsql-editor-diagnostics-list"></div>';
        
        const statusHeader = diagPanel.querySelector('.etlsql-editor-diagnostics-status');
        statusHeader.addEventListener('click', () => {
            const isCollapsed = diagPanel.classList.toggle('collapsed');
            container.classList.toggle('diagnostics-collapsed', isCollapsed);
            const arrow = statusHeader.querySelector('span:last-child');
            if (arrow) arrow.textContent = isCollapsed ? 'Toggle ▶' : 'Toggle ▼';
        });

        container.appendChild(diagPanel);
    }
    if (analyzeUrl && opts.analyzeOnLoad !== false) scheduleAnalysis(opts.value ?? '');
    attachHover();

    return {
        getValue: () => view.state.doc.toString(),
        getSelection: () => {
            const ranges = view.state.selection.ranges
                .filter(range => !range.empty)
                .map(range => view.state.doc.sliceString(range.from, range.to));
            return ranges.join('\n');
        },
        getCurrentStatement: () => currentStatement(),
        hasCompletion: Boolean(completeUrl),
        triggerCompletion: () => {
            if (!startCompletionKey || !view) return false;
            view.focus();
            return startCompletionKey.run(view);
        },
        setValue: (text) => view.dispatch({
            changes: { from: 0, to: view.state.doc.length, insert: text },
        }),
        analyze: () => runAnalysis(view.state.doc.toString()),
        dispose: () => {
            clearTimeout(analyzeTimer);
            clearTimeout(hoverTimer);
            clearTimeout(hoverHideTimer);
            analyzeAbort?.abort();
            hoverAbort?.abort();
            hideHover();
            view.destroy();
            diagPanel?.remove();
        },
    };
}

function normalizeRunTrace(result, script) {
    if (Array.isArray(result?.trace)) return result.trace;
    const isSuccess = result?.success !== false;
    const rows = Array.isArray(result?.rows) ? result.rows : [];
    const columns = Array.isArray(result?.columns) ? result.columns : [];
    const elapsedMs = Number.isFinite(result?.elapsedMs) ? result.elapsedMs : 0;
    const message = result?.message || (rows.length ? `Returned ${rows.length} rows.` : 'No rows returned.');
    
    const trace = [
        { type: 'clear', resetHistory: true },
        { type: 'status', status: isSuccess ? 'running' : 'failed' },
        { type: 'message', level: 'sys', text: 'Designer run started.' }
    ];
    
    if (Array.isArray(result?.messages)) {
        result.messages.forEach(m => {
            trace.push({ type: 'message', level: 'info', text: typeof m === 'string' ? m : (m.text || m.message || '') });
        });
    }
    
    if (Array.isArray(result?.diagnostics)) {
        result.diagnostics.forEach(d => {
            trace.push({ type: 'message', level: d.severity?.toLowerCase() === 'error' ? 'error' : 'warn', text: `[${d.code || 'Error'}] Line ${d.line || 0}: ${d.message}` });
        });
    }

    // Prefer the engine's real execution tree (ExecutionResult.ExecutionTree snapshot);
    // fall back to a single summary node for hosts that don't return one yet.
    const pipeline = Array.isArray(result?.pipeline) && result.pipeline.length
        ? result.pipeline
        : [{ id: '1', name: 'Execute script', status: isSuccess ? 'Completed' : 'Failed', rowsProcessed: rows.length, durationMs: elapsedMs, isParallelBlock: false, children: [] }];
    trace.push({ type: 'progress', data: pipeline });

    if (Array.isArray(result?.lineage)) {
        trace.push({ type: 'lineage', data: result.lineage });
    }

    if (isSuccess) {
        trace.push({ type: 'message', level: rows.length ? 'info' : 'warn', text: message });
        trace.push({ type: 'message', level: 'sys', text: String(script || '').trim().replace(/\s+/g, ' ').slice(0, 180) });
        trace.push({ type: 'results', columns, rows });
        trace.push({ type: 'performance', metrics: {
            executionMs: elapsedMs,
            rowsProcessed: rows.length,
            memoryMb: 0,
            statements: [{ type: 'SELECT', totalMs: elapsedMs }],
        } });
        trace.push({ type: 'done', exitCode: 0 });
    } else {
        trace.push({ type: 'message', level: 'error', text: message });
        trace.push({ type: 'done', exitCode: 1 });
    }
    return trace;
}

function toXlsxXml(columns, rows) {
    let xml = `<?xml version="1.0"?><?mso-application progid="Excel.Sheet"?><Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet" xmlns:html="http://www.w3.org/TR/REC-html40"><Worksheet ss:Name="Sheet1"><Table>`;
    // Header Row
    xml += '<Row>';
    columns.forEach(c => {
        xml += `<Cell><ss:Data ss:Type="String">${escapeHtml(c)}</ss:Data></Cell>`;
    });
    xml += '</Row>';
    // Data Rows
    rows.forEach(r => {
        xml += '<Row>';
        columns.forEach(c => {
            const val = r[c] == null ? '' : String(r[c]);
            const type = typeof r[c] === 'number' ? 'Number' : 'String';
            xml += `<Cell><ss:Data ss:Type="${type}">${escapeHtml(val)}</ss:Data></Cell>`;
        });
        xml += '</Row>';
    });
    xml += '</Table></Worksheet></Workbook>';
    return xml;
}

// Flattens the execution tree into left-to-right swimlane columns: each sequential
// step is its own column, a parallel block stacks its branches inside one column, and
// a plain container (the script root) contributes its steps rather than itself.
function flattenDagColumns(nodes, columns = []) {
    for (const node of (nodes || [])) {
        const children = Array.isArray(node.children) ? node.children : [];
        if (node.isParallelBlock && children.length) {
            columns.push({ type: 'parallel', nodes: children });
        } else if (children.length) {
            flattenDagColumns(children, columns);
        } else {
            columns.push({ type: 'single', node });
        }
    }
    return columns;
}

function renderCompactDag(nodes) {
    if (!nodes || !nodes.length) return '';
    const columns = flattenDagColumns(nodes);
    if (!columns.length) return '';

    let html = `<div class="etlsql-compact-dag">`;
    html += `<svg class="etlsql-compact-dag-svg" style="position:absolute; inset:0; width:100%; height:100%; pointer-events:none; z-index:0;"></svg>`;
    html += `<div class="etlsql-compact-dag-columns" style="display:flex; gap:60px; padding:20px; align-items:center; position:relative; z-index:1; height:100%;">`;
    
    columns.forEach((col, colIdx) => {
        html += `<div class="etlsql-compact-dag-column" style="display:flex; flex-direction:column; gap:12px; justify-content:center;">`;
        if (col.type === 'single') {
            html += renderDagCapsule(col.node, colIdx, 0);
        } else {
            col.nodes.forEach((childNode, rowIdx) => {
                html += renderDagCapsule(childNode, colIdx, rowIdx);
            });
        }
        html += `</div>`;
    });
    
    html += `</div></div>`;
    return html;
}

function renderDagCapsule(node, col, row) {
    const statusClass = (node.status || '').toLowerCase();
    const rows = Number(node.rowsProcessed || 0).toLocaleString();
    const duration = node.durationMs != null ? `${Math.round(node.durationMs).toLocaleString()} ms` : '';
    
    let statusIcon = '⚪';
    if (statusClass === 'completed' || statusClass === 'success') statusIcon = '✅';
    else if (statusClass === 'running') statusIcon = '🔄';
    else if (statusClass === 'failed' || statusClass === 'error') statusIcon = '❌';

    return `
        <div class="etlsql-dag-capsule status-${statusClass}" data-col="${col}" data-row="${row}" title="${escapeHtml(node.name)}"
             style="border: 1px solid var(--portal-border, #30363d); background: var(--portal-surface-subtle, #161b22); padding: 8px 12px; border-radius: 8px; width: 160px; font-size: 11px; z-index:2; position:relative; box-shadow: 0 4px 6px rgba(0,0,0,0.1);">
            <div style="font-weight:bold; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; display:flex; justify-content:between; align-items:center; margin-bottom:4px;">
                <span>${statusIcon} ${escapeHtml(node.name)}</span>
            </div>
            <div style="color:var(--portal-text-muted, #9da7b1); display:flex; justify-content:space-between;">
                <span>${rows} rows</span>
                <span>${duration}</span>
            </div>
        </div>
    `;
}

function updateDagLines(container) {
    const svg = container.querySelector('.etlsql-compact-dag-svg');
    if (!svg) return;
    svg.innerHTML = '';
    const containerRect = container.getBoundingClientRect();
    
    const capsules = Array.from(container.querySelectorAll('.etlsql-dag-capsule'));
    const cols = {};
    capsules.forEach(cap => {
        const col = parseInt(cap.dataset.col, 10);
        if (!cols[col]) cols[col] = [];
        cols[col].push(cap);
    });
    
    const sortedColKeys = Object.keys(cols).map(Number).sort((a,b)=>a-b);
    for (let i = 0; i < sortedColKeys.length - 1; i++) {
        const c1 = sortedColKeys[i];
        const c2 = sortedColKeys[i+1];
        const nodes1 = cols[c1];
        const nodes2 = cols[c2];
        
        nodes1.forEach(n1 => {
            const r1 = n1.getBoundingClientRect();
            const x1 = r1.right - containerRect.left;
            const y1 = (r1.top + r1.bottom) / 2 - containerRect.top;
            
            nodes2.forEach(n2 => {
                const r2 = n2.getBoundingClientRect();
                const x2 = r2.left - containerRect.left;
                const y2 = (r2.top + r2.bottom) / 2 - containerRect.top;
                
                const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                const cp1x = x1 + (x2 - x1) / 3;
                const cp2x = x1 + 2 * (x2 - x1) / 3;
                path.setAttribute('d', `M ${x1} ${y1} C ${cp1x} ${y1}, ${cp2x} ${y2}, ${x2} ${y2}`);
                path.setAttribute('stroke', 'var(--portal-border, #30363d)');
                path.setAttribute('stroke-width', '2');
                path.setAttribute('fill', 'none');
                svg.appendChild(path);
            });
        });
    }
}

export function createScriptResultsPanel(container) {
    let messages = [];
    let progress = [];
    let resultSets = [];
    let diagnostics = [];
    let performance = null;
    let activeTab = 'results';
    let status = 'idle';
    let resultFilter = '';

    container.className = 'etlsql-script-results';
    container.innerHTML = `
        <div class="etlsql-script-results-tabs">
            <button type="button" data-tab="results">Results</button>
            <button type="button" data-tab="messages">Messages</button>
            <button type="button" data-tab="pipeline">Pipeline</button>
            <button type="button" data-tab="performance">Performance</button>
            <span class="etlsql-script-results-tools" data-result-tools>
                <input type="search" data-result-filter placeholder="Filter results" autocomplete="off">
                <button type="button" data-export="csv">CSV</button>
                <button type="button" data-export="xlsx">Excel</button>
                <button type="button" data-export="json">JSON</button>
            </span>
            <span class="etlsql-script-results-status" data-status>Idle</span>
        </div>
        <div class="etlsql-script-results-body" data-body></div>`;

    const body = container.querySelector('[data-body]');
    const statusEl = container.querySelector('[data-status]');
    const filterEl = container.querySelector('[data-result-filter]');
    const toolsEl = container.querySelector('[data-result-tools]');

    function setTab(tab) {
        activeTab = tab;
        render();
        if (tab === 'pipeline') {
            setTimeout(() => {
                const dagCont = body.querySelector('.etlsql-compact-dag');
                if (dagCont) updateDagLines(dagCont);
            }, 50);
        }
    }

    function escape(value) {
        return escapeHtml(value);
    }

    let activeLineageColumn = null;
    let lineageData = [];

    function renderLineageBar() {
        if (!activeLineageColumn) return '';
        // A column name appears once per hop (m.Users.UserID -> #staging.UserID -> RESULTSET.UserID).
        // The grid shows the final result set, so prefer that entry; a plain find() would report
        // the first intermediate hop instead of the lineage of the column actually clicked.
        const columnMatches = lineageData.filter(e =>
            String(e.targetColumn || e.TargetColumn || '').toLowerCase() === String(activeLineageColumn).toLowerCase()
        );
        const match = columnMatches.find(e =>
            String(e.targetTable || e.TargetTable || '').toUpperCase() === 'RESULTSET'
        ) ?? columnMatches[0];
        let pathStr = '';
        if (match) {
            const srcT = match.sourceTables || match.SourceTables || 'source';
            const srcC = match.sourceColumns || match.SourceColumns || activeLineageColumn;
            const tgtT = match.targetTable || match.TargetTable || 'result';
            const kind = match.transformationKind || match.TransformationKind ? ` [${match.transformationKind || match.TransformationKind}]` : '';
            const desc = match.description || match.Description ? ` — ${match.description || match.Description}` : '';
            pathStr = `${escape(srcT)}.${escape(srcC)} ➔ ${escape(tgtT)}.${escape(activeLineageColumn)}${escape(kind)}${escape(desc)}`;
        } else {
            // Say so rather than drawing a plausible-looking path. A guessed
            // source.db ➔ #staging ➔ result chain reads as recorded lineage and would be
            // trusted as such — the whole point of the panel is that it reflects the run.
            pathStr = `<em>no lineage recorded for ${escape(activeLineageColumn)}</em>`;
        }
        return `
            <div class="etlsql-lineage-bar" style="background:var(--portal-accent-soft, rgba(88,166,255,0.15)); border:1px solid var(--portal-border, #30363d); padding:4px 10px; font-size:11px; display:flex; align-items:center; justify-content:space-between; margin-bottom:6px; border-radius:4px;">
                <span>📍 <strong>Lineage:</strong> ${pathStr}</span>
                <button type="button" data-close-lineage style="background:none; border:none; color:var(--portal-text-muted, #9da7b1); cursor:pointer; font-size:11px; font-weight:bold;">✕</button>
            </div>`;
    }

    function renderResults() {
        const latest = resultSets[resultSets.length - 1];
        if (!latest) return '<div class="etlsql-script-results-empty">No results yet.</div>';
        const columns = Array.isArray(latest.columns) ? latest.columns : [];
        const rows = Array.isArray(latest.rows) ? latest.rows : [];
        if (!columns.length) return '<div class="etlsql-script-results-empty">No result grid.</div>';
        const filteredRows = filterRows(rows, columns, resultFilter);
        // Bounded so an uncapped producer cannot hang the panel; the label says when it truncated.
        const { visible, label: count } = resultRenderWindow(filteredRows, rows.length, !!resultFilter);
        const head = columns.map(c => `<th data-column="${escape(c)}" style="cursor:pointer;" title="Click for column lineage">${escape(c)}</th>`).join('');
        const dataRows = visible.map(row => `<tr>${columns.map(c => `<td data-column="${escape(c)}" style="cursor:pointer;" title="Click for cell lineage">${escape(formatResultCell(row?.[c]))}</td>`).join('')}</tr>`).join('');
        return `${renderLineageBar()}<div class="etlsql-script-results-count">${escape(count)}</div><table><thead><tr>${head}</tr></thead><tbody>${dataRows || `<tr><td colspan="${columns.length}">No rows</td></tr>`}</tbody></table>`;
    }

    function diagnosticLevel(d) {
        const severity = String(d?.severity ?? '').toLowerCase();
        return (severity.includes('error') || d?.severity === 0) ? 'error' : 'warn';
    }

    function renderDiagnosticsBlock() {
        if (!diagnostics.length) return '';
        const rows = diagnostics.map(d => {
            // Analyzer positions are 0-based; the editor gutter shows them 1-based.
            const line = (Number.isFinite(d.startLine) ? d.startLine : 0) + 1;
            const column = (Number.isFinite(d.startColumn) ? d.startColumn : 0) + 1;
            return `<div class="etlsql-script-message" data-level="${diagnosticLevel(d)}"><span>${escape(d.code || d.source || 'lint')}</span>${escape(`${line}:${column}  ${d.message || ''}`)}</div>`;
        }).join('');
        return `<div class="etlsql-script-message-group"><div class="etlsql-script-message-group-title">Diagnostics</div>${rows}</div>`;
    }

    function renderMessages() {
        if (!messages.length && !diagnostics.length) return '<div class="etlsql-script-results-empty">No messages yet.</div>';
        const runMessages = messages.length
            ? `<div class="etlsql-script-message-list">${messages.map(m => `<div class="etlsql-script-message" data-level="${escape(m.level || 'info')}"><span>${escape(m.level || 'info')}</span>${escape(m.text || '')}</div>`).join('')}</div>`
            : '';
        return `${renderDiagnosticsBlock()}${runMessages}`;
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
        const dagHtml = renderCompactDag(latest);
        const tableHtml = `<table><thead><tr><th>Step</th><th>Status</th><th>Rows</th><th>Duration</th></tr></thead><tbody>${renderPipelineRows(latest)}</tbody></table>`;
        return `
            <div class="etlsql-pipeline-view" style="display:flex; flex-direction:column; height:100%; overflow:hidden;">
                ${dagHtml}
                <div class="etlsql-pipeline-table-container" style="flex:1; overflow:auto; padding-top:10px;">
                    ${tableHtml}
                </div>
            </div>
        `;
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

    // Elapsed time ticks next to the status while a run is in flight, so a long run looks
    // busy rather than hung.
    let elapsedTimer = null;
    let elapsedStart = 0;

    function formatElapsed(ms) {
        const seconds = ms / 1000;
        return seconds < 10 ? `${seconds.toFixed(1)}s` : `${Math.round(seconds)}s`;
    }

    function paintStatus() {
        if (!statusEl) return;
        statusEl.textContent = elapsedTimer
            ? `${status} · ${formatElapsed(Date.now() - elapsedStart)}`
            : status;
    }

    function renderMessagesTabLabel() {
        const tab = container.querySelector('[data-tab="messages"]');
        if (!tab) return;
        const errors = diagnostics.filter(d => diagnosticLevel(d) === 'error').length;
        tab.textContent = diagnostics.length ? `Messages (${diagnostics.length})` : 'Messages';
        tab.dataset.badge = errors ? 'error' : (diagnostics.length ? 'warn' : '');
    }

    function render() {
        container.querySelectorAll('[data-tab]').forEach(btn => btn.classList.toggle('active', btn.dataset.tab === activeTab));
        renderMessagesTabLabel();
        paintStatus();
        if (toolsEl) toolsEl.hidden = activeTab !== 'results';
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
        resultFilter = '';
        if (filterEl) filterEl.value = '';
        status = 'Idle';
        render();
    }

    function latestResults() {
        const latest = resultSets[resultSets.length - 1];
        const columns = Array.isArray(latest?.columns) ? latest.columns : [];
        const rows = Array.isArray(latest?.rows) ? latest.rows : [];
        return { columns, rows: filterRows(rows, columns, resultFilter) };
    }

    function exportResults(format) {
        const { columns, rows } = latestResults();
        if (!columns.length) return;
        let text = '';
        let mime = '';
        let ext = '';
        
        if (format === 'json') {
            text = JSON.stringify(rows, null, 2);
            mime = 'application/json';
            ext = 'json';
        } else if (format === 'xlsx') {
            text = toXlsxXml(columns, rows);
            mime = 'application/vnd.ms-excel';
            ext = 'xls';
        } else {
            text = toCsv(columns, rows);
            mime = 'text/csv';
            ext = 'csv';
        }

        const blob = new Blob([text], { type: `${mime};charset=utf-8` });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `etl-sql-results.${ext}`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }

    function post(message) {
        switch (message?.type) {
            case 'clear':
                clear();
                break;
            case 'status':
                status = message.status || status;
                // Auto switch tabs during running state
                if (status === 'running') {
                    activeTab = 'pipeline';
                }
                break;
            case 'message':
                messages.push(message);
                break;
            case 'progress':
                progress.push(Array.isArray(message.data) ? message.data : []);
                break;
            case 'lineage':
                lineageData = Array.isArray(message.data) ? message.data : [];
                break;
            case 'results':
                resultSets.push({ columns: message.columns || [], rows: message.rows || [] });
                // Focus results tab on success
                activeTab = 'results';
                break;
            case 'performance':
                performance = message;
                break;
            case 'done':
                status = message.status ?? (message.exitCode === 0 ? 'Complete' : 'Failed');
                // Switch to Messages if execution failed
                if (message.exitCode !== 0) {
                    activeTab = 'messages';
                }
                break;
            default:
                break;
        }
        render();
        if (activeTab === 'pipeline') {
            setTimeout(() => {
                const dagCont = body.querySelector('.etlsql-compact-dag');
                if (dagCont) updateDagLines(dagCont);
            }, 50);
        }
    }

    // Delegated: the results grid is re-rendered on every trace message, so binding a listener
    // per cell would re-attach hundreds of them per run.
    body.addEventListener('click', (event) => {
        if (event.target.closest('[data-close-lineage]')) {
            activeLineageColumn = null;
            render();
            return;
        }
        const cell = event.target.closest('[data-column]');
        if (!cell) return;
        activeLineageColumn = cell.dataset.column;
        render();
    });

    container.querySelectorAll('[data-tab]').forEach(btn => btn.addEventListener('click', () => setTab(btn.dataset.tab)));
    filterEl?.addEventListener('input', () => {
        resultFilter = filterEl.value || '';
        render();
    });
    container.querySelectorAll('[data-export]').forEach(btn => btn.addEventListener('click', () => exportResults(btn.dataset.export)));
    
    // Window resize handler for SVG updating
    const onResize = () => {
        if (activeTab === 'pipeline') {
            const dagCont = body.querySelector('.etlsql-compact-dag');
            if (dagCont) updateDagLines(dagCont);
        }
    };
    window.addEventListener('resize', onResize);

    clear();
    return {
        replay(trace) {
            for (const message of (Array.isArray(trace) ? trace : [])) post(message);
        },
        // Linter/parser diagnostics belong to the buffer, not to a run, so they are
        // held separately from run messages and survive clear().
        setDiagnostics(list) {
            diagnostics = Array.isArray(list) ? list : [];
            render();
        },
        startElapsed() {
            elapsedStart = Date.now();
            clearInterval(elapsedTimer);
            elapsedTimer = setInterval(paintStatus, 100);
            paintStatus();
        },
        stopElapsed() {
            clearInterval(elapsedTimer);
            elapsedTimer = null;
            paintStatus();
        },
        clear,
        dispose() {
            clearInterval(elapsedTimer);
            elapsedTimer = null;
            window.removeEventListener('resize', onResize);
            container.replaceChildren();
        },
    };

}

/**
 * Rows the grid will build DOM for in one pass.
 *
 * Not every producer bounds its result set: the Workstation and Portal run paths cap at 100/1000,
 * but the VS Code REPL streams whatever the CLI evaluated, so `SELECT * FROM big_table` arrives
 * whole. Rendering that as a single HTML string hangs the panel. Export is unaffected because it
 * reads the filtered rows directly rather than what was drawn.
 */
export const MAX_RENDERED_ROWS = 5000;

/**
 * Splits filtered rows into what to draw and what to say about it. Pure so the cap is testable
 * without a DOM — the point is that a truncated grid says so rather than quietly showing less.
 */
export function resultRenderWindow(filteredRows, totalRows, isFiltered, cap = MAX_RENDERED_ROWS) {
    const filtered = Array.isArray(filteredRows) ? filteredRows : [];
    const total = Number.isFinite(totalRows) ? totalRows : filtered.length;
    const truncated = filtered.length > cap;
    const visible = truncated ? filtered.slice(0, cap) : filtered;

    const plural = n => `${n.toLocaleString()} row${n === 1 ? '' : 's'}`;
    let label;
    if (truncated) {
        label = isFiltered
            ? `showing first ${plural(visible.length)} of ${filtered.length.toLocaleString()} matched (${plural(total)} total)`
            : `showing first ${plural(visible.length)} of ${plural(total)}`;
    } else {
        label = isFiltered ? `${filtered.length.toLocaleString()} of ${plural(total)}` : plural(total);
    }

    return { visible, truncated, label };
}

// Exported for scripts/test-result-grid-ui.mjs. These carry the result grid's behaviour — what the
// filter box matches, how a value becomes display text, what CSV export writes, and how many rows
// are drawn — and are pure, so they are testable without a DOM. The rendering around them is not.
export function filterRows(rows, columns, filter) {
    const term = String(filter || '').trim().toLowerCase();
    if (!term) return rows;
    return rows.filter(row => columns.some(c => formatResultCell(row?.[c]).toLowerCase().includes(term)));
}

export function toCsv(columns, rows) {
    const escapeCsv = value => {
        const text = formatResultCell(value);
        return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    };
    return [
        columns.map(escapeCsv).join(','),
        ...rows.map(row => columns.map(c => escapeCsv(row?.[c])).join(',')),
    ].join('\r\n');
}

export function formatResultCell(value) {
    if (value == null) return '';
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
}

// Toolbar iconography. Inline stroke SVGs (currentColor, 16px) keep the workbench
// self-contained — no icon font or sprite sheet to ship to VS Code / Player / Portal.
const _TOOLBAR_ICONS = {
    sidebar: '<path d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 12.5z"/><path d="M6.5 2v12"/>',
    theme: '<path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7z"/>',
    commands: '<path d="m4 5 3 3-3 3"/><path d="M8.5 11h4"/>',
    suggest: '<path d="m8 2 1.6 3.9L13.5 7.5 9.6 9.1 8 13l-1.6-3.9L2.5 7.5l3.9-1.6z"/>',
    runSelected: '<path d="M2.5 3.5h3"/><path d="M2.5 12.5h3"/><path d="m7.5 3.5 6 4.5-6 4.5z"/>',
    run: '<path d="m4 2.5 9 5.5-9 5.5z"/>',
    preview: '<path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8"/><circle cx="8" cy="8" r="1.9"/>',
    apply: '<path d="M2 3.5h12"/><path d="M2 8h12"/><path d="M2 12.5h7"/>',
    save: '<path d="M3 2.5h7.5L13.5 5.5V13a.5.5 0 0 1-.5.5H3a.5.5 0 0 1-.5-.5V3a.5.5 0 0 1 .5-.5"/><path d="M5 2.5v4h5v-4"/><path d="M5 13.5v-4h6v4"/>',
    close: '<path d="m4 4 8 8"/><path d="m12 4-8 8"/>',
    cancel: '<rect x="4" y="4" width="8" height="8" rx="1"/>',
    format: '<path d="M2 3.5h12"/><path d="M2 7.5h8"/><path d="M2 11.5h12"/><path d="M2 15.5h6"/>',
    formatSettings: '<path d="M8 2.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11z"/><path d="M8 1v2m0 10v2m-6-7h2m10 0h2m-2.1-4.9-1.4 1.4m-7 7-1.4 1.4m0-9.8 1.4 1.4m7 7 1.4 1.4"/>',
};

function toolbarIcon(name) {
    return `<svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.4"
        stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${_TOOLBAR_ICONS[name] || ''}</svg>`;
}

// Icon-only by default; `label` is reserved for the primary action so the toolbar
// still reads at a glance. Everything carries a title + aria-label for a11y.
function toolbarButton({ attr, icon, title, label, primary, key }) {
    const hint = key ? `${title} (${key})` : title;
    return `<button type="button" class="etlsql-tool-btn${primary ? ' etlsql-tool-btn-primary' : ''}${label ? ' etlsql-tool-btn-labelled' : ''}"
        ${attr} title="${escapeHtml(hint)}" aria-label="${escapeHtml(title)}">${toolbarIcon(icon)}${label ? `<span>${escapeHtml(label)}</span>` : ''}</button>`;
}

export async function createScriptEditorWorkbench(container, opts = {}) {
    const savedTheme = localStorage.getItem('portal-theme') || 'light';
    if (savedTheme === 'dark') {
        document.body.classList.add('theme-dark');
    } else {
        document.body.classList.remove('theme-dark');
    }

    // Sections are opt-in per host: the Workstation has a real file workspace and git,
    // the Portal has neither (its catalog is folders/reports, and git write-back is a
    // separate roadmap item), so it enables only schema + session.
    // `showSidebar: true` remains shorthand for "everything".
    const sidebarOpts = opts.sidebar ?? (opts.showSidebar ? { workspace: true, schema: true, session: true, git: true } : null);
    const hasSidebar = Boolean(sidebarOpts);
    const showWorkspace = Boolean(sidebarOpts?.workspace);
    const showSchema = Boolean(sidebarOpts?.schema);
    const showSession = Boolean(sidebarOpts?.session);
    const showGit = Boolean(sidebarOpts?.git);

    container.innerHTML = `
        <div class="etlsql-script-workbench ${hasSidebar ? 'etlsql-script-workbench-with-sidebar' : ''}">
            <div class="etlsql-script-workbench-toolbar">
                <strong class="etlsql-script-workbench-title">${escapeHtml(opts.title || 'Script')}</strong>
                <span class="etlsql-workbench-branch-badge" data-workbench-branch style="display:none; font-size:11px; font-weight:500; color:var(--portal-text-soft, #9da7b1); background:var(--portal-surface-subtle, rgba(255,255,255,0.06)); padding:2px 8px; border-radius:12px; border:1px solid var(--portal-border, #30363d); margin-left:8px;"></span>
                <span class="etlsql-script-workbench-spacer"></span>
                ${hasSidebar ? toolbarButton({ attr: 'data-toggle-sidebar', icon: 'sidebar', title: 'Toggle sidebar' }) : ''}
                ${toolbarButton({ attr: 'data-toggle-theme', icon: 'theme', title: 'Toggle dark/light mode' })}
                ${toolbarButton({ attr: 'data-command-palette', icon: 'commands', title: 'Command palette', key: 'Ctrl+Shift+P' })}
                ${opts.editor?.completeUrl ? toolbarButton({ attr: 'data-suggest', icon: 'suggest', title: 'Suggest completions', key: 'Ctrl+Space' }) : ''}
                ${opts.previewApiUrl ? toolbarButton({ attr: 'data-preview', icon: 'preview', title: 'Preview report' }) : ''}
                ${opts.onApply ? toolbarButton({ attr: 'data-apply', icon: 'apply', title: 'Update designer from script' }) : ''}
                ${toolbarButton({ attr: 'data-format', icon: 'format', title: 'Format document', key: 'Shift+Alt+F' })}
                ${toolbarButton({ attr: 'data-format-settings', icon: 'formatSettings', title: 'Formatter settings (.etlsql-formatter.json)' })}
                ${opts.onSave ? toolbarButton({ attr: 'data-save', icon: 'save', title: 'Save', key: 'Ctrl+S' }) : ''}
                ${opts.onClose ? toolbarButton({ attr: 'data-close', icon: 'close', title: 'Close editor' }) : ''}
                ${opts.onExit ? toolbarButton({ attr: 'data-exit', icon: 'close', title: 'Exit process', label: 'Exit' }) : ''}
                <span class="etlsql-toolbar-divider"></span>
                ${toolbarButton({ attr: 'data-run-selected', icon: 'runSelected', title: 'Run selection or statement under cursor', key: 'Ctrl+Enter' })}
                ${toolbarButton({ attr: 'data-run', icon: 'run', title: 'Run script', key: 'Ctrl+Shift+Enter', label: 'Run', primary: true })}
                ${toolbarButton({ attr: 'data-cancel-run', icon: 'cancel', title: 'Cancel the running script', key: 'Esc', label: 'Cancel' })}
            </div>
            
            ${hasSidebar ? `
            <div class="etlsql-script-workbench-body" style="display:flex; height: calc(100% - 38px); overflow:hidden; position:relative; z-index: 10;">
                <aside class="etlsql-script-workbench-sidebar" data-sidebar>
                    ${showWorkspace ? `
                    <div class="etlsql-sidebar-section-header">
                        <span>Workspace</span>
                        <button type="button" class="etlsql-sidebar-action" data-open-directory>Open folder</button>
                    </div>
                    <div class="etlsql-sidebar-section" data-sidebar-files>Loading workspace…</div>` : ''}

                    ${showSchema ? `
                    <div class="etlsql-sidebar-section-header"><span>Schema explorer</span></div>
                    <div class="etlsql-sidebar-section" data-sidebar-schema>Loading connections…</div>` : ''}

                    ${showSession ? `
                    <div class="etlsql-sidebar-section-header"><span>Session</span></div>
                    <div class="etlsql-sidebar-section" data-sidebar-variables>Loading session…</div>` : ''}

                    ${showGit ? `
                    <div class="etlsql-sidebar-section-header" data-sidebar-git-header><span>Source control</span></div>
                    <div class="etlsql-sidebar-section" data-sidebar-git>Loading git…</div>` : ''}
                </aside>
                <div class="etlsql-script-workbench-content" style="flex:1; display:grid; grid-template-rows: minmax(100px, 1fr) 8px minmax(36px, 34%); min-width:0; height:100%; position:relative;">
                    <div class="etlsql-script-workbench-editor etlsql-editor-container" data-editor></div>
                    <div class="etlsql-script-workbench-splitter" data-splitter title="Drag to resize results" style="cursor:row-resize; height:8px; border-top:1px solid var(--portal-border, #30363d); border-bottom:1px solid var(--portal-border, #30363d); background:var(--portal-surface-subtle, #161b22);"></div>
                    <div class="etlsql-script-workbench-results" data-results></div>
                </div>
            </div>
            ` : `
            <div class="etlsql-script-workbench-editor etlsql-editor-container" data-editor></div>
            <div class="etlsql-script-workbench-splitter" data-splitter title="Drag to resize results"></div>
            <div class="etlsql-script-workbench-results" data-results></div>
            `}
            
            ${opts.previewApiUrl ? `
            <div class="etlsql-script-workbench-preview" data-preview-overlay>
                <div class="etlsql-script-workbench-preview-toolbar">
                    <strong>Preview</strong>
                    <span class="etlsql-script-workbench-preview-status" data-preview-status></span>
                    <span class="etlsql-script-workbench-spacer"></span>
                    <button type="button" class="btn btn-sm" data-preview-refresh title="Re-run the report and refresh the preview">↻ Refresh</button>
                    <button type="button" class="btn btn-sm" data-preview-close>Close</button>
                </div>
                <iframe data-preview-frame title="Report preview" sandbox="allow-scripts allow-same-origin"></iframe>
            </div>` : ''}
            
            <div class="etlsql-script-command-palette" data-palette hidden>
                <div class="etlsql-script-command-box">
                    <input type="search" data-palette-filter placeholder="Run command" autocomplete="off">
                    <div data-palette-list></div>
                </div>
            </div>
        </div>`;

    let currentFilePath = opts.title && opts.title !== 'Script' ? opts.title : '';
    let activeDirectoryHandle = null;
    let activeFileHandle = null;
    const originalDocUri = opts.editor?.documentUri;
    const getDocumentUri = () => {
        if (currentFilePath) return currentFilePath;
        if (typeof originalDocUri === 'function') return originalDocUri();
        return originalDocUri || 'portal-designer';
    };

    const root = container.querySelector('.etlsql-script-workbench');
    const editorHost = container.querySelector('[data-editor]');
    const resultsHost = container.querySelector('[data-results]');
    const splitter = container.querySelector('[data-splitter]');
    const palette = container.querySelector('[data-palette]');
    const paletteFilter = container.querySelector('[data-palette-filter]');
    const paletteList = container.querySelector('[data-palette-list]');
    const resultsPanel = createScriptResultsPanel(resultsHost);

    const editorOpts = {
        ...(opts.editor || {}),
        documentUri: getDocumentUri,
        // The workbench owns a Messages tab, so the editor's own inline diagnostics
        // list would be a third copy of the same information (gutter + underline).
        diagnosticsPanel: false,
        onDiagnostics: (list) => {
            resultsPanel.setDiagnostics(list);
            opts.editor?.onDiagnostics?.(list);
            // Analysis is what registers CREATE CONNECTION / #temp metadata on the
            // server, so this is the point where the sidebar has something new to show.
            scheduleSidebarRefresh();
        },
    };
    const editor = await createScriptEditor(editorHost, editorOpts);
    let runAbort = null;

    const content = hasSidebar ? root.querySelector('.etlsql-script-workbench-content') : root;

    splitter.addEventListener('pointerdown', (event) => {
        event.preventDefault();
        splitter.setPointerCapture(event.pointerId);
        const rect = content.getBoundingClientRect();
        
        const toolbar = root.querySelector('.etlsql-script-workbench-toolbar');
        const toolbarHeight = (hasSidebar || !toolbar) ? 0 : toolbar.getBoundingClientRect().height;

        const onMove = (moveEvent) => {
            const minEditor = 100;
            const minResults = 36;
            const splitterHeight = 8;
            
            const minY = rect.top + toolbarHeight + minEditor + (splitterHeight / 2);
            const maxY = rect.bottom - minResults - (splitterHeight / 2);
            const y = Math.max(minY, Math.min(maxY, moveEvent.clientY));
            
            const editorHeight = y - (rect.top + toolbarHeight) - (splitterHeight / 2);
            const resultHeight = rect.bottom - y - (splitterHeight / 2);
            
            if (hasSidebar) {
                content.style.gridTemplateRows = `${editorHeight}px ${splitterHeight}px ${resultHeight}px`;
            } else {
                content.style.gridTemplateRows = `auto ${editorHeight}px ${splitterHeight}px ${resultHeight}px`;
            }
        };
        const onUp = () => {
            splitter.removeEventListener('pointermove', onMove);
            splitter.removeEventListener('pointerup', onUp);
        };
        splitter.addEventListener('pointermove', onMove);
        splitter.addEventListener('pointerup', onUp);
    });

    async function loadFile(filePath) {
        try {
            const fetcher = opts.authFetch ?? fetch;
            const url = `/api/files?path=${encodeURIComponent(filePath)}`;
            const res = await fetcher(url);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            if (data && typeof data.content === 'string') {
                editor.setValue(data.content);
                currentFilePath = filePath;
                const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = filePath;
                }
                if (hasSidebar) {
                    loadSchema();
                    loadSession();
                    loadGit();
                }
            }
        } catch (err) {
            console.error(err);
            alert(`Error loading file: ${err.message}`);
        }
    }

    async function loadFiles() {
        const filesEl = root.querySelector('[data-sidebar-files]');
        if (!filesEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const res = await fetcher(opts.workspaceUrl || '/api/workspace');
            if (!res.ok) throw new Error(`Workspace listing unavailable (HTTP ${res.status})`);
            const data = await res.json();
            if (data && data.files) {
                filesEl.innerHTML = '';
                data.files.forEach(f => {
                    const item = document.createElement('div');
                    item.className = 'etlsql-sidebar-file';
                    item.innerHTML = `<span class="etlsql-tree-label">${escapeHtml(f.path)}</span><span class="etlsql-tree-type">${Math.round(f.size / 10.24) / 100} KB</span>`;

                    if (f.path === currentFilePath) item.classList.add('active');

                    item.addEventListener('click', async () => {
                        filesEl.querySelectorAll('.etlsql-sidebar-file').forEach(e => e.classList.remove('active'));
                        item.classList.add('active');
                        if (opts.onFileSelect) {
                            await opts.onFileSelect(f.path);
                        } else {
                            await loadFile(f.path);
                        }
                    });
                    filesEl.appendChild(item);
                });
            } else {
                filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">No files.</div>';
            }
        } catch (err) {
            filesEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    async function renderDirectoryTree(dirHandle) {
        const filesEl = root.querySelector('[data-sidebar-files]');
        if (!filesEl) return;
        filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">Loading...</div>';
        try {
            const files = [];
            async function traverse(handle, relativePath = '') {
                for await (const entry of handle.values()) {
                    const fullPath = relativePath ? `${relativePath}/${entry.name}` : entry.name;
                    if (entry.kind === 'file') {
                        if (entry.name.endsWith('.etlsql') || entry.name.endsWith('.rptsql') || entry.name.endsWith('.sql')) {
                            files.push({
                                path: fullPath,
                                name: entry.name,
                                handle: entry
                            });
                        }
                    } else if (entry.kind === 'directory') {
                        await traverse(entry, fullPath);
                    }
                }
            }
            await traverse(dirHandle);
            files.sort((a, b) => a.path.localeCompare(b.path));
            if (files.length === 0) {
                filesEl.innerHTML = '<div style="color:var(--portal-text-muted, #9da7b1); padding:4px;">No script files.</div>';
                return;
            }
            filesEl.innerHTML = '';
            files.forEach(f => {
                const item = document.createElement('div');
                item.className = 'etlsql-sidebar-file';
                item.innerHTML = `<span class="etlsql-tree-label">${escapeHtml(f.path)}</span>`;
                if (f.path === currentFilePath) item.classList.add('active');
                item.addEventListener('click', async () => {
                    filesEl.querySelectorAll('.etlsql-sidebar-file').forEach(e => e.classList.remove('active'));
                    item.classList.add('active');
                    try {
                        const file = await f.handle.getFile();
                        const content = await file.text();
                        editor.setValue(content);
                        currentFilePath = f.path;
                        activeFileHandle = f.handle;
                        const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                        if (titleEl) titleEl.textContent = f.name;
                    } catch (e) {
                        alert('Failed to read file: ' + e.message);
                    }
                });
                filesEl.appendChild(item);
            });
        } catch (err) {
            filesEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    function metadataApiBase() {
        const runUrl = opts.runUrl || '';
        return runUrl.includes('/api/designer/run') ? runUrl.split('/api/designer/run')[0] : '';
    }

    // ── Sidebar tree primitives ────────────────────────────────────────────────
    // Shared by the schema explorer and the session explorer so connections, tables,
    // temp tables and columns all expand and drag identically.

    // A private MIME type keeps CodeMirror's own text drag/drop untouched — we only
    // intercept drops that originated from one of these tree rows.
    const SNIPPET_MIME = 'application/x-etlsql-snippet';

    function makeDraggable(el, snippet) {
        el.draggable = true;
        el.title = `Drag into the editor to insert "${snippet}"`;
        el.addEventListener('dragstart', (event) => {
            event.stopPropagation();
            event.dataTransfer.setData(SNIPPET_MIME, snippet);
            event.dataTransfer.setData('text/plain', snippet);
            event.dataTransfer.effectAllowed = 'copy';
            el.classList.add('dragging');
        });
        el.addEventListener('dragend', () => el.classList.remove('dragging'));
    }

    function makeColumnRow(column, snippet) {
        const row = document.createElement('div');
        row.className = 'etlsql-tree-row etlsql-tree-column';
        const type = column.type ?? column.dataType ?? '';
        row.innerHTML = `<span class="etlsql-tree-indent"></span><span class="etlsql-tree-label">${escapeHtml(column.name)}</span>`
            + (type ? `<span class="etlsql-tree-type">${escapeHtml(type)}</span>` : '');
        makeDraggable(row, snippet);
        return row;
    }

    // Builds a collapsible node. `loadChildren` runs once, on first expand.
    function makeTreeNode({ label, icon, className, snippet, loadChildren }) {
        const node = document.createElement('div');
        node.className = 'etlsql-tree-node';

        const header = document.createElement('div');
        header.className = `etlsql-tree-row etlsql-tree-header ${className || ''}`;
        header.innerHTML = `<span class="etlsql-tree-caret">▶</span><span class="etlsql-tree-icon">${icon}</span><span class="etlsql-tree-label">${escapeHtml(label)}</span>`;

        const children = document.createElement('div');
        children.className = 'etlsql-tree-children';

        let loaded = false;
        header.addEventListener('click', async (event) => {
            event.stopPropagation();
            const expanded = node.classList.toggle('expanded');
            if (expanded && !loaded) {
                loaded = true;
                children.innerHTML = '<div class="etlsql-tree-note">Loading…</div>';
                try {
                    await loadChildren(children);
                } catch (err) {
                    children.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
                    loaded = false;
                }
            }
        });

        if (snippet) makeDraggable(header, snippet);
        node.append(header, children);
        return node;
    }

    // Re-fetching metadata after every keystroke-triggered analysis would collapse any
    // tree the user had expanded, so each section only re-renders when its data changed.
    let schemaSignature = null;
    let sessionSignature = null;
    let sidebarRefreshTimer = null;

    function scheduleSidebarRefresh() {
        if (!showSchema && !showSession) return;
        clearTimeout(sidebarRefreshTimer);
        sidebarRefreshTimer = setTimeout(() => {
            if (showSchema) loadSchema();
            if (showSession) loadSession();
        }, 200);
    }

    async function loadSchema() {
        const schemaEl = root.querySelector('[data-sidebar-schema]');
        if (!schemaEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const docUri = getDocumentUri();

            const res = await fetcher(`${apiBase}/api/session/metadata?documentUri=${encodeURIComponent(docUri)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            const signature = JSON.stringify(data?.connections ?? []);
            if (signature === schemaSignature) return;
            schemaSignature = signature;
            if (data && data.connections && data.connections.length > 0) {
                schemaEl.innerHTML = '';
                data.connections.forEach(conn => {
                    schemaEl.appendChild(makeTreeNode({
                        label: conn,
                        icon: '🔌',
                        className: 'etlsql-tree-connection',
                        loadChildren: async (host) => {
                            const schemaRes = await fetcher(`${apiBase}/api/designer/schema?connection=${encodeURIComponent(conn)}&documentUri=${encodeURIComponent(docUri)}`);
                            if (!schemaRes.ok) throw new Error(`HTTP ${schemaRes.status}`);
                            const schemaData = await schemaRes.json();
                            const tables = schemaData?.tables ?? [];
                            if (!tables.length) {
                                host.innerHTML = '<div class="etlsql-tree-note">No tables or views.</div>';
                                return;
                            }
                            host.innerHTML = '';
                            for (const table of tables) {
                                host.appendChild(makeTreeNode({
                                    label: table.name,
                                    icon: '▤',
                                    className: 'etlsql-tree-table',
                                    snippet: `${conn}.${table.name}`,
                                    loadChildren: (columnHost) => {
                                        const columns = table.columns ?? [];
                                        if (!columns.length) {
                                            columnHost.innerHTML = '<div class="etlsql-tree-note">No columns</div>';
                                            return;
                                        }
                                        columnHost.innerHTML = '';
                                        for (const column of columns) {
                                            columnHost.appendChild(makeColumnRow(column, column.name));
                                        }
                                    },
                                }));
                            }
                        },
                    }));
                });
            } else {
                schemaEl.innerHTML = '<div class="etlsql-tree-note">No active connections.</div>';
            }
        } catch (err) {
            schemaSignature = null;
            schemaEl.innerHTML = `<div style="color:var(--portal-danger, #ff7b72);">${escapeHtml(err.message)}</div>`;
        }
    }

    async function loadSession() {
        const varsEl = root.querySelector('[data-sidebar-variables]');
        if (!varsEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const apiBase = metadataApiBase();
            const docUri = getDocumentUri();

            const res = await fetcher(`${apiBase}/api/session/metadata?documentUri=${encodeURIComponent(docUri)}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            const signature = JSON.stringify([data?.variables ?? [], data?.tempTables ?? []]);
            if (signature === sessionSignature) return;
            sessionSignature = signature;
            const variables = data?.variables ?? [];
            const tempTables = data?.tempTables ?? [];
            varsEl.innerHTML = '';

            for (const variable of variables) {
                const row = document.createElement('div');
                row.className = 'etlsql-tree-row etlsql-tree-variable';
                row.innerHTML = `<span class="etlsql-tree-icon">@</span><span class="etlsql-tree-label">${escapeHtml(variable.name)}</span>`
                    + `<span class="etlsql-tree-value">${escapeHtml(String(variable.value ?? ''))}</span>`
                    + (variable.type ? `<span class="etlsql-tree-type">${escapeHtml(variable.type)}</span>` : '');
                makeDraggable(row, variable.name);
                varsEl.appendChild(row);
            }

            // Temp tables expand to their columns exactly like a schema table does.
            for (const table of tempTables) {
                varsEl.appendChild(makeTreeNode({
                    label: table.name,
                    icon: '▦',
                    className: 'etlsql-tree-temp',
                    snippet: table.name,
                    loadChildren: (columnHost) => {
                        const columns = (table.columns ?? []).map(c => (typeof c === 'string' ? { name: c, type: '' } : c));
                        if (!columns.length) {
                            columnHost.innerHTML = '<div class="etlsql-tree-note">No columns</div>';
                            return;
                        }
                        columnHost.innerHTML = '';
                        for (const column of columns) {
                            columnHost.appendChild(makeColumnRow(column, column.name));
                        }
                    },
                }));
            }

            if (!variables.length && !tempTables.length) {
                varsEl.innerHTML = '<div class="etlsql-tree-note">No variables/temp tables.</div>';
            }
        } catch (err) {
            sessionSignature = null;
            varsEl.innerHTML = `<div class="etlsql-tree-note etlsql-tree-error">${escapeHtml(err.message)}</div>`;
        }
    }

    function hideGitSection() {
        // Not every host exposes source control (see the Git Integration item in the
        // Unified Script Editor Roadmap). Hide the section rather than parking a fetch
        // error in the sidebar.
        root.querySelector('[data-sidebar-git]')?.remove();
        root.querySelector('[data-sidebar-git-header]')?.remove();
    }

    async function loadGit() {
        const gitEl = root.querySelector('[data-sidebar-git]');
        const branchBadge = root.querySelector('[data-workbench-branch]');
        if (!gitEl) return;
        try {
            const fetcher = opts.authFetch ?? fetch;
            const res = await fetcher(opts.gitStatusUrl || '/api/git/status');
            if (!res.ok) { hideGitSection(); if (branchBadge) branchBadge.style.display = 'none'; return; }
            const data = await res.json();
            if (data && (data.branch || data.isGitRepository !== false)) {
                const branchName = data.branch || opts.gitStatus?.branch || '';
                if (branchBadge) {
                    if (branchName) {
                        branchBadge.textContent = `🌿 ${branchName}`;
                        branchBadge.style.display = 'inline-block';
                    } else {
                        branchBadge.style.display = 'none';
                    }
                }

                let gitHtml = `<div class="etlsql-tree-row etlsql-tree-header">🌿 ${escapeHtml(branchName)}</div>`;
                if (data.staged && data.staged.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Staged</div>';
                    gitHtml += data.staged.map(f => `<div class="etlsql-tree-row" style="color:var(--portal-success, #117853);">✓ ${escapeHtml(f)}</div>`).join('');
                }
                if (data.modified && data.modified.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Modified</div>';
                    gitHtml += data.modified.map(f => `<div class="etlsql-tree-row" style="color:var(--portal-warning, #a05a00);">📝 ${escapeHtml(f)}</div>`).join('');
                }
                if (data.untracked && data.untracked.length > 0) {
                    gitHtml += '<div class="etlsql-tree-note">Untracked</div>';
                    gitHtml += data.untracked.map(f => `<div class="etlsql-tree-row">➕ ${escapeHtml(f)}</div>`).join('');
                }
                gitHtml += `
                    <input type="text" data-git-comment placeholder="Commit message..." style="background:var(--portal-surface, #0f141b); color:var(--portal-text, #e6edf3); border:1px solid var(--portal-border, #30363d); padding:4px 6px; border-radius:4px; font-size:11px; margin-top:6px; outline:none; width: 100%;">
                    <button type="button" class="btn btn-sm btn-primary" data-git-commit style="margin-top:4px; font-size:11px; font-weight:600; padding:4px; width: 100%;">Commit Changes</button>
                `;
                gitEl.innerHTML = gitHtml;
                
                const commitBtn = gitEl.querySelector('[data-git-commit]');
                const commentInput = gitEl.querySelector('[data-git-comment]');
                commitBtn?.addEventListener('click', async () => {
                    const comment = commentInput.value || '';
                    if (!comment.trim()) { alert('Please enter a commit message.'); return; }
                    commitBtn.disabled = true;
                    try {
                        const cRes = await fetcher('/api/git/commit', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ comment })
                        });
                        const cData = await cRes.json();
                        if (cData.committed) {
                            alert(`Committed successfully! Rev: ${cData.sourceRevision || cData.rev || ''}`);
                            await loadGit();
                            await loadFiles();
                        } else {
                            alert(cData.message || 'Nothing to commit.');
                        }
                    } catch (e) {
                        alert('Commit failed: ' + e.message);
                    } finally {
                        commitBtn.disabled = false;
                    }
                });
            } else {
                hideGitSection();
                if (branchBadge) branchBadge.style.display = 'none';
            }
        } catch {
            hideGitSection();
            if (branchBadge) branchBadge.style.display = 'none';
        }
    }

    const toggleBtn = container.querySelector('[data-toggle-sidebar]');
    const sidebar = container.querySelector('[data-sidebar]');
    toggleBtn?.classList.add('active'); // sidebar starts visible
    toggleBtn?.addEventListener('click', () => {
        if (sidebar.style.display === 'none') {
            sidebar.style.display = 'flex';
            toggleBtn.classList.add('active');
        } else {
            sidebar.style.display = 'none';
            toggleBtn.classList.remove('active');
        }
    });

    const toggleThemeBtn = container.querySelector('[data-toggle-theme]');
    toggleThemeBtn?.addEventListener('click', () => {
        const isDark = document.body.classList.toggle('theme-dark');
        localStorage.setItem('portal-theme', isDark ? 'dark' : 'light');
        renderCanvas();
    });

    const openDirBtn = container.querySelector('[data-open-directory]');
    openDirBtn?.addEventListener('click', async () => {
        try {
            activeDirectoryHandle = await window.showDirectoryPicker();
            await renderDirectoryTree(activeDirectoryHandle);
        } catch (err) {
            console.error('Failed to open directory:', err);
        }
    });

    if (showWorkspace) loadFiles();
    if (showSchema) loadSchema();
    if (showSession) loadSession();
    if (showGit) loadGit();

    // scope: 'script' runs the whole file (Run); 'selection' runs the highlighted text
    // or the statement under the cursor (Run Selected) — see the roadmap's toolbar schema.
    // Hosts signal a destructive-statement refusal with a RUN_DESTRUCTIVE diagnostic code.
    function isDestructiveRefusal(result) {
        return result?.success === false
            && (result.diagnostics ?? []).some(d => d?.code === 'RUN_DESTRUCTIVE');
    }

    function setRunning(isRunning) {
        root.classList.toggle('is-running', isRunning);
        const runBtn = container.querySelector('[data-run]');
        const runSelBtn = container.querySelector('[data-run-selected]');
        if (runBtn) runBtn.disabled = isRunning;
        if (runSelBtn) runSelBtn.disabled = isRunning;
    }

    async function run(scope = 'script', confirmDestructive = false) {
        if (!opts.runUrl && !opts.onRun) return;
        const script = editor.getValue();
        let runText = script;
        if (scope === 'selection') {
            runText = editor.getSelection?.() || editor.getCurrentStatement?.() || script;
        }
        resultsPanel.replay([
            { type: 'clear', resetHistory: true },
            { type: 'status', status: 'running' },
            { type: 'message', level: 'sys', text: scope === 'selection' ? 'Running selected statement.' : 'Running script.' },
        ]);
        setRunning(true);
        resultsPanel.startElapsed();
        try {
            runAbort?.abort();
            runAbort = new AbortController();
            const result = opts.onRun
                ? await opts.onRun({ script, selection: runText, connectionRef: opts.connectionRef || null, confirmDestructive, signal: runAbort.signal })
                : await (async () => {
                    const fetcher = opts.authFetch ?? ((url, init) => fetch(url, init));
                    const res = await fetcher(opts.runUrl, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ script, selection: runText, connectionRef: opts.connectionRef || null, documentUri: getDocumentUri(), confirmDestructive }),
                        signal: runAbort.signal,
                    });

                    if (!res?.ok) throw new Error(await res.text());
                    return await res.json();
                })();

            // The host refuses destructive statements until they are acknowledged. Ask once, then
            // re-run confirmed rather than making the user edit the script to get past the guard.
            if (!confirmDestructive && isDestructiveRefusal(result)) {
                setRunning(false);
                resultsPanel.stopElapsed();
                if (confirm(`${result.message}\n\nRun anyway?`)) {
                    await run(scope, true);
                } else {
                    resultsPanel.replay([
                        { type: 'message', level: 'warn', text: 'Run cancelled — destructive statements not confirmed.' },
                        { type: 'done', exitCode: 1, status: 'Cancelled' },
                    ]);
                }
                return;
            }

            resultsPanel.replay(normalizeRunTrace(result, runText));
        } catch (err) {
            if (err?.name === 'AbortError') {
                resultsPanel.replay([
                    { type: 'message', level: 'warn', text: 'Run cancelled.' },
                    { type: 'done', exitCode: 1, status: 'Cancelled' },
                ]);
                return;
            }
            resultsPanel.replay([
                { type: 'clear', resetHistory: true },
                { type: 'message', level: 'error', text: err?.message || 'Run failed.' },
                { type: 'done', exitCode: 1 },
            ]);
        } finally {
            setRunning(false);
            resultsPanel.stopElapsed();
        }
    }

    function cancelRun() {
        runAbort?.abort();
    }

    async function save() {
        if (activeFileHandle) {
            try {
                const writable = await activeFileHandle.createWritable();
                await writable.write(editor.getValue());
                await writable.close();
                alert('Saved successfully!');
            } catch (err) {
                alert('Browser save failed: ' + err.message);
            }
            return;
        }
        
        if (activeDirectoryHandle) {
            const requestedPath = prompt('Save new script as', currentFilePath || 'new-script.etlsql');
            if (!requestedPath) return;
            try {
                activeFileHandle = await activeDirectoryHandle.getFileHandle(requestedPath, { create: true });
                const writable = await activeFileHandle.createWritable();
                await writable.write(editor.getValue());
                await writable.close();
                currentFilePath = requestedPath;
                const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = requestedPath;
                }
                await renderDirectoryTree(activeDirectoryHandle);
                alert('Saved successfully!');
            } catch (err) {
                alert('Browser save failed: ' + err.message);
            }
            return;
        }

        if (opts.onSave) {
            await opts.onSave?.(editor.getValue(), currentFilePath);
        } else {
            if (!currentFilePath) {
                const requestedPath = prompt('Save new script as', 'new-script.etlsql');
                if (!requestedPath) return;
                currentFilePath = requestedPath;
                const titleEl = root.querySelector('.etlsql-script-workbench-toolbar strong');
                if (titleEl) {
                    titleEl.textContent = currentFilePath;
                }
            }
            try {
                const fetcher = opts.authFetch ?? fetch;
                const res = await fetcher('/api/files', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path: currentFilePath, content: editor.getValue() })
                });
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                await loadFiles();
            } catch (err) {
                console.error(err);
                alert(`Error saving file: ${err.message}`);
            }
        }
    }

    async function apply() {
        await opts.onApply?.(editor.getValue());
    }

    // ── Report preview ─────────────────────────────────────────────────────────
    // Optional: when opts.previewApiUrl is set, the toolbar shows a 👁 Preview button
    // that POSTs the current script for a compiled ReportManifest, then renders it in a
    // sandboxed iframe (opts.previewUrl host) via report-runtime.js — the same
    // manifest-mode handshake the report designer uses, so the standalone script editor
    // gets a first-class WYSIWYG preview of its own.
    const previewOverlay  = container.querySelector('[data-preview-overlay]');
    const previewFrame    = container.querySelector('[data-preview-frame]');
    const previewStatusEl = container.querySelector('[data-preview-status]');
    const previewUrl = opts.previewUrl ?? '/designer-preview.html';
    let _pendingManifest = null;
    let _previewMessageHandler = null;

    function setPreviewStatus(text, kind) {
        if (!previewStatusEl) return;
        previewStatusEl.textContent = text || '';
        const colors = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        previewStatusEl.style.color = colors[kind] || colors.neutral;
    }

    if (previewFrame) {
        // The preview iframe posts 'previewReady' after each (re)load; hand it the latest manifest.
        _previewMessageHandler = (event) => {
            if (event.source !== previewFrame.contentWindow) return;
            if (event.data?.type !== 'previewReady') return;
            if (_pendingManifest) {
                previewFrame.contentWindow.postMessage({
                    type: 'reportManifest',
                    manifest: _pendingManifest,
                    dark: document.body.classList.contains('theme-dark'),
                }, '*');
            }
        };
        window.addEventListener('message', _previewMessageHandler);
    }

    async function refreshPreview() {
        setPreviewStatus('Building preview…', 'pending');
        try {
            const script = editor.getValue();
            if (!script.trim()) { setPreviewStatus('Nothing to preview yet.', 'neutral'); return; }
            const fetcher = opts.authFetch ?? ((url, init) => fetch(url, init));
            const res = await fetcher(opts.previewApiUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, connectionRef: opts.connectionRef || null }),
            });
            if (!res?.ok) throw new Error(await res.text());
            const manifest = await res.json();
            _pendingManifest = manifest;
            // Reload the host page so report-runtime.js boots fresh with the new manifest.
            previewFrame.src = previewUrl + (previewUrl.includes('?') ? '&' : '?') + 't=' + Date.now();
            const pages = manifest?.pages?.length ?? 0;
            const visuals = manifest?.visuals?.length ?? 0;
            setPreviewStatus(`Rendered ${pages} page${pages === 1 ? '' : 's'}, ${visuals} visual${visuals === 1 ? '' : 's'}.`, 'neutral');
        } catch (e) {
            setPreviewStatus('Preview failed: ' + (e?.message || e), 'error');
        }
    }

    function openPreview() {
        if (!previewOverlay) return;
        previewOverlay.classList.add('active');
        refreshPreview();
    }

    function closePreview() {
        previewOverlay?.classList.remove('active');
    }

    function commandItems() {
        return [
            { id: 'run', label: 'ETL-SQL: Run Script', enabled: Boolean(opts.runUrl || opts.onRun), action: () => run('script') },
            { id: 'run-selected', label: 'ETL-SQL: Run Selection or Current Statement', enabled: Boolean(opts.runUrl || opts.onRun), action: () => run('selection') },
            { id: 'cancel-run', label: 'ETL-SQL: Cancel Running Script', enabled: root.classList.contains('is-running'), action: cancelRun },
            { id: 'preview', label: 'ETL-SQL: Preview Report', enabled: Boolean(opts.previewApiUrl), action: openPreview },
            { id: 'suggest', label: 'ETL-SQL: Trigger Suggestions (Ctrl-Space / Ctrl-.)', enabled: Boolean(editor.hasCompletion && editor.triggerCompletion), action: () => editor.triggerCompletion() },
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

    async function formatScript() {
        if (opts.onFormat) {
            await opts.onFormat(editor.getValue());
            return;
        }
        try {
            const fetcher = opts.authFetch ?? fetch;
            const docUri = getDocumentUri();
            const script = editor.getValue();
            const res = await fetcher('/api/format', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ script, documentUri: docUri }),
            });
            if (res.ok) {
                const data = await res.json();
                if (data?.script) editor.setValue(data.script);
            }
        } catch (e) {
            console.warn('Format failed:', e);
        }
    }

    async function openFormatterSettingsModal() {
        let modal = container.querySelector('#etlsql-formatter-modal');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'etlsql-formatter-modal';
            modal.className = 'etlsql-formatter-drawer';
            container.appendChild(modal);
        }

        modal.innerHTML = `
            <div class="etlsql-formatter-header">
                <strong>⚙️ Formatter Settings</strong>
                <button type="button" class="etlsql-tool-btn" data-fmt-close title="Close">✕</button>
            </div>
            <div class="etlsql-formatter-body">
                <label class="etlsql-fmt-field">
                    <span>Keyword Casing</span>
                    <select id="fmt-casing" class="form-control">
                        <option value="upper">UPPERCASE (SELECT)</option>
                        <option value="lower">lowercase (select)</option>
                        <option value="pascal">PascalCase (Select)</option>
                        <option value="preserve">Preserve</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Indent Size</span>
                    <select id="fmt-indent" class="form-control">
                        <option value="2">2 spaces</option>
                        <option value="4">4 spaces</option>
                        <option value="8">8 spaces</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Comma Placement</span>
                    <select id="fmt-comma" class="form-control">
                        <option value="leading">Leading (,col)</option>
                        <option value="trailing">Trailing (col,)</option>
                    </select>
                </label>
                <label class="etlsql-fmt-field">
                    <span>Line Width</span>
                    <input type="number" id="fmt-linewidth" class="form-control" min="40" max="300" value="100">
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-indentjoins"> Indent JOIN clauses
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-onnewline"> Put ON clause on new line
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-casenewline"> Put CASE WHEN/THEN on new line
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-breakwindow"> Breakout window functions
                </label>
                <label class="etlsql-fmt-checkbox">
                    <input type="checkbox" id="fmt-rightalign"> Right-align query keywords
                </label>
            </div>
            <div class="etlsql-formatter-footer">
                <button type="button" id="fmt-save-btn" class="btn btn-primary btn-sm">Save to .etlsql-formatter.json</button>
                <span id="fmt-status" class="etlsql-fmt-status"></span>
            </div>
        `;

        modal.style.display = 'flex';
        modal.querySelector('[data-fmt-close]').addEventListener('click', () => { modal.style.display = 'none'; });

        try {
            const fetcher = opts.authFetch ?? fetch;
            const docUri = getDocumentUri();
            const res = await fetcher(`/api/formatter/config?documentUri=${encodeURIComponent(docUri)}`);
            if (res.ok) {
                const config = await res.json();
                if (config) {
                    if (config.keywordCasing) modal.querySelector('#fmt-casing').value = config.keywordCasing.toLowerCase();
                    if (config.indentSize) modal.querySelector('#fmt-indent').value = String(config.indentSize);
                    if (config.commaPlacement) modal.querySelector('#fmt-comma').value = config.commaPlacement.toLowerCase();
                    if (config.lineWidth) modal.querySelector('#fmt-linewidth').value = config.lineWidth;
                    modal.querySelector('#fmt-indentjoins').checked = Boolean(config.indentJoins);
                    modal.querySelector('#fmt-onnewline').checked = Boolean(config.onClauseOnNewLine);
                    modal.querySelector('#fmt-casenewline').checked = Boolean(config.caseWhenThenNewLine);
                    modal.querySelector('#fmt-breakwindow').checked = Boolean(config.breakoutWindowFunctions);
                    modal.querySelector('#fmt-rightalign').checked = Boolean(config.rightAlignKeywords);
                }
            }
        } catch (e) {
            console.warn('Failed to load formatter options:', e);
        }

        modal.querySelector('#fmt-save-btn').addEventListener('click', async () => {
            const statusEl = modal.querySelector('#fmt-status');
            statusEl.textContent = 'Saving...';
            const payload = {
                keywordCasing: modal.querySelector('#fmt-casing').value,
                indentSize: parseInt(modal.querySelector('#fmt-indent').value, 10),
                commaPlacement: modal.querySelector('#fmt-comma').value,
                lineWidth: parseInt(modal.querySelector('#fmt-linewidth').value, 10) || 100,
                indentJoins: modal.querySelector('#fmt-indentjoins').checked,
                onClauseOnNewLine: modal.querySelector('#fmt-onnewline').checked,
                caseWhenThenNewLine: modal.querySelector('#fmt-casenewline').checked,
                breakoutWindowFunctions: modal.querySelector('#fmt-breakwindow').checked,
                rightAlignKeywords: modal.querySelector('#fmt-rightalign').checked,
            };

            try {
                const fetcher = opts.authFetch ?? fetch;
                const res = await fetcher('/api/formatter/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload),
                });

                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                statusEl.textContent = '✓ Saved to .etlsql-formatter.json';
                setTimeout(() => { modal.style.display = 'none'; }, 1000);

                await formatScript();
            } catch (err) {
                statusEl.textContent = 'Error: ' + err.message;
            }
        });
    }

    container.querySelector('[data-command-palette]')?.addEventListener('click', openPalette);
    container.querySelector('[data-suggest]')?.addEventListener('click', () => editor.triggerCompletion?.());
    container.querySelector('[data-run]')?.addEventListener('click', () => run('script'));
    container.querySelector('[data-run-selected]')?.addEventListener('click', () => run('selection'));
    container.querySelector('[data-cancel-run]')?.addEventListener('click', cancelRun);
    container.querySelector('[data-preview]')?.addEventListener('click', openPreview);
    container.querySelector('[data-preview-refresh]')?.addEventListener('click', refreshPreview);
    container.querySelector('[data-preview-close]')?.addEventListener('click', closePreview);
    container.querySelector('[data-apply]')?.addEventListener('click', apply);
    container.querySelector('[data-format]')?.addEventListener('click', formatScript);
    container.querySelector('[data-format-settings]')?.addEventListener('click', openFormatterSettingsModal);
    container.querySelector('[data-save]')?.addEventListener('click', save);
    container.querySelector('[data-close]')?.addEventListener('click', () => opts.onClose?.());
    container.querySelector('[data-exit]')?.addEventListener('click', () => opts.onExit?.());
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
        if (key === 'escape' && root.classList.contains('is-running')) {
            event.preventDefault();
            cancelRun();
        } else if (mod && event.shiftKey && key === 'p') {
            event.preventDefault();
            openPalette();
        } else if (mod && key === 'enter') {
            event.preventDefault();
            await run(event.shiftKey ? 'script' : 'selection');
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
            if (_previewMessageHandler) window.removeEventListener('message', _previewMessageHandler);
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
    const sourceControlEnabled = Boolean(opts.sourceControlEnabled);
    const folderId  = opts.folderId   ?? null;
    const apiBase   = opts.apiBase    ?? '';
    const _fetch    = opts.authFetch  ?? ((url, o) => fetch(url, o));
    const previewUrl = opts.previewUrl ?? '/designer-preview.html';

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
        if (version !== null && version !== undefined)
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
        <button class="btn btn-sm" id="dsgn-tidy" title="Compact empty row gaps and tidy layout">🧹 Tidy Layout</button>
        <select id="dsgn-theme-select" class="etlsql-theme-select" title="Select canvas theme">
            <option value="light">☀️ Light</option>
            <option value="dark">🌑 Dark</option>
            <option value="midnight">🌌 Midnight</option>
            <option value="dracula">🧛 Dracula</option>
            <option value="nord">❄️ Nord</option>
        </select>
        <button class="btn btn-sm" id="dsgn-script-toggle">⌨ Script</button>
        <button class="btn btn-sm" id="dsgn-preview-toggle" title="Render a live WYSIWYG preview of this report">👁 Preview</button>
        <button class="btn btn-sm btn-primary" id="dsgn-save">Save</button>
        <button class="btn btn-sm" id="dsgn-commit" title="Commit the saved script to source control (Git)" style="display:none">Commit</button>
        <span id="dsgn-scm-status" role="status" aria-live="polite"></span>
        <button class="btn btn-sm" id="dsgn-cancel">Cancel</button>
    `;
    root.appendChild(topbar);
    topbar.querySelector('#dsgn-name').value = reportName;
    topbar.querySelector('#dsgn-theme-select').value = localStorage.getItem('portal-theme') || 'light';
    if (sourceControlEnabled && reportId) topbar.querySelector('#dsgn-commit').style.display = '';

    function setScmStatus(text, kind) {
        const el = topbar.querySelector('#dsgn-scm-status');
        if (!el) return;
        el.textContent = text || '';
        const colors = { success: '#16a34a', error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        el.style.color = colors[kind] || colors.neutral;
        el.style.marginLeft = '8px';
        el.style.fontSize = '12px';
    }
    const shortRev = r => (r ? String(r).slice(0, 8) : '');

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

    // Report preview overlay: reuses the script overlay's positioning/visibility, hosts a
    // sandboxed iframe that renders the compiled report manifest via report-runtime.js.
    const previewOverlay = document.createElement('div');
    previewOverlay.className = 'etlsql-designer-script-overlay';
    previewOverlay.innerHTML = `
        <div class="etlsql-designer-script-toolbar">
            <strong>Preview</strong>
            <span id="dsgn-preview-status" style="font-size:12px;color:#64748b"></span>
            <span style="flex:1"></span>
            <button type="button" class="btn btn-sm" id="dsgn-preview-refresh" title="Re-run the report and refresh the preview">↻ Refresh</button>
            <button type="button" class="btn btn-sm" id="dsgn-preview-close">Close</button>
        </div>
        <iframe id="dsgn-preview-frame" title="Report preview" sandbox="allow-scripts allow-same-origin" style="flex:1;border:0;width:100%;background:#fff"></iframe>`;
    root.appendChild(previewOverlay);

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

    let activeSnapshotFilter = null;

    function tidyLayout() {
        const page = curPage();
        if (!page?.visuals?.length) return;

        const visuals = [...page.visuals].sort((a, b) => ((a.gridRow || 1) - (b.gridRow || 1)) || ((a.gridCol || 1) - (b.gridCol || 1)));

        for (let i = 0; i < visuals.length; i++) {
            const v = visuals[i];
            const vColStart = v.gridCol || 1;
            const vColEnd = vColStart + (v.gridColSpan || 12) - 1;

            let newRow = 1;

            for (let j = 0; j < i; j++) {
                const prev = visuals[j];
                const pColStart = prev.gridCol || 1;
                const pColEnd = pColStart + (prev.gridColSpan || 12) - 1;

                const overlapsHorizontally = (vColStart <= pColEnd) && (vColEnd >= pColStart);

                if (overlapsHorizontally) {
                    const prevBottom = (prev.gridRow || 1) + (prev.gridRowSpan || 4);
                    if (prevBottom > newRow) {
                        newRow = prevBottom;
                    }
                }
            }

            const deltaRow = newRow - (v.gridRow || 1);
            v.gridRow = newRow;

            if (v.type === 'CONTAINER' && deltaRow !== 0) {
                for (const child of page.visuals) {
                    if (child.containerId === v.id) {
                        child.gridRow = Math.max(1, (child.gridRow || 1) + deltaRow);
                    }
                }
            }
        }

        renderCanvas();
        renderTree();
        renderProps();
    }

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

    function _renderSnapshotCardBody(bodyEl, visual, snapshotPackage) {
        if (!snapshotPackage || !snapshotPackage.sampleRows) {
            bodyEl.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--portal-muted,#64748b);font-size:11px;">No snapshot data</div>`;
            return;
        }

        // Resolve the visual's own identity first, then its dataset. The snapshot manifest records
        // visuals and datasets but never links them, so the server keys sample rows by visual name —
        // the only identity both sides share. Dataset lookup stays as a fallback for packages keyed
        // that way (the UI sandbox fixtures), and the first-entry fallback keeps a single-dataset
        // report rendering rather than showing nothing.
        const sampleRows = snapshotPackage.sampleRows;
        const byVisual = [visual.name, visual.title, visual.id].find(k => k && sampleRows[k]);
        const dsName = visual.dataset;
        let rows = (byVisual && sampleRows[byVisual])
            || (dsName && sampleRows[dsName])
            || Object.values(sampleRows)[0]
            || [];
        const type = (visual.type || '').toUpperCase();

        // Interactive Filter Slicers Simulation
        if (type === 'SLICER' || type === 'MULTISELECT' || type === 'DATEPICKER') {
            const categories = Array.from(new Set(rows.map(r => String(Array.isArray(r) ? r[0] : r))));
            const selected = activeSnapshotFilter;
            let btnHtml = `<button class="btn btn-xs ${!selected ? 'btn-primary' : ''}" data-slicer-val="" style="margin:2px;font-size:10px;">All</button>`;
            categories.slice(0, 8).forEach(cat => {
                const isSel = String(selected).toLowerCase() === String(cat).toLowerCase();
                btnHtml += `<button class="btn btn-xs ${isSel ? 'btn-primary' : ''}" data-slicer-val="${esc(cat)}" style="margin:2px;font-size:10px;">${esc(cat)}</button>`;
            });

            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:4px;text-align:center;">
                    <div style="font-size:10px;font-weight:600;color:var(--portal-muted,#64748b);margin-bottom:4px;">Filter by ${esc(visual.title || 'Category')}</div>
                    <div style="display:flex;flex-wrap:wrap;justify-content:center;gap:2px;">${btnHtml}</div>
                </div>`;

            bodyEl.querySelectorAll('[data-slicer-val]').forEach(b => {
                b.addEventListener('click', e => {
                    e.stopPropagation();
                    const btn = e.currentTarget;
                    const val = btn.getAttribute('data-slicer-val');
                    activeSnapshotFilter = val || null;
                    renderCanvas();
                });
            });
            return;
        }

        if (type === 'CONTAINER') {
            const containerType = visual.options?.CONTAINER_TYPE || 'BOX';
            const childCount = curVis().filter(c => c.containerId === visual.id).length;
            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:12px;color:var(--portal-muted,#64748b);font-size:11px;border:1.5px dashed var(--portal-border-soft,#cbd5e1);border-radius:6px;background:rgba(37, 99, 235, 0.02);pointer-events:none;">
                    <div style="font-weight:600;color:var(--portal-text-soft,#475569);font-size:12px;margin-bottom:2px;">📁 ${esc(containerType)} Container</div>
                    <div style="font-size:10px;color:var(--portal-muted,#94a3b8);">${childCount > 0 ? `${childCount} visual${childCount === 1 ? '' : 's'} grouped inside` : 'Drag visuals on top to group'}</div>
                </div>`;
            return;
        }

        // Apply active filter if set
        if (activeSnapshotFilter) {
            const filterLower = activeSnapshotFilter.toLowerCase();
            rows = rows.filter(r => Array.isArray(r)
                ? r.some(cell => String(cell).toLowerCase() === filterLower)
                : String(r).toLowerCase() === filterLower);
        }

        if (type === 'CARD') {
            const val = rows[0] ? (rows[0][rows[0].length - 1] ?? rows[0][0]) : '0';
            bodyEl.innerHTML = `
                <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:4px;text-align:center;">
                    <div style="font-size:22px;font-weight:700;color:var(--portal-accent,#2563eb);line-height:1.2;">${esc(val)}</div>
                    <div style="font-size:11px;color:var(--portal-muted,#64748b);margin-top:2px;">${esc(visual.title || visual.name)}</div>
                </div>`;
            return;
        }

        if (type === 'TABLE' || type === 'MATRIX') {
            const mappings = visual.mappings || {};
            const sampleHeaders = Object.values(mappings).filter(Boolean);
            const headers = sampleHeaders.length ? sampleHeaders : (type === 'MATRIX' ? ['Row', 'Col', 'Value'] : ['Region', 'Quarter', 'Revenue']);
            let html = `<table style="width:100%;height:100%;font-size:11px;border-collapse:collapse;color:var(--portal-text,#172033);">
                <thead><tr style="background:var(--portal-surface-subtle,#f8fafc);border-bottom:1px solid var(--portal-border,#d9e0ea);">
                    ${headers.map(h => `<th style="padding:3px 5px;text-align:left;font-weight:600;">${esc(h)}</th>`).join('')}
                </tr></thead><tbody>`;
            const displayRows = rows.slice(0, 5);
            displayRows.forEach(r => {
                const cells = Array.isArray(r) ? r : [r];
                html += `<tr style="border-bottom:1px solid var(--portal-border,#e2e8f0);">${cells.map(cell => `<td style="padding:2px 5px;">${esc(cell)}</td>`).join('')}</tr>`;
            });
            html += `</tbody></table>`;
            bodyEl.innerHTML = html;
            return;
        }

        // Render live chart via ECharts
        if (window.echarts && typeof window.echarts.init === 'function') {
            try {
                const isDark = document.body.classList.contains('theme-dark') || 
                               document.body.classList.contains('theme-midnight') || 
                               document.body.classList.contains('theme-dracula') || 
                               document.body.classList.contains('theme-nord');
                let chart = window.echarts.getInstanceByDom(bodyEl);
                if (chart) {
                    chart.dispose();
                }
                chart = window.echarts.init(bodyEl, isDark ? 'dark' : null);
                
                const sample = rows[0] || [];
                const catIdx = (Array.isArray(sample) && sample.length >= 3) ? 1 : 0;
                const valIdx = (Array.isArray(sample) && sample.length >= 2) ? sample.length - 1 : 0;

                const categories = rows.map(r => Array.isArray(r) ? String(r[catIdx]) : String(r));
                const values = rows.map(r => Array.isArray(r) ? (typeof r[valIdx] === 'number' ? r[valIdx] : parseFloat(r[valIdx]) || 10) : 10);

                let option = {};
                if (type === 'PIE' || type === 'DONUT') {
                    option = {
                        backgroundColor: 'transparent',
                        tooltip: { trigger: 'item' },
                        series: [{
                            type: 'pie',
                            radius: type === 'DONUT' ? ['35%', '65%'] : '65%',
                            data: rows.map(r => ({ name: String(Array.isArray(r) ? r[catIdx] : r), value: parseFloat(Array.isArray(r) ? r[valIdx] : 10) || 10 })),
                            label: { show: false }
                        }]
                    };
                } else if (type === 'LINE' || type === 'AREA') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'category', data: categories, axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [{ type: 'line', data: values, areaStyle: type === 'AREA' ? {} : null, itemStyle: { color: VCOLOR[type] || '#2563eb' } }]
                    };
                } else if (type === 'SCATTER' || type === 'BUBBLE') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [{
                            type: 'scatter',
                            data: rows.map(r => [
                                parseFloat(Array.isArray(r) ? r[0] : r) || 0,
                                parseFloat(Array.isArray(r) ? r[1] : 10) || 0,
                                parseFloat(Array.isArray(r) ? r[2] : 10) || 10
                            ]),
                            symbolSize: function (data) {
                                return type === 'BUBBLE' ? Math.min(Math.max(data[2] || 10, 5), 30) : 8;
                            },
                            itemStyle: { color: VCOLOR[type] || '#3b82f6' }
                        }]
                    };
                } else if (type === 'COMBO') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'category', data: categories, axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [
                            { type: 'bar', data: values, itemStyle: { color: '#3b82f6', borderRadius: 2 } },
                            { type: 'line', data: values.map(v => v * 0.9), itemStyle: { color: '#ef4444' } }
                        ]
                    };
                } else if (type === 'TREEMAP') {
                    option = {
                        backgroundColor: 'transparent',
                        series: [{
                            type: 'treemap',
                            roam: false,
                            breadcrumb: { show: false },
                            data: rows.map(r => ({
                                name: String(Array.isArray(r) ? r[0] : r),
                                value: parseFloat(Array.isArray(r) ? r[valIdx] : 10) || 10
                            })),
                            label: { fontSize: 9 }
                        }]
                    };
                } else if (type === 'FUNNEL') {
                    option = {
                        backgroundColor: 'transparent',
                        series: [{
                            type: 'funnel',
                            left: '10%', width: '80%',
                            data: rows.map(r => ({
                                name: String(Array.isArray(r) ? r[0] : r),
                                value: parseFloat(Array.isArray(r) ? r[valIdx] : 10) || 10
                            })),
                            label: { fontSize: 9 }
                        }]
                    };
                } else if (type === 'RADAR') {
                    const indicators = categories.slice(0, 6).map(c => ({ name: c, max: 100 }));
                    const valData = values.slice(0, 6).map(v => Math.min(v, 100));
                    option = {
                        backgroundColor: 'transparent',
                        radar: {
                            indicator: indicators.length > 0 ? indicators : [{ name: 'A', max: 100 }, { name: 'B', max: 100 }, { name: 'C', max: 100 }],
                            radius: '60%',
                            axisName: { fontSize: 8 }
                        },
                        series: [{
                            type: 'radar',
                            data: [{ value: valData.length > 0 ? valData : [60, 70, 80], name: 'Data' }]
                        }]
                    };
                } else if (type === 'GAUGE') {
                    const gaugeVal = parseFloat(values[0]) || 50;
                    option = {
                        backgroundColor: 'transparent',
                        series: [{
                            type: 'gauge',
                            progress: { show: true, width: 8 },
                            detail: { valueAnimation: true, formatter: '{value}', fontSize: 12, offsetCenter: [0, '70%'] },
                            data: [{ value: Math.min(Math.max(gaugeVal, 0), 100) }],
                            axisLabel: { fontSize: 8, distance: 10 },
                            axisTick: { show: false },
                            splitLine: { show: false },
                            anchor: { show: false },
                            title: { show: false }
                        }]
                    };
                } else if (type === 'HEATMAP') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'category', data: categories.slice(0, 5), axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'category', data: categories.slice(0, 5), axisLabel: { fontSize: 9 } },
                        visualMap: { min: 0, max: 100, show: false, inRange: { color: ['#eff6ff', '#1e3a8a'] } },
                        series: [{
                            type: 'heatmap',
                            data: rows.slice(0, 10).map((r, idx) => [
                                idx % 5,
                                Math.floor(idx / 5) % 5,
                                parseFloat(Array.isArray(r) ? r[valIdx] : 10) || 10
                            ]),
                            label: { show: false }
                        }]
                    };
                } else if (type === 'BOXPLOT') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'category', data: ['Data'], axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [{
                            type: 'boxplot',
                            data: [
                                [
                                    parseFloat(values[0]) || 10,
                                    parseFloat(values[1]) || 20,
                                    parseFloat(values[2]) || 30,
                                    parseFloat(values[3]) || 40,
                                    parseFloat(values[4]) || 50
                                ]
                            ]
                        }]
                    };
                } else if (type === 'SUNBURST') {
                    option = {
                        backgroundColor: 'transparent',
                        series: [{
                            type: 'sunburst',
                            radius: ['20%', '80%'],
                            data: rows.slice(0, 8).map(r => ({
                                name: String(Array.isArray(r) ? r[0] : r),
                                value: parseFloat(Array.isArray(r) ? r[valIdx] : 10) || 10
                            })),
                            label: { fontSize: 8 }
                        }]
                    };
                } else if (type === 'WATERFALL') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'category', data: categories, axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [
                            {
                                type: 'bar',
                                stack: 'all',
                                itemStyle: { borderColor: 'transparent', color: 'transparent' },
                                data: values.map((v, idx) => {
                                    if (idx === 0) return 0;
                                    return Math.min(values[idx - 1], v);
                                })
                            },
                            {
                                type: 'bar',
                                stack: 'all',
                                data: values.map((v, idx) => {
                                    if (idx === 0) return v;
                                    return Math.abs(v - values[idx - 1]);
                                }),
                                itemStyle: {
                                    color: function (params) {
                                        const idx = params.dataIndex;
                                        if (idx === 0) return '#3b82f6';
                                        return values[idx] >= values[idx - 1] ? '#10b981' : '#ef4444';
                                    }
                                }
                            }
                        ]
                    };
                } else if (type === 'CANDLESTICK') {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: { type: 'category', data: categories, axisLabel: { fontSize: 9 } },
                        yAxis: { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [{
                            type: 'candlestick',
                            data: rows.map(r => {
                                const v = parseFloat(Array.isArray(r) ? r[valIdx] : 10) || 10;
                                return [v * 0.9, v * 1.1, v * 0.8, v * 1.2];
                            }),
                            itemStyle: {
                                color: '#10b981',
                                color0: '#ef4444',
                                borderColor: '#10b981',
                                borderColor0: '#ef4444'
                            }
                        }]
                    };
                } else if (type === 'SANKEY') {
                    option = {
                        backgroundColor: 'transparent',
                        series: [{
                            type: 'sankey',
                            layout: 'none',
                            emphasis: { focus: 'adjacency' },
                            data: [
                                { name: 'A' }, { name: 'B' }, { name: 'C' }
                            ],
                            links: [
                                { source: 'A', target: 'B', value: 10 },
                                { source: 'B', target: 'C', value: 5 }
                            ],
                            label: { fontSize: 8 }
                        }]
                    };
                } else {
                    option = {
                        backgroundColor: 'transparent',
                        grid: { top: 10, bottom: 20, left: 30, right: 10 },
                        xAxis: type === 'HBAR' ? { type: 'value', axisLabel: { fontSize: 9 } } : { type: 'category', data: categories, axisLabel: { fontSize: 9 } },
                        yAxis: type === 'HBAR' ? { type: 'category', data: categories, axisLabel: { fontSize: 9 } } : { type: 'value', axisLabel: { fontSize: 9 } },
                        series: [{ type: 'bar', data: values, itemStyle: { color: VCOLOR[type] || '#3b82f6', borderRadius: 2 } }]
                    };
                }
                chart.setOption(option, true);

                if (window.ResizeObserver) {
                    const ro = new ResizeObserver(() => { try { chart.resize(); } catch {} });
                    ro.observe(bodyEl);
                }
                return;
            } catch (err) {
                console.warn('Snapshot ECharts canvas error:', err);
            }
        }

        bodyEl.innerHTML = `
            <div style="display:flex;flex-direction:column;justify-content:center;align-items:center;height:100%;padding:8px;color:var(--portal-muted,#64748b);font-size:11px;">
                <div style="font-weight:600;color:var(--portal-accent,#2563eb);margin-bottom:2px;">${esc(visual.type)} Snapshot</div>
                <div>${rows.length} rows loaded from .etlsnap</div>
            </div>`;
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
            const isContainer = v.type === 'CONTAINER';
            const card = document.createElement('div');
            card.className = 'etlsql-dsgn-visual-card' + (v.id === selVisualId ? ' selected' : '') + (isContainer ? ' is-container' : '');
            if (v.containerId) {
                card.classList.add('has-container');
                card.dataset.containerId = v.containerId;
            }
            card.dataset.vid = v.id;
            card.style.gridColumn = `${v.gridCol || 1} / span ${v.gridColSpan || 12}`;
            card.style.gridRow    = `${v.gridRow || 1} / span ${v.gridRowSpan || 4}`;
            card.style.setProperty('--vc', VCOLOR[v.type] || '#64748b');
            card.style.zIndex     = isContainer ? '1' : '2';

            let badgeExtra = '';
            if (opts.snapshotPackage) {
                const meta = opts.snapshotPackage.metadata || {};
                if (meta.rlsPolicy || meta.rlsEnforced) {
                    badgeExtra += `<span style="background:var(--portal-accent,#2563eb);color:#fff;padding:1px 4px;border-radius:3px;font-size:9px;margin-left:4px;" title="RLS Governance Policy Enforced">🔒 RLS</span>`;
                }
                if (meta.isSampled) {
                    badgeExtra += `<span style="background:#f59e0b;color:#fff;padding:1px 4px;border-radius:3px;font-size:9px;margin-left:4px;" title="Sampled Snapshot Data">⚡ Sampled</span>`;
                }
            }

            const badgeText = isContainer ? `📁 ${v.options?.CONTAINER_TYPE || 'BOX'}` : v.type;
            const cardHdr = document.createElement('div');
            cardHdr.className = 'etlsql-dsgn-vcard-hdr';
            cardHdr.innerHTML = `
                <div class="etlsql-dsgn-vcard-badge">${badgeText}${badgeExtra}</div>
                <div class="etlsql-dsgn-vcard-name" style="flex:1;font-weight:600;font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${esc(v.title || v.name)}</div>
                <button class="etlsql-dsgn-vcard-del" data-del="${v.id}" title="Remove visual">✕</button>
            `;
            card.appendChild(cardHdr);

            const cardBody = document.createElement('div');
            cardBody.className = 'etlsql-dsgn-vcard-body';

            if (opts.snapshotPackage || opts.snapshotMode) {
                _renderSnapshotCardBody(cardBody, v, opts.snapshotPackage);
            } else {
                cardBody.innerHTML = `<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--portal-muted,#64748b);font-size:11px;">${v.type} Placeholder</div>`;
            }

            card.appendChild(cardBody);

            const resizeHandle = document.createElement('div');
            resizeHandle.className = 'etlsql-dsgn-vcard-resize';
            resizeHandle.title = 'Drag to resize';
            card.appendChild(resizeHandle);

            canvasGrid.appendChild(card);
        }
    }

    function renderTree() {
        const tree = sidebar.querySelector('#dsgn-tree');
        tree.innerHTML = '';
        const visuals = curVis();
        const containers = visuals.filter(v => v.type === 'CONTAINER');
        const rootVisuals = visuals.filter(v => !v.containerId || !containers.some(c => c.id === v.containerId));

        for (const v of rootVisuals) {
            const item = document.createElement('div');
            item.className = 'etlsql-dsgn-tree-item' + (v.id === selVisualId ? ' selected' : '');
            item.dataset.vid = v.id;
            const icon = v.type === 'CONTAINER' ? '📁' : '📊';
            item.textContent = `${icon} ${v.name} (${v.type})`;
            tree.appendChild(item);

            if (v.type === 'CONTAINER') {
                const children = visuals.filter(c => c.containerId === v.id);
                for (const child of children) {
                    const citem = document.createElement('div');
                    citem.className = 'etlsql-dsgn-tree-item child-item' + (child.id === selVisualId ? ' selected' : '');
                    citem.style.paddingLeft = '20px';
                    citem.dataset.vid = child.id;
                    citem.textContent = `└─ ${child.name} (${child.type})`;
                    tree.appendChild(citem);
                }
            }
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

        const containers = curVis().filter(c => c.type === 'CONTAINER' && c.id !== v.id);
        const cOpts = containers
            .map(c => `<option value="${c.id}"${v.containerId === c.id ? ' selected' : ''}>📁 ${esc(c.title || c.name)}</option>`)
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
                <label class="etlsql-dsgn-label">Container Group
                    <select id="pp-container-id" class="form-control">
                        <option value="">— none —</option>${cOpts}
                    </select>
                </label>
                <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                <label class="etlsql-dsgn-label">Dataset
                    <select id="pp-ds" class="form-control">
                        <option value="">— none —</option>${dsOpts}
                    </select>
                </label>
                <label class="etlsql-dsgn-label">Width<input id="pp-width" class="form-control" placeholder="auto, 300px, 100%" value="${esc(v.options?.WIDTH || v.width || '')}"></label>
                <label class="etlsql-dsgn-label">Height<input id="pp-height" class="form-control" placeholder="auto, 200px, 100%" value="${esc(v.options?.HEIGHT || v.height || '')}"></label>
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

        on('#pp-name',         e => { v.name  = e.target.value; renderCanvas(); renderTree(); });
        on('#pp-type',         e => { v.type  = e.target.value; renderCanvas(); renderTree(); });
        on('#pp-container-id', e => { v.containerId = e.target.value || null; renderTree(); renderCanvas(); });
        on('#pp-title',        e => { v.title = e.target.value; renderCanvas(); });
        on('#pp-ds',           e => { v.dataset = e.target.value || null; });
        on('#pp-width',        e => { if (!v.options) v.options = {}; if (e.target.value.trim()) v.options.WIDTH = e.target.value.trim(); else delete v.options.WIDTH; });
        on('#pp-height',       e => { if (!v.options) v.options = {}; if (e.target.value.trim()) v.options.HEIGHT = e.target.value.trim(); else delete v.options.HEIGHT; });
        on('#pp-col',          e => { v.gridCol     = +e.target.value || 1;  renderCanvas(); });
        on('#pp-row',          e => { v.gridRow     = +e.target.value || 1;  renderCanvas(); });
        on('#pp-cspan',        e => { v.gridColSpan = +e.target.value || 12; renderCanvas(); });
        on('#pp-rspan',        e => { v.gridRowSpan = +e.target.value || 4;  renderCanvas(); });

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

    let selVisualIds = new Set();

    function selectVisual(id, opts = {}) {
        if (opts.toggle || opts.multi) {
            if (id) {
                if (selVisualIds.has(id)) selVisualIds.delete(id);
                else selVisualIds.add(id);
            }
        } else {
            selVisualIds.clear();
            if (id) selVisualIds.add(id);
        }

        selVisualId = selVisualIds.size === 1 ? Array.from(selVisualIds)[0] : null;

        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card')) {
            card.classList.toggle('selected', selVisualIds.has(card.dataset.vid));
        }

        renderTree();
        renderProps();
        renderAlignmentToolbar();
    }

    function renderAlignmentToolbar() {
        let bar = canvasWrap.querySelector('#dsgn-align-bar');
        if (selVisualIds.size < 2) {
            if (bar) bar.style.display = 'none';
            return;
        }

        if (!bar) {
            bar = document.createElement('div');
            bar.id = 'dsgn-align-bar';
            bar.className = 'etlsql-dsgn-align-bar';
            canvasWrap.appendChild(bar);

            bar.addEventListener('click', e => {
                const btn = e.target.closest('[data-align]');
                if (!btn) return;
                const mode = btn.dataset.align;
                const visuals = curVis().filter(v => selVisualIds.has(v.id));
                if (visuals.length < 2) return;

                if (mode === 'left') {
                    const minCol = Math.min(...visuals.map(v => v.gridCol || 1));
                    visuals.forEach(v => v.gridCol = minCol);
                } else if (mode === 'top') {
                    const minRow = Math.min(...visuals.map(v => v.gridRow || 1));
                    visuals.forEach(v => v.gridRow = minRow);
                } else if (mode === 'width') {
                    const targetSpan = visuals[0].gridColSpan || 12;
                    visuals.forEach(v => v.gridColSpan = targetSpan);
                } else if (mode === 'height') {
                    const targetSpan = visuals[0].gridRowSpan || 4;
                    visuals.forEach(v => v.gridRowSpan = targetSpan);
                }
                renderCanvas();
            });
        }

        bar.innerHTML = `
            <span style="font-size:11px;font-weight:600;margin-right:2px;">${selVisualIds.size} selected</span>
            <button class="btn btn-xs" data-align="left" title="Align Left">⬅ Left</button>
            <button class="btn btn-xs" data-align="top" title="Align Top">⬆ Top</button>
            <button class="btn btn-xs" data-align="width" title="Equal Width">↔ Width</button>
            <button class="btn btn-xs" data-align="height" title="Equal Height">↕ Height</button>
        `;
        bar.style.display = 'flex';
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
            // The Portal has no file workspace (its catalog is folders/reports) and git
            // write-back is a separate roadmap item, so only schema + session are enabled.
            sidebar: { schema: true, session: true },
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

    // ── Report preview ──────────────────────────────────────────────────────────
    const previewFrame   = previewOverlay.querySelector('#dsgn-preview-frame');
    const previewStatusEl = previewOverlay.querySelector('#dsgn-preview-status');
    let _pendingManifest = null;

    function setPreviewStatus(text, kind) {
        if (!previewStatusEl) return;
        previewStatusEl.textContent = text || '';
        const colors = { error: '#dc2626', pending: '#a16207', neutral: '#64748b' };
        previewStatusEl.style.color = colors[kind] || colors.neutral;
    }

    // The preview iframe posts 'previewReady' after each (re)load; hand it the latest manifest.
    window.addEventListener('message', (event) => {
        if (event.source !== previewFrame?.contentWindow) return;
        if (event.data?.type !== 'previewReady') return;
        if (_pendingManifest) {
            previewFrame.contentWindow.postMessage({
                type: 'reportManifest',
                manifest: _pendingManifest,
                dark: document.body.classList.contains('theme-dark'),
            }, '*');
        }
    });

    async function refreshPreview() {
        setPreviewStatus('Building preview…', 'pending');
        try {
            const gen = await apiJson('/api/designer/generate', 'POST', { designState: state });
            const script = gen?.script ?? '';
            if (!script.trim()) { setPreviewStatus('Nothing to preview yet.', 'neutral'); return; }
            const manifest = await apiJson('/api/designer/preview', 'POST', { script });
            _pendingManifest = manifest;
            // Reload the host page so report-runtime.js boots fresh with the new manifest.
            previewFrame.src = previewUrl + (previewUrl.includes('?') ? '&' : '?') + 't=' + Date.now();
            const pages = manifest?.pages?.length ?? 0;
            const visuals = manifest?.visuals?.length ?? 0;
            setPreviewStatus(`Rendered ${pages} page${pages === 1 ? '' : 's'}, ${visuals} visual${visuals === 1 ? '' : 's'}.`, 'neutral');
        } catch (e) {
            setPreviewStatus('Preview failed: ' + e.message, 'error');
        }
    }

    function openPreview() {
        previewOverlay.classList.add('active');
        refreshPreview();
    }

    function closePreview() {
        previewOverlay.classList.remove('active');
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
                if (sourceControlEnabled) {
                    // Save writes the catalog + script artifact only. Committing to Git is a
                    // separate, explicit step, so stay on the page and surface the Commit action
                    // instead of navigating away.
                    setScmStatus(`Saved v${reportVersion} · not yet committed`, 'pending');
                    const commitBtn = topbar.querySelector('#dsgn-commit');
                    if (commitBtn) commitBtn.disabled = false;
                } else {
                    opts.onSave?.();
                }
            } else {
                saveModal.querySelector('#dsgn-modal-name').value   = reportName;
                saveModal.querySelector('#dsgn-modal-folder').value = folderId ?? '';
                saveModal._script = script;
                saveModal.style.display = 'flex';
            }
        } catch (e) { alert('Save failed: ' + e.message); }
    }

    // Explicit, separately reported source-control step. Commits the last-saved script
    // artifact to Git (and pushes if the server is configured to push on commit). This never
    // holds a database transaction — the server stages/commits under its own repository lease.
    async function commitScript() {
        if (!reportId) return;
        const commitBtn = topbar.querySelector('#dsgn-commit');
        const prevLabel = commitBtn ? commitBtn.textContent : 'Commit';
        if (commitBtn) { commitBtn.disabled = true; commitBtn.textContent = 'Committing…'; }
        setScmStatus('Committing to source control…', 'pending');
        try {
            const res = await apiJson(`/api/reports/${reportId}/script-source/commit`, 'POST', {});
            if (res?.committed) {
                sourceRevision = res.sourceRevision ?? sourceRevision;
                setScmStatus(`Committed ${shortRev(res.sourceRevision)}`, 'success');
            } else {
                setScmStatus(`Nothing to commit — working tree matches ${shortRev(res?.sourceRevision) || 'HEAD'}`, 'neutral');
            }
        } catch (e) {
            setScmStatus(`Commit failed: ${e.message}`, 'error');
        } finally {
            if (commitBtn) { commitBtn.disabled = false; commitBtn.textContent = prevLabel || 'Commit'; }
        }
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
    topbar.querySelector('#dsgn-commit')?.addEventListener('click', commitScript);
    topbar.querySelector('#dsgn-add-page').addEventListener('click', addPage);
    topbar.querySelector('#dsgn-tidy')?.addEventListener('click', tidyLayout);
    topbar.querySelector('#dsgn-theme-select')?.addEventListener('change', e => {
        const themes = ['light', 'dark', 'midnight', 'dracula', 'nord'];
        const nextTheme = e.target.value;
        
        themes.forEach(t => document.body.classList.remove('theme-' + t));
        document.body.classList.add('theme-' + nextTheme);
        localStorage.setItem('portal-theme', nextTheme);
        renderCanvas();
    });
    topbar.querySelector('#dsgn-name').addEventListener('change',   e => { reportName = e.target.value; });
    topbar.querySelector('#dsgn-script-toggle').addEventListener('click', () =>
        scriptOverlay.classList.contains('active') ? closeScript() : openScript());
    topbar.querySelector('#dsgn-preview-toggle')?.addEventListener('click', () =>
        previewOverlay.classList.contains('active') ? closePreview() : openPreview());
    previewOverlay.querySelector('#dsgn-preview-refresh')?.addEventListener('click', refreshPreview);
    previewOverlay.querySelector('#dsgn-preview-close')?.addEventListener('click', closePreview);

    topbar.querySelector('#dsgn-pages').addEventListener('click', e => {
        const tab = e.target.closest('.etlsql-designer-page-tab');
        if (tab) { pageIdx = +tab.dataset.idx; selVisualId = null; renderAll(); }
    });

    sidebar.addEventListener('click', e => {
        const btn = e.target.closest('.etlsql-dsgn-palette-btn');
        if (btn) addVisual(btn.dataset.vtype);
    });

    // ── Drag, Resize & Marquee Interaction ─────────────────────────────────
    let isDragging = false;
    let isResizing = false;
    let isMarquee = false;
    let marqueeStartX = 0, marqueeStartY = 0;
    let marqueeEl = null;
    let activeId = null;
    let activeCardEl = null;
    let ghostEl = null;
    let startX = 0, startY = 0;
    let startCol = 1, startRow = 1;
    let startColSpan = 12, startRowSpan = 4;
    let targetCol = 1, targetRow = 1;
    let targetColSpan = 12, targetRowSpan = 4;
    let initialRect = null;

    function handleMarqueeMove(e) {
        if (!isMarquee || !marqueeEl) return;
        const wrapRect = canvasWrap.getBoundingClientRect();
        
        const curX = e.clientX;
        const curY = e.clientY;

        const left = Math.min(marqueeStartX, curX) - wrapRect.left + canvasWrap.scrollLeft;
        const top = Math.min(marqueeStartY, curY) - wrapRect.top + canvasWrap.scrollTop;
        const width = Math.abs(curX - marqueeStartX);
        const height = Math.abs(curY - marqueeStartY);

        marqueeEl.style.left = `${left}px`;
        marqueeEl.style.top = `${top}px`;
        marqueeEl.style.width = `${width}px`;
        marqueeEl.style.height = `${height}px`;

        const mRect = marqueeEl.getBoundingClientRect();
        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card')) {
            const cRect = card.getBoundingClientRect();
            const intersects = !(mRect.right < cRect.left || mRect.left > cRect.right || mRect.bottom < cRect.top || mRect.top > cRect.bottom);
            if (intersects) {
                selVisualIds.add(card.dataset.vid);
            } else if (!e.shiftKey && !e.ctrlKey && !e.metaKey) {
                selVisualIds.delete(card.dataset.vid);
            }
        }

        selVisualId = selVisualIds.size === 1 ? Array.from(selVisualIds)[0] : null;
        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card')) {
            card.classList.toggle('selected', selVisualIds.has(card.dataset.vid));
        }
        renderAlignmentToolbar();
    }

    function handleMarqueeUp() {
        if (marqueeEl) {
            marqueeEl.style.display = 'none';
        }
        isMarquee = false;
        document.removeEventListener('mousemove', handleMarqueeMove);
        document.removeEventListener('mouseup', handleMarqueeUp);
        renderTree();
        renderProps();
    }

    canvasGrid.addEventListener('mousedown', e => {
        const resizeHandle = e.target.closest('.etlsql-dsgn-vcard-resize');
        const card = e.target.closest('.etlsql-dsgn-visual-card');
        const delBtn = e.target.closest('[data-del]');

        if (delBtn) return; // Delete visual handler manages this

        if (!card && !resizeHandle) {
            isMarquee = true;
            marqueeStartX = e.clientX;
            marqueeStartY = e.clientY;

            if (!e.shiftKey && !e.ctrlKey && !e.metaKey) {
                selectVisual(null);
            }

            if (!marqueeEl) {
                marqueeEl = document.createElement('div');
                marqueeEl.className = 'etlsql-dsgn-marquee';
                canvasWrap.appendChild(marqueeEl);
            }
            const wrapRect = canvasWrap.getBoundingClientRect();
            marqueeEl.style.left = `${e.clientX - wrapRect.left + canvasWrap.scrollLeft}px`;
            marqueeEl.style.top = `${e.clientY - wrapRect.top + canvasWrap.scrollTop}px`;
            marqueeEl.style.width = '0px';
            marqueeEl.style.height = '0px';
            marqueeEl.style.display = 'block';

            document.addEventListener('mousemove', handleMarqueeMove);
            document.addEventListener('mouseup', handleMarqueeUp);
            return;
        }

        if (card) {
            const vid = card.dataset.vid;
            const v = findVis(vid);
            if (!v) return;

            selectVisual(vid, { skipCanvas: true, toggle: e.shiftKey || e.ctrlKey || e.metaKey });

            startX = e.clientX;
            startY = e.clientY;
            activeId = vid;
            activeCardEl = card;
            startCol = targetCol = v.gridCol || 1;
            startRow = targetRow = v.gridRow || 1;
            startColSpan = targetColSpan = v.gridColSpan || 12;
            startRowSpan = targetRowSpan = v.gridRowSpan || 4;

            if (resizeHandle) {
                isResizing = true;
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            } else {
                isDragging = true;
                initialRect = card.getBoundingClientRect();
                e.preventDefault();
                document.addEventListener('mousemove', handleMouseMove);
                document.addEventListener('mouseup', handleMouseUp);
            }
        }
    });

    function handleMouseMove(e) {
        if (!activeId || !activeCardEl) return;
        const v = findVis(activeId);
        if (!v) return;

        const gridRect = canvasGrid.getBoundingClientRect();
        const gridW = gridRect.width - 32;
        const W_col = (gridW - 11 * 6) / 12;

        if (isDragging) {
            if (!ghostEl) {
                ghostEl = document.createElement('div');
                ghostEl.className = 'etlsql-dsgn-grid-ghost';
                ghostEl.style.gridColumn = `${startCol} / span ${startColSpan}`;
                ghostEl.style.gridRow    = `${startRow} / span ${startRowSpan}`;
                canvasGrid.appendChild(ghostEl);

                activeCardEl.classList.add('dragging');
                activeCardEl.style.width = `${initialRect.width}px`;
                activeCardEl.style.height = `${initialRect.height}px`;
                activeCardEl.style.left = `${initialRect.left - gridRect.left}px`;
                activeCardEl.style.top = `${initialRect.top - gridRect.top}px`;
            }

            const dx = e.clientX - startX;
            const dy = e.clientY - startY;
            activeCardEl.style.transform = `translate3d(${dx}px, ${dy}px, 0)`;

            const currentLeft = (initialRect.left - gridRect.left) + dx - 16;
            const currentTop  = (initialRect.top - gridRect.top) + dy - 16;

            let newCol = Math.round(currentLeft / (W_col + 6)) + 1;
            newCol = Math.max(1, Math.min(12, newCol));

            let newColSpan = startColSpan;
            if (newCol + newColSpan - 1 > 12) {
                newColSpan = Math.max(1, 13 - newCol);
            }

            let newRow = Math.round(currentTop / 66) + 1;
            newRow = Math.max(1, newRow);

            targetCol = newCol;
            targetRow = newRow;
            targetColSpan = newColSpan;
            targetRowSpan = startRowSpan;

            ghostEl.style.gridColumn = `${newCol} / span ${newColSpan}`;
            ghostEl.style.gridRow    = `${newRow} / span ${startRowSpan}`;

            // Highlight hover container drop zones
            let hoverContainerId = null;
            if (v.type !== 'CONTAINER') {
                const containers = curVis().filter(c => c.type === 'CONTAINER');
                const parentContainer = containers.find(c => {
                    const cColStart = c.gridCol || 1;
                    const cColEnd = cColStart + (c.gridColSpan || 12) - 1;
                    const cRowStart = c.gridRow || 1;
                    const cRowEnd = cRowStart + (c.gridRowSpan || 4) - 1;
                    return targetCol >= cColStart && targetCol <= cColEnd && targetRow >= cRowStart && targetRow <= cRowEnd;
                });
                if (parentContainer) hoverContainerId = parentContainer.id;
            }

            for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card.is-container')) {
                if (card.dataset.vid === hoverContainerId) {
                    card.classList.add('drop-zone-hover');
                } else {
                    card.classList.remove('drop-zone-hover');
                }
            }

        } else if (isResizing) {
            if (!ghostEl) {
                ghostEl = document.createElement('div');
                ghostEl.className = 'etlsql-dsgn-grid-ghost';
                ghostEl.style.gridColumn = `${startCol} / span ${startColSpan}`;
                ghostEl.style.gridRow    = `${startRow} / span ${startRowSpan}`;
                canvasGrid.appendChild(ghostEl);
            }

            const cardRightX = e.clientX - gridRect.left - 16;
            const cardBottomY = e.clientY - gridRect.top - 16;

            const cardLeftX = (startCol - 1) * (W_col + 6);
            const cardTopY = (startRow - 1) * 66;

            let newColSpan = Math.round((cardRightX - cardLeftX + 6) / (W_col + 6));
            newColSpan = Math.max(1, Math.min(13 - startCol, newColSpan));

            let newRowSpan = Math.round((cardBottomY - cardTopY + 6) / 66);
            newRowSpan = Math.max(1, newRowSpan);

            targetCol = startCol;
            targetRow = startRow;
            targetColSpan = newColSpan;
            targetRowSpan = newRowSpan;

            activeCardEl.style.gridColumn = `${startCol} / span ${newColSpan}`;
            activeCardEl.style.gridRow    = `${startRow} / span ${newRowSpan}`;

            ghostEl.style.gridColumn = `${startCol} / span ${newColSpan}`;
            ghostEl.style.gridRow    = `${startRow} / span ${newRowSpan}`;

            const chartBody = activeCardEl.querySelector('.etlsql-dsgn-vcard-body');
            if (chartBody && window.echarts) {
                try {
                    const chart = window.echarts.getInstanceByDom(chartBody);
                    chart?.resize();
                } catch {}
            }
        }
    }

    function handleMouseUp(e) {
        if (ghostEl) {
            ghostEl.remove();
            ghostEl = null;
        }

        for (const card of canvasGrid.querySelectorAll('.etlsql-dsgn-visual-card.is-container')) {
            card.classList.remove('drop-zone-hover');
        }

        if (activeId && activeCardEl) {
            activeCardEl.classList.remove('dragging');
            activeCardEl.style.position = '';
            activeCardEl.style.width = '';
            activeCardEl.style.height = '';
            activeCardEl.style.left = '';
            activeCardEl.style.top = '';
            activeCardEl.style.transform = '';
            activeCardEl.style.zIndex = '';
            activeCardEl.style.opacity = '';

            const v = findVis(activeId);
            if (v) {
                const deltaCol = targetCol - (v.gridCol || 1);
                const deltaRow = targetRow - (v.gridRow || 1);

                v.gridCol = targetCol;
                v.gridRow = targetRow;
                v.gridColSpan = targetColSpan;
                v.gridRowSpan = targetRowSpan;

                if (selVisualIds.has(v.id) && selVisualIds.size > 1 && isDragging && (deltaCol !== 0 || deltaRow !== 0)) {
                    for (const otherId of selVisualIds) {
                        if (otherId !== v.id) {
                            const other = findVis(otherId);
                            if (other) {
                                other.gridCol = Math.max(1, (other.gridCol || 1) + deltaCol);
                                other.gridRow = Math.max(1, (other.gridRow || 1) + deltaRow);
                            }
                        }
                    }
                } else if (v.type === 'CONTAINER' && isDragging && (deltaCol !== 0 || deltaRow !== 0)) {
                    for (const child of curVis()) {
                        if (child.containerId === v.id) {
                            child.gridCol = Math.max(1, (child.gridCol || 1) + deltaCol);
                            child.gridRow = Math.max(1, (child.gridRow || 1) + deltaRow);
                        }
                    }
                } else if (v.type !== 'CONTAINER' && isDragging) {
                    const containers = curVis().filter(c => c.type === 'CONTAINER' && c.id !== v.id);
                    const parentContainer = containers.find(c => {
                        const cColStart = c.gridCol || 1;
                        const cColEnd = cColStart + (c.gridColSpan || 12) - 1;
                        const cRowStart = c.gridRow || 1;
                        const cRowEnd = cRowStart + (c.gridRowSpan || 4) - 1;
                        return targetCol >= cColStart && targetCol <= cColEnd && targetRow >= cRowStart && targetRow <= cRowEnd;
                    });
                    v.containerId = parentContainer ? parentContainer.id : null;
                }
            }

            renderCanvas();
            renderProps();
        }

        isDragging = false;
        isResizing = false;
        activeId = null;
        activeCardEl = null;
        initialRect = null;
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
    }

    canvasGrid.addEventListener('click', e => {
        const del = e.target.closest('[data-del]');
        if (del) { deleteVisual(del.dataset.del); return; }
        const card = e.target.closest('.etlsql-dsgn-visual-card');
        if (card) {
            selectVisual(card.dataset.vid, { toggle: e.shiftKey || e.ctrlKey || e.metaKey });
        } else {
            selectVisual(null);
        }
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
