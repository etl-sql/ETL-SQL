import { buildKitchenSinkGraph, buildSmallGraph, buildEdwExampleGraph, buildCrossScriptGraph } from '../fixture.js';

const BUILDERS = {
  kitchen: buildKitchenSinkGraph,
  small:   buildSmallGraph,
  edw:     buildEdwExampleGraph,
  xscript: buildCrossScriptGraph,
};

function _nodeColor(type) {
  switch (type) {
    case 'page':    return '#475569';
    case 'visual':  return '#10b981';
    case 'dataset': return '#8b5cf6';
    case 'table':   return '#3b82f6';
    default:        return '#64748b';
  }
}

// Reachability calculation for lineage flow (ancestors + descendants)
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

// Custom BFS layout algorithm
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
  const MAX_PER_ROW = 6;

  const pos = {};
  let yBase = -200;
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
    
    // 1. Setup layout grid containing Canvas + Sidebar panel
    stage.style.display = 'flex';
    stage.style.flexDirection = 'row';
    stage.style.width = '100%';
    stage.style.height = '100%';
    stage.style.background = '#090d16';
    stage.style.overflow = 'hidden';
    stage.style.userSelect = 'none';

    // Canvas container (takes remaining flex space)
    const canvasContainer = document.createElement('div');
    canvasContainer.style.flex = '1 1 auto';
    canvasContainer.style.height = '100%';
    canvasContainer.style.position = 'relative';
    canvasContainer.style.overflow = 'hidden';
    stage.appendChild(canvasContainer);

    // Sidebar detail panel (reuses production CSS class: etlsql-dag-panel)
    const panel = document.createElement('div');
    panel.className = 'etlsql-dag-panel';
    panel.style.display = 'none';
    stage.appendChild(panel);

    const graph = (BUILDERS[fixtureId] ?? buildKitchenSinkGraph)();
    ctx.stat(`Mockup: ${graph.nodes.length} nodes · Drag headers to move · Click to inspect · Ctrl+Click to filter`);

    const viewport = document.createElement('div');
    viewport.style.position = 'absolute';
    viewport.style.left = '50%';
    viewport.style.top = '40%';
    viewport.style.width = '0';
    viewport.style.height = '0';
    viewport.style.transformOrigin = 'center center';
    canvasContainer.appendChild(viewport);

    // Setup large SVG overlay for lines
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

    // Compute layout positions
    const pos = _computeLayout(graph.nodes, graph.edges);

    const cardElements = [];
    let activeHighlightNode = null; // clicked node for highlighting column lineage
    let currentFilterNode = null;   // Ctrl-clicked node for filtering whole graph
    let filteredNodes = null;       // Set of visible node IDs under active filter

    // 5. Setup filter notification banner at top of canvas
    const filterBanner = document.createElement('div');
    filterBanner.style.position = 'absolute';
    filterBanner.style.top = '12px';
    filterBanner.style.left = '50%';
    filterBanner.style.transform = 'translateX(-50%)';
    filterBanner.style.background = '#1e1b4b';
    filterBanner.style.border = '1px solid #4f46e5';
    filterBanner.style.borderRadius = '20px';
    filterBanner.style.padding = '6px 16px';
    filterBanner.style.color = '#c7d2fe';
    filterBanner.style.fontSize = '12px';
    filterBanner.style.display = 'none';
    filterBanner.style.alignItems = 'center';
    filterBanner.style.gap = '10px';
    filterBanner.style.zIndex = '50';
    filterBanner.style.boxShadow = '0 4px 10px rgba(0, 0, 0, 0.4)';
    filterBanner.innerHTML = `
      <span style="font-weight: 500;">Filter active</span>
      <button style="background: #4f46e5; border: none; color: #fff; border-radius: 12px; padding: 2px 10px; cursor: pointer; font-size: 11px; font-weight: bold;">Clear</button>
    `;
    canvasContainer.appendChild(filterBanner);

    filterBanner.querySelector('button').addEventListener('click', clearFilter);

    function clearFilter() {
      currentFilterNode = null;
      filteredNodes = null;
      filterBanner.style.display = 'none';
      
      graph.nodes.forEach(n => {
        const card = document.getElementById(`node__${n.id}`);
        if (card) card.style.display = 'block';
      });
      updateConnections();
    }

    function applyFilter(nodeId) {
      currentFilterNode = nodeId;
      filteredNodes = _lineageReach(nodeId, graph.edges, graph.nodes);
      
      filterBanner.style.display = 'flex';
      const label = graph.nodes.find(x => x.id === nodeId)?.label || nodeId;
      filterBanner.querySelector('span').textContent = `Showing lineage for: ${label}`;
      
      graph.nodes.forEach(n => {
        const card = document.getElementById(`node__${n.id}`);
        if (card) {
          card.style.display = filteredNodes.has(n.id) ? 'block' : 'none';
        }
      });
      updateConnections();
    }

    // 6. Sidebar Properties Panel Render Function
    function showNodeDetails(node) {
      panel.style.display = 'block';
      panel.innerHTML = '';

      // Header
      const head = document.createElement('div');
      head.className = 'etlsql-dag-panel-head';
      
      const dot = document.createElement('span');
      dot.className = 'etlsql-dag-panel-dot';
      dot.style.background = _nodeColor(node.type);
      head.appendChild(dot);

      const title = document.createElement('strong');
      title.className = 'etlsql-dag-panel-title';
      title.textContent = node.label;
      head.appendChild(title);

      const close = document.createElement('button');
      close.className = 'etlsql-dag-panel-x';
      close.textContent = '✕';
      close.title = 'Close';
      close.addEventListener('click', () => {
        panel.style.display = 'none';
      });
      head.appendChild(close);
      panel.appendChild(head);

      // Subtitle
      const subtitle = document.createElement('div');
      subtitle.className = 'etlsql-dag-panel-sub';
      subtitle.textContent = `Type: ${node.type.toUpperCase()}`;
      panel.appendChild(subtitle);

      // Divider
      const hr = document.createElement('div');
      hr.style.height = '1px';
      hr.style.background = '#1e293b';
      hr.style.margin = '10px 0';
      panel.appendChild(hr);

      // Upstream/Downstream relations (calculated from edges)
      const upstream = [];
      const downstream = [];
      graph.edges.forEach(e => {
        if (e.target === node.id) {
          const srcNode = graph.nodes.find(x => x.id === e.source);
          if (srcNode) upstream.push(srcNode.label);
        }
        if (e.source === node.id) {
          const tgtNode = graph.nodes.find(x => x.id === e.target);
          if (tgtNode) downstream.push(tgtNode.label);
        }
      });

      if (upstream.length > 0) {
        const uHeader = document.createElement('div');
        uHeader.className = 'etlsql-dag-panel-h';
        uHeader.textContent = 'Upstream Sources';
        panel.appendChild(uHeader);

        const ul = document.createElement('ul');
        ul.className = 'etlsql-dag-panel-list';
        upstream.forEach(name => {
          const li = document.createElement('li');
          li.className = 'etlsql-dag-panel-li';
          li.textContent = name;
          ul.appendChild(li);
        });
        panel.appendChild(ul);
      }

      if (downstream.length > 0) {
        const dHeader = document.createElement('div');
        dHeader.className = 'etlsql-dag-panel-h';
        dHeader.textContent = 'Downstream Dependencies';
        panel.appendChild(dHeader);

        const ul = document.createElement('ul');
        ul.className = 'etlsql-dag-panel-list';
        downstream.forEach(name => {
          const li = document.createElement('li');
          li.className = 'etlsql-dag-panel-li';
          li.textContent = name;
          ul.appendChild(li);
        });
        panel.appendChild(ul);
      }

      // Columns metadata section
      if (node.type === 'table' || node.type === 'dataset') {
        const cHeader = document.createElement('div');
        cHeader.className = 'etlsql-dag-panel-h';
        cHeader.textContent = `Columns (${node.meta?.columns?.length ?? 0})`;
        panel.appendChild(cHeader);

        const list = document.createElement('ul');
        list.className = 'etlsql-dag-panel-list';
        if (node.meta?.columns) {
          node.meta.columns.forEach(c => {
            const li = document.createElement('li');
            li.className = 'etlsql-dag-panel-li';
            
            const nameSpan = document.createElement('span');
            nameSpan.className = 'etlsql-dag-panel-v';
            nameSpan.textContent = c;
            li.appendChild(nameSpan);

            // Fetch column lineage mapping if present
            if (node.meta.columnLineage && node.meta.columnLineage[c]) {
              const lin = node.meta.columnLineage[c];
              if (lin.sources && lin.sources[0]) {
                const fromSpan = document.createElement('span');
                fromSpan.className = 'etlsql-dag-panel-from';
                fromSpan.style.color = '#10b981';
                fromSpan.textContent = ` ← ${lin.sources[0].table}.${lin.sources[0].column}`;
                li.appendChild(fromSpan);
              }
            }
            list.appendChild(li);
          });
        } else {
          const empty = document.createElement('div');
          empty.className = 'etlsql-dag-panel-empty';
          empty.textContent = 'No columns defined.';
          panel.appendChild(empty);
        }
        panel.appendChild(list);
      } else if (node.type === 'visual') {
        const mHeader = document.createElement('div');
        mHeader.className = 'etlsql-dag-panel-h';
        mHeader.textContent = 'Field Mappings';
        panel.appendChild(mHeader);

        const list = document.createElement('ul');
        list.className = 'etlsql-dag-panel-list';
        if (node.meta?.mappings) {
          node.meta.mappings.forEach(m => {
            const li = document.createElement('li');
            li.className = 'etlsql-dag-panel-li';

            const roleSpan = document.createElement('span');
            roleSpan.className = 'etlsql-dag-panel-k';
            roleSpan.textContent = `${m.role}: `;
            li.appendChild(roleSpan);

            const colSpan = document.createElement('span');
            colSpan.className = 'etlsql-dag-panel-v';
            colSpan.textContent = m.column;
            li.appendChild(colSpan);

            list.appendChild(li);
          });
        } else {
          const empty = document.createElement('div');
          empty.className = 'etlsql-dag-panel-empty';
          empty.textContent = 'No field mappings.';
          panel.appendChild(empty);
        }
        panel.appendChild(list);

        const vInfo = document.createElement('div');
        vInfo.style.marginTop = '15px';
        vInfo.style.fontSize = '11px';
        vInfo.style.color = '#64748b';
        vInfo.innerHTML = `
          <div><strong>Chart Visual Type:</strong> ${node.meta?.visualType ?? 'Unknown'}</div>
          <div><strong>Report Page:</strong> ${node.meta?.page ?? 'None'}</div>
        `;
        panel.appendChild(vInfo);
      }
    }

    // 7. Render Nodes onto infinite viewport
    graph.nodes.forEach(n => {
      const card = document.createElement('div');
      card.id = `node__${n.id}`;
      card.style.position = 'absolute';
      card.style.left = `${pos[n.id].x - 120}px`;
      card.style.top = `${pos[n.id].y}px`;
      card.style.background = '#111827';
      card.style.borderRadius = '8px';
      card.style.boxShadow = '0 10px 15px -3px rgba(0, 0, 0, 0.4)';
      card.style.color = '#cbd5e1';
      card.style.zIndex = '10';
      card.style.cursor = 'grab';

      let titleColor = '#cbd5e1';
      let borderStyle = '1px solid #1f2937';
      let showColumns = false;

      if (n.type === 'table') {
        titleColor = '#3b82f6';
        borderStyle = '1px solid #2563eb';
        showColumns = true;
        card.style.width = '240px';
      } else if (n.type === 'dataset') {
        titleColor = '#8b5cf6';
        borderStyle = '1px solid #7c3aed';
        showColumns = true;
        card.style.width = '240px';
      } else if (n.type === 'page') {
        titleColor = '#e2e8f0';
        borderStyle = '1px solid #475569';
        card.style.width = '180px';
      } else if (n.type === 'visual') {
        titleColor = '#10b981';
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

      // Card-level connection ports
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
          pos[n.id].x = origX + 120 + dx;
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

      // Interactive Click Logic (Ctrl+Click to filter, regular Click to highlight + show sidebar)
      card.addEventListener('click', e => {
        if (e.target !== header && e.target !== titleSpan && e.target !== typeDot) return;
        
        if (e.ctrlKey) {
          // Isolate Lineage Filter Mode
          if (currentFilterNode === n.id) {
            clearFilter();
          } else {
            applyFilter(n.id);
          }
        } else {
          // Open details sidebar
          showNodeDetails(n);

          // Highlight column connections
          if (activeHighlightNode === n.id) {
            activeHighlightNode = null;
          } else {
            activeHighlightNode = n.id;
          }
          updateConnections();
        }
      });
    });

    // 8. Parse Column-to-Column Lineage
    const colConnections = [];
    graph.nodes.forEach(n => {
      if (n.meta?.columnLineage) {
        Object.entries(n.meta.columnLineage).forEach(([tgtCol, lin]) => {
          if (lin.sources) {
            lin.sources.forEach(src => {
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

    // 9. Draw/Update Connections
    function updateConnections() {
      activePaths.forEach(p => p.remove());
      activePaths = [];

      const vRect = viewport.getBoundingClientRect();

      // Render primary table-level connections
      graph.edges.forEach(e => {
        // If filters are active, skip edges connected to hidden nodes
        if (filteredNodes && (!filteredNodes.has(e.source) || !filteredNodes.has(e.target))) return;

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

        const isDimmed = activeHighlightNode && (activeHighlightNode !== e.source && activeHighlightNode !== e.target);
        const color = isDimmed ? 'rgba(71,85,105,0.1)' : '#475569';
        const width = isDimmed ? 0.75 : 1.5;

        const path = drawLink(x1, y1, x2, y2, color, width);
        activePaths.push(path);
      });

      // Render column-to-column connections
      colConnections.forEach(c => {
        // If filters are active, skip columns belonging to hidden tables
        if (filteredNodes && (!filteredNodes.has(c.fromTable) || !filteredNodes.has(c.toTable))) return;

        const isDirectConnection = activeHighlightNode === c.fromTable || activeHighlightNode === c.toTable;
        if (activeHighlightNode && !isDirectConnection) return;

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

        const color = activeHighlightNode ? '#10b981' : 'rgba(16,185,129,0.35)';
        const width = activeHighlightNode ? 2.5 : 1;

        const path = drawLink(x1, y1, x2, y2, color, width, !activeHighlightNode);
        activePaths.push(path);
      });
    }

    // 10. Pan and Zoom Interaction
    let panX = 0;
    let panY = 0;
    let zoom = fixtureId === 'kitchen' ? 0.22 : 0.65;

    viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;

    canvasContainer.addEventListener('wheel', e => {
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

    canvasContainer.addEventListener('mousedown', e => {
      if (e.target !== canvasContainer && e.target !== svg) return;
      isPanning = true;
      startX = e.clientX - panX;
      startY = e.clientY - panY;
      canvasContainer.style.cursor = 'grabbing';
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
        canvasContainer.style.cursor = 'default';
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
