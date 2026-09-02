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
//   POST /api/designer/pipeline-task {script,op,...} -> { applied, script, error, tasks }
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

    // The export step asks for a PDF. The bytes are a stub — what the sandbox can prove is that
    // Studio posted the script it is showing and handed a file to the reader; whether the exporter
    // paginates correctly is settled where the exporter lives, not here.
    if (path.endsWith('/api/designer/preview/pdf')) {
      const pdf = new TextEncoder().encode('%PDF-1.4\n% ui-sandbox stub\n');
      return {
        ok: true,
        status: 200,
        headers: { get: name => (String(name).toLowerCase() === 'content-type' ? 'application/pdf' : null) },
        blob: async () => new Blob([pdf], { type: 'application/pdf' }),
        arrayBuffer: async () => pdf.buffer,
        json: async () => ({ error: 'The export returned a PDF, not JSON.' }),
        text: async () => '%PDF-1.4',
      };
    }

    let data = UNMATCHED;
    if (path.endsWith('/api/designer/generate')) {
      // Matches the hosts: with a script in hand this patches it, and generates from scratch only
      // when there is nothing to patch. Regenerating unconditionally meant the sandbox destroyed
      // CREATE CONNECTION on every canvas write-back, so the one bug this endpoint can cause was
      // both guaranteed here and indistinguishable from correct behaviour.
      data = body.script && body.script.trim()
        ? { script: mockPatchScript(body.script, body.designState ?? seedState) }
        : { script: generateMockScript(body.designState ?? seedState) };
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
        // The query workbench builds its run preamble from these, so a mock that omitted them would
        // make every embedded run in the sandbox look like an undeclared connection.
        designState.connections = mockParseConnections(scriptText);
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
        // The canned multi-stage demo graph, plus a node for every labelled task the script
        // actually declares — so the editable layer has real cards to decorate without the
        // read-only stories losing the stages they assert on.
        dag: mockWithScriptTasks(body.script || '', {
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
        }),
      };
    } else if (path.endsWith('/api/designer/pipeline-task')) {
      data = mockPipelineTask(body);
    } else if (path.endsWith('/api/designer/pipeline-scope')) {
      data = mockPipelineScope(body);
    } else if (path.endsWith('/api/designer/pipeline-run-plan')) {
      data = mockPipelineRunPlan(body);
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
      // A paginated script gets a paginated manifest: the physical-page breakdown is what Studio's
      // pagination preview reads, and the canned dashboard manifest has none. The real compilation
      // lives in PhysicalPageCompiler and is proven against exported PDFs; this only has to be the
      // right shape for the surface that reads it.
      data = /\bAS\s+PAGINATED\b/i.test(String(body.script || ''))
        ? mockPaginatedManifest()
        : await fetch('/tools/ui-sandbox/fixtures/sandbox-report.manifest.json')
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
  let inString = false;
  for (let index = 0; index < list.length; index++) {
    const ch = list[index];
    if (ch === "'") {
      if (inString && list[index + 1] === "'") {
        cur += "''";
        index++;
        continue;
      }
      inString = !inString;
    } else if (!inString && ch === '(') depth++;
    else if (!inString && ch === ')') depth = Math.max(0, depth - 1);
    if (!inString && ch === ',' && depth === 0) { out.push(cur); cur = ''; } else cur += ch;
  }
  if (cur.trim()) out.push(cur);
  return out;
}

// Synthesize three representative rows, typing values by the mock schema when known.
/**
 * `count` rows whose text columns are distinct per row, so a story can drive a surface whose
 * behaviour only begins past its first page of values — the filter pane's search, paging and bulk
 * selection. Set `window.__STUDIO_SAMPLE_ROWS__`; every sample the mock serves widens with it, so a
 * refresh mid-journey does not quietly hand back the narrow default.
 */
function mockWideSampleRows(columns, table, count) {
  const typeOf = (name) => {
    const column = table?.columns?.find((item) => item.name.toLowerCase() === name.toLowerCase());
    return (column?.type || '').toUpperCase();
  };
  return Array.from({ length: count }, (_, index) => {
    const row = {};
    for (const name of columns) {
      const type = typeOf(name);
      if (/INT|DEC|NUM|FLOAT|MONEY|REAL/.test(type)) row[name] = (index + 1) * 100;
      else if (/DATE|TIME/.test(type)) row[name] = `2026-08-${String((index % 28) + 1).padStart(2, '0')}`;
      else if (/BOOL|BIT/.test(type)) row[name] = index % 2 === 0;
      else row[name] = `${name}_${String(index).padStart(2, '0')}`;
    }
    return row;
  });
}

