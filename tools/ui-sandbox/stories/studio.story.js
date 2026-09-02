// Story: ETL-SQL Studio (Flagship Dual-Projection Visual & Script Workbench)
import { importFresh, STUDIO_JS } from '../util.js';
import { makeMockApi } from '../mockApi.js';

/**
 * The sample the workbench opens with.
 *
 * Set `window.__STUDIO_SAMPLE_ROWS__` before mounting for a wide one — a distinct region per row —
 * so a surface whose behaviour only begins past its first page of values (search, paging, bulk
 * selection in the filter pane) can be driven at all. Five rows is the readable default.
 */
function sampleSnapshot() {
  const columns = [
    { name: 'order_date', type: 'DATE' },
    { name: 'total_amount', type: 'DECIMAL' },
    { name: 'region', type: 'VARCHAR' },
  ];
  const wide = Number(globalThis.__STUDIO_SAMPLE_ROWS__) || 0;
  const rows = wide > 0
    ? Array.from({ length: wide }, (_, index) => ({
      order_date: `2026-08-${String((index % 28) + 1).padStart(2, '0')}`,
      total_amount: (index + 1) * 100,
      region: `region_${String(index).padStart(2, '0')}`,
    }))
    : [
      { order_date: '2026-08-03', total_amount: 1840, region: 'North' },
      { order_date: '2026-08-09', total_amount: 920, region: 'South' },
      { order_date: '2026-08-14', total_amount: 2310, region: 'North' },
      { order_date: '2026-08-21', total_amount: 1280, region: 'West' },
      { order_date: '2026-08-27', total_amount: 2760, region: 'East' },
    ];
  return { source: '&orders', columns, rowCount: rows.length, rows };
}

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
  MAPPINGS (VALUE = total_amount),
  OPTIONS (FORMAT = '$#,##0.00'),
  FORMATTING (WHEN total_amount < 1000 THEN '#3f1d24' FONT_COLOR '#ffb4ab')
);

CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = &orders,
  TITLE (TEXT = 'Sales by Region', COLOR = '#f0f6fc', FONT = 'Segoe UI', SIZE = '14px', WEIGHT = 'BOLD', ALIGN = LEFT),
  MAPPINGS (X = region, Y = total_amount),
  OPTIONS (LEGEND = OFF, GRID_LINES = ON, BAND_SIZE = 0.75,
    X_AXIS (LABEL = 'Region', LABEL_ROTATION = AUTO, LABEL_SKIP = 0),
    Y_AXIS (LABEL = 'Revenue', MIN = 0, INCLUDE_ZERO = ON, MAJOR_TICK_COUNT = 6, MINOR_TICKS = ON, FORMAT = '$#,##0')),
  STYLE (PALETTE = ('#58a6ff', '#2ea043', '#d29922'))
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
ASSERT (SELECT COUNT(*) FROM #ready_sales) > 0, 'No clean sales rows were staged.';

-- 4. A labelled execution task: the canvas can rename, reorder, and delete this one.
load_orders:
EXECUTE staging_db BEGIN
    INSERT INTO warehouse.sales SELECT * FROM staging.ready_sales;
END;`,
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
      { id: 'v1', name: 'RevenueCard', type: 'CARD', gridCol: 1, gridRow: 1, gridColSpan: 6, gridRowSpan: 4, title: 'Total Revenue', dataset: '&orders', mappings: { VALUE: 'total_amount' }, options: { FORMAT: '$#,##0.00' }, formatting: { title: { text: 'Total Revenue' }, conditionalRules: [{ condition: 'total_amount < 1000', backgroundColor: '#3f1d24', fontColor: '#ffb4ab' }] } },
      { id: 'v2', name: 'SalesByRegion', type: 'BAR', gridCol: 7, gridRow: 1, gridColSpan: 6, gridRowSpan: 4, title: 'Sales by Region', dataset: '&orders', mappings: { X: 'region', Y: 'total_amount' }, options: { LEGEND: 'OFF', GRID_LINES: 'ON', BAND_SIZE: '0.75' }, formatting: { title: { text: 'Sales by Region', color: '#f0f6fc', font: 'Segoe UI', size: '14px', weight: 'BOLD', align: 'LEFT' }, xAxis: { LABEL: 'Region', LABEL_ROTATION: 'AUTO', LABEL_SKIP: '0' }, yAxis: { LABEL: 'Revenue', MIN: '0', INCLUDE_ZERO: 'ON', MAJOR_TICK_COUNT: '6', MINOR_TICKS: 'ON', FORMAT: '$#,##0' }, palette: ['#58a6ff', '#2ea043', '#d29922'] } },
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
      initialSnapshot: sampleSnapshot(),
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
      onLoadGitStatus: window.__STUDIO_NO_GIT__ ? null : async () => ({
        branch: 'feature/studio-diff',
        modified: ['reports/sales_overview.rptsql'],
        untracked: [],
        staged: [],
        isGitRepository: true,
      }),
      onLoadGitHistory: window.__STUDIO_NO_GIT__ ? null : async () => ({
        isGitRepository: true,
        entries: [
          { revision: 'a12bc34def567890123456789012345678901234', shortRevision: 'a12bc34d', authoredAt: '2026-08-28T14:20:00Z', author: 'Studio Author', subject: 'Add sales overview' },
          { revision: '91fe230abc456789012345678901234567890123', shortRevision: '91fe230a', authoredAt: '2026-08-26T09:10:00Z', author: 'Studio Author', subject: 'Start report workspace' },
        ],
      }),
      onLoadGitDiff: window.__STUDIO_NO_GIT__ ? null : async (document, revision, content) => ({
        path: document.path,
        revision,
        baselineLabel: revision === 'HEAD' ? 'HEAD a12bc34d' : revision.slice(0, 8),
        baselineContent: document.content.replace("TITLE = 'Total Revenue'", "TITLE = 'Revenue'"),
        workingContent: content,
      }),
    });

    window.__STUDIO_INSTANCE__ = workbench;
    window.__STUDIO_API_REQUESTS__ = apiRequests;
    stage.querySelector('.etlsql-studio-shell')?.setAttribute('data-studio-ready', 'true');

    return () => {
      window.__STUDIO_INSTANCE__ = null;
      window.__STUDIO_API_REQUESTS__ = null;
      window.__STUDIO_API_DELAY__ = null;
      window.__STUDIO_NO_GIT__ = null;
      workbench?.dispose?.();
    };
  }
};
