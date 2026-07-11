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

// Maps a node to its mock connection database dialect/system type
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
  // Staging / facts / rollups are in-memory temp tables coordinated by the engine
  if (label.startsWith('Stg') || label.startsWith('Enriched') || label.includes('Rollup') || label.includes('Plan') || label.includes('Log') || label.startsWith('Fact')) {
    return 'Engine';
  }
  return 'Database';
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

  const LAYER_H    = 300; // Increased spacing slightly to make room for inline formulas
  const SUB_ROW_H  = 180;
  const NODE_W     = 360;
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
    
    // 1. Setup stage as relative container
    stage.style.position = 'relative';
    stage.style.width = '100%';
    stage.style.height = '100%';
    stage.style.background = '#090d16';
    stage.style.overflow = 'hidden';
    stage.style.userSelect = 'none';

    // Canvas container (takes 100% space)
    const canvasContainer = document.createElement('div');
    canvasContainer.style.width = '100%';
    canvasContainer.style.height = '100%';
    canvasContainer.style.position = 'relative';
    canvasContainer.style.overflow = 'hidden';
    stage.appendChild(canvasContainer);

    // Sidebar detail panel (floats on the right via absolute positioning)
    const panel = document.createElement('div');
    panel.className = 'etlsql-dag-panel';
    panel.style.position = 'absolute';
    panel.style.right = '0';
    panel.style.top = '0';
    panel.style.width = '340px';
    panel.style.height = '100%';
    panel.style.background = '#0f172a';
    panel.style.borderLeft = '1px solid #1e293b';
    panel.style.zIndex = '100'; // sits on top of canvas/nodes
    panel.style.overflowY = 'auto';
    panel.style.padding = '12px 14px 16px';
    panel.style.boxShadow = '-5px 0 25px rgba(0, 0, 0, 0.5)';
    panel.style.display = 'none';
    
    // Overriding light theme variable defaults specifically inside the panel to ensure dark mode visibility
    panel.style.setProperty('--portal-text', '#e2e8f0');
    panel.style.setProperty('--portal-text-soft', '#94a3b8');
    panel.style.setProperty('--portal-text-muted', '#64748b');
    panel.style.setProperty('--portal-muted', '#94a3b8');
    panel.style.setProperty('--portal-accent', '#60a5fa');
    panel.style.color = '#e2e8f0';

    stage.appendChild(panel);

    const graph = (BUILDERS[fixtureId] ?? buildKitchenSinkGraph)();

    // Inject high-fidelity metadata (tags/descriptions/transformations) dynamically for demonstration
    graph.nodes.forEach(n => {
      if (n.meta?.columns) {
        n.meta.columns.forEach(c => {
          n.meta.columnLineage ??= {};
          n.meta.columnLineage[c] ??= { sources: [] };
          if (c === 'Revenue' || c === 'Discount' || c === 'email') {
            n.meta.columnLineage[c].tags = { pii: 'true', classification: 'confidential', owner: 'finance' };
            n.meta.columnLineage[c].description = `Financial metrics for column: ${c}. Raw transaction amount.`;
          } else if (c === 'Region' || c === 'Category') {
            n.meta.columnLineage[c].description = `Grouping axis: ${c}. Derived from dimensions hub.`;
          }
        });
      }
    });

    const _nodeById = Object.fromEntries(graph.nodes.map(n => [n.id, n]));

    ctx.stat(`Mockup: ${graph.nodes.length} nodes · Drag headers to move · Click to inspect · Click column row to isolate path`);

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

    // Setup HTML overlay container for edge badges/pills
    const badgeContainer = document.createElement('div');
    badgeContainer.style.position = 'absolute';
    badgeContainer.style.top = '0';
    badgeContainer.style.left = '0';
    badgeContainer.style.width = '100%';
    badgeContainer.style.height = '100%';
    badgeContainer.style.pointerEvents = 'none';
    badgeContainer.style.zIndex = '5';
    viewport.appendChild(badgeContainer);

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

    // Helper to draw Inline Edge transformation badges/pills
    function drawEdgeBadge(x1, y1, x2, y2, text, inPath, isDimmed) {
      if (!text) return;
      const mx = (x1 + x2) / 2;
      const my = (y1 + y2) / 2;

      const badge = document.createElement('div');
      badge.style.position = 'absolute';
      badge.style.left = `${mx}px`;
      badge.style.top = `${my}px`;
      badge.style.transform = 'translate(-50%, -50%)';
      
      badge.style.background = inPath ? '#0f766e' : (isDimmed ? 'rgba(17,24,39,0.05)' : '#1e293b');
      badge.style.border = inPath ? '1px solid #14b8a6' : (isDimmed ? '1px solid rgba(55,65,81,0.05)' : '1px solid #3b82f6');
      badge.style.borderRadius = '10px';
      badge.style.padding = '1px 6px';
      badge.style.color = inPath ? '#ccfbf1' : (isDimmed ? 'rgba(156,163,175,0.05)' : '#93c5fd');
      badge.style.fontSize = '8px';
      badge.style.fontWeight = 'bold';
      badge.style.pointerEvents = 'none';
      badge.style.whiteSpace = 'nowrap';
      badge.style.boxShadow = isDimmed ? 'none' : '0 2px 4px rgba(0,0,0,0.5)';
      badge.textContent = text;
      
      badgeContainer.appendChild(badge);
    }

    // Compute layout positions
    const pos = _computeLayout(graph.nodes, graph.edges);

    const cardElements = [];
    let activeHighlightNode = null; // clicked node for highlighting column lineage
    let currentFilterNode = null;   // Ctrl-clicked node for filtering whole graph
    let filteredNodes = null;       // Set of visible node IDs under active filter
    let activeColumnPath = null;    // colKey of currently isolated column path
    let activeColumnPathSet = null; // Set of elements in the isolated column path

    // Setup filter notification banner at top of canvas
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
      activeColumnPath = null;
      activeColumnPathSet = null;
      filterBanner.style.display = 'none';
      
      graph.nodes.forEach(n => {
        const card = document.getElementById(`node__${n.id}`);
        if (card) card.style.display = 'block';
      });
      updateUIForPathIsolation();
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

    // Resolve a source table name (as recorded in lineage) to a graph node id.
    function findTableNodeId(tableName) {
      if (_nodeById[`ds:${tableName}`])    return `ds:${tableName}`;
      if (_nodeById[`table:${tableName}`]) return `table:${tableName}`;
      const hit = graph.nodes.find(n => (n.type === 'table' || n.type === 'dataset') && n.label === tableName);
      return hit ? hit.id : null;
    }

    // Trace column paths recursively (both upstream and downstream)
    function _traceColumnPath(nodeId, colName, pathSet, direction = 'both') {
      const colKey = `${nodeId}__col__${colName}`;
      if (pathSet.has(colKey)) return;
      pathSet.add(colKey);
      pathSet.add(nodeId);

      const node = _nodeById[nodeId];
      if (!node) return;

      // 1. Upstream (Ancestors)
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

      // 2. Downstream (Descendants)
      if (direction === 'both' || direction === 'down') {
        // Look at other tables/datasets
        graph.nodes.forEach(other => {
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

        // Look at visual mapping rows
        graph.nodes.forEach(other => {
          if (other.type === 'visual' && other.meta?.mappings) {
            const hasEdge = graph.edges.some(e => e.source === nodeId && e.target === other.id);
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

    // Toggle card/column opacity states during path isolation
    function updateUIForPathIsolation() {
      if (activeColumnPath) {
        const parts = activeColumnPath.split('__');
        const nodeId = parts[0];
        const colName = parts[parts.length - 1];
        const label = _nodeById[nodeId]?.label || nodeId;
        
        filterBanner.style.display = 'flex';
        filterBanner.querySelector('span').textContent = `Isolated Path: ${label} ➔ ${colName}`;
      } else {
        if (!currentFilterNode) {
          filterBanner.style.display = 'none';
        }
      }

      graph.nodes.forEach(n => {
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
                  if (labelSpan) {
                    labelSpan.style.color = inPath ? '#34d399' : '#4b5563';
                  }
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
                  if (labelSpan) {
                    labelSpan.style.color = inPath ? '#34d399' : '#4b5563';
                  }
                }
              });
            }
          } else {
            card.style.opacity = '0.08';
            card.style.borderColor = '#1f2937';
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
                if (labelSpan) {
                  labelSpan.style.color = '#cbd5e1';
                }
              }
            });
          }
          if (n.meta?.mappings) {
            n.meta.mappings.forEach(m => {
              const row = document.getElementById(`${n.id}__map__${m.role}`);
              if (row) {
                row.style.opacity = '1';
                const labelSpan = row.querySelector('.col-label-span');
                if (labelSpan) {
                  labelSpan.style.color = '#cbd5e1';
                }
              }
            });
          }
        }
      });
    }

    // Walk a column back through its sources recursively (production matching)
    function appendColumnLineage(container, tableNodeId, column, depth, seen) {
      seen = seen || new Set();
      const key = `${tableNodeId}|${column}`;
      if (seen.has(key) || depth > 12) return;
      seen.add(key);

      const tnode = _nodeById[tableNodeId];
      const cl = tnode?.meta?.columnLineage?.[column];

      const row = document.createElement('div');
      row.className = 'etlsql-dag-lin';
      row.style.paddingLeft = `${depth * 14}px`;
      row.style.margin = '4px 0';
      row.style.display = 'flex';
      row.style.alignItems = 'center';
      row.style.flexWrap = 'wrap';
      row.style.gap = '4px';

      if (depth > 0) {
        const a = document.createElement('span');
        a.className = 'etlsql-dag-lin-arrow';
        a.textContent = '↖';
        row.appendChild(a);
      }

      const colEl = document.createElement('span');
      colEl.className = 'etlsql-dag-lin-col';
      colEl.textContent = column;
      row.appendChild(colEl);

      if (cl?.transform) {
        const t = document.createElement('span');
        t.className = 'etlsql-dag-lin-expr';
        t.textContent = `= ${cl.transform}`;
        row.appendChild(t);
      }

      const tbl = document.createElement('span');
      tbl.className = 'etlsql-dag-lin-tbl';
      tbl.textContent = tnode?.label ?? tableNodeId;
      row.appendChild(tbl);
      container.appendChild(row);

      // Render Tags
      if (cl?.tags && Object.keys(cl.tags).length) {
        const tagRow = document.createElement('div');
        tagRow.className = 'etlsql-dag-lin-meta';
        tagRow.style.paddingLeft = `${depth * 14 + 16}px`;
        tagRow.style.display = 'flex';
        tagRow.style.gap = '4px';
        tagRow.style.margin = '2px 0';
        for (const k of Object.keys(cl.tags)) {
          const tg = document.createElement('span');
          tg.className = 'etlsql-dag-lin-tag';
          tg.textContent = `⚠ ${k}`;
          tagRow.appendChild(tg);
        }
        container.appendChild(tagRow);
      }

      // Render Description
      if (cl?.description) {
        const d = document.createElement('div');
        d.className = 'etlsql-dag-lin-desc';
        d.style.paddingLeft = `${depth * 14 + 16}px`;
        d.textContent = cl.description;
        container.appendChild(d);
      }

      // Walk recursively
      for (const s of (cl?.sources ?? [])) {
        if (!s.column) continue;
        const srcId = findTableNodeId(s.table);
        if (srcId) {
          appendColumnLineage(container, srcId, s.column, depth + 1, seen);
        } else {
          const leaf = document.createElement('div');
          leaf.className = 'etlsql-dag-lin';
          leaf.style.paddingLeft = `${(depth + 1) * 14}px`;
          leaf.style.margin = '4px 0';
          leaf.style.display = 'flex';
          leaf.style.alignItems = 'center';
          leaf.style.gap = '4px';

          const a = document.createElement('span');
          a.className = 'etlsql-dag-lin-arrow';
          a.textContent = '↖';
          leaf.appendChild(a);

          const c = document.createElement('span');
          c.className = 'etlsql-dag-lin-col';
          c.textContent = s.column;
          leaf.appendChild(c);

          const tb = document.createElement('span');
          tb.className = 'etlsql-dag-lin-tbl';
          tb.textContent = s.table;
          leaf.appendChild(tb);

          container.appendChild(leaf);
        }
      }
    }

    // Sidebar Properties Panel Render Function
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

      // Columns metadata section (matching production lineage walk)
      if (node.type === 'table' || node.type === 'dataset') {
        const cHeader = document.createElement('div');
        cHeader.className = 'etlsql-dag-panel-h';
        cHeader.textContent = `Columns (${node.meta?.columns?.length ?? 0})`;
        panel.appendChild(cHeader);

        if (node.meta?.columns?.length) {
          node.meta.columns.forEach(c => {
            appendColumnLineage(panel, node.id, c, 0);
          });
        } else {
          const empty = document.createElement('div');
          empty.className = 'etlsql-dag-panel-empty';
          empty.textContent = 'No columns defined.';
          panel.appendChild(empty);
        }
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

            // Also draw column lineage for this mapped field
            const srcId = findTableNodeId(graph.nodes.find(x => x.id === activeHighlightNode || x.label === node.meta.page)?.id || '');
            if (srcId) {
              appendColumnLineage(panel, srcId, m.column, 1);
            }
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

    // Render Nodes onto infinite viewport
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

      // Connection dialect badge in header (proves cross-source movement)
      const dialect = getConnectionDialect(n);
      if (dialect) {
        const dbBadge = document.createElement('span');
        dbBadge.textContent = dialect;
        dbBadge.style.fontSize = '8px';
        dbBadge.style.fontWeight = 'bold';
        dbBadge.style.padding = '1px 5px';
        dbBadge.style.borderRadius = '3px';
        dbBadge.style.marginLeft = 'auto';
        dbBadge.style.marginRight = '8px';
        
        if (dialect === 'MSSQL') {
          dbBadge.style.background = '#1e3a8a';
          dbBadge.style.color = '#93c5fd';
        } else if (dialect === 'Postgres') {
          dbBadge.style.background = '#065f46';
          dbBadge.style.color = '#a7f3d0';
        } else if (dialect === 'Snowflake') {
          dbBadge.style.background = '#0369a1';
          dbBadge.style.color = '#bae6fd';
        } else if (dialect === 'CSV') {
          dbBadge.style.background = '#78350f';
          dbBadge.style.color = '#fef3c7';
        } else if (dialect === 'Oracle') {
          dbBadge.style.background = '#991b1b';
          dbBadge.style.color = '#fee2e2';
        } else if (dialect === 'SFTP') {
          dbBadge.style.background = '#3730a3';
          dbBadge.style.color = '#e0e7ff';
        } else if (dialect === 'Engine') {
          dbBadge.style.background = '#374151';
          dbBadge.style.color = '#cbd5e1';
          dbBadge.style.border = '1px solid #4b5563';
        } else if (dialect === 'Dataset') {
          dbBadge.style.background = '#581c87';
          dbBadge.style.color = '#f3e8ff';
        }
        header.insertBefore(dbBadge, typeDot);
      }

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
          row.style.cursor = 'pointer';

          // Layout grid for column name and formula
          const labelGrid = document.createElement('div');
          labelGrid.style.display = 'flex';
          labelGrid.style.flexDirection = 'column';
          labelGrid.style.gap = '1px';

          const colNameSpan = document.createElement('span');
          colNameSpan.className = 'col-label-span';
          colNameSpan.textContent = c;
          colNameSpan.style.color = '#cbd5e1';
          labelGrid.appendChild(colNameSpan);

          // Inline formula detail rendering
          const cl = n.meta?.columnLineage?.[c];
          if (cl?.transform) {
            const formSpan = document.createElement('span');
            formSpan.style.fontSize = '8px';
            formSpan.style.color = '#64748b';
            formSpan.style.fontStyle = 'italic';
            const cleanForm = cl.transform.startsWith('=') ? cl.transform : `= ${cl.transform}`;
            formSpan.textContent = cleanForm;
            labelGrid.appendChild(formSpan);
          }

          row.appendChild(labelGrid);

          // Render tags inline inside column row for comparison
          if (cl?.tags && Object.keys(cl.tags).length) {
            const inlineTag = document.createElement('span');
            inlineTag.textContent = Object.keys(cl.tags)[0].toUpperCase();
            inlineTag.style.fontSize = '7px';
            inlineTag.style.background = '#ef4444';
            inlineTag.style.color = '#fff';
            inlineTag.style.padding = '1px 3px';
            inlineTag.style.borderRadius = '2px';
            inlineTag.style.fontWeight = 'bold';
            inlineTag.style.marginLeft = 'auto';
            inlineTag.style.marginRight = '8px';
            row.appendChild(inlineTag);
          }

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

          // Click a column row to isolate its path
          row.addEventListener('click', e => {
            e.stopPropagation();
            const colKey = `${n.id}__col__${c}`;
            if (activeColumnPath === colKey) {
              activeColumnPath = null;
              activeColumnPathSet = null;
            } else {
              activeColumnPath = colKey;
              activeColumnPathSet = new Set();
              _traceColumnPath(n.id, c, activeColumnPathSet);
            }
            updateUIForPathIsolation();
            updateConnections();
          });
        });
      } else if (n.type === 'visual' && n.meta?.mappings) {
        // Visual Mapping Ports: Display mapping columns as left-connected rows on visual card
        n.meta.mappings.forEach(m => {
          const row = document.createElement('div');
          row.id = `${n.id}__map__${m.role}`;
          row.style.padding = '5px 12px';
          row.style.fontSize = '11px';
          row.style.display = 'flex';
          row.style.alignItems = 'center';
          row.style.justifyContent = 'space-between';
          row.style.position = 'relative';
          row.style.borderBottom = '1px solid #1f2937';
          row.style.cursor = 'pointer';

          const labelGrid = document.createElement('div');
          labelGrid.style.display = 'flex';
          labelGrid.style.flexDirection = 'column';
          labelGrid.style.gap = '1px';

          const colNameSpan = document.createElement('span');
          colNameSpan.className = 'col-label-span';
          colNameSpan.innerHTML = `<strong>${m.role}:</strong> ${m.column}`;
          colNameSpan.style.color = '#cbd5e1';
          labelGrid.appendChild(colNameSpan);

          row.appendChild(labelGrid);

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

          card.appendChild(row);

          // Click a visual mapping row to isolate its path
          row.addEventListener('click', e => {
            e.stopPropagation();
            const mapKey = `${n.id}__map__${m.role}`;
            if (activeColumnPath === mapKey) {
              activeColumnPath = null;
              activeColumnPathSet = null;
            } else {
              activeColumnPath = mapKey;
              activeColumnPathSet = new Set();
              const cleanCol = m.column.replace(/.*\((.*)\)/, '$1');
              
              // Find matching edge to trace backwards
              const srcEdge = graph.edges.find(x => x.target === n.id && (x.label?.includes(cleanCol) || _nodeById[x.source]?.meta?.columns?.includes(cleanCol)));
              if (srcEdge) {
                activeColumnPathSet.add(mapKey);
                _traceColumnPath(srcEdge.source, cleanCol, activeColumnPathSet);
              }
            }
            updateUIForPathIsolation();
            updateConnections();
          });
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
          // Highlight column connections
          if (activeHighlightNode === n.id) {
            activeHighlightNode = null;
            panel.style.display = 'none';
          } else {
            activeHighlightNode = n.id;
            // Open details sidebar with metadata details
            showNodeDetails(n);
          }
          updateConnections();
        }
      });
    });

    // Parse Column-to-Column Lineage
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

    // Draw/Update Connections
    function updateConnections() {
      activePaths.forEach(p => p.remove());
      activePaths = [];
      badgeContainer.innerHTML = '';

      const vRect = viewport.getBoundingClientRect();

      // Render primary table-level connections
      graph.edges.forEach(e => {
        // If filters are active, skip edges connected to hidden nodes
        if (filteredNodes && (!filteredNodes.has(e.source) || !filteredNodes.has(e.target))) return;

        const fromNode = _nodeById[e.source];
        const toNode = _nodeById[e.target];

        if (!fromNode || !toNode) return;

        // Visual Mapping Ports: Route directly from dataset column port to visual role port
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
                  const x1 = (r1.left + r1.width / 2 - vRect.left) / zoom;
                  const y1 = (r1.top + r1.height / 2 - vRect.top) / zoom;
                  const x2 = (r2.left + r2.width / 2 - vRect.left) / zoom;
                  const y2 = (r2.top + r2.height / 2 - vRect.top) / zoom;

                  const inPath = activeColumnPathSet && activeColumnPathSet.has(`${e.source}__col__${cleanCol}`) && activeColumnPathSet.has(`${e.target}__map__${m.role}`);
                  const isDimmed = activeColumnPathSet && !inPath;
                  
                  const isHighlightNodeActive = activeHighlightNode && (activeHighlightNode === e.source || activeHighlightNode === e.target);
                  const isDimmedHighlight = activeHighlightNode && !isHighlightNodeActive;

                  const color = inPath ? '#06b6d4' : (isDimmed || isDimmedHighlight ? 'rgba(71,85,105,0.06)' : '#34d399');
                  const width = inPath ? 3.0 : (isDimmed || isDimmedHighlight ? 0.75 : 1.5);

                  const path = drawLink(x1, y1, x2, y2, color, width);
                  activePaths.push(path);
                  routedAny = true;

                  // Transformation edge badge midpoint
                  const edgeTransform = m.column.includes('(') ? m.column.split('(')[0] : 'SELECT';
                  drawEdgeBadge(x1, y1, x2, y2, edgeTransform, inPath, isDimmed || isDimmedHighlight);
                }
              }
            }
          });
          if (routedAny) return; // skip card-to-card fallbacks
        }

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

      // Render column-to-column connections
      colConnections.forEach(c => {
        if (filteredNodes && (!filteredNodes.has(c.fromTable) || !filteredNodes.has(c.toTable))) return;

        const isDirectConnection = activeHighlightNode === c.fromTable || activeHighlightNode === c.toTable;
        if (activeHighlightNode && !isDirectConnection) return;

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

        const x1 = (r1.left + r1.width / 2 - vRect.left) / zoom;
        const y1 = (r1.top + r1.height / 2 - vRect.top) / zoom;
        const x2 = (r2.left + r2.width / 2 - vRect.left) / zoom;
        const y2 = (r2.top + r2.height / 2 - vRect.top) / zoom;

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

    // Pan and Zoom Interaction (Zoom-to-cursor to prevent drift)
    let panX = 0;
    let panY = 0;
    let zoom = fixtureId === 'kitchen' ? 0.22 : 0.65;

    viewport.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;

    canvasContainer.addEventListener('wheel', e => {
      e.preventDefault();
      
      const rect = canvasContainer.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;

      const CX = rect.width / 2;
      const CY = rect.height * 0.4;

      const vx = (mouseX - CX - panX) / zoom;
      const vy = (mouseY - CY - panY) / zoom;

      const factor = 1.15;
      if (e.deltaY < 0) {
        zoom = Math.min(2, zoom * factor);
      } else {
        zoom = Math.max(0.08, zoom / factor);
      }

      panX = mouseX - CX - vx * zoom;
      panY = mouseY - CY - vy * zoom;

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
