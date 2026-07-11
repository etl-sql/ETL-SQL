import { buildKitchenSinkGraph, buildSmallGraph, buildEdwExampleGraph, buildCrossScriptGraph } from '../fixture.js';
import { importFresh, DESIGNER_JS } from '../util.js';

const BUILDERS = {
  kitchen: buildKitchenSinkGraph,
  small:   buildSmallGraph,
  edw:     buildEdwExampleGraph,
  xscript: buildCrossScriptGraph,
};

// Custom BFS layout algorithm (matching designer.js layout spacing)
function _computeLayout(nodes, edges) {
  const ids     = nodes.map(n => n.id);
  const inDeg   = Object.fromEntries(ids.map(id => [id, 0]));
  const children = Object.fromEntries(ids.map(id => [id, []]));

  for (const e of edges) {
    if (inDeg[e.target] !== undefined)  inDeg[e.target]++;
    if (children[e.source])             children[e.source].push(e.target);
  }

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
  for (const id of ids) if (layer[id] === undefined) layer[id] = 0;

  const byLayer = {};
  for (const id of ids) {
    const l = layer[id];
    (byLayer[l] = byLayer[l] || []).push(id);
  }

  const LAYER_H    = 280;
  const SUB_ROW_H  = 160;
  const NODE_W     = 340;
  const MAX_PER_ROW = 6; // slightly narrower wrap for compact presentation

  const pos = {};
  let yBase = -200; // center vertically
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

export default {
  id: 'lineage-mockup',
  title: 'React Flow / Port Lineage Mockup',
  subtitle: 'Interactive column-level flow',
  fixtures: [
    { id: 'kitchen', label: 'Kitchen Sink (~106 nodes)' },
    { id: 'small',   label: 'Small report (5 nodes)' },
    { id: 'edw',     label: 'EDW example (salesBar drill-down)' },
    { id: 'xscript', label: 'Cross-script (dataset built separately)' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.innerHTML = '';

    const graph = (BUILDERS[fixtureId] ?? buildKitchenSinkGraph)();
    ctx.stat(`Mockup: ${graph.nodes.length} nodes · Drag headers to move · Wheel to zoom · Drag background to pan`);

    // 1. Setup infinite-canvas viewport
    stage.style.position = 'relative';
    stage.style.width = '100%';
    stage.style.height = '100%';
    stage.style.background = '#090d16';
    stage.style.overflow = 'hidden';
    stage.style.userSelect = 'none';

    const viewport = document.createElement('div');
    viewport.style.position = 'absolute';
    viewport.style.left = '50%';
    viewport.style.top = '40%';
    viewport.style.width = '0';
    viewport.style.height = '0';
    viewport.style.transformOrigin = 'center center';
    stage.appendChild(viewport);

    // 2. Setup large SVG overlay for lines
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.style.position = 'absolute';
    svg.style.left = '-10000px';
    svg.style.top = '-10000px';
    svg.style.width = '20000px';
    svg.style.height = '20000px';
    svg.style.pointerEvents = 'none';
    svg.style.zIndex = '1';
    viewport.appendChild(svg);

    // Helper to draw Bezier Curve
    function drawLink(x1, y1, x2, y2, color = '#64748b', width = 1.5, isDashed = false) {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      const dx = Math.abs(x2 - x1) * 0.45;
      path.setAttribute('d', `M ${x1 + 10000} ${y1 + 10000} C ${x1 + dx + 10000} ${y1 + 10000}, ${x2 - dx + 10000} ${y2 + 10000}, ${x2 + 10000} ${y2 + 10000}`);
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

    // 3. Compute layout positions
    const pos = _computeLayout(graph.nodes, graph.edges);

    const cardElements = [];
    let activeHighlightNode = null; // clicked node for highlighting column lineage

    // 4. Render Nodes
    graph.nodes.forEach(n => {
      const card = document.createElement('div');
      card.id = `node__${n.id}`;
      card.style.position = 'absolute';
      card.style.left = `${pos[n.id].x - 120}px`; // center node X
      card.style.top = `${pos[n.id].y}px`;
      card.style.background = '#111827';
      card.style.borderRadius = '8px';
      card.style.boxShadow = '0 10px 15px -3px rgba(0, 0, 0, 0.4)';
      card.style.color = '#cbd5e1';
      card.style.zIndex = '10';
      card.style.cursor = 'grab';

      // Assign type specific styling
      let titleColor = '#cbd5e1';
      let borderStyle = '1px solid #1f2937';
      let showColumns = false;

      if (n.type === 'table') {
        titleColor = '#3b82f6'; // blue
        borderStyle = '1px solid #2563eb';
        showColumns = true;
        card.style.width = '240px';
      } else if (n.type === 'dataset') {
        titleColor = '#8b5cf6'; // purple
        borderStyle = '1px solid #7c3aed';
        showColumns = true;
        card.style.width = '240px';
      } else if (n.type === 'page') {
        titleColor = '#e2e8f0';
        borderStyle = '1px solid #475569';
        card.style.width = '180px';
      } else if (n.type === 'visual') {
        titleColor = '#10b981'; // green
        borderStyle = '1px solid #059669';
        card.style.width = '200px';
      }

      card.style.border = borderStyle;

      // Card Header
      const header = document.createElement('div');
      header.style.padding = '8px 12px';
      header.style.background = '#1f2937';
      header.style.borderBottom = '1px solid #374151';
      header.style.borderTopLeftRadius = '7px';
      header.style.borderTopRightRadius = '7px';
      header.style.fontWeight = 'bold';
      header.style.fontSize = '12px';
      header.style.color = titleColor;
      header.style.display = 'flex';
      header.style.alignItems = 'center';
      header.style.justifyContent = 'space-between';

      const titleSpan = document.createElement('span');
      titleSpan.textContent = n.label;
      titleSpan.style.overflow = 'hidden';
      titleSpan.style.textOverflow = 'ellipsis';
      titleSpan.style.whiteSpace = 'nowrap';
      header.appendChild(titleSpan);

      const typeDot = document.createElement('span');
      typeDot.style.width = '6px';
      typeDot.style.height = '6px';
      typeDot.style.borderRadius = '50%';
      typeDot.style.background = titleColor;
      header.appendChild(typeDot);

      card.appendChild(header);

      // Card Body
      if (showColumns && n.meta?.columns) {
        n.meta.columns.forEach(c => {
          const row = document.createElement('div');
          row.id = `${n.id}__col__${c}`;
          row.style.padding = '5px 12px';
          row.style.fontSize = '11px';
          row.style.display = 'flex';
          row.style.alignItems = 'center';
          row.style.justifyContent = 'space-between';
          row.style.position = 'relative';
          row.style.borderBottom = '1px solid #1f2937';

          const label = document.createElement('span');
          label.textContent = c;
          row.appendChild(label);

          // Left/Right ports
          const lp = document.createElement('div');
          lp.className = 'port-left';
          lp.style.position = 'absolute';
          lp.style.left = '-4px';
          lp.style.top = '50%';
          lp.style.transform = 'translateY(-50%)';
          lp.style.width = '8px';
          lp.style.height = '8px';
          lp.style.borderRadius = '50%';
          lp.style.background = '#475569';
          lp.style.border = '1px solid #0f172a';
          row.appendChild(lp);

          const rp = document.createElement('div');
          rp.className = 'port-right';
          rp.style.position = 'absolute';
          rp.style.right = '-4px';
          rp.style.top = '50%';
          rp.style.transform = 'translateY(-50%)';
          rp.style.width = '8px';
          rp.style.height = '8px';
          rp.style.borderRadius = '50%';
          rp.style.background = '#475569';
          rp.style.border = '1px solid #0f172a';
          row.appendChild(rp);

          card.appendChild(row);
        });
      } else {
        const desc = document.createElement('div');
        desc.style.padding = '8px 12px';
        desc.style.fontSize = '11px';
        desc.style.color = '#64748b';
        desc.style.fontStyle = 'italic';
        desc.textContent = n.type;
        card.appendChild(desc);
      }

      // Add generic card-level connection ports
      const cardLp = document.createElement('div');
      cardLp.className = 'card-port-left';
      cardLp.style.position = 'absolute';
      cardLp.style.left = '-5px';
      cardLp.style.top = '50%';
      cardLp.style.transform = 'translateY(-50%)';
      cardLp.style.width = '10px';
      cardLp.style.height = '10px';
      cardLp.style.borderRadius = '50%';
      cardLp.style.background = titleColor;
      cardLp.style.border = '2px solid #0f172a';
      card.appendChild(cardLp);

      const cardRp = document.createElement('div');
      cardRp.className = 'card-port-right';
      cardRp.style.position = 'absolute';
      cardRp.style.right = '-5px';
      cardRp.style.top = '50%';
      cardRp.style.transform = 'translateY(-50%)';
      cardRp.style.width = '10px';
      cardRp.style.height = '10px';
      cardRp.style.borderRadius = '50%';
      cardRp.style.background = titleColor;
      cardRp.style.border = '2px solid #0f172a';
      card.appendChild(cardRp);

      viewport.appendChild(card);
      cardElements.push({ n, card });

      // Dragging nodes
      card.addEventListener('mousedown', e => {
        if (e.target !== header && e.target !== titleSpan && e.target !== typeDot) return;
        e.preventDefault();
        card.style.cursor = 'grabbing';
        card.style.zIndex = '100';

        const startX = e.clientX;
        const startY = e.clientY;
        const origX = card.offsetLeft;
        const origY = card.offsetTop;

        function onMouseMove(me) {
          const dx = (me.clientX - startX) / zoom;
          const dy = (me.clientY - startY) / zoom;
          pos[n.id].x = origX + 120 + dx; // adjust for center offset
          pos[n.id].y = origY + dy;
          card.style.left = `${pos[n.id].x - 120}px`;
          card.style.top = `${pos[n.id].y}px`;
          updateConnections();
        }

        function onMouseUp() {
          card.style.cursor = 'grab';
          card.style.zIndex = '10';
          document.removeEventListener('mousemove', onMouseMove);
          document.removeEventListener('mouseup', onMouseUp);
        }

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
      });

      // Selection logic: Click on a table/dataset node to toggle its column-level lineage
      card.addEventListener('click', e => {
        if (e.target !== header && e.target !== titleSpan && e.target !== typeDot) return;
        if (activeHighlightNode === n.id) {
          activeHighlightNode = null;
        } else {
          activeHighlightNode = n.id;
        }
        updateConnections();
      });
    });

    // 5. Parse Column-to-Column Lineage from fixture metadata
    const colConnections = [];
    graph.nodes.forEach(n => {
      if (n.meta?.columnLineage) {
        Object.entries(n.meta.columnLineage).forEach(([tgtCol, lin]) => {
          if (lin.sources) {
            lin.sources.forEach(src => {
              // Find the source node id by matching label
              const srcNode = graph.nodes.find(x => x.label === src.table);
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

    // 6. Draw/Update Connections
    function updateConnections() {
      activePaths.forEach(p => p.remove());
      activePaths = [];

      const vRect = viewport.getBoundingClientRect();

      // 6.1 Render primary table-level connections
      graph.edges.forEach(e => {
        const fromCard = document.getElementById(`node__${e.source}`);
        const toCard = document.getElementById(`node__${e.target}`);

        if (!fromCard || !toCard) return;

        const fromPort = fromCard.querySelector('.card-port-right');
        const toPort = toCard.querySelector('.card-port-left');

        if (!fromPort || !toPort) return;

        const r1 = fromPort.getBoundingClientRect();
        const r2 = toPort.getBoundingClientRect();

        const x1 = (r1.left + r1.width / 2 - vRect.left) / zoom;
        const y1 = (r1.top + r1.height / 2 - vRect.top) / zoom;
        const x2 = (r2.left + r2.width / 2 - vRect.left) / zoom;
        const y2 = (r2.top + r2.height / 2 - vRect.top) / zoom;

        // Dim lines if a specific node is highlighted
        const isDimmed = activeHighlightNode && (activeHighlightNode !== e.source && activeHighlightNode !== e.target);
        const color = isDimmed ? 'rgba(71,85,105,0.1)' : '#475569';
        const width = isDimmed ? 0.75 : 1.5;

        const path = drawLink(x1, y1, x2, y2, color, width);
        activePaths.push(path);
      });

      // 6.2 Render column-to-column connections
      colConnections.forEach(c => {
        // Only show column lineage globally if no node is active, or highlight it if it's connected to the selected node
        const isDirectConnection = activeHighlightNode === c.fromTable || activeHighlightNode === c.toTable;
        if (activeHighlightNode && !isDirectConnection) return; // Hide unrelated column lines in focus mode

        const fromEl = document.getElementById(c.from);
        const toEl = document.getElementById(c.to);

        if (!fromEl || !toEl) return;

        const fromPort = fromEl.querySelector('.port-right');
        const toPort = toEl.querySelector('.port-left');

        if (!fromPort || !toPort) return;

        const r1 = fromPort.getBoundingClientRect();
        const r2 = toPort.getBoundingClientRect();

        const x1 = (r1.left + r1.width / 2 - vRect.left) / zoom;
        const y1 = (r1.top + r1.height / 2 - vRect.top) / zoom;
        const x2 = (r2.left + r2.width / 2 - vRect.left) / zoom;
        const y2 = (r2.top + r2.height / 2 - vRect.top) / zoom;

        const color = activeHighlightNode ? '#10b981' : 'rgba(16,185,129,0.35)'; // bold green on focus, subtle green by default
        const width = activeHighlightNode ? 2.5 : 1;

        const path = drawLink(x1, y1, x2, y2, color, width, !activeHighlightNode);
        activePaths.push(path);
      });
    }

    // 7. Pan and Zoom Interaction
    let panX = 0;
    let panY = 0;
    let zoom = fixtureId === 'kitchen' ? 0.25 : 0.65; // Zoom out further for kitchen sink by default

    // Align viewport initial transform
    viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;

    stage.addEventListener('wheel', e => {
      e.preventDefault();
      const factor = 1.15;
      if (e.deltaY < 0) {
        zoom = Math.min(2, zoom * factor);
      } else {
        zoom = Math.max(0.08, zoom / factor);
      }
      viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
      updateConnections();
    });

    let isPanning = false;
    let startX = 0;
    let startY = 0;

    stage.addEventListener('mousedown', e => {
      if (e.target !== stage && e.target !== svg) return;
      isPanning = true;
      startX = e.clientX - panX;
      startY = e.clientY - panY;
      stage.style.cursor = 'grabbing';
    });

    document.addEventListener('mousemove', e => {
      if (!isPanning) return;
      panX = e.clientX - startX;
      panY = e.clientY - startY;
      viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
    });

    document.addEventListener('mouseup', () => {
      if (isPanning) {
        isPanning = false;
        stage.style.cursor = 'default';
      }
    });

    // Initial draw
    setTimeout(updateConnections, 100);

    return {
      dispose() {
        svg.remove();
      },
      resize() {
        updateConnections();
      }
    };
  }
};
