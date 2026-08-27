function svgFor(tier) {
  const sizes = {
    COMPACT: [480, 300],
    STANDARD: [720, 420],
    WIDE: [1200, 600],
  };
  const [width, height] = sizes[tier];
  const candles = [
    [0.18, 0.32, 0.63, 0.76],
    [0.42, 0.28, 0.46, 0.70],
    [0.66, 0.23, 0.58, 0.82],
    [0.84, 0.36, 0.49, 0.68],
  ].map((item, index) => {
    const x = Math.round(width * item[0]);
    const high = Math.round(height * item[1]);
    const open = Math.round(height * item[2]);
    const close = Math.round(height * item[3]);
    const low = Math.round(height * Math.min(.88, item[3] + .1));
    return `<g data-row-index="${index}"><line x1="${x}" y1="${high}" x2="${x}" y2="${low}" stroke="#334155" stroke-width="3"/><rect x="${x - 18}" y="${Math.min(open, close)}" width="36" height="${Math.max(8, Math.abs(close - open))}" rx="3" fill="${close < open ? '#16a34a' : '#dc2626'}"><title>Trading day ${index + 1}</title></rect></g>`;
  }).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="Responsive candlestick chart, ${tier.toLowerCase()} layout"><rect width="${width}" height="${height}" fill="#f8fafc"/><text x="24" y="34" font-family="system-ui" font-size="18" font-weight="700" fill="#0f172a">Price and volume · ${tier}</text>${candles}<text x="24" y="${height - 20}" font-family="system-ui" font-size="13" fill="#475569">Resize the sandbox: requests occur only when the tier changes.</text></svg>`;
}

function manifestFor(tier) {
  const size = tier === 'COMPACT' ? [480, 300] : tier === 'WIDE' ? [1200, 600] : [720, 420];
  return {
    source: 'ui-sandbox/native-chart-layout.rptsql',
    builtAt: '2026-08-26T12:00:00Z',
    title: 'Bounded native chart layout',
    formatting: { locale: '', timeZone: 'UTC', nullLabel: 'NULL' },
    visuals: [{
      name: 'PriceAndVolume',
      visualType: 'CUSTOM',
      nativeSvg: svgFor(tier),
      semanticFallback: { kind: 'TimeSeriesTable', heading: 'Price and volume', items: [], summary: 'Four daily candlesticks.' },
      layout: { tier, compactMaxWidth: 479, standardMaxWidth: 959, width: size[0], height: size[1] },
      columns: ['Day', 'Open', 'Close'],
      rows: [['Mon', '10', '13'], ['Tue', '13', '11'], ['Wed', '11', '17'], ['Thu', '17', '15']],
      options: { title: 'Price and volume' },
      actions: [],
      error: null,
    }],
    pages: [{ name: 'Main', structure: 'A', slotMap: { A: 'PriceAndVolume' } }],
    datasets: [],
    parameters: {},
    parameterMetadata: {},
  };
}

export default {
  id: 'native-chart-layout',
  title: 'Native chart responsive tiers',
  fixtures: [{ id: 'responsive', label: 'Compact / standard / wide' }],
  async mount(stage, _fixtureId, ctx) {
    const initial = JSON.stringify(manifestFor('STANDARD')).replace(/</g, '\\u003c');
    const compact = JSON.stringify(manifestFor('COMPACT')).replace(/</g, '\\u003c');
    const standard = JSON.stringify(manifestFor('STANDARD')).replace(/</g, '\\u003c');
    const wide = JSON.stringify(manifestFor('WIDE')).replace(/</g, '\\u003c');
    const iframe = document.createElement('iframe');
    iframe.className = 'vscode-webview-frame';
    iframe.srcdoc = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="/src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.css"><style>#layout-monitor{position:fixed;right:12px;bottom:10px;z-index:50;padding:6px 9px;border-radius:999px;background:#0f172a;color:#fff;font:12px system-ui}</style></head><body><div id="root"></div><div id="layout-monitor" aria-live="polite">Tier requests: 0</div><script>window.__IS_WEB__=true;window.__MANIFEST__=${initial};window.__layoutManifests={COMPACT:${compact},STANDARD:${standard},WIDE:${wide}};window.__layoutRequestCount=0;window.fetch=async function(url,options){if(String(url).endsWith('/layout')){var body=JSON.parse(options.body);window.__layoutRequestCount++;document.getElementById('layout-monitor').textContent='Tier requests: '+window.__layoutRequestCount+' · '+body.tier;return new Response(JSON.stringify(window.__layoutManifests[body.tier]),{status:200,headers:{'Content-Type':'application/json'}});}return new Response('{}',{status:404});};</script><script src="/src/ETL-SQL.ReportRuntime/Resources/Shared/report-runtime.js"></script></body></html>`;
    stage.replaceChildren(iframe);
    ctx.stat('Canonical runtime · ResizeObserver · 180 ms debounce · bounded server tiers');
    return { dispose() { iframe.remove(); }, resize() {} };
  },
};
