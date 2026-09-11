import assert from 'node:assert/strict';
import {
    toHexColor,
    parseNumericRadius,
    parseNumericOpacity,
    visualFormatting,
    splitRuleCondition,
    renderVisualFormatInspectorHtml,
    renderFormattingSectionHtml,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/visual-format-inspector.js';

// ── 1. toHexColor ─────────────────────────────────────────────────────────────
assert.equal(toHexColor(null, '#000000'), '#000000');
assert.equal(toHexColor(undefined, '#ffffff'), '#ffffff');
assert.equal(toHexColor(12345, '#ffffff'), '#ffffff');
assert.equal(toHexColor('', '#123456'), '#123456');
assert.equal(toHexColor('invalid', '#123456'), '#123456');
assert.equal(toHexColor('rgb(255, 0, 0)', '#fallback'), '#fallback');

// 3-digit hex expansion
assert.equal(toHexColor('#abc', '#fallback'), '#aabbcc');
assert.equal(toHexColor('#FFF', '#fallback'), '#FFFFFF');
assert.equal(toHexColor(' #123 ', '#fallback'), '#112233');

// 6-digit hex preservation
assert.equal(toHexColor('#123456', '#fallback'), '#123456');
assert.equal(toHexColor('#abcdef', '#fallback'), '#abcdef');
assert.equal(toHexColor('  #ABCDEF  ', '#fallback'), '#ABCDEF');

// ── 2. parseNumericRadius ─────────────────────────────────────────────────────
assert.equal(parseNumericRadius(null, 8), 8);
assert.equal(parseNumericRadius(undefined, 8), 8);
assert.equal(parseNumericRadius('', 8), 8);
assert.equal(parseNumericRadius('abc', 8), 8);

// Valid integers and string units
assert.equal(parseNumericRadius(16, 8), 16);
assert.equal(parseNumericRadius('12px', 8), 12);
assert.equal(parseNumericRadius('4', 8), 4);

// Clamping between 0 and 32
assert.equal(parseNumericRadius('0px', 8), 0);
assert.equal(parseNumericRadius('32px', 8), 32);
assert.equal(parseNumericRadius('9999px', 8), 32);

// ── 3. parseNumericOpacity ────────────────────────────────────────────────────
assert.equal(parseNumericOpacity(null, 100), 100);
assert.equal(parseNumericOpacity(undefined, 100), 100);
assert.equal(parseNumericOpacity('', 100), 100);
assert.equal(parseNumericOpacity('invalid', 100), 100);

// Float <= 1 converts to percentage (0 - 100)
assert.equal(parseNumericOpacity(0.5, 100), 50);
assert.equal(parseNumericOpacity('0.75', 100), 75);
assert.equal(parseNumericOpacity('1', 100), 100);
assert.equal(parseNumericOpacity(1, 100), 100);
assert.equal(parseNumericOpacity('0', 100), 0);

// Numbers > 1 clamp to 0 - 100
assert.equal(parseNumericOpacity(80, 100), 80);
assert.equal(parseNumericOpacity('65', 100), 65);
assert.equal(parseNumericOpacity(150, 100), 100);

// ── 4. visualFormatting ───────────────────────────────────────────────────────
const visualWithoutFormatting = {
    type: 'BAR',
    title: 'Monthly Revenue',
};
const fmt = visualFormatting(visualWithoutFormatting);
assert.ok(visualWithoutFormatting.formatting);
assert.equal(visualWithoutFormatting.formatting.title?.text, 'Monthly Revenue');
assert.deepEqual(visualWithoutFormatting.formatting.xAxis, {});
assert.deepEqual(visualWithoutFormatting.formatting.yAxis, {});
assert.deepEqual(visualWithoutFormatting.formatting.palette, []);
assert.deepEqual(visualWithoutFormatting.formatting.conditionalRules, []);
assert.deepEqual(visualWithoutFormatting.formatting.fields, {});

// Preserves existing properties
const visualWithExisting = {
    type: 'LINE',
    formatting: {
        palette: ['#ff0000', '#00ff00'],
        xAxis: { title: 'Date' },
    },
};
const fmt2 = visualFormatting(visualWithExisting);
assert.deepEqual(fmt2.palette, ['#ff0000', '#00ff00']);
assert.equal(fmt2.xAxis?.title, 'Date');
assert.ok(Array.isArray(fmt2.conditionalRules));

// ── 5. splitRuleCondition ─────────────────────────────────────────────────────
assert.deepEqual(splitRuleCondition('amount <= 100'), { field: 'amount', operator: '<=', value: '100' });
assert.deepEqual(splitRuleCondition('sales >= 500'), { field: 'sales', operator: '>=', value: '500' });
assert.deepEqual(splitRuleCondition('status <> active'), { field: 'status', operator: '<>', value: 'active' });
assert.deepEqual(splitRuleCondition('status != pending'), { field: 'status', operator: '!=', value: 'pending' });
assert.deepEqual(splitRuleCondition('category = Books'), { field: 'category', operator: '=', value: 'Books' });
assert.deepEqual(splitRuleCondition('qty < 10'), { field: 'qty', operator: '<', value: '10' });
assert.deepEqual(splitRuleCondition('margin > 0.2'), { field: 'margin', operator: '>', value: '0.2' });

// Fallbacks
assert.deepEqual(splitRuleCondition(''), { field: 'value', operator: '<', value: '0' });
assert.deepEqual(splitRuleCondition(null, 'sales'), { field: 'sales', operator: '<', value: '0' });
assert.deepEqual(splitRuleCondition('badcondition', 'profit'), { field: 'profit', operator: '<', value: '0' });

// ── 6. renderVisualFormatInspectorHtml ────────────────────────────────────────
// 6.1 Cartesian Chart (BAR)
const barVisual = {
    name: 'SalesChart',
    type: 'BAR',
    title: 'Sales by Region',
    mappings: { x: 'region', y: 'revenue' },
    options: {
        FORMAT: '$#,##0',
        'COLOR:North': '#3b82f6',
        'COLOR:South': '#10b981',
        overlays: 'avg_line',
        ZERO_LINE: 'ON',
        STACKED: 'ON',
    },
    formatting: {
        subtitle: { text: 'Q1 Performance' },
        xAxis: { LABEL: 'Region', MIN: '0', MAX: '100', SCALE: 'LINEAR' },
        yAxis: { LABEL: 'Revenue', MIN: '0', MAX: '1000000', SCALE: 'LINEAR' },
        palette: ['#3b82f6', '#10b981', '#f59e0b'],
        conditionalRules: [
            { condition: 'revenue > 50000', backgroundColor: '#10b981', fontColor: '#ffffff' },
        ],
    },
};
const barHtml = renderVisualFormatInspectorHtml(barVisual, ['region', 'revenue', 'cost']);
assert.ok(barHtml.includes('pp-title'));
assert.ok(barHtml.includes('Sales by Region'));
assert.ok(barHtml.includes('Q1 Performance'));
assert.ok(barHtml.includes('data-axis="x" data-axis-key="LABEL"'));
assert.ok(barHtml.includes('data-axis="y" data-axis-key="LABEL"'));
assert.ok(barHtml.includes('pp-format-zero-line'));
assert.ok(barHtml.includes('pp-format-stacked'));
assert.ok(barHtml.includes('data-palette-color="0"'));
assert.ok(barHtml.includes('data-palette-add'));
assert.ok(barHtml.includes('data-named-color-name'));
assert.ok(barHtml.includes('data-rule-field'));
assert.ok(barHtml.includes('data-rule-operator'));
assert.ok(barHtml.includes('data-rule-value'));
assert.ok(barHtml.includes('data-rule-add'));

// 6.2 Pie / Donut
const donutVisual = {
    name: 'ShareDonut',
    type: 'DONUT',
    title: 'Market Share',
    options: { SORT: 'VALUE_DESC', EXPLODE: 'Tech' },
};
const donutHtml = renderVisualFormatInspectorHtml(donutVisual, ['category', 'share']);
assert.ok(donutHtml.includes('pp-format-pie-sort'));
assert.ok(donutHtml.includes('pp-format-pie-explode'));
assert.ok(!donutHtml.includes('data-axis="x"'));

// 6.3 Scatter / Bubble
const scatterVisual = {
    name: 'ScatterPlot',
    type: 'SCATTER',
    title: 'Speed vs Quality',
    options: { JITTER: 'ON' },
};
const scatterHtml = renderVisualFormatInspectorHtml(scatterVisual, ['speed', 'quality']);
assert.ok(scatterHtml.includes('pp-format-scatter-jitter'));
assert.ok(scatterHtml.includes('pp-format-scatter-jitter-width'));

const bubbleVisual = {
    name: 'RiskBubble',
    type: 'BUBBLE',
    title: 'Risk vs Reward',
    options: { MIN_BUBBLE_SIZE: '10', MAX_BUBBLE_SIZE: '50' },
};
const bubbleHtml = renderVisualFormatInspectorHtml(bubbleVisual, ['risk', 'reward', 'size']);
assert.ok(bubbleHtml.includes('pp-format-bubble-min-size'));
assert.ok(bubbleHtml.includes('pp-format-bubble-max-size'));

// 6.4 Heatmap
const heatmapVisual = {
    name: 'ActivityMap',
    type: 'HEATMAP',
    title: 'User Activity',
    options: { MIDPOINT: '50', CELL_BORDER: 'ON' },
};
const heatmapHtml = renderVisualFormatInspectorHtml(heatmapVisual, ['hour', 'day', 'count']);
assert.ok(heatmapHtml.includes('pp-format-heatmap-midpoint'));
assert.ok(heatmapHtml.includes('pp-format-heatmap-color-low'));
assert.ok(heatmapHtml.includes('pp-format-heatmap-cell-border'));

// 6.5 Waterfall
const waterfallVisual = {
    name: 'PnlWaterfall',
    type: 'WATERFALL',
    title: 'P&L Waterfall',
    options: { COLOR_TOTAL: '#3b82f6', COLOR_UP: '#10b981', COLOR_DOWN: '#ef4444' },
};
const waterfallHtml = renderVisualFormatInspectorHtml(waterfallVisual, ['item', 'delta']);
assert.ok(waterfallHtml.includes('pp-format-waterfall-color-total'));
assert.ok(waterfallHtml.includes('pp-format-waterfall-color-up'));
assert.ok(waterfallHtml.includes('pp-format-waterfall-color-down'));

// 6.6 Gantt
const ganttVisual = {
    name: 'ProjectSchedule',
    type: 'GANTT',
    title: 'Project Timeline',
    options: { TODAY_LINE: 'ON', TODAY_DATE: '2026-03-31' },
};
const ganttHtml = renderVisualFormatInspectorHtml(ganttVisual, ['task', 'start', 'end', 'progress']);
assert.ok(ganttHtml.includes('pp-format-gantt-today-line'));
assert.ok(ganttHtml.includes('pp-format-gantt-today-date'));

// 6.7 Candlestick
const candleVisual = {
    name: 'StockCandle',
    type: 'CANDLESTICK',
    title: 'Stock OHLC',
    options: { COLOR_UP: '#26a69a', COLOR_DOWN: '#ef5350' },
};
const candleHtml = renderVisualFormatInspectorHtml(candleVisual, ['time', 'open', 'high', 'low', 'close']);
assert.ok(candleHtml.includes('pp-format-candlestick-color-up'));
assert.ok(candleHtml.includes('pp-format-candlestick-color-down'));

// 6.8 Radar
const radarVisual = {
    name: 'SkillRadar',
    type: 'RADAR',
    title: 'Team Competencies',
    options: { SHAPE: 'CIRCLE', FILL_OPACITY: '0.25' },
};
const radarHtml = renderVisualFormatInspectorHtml(radarVisual, ['skill', 'score']);
assert.ok(radarHtml.includes('pp-format-radar-shape'));
assert.ok(radarHtml.includes('pp-format-radar-fill-opacity'));

// 6.9 Funnel
const funnelVisual = {
    name: 'SalesFunnel',
    type: 'FUNNEL',
    title: 'Conversion Funnel',
    options: { FUNNEL_SHAPE: 'PYRAMID', SHOW_PERCENT: 'ON' },
};
const funnelHtml = renderVisualFormatInspectorHtml(funnelVisual, ['stage', 'count']);
assert.ok(funnelHtml.includes('pp-format-funnel-shape'));
assert.ok(funnelHtml.includes('pp-format-funnel-show-percent'));

// 6.10 Sankey
const sankeyVisual = {
    name: 'EnergySankey',
    type: 'SANKEY',
    title: 'Energy Flows',
    options: { NODE_PADDING: '16', LINK_OPACITY: '0.6' },
};
const sankeyHtml = renderVisualFormatInspectorHtml(sankeyVisual, ['source', 'target', 'flow']);
assert.ok(sankeyHtml.includes('pp-format-sankey-node-padding'));
assert.ok(sankeyHtml.includes('pp-format-sankey-link-opacity'));

// 6.11 Treemap / Sunburst
const treemapVisual = {
    name: 'OrgTreemap',
    type: 'TREEMAP',
    title: 'Department Headcount',
    options: { SHOW_BREADCRUMB: 'ON', LABEL_MIN_SIZE: '30' },
};
const treemapHtml = renderVisualFormatInspectorHtml(treemapVisual, ['dept', 'team', 'headcount']);
assert.ok(treemapHtml.includes('pp-format-hierarchy-show-breadcrumb'));
assert.ok(treemapHtml.includes('pp-format-treemap-label-min-size'));

// 6.12 BoxPlot
const boxplotVisual = {
    name: 'LatencyBoxPlot',
    type: 'BOXPLOT',
    title: 'Service Latency Distribution',
    options: { BOX_STYLE: 'VIOLIN', NOTCHED: 'ON' },
};
const boxplotHtml = renderVisualFormatInspectorHtml(boxplotVisual, ['service', 'latency']);
assert.ok(boxplotHtml.includes('pp-format-boxplot-style'));
assert.ok(boxplotHtml.includes('pp-format-boxplot-notched'));

// 6.13 Network
const networkVisual = {
    name: 'GraphNetwork',
    type: 'NETWORK',
    title: 'Cluster Topology',
    options: { REPULSION: '600', DIRECTED: 'ON' },
};
const networkHtml = renderVisualFormatInspectorHtml(networkVisual, ['node_a', 'node_b', 'weight']);
assert.ok(networkHtml.includes('pp-format-network-repulsion'));
assert.ok(networkHtml.includes('pp-format-network-directed'));

// 6.14 Table
const tableVisual = {
    name: 'OrdersTable',
    type: 'TABLE',
    title: 'Recent Orders',
    mappings: { revenue: 'revenue' },
    formatting: {
        fields: {
            revenue: { format: '$#,##0.00', dataBar: true, dataBarColor: '#10b981' },
        },
    },
};
const tableHtml = renderVisualFormatInspectorHtml(tableVisual, ['id', 'customer', 'revenue', 'created_at']);
assert.ok(tableHtml.includes('data-format-field="revenue"'));
assert.ok(tableHtml.includes('data-field-format'));
assert.ok(tableHtml.includes('data-field-data-bar'));
assert.ok(tableHtml.includes('data-field-data-bar-color'));
// Number format is not in main Title & Number group for TABLE
assert.ok(!tableHtml.includes('id="pp-number-format"'));

// ── 7. renderFormattingSectionHtml ────────────────────────────────────────────
const cardVisual = {
    name: 'SummaryCard',
    type: 'CARD',
    options: {
        BACKGROUND: '#0f172a',
        COLOR: '#ffffff',
        BORDER: '1px solid #334155',
        BORDER_RADIUS: '12px',
        FONT: 'Inter, sans-serif',
        FONT_SIZE: '16px',
        FONT_WEIGHT: 'SEMIBOLD',
        SHADOW: 'ON',
        OPACITY: '0.9',
    },
};
const sectionHtml = renderFormattingSectionHtml(cardVisual);
assert.ok(sectionHtml.includes('Card style'));
assert.ok(sectionHtml.includes('pp-fmt-bg-picker'));
assert.ok(sectionHtml.includes('pp-fmt-bg-text'));
assert.ok(sectionHtml.includes('pp-fmt-color-picker'));
assert.ok(sectionHtml.includes('pp-fmt-color-text'));
assert.ok(sectionHtml.includes('pp-fmt-border-text'));
assert.ok(sectionHtml.includes('pp-fmt-radius-slider'));
assert.ok(sectionHtml.includes('pp-fmt-radius-text'));
assert.ok(sectionHtml.includes('pp-fmt-font-select'));
assert.ok(sectionHtml.includes('pp-fmt-size-select'));
assert.ok(sectionHtml.includes('pp-fmt-weight-select'));
assert.ok(sectionHtml.includes('pp-fmt-shadow-select'));
assert.ok(sectionHtml.includes('pp-fmt-opacity-slider'));
assert.ok(sectionHtml.includes('pp-fmt-opacity-text'));

console.log('Visual format inspector contract passed (all tests successful).');