function mockRowsForColumns(columns, table) {
  const wide = Number(globalThis.__STUDIO_SAMPLE_ROWS__) || 0;
  if (wide > 0) return mockWideSampleRows(columns, table, wide);
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

// Finds either `KEYWORD ( ... )` or `KEYWORD = value` inside a statement. The formatting inspector
// upgrades scalar TITLE/SUBTITLE clauses into structured ones, so a parenthesis-only finder would
// miss the old scalar, append a second TITLE, and hand the next parse an invalid visual.
function mockFindClause(text, keyword, from = 0) {
  let depth = 0;
  let inString = false;
  const upperKeyword = keyword.toUpperCase();
  for (let index = 0; index < text.length; index++) {
    const current = text[index];
    if (current === "'") {
      if (inString && text[index + 1] === "'") { index++; continue; }
      inString = !inString;
      continue;
    }
    if (inString) continue;
    if (current === '(') { depth++; continue; }
    if (current === ')') { depth--; continue; }
    if (index < from || depth < 1 || text.slice(index, index + keyword.length).toUpperCase() !== upperKeyword) continue;
    const before = index > 0 ? text[index - 1] : '';
    const after = text[index + keyword.length] || '';
    if (/[A-Za-z0-9_]/.test(before) || /[A-Za-z0-9_]/.test(after)) continue;

    let cursor = index + keyword.length;
    while (/\s/.test(text[cursor] || '')) cursor++;
    if (text[cursor] === '(') {
      let clauseDepth = 0;
      let clauseString = false;
      for (let end = cursor; end < text.length; end++) {
        if (text[end] === "'") {
          if (clauseString && text[end + 1] === "'") { end++; continue; }
          clauseString = !clauseString;
        } else if (!clauseString && text[end] === '(') clauseDepth++;
        else if (!clauseString && text[end] === ')' && --clauseDepth === 0)
          return { start: index, end: end + 1, text: text.slice(index, end + 1) };
      }
      return null;
    }
    if (text[cursor] === '=') {
      let valueDepth = depth;
      let valueString = false;
      for (let end = cursor + 1; end < text.length; end++) {
        if (text[end] === "'") {
          if (valueString && text[end + 1] === "'") { end++; continue; }
          valueString = !valueString;
        } else if (!valueString && text[end] === '(') valueDepth++;
        else if (!valueString && text[end] === ')') {
          if (valueDepth === depth) return { start: index, end, text: text.slice(index, end) };
          valueDepth--;
        } else if (!valueString && text[end] === ',' && valueDepth === depth)
          return { start: index, end, text: text.slice(index, end) };
      }
      return { start: index, end: text.length, text: text.slice(index) };
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
  const maps = mockVisualMappings(visual);
  if (maps.length) clauses.push('    MAPPINGS (' + maps.join(', ') + ')');
  const title = mockTextClause('TITLE', visual.formatting?.title, visual.title);
  if (title) clauses.push('    ' + title);
  const subtitle = mockTextClause('SUBTITLE', visual.formatting?.subtitle, null);
  if (subtitle) clauses.push('    ' + subtitle);
  const options = mockOptionsClause(visual.options, visual.formatting);
  if (options) clauses.push('    ' + options);
  // ACTIONS, INTERACTIONS and CASCADE are their own clauses, not OPTIONS entries. A mock that
  // dropped them made the interaction inspector look like a dead panel in the sandbox.
  for (const clause of mockInteractionClauses(visual.options)) clauses.push('    ' + clause);
  const palette = visual.formatting?.palette ?? [];
  if (palette.length) clauses.push('    STYLE (PALETTE = (' + palette.map(mockQuote).join(', ') + '))');
  const rules = mockFormattingClause(visual.formatting?.conditionalRules);
  if (rules) clauses.push('    ' + rules);
  if (visual.options?.print_layout) clauses.push('    ' + visual.options.print_layout);
  return 'CREATE VISUAL ' + name + ' AS ' + visual.type + ' (\n' + clauses.join(',\n') + '\n);';
}

// `inline_source` and `print_layout` are carried as their own clauses, not as OPTIONS entries.
function mockOptionsClause(options, formatting) {
  const entries = Object.entries(options ?? {}).filter(([key]) => key !== 'inline_source' && key !== 'print_layout');
  const body = entries.map(([key, value]) => key + ' = ' + mockOptionValue(value));
  for (const [axis, values] of [['X', formatting?.xAxis], ['Y', formatting?.yAxis]]) {
    const axisEntries = Object.entries(values ?? {}).filter(([, value]) => String(value || '').trim());
    if (axisEntries.length) body.push(axis + '_AXIS (' + axisEntries.map(([key, value]) => key.toUpperCase() + ' = ' + mockOptionValue(value)).join(', ') + ')');
  }
  return body.length ? 'OPTIONS (' + body.join(', ') + ')' : '';
}

function mockQuote(value) { return "'" + String(value ?? '').replace(/'/g, "''") + "'"; }

function mockOptionValue(value) {
  const text = String(value ?? '').trim();
  return /^-?\d+(?:\.\d+)?$/.test(text) || /^(?:ON|OFF|TRUE|FALSE)$/i.test(text) ? text.toUpperCase() : mockQuote(text);
}

function mockTextClause(keyword, formatting, fallback) {
  const text = formatting?.text ?? fallback;
  const styled = formatting && ['color', 'font', 'size', 'weight', 'align'].some(key => formatting[key]);
  if (!styled) return text ? keyword + ' = ' + mockQuote(text) : '';
  const parts = [];
  if (text) parts.push('TEXT = ' + mockQuote(text));
  if (formatting.color) parts.push('COLOR = ' + mockQuote(formatting.color));
  if (formatting.font) parts.push('FONT = ' + mockQuote(formatting.font));
  if (formatting.size) parts.push('SIZE = ' + mockQuote(formatting.size));
  if (formatting.weight) parts.push('WEIGHT = ' + mockQuote(formatting.weight));
  if (formatting.align) parts.push('ALIGN = ' + String(formatting.align).toUpperCase());
  return keyword + ' (' + parts.join(', ') + ')';
}

function mockVisualMappings(visual) {
  const fields = visual.formatting?.fields ?? {};
  return Object.entries(visual.mappings ?? {}).filter(([, column]) => column).map(([role, column]) => {
    const field = fields[role] || fields[role.toUpperCase()];
    if (!field) return role + ' = ' + column;
    let text = String(column);
    if (field.format) text += ' FORMAT ' + mockQuote(field.format);
    if (field.align) text += ' ALIGN ' + mockQuote(field.align);
    if (field.dataBar) text += ' DATA_BAR' + (field.dataBarColor ? ' COLOR ' + mockQuote(field.dataBarColor) : '');
    if (field.displayName) text += ' AS ' + mockQuote(field.displayName);
    return text;
  });
}

function mockFormattingClause(rules) {
  const entries = (rules ?? []).filter(rule => rule.condition && rule.backgroundColor).map(rule =>
    'WHEN ' + rule.condition + ' THEN ' + mockQuote(rule.backgroundColor)
      + (rule.fontColor ? ' FONT_COLOR ' + mockQuote(rule.fontColor) : ''));
  return entries.length ? 'FORMATTING (' + entries.join(', ') + ')' : '';
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
  out = mockReplaceClause(out, 'TITLE', mockTextClause('TITLE', visual.formatting?.title, visual.title));
  out = mockReplaceClause(out, 'SUBTITLE', mockTextClause('SUBTITLE', visual.formatting?.subtitle, null));
  const mappings = mockVisualMappings(visual);
  out = mockReplaceClause(out, 'MAPPINGS', mappings.length ? 'MAPPINGS (' + mappings.join(', ') + ')' : '');
  out = mockReplaceClause(out, 'OPTIONS', mockOptionsClause(visual.options, visual.formatting));
  const palette = visual.formatting?.palette ?? [];
  out = mockReplaceClause(out, 'STYLE', palette.length ? 'STYLE (PALETTE = (' + palette.map(mockQuote).join(', ') + '))' : '');
  out = mockReplaceClause(out, 'FORMATTING', mockFormattingClause(visual.formatting?.conditionalRules));
  out = mockReplaceClause(out, 'ACTIONS', mockPrefixedClause(visual.options, 'action:', 'ACTIONS'));
  out = mockReplaceClause(out, 'INTERACTIONS', mockPrefixedClause(visual.options, 'interaction:', 'INTERACTIONS'));
  out = mockReplaceClause(out, 'CASCADE', String(visual.options?.cascade || '').trim());
  out = mockReplaceClause(out, 'PRINT_LAYOUT', visual.options?.print_layout || '');
  return out;
}

// `action:ON_CLICK` and `interaction:ON_SELECT` are carried on the visual's options with a prefix,
// exactly as the real parsing service reports them, and written back as one clause each.
// A paginated manifest whose detail table is split across two sheets, as the compiler splits one.
function mockPaginatedManifest() {
  const detail = {
    name: 'detail_rows',
    visualType: 'TABLE',
    columns: ['Territory', 'Reference', 'Amount'],
    rows: Array.from({ length: 60 }, (_, index) => [`Zone ${index % 4}`, `SO-${index}`, String(index * 37)]),
  };
  const layout = { pageSize: 'Letter', orientation: 'PORTRAIT', marginTop: 0.75, marginBottom: 0.75 };
  return {
    title: 'Preview',
    visuals: [detail],
    pages: [{
      name: 'Detail',
      mode: 'PAGINATED',
      structure: 'A',
      slotMap: { A: 'detail_rows' },
      printLayout: layout,
      physicalPages: [
        { pageNumber: 1, layout, visuals: [{ visual: detail, topOffset: 0, height: 9, startRowIndex: 0, endRowIndex: 33 }] },
        { pageNumber: 2, layout, visuals: [{ visual: detail, topOffset: 0, height: 7, startRowIndex: 34, endRowIndex: 59 }] },
      ],
    }],
  };
}

function mockPrefixedClause(options, prefix, keyword) {
  const entries = Object.entries(options ?? {})
    .filter(([key, value]) => key.startsWith(prefix) && String(value || '').trim())
    .map(([key, value]) => key.slice(prefix.length).toUpperCase() + ' = ' + value);
  return entries.length ? keyword + ' (' + entries.join(', ') + ')' : '';
}

function mockInteractionClauses(options) {
  return [
    mockPrefixedClause(options, 'action:', 'ACTIONS'),
    mockPrefixedClause(options, 'interaction:', 'INTERACTIONS'),
    String(options?.cascade || '').trim(),
  ].filter(Boolean);
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
    // A source may be a parenthesised SELECT containing commas, so it is read by balancing
    // parentheses rather than stopping at the first comma. Truncating it made an aggregated
    // visual look broken here: the canvas card could not see the GROUP BY its source declares.
    const source = mockReadSourceClause(body);
    const dataset = source && source.startsWith('&') ? source : null;
    if (source && !dataset) options.inline_source = source;

    const optionsClause = mockFindClause(body, 'OPTIONS');
    if (optionsClause) {
      const inner = optionsClause.text.slice(optionsClause.text.indexOf('(') + 1, -1);
      for (const entry of splitTopLevel(inner)) {
        const axis = /^([XY])_AXIS\s*\(([\s\S]*)\)$/i.exec(entry.trim());
        if (axis) continue;
        const [key, ...rest] = entry.split('=');
        if (!key || !rest.length) continue;
        options[key.trim()] = rest.join('=').trim().replace(/^'|'$/g, '');
      }
    }
    const printLayout = mockFindClause(body, 'PRINT_LAYOUT');
    if (printLayout) options.print_layout = printLayout.text;
    const cascadeClause = mockFindClause(body, 'CASCADE');
    if (cascadeClause) options.cascade = cascadeClause.text;
    for (const [keyword, prefix] of [['ACTIONS', 'action:'], ['INTERACTIONS', 'interaction:']]) {
      const clause = mockFindClause(body, keyword);
      if (!clause) continue;
      const inner = clause.text.slice(clause.text.indexOf('(') + 1, -1);
      for (const entry of splitTopLevel(inner)) {
        const [key, ...rest] = entry.split('=');
        if (!key || !rest.length) continue;
        options[prefix + key.trim().toUpperCase()] = rest.join('=').trim();
      }
    }
    const textDefault = /\bDEFAULT\s*=\s*('(?:[^']|'')*')/i.exec(body)?.[1];
    if (textDefault) options.text_default = textDefault;

    const mappings = {};
    const fields = {};
    const mappingClause = mockFindClause(body, 'MAPPINGS');
    if (mappingClause) {
      const inner = mappingClause.text.slice(mappingClause.text.indexOf('(') + 1, -1);
      splitTopLevel(inner).forEach((entry, index) => {
        const parts = entry.split('=');
        if (parts.length >= 2) mappings[parts[0].trim().toUpperCase()] = parts.slice(1).join('=').trim();
        else if (entry.trim()) {
          const column = /^([A-Za-z_][A-Za-z0-9_]*)/.exec(entry.trim())?.[1] || `COLUMN${index + 1}`;
          mappings[column.toUpperCase()] = column;
          const format = /\bFORMAT\s+'((?:[^']|'')*)'/i.exec(entry)?.[1]?.replace(/''/g, "'");
          const align = /\bALIGN\s+'((?:[^']|'')*)'/i.exec(entry)?.[1]?.replace(/''/g, "'");
          const dataBar = /\bDATA_BAR\b/i.test(entry);
          const dataBarColor = /\bDATA_BAR\s+COLOR\s+'((?:[^']|'')*)'/i.exec(entry)?.[1]?.replace(/''/g, "'");
          const displayName = /\bAS\s+'((?:[^']|'')*)'/i.exec(entry)?.[1]?.replace(/''/g, "'");
          if (format || align || dataBar || displayName) fields[column.toUpperCase()] = { format, align, dataBar, dataBarColor, displayName };
        }
      });
    }

    const titleBlock = mockFindClause(body, 'TITLE');
    const titleText = titleBlock?.text.includes('(')
      ? /\bTEXT\s*=\s*'((?:[^']|'')*)'/i.exec(titleBlock.text)?.[1]?.replace(/''/g, "'")
      : /\bTITLE\s*=\s*'((?:[^']|'')*)'/i.exec(body)?.[1]?.replace(/''/g, "'");
    const textFormatting = (keyword) => {
      const clause = mockFindClause(body, keyword);
      if (!clause || !clause.text.includes('(')) return null;
      const read = key => new RegExp('\\b' + key + "\\s*=\\s*'((?:[^']|'')*)'", 'i').exec(clause.text)?.[1]?.replace(/''/g, "'") || null;
      return { text: read('TEXT'), color: read('COLOR'), font: read('FONT'), size: read('SIZE'), weight: read('WEIGHT'), align: /\bALIGN\s*=\s*([A-Za-z0-9_]+)/i.exec(clause.text)?.[1] || null };
    };
    const axisOptions = axis => {
      const optionsText = optionsClause?.text || '';
      const clause = mockFindClause(optionsText, axis + '_AXIS');
      if (!clause) return {};
      const values = {};
      for (const entry of splitTopLevel(clause.text.slice(clause.text.indexOf('(') + 1, -1))) {
        const [key, ...rest] = entry.split('=');
        if (key && rest.length) values[key.trim().toUpperCase()] = rest.join('=').trim().replace(/^'|'$/g, '');
      }
      return values;
    };
    const paletteClause = mockFindClause(body, 'STYLE');
    const paletteBody = paletteClause ? /\bPALETTE\s*=\s*\(([\s\S]*?)\)/i.exec(paletteClause.text)?.[1] : null;
    const palette = paletteBody ? splitTopLevel(paletteBody).map(value => value.trim().replace(/^'|'$/g, '')) : [];
    const formattingClause = mockFindClause(body, 'FORMATTING');
    const conditionalRules = formattingClause ? splitTopLevel(formattingClause.text.slice(formattingClause.text.indexOf('(') + 1, -1)).map(entry => {
      const rule = /^\s*WHEN\s+([\s\S]+?)\s+THEN\s+'((?:[^']|'')*)'(?:\s+FONT_COLOR\s+'((?:[^']|'')*)')?\s*$/i.exec(entry);
      return rule ? { condition: rule[1].trim(), backgroundColor: rule[2].replace(/''/g, "'"), fontColor: rule[3]?.replace(/''/g, "'") || null } : null;
    }).filter(Boolean) : [];

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
      title: titleText || null,
      dataset,
      mappings,
      options,
      formatting: {
        title: textFormatting('TITLE'),
        subtitle: textFormatting('SUBTITLE') || (() => {
          const text = /\bSUBTITLE\s*=\s*'((?:[^']|'')*)'/i.exec(body)?.[1]?.replace(/''/g, "'");
          return text ? { text } : null;
        })(),
        xAxis: axisOptions('X'),
        yAxis: axisOptions('Y'),
        palette,
        conditionalRules,
        fields,
      },
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

