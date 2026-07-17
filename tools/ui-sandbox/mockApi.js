// A fetch-like function that answers the report designer's endpoints with canned
// data, so createDesigner() runs in the sandbox with no portal server.
//
// createDesigner calls:
//   POST /api/designer/generate {designState}  -> { script }
//   POST /api/designer/parse    {script}       -> { designState }
//   POST /api/designer/analyze  {script}       -> { diagnostics }
//   POST /api/designer/complete {script,line,column,connectionRef} -> { items }
//   POST /api/designer/run      {script,selection,connectionRef} -> { columns, rows }
//   GET  /api/designer/schema?connection=demo -> { tables }
//   (save endpoints are bypassed via opts.onSaveScript in the story)
//
// The parse round-trip just echoes the seed state — a faithful script↔state parse
// is the real DesignerController's job; here we only need the UI to round-trip.
export function makeMockApi(seedState) {
  return async (url, init) => {
    const path = String(url).replace(/^https?:\/\/[^/]+/, '').replace(/\?.*$/, '');
    let body = {};
    try { body = init?.body ? JSON.parse(init.body) : {}; } catch { /* ignore */ }

    let data = {};
    if (path.endsWith('/api/designer/generate')) {
      data = { script: generateMockScript(body.designState ?? seedState) };
    } else if (path.endsWith('/api/designer/parse')) {
      data = { designState: seedState };
    } else if (path.endsWith('/api/designer/analyze')) {
      data = { diagnostics: analyzeMockScript(body.script ?? '') };
    } else if (path.endsWith('/api/designer/complete')) {
      data = { items: completeMockScript(body.script ?? '', body.line ?? 0, body.column ?? 0, body.connectionRef ?? null) };
    } else if (path.endsWith('/api/designer/run')) {
      data = runMockScript(body.selection || body.script || '');
    } else if (path.endsWith('/api/designer/schema')) {
      data = { connection: 'demo', tables: mockSchemaTables() };
    } else if (path.endsWith('/api/scripts/upload')) {
      data = { path: 'sandbox/' + (body.fileName || 'report.rptsql') };
    } else if (path.endsWith('/api/designer/save')) {
      data = { version: 2, sourceRevision: 'sandbox-rev-2' };
    } else if (path.endsWith('/api/reports') || path.includes('/script-content')) {
      data = { id: 1, ok: true, version: 1, sourceRevision: 'sandbox-rev-1' };
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

function buildStructure(visuals) {
  if (!visuals || visuals.length === 0) return '.';
  const maxRow = Math.max(...visuals.map(v => (v.gridRow || 1) + (v.gridRowSpan || 4) - 1));
  const maxCol = Math.max(...visuals.map(v => (v.gridCol || 1) + (v.gridColSpan || 12) - 1));
  const usedCols = Math.min(12, maxCol);
  
  const grid = Array.from({ length: maxRow }, () => Array(usedCols).fill('.'));
  
  for (const v of visuals) {
    const slot = sanitizeName(v.name, v.id);
    const startRow = (v.gridRow || 1) - 1;
    const endRow = startRow + (v.gridRowSpan || 4);
    const startCol = (v.gridCol || 1) - 1;
    const endCol = startCol + (v.gridColSpan || 12);
    
    for (let r = startRow; r < endRow && r < maxRow; r++) {
      for (let c = startCol; c < endCol && c < usedCols; c++) {
        grid[r][c] = slot;
      }
    }
  }
  
  return grid.map(row => row.join(' ')).join(' / ');
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
        out.push(`CREATE VISUAL ${vName} AS ${v.type} (\n    SOURCE = ${dsName}${maps ? `,\n    MAPPINGS (${maps})` : ''},\n    TITLE = '${v.title || v.name}'\n);`);
      }
    }
    const structure = buildStructure(p.visuals);
    const mapEntries = (p.visuals ?? []).map(v => {
      const slot = sanitizeName(v.name, v.id);
      return `            '${slot}' = ${sanitizeName(v.name, v.id)}`;
    }).join(',\n');

    out.push(`CREATE PAGE [${sanitizeName(p.name, p.id)}] AS DASHBOARD (\n    LAYOUT (\n        STRUCTURE = '${structure}',\n        MAP (\n${mapEntries}\n        )\n    )\n);`);
  }
  return out.join('\n');
}

