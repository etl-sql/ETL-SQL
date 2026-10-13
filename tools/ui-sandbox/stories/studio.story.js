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

/**
 * A stand-in for the Portal's pipeline-run host: a run is "running" for a few polls and then ends.
 *
 * Set `window.__STUDIO_PIPELINE_RUN__` to script the other endings before pressing Run:
 * `{ outcome: 'FAILURE' }`, `{ outcome: 'refuse' }` (no grant, nothing runs), `{ outcome: 'lost' }`
 * (status reads stop answering), `{ cancel: 'refuse' }` (Stop is never confirmed).
 */
function makePipelineRunHost() {
  let lastRunId = 100;
  const runs = new Map();
  const scripted = () => window.__STUDIO_PIPELINE_RUN__ || {};
  const scriptHash = 'sha256:5f2c9a41d07be36c1a9f04d2e8b7c3a15d60f9e2';
  return {
    submit: async document => {
      if (scripted().outcome === 'refuse')
        throw new Error("This account holds no EXECUTE grant on Orchestrator job 'PIPELINE:42'. An owner or manager of the job grants it.");
      const runId = ++lastRunId;
      runs.set(runId, { polls: 0, cancelled: false });
      return { runId, version: document.version, scriptHash };
    },
    read: async (_document, runId) => {
      if (scripted().outcome === 'lost') throw new TypeError('Failed to fetch');
      const run = runs.get(runId);
      run.polls += 1;
      const done = run.cancelled || run.polls >= 4;
      const status = run.cancelled ? 'CANCELLED'
        : !done ? 'RUNNING'
        : scripted().outcome === 'FAILURE' ? 'FAILURE' : 'SUCCESS';
      return {
        runId, status, isTerminal: done, scriptHash, logTruncated: false,
        rowsProcessed: status === 'SUCCESS' ? 128 : 0,
        errorMessage: status === 'FAILURE' ? 'Assertion failed: No clean sales rows were staged.'
          : status === 'CANCELLED' ? 'The operation was canceled.' : null,
        statements: !done || run.cancelled ? [] : [
          { statement: 'SELECT id, product_id, quantity, amount, sale_date INTO #raw_sales FROM staging_db.sales_orders WHERE sale_date >= ?;', durationMs: 412, rowsProcessed: 128, failed: false },
          { statement: 'ASSERT ((SELECT COUNT(*) FROM #ready_sales) > ?);', durationMs: 3, rowsProcessed: 0, failed: status === 'FAILURE' },
        ],
      };
    },
    cancel: async (_document, runId) => {
      if (scripted().cancel === 'refuse') throw new TypeError('Failed to fetch');
      runs.get(runId).cancelled = true;
    },
  };
}

/**
 * A stand-in for the Portal's schedule host. The schedule pins version 3; saving the document
 * afterwards is what makes the panel report the pinned revision as stale.
 * `window.__STUDIO_PIPELINE_RUN__ = { schedule: 'refuse' }` answers as a caller without MANAGE.
 */
function makePipelineScheduleHost() {
  let schedule = { scheduled: false, cron: null, timeZone: null, enabled: false, nextRun: null, lastRun: null, pinnedVersion: null };
  const view = document => ({
    ...schedule,
    scheduledRevisionIsSaved: schedule.scheduled && schedule.pinnedVersion === document.version,
    savedVersion: document.version,
  });
  return {
    read: async document => view(document),
    set: async (document, cron, timeZone) => {
      if (window.__STUDIO_PIPELINE_RUN__?.schedule === 'refuse')
        throw new Error("Scheduling changes what Orchestrator job 'PIPELINE:42' runs unattended, so it needs a MANAGE grant on that job. EXECUTE is enough to run it, not to schedule it.");
      schedule = { scheduled: true, cron, timeZone: timeZone || 'UTC', enabled: true, nextRun: '2030-01-02T02:00:00Z', lastRun: null, pinnedVersion: document.version };
      return view(document);
    },
    remove: async document => {
      schedule = { scheduled: false, cron: null, timeZone: null, enabled: false, nextRun: null, lastRun: null, pinnedVersion: null };
      return view(document);
    },
  };
}