/**
 * The CREATE CONNECTION statements a script declares, as authored.
 *
 * The mock is a scanner, so it does what a scanner can do honestly: it walks to the terminating
 * semicolon while skipping quoted strings and line comments, rather than stopping at the first `;`
 * the way the workbench's old regex did. The real hosts answer this from the parser.
 */
function mockParseConnections(script) {
  const connections = [];
  const head = /\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?CONNECTION\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:\[([^\]]+)\]|([A-Za-z_][A-Za-z0-9_]*))/gi;
  let match;
  while ((match = head.exec(script)) !== null) {
    let i = head.lastIndex;
    let quote = null;
    while (i < script.length) {
      const ch = script[i];
      if (quote) {
        if (ch === quote) quote = null;
      } else if (ch === "'" || ch === '"') {
        quote = ch;
      } else if (ch === '-' && script[i + 1] === '-') {
        const newline = script.indexOf('\n', i);
        i = newline === -1 ? script.length : newline;
      } else if (ch === ';') {
        break;
      }
      i++;
    }
    connections.push({ name: match[1] || match[2], text: script.slice(match.index, i).trim() });
    head.lastIndex = Math.min(i + 1, script.length);
  }
  return connections;
}

/**
 * The labelled EXECUTE blocks a script declares: the sandbox's stand-in for the host's parse.
 *
 * Text scanning is honest here in a way it would not be in a host: the sandbox has no parser, and
 * this only has to be good enough to drive the UI. The real contract — that an edit never disturbs a
 * byte it did not claim — is the host's, and is proved against the parser in its own test lane.
 */
