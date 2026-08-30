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

-- 2. Branch clean rows from rows that need review
IF 1 = 1 BEGIN
  SELECT * INTO #ready_sales FROM #raw_sales WHERE amount IS NOT NULL;
END ELSE BEGIN
  SELECT * INTO #quarantine_sales FROM #raw_sales WHERE amount IS NULL;
END;

-- 3. Stop before loading if the quality gate fails
ASSERT (SELECT COUNT(*) FROM #ready_sales) > 0, 'No clean sales rows were staged.';`,
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
    const apiRequests = [];
    const exitRequests = [];
    const sandboxWorkspace = {
      files: JSON.parse(JSON.stringify(STUDIO_DESIGN_STATE.files)),
      folders: [{ path: 'reports' }, { path: 'etl' }, { path: 'scripts' }],
    };
    const workspaceSnapshot = result => ({
      files: JSON.parse(JSON.stringify(sandboxWorkspace.files)),
      folders: JSON.parse(JSON.stringify(sandboxWorkspace.folders)),
      result,
    });
    const authFetch = async (url, init) => {
      let body = null;
      try { body = init?.body ? JSON.parse(init.body) : null; } catch { /* test instrumentation only */ }
      apiRequests.push({ url: String(url), body });
      const delay = Number(window.__STUDIO_API_DELAY__?.({ url: String(url), body }) || 0);
      if (delay > 0) await new Promise(resolve => setTimeout(resolve, delay));
      return api(url, init);
    };

    const workbench = await studioMod.createStudioWorkbench(stage, {
      documents: JSON.parse(JSON.stringify(SAMPLE_DOCS)),
      workspaceFiles: JSON.parse(JSON.stringify(sandboxWorkspace.files)),
      workspaceFolders: JSON.parse(JSON.stringify(sandboxWorkspace.folders)),
      authFetch,
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
      onExit: async state => {
        exitRequests.push(state);
        return true;
      },
      onSave: async (content, path) => {
        console.log(`[Studio Save] Saved ${path} (${content.length} chars)`);
      },
      onRenameDocument: async (document, name) => {
        const slash = Math.max(document.path.lastIndexOf('/'), document.path.lastIndexOf('\\'));
        const directory = slash >= 0 ? document.path.slice(0, slash + 1) : '';
        const extension = name.includes('.') ? '' : document.name.slice(document.name.lastIndexOf('.'));
        const path = `${directory}${name}${extension}`;
        const file = sandboxWorkspace.files.find(item => item.path === document.path);
        if (file) file.path = path;
        return { path, name: path.slice(directory.length) };
      },
      onCreateWorkspaceFolder: async path => {
        const folder = { path };
        sandboxWorkspace.folders.push(folder);
        return workspaceSnapshot(folder);
      },
      onRenameWorkspaceEntry: async (entry, name) => {
        const slash = entry.path.lastIndexOf('/');
        const directory = slash >= 0 ? entry.path.slice(0, slash + 1) : '';
        const extension = !entry.isDirectory && !name.includes('.') ? entry.path.slice(entry.path.lastIndexOf('.')) : '';
        const path = `${directory}${name}${extension}`;
        if (entry.isDirectory) {
          sandboxWorkspace.folders.forEach(folder => { if (folder.path === entry.path || folder.path.startsWith(`${entry.path}/`)) folder.path = path + folder.path.slice(entry.path.length); });
          sandboxWorkspace.files.forEach(file => { if (file.path.startsWith(`${entry.path}/`)) file.path = path + file.path.slice(entry.path.length); });
        } else {
          const file = sandboxWorkspace.files.find(item => item.path === entry.path);
          if (file) file.path = path;
        }
        return workspaceSnapshot({ path, isDirectory: entry.isDirectory });
      },
      onDeleteWorkspaceEntry: async entry => {
        sandboxWorkspace.files = sandboxWorkspace.files.filter(file => file.path !== entry.path && !(entry.isDirectory && file.path.startsWith(`${entry.path}/`)));
        sandboxWorkspace.folders = sandboxWorkspace.folders.filter(folder => folder.path !== entry.path && !(entry.isDirectory && folder.path.startsWith(`${entry.path}/`)));
        return workspaceSnapshot(null);
      },
      onMoveWorkspaceFile: async (path, destinationFolder) => {
        const file = sandboxWorkspace.files.find(item => item.path === path);
        const name = path.slice(path.lastIndexOf('/') + 1);
        const movedPath = destinationFolder ? `${destinationFolder}/${name}` : name;
        if (file) file.path = movedPath;
        return workspaceSnapshot({ path: movedPath, isDirectory: false });
      },
    });

    window.__STUDIO_INSTANCE__ = workbench;
    window.__STUDIO_API_REQUESTS__ = apiRequests;
    window.__STUDIO_EXIT_REQUESTS__ = exitRequests;

    return () => {
      window.__STUDIO_INSTANCE__ = null;
      window.__STUDIO_API_REQUESTS__ = null;
      window.__STUDIO_EXIT_REQUESTS__ = null;
      window.__STUDIO_API_DELAY__ = null;
      workbench?.dispose?.();
    };
  }
};