export default {
  id: 'studio',
  title: 'ETL-SQL Studio',
  subtitle: 'Flagship Dual-Projection Visual & Script Workbench',
  fixtures: [
    { id: 'default', label: 'Multi-Tab Workbench (Report, ETL, Script)' },
    { id: 'pending-save', label: 'Slow Save (type while saving)' },
    { id: 'crlf-pipeline', label: 'CRLF Pipeline (initial projection)' },
    { id: 'pipeline-run', label: 'Saved Pipeline (Run, follow, Stop, schedule)' },
    { id: 'helper-context', label: 'Helper preview context controls (mock rows)' },
    { id: 'degraded-editor', label: 'Editor recovery (block CodeMirror to exercise fallback)' },
    { id: 'large-document', label: 'Large document (editor services pause above 200,000 characters)' },
    { id: 'catalog-paging', label: 'Catalog search and paging (125 mock documents)' },
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
      // Test instrumentation: answer with a scripted Response, or 'disconnect' to fail the fetch
      // the way a browser does when the host is unreachable.
      const scripted = window.__STUDIO_API_RESPONSE__?.({ url: String(url), body });
      if (scripted === 'disconnect') throw new TypeError('Failed to fetch');
      if (scripted) return scripted;
      return api(url, init);
    };

    const documents = JSON.parse(JSON.stringify(SAMPLE_DOCS));
    if (fixtureId === 'large-document') {
      documents.find(document => document.id === 'doc-etl').content = '--' + 'é'.repeat(199999);
    }
    const catalog = Array.from({ length: 125 }, (_, index) => ({
      id: index + 1, folderId: 1, folderPath: '/Practice', name: `Practice ${String(index + 1).padStart(3, '0')}`,
      kind: 'Report', version: 1,
    }));
    if (fixtureId === 'crlf-pipeline') {
      const pipeline = documents.find(document => document.id === 'doc-etl');
      pipeline.content = pipeline.content.replace(/\r?\n/g, '\r\n');
    }
    if (fixtureId === 'pipeline-run') {
      // A saved catalog pipeline: Run submits its saved version to the host and follows that run.
      Object.assign(documents.find(document => document.id === 'doc-etl'), { reportId: 42, version: 3 });
    }
    const workbench = await studioMod.createStudioWorkbench(stage, {
      documents,
      activeDocId: fixtureId === 'catalog-paging' ? '__home__'
        : ['crlf-pipeline', 'pipeline-run', 'degraded-editor', 'large-document'].includes(fixtureId) ? 'doc-etl' : undefined,
      catalogReports: fixtureId === 'catalog-paging' ? catalog.slice(0, 50) : undefined,
      catalogDiscovery: fixtureId === 'catalog-paging' ? {
        nextAfterId: 50,
        search: async (query, afterId) => {
          const matches = catalog.filter(report => report.id > afterId && report.name.includes(query));
          return { items: matches.slice(0, 50), nextAfterId: matches.length > 50 ? matches[49].id : null };
        },
      } : undefined,
      onOpenDocument: fixtureId === 'catalog-paging' ? async report => ({
        id: `report-${report.id}`, reportId: report.id, name: `${report.name}.rptsql`,
        path: `/Practice/${report.name}.rptsql`, content: 'SELECT 1;',
      }) : undefined,
      pipelineRuns: fixtureId === 'pipeline-run' ? makePipelineRunHost() : undefined,
      pipelineSchedule: fixtureId === 'pipeline-run' ? makePipelineScheduleHost() : undefined,
      workspaceFiles: JSON.parse(JSON.stringify(sandboxWorkspace.files)),
      workspaceFolders: JSON.parse(JSON.stringify(sandboxWorkspace.folders)),
      authFetch,
      apiBase: '',
      initialSnapshot: sampleSnapshot(),
      onSave: async (content, path) => {
        if (fixtureId === 'pending-save') await new Promise(resolve => setTimeout(resolve, 3000));
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

    let helperWorkbench = null;
    if (fixtureId === 'helper-context') {
      const { createQueryWorkbench } = await importFresh(STUDIO_JS.replace('studio.js', 'studio-query-workbench.js'));
      const { STUDIO_ROUTES } = await importFresh(STUDIO_JS.replace('studio.js', 'studio-contracts.js'));
      const helperHost = document.createElement('section');
      helperHost.setAttribute('aria-label', 'Helper preview context example');
      stage.appendChild(helperHost);
      helperWorkbench = await createQueryWorkbench(helperHost, {
        context: 'engine', value: 'SELECT Id FROM #stage WHERE Id >= @minimum', previewContextLine: 3,
        scriptText: () => 'DECLARE @minimum INT = 2;\nSELECT 2 AS Id INTO #stage;\nSELECT 99 AS Later;',
        routes: STUDIO_ROUTES,
        request: async (_route, options) => options.body.previewContextLine === 3
          ? { columns: ['Id'], rows: [{ Id: 2 }], rowCount: 1 }
          : { success: false, message: 'Preview context is unavailable. Nothing was executed.' },
      });
    }
    window.__STUDIO_INSTANCE__ = workbench;
    window.__STUDIO_API_REQUESTS__ = apiRequests;
    // The seed the mock host answers from, exposed so a test can put the host into a state the
    // fixtures do not cover — a connection whose schema read fails, for instance. The mock closes
    // over this same object, so a mutation here is visible to the next request.
    window.__STUDIO_SEED_STATE__ = STUDIO_DESIGN_STATE;
    stage.querySelector('.etlsql-studio-shell')?.setAttribute('data-studio-ready', 'true');

    return () => {
      window.__STUDIO_INSTANCE__ = null;
      window.__STUDIO_API_REQUESTS__ = null;
      delete STUDIO_DESIGN_STATE.unreadableConnections;
      window.__STUDIO_SEED_STATE__ = null;
      window.__STUDIO_API_DELAY__ = null;
      window.__STUDIO_PIPELINE_RUN__ = null;
      window.__STUDIO_NO_GIT__ = null;
      workbench?.dispose?.();
      helperWorkbench?.dispose?.();
    };
  }
};