/** The first line of a statement, for a card label. */
function firstLine(text) {
  return String(text || '').split(/\r?\n/)[0].slice(0, 40);
}

function mockParseTasks(script) {
  const tasks = [];
  // A task is a label followed by one statement. The kind is read from the statement's first word,
  // the same way the host reads it from the parsed node type.
  const head = /^[ \t]*([A-Za-z_][A-Za-z0-9_]*)[ \t]*:[ \t]*\r?\n[ \t]*(EXEC(?:UTE)?|COPY|ASSERT|SEND|PARALLEL|FOREACH|BEGIN[ \t]+TRY)\b/gim;
  let match;
  while ((match = head.exec(script)) !== null) {
    const keyword = match[2].toUpperCase().replace(/\s+/g, ' ');
    const kind = keyword.startsWith('EXEC') ? 'execution'
      : keyword === 'COPY' ? 'fileoperation'
        : keyword === 'ASSERT' ? 'validation'
          : keyword === 'PARALLEL' ? 'parallel'
            : keyword === 'FOREACH' ? 'foreach'
              : keyword === 'BEGIN TRY' ? 'transaction'
                : 'notification';

    // An execution task ends at its matching END; the others end at their terminating semicolon.
    let end;
    let connection = '';
    let body = '';
    let variable = null;
    let collection = null;
    if (kind === 'parallel' || kind === 'foreach' || kind === 'transaction') {
      end = mockContainerEnd(script, match.index, kind);
      if (kind === 'foreach') {
        const header = /FOREACH[ \t]+(@[A-Za-z_][A-Za-z0-9_]*)[ \t]+IN[ \t]+([\s\S]*?)\r?\n[ \t]*BEGIN\b/i
          .exec(script.slice(match.index, end));
        variable = header?.[1] ?? null;
        collection = header?.[2]?.trim() ?? null;
      }
    } else if (kind === 'execution') {
      connection = /\bEXEC(?:UTE)?[ \t]+([A-Za-z_][A-Za-z0-9_]*)/i.exec(script.slice(match.index))?.[1] ?? '';
      const beginAt = script.toUpperCase().indexOf('BEGIN', match.index);
      // The closing END may be indented, because the task may be inside a container. Anchoring on an
      // unindented "\nEND" made a nested task run past its own close and swallow the container's.
      const closer = beginAt === -1 ? null : /\r?\n[ \t]*END\b/i.exec(script.slice(beginAt));
      const endAt = closer ? beginAt + closer.index : -1;
      const stop = endAt === -1 ? script.length : endAt;
      body = script.slice(script.indexOf('\n', beginAt) + 1, stop).replace(/^\r?\n/, '').trimEnd();
      end = endAt === -1 ? script.length : Math.min(script.length, (script.indexOf(';', stop) + 1) || stop + 4);
    } else {
      const semicolon = script.indexOf(';', match.index);
      end = semicolon === -1 ? script.length : semicolon + 1;
      body = script.slice(script.indexOf('\n', match.index) + 1, end).trim();
      if (kind === 'notification') {
        connection = /\bAT[ \t]+([A-Za-z_][A-Za-z0-9_]*)/i.exec(script.slice(match.index, end))?.[1] ?? '';
      }
    }

    // The run of -- @after: lines immediately above the label, if there is one. Each declares one
    // or more prerequisites; an edge carrying the author's own expression gets a line of its own,
    // because a comma inside that expression would otherwise read as the next prerequisite.
    let lineStart = script.lastIndexOf('\n', Math.max(0, match.index - 1)) + 1;
    const tagLines = [];
    while (lineStart > 0) {
      const previousStart = script.lastIndexOf('\n', lineStart - 2) + 1;
      const previous = script.slice(previousStart, lineStart).trim();
      if (!previous.startsWith('-- @after:')) break;
      tagLines.unshift(previous.slice('-- @after:'.length).trim());
      lineStart = previousStart;
    }
    const dependsOn = tagLines.flatMap(mockParseDependencyLine);

    // A task sits inside the innermost container whose span already covers it. Containers are
    // always found first because their label comes earlier in the script than their children's.
    const container = tasks
      .filter(other => other.kind === 'parallel' || other.kind === 'foreach' || other.kind === 'transaction')
      .filter(other => other.start < match.index && other.end > end)
      .sort((left, right) => right.start - left.start)[0]?.id ?? null;

    tasks.push({
      id: match[1],
      kind,
      connection,
      body,
      variable,
      collection,
      container,
      dependsOn,
      line: script.slice(0, match.index).split('\n').length,
      start: tagLines.length ? lineStart : match.index,
      end,
    });
    // Containers are re-entered rather than skipped, so the tasks they hold are found too.
    head.lastIndex = kind === 'parallel' || kind === 'foreach' || kind === 'transaction'
      ? script.indexOf('\n', match.index) + 1
      : end;
  }
  return tasks;
}

