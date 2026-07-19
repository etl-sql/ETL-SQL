// Story: Unified Script Editor Workbench (with Sidebar, Horizontal DAG, Git, and Schema Explorer).
import { importFresh, DESIGNER_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

const UNIFIED_SCRIPTS = {
  etl: `CREATE CONNECTION staging_db AS POSTGRES(
  HOST = '127.0.0.1', 
  DATABASE = 'staging', 
  USER = 'SECRET:db_user', 
  PASSWORD = 'SECRET:db_pass'
);

CREATE CONNECTION analytics_dw AS MSSQL(
  SERVER = 'localhost', 
  DATABASE = 'dw', 
  TRUSTED_CONNECTION = TRUE
);

BEGIN TRY
  -- 1. Extract raw sales from staging postgres
  SELECT id, product_id, quantity, amount, sale_date
  INTO #raw_sales
  FROM staging_db.sales_orders
  WHERE sale_date >= DATEADD(DAY, -7, GETDATE());

  -- 2. Validate and enrich data
  UPDATE #raw_sales 
  SET amount = 0 
  WHERE amount IS NULL OR amount < 0;

  -- 3. Load & Merge into enterprise data warehouse
  MERGE INTO analytics_dw.dbo.SalesFact AS T
  USING #raw_sales AS S ON T.id = S.id
  WHEN MATCHED THEN
    UPDATE SET T.quantity = S.quantity, T.amount = S.amount
  WHEN NOT MATCHED THEN
    INSERT (id, product_id, quantity, amount, sale_date)
    VALUES (S.id, S.product_id, S.quantity, S.amount, S.sale_date);

  PRINT 'Batch ETL Load complete.';
END TRY
BEGIN CATCH
  PRINT 'Error encountered: ' + ERROR_MESSAGE();
  THROW;
END CATCH;`,

  // What a Portal interactive run is allowed to be: read-only SELECTs plus SELECT ... INTO #temp.
  // See PortalInteractiveRunPolicy — CREATE CONNECTION comes from the shared catalog, not the script.
  portal: `SELECT id, product_id, quantity, amount, sale_date
INTO #raw_sales
FROM staging_db.sales_orders
WHERE sale_date >= DATEADD(DAY, -7, GETDATE());

SELECT product_id, SUM(amount) AS revenue
FROM #raw_sales
GROUP BY product_id
ORDER BY revenue DESC;`,

  report: `SET REPORT TITLE = 'Weekly Revenue Fact Sheet';
SET REPORT DESCRIPTION = 'Performance breakdown across product categories';

CREATE DATASET &salesSummary AS (
  SELECT product_category, SUM(amount) AS total_revenue
  FROM analytics_dw.dbo.SalesFact
  GROUP BY product_category
);

CREATE VISUAL categoryBar AS BAR (
  SOURCE = &salesSummary,
  MAPPINGS (X = product_category, Y = total_revenue),
  STYLE (THEME = dark)
);

CREATE PAGE Overview AS DASHBOARD (
  STRUCTURE = 'A',
  MAP ('A' = categoryBar)
);`
};

export default {
  id: 'script-editor-unified',
  title: 'Unified Script Editor (Stateful)',
  subtitle: 'createScriptEditorWorkbench() + Stateful Sidebar',
  fixtures: [
    { id: 'etl', label: 'Stateful ETL Flow' },
    { id: 'report', label: 'Report SQL Dashboard' },
    { id: 'portal', label: 'Portal (schema + session only)' }
  ],
  async mount(stage, fixtureId, ctx) {
    const value = UNIFIED_SCRIPTS[fixtureId] ?? '';
    const mod = await importFresh(DESIGNER_JS);
    
    // Create seed configuration context
    const api = makeMockApi({
      files: [
        { path: 'etl/weekly_load.etlsql', size: 1024 },
        { path: 'etl/staging_clean.etlsql', size: 450 },
        { path: 'reports/sales_dashboard.rptsql', size: 2150 }
      ],
      connections: ['staging_db', 'analytics_dw'],
      variables: [
        { name: '@TODAY', value: new Date().toISOString().slice(0, 10), type: 'datetime' },
        { name: '@BATCH_LIMIT', value: '10000', type: 'int' }
      ],
      tempTables: [
        { name: '#raw_sales', columns: ['id', 'product_id', 'quantity', 'amount', 'sale_date'] },
        { name: '#cleaned_sales', columns: ['id', 'product_id', 'quantity', 'amount', 'sale_date', 'enriched_at'] }
      ]
    });

    // The Portal hosts the same workbench with a narrower sidebar: it has no file workspace
    // (its catalog is folders/reports) and git write-back is a separate roadmap item.
    const isPortal = fixtureId === 'portal';
    const sidebar = isPortal
      ? { schema: true, session: true }
      : { workspace: true, schema: true, session: true, git: true };

    const workbench = await mod.createScriptEditorWorkbench(stage, {
      title: fixtureId === 'etl' ? 'etl/weekly_load.etlsql'
        : isPortal ? 'Script' : 'reports/sales_dashboard.rptsql',
      runUrl: '/api/designer/run',
      previewApiUrl: '/api/designer/preview',
      previewUrl: '/tools/ui-sandbox/designer-preview.html',
      connectionRef: 'demo',
      authFetch: api,
      
      // Configuration for stateful sidebars
      workspaceRoot: 'C:/Users/chuck/scratch/ETL-SQL',
      sidebar,
      gitStatus: {
        branch: 'main',
        modified: ['etl/weekly_load.etlsql'],
        untracked: ['etl/new_enrichment.etlsql']
      },
      
      editor: {
        value,
        analyzeUrl: '/api/designer/analyze',
        completeUrl: '/api/designer/complete',
        hoverUrl: '/api/designer/hover',
        connectionRef: 'demo',
        authFetch: api,
        onChange: (v) => ctx.stat(`${v.length} chars · ${v.split('\n').length} lines`),
        onDiagnostics: (d) => ctx.stat(`${value.length} chars · ${d.length} diagnostics`),
      },
    });

    ctx.stat(`${value.length} chars · ${(value ? value.split('\n').length : 0)} lines`);
    return { dispose: () => workbench.dispose() };
  }
};
