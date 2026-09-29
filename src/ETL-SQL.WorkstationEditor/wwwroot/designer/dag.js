// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/dag.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/dag.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * dag.js — split out of designer.js, TODO.md §2.
 * Lineage DAG layout and rendering, including the compact and capsule variants.
 */
import { escapeHtml } from './designer-util.js';
export const _TYPE_COLOR = {
    dataset: '#10b981',
    connection: '#0ea5e9',
    visual: '#3b82f6',
    page: '#8b5cf6',
    container: '#334155',
    table: '#64748b',
    column: '#94a3b8',
    statement: '#475569',
    conditional: '#f59e0b',
    loop: '#f97316',
    parallel: '#06b6d4',
    transaction: '#2dd4bf',
    validation: '#eab308',
    io: '#14b8a6',
    outbound: '#0f766e',
    destructive: '#dc2626',
    procedure: '#a855f7',
};
export function _nodeColor(type) {
    return _TYPE_COLOR[type] ?? '#94a3b8';
}
/**
 * Assign x,y positions to nodes using a top-down layered (Sugiyama-inspired) layout.
 * Returns a map of { [nodeId]: { x, y } }.
 */
export function _computeLayout(nodes, edges) {
    const ids = nodes.map(n => n.id);
    const inDeg = Object.fromEntries(ids.map(id => [id, 0]));
    const children = Object.fromEntries(ids.map(id => [id, []]));
    for (const e of edges) {
        if (inDeg[e.target] !== undefined)
            inDeg[e.target]++;
        if (children[e.source])
            children[e.source].push(e.target);
    }
    // BFS from roots to assign layers
    const layer = {};
    const queue = ids.filter(id => inDeg[id] === 0);
    for (const id of queue)
        layer[id] = 0;
    while (queue.length > 0) {
        const id = queue.shift();
        const cur = layer[id] ?? 0;
        for (const child of children[id] || []) {
            if (layer[child] === undefined || layer[child] <= cur) {
                layer[child] = cur + 1;
                queue.push(child);
            }
        }
    }
    // Any unreached nodes (isolated or cycles) get layer 0
    for (const id of ids)
        if (layer[id] === undefined)
            layer[id] = 0;
    // Group by layer, preserving original node order within each layer
    const byLayer = {};
    for (const id of ids) {
        const l = layer[id];
        (byLayer[l] = byLayer[l] || []).push(id);
    }
    const LAYER_H = 300;
    const SUB_ROW_H = 180;
    const NODE_W = 360;
    const MAX_PER_ROW = 6;
    const pos = {};
    let yBase = 0;
    const sortedLayers = Object.keys(byLayer).map(Number).sort((a, b) => a - b);
    for (const l of sortedLayers) {
        const layerIds = byLayer[l];
        const count = layerIds.length;
        const numRows = Math.ceil(count / MAX_PER_ROW);
        layerIds.forEach((id, i) => {
            const row = Math.floor(i / MAX_PER_ROW);
            const colInRow = i % MAX_PER_ROW;
            const rowCount = Math.min(MAX_PER_ROW, count - row * MAX_PER_ROW);
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
export function _lineageReach(rootId, allEdges, allNodes) {
    const down = {}, up = {};
    for (const e of allEdges) {
        (down[e.source] ??= []).push(e.target);
        (up[e.target] ??= []).push(e.source);
    }
    const keep = new Set([rootId]);
    const walk = (adj) => {
        const stack = [rootId];
        while (stack.length) {
            const id = stack.pop();
            for (const nxt of (adj[id] ?? []))
                if (!keep.has(nxt)) {
                    keep.add(nxt);
                    stack.push(nxt);
                }
        }
    };
    walk(down); // descendants
    walk(up); // ancestors
    // Keep expanded column children whose parent node is in focus.
    for (const n of allNodes)
        if (n.meta?.parent && keep.has(n.meta.parent))
            keep.add(n.id);
    return keep;
}
/**
 * How a precedence edge is drawn, from the label the projection put on it.
 *
 * Shared by every host for the same reason the graph shape is: an on-failure edge has to look like
 * an on-failure edge in the Portal, in VS Code, and in the Workstation editor, or the same script
 * reads as a different pipeline depending on where it is opened.
 */
export function _edgeStyle(label) {
    const text = String(label ?? '').trim().toUpperCase();
    if (text === 'ON SUCCESS')
        return { kind: 'success', color: '#3fb950', dash: null };
    if (text === 'ON FAILURE')
        return { kind: 'failure', color: '#f85149', dash: '6 4' };
    if (text === 'ON COMPLETION')
        return { kind: 'completion', color: '#58a6ff', dash: '2 3' };
    if (text.startsWith('WHEN '))
        return { kind: 'expression', color: '#d29922', dash: '10 3 2 3' };
    return { kind: null, color: '#8b949e', dash: null };
}
export function renderDag(container, { nodes, edges }, options = {}) {
    const graphNodes = (nodes ?? []).map(n => ({ ...n, type: n.type || 'table' }));
    const graphEdges = edges ?? [];
    if (!graphNodes.length) {
        container.innerHTML = '<div class="etlsql-dag-empty">No structure data available.</div>';
        return { dispose: () => { }, resize: () => { }, showDetail: () => { } };
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
    const cardWidth = options.cardWidth ?? 260;
    // Marker ids are document-global, and more than one map can be on the page at once.
    const markerPrefix = `etlsql-dag-head-${Math.random().toString(36).slice(2, 10)}`;
    const computePositions = (layoutNodes, layoutEdges) => {
        const projected = _computeLayout(layoutNodes, layoutEdges);
        if (options.orientation !== 'horizontal')
            return projected;
        return Object.fromEntries(Object.entries(projected).map(([id, point]) => [id, {
                x: point.y,
                y: point.x * 0.55,
            }]));
    };
    let positions = computePositions(graphNodes, graphEdges);
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
    // The CSS places this 20000px layer at -10000px so lines can run anywhere around the cards; the
    // viewBox puts its origin back on the viewport's, where `centerOf` measures. Without it every
    // edge was drawn 10000 units up and to the left - present in the DOM, off screen on every map.
    svg.setAttribute('viewBox', '-10000 -10000 20000 20000');
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
    zoomControls.append(zoomButton('+', 'Zoom in', () => setZoom(Math.min(2, zoom * 1.2))), zoomButton('-', 'Zoom out', () => setZoom(Math.max(0.1, zoom / 1.2))), zoomButton('Reset', 'Fit graph to view', fitToView));
    const presentTypes = [...new Set(graphNodes.map(n => n.type || 'table'))].sort();
    buildChips();
    render();
    requestAnimationFrame(fitToView);
    searchInput.addEventListener('input', () => {
        const term = searchInput.value.trim().toLowerCase();
        searchMatches = term ? visibleNodes().filter(n => String(n.label ?? '').toLowerCase().includes(term)).map(n => n.id) : [];
        searchIdx = -1;
        updateSearchCount();
        if (searchMatches.length)
            nextMatch();
    });
    searchInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') {
            e.preventDefault();
            nextMatch();
        }
        if (e.key === 'Escape') {
            searchInput.value = '';
            searchMatches = [];
            searchIdx = -1;
            updateSearchCount();
        }
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
        if (e.target !== canvas && e.target !== viewport && e.target !== svg)
            return;
        isPanning = true;
        panStartX = e.clientX - panX;
        panStartY = e.clientY - panY;
        canvas.style.cursor = 'grabbing';
    });
    const onDocMove = (e) => {
        if (!isPanning)
            return;
        panX = e.clientX - panStartX;
        panY = e.clientY - panStartY;
        updateViewport(false);
    };
    const onDocUp = () => {
        if (!isPanning)
            return;
        isPanning = false;
        canvas.style.cursor = '';
        drawConnections();
    };
    document.addEventListener('mousemove', onDocMove);
    document.addEventListener('mouseup', onDocUp);
    function visibleNodes() {
        return graphNodes.filter(n => !hiddenTypes.has(n.type || 'table'));
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
            text.textContent = `${type} ${graphNodes.filter(n => (n.type || 'table') === type).length}`;
            chip.append(dot, text);
            chip.addEventListener('click', () => {
                if (hiddenTypes.has(type))
                    hiddenTypes.delete(type);
                else
                    hiddenTypes.add(type);
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
        if (disposed)
            return;
        const nodesToRender = visibleNodes();
        positions = { ...positions, ...computePositions(nodesToRender, visibleEdges()) };
        cardLayer.replaceChildren();
        for (const node of nodesToRender)
            renderCard(node);
        updateFocusBadge();
        updateViewport();
        options.onNodeClick?.(focusedNode, focusedNode ? (nodeById[focusedNode]?.meta ?? null) : null);
    }
    function renderCard(node) {
        const p = positions[node.id] ?? { x: 0, y: 0 };
        const card = document.createElement('div');
        card.id = `node__${node.id}`;
        card.className = 'etlsql-dag-card';
        card.style.left = `${p.x - cardWidth / 2}px`;
        card.style.top = `${p.y}px`;
        card.style.width = `${cardWidth}px`;
        card.style.border = `1px solid ${_nodeColor(node.type || 'table')}`;
        card.dataset.nodeId = node.id;
        card.dataset.dagNode = node.id;
        // A statement introduced by a section label is addressable by that label rather than by its
        // positional id. The DAG stays read-only; it just says which cards a surface can edit.
        if (node.meta?.key)
            card.dataset.taskKey = node.meta.key;
        const header = document.createElement('div');
        header.className = 'etlsql-dag-card-header';
        const title = document.createElement('span');
        title.textContent = node.label ?? node.id;
        title.style.overflow = 'hidden';
        title.style.textOverflow = 'ellipsis';
        title.style.whiteSpace = 'nowrap';
        const kind = document.createElement('span');
        kind.textContent = node.type || 'table';
        kind.style.color = _nodeColor(node.type || 'table');
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
        leftPort.style.background = _nodeColor(node.type || 'table');
        const rightPort = document.createElement('span');
        rightPort.className = 'card-port-right';
        rightPort.style.background = _nodeColor(node.type || 'table');
        card.append(leftPort, rightPort);
        header.addEventListener('mousedown', e => {
            // A card the pipeline surface has claimed is a drag source for the script: dragging it
            // onto another task reorders, and onto a container nests. Repositioning a node on the
            // map is cosmetic and is not persisted, so where a card is both, the structural gesture
            // wins.
            //
            // It has to win here rather than later, because `startNodeDrag` calls preventDefault and
            // that cancels the native drag before it starts. The header is also not a small target
            // to avoid: these cards render about 26px tall with a 33px header, so the header is the
            // whole card - which meant the reorder and nest gestures could never fire at all, from
            // anywhere on any card, for anyone.
            if (card.classList.contains('is-editable-task'))
                return;
            startNodeDrag(e, node.id, card);
        });
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
        const move = (me) => {
            positions[nodeId] = {
                x: original.x + (me.clientX - startX) / zoom,
                y: original.y + (me.clientY - startY) / zoom,
            };
            card.style.left = `${positions[nodeId].x - cardWidth / 2}px`;
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
        if (pathSet.has(key))
            return;
        pathSet.add(key);
        pathSet.add(nodeId);
        const node = nodeById[nodeId];
        if (!node)
            return;
        if (direction === 'both' || direction === 'up') {
            for (const src of (node.meta?.columnLineage?.[column]?.sources ?? [])) {
                const srcNode = graphNodes.find(n => n.label === src.table || n.id === src.table);
                if (srcNode)
                    traceColumnPath(srcNode.id, src.column, pathSet, 'up');
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
        if (!node)
            return;
        panel.style.display = 'block';
        panel.replaceChildren();
        const head = document.createElement('div');
        head.className = 'etlsql-dag-panel-head';
        const dot = document.createElement('span');
        dot.className = 'etlsql-dag-panel-dot';
        dot.style.background = _nodeColor(node.type || 'table');
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
        appendPanelList('Metadata', Object.entries(node.meta ?? {}).filter(([, v]) => typeof v !== 'object').map(([k, v]) => ({ k, v })), 'No scalar metadata.');
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
        if (!node)
            return;
        const inFocus = !focusSet || focusSet.has(node.id);
        card.style.opacity = inFocus ? '1' : '0.12';
        card.style.borderColor = node.id === focusedNode ? '#f8fafc' : _nodeColor(node.type || 'table');
        for (const row of card.querySelectorAll('.etlsql-dag-col-row')) {
            const rowId = row.id;
            const active = !activeColumnPathSet || activeColumnPathSet.has(rowId) || activeColumnPathSet.has(node.id);
            row.style.opacity = active ? '1' : '0.14';
            const label = row.querySelector('.col-label-span');
            if (label)
                label.style.color = activeColumnPathSet?.has(rowId) ? '#34d399' : '#cbd5e1';
        }
    }
    function drawConnections() {
        svg.replaceChildren();
        badgeLayer.replaceChildren();
        const rect = viewport.getBoundingClientRect();
        for (const edge of visibleEdges())
            drawEdge(edge, rect);
        drawColumnEdges(rect);
    }
    function drawEdge(edge, rect) {
        const from = document.getElementById(`node__${edge.source}`);
        const to = document.getElementById(`node__${edge.target}`);
        if (!from || !to)
            return;
        const fromPort = from.querySelector('.card-port-right');
        const toPort = to.querySelector('.card-port-left');
        if (!fromPort || !toPort)
            return;
        const a = centerOf(fromPort, rect);
        const b = centerOf(toPort, rect);
        const inPath = !focusSet || (focusSet.has(edge.source) && focusSet.has(edge.target));
        const style = _edgeStyle(edge.label);
        const path = drawLink(a.x, a.y, b.x, b.y, inPath ? style.color : 'rgba(71,85,105,0.08)', inPath ? 2 : 1);
        // A connector is read at whatever zoom the map is at, so its stroke is measured in screen
        // pixels rather than graph units - scaled with the map it thinned to half a pixel. The head
        // is what says "this runs, then that": without it a line between two steps has no direction.
        path.setAttribute('vector-effect', 'non-scaling-stroke');
        if (inPath)
            path.setAttribute('marker-end', `url(#${arrowHead(style.color)})`);
        // The dash is not decoration. Colour alone would leave the difference between "only if this
        // succeeded" and "only if this failed" invisible to a red/green colour-blind reader, and to
        // anyone printing the map, so every conditional edge also has its own stroke pattern and
        // keeps the words on its badge.
        if (style.dash && inPath)
            path.setAttribute('stroke-dasharray', style.dash);
        path.dataset.dagSource = edge.source;
        path.dataset.dagTarget = edge.target;
        if (edge.label)
            path.dataset.dagLabel = edge.label;
        if (style.kind)
            path.dataset.dagEdgeKind = style.kind;
        if (edge.label && inPath)
            drawEdgeBadge(a.x, a.y, b.x, b.y, edge.label, false, false, style.color);
    }
    /** The id of an arrowhead in this colour, created on first use. `drawConnections` clears the SVG, defs included. */
    function arrowHead(color) {
        const id = `${markerPrefix}-${color.replace(/[^a-z0-9]/gi, '')}`;
        if (svg.querySelector(`#${id}`))
            return id;
        let defs = svg.querySelector('defs');
        if (!defs) {
            defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
            svg.prepend(defs);
        }
        const marker = document.createElementNS('http://www.w3.org/2000/svg', 'marker');
        marker.id = id;
        marker.setAttribute('viewBox', '0 0 10 10');
        marker.setAttribute('refX', '9');
        marker.setAttribute('refY', '5');
        marker.setAttribute('markerWidth', '12');
        marker.setAttribute('markerHeight', '12');
        marker.setAttribute('markerUnits', 'userSpaceOnUse');
        marker.setAttribute('orient', 'auto');
        const head = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        head.setAttribute('d', 'M 0 0 L 10 5 L 0 10 z');
        head.setAttribute('fill', color);
        marker.appendChild(head);
        defs.appendChild(marker);
        return id;
    }
    function drawColumnEdges(rect) {
        for (const node of visibleNodes()) {
            for (const [targetColumn, lineage] of Object.entries(node.meta?.columnLineage ?? {})) {
                for (const src of (lineage.sources ?? [])) {
                    const srcNode = graphNodes.find(n => n.label === src.table || n.id === src.table);
                    if (!srcNode || hiddenTypes.has(srcNode.type || 'table'))
                        continue;
                    const from = document.getElementById(`${srcNode.id}__col__${src.column}`);
                    const to = document.getElementById(`${node.id}__col__${targetColumn}`);
                    if (!from || !to)
                        continue;
                    const fromPort = from.querySelector('.port-right');
                    const toPort = to.querySelector('.port-left');
                    if (!fromPort || !toPort)
                        continue;
                    const a = centerOf(fromPort, rect);
                    const b = centerOf(toPort, rect);
                    const fromKey = `${srcNode.id}__col__${src.column}`;
                    const toKey = `${node.id}__col__${targetColumn}`;
                    const inPath = activeColumnPathSet && activeColumnPathSet.has(fromKey) && activeColumnPathSet.has(toKey);
                    const dim = activeColumnPathSet && !inPath;
                    drawLink(a.x, a.y, b.x, b.y, inPath ? '#10b981' : (dim ? 'rgba(16,185,129,0.05)' : 'rgba(16,185,129,0.35)'), inPath ? 3 : 1, !inPath);
                    if (lineage.transform && !dim)
                        drawEdgeBadge(a.x, a.y, b.x, b.y, transformLabel(lineage.transform), Boolean(inPath), Boolean(dim));
                }
            }
        }
    }
    function drawLink(x1, y1, x2, y2, color, width, dashed = false) {
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        const dx = Math.abs(x2 - x1) * 0.45;
        path.setAttribute('d', `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`);
        path.setAttribute('stroke', color);
        path.setAttribute('stroke-width', String(width));
        path.setAttribute('fill', 'none');
        if (dashed)
            path.setAttribute('stroke-dasharray', '4 4');
        svg.appendChild(path);
        return path;
    }
    function drawEdgeBadge(x1, y1, x2, y2, text, inPath, dim, accent = null) {
        const badgeEl = document.createElement('div');
        badgeEl.className = 'etlsql-dag-edge-badge';
        badgeEl.style.left = `${(x1 + x2) / 2}px`;
        badgeEl.style.top = `${(y1 + y2) / 2}px`;
        badgeEl.style.background = inPath ? '#0f766e' : '#1e293b';
        // A conditional edge borders and letters its badge in the line's own colour, so the words
        // and the stroke are read as one thing rather than as a label that happens to sit near it.
        badgeEl.style.border = `1px solid ${accent ?? (inPath ? '#14b8a6' : '#3b82f6')}`;
        badgeEl.style.color = accent ?? (inPath ? '#ccfbf1' : '#93c5fd');
        badgeEl.style.opacity = dim ? '0.12' : '1';
        badgeEl.textContent = text;
        badgeLayer.appendChild(badgeEl);
    }
    function centerOf(el, viewportRect) {
        if (!el)
            return { x: 0, y: 0 };
        const r = el.getBoundingClientRect();
        return { x: (r.left + r.width / 2 - viewportRect.left) / zoom, y: (r.top + r.height / 2 - viewportRect.top) / zoom };
    }
    function updateViewport(redraw = true) {
        viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
        if (redraw)
            requestAnimationFrame(drawConnections);
    }
    function setZoom(value) {
        zoom = value;
        updateViewport();
    }
    function fitToView() {
        const visible = visibleNodes();
        if (!visible.length || !canvas.clientWidth || !canvas.clientHeight)
            return;
        const points = visible.map(node => positions[node.id]).filter(Boolean);
        if (!points.length)
            return;
        const minX = Math.min(...points.map(point => point.x)) - (cardWidth / 2 + 20);
        const maxX = Math.max(...points.map(point => point.x)) + (cardWidth / 2 + 20);
        const minY = Math.min(...points.map(point => point.y)) - 45;
        const maxY = Math.max(...points.map(point => point.y)) + 110;
        const graphWidth = Math.max(300, maxX - minX);
        const graphHeight = Math.max(155, maxY - minY);
        const fitted = Math.min(1.1, (canvas.clientWidth - 48) / graphWidth, (canvas.clientHeight - 40) / graphHeight);
        const floor = options.minFitZoom ?? 0.2;
        zoom = Math.max(floor, fitted);
        // Too big to fit legibly: open on the first stage, where the flow starts, and let the author
        // pan along it - the way a long SSIS package opens at its first task rather than shrunk to a strip.
        panX = fitted < floor
            ? 24 - canvas.clientWidth / 2 - minX * zoom
            : -((minX + maxX) / 2) * zoom;
        panY = canvas.clientHeight * 0.1 - ((minY + maxY) / 2) * zoom;
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
        const label = activeColumnLabel || (focusedNode ? nodeById[focusedNode]?.label : null) || focusedNode;
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
        if (!searchMatches.length)
            return;
        searchIdx = (searchIdx + 1) % searchMatches.length;
        updateSearchCount();
        const id = searchMatches[searchIdx];
        const p = positions[id];
        if (!p)
            return;
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
            for (const remove of dragRemovers)
                remove();
            container.innerHTML = '';
        },
        resize() { fitToView(); },
        showDetail(id) { showNodeDetails(nodeById[id]); },
    };
}
// Flattens the execution tree into left-to-right swimlane columns: each sequential
// step is its own column, a parallel block stacks its branches inside one column, and
// a plain container (the script root) contributes its steps rather than itself.
export function flattenDagColumns(nodes, columns = []) {
    for (const node of (nodes || [])) {
        const children = Array.isArray(node.children) ? node.children : [];
        if (node.isParallelBlock && children.length) {
            columns.push({ type: 'parallel', nodes: children });
        }
        else if (children.length) {
            flattenDagColumns(children, columns);
        }
        else {
            columns.push({ type: 'single', node });
        }
    }
    return columns;
}
export function renderCompactDag(nodes) {
    if (!nodes || !nodes.length)
        return '';
    const columns = flattenDagColumns(nodes);
    if (!columns.length)
        return '';
    let html = `<div class="etlsql-compact-dag">`;
    html += `<svg class="etlsql-compact-dag-svg" style="position:absolute; inset:0; width:100%; height:100%; pointer-events:none; z-index:0;"></svg>`;
    html += `<div class="etlsql-compact-dag-columns" style="display:flex; gap:60px; padding:20px; align-items:center; position:relative; z-index:1; height:100%;">`;
    columns.forEach((col, colIdx) => {
        html += `<div class="etlsql-compact-dag-column" style="display:flex; flex-direction:column; gap:12px; justify-content:center;">`;
        if (col.type === 'single') {
            html += renderDagCapsule(col.node, colIdx, 0);
        }
        else {
            col.nodes.forEach((childNode, rowIdx) => {
                html += renderDagCapsule(childNode, colIdx, rowIdx);
            });
        }
        html += `</div>`;
    });
    html += `</div></div>`;
    return html;
}
export function renderDagCapsule(node, col, row) {
    const statusClass = (node.status || '').toLowerCase();
    const rows = Number(node.rowsProcessed || 0).toLocaleString();
    const duration = node.durationMs != null ? `${Math.round(node.durationMs).toLocaleString()} ms` : '';
    let statusIcon = '⚪';
    if (statusClass === 'completed' || statusClass === 'success')
        statusIcon = '✅';
    else if (statusClass === 'running')
        statusIcon = '🔄';
    else if (statusClass === 'failed' || statusClass === 'error')
        statusIcon = '❌';
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
export function updateDagLines(container) {
    const svg = container.querySelector('.etlsql-compact-dag-svg');
    if (!svg)
        return;
    svg.innerHTML = '';
    const containerRect = container.getBoundingClientRect();
    const capsules = Array.from(container.querySelectorAll('.etlsql-dag-capsule'));
    const cols = {};
    capsules.forEach(cap => {
        const col = parseInt(cap.dataset.col || '0', 10);
        if (!cols[col])
            cols[col] = [];
        cols[col].push(cap);
    });
    const sortedColKeys = Object.keys(cols).map(Number).sort((a, b) => a - b);
    for (let i = 0; i < sortedColKeys.length - 1; i++) {
        const c1 = sortedColKeys[i];
        const c2 = sortedColKeys[i + 1];
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