/**
 * Where a container's statement ends: its matching END, or the END CATCH of a transaction scope.
 *
 * Counts block depth rather than searching for the next END, so a child whose pushed-down SQL has
 * its own BEGIN/END pair cannot be mistaken for the container's close.
 */
function mockContainerEnd(script, from, kind) {
  const tokens = /\b(BEGIN[ \t]+TRY|BEGIN[ \t]+CATCH|BEGIN[ \t]+TRAN(?:SACTION)?|END[ \t]+TRY|END[ \t]+CATCH|BEGIN|END)\b/gi;
  tokens.lastIndex = script.indexOf('\n', from) + 1;

  let depth = 0;
  let match;
  while ((match = tokens.exec(script)) !== null) {
    const word = match[1].toUpperCase().replace(/\s+/g, ' ');
    if (kind === 'transaction') {
      if (word === 'END CATCH') return Math.min(script.length, (script.indexOf(';', match.index) + 1) || script.length);
      continue;
    }
    if (word === 'BEGIN') depth += 1;
    else if (word === 'END') {
      depth -= 1;
      if (depth === 0) return Math.min(script.length, (script.indexOf(';', match.index) + 1) || script.length);
    }
  }
  return script.length;
}

/** The statement a kind writes. Mirrors the host's renderer closely enough to drive the UI. */
function mockRenderTask(body) {
  const literal = value => `'${String(value ?? '').replace(/'/g, "''")}'`;
  const kind = String(body.kind || 'execution').toLowerCase();
  if (kind === 'fileoperation') {
    return `${body.id}:\nCOPY FILE ${literal(body.source)} TO ${literal(body.target)};\n`;
  }
  if (kind === 'validation') {
    return `${body.id}:\nASSERT ${body.condition},\n    ${literal(body.message)};\n`;
  }
  if (kind === 'notification') {
    return `${body.id}:\nSEND EMAIL\n    TO ${literal(body.recipient)}\n    FROM ${literal(body.sender)}\n`
      + `    SUBJECT ${literal(body.subject)}\n    BODY ${literal(body.body)}\n    AT ${body.connection};\n`;
  }
  if (kind === 'parallel') {
    return `${body.id}:\nPARALLEL BEGIN\nEND;\n`;
  }
  if (kind === 'foreach') {
    return `${body.id}:\nFOREACH @${String(body.variable || 'item').replace(/^@/, '')} IN ${String(body.collection || '').trim()}\nBEGIN\nEND;\n`;
  }
  if (kind === 'transaction') {
    return `${body.id}:\nBEGIN TRY\n    BEGIN TRANSACTION;\n    COMMIT;\nEND TRY\nBEGIN CATCH\n`
      + `    IF @@TRANCOUNT > 0 ROLLBACK;\n    THROW;\nEND CATCH;\n`;
  }
  const indented = String(body.body || 'SELECT 1;').split('\n').map(line => `    ${line}`).join('\n');
  return `${body.id}:\nEXECUTE ${body.connection} BEGIN\n${indented}\nEND;\n`;
}

