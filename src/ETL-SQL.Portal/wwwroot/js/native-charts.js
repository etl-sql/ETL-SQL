(function (global) {
  'use strict';
  const instances = new WeakMap();
  const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]);

  function init(host) {
    const handlers = {};
    const api = {
      setOption(option) { render(host, option || {}); },
      resize() {},
      dispose() { host.replaceChildren(); instances.delete(host); },
      on(name, callback) { handlers[name] = callback; },
      dispatchAction() {}
    };
    host.addEventListener('click', event => handlers.click?.({ name: event.target?.dataset?.name, dataIndex: Number(event.target?.dataset?.index || 0), event: { event } }));
    instances.set(host, api);
    return api;
  }

  function render(host, option) {
    const width = Math.max(320, host.clientWidth || 600), height = Math.max(120, host.clientHeight || 240), pad = 24;
    const series = Array.isArray(option.series) ? option.series : [];
    const graph = series.find(item => item.type === 'graph');
    let marks = '';
    if (graph) {
      const nodes = graph.data || [], radius = Math.min(width, height) * .34, cx = width / 2, cy = height / 2;
      const positions = new Map(nodes.map((node, index) => [String(node.name), [cx + radius * Math.cos(index * 2 * Math.PI / Math.max(1, nodes.length)), cy + radius * Math.sin(index * 2 * Math.PI / Math.max(1, nodes.length))]]));
      marks += (graph.links || []).map(link => { const a = positions.get(String(link.source)), b = positions.get(String(link.target)); return a && b ? `<line x1="${a[0]}" y1="${a[1]}" x2="${b[0]}" y2="${b[1]}" stroke="#94a3b8"/>` : ''; }).join('');
      marks += nodes.map((node, index) => { const p = positions.get(String(node.name)); return `<g data-index="${index}" data-name="${esc(node.name)}"><circle cx="${p[0]}" cy="${p[1]}" r="7" fill="#3b82f6"/><text x="${p[0]}" y="${p[1] + 18}" text-anchor="middle" font-size="10">${esc(node.name)}</text></g>`; }).join('');
    } else {
      const values = series.flatMap(item => Array.isArray(item.data) ? item.data.map(point => Number(Array.isArray(point) ? point.at(-1) : point?.value ?? point) || 0) : []);
      const maximum = Math.max(1, ...values.map(Math.abs)), slot = (width - pad * 2) / Math.max(1, values.length);
      marks = values.map((value, index) => { const h = Math.abs(value) / maximum * (height - pad * 2); return `<rect data-index="${index}" x="${pad + index * slot + slot * .15}" y="${height - pad - h}" width="${slot * .7}" height="${h}" rx="2" fill="#3b82f6"/>`; }).join('');
    }
    host.innerHTML = `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Native chart" style="width:100%;height:100%">${marks}</svg>`;
  }

  global.nativeCharts = { init, getInstanceByDom: host => instances.get(host) };
})(window);
