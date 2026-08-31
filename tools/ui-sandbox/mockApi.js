// A fetch-like function that answers the report designer's endpoints with canned
// data, so createDesigner() runs in the sandbox with no portal server.
//
// createDesigner calls:
//   POST /api/designer/generate {designState}  -> { script }
//   POST /api/designer/parse    {script}       -> { designState }
//   POST /api/designer/analyze  {script}       -> { diagnostics }
//   POST /api/designer/complete {script,line,column,connectionRef} -> { items }
//   POST /api/designer/run      {script,selection,connectionRef} -> { columns, rows }
//   POST /api/designer/dag      {script,documentUri} -> { parsed, dag }
//   GET  /api/designer/schema?connection=demo -> { tables }
//   (save endpoints are bypassed via opts.onSaveScript in the story)
//
// The parse round-trip just echoes the seed state — a faithful script↔state parse
// is the real DesignerController's job; here we only need the UI to round-trip.
// Distinguishes "no handler matched" from a handler that legitimately returns an empty object.
const UNMATCHED = Symbol('unmatched-mock-route');

export function makeMockApi(seedState) {
  let commitCount = 0;
  return async (url, init) => {
    const path = String(url).replace(/^https?:\/\/[^/]+/, '').replace(/\?.*$/, '');
    let body = {};
    try { body = init?.body ? JSON.parse(init.body) : {}; } catch { /* ignore */ }

    let data = UNMATCHED;
    if (path.endsWith('/api/designer/generate')) {
      data = { script: generateMockScript(body.designState ?? seedState) };
    } else if (path.endsWith('/api/designer/patch')) {
      data = body.script
        ? { script: mockPatchScript(body.script, body.designState ?? seedState) }
        : { script: generateMockScript(body.designState ?? seedState) };
    } else if (path.endsWith('/api/designer/query-filter')) {
      data = { source: body.source };
    } else if (path.endsWith('/api/designer/option-source')) {
      data = { source: `(SELECT DISTINCT ${body.column} FROM &orders ORDER BY ${body.column})` };
    } else if (path.endsWith('/api/designer/parse')) {
      const scriptText = body.script || '';
      if (scriptText.includes('SYNTAX_ERROR') || scriptText.includes('>>> INVALID <<<') || body._mockParseError) {
        data = { error: 'Syntax error: Unexpected token in script', designState: null };
      } else {
        const designState = JSON.parse(JSON.stringify(seedState));
        const datasetPattern = /CREATE\s+DATASET\s+(&?[A-Za-z_][A-Za-z0-9_]*)\s+AS\s*\(([\s\S]*?)\)\s*;/gi;
        const datasets = [];
        let match;
        while ((match = datasetPattern.exec(scriptText)) !== null) {
          datasets.push({ id: `ds_${datasets.length}`, name: match[1], query: match[2].trim() });
        }
        if (datasets.length) designState.datasets = datasets;
        const pagePattern = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?PAGE\s+(?:\[([^\]]+)\]|([A-Za-z_][A-Za-z0-9_]*))\s+AS\s+(DASHBOARD|PAGINATED)/gi;
        const pages = [];
        while ((match = pagePattern.exec(scriptText)) !== null) {
          const mode = match[3][0] + match[3].slice(1).toLowerCase();
          // Every visual lands on the first page: the mock does not resolve a page's MAP
          // clause, and a visual the design state cannot see is worse than one on the wrong page.
          pages.push({
            id: `p${pages.length + 1}`,
            name: match[1] || match[2],
            mode,
            visuals: pages.length === 0 ? mockParseVisuals(scriptText) : [],
            printLayout: mode === 'Paginated' ? {
              pageSize: scriptText.match(/PAGE_SIZE\s*=\s*'([^']+)'/i)?.[1] || 'Letter',
              orientation: scriptText.match(/ORIENTATION\s*=\s*'([^']+)'/i)?.[1] || 'PORTRAIT',
              marginTop: Number(scriptText.match(/MARGINS\s*=\s*\(\s*([0-9.]+)/i)?.[1] || 0.75),
              marginRight: Number(scriptText.match(/MARGINS\s*=\s*\(\s*[0-9.]+\s*,\s*([0-9.]+)/i)?.[1] || 0.75),
              marginBottom: 0.75,
              marginLeft: 0.75,
              units: 'in',
              overflow: 'SPLIT',
            } : null,
          });
        }
        if (pages.length) designState.pages = pages;
        // Parameters were not reported at all, so a DECLARE the canvas wrote was invisible to
        // every reader of the design state.
        designState.parameters = mockParseParameters(scriptText);
        data = { designState };
      }
    } else if (path.endsWith('/api/designer/dag')) {
      data = (body.script || '').includes('>>> INVALID <<<') ? {
        parsed: false,
        error: 'Syntax error: Unexpected token in pipeline script',
        dag: { nodes: [], edges: [] },
      } : {
        parsed: true,
        error: null,
        dag: {
          nodes: [
            { id: 'staging_db', label: 'CONNECT staging_db', type: 'connection', meta: { line: 1 } },
            { id: '#raw_sales', label: 'SELECT INTO #raw_sales', type: 'io', meta: { line: 8 } },
            { id: 'quality_branch', label: 'IF', type: 'conditional', meta: { line: 15 } },
            { id: '#ready_sales', label: 'SELECT INTO #ready_sales', type: 'io', meta: { line: 16 } },
            { id: '#quarantine_sales', label: 'SELECT INTO #quarantine_sales', type: 'io', meta: { line: 19 } },
            { id: 'quality_gate', label: 'ASSERT', type: 'validation', meta: { line: 22 } },
          ],
          edges: [
            { source: 'staging_db', target: '#raw_sales' },
            { source: '#raw_sales', target: 'quality_branch' },
            { source: 'quality_branch', target: '#ready_sales', label: 'TRUE' },
            { source: 'quality_branch', target: '#quarantine_sales', label: 'ELSE' },
            { source: '#ready_sales', target: 'quality_gate' },
            { source: '#quarantine_sales', target: 'quality_gate' },
          ],
        },
      };
    } else if (path.endsWith('/api/designer/analyze')) {
      data = { diagnostics: analyzeMockScript(body.script ?? '') };
    } else if (path.endsWith('/api/designer/complete')) {
      data = { items: completeMockScript(body.script ?? '', body.line ?? 0, body.column ?? 0, body.connectionRef ?? null) };
    } else if (path.endsWith('/script-source/commit') || path.endsWith('/api/git/commit')) {
      // First commit records a new revision; a second (no changes) reports nothing to commit.
      commitCount += 1;
      data = commitCount === 1
        ? { sourceRevision: 'sandboxc0ffee1', committed: true }
        : { sourceRevision: 'sandboxc0ffee1', committed: false };
    } else if (path.endsWith('/api/designer/preview')) {
      // Return a real, chart-rich manifest (built from tools/ui-sandbox/fixtures/sandbox-report.rptsql
      // via the report CLI) so the preview iframe renders KPI cards + bar/pie/line charts + a table.
      data = await fetch('/tools/ui-sandbox/fixtures/sandbox-report.manifest.json')
        .then(r => r.json())
        .catch(() => ({ title: 'Preview', pages: [], visuals: [] }));
    } else if (path.endsWith('/api/designer/data-preview') || path.endsWith('/api/designer/data-sample')) {
      const source = body.sourceKind === 'temp' ? body.tempTable
        : body.sourceKind === 'dataset' ? body.dataset
        : `${body.connection}.${body.table}`;
      const datasetQuery = body.sourceKind === 'dataset' ? extractDatasetQuery(body.script || '', body.dataset) : '';
      const tableName = body.sourceKind === 'temp' ? body.tempTable
        : body.sourceKind === 'dataset' ? selectTargetTable(datasetQuery)
        : body.table;
      const table = mockSchemaTables().find((t) => t.name.toLowerCase() === String(tableName || '').replace(/^#/, '').toLowerCase())
        || mockSchemaTables()[0];
      const select = body.sourceKind === 'dataset' ? datasetQuery : null;
      const columns = select ? resolveSelectColumns(select, table) : table.columns.map((c) => c.name);
      const rows = mockRowsForColumns(columns, table);
      data = {
        sourceKind: body.sourceKind,
        source,
        columns,
        rows,
        rowCount: rows.length,
        capped: false,
        byteCapped: false,
        elapsedMs: body.sourceKind === 'temp' ? 12 : 31,
        message: `Previewed ${rows.length} rows from ${source}.`,
      };
    } else if (path.endsWith('/api/designer/run')) {
      data = runMockScript(body.selection || body.script || '');
    } else if (path.endsWith('/api/designer/schema')) {
      const connParam = new URL(url, window.location.origin).searchParams.get('connection') || 'demo';
      data = { connection: connParam, tables: mockSchemaTables() };
    } else if (path.endsWith('/api/scripts/upload')) {
      data = { path: 'sandbox/' + (body.fileName || 'report.rptsql') };
    } else if (path.endsWith('/api/designer/save')) {
      data = { version: 2, sourceRevision: 'sandbox-rev-2' };
    } else if (path.endsWith('/api/designer/lease') && init?.method === 'POST') {
      data = { acquired: true, owner: 'sandbox-author', expiresAt: new Date(Date.now() + 300_000).toISOString() };
    } else if (path.includes('/api/designer/lease/') && init?.method === 'DELETE') {
      data = {};
    } else if (path.endsWith('/api/reports') || path.includes('/script-content')) {
      data = { id: 1, ok: true, version: 1, sourceRevision: 'sandbox-rev-1' };
    } else if (path.endsWith('/api/workspace')) {
      data = {
        // Placeholder only — this is mock response data, never a real path. Keep it generic so the
        // sandbox does not bake one developer's checkout location into the repository.
        root: 'C:/workspace/ETL-SQL',
        files: seedState?.files || [
          { path: 'etl/weekly_load.etlsql', size: 1024 },
          { path: 'etl/staging_clean.etlsql', size: 450 }
        ]
      };
    } else if (path.endsWith('/api/connections')) {
      // Desktop-host route: the workspace's registered connections. The Portal has no equivalent,
      // which is why studio.js reaches it behind a workspace-host check.
      data = { connections: seedState?.connections || ['staging_db', 'analytics_dw'] };
    } else if (path.endsWith('/api/session/metadata')) {
      data = {
        connections: seedState?.connections || ['staging_db', 'analytics_dw'],
        variables: seedState?.variables || [],
        tempTables: seedState?.tempTables || []
      };
    } else if (path.endsWith('/api/formatter/config')) {
      if (init?.method === 'POST') {
        seedState._formatterOptions = { ...(seedState?._formatterOptions || {}), ...body };
        data = { saved: true, path: '.etlsql-formatter.json' };
      } else {
        data = seedState?._formatterOptions || {
          keywordCasing: 'upper',
          indentSize: 2,
          commaPlacement: 'leading',
          lineWidth: 100,
          indentJoins: false,
          onClauseOnNewLine: false,
          caseWhenThenNewLine: false,
          breakoutWindowFunctions: false,
          rightAlignKeywords: false,
        };
      }
    } else if (path.endsWith('/api/designer/hover')) {
      const word = String(body.word || '').toUpperCase();
      data = word
        ? { markdown: '#### ' + word + String.fromCharCode(10,10) + 'Sandbox help for ' + word + '.', kind: 'keyword' }
        : { markdown: null, kind: null };
    } else if (path.endsWith('/api/connectors/schema')) {
      // Shape matters: the wizard reads an array, or { schemas: [...] }. Returning an empty or
      // wrongly-keyed payload silently leaves it on its built-in fallback list, which is how the
      // sandbox hid an empty Test Data category.
      data = {
        schemas: [
          {
            connectorType: 'MOCKDB',
            aliases: [],
            description: 'Built-in in-memory sample data. Needs no external database.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [],
          },
          {
            connectorType: 'MSSQL',
            aliases: ['SQLSERVER'],
            description: 'Microsoft SQL Server and Azure SQL Database.',
            isFileBased: false,
            isDataWarehouse: false,
            commandTimeoutSeconds: 30,
            options: [
              { name: 'SERVER', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'localhost' },
              { name: 'DATABASE', type: 0, isMandatory: true, category: 'Basic', defaultValue: 'master' },
            ],
          },
        ],
      };
    } else if (path.endsWith('/api/designer/format') || path.endsWith('/api/format')) {
      const casing = (seedState?._formatterOptions?.keywordCasing || 'upper').toLowerCase();
      let formatted = body.script || '';
      if (casing === 'lower') {
        formatted = formatted.replace(/\b(SELECT|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|GROUP BY|ORDER BY|HAVING|CREATE|CONNECTION|DATASET|VISUAL|PAGE|AS|INTO|BEGIN|TRY|CATCH|MERGE|UPDATE|SET|INSERT|VALUES)\b/g, m => m.toLowerCase());
      } else if (casing === 'upper') {
        formatted = formatted.replace(/\b(select|from|where|join|left|right|inner|outer|group by|order by|having|create|connection|dataset|visual|page|as|into|begin|try|catch|merge|update|set|insert|values)\b/gi, m => m.toUpperCase());
      }
      data = { script: formatted, diagnostics: [] };
    } else if (path.endsWith('/api/git/status')) {
      data = {
        branch: 'main',
        staged: ['etl/staged_load.etlsql'],
        modified: ['etl/weekly_load.etlsql'],
        untracked: ['etl/new_enrichment.etlsql'],
        isGitRepository: true,
      };
    } else if (path.endsWith('/api/git/commit')) {
      data = {
        committed: true,
        sourceRevision: 'c0ffee1',
        message: 'Committed successfully.'
      };
    }

    // Fail closed on an unrecognised path. This used to answer ok:true with an empty body for ANY
    // route, so a client calling a URL the real host does not serve looked healthy here and 404'd in
    // production. That is exactly how Studio shipped pointing at desktop-only routes.
    if (data === UNMATCHED) {
      const message = `[ui-sandbox] No mock for ${init?.method || 'GET'} ${path}. `
        + 'Add a handler in mockApi.js, or fix the caller if the real host does not serve this route.';
      console.error(message);
      return {
        ok: false,
        status: 404,
        json: async () => ({ error: message }),
        text: async () => message,
      };
    }

    return {
      ok: true,
      status: 200,
      json: async () => data,
      text: async () => JSON.stringify(data),
    };
  };
}


function analyzeMockScript(script) {
  const diagnostics = [];
  const lines = String(script || '').split(/\r\n|\r|\n/);
  lines.forEach((line, i) => {
    const selectStar = line.indexOf('SELECT *');
    if (selectStar >= 0) {
      diagnostics.push({
        startLine: i,
        startColumn: selectStar,
        endLine: i,
        endColumn: selectStar + 6,
        severity: 'Warning',
        message: 'Avoid SELECT * in published scripts; list required columns explicitly.',
        code: 'AvoidSelectStar',
        source: 'Sandbox analyzer',
      });
    }
    const badCreate = line.indexOf('CREATE CONNECTION');
    if (badCreate >= 0 && /\bAS\s*;/.test(line)) {
      diagnostics.push({
        startLine: i,
        startColumn: badCreate,
        endLine: i,
        endColumn: line.length,
        severity: 'Error',
        message: 'Expected connector type after AS.',
        code: 'SYNTAX',
        source: 'Sandbox analyzer',
      });
    }
    if (badCreate >= 0 && /\bCREATE\s+CONNECTION\s+\w+\s+ON\b/i.test(line)) {
      diagnostics.push({
        startLine: i,
        startColumn: badCreate,
        endLine: i,
        endColumn: line.length,
        severity: 'Warning',
        message: 'Use CREATE CONNECTION <name> AS <ConnectorType>(...) in current ETL-SQL scripts.',
        code: 'CONNECTION_AS',
        source: 'Sandbox analyzer',
      });
    }
  });
  return diagnostics;
}

function runMockScript(script) {
  const select = extractSelectStatement(String(script || ''));
  if (!select) {
    return {
      columns: [],
      rows: [],
      rowCount: 0,
      capped: false,
      elapsedMs: 0,
      message: 'No SELECT statement to run.',
    };
  }

  const tables = mockSchemaTables();
  const tableName = selectTargetTable(select);
  const table = tables.find((t) => t.name.toLowerCase() === (tableName || '').toLowerCase());
  const columns = resolveSelectColumns(select, table);
  const rows = mockRowsForColumns(columns, table);

  return {
    columns,
    rows,
    rowCount: rows.length,
    capped: false,
    elapsedMs: 18,
    message: `Returned ${rows.length} rows.`,
    trace: mockProgressTrace({ columns, rows, elapsedMs: 18 }, select),
  };
}

// Pull the SELECT to run out of a (possibly multi-statement) script. The designer
// sends the selected/current statement when it can, but when the cursor sits past
// the trailing ';' it falls back to the whole script (e.g. `CREATE CONNECTION m AS
// MOCKDB(); SELECT ...`). Match the real Portal, which executes the whole thing, by
// scanning for the last SELECT rather than requiring the text to start with one.
function extractSelectStatement(text) {
  const statements = text
    .replace(/--[^\n]*/g, ' ') // strip line comments so ';' splitting is clean
    .split(';')
    .map((s) => s.trim())
    .filter(Boolean);
  const selects = statements.filter((s) => /^SELECT\b/i.test(s));
  if (selects.length) return selects[selects.length - 1];
  const bare = text.trim().replace(/;+\s*$/, '');
  return /^SELECT\b/i.test(bare) ? bare : '';
}

function extractDatasetQuery(text, datasetName) {
  const wanted = String(datasetName || '').replace(/^&/, '').toLowerCase();
  const pattern = /CREATE\s+DATASET\s+(&?[A-Za-z_][A-Za-z0-9_]*)\s+AS\s*\(([\s\S]*?)\)\s*;/gi;
  let match;
  while ((match = pattern.exec(String(text || ''))) !== null) {
    if (match[1].replace(/^&/, '').toLowerCase() === wanted) return match[2].trim();
  }
  return '';
}

// Resolve the FROM target, dropping any connection/schema qualifier and alias:
//   `FROM m.Users AS u` -> `Users`
function selectTargetTable(select) {
  const m = /\bFROM\s+([A-Za-z0-9_.[\]"`]+)/i.exec(select);
  if (!m) return '';
  const ref = m[1].replace(/[[\]"`]/g, '');
  const parts = ref.split('.').filter(Boolean);
  return parts[parts.length - 1] || '';
}

// Resolve the projected columns. `*` / `<alias>.*` expands to the table's columns;
// explicit columns keep their name with any `<alias>.` qualifier or trailing `AS`
// alias reduced to a plain identifier.
function resolveSelectColumns(select, table) {
  const m = /^SELECT\s+(?:DISTINCT\s+|TOP\s+\d+\s+)*([\s\S]+?)\s+FROM\b/i.exec(select);
  const list = (m ? m[1] : '*').trim();
  if (!list || /(^|,)\s*[A-Za-z0-9_]*\.?\*/.test(list)) {
    return table ? table.columns.map((c) => c.name) : ['col1', 'col2', 'col3'];
  }
  const cols = splitTopLevel(list)
    .map((part) => {
      const asMatch = /\s+AS\s+([A-Za-z0-9_]+)\s*$/i.exec(part);
      let name = asMatch ? asMatch[1] : part.trim();
      name = name.replace(/^[A-Za-z0-9_]+\./, ''); // drop `alias.` qualifier
      return name.trim();
    })
    .filter(Boolean);
  return cols.length ? cols : (table ? table.columns.map((c) => c.name) : ['col1']);
}

// Split a projection list on top-level commas, ignoring commas inside parens (e.g. fn(a, b)).
function splitTopLevel(list) {
  const out = [];
  let depth = 0;
  let cur = '';
  for (const ch of list) {
    if (ch === '(') depth++;
    else if (ch === ')') depth = Math.max(0, depth - 1);
    if (ch === ',' && depth === 0) { out.push(cur); cur = ''; } else cur += ch;
  }
  if (cur.trim()) out.push(cur);
  return out;
}

// Synthesize three representative rows, typing values by the mock schema when known.
function mockRowsForColumns(columns, table) {
  const typeOf = (name) => {
    const col = table?.columns?.find((c) => c.name.toLowerCase() === name.toLowerCase());
    return (col?.type || '').toUpperCase();
  };
  const sample = (name, type, i) => {
    if (/INT/.test(type)) return (i + 1) * 10 + i;
    if (/DEC|NUM|FLOAT|MONEY|REAL/.test(type)) return Math.round((1000 + i * 137.5) * 100) / 100;
    if (/DATE|TIME/.test(type)) return `2026-0${i + 1}-1${i}`;
    if (/BOOL|BIT/.test(type)) return i % 2 === 0;
    return `${name}-${i + 1}`;
  };
  return [0, 1, 2].map((i) => {
    const row = {};
    for (const name of columns) row[name] = sample(name, typeOf(name), i);
    return row;
  });
}

function mockProgressTrace(result, script) {
  const rows = Array.isArray(result?.rows) ? result.rows : [];
  const elapsed = Number.isFinite(result?.elapsedMs) ? result.elapsedMs : 18;
  return [
    { type: 'clear', resetHistory: true },
    { type: 'status', status: 'running' },
    { type: 'message', text: 'Sandbox run started.', level: 'sys' },
    { type: 'progress', data: [
      { id: '1', name: 'Parse current statement', status: 'Completed', rowsProcessed: 0, durationMs: 2, isParallelBlock: false, children: [] },
      { id: '2', name: 'Execute SELECT', status: 'Running', rowsProcessed: 0, durationMs: 0, isParallelBlock: false, children: [] },
    ]},
    { type: 'message', text: String(script || '').trim().replace(/\s+/g, ' ').slice(0, 160), level: 'info' },
    { type: 'progress', data: [
      { id: '1', name: 'Parse current statement', status: 'Completed', rowsProcessed: 0, durationMs: 2, isParallelBlock: false, children: [] },
      { id: '2', name: 'Execute SELECT', status: 'Completed', rowsProcessed: rows.length, durationMs: elapsed, isParallelBlock: false, children: [] },
    ]},
    { type: 'results', columns: result.columns || [], rows },
    { type: 'performance', metrics: {
      executionMs: elapsed,
      rowsProcessed: rows.length,
      memoryMb: 1.4,
      statements: [
        { type: 'SELECT', totalMs: elapsed },
      ],
    }},
    { type: 'done', exitCode: 0 },
  ];
}

function mockSchemaTables() {
  return [
    {
      name: 'Users',
      columns: [
        { name: 'UserId', type: 'INT' },
        { name: 'Id', type: 'INT' },
        { name: 'Name', type: 'VARCHAR' },
        { name: 'Email', type: 'VARCHAR' },
        { name: 'Region', type: 'VARCHAR' },
      ],
    },
    {
      name: 'Customers',
      columns: [
        { name: 'CustomerId', type: 'INT' },
        { name: 'CustomerName', type: 'VARCHAR' },
        { name: 'Region', type: 'VARCHAR' },
        { name: 'Segment', type: 'VARCHAR' },
      ],
    },
    {
      name: 'Orders',
      columns: [
        { name: 'OrderId', type: 'INT' },
        { name: 'CustomerId', type: 'INT' },
        { name: 'Amount', type: 'DECIMAL' },
        { name: 'OrderDate', type: 'DATE' },
      ],
    },
    {
      name: 'Products',
      columns: [
        { name: 'ProductId', type: 'INT' },
        { name: 'Sku', type: 'VARCHAR' },
        { name: 'Category', type: 'VARCHAR' },
        { name: 'UnitPrice', type: 'DECIMAL' },
      ],
    },
    {
      name: 'Sales',
      columns: [
        { name: 'SaleId', type: 'INT' },
        { name: 'OrderId', type: 'INT' },
        { name: 'ProductId', type: 'INT' },
        { name: 'Revenue', type: 'DECIMAL' },
        { name: 'SaleDate', type: 'DATE' },
      ],
    },
  ];
}

const CONNECTOR_COMPLETIONS = [
  ['MOCKDB()', 'In-memory test database'],
  ['MSSQL(SERVER = \'\', DATABASE = \'\', TRUSTED_CONNECTION = TRUE)', 'SQL Server connection'],
  ['POSTGRES(HOST = \'\', DATABASE = \'\', USER = \'\', PASSWORD = \'\')', 'PostgreSQL connection'],
  ['FLATFILE(\'data.csv\', HEADER = \'ON\')', 'Delimited file connection'],
  ['CSV(\'data.csv\', HEADER = \'ON\')', 'CSV file connection'],
  ['JSON(PATH = \'data.json\')', 'JSON file connection'],
  ['EXCEL(PATH = \'workbook.xlsx\')', 'Excel workbook connection'],
  ['SFTP(HOST = \'\', USER = \'\', KEYFILE = \'\')', 'SFTP connection'],
  ['REST(BASE_URL = \'\')', 'REST API connection'],
  ['PORTAL(BASE_URL = \'\')', 'Portal connection'],
  ['ORCHESTRATOR(BASE_URL = \'\')', 'Orchestrator connection'],
];

function completionItem(label, kind, detail = kind, documentation = null, insertText = null, startColumn = null, endColumn = null) {
  const item = { label, insertText: insertText || label, kind, detail, documentation };
  if (Number.isFinite(startColumn)) item.startColumn = startColumn;
  if (Number.isFinite(endColumn)) item.endColumn = endColumn;
  return item;
}

function uniqueItems(items) {
  const seen = new Set();
  return items.filter(item => {
    const key = String(item.label).toLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function matchesPrefix(item, prefix) {
  if (!prefix) return true;
  const lower = prefix.toLowerCase();
  const label = String(item.label || '').toLowerCase();
  return label.startsWith(lower) || label.split('.').pop().startsWith(lower);
}

function connectionAliases(script, connectionRef) {
  const aliases = new Set();
  if (connectionRef) aliases.add(String(connectionRef));
  const re = /\bCREATE\s+CONNECTION\s+([A-Za-z_][\w]*)\s+(?:AS|ON)\s+([A-Za-z_][\w]*)/ig;
  let match;
  while ((match = re.exec(String(script || ''))) !== null) {
    aliases.add(match[1]);
  }
  return [...aliases];
}

function tableForName(name, schema) {
  return schema.find(t => t.name.toLowerCase() === String(name || '').toLowerCase()) || null;
}

function tableAliases(script, schema) {
  const aliases = new Map();
  const re = /\b(?:FROM|JOIN)\s+(?:([A-Za-z_][\w]*)\.)?([A-Za-z_][\w#&$]*)(?:\s+(?:AS\s+)?([A-Za-z_][\w]*))?/ig;
  let match;
  while ((match = re.exec(String(script || ''))) !== null) {
    const table = tableForName(match[2], schema);
    const alias = match[3];
    if (table && alias && !/^(WHERE|JOIN|LEFT|RIGHT|FULL|INNER|OUTER|ON|GROUP|ORDER|HAVING|LIMIT|UNION)$/i.test(alias)) {
      aliases.set(alias.toLowerCase(), table);
    }
  }
  return aliases;
}

function statementBeforeCursor(script, line, column) {
  const lines = String(script || '').split(/\r\n|\r|\n/);
  const safeLine = Math.max(0, Math.min(lines.length - 1, Number(line) || 0));
  const current = lines[safeLine] || '';
  const beforeCurrent = current.slice(0, Math.max(0, Math.min(current.length, Number(column) || 0)));
  const before = [...lines.slice(0, safeLine), beforeCurrent].join('\n');
  const lastSemi = before.lastIndexOf(';');
  return lastSemi >= 0 ? before.slice(lastSemi + 1) : before;
}

function statementAroundCursor(script, line, column) {
  const lines = String(script || '').split(/\r\n|\r|\n/);
  const safeLine = Math.max(0, Math.min(lines.length - 1, Number(line) || 0));
  const current = lines[safeLine] || '';
  const beforeCurrent = current.slice(0, Math.max(0, Math.min(current.length, Number(column) || 0)));
  const absoluteBefore = [...lines.slice(0, safeLine), beforeCurrent].join('\n');
  const pos = absoluteBefore.length;
  const text = String(script || '');
  let start = text.lastIndexOf(';', Math.max(0, pos - 1));
  let end = text.indexOf(';', pos);
  start = start < 0 ? 0 : start + 1;
  end = end < 0 ? text.length : end;
  return text.slice(start, end);
}

function connectorItems(prefix) {
  return CONNECTOR_COMPLETIONS
    .map(([snippet, doc]) => completionItem(snippet.replace(/\(.*/, ''), 'connector', 'Connector', doc, snippet))
    .filter(item => matchesPrefix(item, prefix));
}

function tableItems(qualifier, schema, rest) {
  return schema
    .filter(t => t.name.toLowerCase().startsWith(String(rest || '').toLowerCase()))
    .map(t => completionItem(`${qualifier}.${t.name}`, 'table', 'Table'));
}

function columnItems(qualifier, table, rest) {
  if (!table) return [];
  return table.columns
    .filter(c => c.name.toLowerCase().startsWith(String(rest || '').toLowerCase()))
    .map(c => completionItem(`${qualifier}.${c.name}`, 'column', c.type));
}

function starExpansionItem(tableAliasLookup, table, column) {
  const aliasEntry = [...tableAliasLookup.entries()][0] || null;
  const qualifier = aliasEntry?.[0] || '';
  const sourceTable = aliasEntry?.[1] || table;
  if (!sourceTable?.columns?.length) return null;
  const columns = sourceTable.columns
    .map(c => qualifier ? `${qualifier}.${c.name}` : c.name)
    .join(', ');
  const endColumn = Number(column) || 0;
  const startColumn = Math.max(0, endColumn - 1);
  return completionItem('Expand * to columns', 'snippet', 'Column expansion', 'Replace * with explicit column names.', columns, startColumn, endColumn);
}

function completeMockScript(script, line, column, connectionRef) {
  const lines = String(script || '').split(/\r\n|\r|\n/);
  const current = lines[Math.max(0, Math.min(lines.length - 1, Number(line) || 0))] || '';
  const before = current.slice(0, Math.max(0, Math.min(current.length, Number(column) || 0)));
  const prefix = (before.match(/([\w@#&$.*()]+)$/) || [])[1] || '';
  const upperBefore = before.toUpperCase();
  const schema = mockSchemaTables();
  const aliases = connectionAliases(script, connectionRef || 'demo');
  const aliasLookup = new Set(aliases.map(a => a.toLowerCase()));
  const statement = statementAroundCursor(script, line, column);
  const beforeStatement = statementBeforeCursor(script, line, column);
  const tableAliasLookup = tableAliases(statement, schema);

  if (/\bCREATE\s+CONNECTION\s+[A-Za-z_][\w]*\s*$/i.test(before)) {
    return [completionItem('AS', 'keyword', 'Keyword')];
  }

  if (/\bCREATE\s+CONNECTION\s+[A-Za-z_][\w]*\s+AS\s+[A-Za-z_]*$/i.test(before)) {
    return connectorItems(prefix);
  }

  if (prefix.includes('.')) {
    const parts = prefix.split('.');
    if (parts.length === 2) {
      const [qualifier, rest = ''] = parts;
      if (aliasLookup.has(qualifier.toLowerCase())) {
        return tableItems(qualifier, schema, rest);
      }
      const aliasedTable = tableAliasLookup.get(qualifier.toLowerCase());
      if (aliasedTable) {
        return columnItems(qualifier, aliasedTable, rest);
      }
      return [];
    }
    if (parts.length === 3) {
      const [connection, tableName, rest = ''] = parts;
      if (aliasLookup.has(connection.toLowerCase())) {
        const table = tableForName(tableName, schema);
        return columnItems(`${connection}.${tableName}`, table, rest);
      }
    }
    return [];
  }

  if (/\b(FROM|JOIN|UPDATE|INTO)\s+[\w@#&$]*$/i.test(upperBefore)) {
    return uniqueItems([
      ...aliases.map(alias => completionItem(alias, 'connection', 'Connection')),
      ...schema.map(t => completionItem(t.name, 'table', 'Table')),
      ...aliases.flatMap(alias => schema.map(t => completionItem(`${alias}.${t.name}`, 'table', 'Table'))),
    ]).filter(i => matchesPrefix(i, prefix));
  }

  const firstTable = (statement.match(/\bFROM\s+(?:([A-Za-z_][\w]*)\.)?([A-Za-z_][\w]*)/i) || [])[2];
  const table = tableForName(firstTable, schema);
  if (prefix === '*') {
    const expansion = starExpansionItem(tableAliasLookup, table, column);
    return expansion ? [expansion] : [];
  }
  if (/\bSELECT\b/i.test(beforeStatement) && !/\bFROM\s+[\w.]*$/i.test(beforeStatement)) {
    const columns = table ? table.columns.map(c => completionItem(c.name, 'column', c.type)) : [];
    const aliasColumns = [...tableAliasLookup.entries()].flatMap(([alias, aliasedTable]) =>
      aliasedTable.columns.map(c => completionItem(`${alias}.${c.name}`, 'column', c.type)));
    return uniqueItems([
      ...columns,
      ...aliasColumns,
      completionItem('COUNT(*)', 'function', 'Function'),
      completionItem('SUM()', 'function', 'Function'),
      completionItem('TRY_CAST()', 'function', 'Function'),
      completionItem('COALESCE()', 'function', 'Function'),
    ]).filter(i => matchesPrefix(i, prefix));
  }

  return [
    completionItem('SELECT', 'keyword'),
    completionItem('FROM', 'keyword'),
    completionItem('WHERE', 'keyword'),
    completionItem('JOIN', 'keyword'),
    completionItem('CREATE', 'keyword'),
    completionItem('CONNECTION', 'keyword'),
    completionItem('AS', 'keyword'),
    ...connectorItems(prefix),
    completionItem('SUM', 'function'),
    completionItem('COUNT', 'function'),
  ].filter(i => matchesPrefix(i, prefix));
}

function sanitizeName(name, id) {
  const input = name || id || 'visual1';
  let safe = input.trim().replace(/[^a-zA-Z0-9_]/g, '_');
  if (!/^[a-zA-Z]/.test(safe)) safe = 'v_' + safe;
  return safe;
}

function getSlotLetter(index) {
  let letter = '';
  let idx = index;
  while (idx >= 0) {
    letter = String.fromCharCode(65 + (idx % 26)) + letter;
    idx = Math.floor(idx / 26) - 1;
  }
  return letter;
}

function buildStructure(visuals) {
  if (!visuals || visuals.length === 0) return '.';
  const maxRow = Math.max(...visuals.map(v => (v.gridRow || 1) + (v.gridRowSpan || 4) - 1));
  const maxCol = Math.max(...visuals.map(v => (v.gridCol || 1) + (v.gridColSpan || 12) - 1));
  const usedCols = Math.min(12, maxCol);

  const grid = Array.from({ length: maxRow }, () => Array(usedCols).fill('.'));

  visuals.forEach((v, index) => {
    const slot = getSlotLetter(index);
    const startRow = (v.gridRow || 1) - 1;
    const endRow = startRow + (v.gridRowSpan || 4);
    const startCol = (v.gridCol || 1) - 1;
    const endCol = startCol + (v.gridColSpan || 12);

    for (let r = startRow; r < endRow && r < maxRow; r++) {
      for (let c = startCol; c < endCol && c < usedCols; c++) {
        grid[r][c] = slot;
      }
    }
  });

  // 1. Compress horizontal contiguous identical slot cells per row
  const compressedRows = grid.map(row => {
    const rowSlots = [];
    row.forEach(slot => {
      if (rowSlots.length === 0 || rowSlots[rowSlots.length - 1] !== slot) {
        rowSlots.push(slot);
      }
    });
    return rowSlots.join(' ');
  });

  // 2. Deduplicate consecutive identical rows vertically
  const dedupedRows = [];
  compressedRows.forEach(rowStr => {
    if (dedupedRows.length === 0 || dedupedRows[dedupedRows.length - 1] !== rowStr) {
      dedupedRows.push(rowStr);
    }
  });

  return dedupedRows.join(' / ');
}

function generateMockScript(state) {
  const out = ['-- generated by the sandbox mock (not the real DesignerController)'];
  for (const ds of (state?.datasets ?? [])) {
    const name = ds.name.startsWith('&') ? ds.name : '&' + ds.name;
    out.push(`CREATE DATASET ${name} AS (\n  ${ds.query}\n);`);
  }
  for (const p of (state?.pages ?? [])) {
    out.push('');
    for (const v of (p.visuals ?? [])) {
      const vName = sanitizeName(v.name, v.id);
      if (v.type === 'CONTAINER') {
        const containerType = v.options?.CONTAINER_TYPE || 'BOX';
        out.push(`CREATE CONTAINER ${vName} AS ${containerType.toUpperCase()} (\n    TITLE = '${v.title || ''}',\n);`);
      } else if (v.type === 'BUTTON') {
        const buttonType = v.options?.BUTTON_TYPE || 'REFRESH';
        out.push(`CREATE BUTTON ${vName} AS (\n    TITLE = '${v.title || ''}',\n    OPTIONS (BUTTON_TYPE = '${buttonType}'),\n);`);
      } else {
        const maps = Object.entries(v.mappings ?? {})
          .filter(([_, c]) => c)
          .map(([k, c]) => `${k} = ${c}`)
          .join(', ');
        const dsName = v.dataset ? (v.dataset.startsWith('&') ? v.dataset : '&' + v.dataset) : '&sales';
        const layoutOpts = [];
        if (v.gridColSpan && v.gridColSpan !== 12) layoutOpts.push(`COLSPAN = ${v.gridColSpan}`);
        if (v.gridRowSpan && v.gridRowSpan !== 4) layoutOpts.push(`ROWSPAN = ${v.gridRowSpan}`);
        if (v.width || v.options?.WIDTH) layoutOpts.push(`WIDTH = '${v.width || v.options.WIDTH}'`);
        if (v.height || v.options?.HEIGHT) layoutOpts.push(`HEIGHT = '${v.height || v.options.HEIGHT}'`);
        const layoutClause = layoutOpts.length ? `,\n    LAYOUT (${layoutOpts.join(', ')})` : '';

        out.push(`CREATE VISUAL ${vName} AS ${v.type} (\n    SOURCE = ${dsName}${maps ? `,\n    MAPPINGS (${maps})` : ''},\n    TITLE = '${v.title || v.name}'${layoutClause}\n);`);
      }
    }
    const structure = buildStructure(p.visuals);
    const mapEntries = (p.visuals ?? []).map((v, index) => {
      const slot = getSlotLetter(index);
      const vName = sanitizeName(v.name, v.id);
      return `            '${slot}' = ${vName}`;
    }).join(',\n');

    out.push(`CREATE PAGE [${sanitizeName(p.name, p.id)}] AS DASHBOARD (\n    LAYOUT (\n        STRUCTURE = '${structure}',\n        MAP (\n${mapEntries}\n        )\n    )\n);`);
  }
  return out.join('\n');
}

// --- Mock script patching -------------------------------------------------
//
// The real POST /api/designer/patch is a lossless span patcher (DesignerScriptPatcher) that edits
// only the clauses the designer changed. This mock is a deliberately simpler text reconciliation:
// it adds, removes, and rewrites the statements the Studio workflows create so that a click which
// writes SQL is actually *visible* in the sandbox.
//
// This used to echo `body.script` straight back. That made every canvas mutation — add a visual,
// add a parameter, add detail bands — look like a dead button, because the sandbox handed back the
// script it was given and Studio faithfully rendered no change.

function mockPatchScript(script, state) {
  let out = script;
  out = mockPatchParameters(out, state?.parameters);
  out = mockPatchDatasets(out, state?.datasets);
  out = mockPatchVisuals(out, state);
  out = mockPatchPages(out, state);
  return out;
}

// Finds `KEYWORD ( ... )` by balancing parentheses, so a nested clause does not end the span early.
function mockFindClause(text, keyword, from = 0) {
  const pattern = new RegExp('\\b' + keyword + '\\s*\\(', 'gi');
  pattern.lastIndex = from;
  const match = pattern.exec(text);
  if (!match) return null;
  let depth = 0;
  for (let i = match.index + match[0].length - 1; i < text.length; i++) {
    if (text[i] === '(') depth++;
    else if (text[i] === ')') {
      depth--;
      if (depth === 0) return { start: match.index, end: i + 1, text: text.slice(match.index, i + 1) };
    }
  }
  return null;
}

// The offset just past `CREATE ... ;` starting at `start`, honouring nested parentheses.
function mockStatementEnd(text, start) {
  let depth = 0;
  for (let i = start; i < text.length; i++) {
    if (text[i] === '(') depth++;
    else if (text[i] === ')') depth--;
    else if (text[i] === ';' && depth <= 0) return i + 1;
  }
  return text.length;
}

function mockNormalizeName(name) {
  return String(name || '').replace(/^&/, '').toLowerCase();
}

function mockDeclarationText(parameter) {
  const name = parameter.name.startsWith('@') ? parameter.name : '@' + parameter.name;
  const initial = parameter.initialValue ? ' = ' + parameter.initialValue : '';
  const flags = [
    parameter.isSensitive ? 'PASSWORD' : '',
    parameter.isInput ? 'INPUT' : '',
    parameter.isOutput ? 'OUTPUT' : '',
    parameter.isRequired ? 'REQUIRED' : '',
  ].filter(Boolean).join(' ');
  return 'DECLARE ' + name + ' ' + (parameter.dataType || 'VARCHAR') + initial + (flags ? ' ' + flags : '') + ';';
}

function mockPatchParameters(script, parameters) {
  if (!Array.isArray(parameters)) return script;
  let out = script;

  const existing = new Map();
  const declarePattern = /^[ \t]*DECLARE\s+(@[A-Za-z_][A-Za-z0-9_]*)[^;]*;/gim;
  let match;
  while ((match = declarePattern.exec(out)) !== null) {
    existing.set(match[1].toLowerCase(), { start: match.index, end: match.index + match[0].length, text: match[0] });
  }

  const byName = new Map(parameters.map(parameter => [
    (parameter.name.startsWith('@') ? parameter.name : '@' + parameter.name).toLowerCase(),
    parameter,
  ]));

  // Rewrite changed declarations and drop removed ones, back-to-front so earlier offsets stay valid.
  // This used to only add and remove, so editing a parameter's default or type was a silent no-op in
  // the sandbox while the real patcher rewrote it — the sandbox reporting a working surface as broken.
  const edits = [];
  for (const [name, span] of existing) {
    const desired = byName.get(name);
    if (!desired) {
      let end = span.end;
      while (end < out.length && /[ \t]/.test(out[end])) end++;
      if (out.slice(end, end + 2) === '\r\n') end += 2;
      else if (out[end] === '\n') end += 1;
      edits.push({ start: span.start, end, text: '' });
      continue;
    }
    const replacement = mockDeclarationText(desired);
    if (replacement !== span.text.trim()) edits.push({ start: span.start, end: span.end, text: replacement });
  }
  for (const edit of edits.sort((a, b) => b.start - a.start)) {
    out = out.slice(0, edit.start) + edit.text + out.slice(edit.end);
  }

  const additions = parameters
    .filter(parameter => !existing.has((parameter.name.startsWith('@') ? parameter.name : '@' + parameter.name).toLowerCase()))
    .map(mockDeclarationText);

  return additions.length ? additions.join('\n') + '\n\n' + out : out;
}

function mockPatchDatasets(script, datasets) {
  if (!Array.isArray(datasets)) return script;
  let out = script;

  const existing = new Map();
  const pattern = /CREATE\s+DATASET\s+(&?[A-Za-z_][A-Za-z0-9_]*)\s+AS\s*\(/gi;
  let match;
  while ((match = pattern.exec(out)) !== null) {
    const end = mockStatementEnd(out, match.index);
    existing.set(mockNormalizeName(match[1]), { start: match.index, end });
    pattern.lastIndex = end;
  }

  const desired = new Set(datasets.map(d => mockNormalizeName(d.name)));
  const removals = [...existing.entries()].filter(([name]) => !desired.has(name)).map(([, span]) => span);
  for (const span of removals.sort((a, b) => b.start - a.start)) {
    out = out.slice(0, span.start) + out.slice(span.end).replace(/^\r?\n\r?\n/, '\n');
  }

  const additions = datasets
    .filter(d => !existing.has(mockNormalizeName(d.name)))
    .map(d => {
      const name = String(d.name).startsWith('&') ? d.name : '&' + d.name;
      const query = String(d.query || 'SELECT 1 AS Placeholder').trim().replace(/;$/, '');
      return 'CREATE DATASET ' + name + ' AS (\n  ' + query + '\n);';
    });
  if (!additions.length) return out;

  const insertAt = mockFirstPresentationOffset(out);
  return out.slice(0, insertAt) + additions.join('\n\n') + '\n\n' + out.slice(insertAt);
}

// Datasets and visuals are declared before the presentation statements that consume them.
function mockFirstPresentationOffset(script) {
  const match = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?(?:VISUAL|CONTAINER|BUTTON|PAGE)\b/i.exec(script);
  return match ? match.index : script.length;
}

function mockVisualStatement(visual) {
  const name = sanitizeName(visual.name, visual.id);
  const source = visual.options?.inline_source
    || (visual.dataset ? (String(visual.dataset).startsWith('&') ? visual.dataset : '&' + visual.dataset) : null);
  const clauses = [];
  if (source) clauses.push('    SOURCE = ' + source);
  const maps = Object.entries(visual.mappings ?? {}).filter(([, column]) => column).map(([role, column]) => role + ' = ' + column);
  if (maps.length) clauses.push('    MAPPINGS (' + maps.join(', ') + ')');
  if (visual.title) clauses.push("    TITLE = '" + String(visual.title).replace(/'/g, "''") + "'");
  const options = mockOptionsClause(visual.options);
  if (options) clauses.push('    ' + options);
  if (visual.options?.print_layout) clauses.push('    ' + visual.options.print_layout);
  return 'CREATE VISUAL ' + name + ' AS ' + visual.type + ' (\n' + clauses.join(',\n') + '\n);';
}

// `inline_source` and `print_layout` are carried as their own clauses, not as OPTIONS entries.
function mockOptionsClause(options) {
  const entries = Object.entries(options ?? {}).filter(([key]) => key !== 'inline_source' && key !== 'print_layout');
  if (!entries.length) return '';
  const body = entries.map(([key, value]) => key + " = '" + String(value).replace(/'/g, "''") + "'").join(', ');
  return 'OPTIONS (' + body + ')';
}

function mockPatchVisuals(script, state) {
  const visuals = (state?.pages ?? []).flatMap(page => page.visuals ?? []);
  let out = script;

  const existing = new Map();
  const pattern = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?VISUAL\s+([A-Za-z_][A-Za-z0-9_]*)\s+AS\s+/gi;
  let match;
  while ((match = pattern.exec(out)) !== null) {
    const end = mockStatementEnd(out, match.index);
    existing.set(match[1].toLowerCase(), { start: match.index, end });
    pattern.lastIndex = end;
  }

  const desired = new Map(visuals.map(visual => [sanitizeName(visual.name, visual.id).toLowerCase(), visual]));

  // Rewrite existing visuals back-to-front, then delete dropped ones, so offsets stay valid.
  const edits = [];
  for (const [name, span] of existing) {
    const original = out.slice(span.start, span.end);
    if (!desired.has(name)) {
      edits.push({ start: span.start, end: span.end, text: '' });
      continue;
    }
    const rewritten = mockRewriteVisualClauses(original, desired.get(name));
    if (rewritten !== original) edits.push({ start: span.start, end: span.end, text: rewritten });
  }
  for (const edit of edits.sort((a, b) => b.start - a.start)) {
    out = out.slice(0, edit.start) + edit.text + out.slice(edit.end);
  }

  const additions = visuals
    .filter(visual => !existing.has(sanitizeName(visual.name, visual.id).toLowerCase()))
    .map(mockVisualStatement);
  if (!additions.length) return out;

  const pageMatch = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?PAGE\b/i.exec(out);
  const insertAt = pageMatch ? pageMatch.index : out.length;
  const prefix = insertAt === out.length && out.length && !out.endsWith('\n') ? '\n\n' : '';
  return out.slice(0, insertAt) + prefix + additions.join('\n\n') + '\n\n' + out.slice(insertAt);
}

// Only the clauses the Studio workflows write are reconciled; everything else the author typed stays.
function mockRewriteVisualClauses(statement, visual) {
  let out = statement;
  out = mockReplaceClause(out, 'OPTIONS', mockOptionsClause(visual.options));
  out = mockReplaceClause(out, 'PRINT_LAYOUT', visual.options?.print_layout || '');
  return out;
}

function mockReplaceClause(statement, keyword, replacement) {
  const existing = mockFindClause(statement, keyword);
  if (!existing) {
    if (!replacement) return statement;
    const close = statement.lastIndexOf(')');
    if (close < 0) return statement;
    let previous = close - 1;
    while (previous >= 0 && /\s/.test(statement[previous])) previous--;
    const separator = previous >= 0 && statement[previous] !== '(' && statement[previous] !== ',' ? ',' : '';
    return statement.slice(0, previous + 1) + separator + '\n    ' + replacement + '\n' + statement.slice(close);
  }
  if (!replacement) {
    let end = existing.end;
    while (end < statement.length && /\s/.test(statement[end])) end++;
    if (statement[end] === ',') end++;
    return statement.slice(0, existing.start) + statement.slice(end);
  }
  return statement.slice(0, existing.start) + replacement + statement.slice(existing.end);
}

function mockPatchPages(script, state) {
  const pages = state?.pages ?? [];
  if (!pages.length) return script;
  let out = script;

  const pattern = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?PAGE\s+(?:\[([^\]]+)\]|([A-Za-z_][A-Za-z0-9_]*))\s+AS\s+(DASHBOARD|PAGINATED)/gi;
  const spans = [];
  let match;
  while ((match = pattern.exec(out)) !== null) {
    const end = mockStatementEnd(out, match.index);
    spans.push({ start: match.index, end });
    pattern.lastIndex = end;
  }

  for (let index = spans.length - 1; index >= 0; index--) {
    const page = pages[index];
    if (!page) continue;
    const span = spans[index];
    const rewritten = mockRewritePageClauses(out.slice(span.start, span.end), page);
    out = out.slice(0, span.start) + rewritten + out.slice(span.end);
  }
  return out;
}

function mockRewritePageClauses(statement, page) {
  const visuals = page.visuals ?? [];
  let out = statement;

  const structure = buildStructure(visuals);
  out = out.replace(/STRUCTURE\s*=\s*'(?:[^']|'')*'/i, "STRUCTURE = '" + structure.replace(/'/g, "''") + "'");

  // A page with no visuals has nothing to map, so it must not gain an empty MAP () clause.
  const mapEntries = visuals
    .map((visual, index) => "            '" + getSlotLetter(index) + "' = " + sanitizeName(visual.name, visual.id))
    .join(',\n');
  const layout = mockFindClause(out, 'LAYOUT');
  if (layout) {
    const rewrittenLayout = mockReplaceClause(
      layout.text,
      'MAP',
      mapEntries ? 'MAP (\n' + mapEntries + '\n        )' : '');
    out = out.slice(0, layout.start) + rewrittenLayout + out.slice(layout.end);
  }

  const print = page.printLayout;
  if (print) {
    const options = [];
    if (print.pageSize) options.push("PAGE_SIZE = '" + print.pageSize + "'");
    if (print.orientation) options.push("ORIENTATION = '" + print.orientation + "'");
    if (print.marginTop != null) {
      const t = print.marginTop;
      options.push('MARGINS = (' + t + ', ' + (print.marginRight ?? t) + ', ' + (print.marginBottom ?? t) + ', ' + (print.marginLeft ?? t) + ')');
    }
    if (print.units) options.push("UNITS = '" + print.units + "'");
    if (print.overflow) options.push("OVERFLOW = '" + print.overflow + "'");
    out = mockReplaceClause(out, 'PRINT_LAYOUT', options.length ? 'PRINT_LAYOUT (' + options.join(', ') + ')' : '');
  }
  return out;
}

// Extracts the visuals a script actually declares, rather than echoing the seed state. The mock
// parse used to hand back the fixture's visuals whenever the script mentioned CREATE VISUAL at all,
// which meant anything the canvas wrote was invisible to every reader of the design state — the
// workflow checklist, the report tree, and the inspector all described the fixture, not the script.
function mockParseVisuals(script) {
  const visuals = [];
  const pattern = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?VISUAL\s+([A-Za-z_][A-Za-z0-9_]*)\s+AS\s+([A-Za-z_]+)\s*\(/gi;
  let match;
  while ((match = pattern.exec(script)) !== null) {
    const end = mockStatementEnd(script, match.index);
    const body = script.slice(match.index, end);
    pattern.lastIndex = end;

    const options = {};
    const source = /\bSOURCE\s*=\s*([^,\n]+)/i.exec(body)?.[1]?.trim();
    const dataset = source && source.startsWith('&') ? source : null;
    if (source && !dataset) options.inline_source = source;

    const optionsClause = mockFindClause(body, 'OPTIONS');
    if (optionsClause) {
      const inner = optionsClause.text.slice(optionsClause.text.indexOf('(') + 1, -1);
      for (const entry of inner.split(',')) {
        const [key, ...rest] = entry.split('=');
        if (!key || !rest.length) continue;
        options[key.trim()] = rest.join('=').trim().replace(/^'|'$/g, '');
      }
    }
    const printLayout = mockFindClause(body, 'PRINT_LAYOUT');
    if (printLayout) options.print_layout = printLayout.text;
    const textDefault = /\bDEFAULT\s*=\s*('(?:[^']|'')*')/i.exec(body)?.[1];
    if (textDefault) options.text_default = textDefault;

    const mappings = {};
    const mappingClause = mockFindClause(body, 'MAPPINGS');
    if (mappingClause) {
      const inner = mappingClause.text.slice(mappingClause.text.indexOf('(') + 1, -1);
      inner.split(',').forEach((entry, index) => {
        const parts = entry.split('=');
        if (parts.length >= 2) mappings[parts[0].trim().toUpperCase()] = parts.slice(1).join('=').trim();
        else if (entry.trim()) mappings[`COLUMN${index + 1}`] = entry.trim();
      });
    }

    const type = match[2].toUpperCase();
    const wide = type === 'TABLE' || type === 'MATRIX' || type === 'TEXT';
    visuals.push({
      id: `v_${match[1]}_${visuals.length}`,
      name: match[1],
      type,
      // The script carries no grid coordinates; they live in the page's LAYOUT STRUCTURE. Stacking
      // them is enough for the design state to be read back correctly.
      gridCol: 1,
      gridRow: visuals.reduce((row, visual) => row + visual.gridRowSpan, 1),
      gridColSpan: wide ? 12 : 6,
      gridRowSpan: type === 'TEXT' ? 2 : type === 'CARD' ? 2 : 4,
      title: /\bTITLE\s*=\s*'((?:[^']|'')*)'/i.exec(body)?.[1]?.replace(/''/g, "'") || null,
      dataset,
      mappings,
      options,
    });
  }
  return visuals;
}

function mockParseParameters(script) {
  const parameters = [];
  // The type may be sized — VARCHAR(50) — and dropping the size made the sandbox show a
  // truncated type the real parser preserves.
  const pattern = /^[ \t]*DECLARE\s+(@[A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z]+(?:\s*\([^)]*\))?)([^;]*);/gim;
  let match;
  while ((match = pattern.exec(script)) !== null) {
    const tail = match[3] || '';
    parameters.push({
      name: match[1],
      dataType: match[2].toUpperCase(),
      initialValue: /=\s*(.+?)(?:\s+(?:INPUT|OUTPUT|REQUIRED|PASSWORD)\b|\s*$)/i.exec(tail)?.[1]?.trim() || null,
      isInput: /\bINPUT\b/i.test(tail),
      isOutput: /\bOUTPUT\b/i.test(tail),
      isRequired: /\bREQUIRED\b/i.test(tail),
      isSensitive: /\bPASSWORD\b/i.test(tail),
    });
  }
  return parameters;
}