/**
 * The base graph with one node per labelled task appended, chained after the last existing stage.
 *
 * The task nodes carry their label as `meta.key`, which is what marks a card editable. Ids stay
 * positional, exactly as the real projection's are — that is the whole point of the key.
 */
function mockWithScriptTasks(script, base) {
  const tasks = mockParseTasks(script);
  if (!tasks.length) return base;

  const nodes = tasks.map((task, index) => ({
    id: `s${index}`,
    label: task.kind === 'execution' ? `EXECUTE ${task.connection}` : firstLine(task.body),
    type: 'procedure',
    meta: { line: task.line, key: task.id },
  }));
  const tail = base.nodes[base.nodes.length - 1];
  const edges = tasks.slice(1).map((task, index) => ({ source: `s${index}`, target: `s${index + 1}` }));
  if (tail) edges.unshift({ source: tail.id, target: 's0' });

  return { nodes: [...base.nodes, ...nodes], edges: [...base.edges, ...edges] };
}

/** True when `taskId` waits on `candidate`, directly or through other tasks. */
/** One `-- @after:` line as the dependencies it declares. Mirrors the host's tag grammar. */
function mockParseDependencyLine(line) {
  const when = line.search(/\swhen\s/i);
  if (when >= 0) {
    const id = line.slice(0, when).trim();
    const expression = line.slice(when).replace(/^\s*when\s+/i, '').trim();
    return id && expression ? [{ id, condition: 'expression', expression }] : [];
  }

  return line.split(',').map(item => item.trim()).filter(Boolean).map(item => {
    const suffix = /\s+on\s+(success|failure|completion)$/i.exec(item);
    return suffix
      ? { id: item.slice(0, suffix.index).trim(), condition: `on${suffix[1].toLowerCase()}`, expression: null }
      : { id: item, condition: 'always', expression: null };
  });
}

/** A dependency as the tag spells it. */
function mockRenderDependency(dependency) {
  if (dependency.condition === 'expression') return `${dependency.id} when ${dependency.expression}`;
  if (dependency.condition === 'onsuccess') return `${dependency.id} on success`;
  if (dependency.condition === 'onfailure') return `${dependency.id} on failure`;
  if (dependency.condition === 'oncompletion') return `${dependency.id} on completion`;
  return dependency.id;
}

function waitsOn(tasks, taskId, candidate) {
  const seen = new Set();
  const pending = [taskId];
  while (pending.length) {
    const current = String(pending.pop() || '').toLowerCase();
    if (seen.has(current)) continue;
    seen.add(current);
    if (current === String(candidate).toLowerCase()) return true;
    const task = tasks.find(entry => entry.id.toLowerCase() === current);
    for (const dependency of task?.dependsOn ?? []) pending.push(dependency.id);
  }
  return false;
}

/**
 * What a task can see from where it sits.
 *
 * Positional, like the host's: only what is written above the task's label counts. A flat scan of the
 * whole script would make the sandbox teach the UI a claim the host does not make.
 */
