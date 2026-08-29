// Story: ETL-SQL Studio (Flagship Dual-Projection Visual & Script Workbench)
import { importFresh, STUDIO_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

const SAMPLE_DOCS = [
  {
    id: 'doc-report',
    path: 'reports/sales_overview.rptsql',
    name: 'sales_overview.rptsql',
    content: `CREATE CONNECTION corp_db AS MSSQL(CONNECTION_STRING = 'SHARED:corp_sales_gw');

CREATE DATASET &orders AS (
  SELECT order_date, total_amount, region 
  FROM corp_db.orders
  WHERE status = 'Completed'
);

CREATE VISUAL RevenueCard AS CARD (
  SOURCE = &orders,
  TITLE = 'Total Revenue',
  MAPPINGS (VALUE = total_amount)
);

CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = &orders,
  TITLE = 'Sales by Region',
  MAPPINGS (X = region, Y = total_amount)
);

CREATE PAGE [Executive Overview] AS DASHBOARD (
  LAYOUT (STRUCTURE = 'A B', MAP ('A' = RevenueCard, 'B' = SalesByRegion))
);`,
    isDirty: false,
    projection: 'split',
  },
  {
    id: 'doc-etl',
    path: 'etl/ingest_orders.etlsql',
    name: 'ingest_orders.etlsql',
    content: `CREATE CONNECTION staging_db AS POSTGRES(
  HOST = '127.0.0.1',
  DATABASE = 'staging',
  USER = 'SECRET:db_user',
  PASSWORD = 'SECRET:db_pass'
);

-- 1. Extract raw sales into engine memory
SELECT id, product_id, quantity, amount, sale_date
INTO #raw_sales
FROM staging_db.sales_orders
WHERE sale_date >= DATEADD(DAY, -7, GETDATE());

-- 2. Validate clean non-null amounts
UPDATE #raw_sales SET amount = 0 WHERE amount IS NULL;

PRINT 'Staging extract & clean complete.';`,
    isDirty: false,
    projection: 'split',
  },
  {
    id: 'doc-secret-test',
    path: 'scripts/direct_connect_test.sql',
    name: 'direct_connect_test.sql',
    content: `-- Test script with raw password to verify Zero-Trust secret warning modal
CREATE CONNECTION raw_test AS MSSQL(
  SERVER = '10.0.0.5',
  DATABASE = 'appdb',
  USER = 'admin',
  PASSWORD = 'SuperSecretPassword123!'
);

SELECT TOP 10 * FROM raw_test.users;`,
    isDirty: true,
    projection: 'code',
  }
];

const STUDIO_DESIGN_STATE = {
  pages: [{
    id: 'p1', name: 'Executive Overview', mode: 'Dashboard', visuals: [
      { id: 'v1', name: 'RevenueCard', type: 'CARD', gridCol: 1, gridRow: 1, gridColSpan: 6, gridRowSpan: 4, title: 'Total Revenue', dataset: '&orders', mappings: { VALUE: 'total_amount' }, options: {} },
      { id: 'v2', name: 'SalesByRegion', type: 'BAR', gridCol: 7, gridRow: 1, gridColSpan: 6, gridRowSpan: 4, title: 'Sales by Region', dataset: '&orders', mappings: { X: 'region', Y: 'total_amount' }, options: {} },
    ],
  }],
  datasets: [{ id: 'ds1', name: '&orders', query: "SELECT order_date, total_amount, region FROM corp_db.orders WHERE status = 'Completed'" }],
  parameters: [],
  files: [
    { path: 'reports/sales_overview.rptsql', size: 1420 },
    { path: 'etl/ingest_orders.etlsql', size: 980 },
    { path: 'scripts/direct_connect_test.sql', size: 450 },
  ],
  connections: ['corp_db', 'staging_db'],
};

export default {
  id: 'studio',
  title: 'ETL-SQL Studio',
  subtitle: 'Flagship Dual-Projection Visual & Script Workbench',
  fixtures: [
    { id: 'default', label: 'Multi-Tab Workbench (Report, ETL, Script)' },
  ],
  async mount(stage, fixtureId, ctx) {
    // Import canonical studio module
    const studioMod = await importFresh(STUDIO_JS);
    const api = makeMockApi(STUDIO_DESIGN_STATE);

    const workbench = await studioMod.createStudioWorkbench(stage, {
      documents: JSON.parse(JSON.stringify(SAMPLE_DOCS)),
      workspaceFiles: JSON.parse(JSON.stringify(STUDIO_DESIGN_STATE.files)),
      authFetch: api,
      apiBase: '',
      initialSnapshot: {
        source: '&orders',
        columns: [
          { name: 'order_date', type: 'DATE' },
          { name: 'total_amount', type: 'DECIMAL' },
          { name: 'region', type: 'VARCHAR' },
        ],
        rowCount: 5,
        rows: [
          { order_date: '2026-08-03', total_amount: 1840, region: 'North' },
          { order_date: '2026-08-09', total_amount: 920, region: 'South' },
          { order_date: '2026-08-14', total_amount: 2310, region: 'North' },
          { order_date: '2026-08-21', total_amount: 1280, region: 'West' },
          { order_date: '2026-08-27', total_amount: 2760, region: 'East' },
        ],
      },
      onSave: async (content, path) => {
        console.log(`[Studio Save] Saved ${path} (${content.length} chars)`);
      },
    });

    window.__STUDIO_INSTANCE__ = workbench;

    return () => {
      window.__STUDIO_INSTANCE__ = null;
      workbench?.dispose?.();
    };
  }
};
