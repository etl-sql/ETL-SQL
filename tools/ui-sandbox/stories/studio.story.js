// Story: ETL-SQL Studio (Flagship Dual-Projection Visual & Script Workbench)
import { importFresh, STUDIO_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

const SAMPLE_DOCS = [
  {
    id: 'doc-report',
    path: 'reports/sales_overview.rptsql',
    name: 'sales_overview.rptsql',
    content: `CREATE CONNECTION corp_db AS MSSQL('SHARED:corp_sales_gw');

CREATE DATASET ds_orders AS 
  SELECT order_date, total_amount, region 
  FROM corp_db.orders
  WHERE status = 'Completed';

PAGE "Executive Overview" {
    CONTAINER row {
        VISUAL rev_kpi TYPE 'KPI' MAPPINGS (VALUE = SUM(total_amount)) OPTIONS (TITLE = 'Total Revenue');
        VISUAL order_bar TYPE 'BAR' MAPPINGS (X = region, Y = SUM(total_amount)) OPTIONS (TITLE = 'Sales by Region');
    }
}`,
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
    const api = makeMockApi({
      files: [
        { path: 'reports/sales_overview.rptsql', size: 1420 },
        { path: 'etl/ingest_orders.etlsql', size: 980 },
        { path: 'scripts/direct_connect_test.sql', size: 450 }
      ],
      connections: ['corp_db', 'staging_db']
    });

    const workbench = await studioMod.createStudioWorkbench(stage, {
      documents: JSON.parse(JSON.stringify(SAMPLE_DOCS)),
      authFetch: api,
      apiBase: '',
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