function mockPipelineScope(body) {
  const script = body.script || '';
  const id = String(body.id || '');
  if (!id) return { resolved: false, error: 'No task is selected.', variables: [], tempTables: [] };

  const label = new RegExp(`^[ \\t]*${id.replace(/[^\\w]/g, '')}[ \\t]*:`, 'm').exec(script);
  if (!label) return { resolved: false, error: `'${id}' is not a task in this script.`, variables: [], tempTables: [] };

  const above = script.slice(0, label.index);
  const lineOf = index => above.slice(0, index).split('\n').length;

  const variables = [];
  const seenVariables = new Set();
  for (const match of above.matchAll(/^[ \t]*DECLARE[ \t]+(@[A-Za-z_]\w*)[ \t]+([A-Za-z]\w*(?:\([^)]*\))?)[ \t]*(?:=[ \t]*([^;]+))?;/gim)) {
    if (seenVariables.has(match[1].toLowerCase())) continue;
    seenVariables.add(match[1].toLowerCase());
    variables.push({
      name: match[1], type: match[2], value: match[3]?.trim() ?? null,
      line: lineOf(match.index), origin: 'declared',
    });
  }

  const tempTables = [];
  const seenTables = new Set();
  for (const match of above.matchAll(/\bINTO[ \t]+(#\w+)|CREATE[ \t]+TABLE[ \t]+(#\w+)/gi)) {
    const name = match[1] || match[2];
    if (seenTables.has(name.toLowerCase())) continue;
    seenTables.add(name.toLowerCase());
    tempTables.push({
      name, columns: [], line: lineOf(match.index),
      origin: match[1] ? 'SELECT INTO' : 'CREATE TABLE',
    });
  }

  return { resolved: true, error: null, variables, tempTables };
}

/**
 * The sandbox's run plan.
 *
 * Deliberately reports effects for the shapes that have them, so the confirmation dialog is
 * reachable here at all. The catch-all `{ok: true}` this mock answers unmatched paths with once hid
 * a whole class of 404s; a route that existed but always planned an empty, effect-free run would
 * hide the confirmation the same way — it would simply never open, and look like it worked.
 */
function mockPipelineRunPlan(body) {
  const script = body.script || '';
  const id = String(body.id || '');
  const refused = error => ({ resolved: false, error, script: '', included: [], skipped: [], effects: [] });
  if (!id) return refused('No task is selected.');

  const tasks = mockParseTasks(script);
  const selected = tasks.find(task => task.id.toLowerCase() === id.toLowerCase());
  if (!selected) return refused(`'${id}' is not a task in this script.`);

  // A declared dependency narrows the run to what it names; with none, plain script order stands.
  const above = tasks.filter(task => task.line <= selected.line);
  const declared = (selected.dependsOn ?? []).map(entry => String(entry?.id ?? entry).toLowerCase());
  const included = declared.length
    ? above.filter(task => declared.includes(task.id.toLowerCase()) || task.id === selected.id)
    : above;
  const includedIds = new Set(included.map(task => task.id.toLowerCase()));

  const patterns = [
    [/^\s*MERGE\s+INTO\s+([\w.]+)/i, 'MERGE INTO'],
    [/^\s*INSERT\s+INTO\s+([\w.]+)/i, 'INSERT INTO'],
    [/^\s*UPDATE\s+([\w.]+)/i, 'UPDATE'],
    [/^\s*DELETE\s+FROM\s+([\w.]+)/i, 'DELETE FROM'],
    [/^\s*TRUNCATE\s+TABLE\s+([\w.]+)/i, 'TRUNCATE TABLE'],
    [/^\s*EXECUTE\s+(\w+)\s+BEGIN/i, 'EXECUTE on'],
    [/^\s*COPY\s+FILE\s+'([^']*)'/i, 'COPY FILE'],
    [/^\s*SEND\s+EMAIL\b/i, 'SEND EMAIL to'],
  ];

  const effects = [];
  script.split('\n').forEach((text, index) => {
    for (const [pattern, action] of patterns) {
      const match = pattern.exec(text);
      if (!match) continue;
      const target = match[1] ?? 'the configured recipient';
      // A #temp target dies with the session, so it is not something to confirm.
      if (target.startsWith('#')) return;
      const owner = included.filter(task => task.line <= index + 1).at(-1);
      if (!owner || !includedIds.has(owner.id.toLowerCase())) return;
      effects.push({ taskId: owner.id, action, target, line: index + 1 });
      return;
    }
  });

  return {
    resolved: true,
    error: null,
    script,
    included: included.map(task => task.id),
    skipped: above.filter(task => !includedIds.has(task.id.toLowerCase())).map(task => task.id),
    effects,
  };
}

