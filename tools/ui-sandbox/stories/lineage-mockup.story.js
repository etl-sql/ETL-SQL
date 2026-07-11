// Story: Column-to-column interactive lineage mockup using Vanilla JS
export default {
  id: 'lineage-mockup',
  title: 'React Flow / Port Lineage Mockup',
  subtitle: 'Interactive column-level flow',
  fixtures: [
    { id: 'standard', label: 'Customer-Order-Sales flow' }
  ],
  async mount(stage, fixtureId, ctx) {
    stage.innerHTML = '';
    ctx.stat('Drag table headers to route columns interactively.');

    // 1. Container styling
    stage.style.position = 'relative';
    stage.style.width = '100%';
    stage.style.height = '100%';
    stage.style.background = '#0b0f19';
    stage.style.overflow = 'hidden';

    // 2. Add SVG overlay
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.style.position = 'absolute';
    svg.style.top = '0';
    svg.style.left = '0';
    svg.style.width = '100%';
    svg.style.height = '100%';
    svg.style.pointerEvents = 'none';
    svg.style.zIndex = '1';
    stage.appendChild(svg);

    // 3. Helper to draw Bezier Curve
    function drawLink(x1, y1, x2, y2, color = '#6366f1') {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      const dx = Math.abs(x2 - x1) * 0.45;
      path.setAttribute('d', `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`);
      path.setAttribute('stroke', color);
      path.setAttribute('stroke-width', '2');
      path.setAttribute('fill', 'none');
      path.setAttribute('opacity', '0.85');
      svg.appendChild(path);
      return path;
    }

    // 4. Create Tables
    const tables = [
      {
        id: 'cust',
        title: 'orders_db.Customers',
        color: '#8b5cf6', // purple
        x: 50, y: 100,
        columns: [
          { name: 'id', pk: true },
          { name: 'name' },
          { name: 'email', tag: 'PII' },
          { name: 'created_at' }
        ]
      },
      {
        id: 'orders',
        title: 'orders_db.Orders',
        color: '#3b82f6', // blue
        x: 350, y: 150,
        columns: [
          { name: 'id', pk: true },
          { name: 'customer_id', fk: true },
          { name: 'total' },
          { name: 'order_date' }
        ]
      },
      {
        id: 'sales',
        title: 'warehouse.SalesSummary',
        color: '#10b981', // green
        x: 680, y: 120,
        columns: [
          { name: 'id', pk: true },
          { name: 'customer_name' },
          { name: 'order_total' },
          { name: 'sale_date' }
        ]
      }
    ];

    const cardElements = [];

    // Create table elements
    tables.forEach(t => {
      const card = document.createElement('div');
      card.style.position = 'absolute';
      card.style.left = `${t.x}px`;
      card.style.top = `${t.y}px`;
      card.style.width = '240px';
      card.style.background = '#111827';
      card.style.border = `1px solid ${t.color}`;
      card.style.borderRadius = '8px';
      card.style.boxShadow = '0 10px 15px -3px rgba(0, 0, 0, 0.5)';
      card.style.color = '#cbd5e1';
      card.style.zIndex = '10';
      card.style.userSelect = 'none';
      card.style.cursor = 'grab';

      // Card Header
      const header = document.createElement('div');
      header.style.padding = '8px 12px';
      header.style.background = '#1f2937';
      header.style.borderBottom = '1px solid #374151';
      header.style.borderTopLeftRadius = '7px';
      header.style.borderTopRightRadius = '7px';
      header.style.fontWeight = 'bold';
      header.style.fontSize = '13px';
      header.style.color = '#fff';
      header.style.display = 'flex';
      header.style.alignItems = 'center';
      header.style.justifyContent = 'space-between';

      const title = document.createElement('span');
      title.textContent = t.title;
      header.appendChild(title);

      const dot = document.createElement('span');
      dot.style.width = '8px';
      dot.style.height = '8px';
      dot.style.borderRadius = '50%';
      dot.style.background = t.color;
      header.appendChild(dot);

      card.appendChild(header);

      // Card Columns
      t.columns.forEach(col => {
        const row = document.createElement('div');
        row.id = `${t.id}__col__${col.name}`;
        row.style.padding = '6px 12px';
        row.style.fontSize = '12px';
        row.style.display = 'flex';
        row.style.alignItems = 'center';
        row.style.justifyContent = 'space-between';
        row.style.position = 'relative';
        row.style.borderBottom = '1px solid #1f2937';

        // Column Label
        const labelSpan = document.createElement('span');
        labelSpan.textContent = col.name;
        if (col.pk) {
          labelSpan.style.fontWeight = 'bold';
          labelSpan.style.color = '#f59e0b';
        }
        row.appendChild(labelSpan);

        // Tags
        if (col.tag) {
          const tagSpan = document.createElement('span');
          tagSpan.textContent = col.tag;
          tagSpan.style.fontSize = '9px';
          tagSpan.style.background = '#ef4444';
          tagSpan.style.color = '#fff';
          tagSpan.style.padding = '1px 4px';
          tagSpan.style.borderRadius = '3px';
          tagSpan.style.marginLeft = 'auto';
          tagSpan.style.marginRight = '8px';
          row.appendChild(tagSpan);
        } else if (col.fk) {
          const tagSpan = document.createElement('span');
          tagSpan.textContent = 'FK';
          tagSpan.style.fontSize = '9px';
          tagSpan.style.background = '#3b82f6';
          tagSpan.style.color = '#fff';
          tagSpan.style.padding = '1px 4px';
          tagSpan.style.borderRadius = '3px';
          tagSpan.style.marginLeft = 'auto';
          tagSpan.style.marginRight = '8px';
          row.appendChild(tagSpan);
        }

        // Connector dots (ports)
        const leftPort = document.createElement('div');
        leftPort.className = 'port-left';
        leftPort.style.position = 'absolute';
        leftPort.style.left = '-5px';
        leftPort.style.top = '50%';
        leftPort.style.transform = 'translateY(-50%)';
        leftPort.style.width = '10px';
        leftPort.style.height = '10px';
        leftPort.style.borderRadius = '50%';
        leftPort.style.background = '#475569';
        leftPort.style.border = '2px solid #1e293b';
        row.appendChild(leftPort);

        const rightPort = document.createElement('div');
        rightPort.className = 'port-right';
        rightPort.style.position = 'absolute';
        rightPort.style.right = '-5px';
        rightPort.style.top = '50%';
        rightPort.style.transform = 'translateY(-50%)';
        rightPort.style.width = '10px';
        rightPort.style.height = '10px';
        rightPort.style.borderRadius = '50%';
        rightPort.style.background = '#475569';
        rightPort.style.border = '2px solid #1e293b';
        row.appendChild(rightPort);

        card.appendChild(row);
      });

      stage.appendChild(card);
      cardElements.push({ t, card });

      // Dragging Logic
      card.addEventListener('mousedown', e => {
        if (e.target !== header && e.target !== title && e.target !== dot) return;
        e.preventDefault();
        card.style.cursor = 'grabbing';
        
        const startX = e.clientX;
        const startY = e.clientY;
        const origX = card.offsetLeft;
        const origY = card.offsetTop;

        function onMouseMove(moveEvent) {
          const dx = moveEvent.clientX - startX;
          const dy = moveEvent.clientY - startY;
          t.x = origX + dx;
          t.y = origY + dy;
          card.style.left = `${t.x}px`;
          card.style.top = `${t.y}px`;
          updateConnections();
        }

        function onMouseUp() {
          card.style.cursor = 'grab';
          document.removeEventListener('mousemove', onMouseMove);
          document.removeEventListener('mouseup', onMouseUp);
        }

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
      });
    });

    // 5. Connections definitions
    const connections = [
      { from: 'cust__col__id', to: 'orders__col__customer_id', color: '#a78bfa' },
      { from: 'cust__col__name', to: 'sales__col__customer_name', color: '#a78bfa' },
      { from: 'orders__col__total', to: 'sales__col__order_total', color: '#60a5fa' }
    ];

    let pathElements = [];

    // 6. Draw / Update Connections Function
    function updateConnections() {
      // Clear old SVG path elements
      pathElements.forEach(p => p.remove());
      pathElements = [];

      const stageRect = stage.getBoundingClientRect();

      connections.forEach(conn => {
        const fromEl = document.getElementById(conn.from);
        const toEl = document.getElementById(conn.to);

        if (!fromEl || !toEl) return;

        const fromPort = fromEl.querySelector('.port-right');
        const toPort = toEl.querySelector('.port-left');

        if (!fromPort || !toPort) return;

        const r1 = fromPort.getBoundingClientRect();
        const r2 = toPort.getBoundingClientRect();

        const x1 = r1.left + r1.width / 2 - stageRect.left;
        const y1 = r1.top + r1.height / 2 - stageRect.top;
        const x2 = r2.left + r2.width / 2 - stageRect.left;
        const y2 = r2.top + r2.height / 2 - stageRect.top;

        const path = drawLink(x1, y1, x2, y2, conn.color);
        pathElements.push(path);
      });
    }

    // Initial draw after a brief delay for client rects to compute
    setTimeout(updateConnections, 50);

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