/** The sandbox's pipeline editor. Refusals are real, so the UI's error path gets exercised. */
function mockPipelineTask(body) {
  const script = body.script || '';
  const tasks = mockParseTasks(script);
  const find = id => tasks.find(task => task.id.toLowerCase() === String(id || '').toLowerCase());
  const refuse = error => ({ applied: false, script, error, tasks: strip(tasks) });
  const strip = list => list.map(({ id, kind, connection, body: text, line, dependsOn, container, variable, collection }) =>
    ({ id, kind, connection, body: text, line, dependsOn, container, variable, collection }));
  const lineEnd = offset => {
    const next = script.indexOf('\n', offset);
    return next === -1 ? script.length : next + 1;
  };

  const op = String(body.op || '').toLowerCase();
  if (op === 'read') return { applied: true, script, error: null, tasks: strip(tasks) };

  const target = op === 'add' ? null : find(body.id);
  if (op !== 'add' && !target) return refuse(`No task called '${body.id}'.`);

  let next = script;
  if (op === 'add') {
    if (find(body.id)) return refuse(`This script already has a task called '${body.id}'.`);
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(String(body.id || ''))) return refuse(`'${body.id}' is not a usable task label.`);
    const text = mockRenderTask(body);
    const anchor = body.after ? find(body.after) : null;
    const at = anchor ? lineEnd(anchor.end) : script.length;
    next = `${script.slice(0, at)}${at >= script.length ? '\n' : ''}${text}${at >= script.length ? '' : '\n'}${script.slice(at)}`;
  } else if (op === 'remove') {
    next = script.slice(0, target.start) + script.slice(lineEnd(target.end));
  } else if (op === 'move') {
    const moved = script.slice(target.start, target.end);
    const without = script.slice(0, target.start) + script.slice(lineEnd(target.end));
    const anchor = body.after ? mockParseTasks(without).find(t => t.id.toLowerCase() === String(body.after).toLowerCase()) : null;
    if (body.after && !anchor) return refuse(`No task called '${body.after}' to move after.`);
    const at = anchor ? lineEnd(anchor.end) : (mockParseTasks(without)[0]?.start ?? without.length);
    next = `${without.slice(0, at)}${moved}\n\n${without.slice(at)}`;
  } else if (op === 'nest') {
    const moved = script.slice(target.start, lineEnd(target.end)).replace(/\r?\n$/, '');
    const without = script.slice(0, target.start) + script.slice(lineEnd(target.end));
    const after = mockParseTasks(without);

    let at;
    let indent;
    if (body.after) {
      const container = after.find(t => t.id.toLowerCase() === String(body.after).toLowerCase());
      if (!container) return refuse(`No task called '${body.after}'.`);
      if (!['parallel', 'foreach', 'transaction'].includes(container.kind)) {
        return refuse(`'${container.id}' does not hold other tasks.`);
      }
      if (container.id.toLowerCase() === target.id.toLowerCase()) return refuse('A container cannot hold itself.');
      // Before the container's closing line — its COMMIT for a scope, otherwise the line its own
      // final END sits on. The *last* line, not the first match: a child's END comes first.
      if (container.kind === 'transaction') {
        const commit = /\n[ \t]*COMMIT\b/i.exec(without.slice(container.start, container.end));
        if (!commit) return refuse(`Could not find where inside '${container.id}' to put '${target.id}'.`);
        at = container.start + commit.index + 1;
      } else {
        at = without.lastIndexOf('\n', container.end - 1) + 1;
      }
      indent = (/^[ \t]*/.exec(without.slice(container.start).split('\n')[0]) ?? [''])[0] + '    ';
    } else {
      if (!target.container) return { applied: true, script, error: null, tasks: strip(tasks) };
      const container = after.find(t => t.id.toLowerCase() === String(target.container).toLowerCase());
      if (!container) return refuse(`Could not find what '${target.id}' is inside.`);
      const nextLine = without.indexOf('\n', container.end);
      at = nextLine === -1 ? without.length : nextLine + 1;
      indent = '';
    }

    const common = Math.min(...moved.split('\n').filter(line => line.trim()).map(line => line.length - line.trimStart().length));
    const shifted = moved.split('\n').map(line => (line.trim() ? indent + line.slice(common) : '')).join('\n');
    next = `${without.slice(0, at)}${shifted}\n${without.slice(at)}`;
  } else if (op === 'connect' || op === 'disconnect') {
    // `id` is the dependent and `after` the dependency, matching the host's contract.
    const dependency = String(body.after || '');
    if (!find(dependency)) return refuse(`No task called '${dependency}'.`);
    if (dependency.toLowerCase() === target.id.toLowerCase()) return refuse('A task cannot depend on itself.');

    // The host refuses a cycle, because a linear script can never execute one. The sandbox has to
    // refuse it too, or the mock teaches the UI a behaviour the real host does not have.
    if (op === 'connect' && waitsOn(tasks, dependency, target.id)) {
      return refuse(`'${dependency}' already waits on '${target.id}', so this would make a cycle.`);
    }

    const edge = String(body.edge || 'always').toLowerCase();
    if (!['always', 'onsuccess', 'onfailure', 'oncompletion', 'expression'].includes(edge)) {
      return refuse(`Unknown edge condition '${body.edge}'.`);
    }
    if (op === 'connect' && edge === 'expression' && !String(body.expression || '').trim()) {
      return refuse('A conditional edge needs the expression it runs on.');
    }

    const existing = target.dependsOn.find(entry => entry.id.toLowerCase() === dependency.toLowerCase());
    const declaration = { id: dependency, condition: edge, expression: body.expression?.trim() ?? null };
    if (op === 'connect' && existing
      && existing.condition === declaration.condition
      && (existing.expression ?? null) === declaration.expression) {
      return { applied: true, script, error: null, tasks: strip(tasks) };
    }
    if (op === 'disconnect' && !existing) {
      return refuse(`'${target.id}' does not wait on '${dependency}'.`);
    }

    // Re-declaring an edge replaces it rather than adding a second prerequisite on the same task.
    const declared = op === 'disconnect'
      ? target.dependsOn.filter(entry => entry.id.toLowerCase() !== dependency.toLowerCase())
      : existing
        ? target.dependsOn.map(entry => (entry === existing ? declaration : entry))
        : [...target.dependsOn, declaration];

    // The sandbox stops at the declaration on purpose. The host also lowers a conditional edge into
    // a BEGIN TRY guard and an IF gate; reproducing that here would be a second emitter to keep in
    // step with the first, and the emitter is covered where it lives, in PipelineConditionalEdgeTests.
    const shared = declared.filter(entry => entry.condition !== 'expression');
    const lines = [
      ...(shared.length ? [shared.map(mockRenderDependency).join(', ')] : []),
      ...declared.filter(entry => entry.condition === 'expression').map(mockRenderDependency),
    ];

    let tagEnd = target.start;
    for (let i = 0; i < target.dependsOn.length; i++) {
      const nextLine = script.indexOf('\n', tagEnd);
      if (nextLine === -1 || !script.slice(tagEnd, nextLine).trim().startsWith('-- @after:')) break;
      tagEnd = nextLine + 1;
    }

    const tag = lines.map(line => `-- @after: ${line}\n`).join('');
    next = script.slice(0, target.start) + tag + script.slice(tagEnd);
  } else if (op === 'update') {
    if (body.newId && find(body.newId) && body.newId.toLowerCase() !== target.id.toLowerCase()) {
      return refuse(`This script already has a task called '${body.newId}'.`);
    }
    let slice = script.slice(target.start, target.end);
    // String.raw, because a plain template literal reads \b as a backspace and the rename silently
    // does nothing — which is exactly how a mock ends up disagreeing with the host it stands in for.
    if (body.newId) slice = slice.replace(new RegExp(String.raw`^([ \t]*)${target.id}\b`), `$1${body.newId}`);
    if (body.connection) slice = slice.replace(/(\bEXEC(?:UTE)?[ \t]+)[A-Za-z_][A-Za-z0-9_]*/i, `$1${body.connection}`);
    next = script.slice(0, target.start) + slice + script.slice(target.end);
  } else {
    return refuse(`Unknown pipeline task operation '${body.op}'.`);
  }

  return { applied: true, script: next, error: null, tasks: strip(mockParseTasks(next)) };
}

/** The SOURCE clause value, honouring parentheses so a derived-table select survives intact. */
function mockReadSourceClause(body) {
  const match = /\bSOURCE\s*=\s*/i.exec(body);
  if (!match) return null;
  const start = match.index + match[0].length;
  if (body[start] !== '(') {
    const plain = /^[^,\n]+/.exec(body.slice(start));
    return plain ? plain[0].trim() : null;
  }
  let depth = 0;
  for (let i = start; i < body.length; i++) {
    if (body[i] === '(') depth++;
    else if (body[i] === ')') {
      depth--;
      if (depth === 0) return body.slice(start, i + 1).trim();
    }
  }
  return null;
}
