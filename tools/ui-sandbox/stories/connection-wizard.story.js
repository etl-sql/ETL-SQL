// UI Sandbox Story: Connection Wizard (Code-First Connector Builder)
// Demonstrates interactive creation, live CREATE CONNECTION syntax generation,
// shared secrets/connection discovery, markdown help integration, and light/dark theme parity.

const SHARED_SECRETS_CATALOG = [
  { name: 'SQL_READER_PW', description: 'Read-only service account for Enterprise Data Warehouse', lastRotated: '2026-07-15' },
  { name: 'SQL_ADMIN_PW', description: 'DBA migration and schema write token', lastRotated: '2026-08-01' },
  { name: 'POSTGRES_APP_KEY', description: 'Application replica read key', lastRotated: '2026-06-20' },
  { name: 'SNOWFLAKE_RSA_KEY', description: 'Service principal private key', lastRotated: '2026-05-10' },
  { name: 'SFTP_TRANSFER_KEY', description: 'Vendor ingest SFTP private key', lastRotated: '2026-07-30' }
];

const SHARED_CONNECTIONS_CATALOG = [
  { alias: 'corp_sales_dw', type: 'MSSQL', target: 'sql01.corp.internal / SalesDW', description: 'Enterprise Sales DW (Read-Only AG Replica)' },
  { alias: 'finance_postgres', type: 'POSTGRES', target: 'pg-fin.internal:5432 / ledger', description: 'General ledger transactional mirror' },
  { alias: 'vendor_sftp_lake', type: 'SFTP', target: 'sftp.vendor.com:22', description: 'Nightly inbound flat files' }
];

const GATEWAYS_CATALOG = [
  {
    id: 'corp-onprem-gw',
    name: 'corp-onprem-gw',
    status: 'Online',
    region: 'US-East On-Prem',
    resources: [
      {
        resourceId: 'prod-finance-db',
        connectorType: 'MSSQL',
        allowedOperations: 'Read, Write',
        state: 'Approved',
        isOnline: true,
        lastSeenUtc: '2026-08-27T12:00:00Z'
      },
      {
        resourceId: 'warehouse-analytics-pg',
        connectorType: 'POSTGRES',
        allowedOperations: 'Read',
        state: 'Approved',
        isOnline: true,
        lastSeenUtc: '2026-08-27T12:05:00Z'
      }
    ]
  }
];

const CONNECTOR_DOCS = {
  MSSQL: {
    title: 'Microsoft SQL Server (MSSQL)',
    description: 'Connects to Microsoft SQL Server or Azure SQL Database. Supports full SQL pushdown, transactions, stored procedures, and AG failover.',
    allOptions: [
      { name: 'SERVER', desc: 'Server name or IP address', mandatory: true, default: '' },
      { name: 'DATABASE', desc: 'Target database name', mandatory: true, default: '' },
      { name: 'PORT', desc: 'TCP port number', mandatory: false, default: '1433' },
      { name: 'USER', desc: 'SQL authentication username', mandatory: false, default: '' },
      { name: 'PASSWORD', desc: 'SQL authentication password (use SECRET:name)', mandatory: false, default: '' },
      { name: 'TRUSTED_CONNECTION', desc: 'Use Windows Integrated Security (ON/OFF)', mandatory: false, default: 'OFF' },
      { name: 'TIMEOUT_SECONDS', desc: 'Command execution timeout in seconds', mandatory: false, default: '30' },
      { name: 'TRUST_SERVER_CERTIFICATE', desc: 'Bypass TLS certificate validation (ON/OFF)', mandatory: false, default: 'OFF' },
      { name: 'ENCRYPT', desc: 'Enable TLS encryption for the connection (ON/OFF)', mandatory: false, default: 'ON' },
      { name: 'APPLICATION_INTENT', desc: 'READWRITE or READONLY (for AG replicas)', mandatory: false, default: 'READWRITE' },
      { name: 'MULTI_SUBNET_FAILOVER', desc: 'Optimize failover for multi-subnet clusters (ON/OFF)', mandatory: false, default: 'OFF' },
      { name: 'CONNECT_TIMEOUT', desc: 'Seconds to wait for a connection', mandatory: false, default: '15' },
      { name: 'POOLING', desc: 'Enable provider connection pooling (ON/OFF)', mandatory: false, default: 'ON' },
      { name: 'MAX_POOL_SIZE', desc: 'Maximum connections allowed in the pool', mandatory: false, default: '100' }
    ]
  },
  FLATFILE: {
    title: 'Flat File / Delimited (CSV, TSV)',
    description: 'Direct ingestion of delimited flat files with zero external dependencies and high-performance streaming parsing.',
    allOptions: [
      { name: 'PATH', desc: 'Absolute or tenant-relative file path', mandatory: true, default: '' },
      { name: 'DELIMITER', desc: 'Column delimiter character (default: ,)', mandatory: false, default: ',' },
      { name: 'HEADER', desc: 'First line contains column names (ON/OFF)', mandatory: false, default: 'ON' },
      { name: 'SKIP_ROWS', desc: 'Number of header rows to skip before column headers', mandatory: false, default: '0' },
      { name: 'ENCODING', desc: 'Text encoding (UTF-8, ASCII, Windows-1252)', mandatory: false, default: 'UTF-8' },
      { name: 'QUOTE_CHAR', desc: 'Character used for quoting text cells', mandatory: false, default: '"' },
      { name: 'ESCAPE_CHAR', desc: 'Escape character inside quoted text', mandatory: false, default: '\\' },
      { name: 'COMMENT_CHAR', desc: 'Lines starting with this character are ignored', mandatory: false, default: '#' }
    ]
  }
};

const FIXTURES = {
  'mssql-standard': {
    type: 'MSSQL',
    alias: 'dw_sales',
    server: 'sql01.corp.internal',
    port: 1433,
    database: 'SalesDW',
    authMode: 'SQL',
    user: 'app_reader',
    secretMode: 'SECRET',
    secretKey: 'SQL_READER_PW',
    timeoutSeconds: 30,
    trustServerCert: true,
    encrypt: true,
    applicationName: 'ReportBuilder',
    customOptions: {}
  },
  'mssql-trusted': {
    type: 'MSSQL',
    alias: 'local_warehouse',
    server: 'localhost',
    port: 1433,
    database: 'AdventureWorksDW',
    authMode: 'TRUSTED',
    user: '',
    secretMode: 'SECRET',
    secretKey: '',
    timeoutSeconds: 15,
    trustServerCert: true,
    encrypt: false,
    applicationName: 'ETL-SQL-IDE',
    customOptions: { 'APPLICATION_INTENT': 'READONLY' }
  },
  'csv-standard': {
    type: 'FLATFILE',
    alias: 'raw_sales_csv',
    path: 'C:/data/finance/sales_2026_q1.csv',
    delimiter: ',',
    header: true,
    skipRows: 0,
    encoding: 'UTF-8',
    quoteChar: '"',
    escapeChar: '\\',
    commentChar: '#',
    customOptions: {}
  },
  'csv-advanced': {
    type: 'FLATFILE',
    alias: 'vendor_feed_tsv',
    path: 'C:/staging/feeds/vendor_exports.tsv',
    delimiter: '\\t',
    header: true,
    skipRows: 2,
    encoding: 'UTF-8',
    quoteChar: '"',
    escapeChar: '\\',
    commentChar: '#',
    customOptions: { 'COMMENT_CHAR': '#' }
  }
};

function generateSql(state) {
  if (state.isSharedRef && state.sharedRefAlias) {
    return `CREATE CONNECTION ${state.alias || 'my_connection'} AS SHARED:${state.sharedRefAlias};`;
  }

  if (state.type === 'MSSQL') {
    const opts = [];
    if (state.server) opts.push(`    SERVER = '${state.server}'`);
    if (state.port && Number(state.port) !== 1433) opts.push(`    PORT = ${state.port}`);
    if (state.database) opts.push(`    DATABASE = '${state.database}'`);
    if (state.authMode === 'TRUSTED') {
      opts.push(`    TRUSTED_CONNECTION = ON`);
    } else {
      if (state.user) opts.push(`    USER = '${state.user}'`);
      if (state.secretMode === 'SECRET' && state.secretKey) {
        opts.push(`    PASSWORD = SECRET:${state.secretKey}`);
      } else if (state.secretMode === 'ENV' && state.secretKey) {
        opts.push(`    PASSWORD = $ENV{${state.secretKey}}`);
      } else if (state.secretMode === 'RAW' && state.secretKey) {
        opts.push(`    PASSWORD = '${state.secretKey}'`);
      }
    }
    if (state.timeoutSeconds && Number(state.timeoutSeconds) !== 30) {
      opts.push(`    TIMEOUT_SECONDS = ${state.timeoutSeconds}`);
    }
    if (state.trustServerCert) opts.push(`    TRUST_SERVER_CERTIFICATE = ON`);
    if (state.encrypt) opts.push(`    ENCRYPT = ON`);
    if (state.applicationName && state.applicationName !== 'ReportBuilder') {
      opts.push(`    APPLICATION_NAME = '${state.applicationName}'`);
    }

    if (state.customOptions) {
      Object.entries(state.customOptions).forEach(([k, v]) => {
        opts.push(`    ${k} = ${isNaN(v) ? `'${v}'` : v}`);
      });
    }

    return `CREATE CONNECTION ${state.alias || 'my_connection'} MSSQL (\n${opts.join(',\n')}\n);`;
  }

  if (state.type === 'FLATFILE') {
    const opts = [];
    if (state.path) opts.push(`    PATH = '${state.path}'`);
    if (state.delimiter && state.delimiter !== ',') {
      const d = state.delimiter === '\\t' ? "'\\t'" : `'${state.delimiter}'`;
      opts.push(`    DELIMITER = ${d}`);
    }
    if (state.header) {
      opts.push(`    HEADER = ON`);
    } else {
      opts.push(`    HEADER = OFF`);
    }
    if (state.skipRows && Number(state.skipRows) > 0) {
      opts.push(`    SKIP_ROWS = ${state.skipRows}`);
    }
    if (state.encoding && state.encoding !== 'UTF-8') {
      opts.push(`    ENCODING = '${state.encoding}'`);
    }
    if (state.quoteChar && state.quoteChar !== '"') {
      opts.push(`    QUOTE_CHAR = '${state.quoteChar}'`);
    }

    if (state.customOptions) {
      Object.entries(state.customOptions).forEach(([k, v]) => {
        opts.push(`    ${k} = '${v}'`);
      });
    }

    return `CREATE CONNECTION ${state.alias || 'my_file'} FLATFILE (\n${opts.join(',\n')}\n);`;
  }

  return `-- Select a connector type`;
}

export default {
  id: 'connection-wizard',
  title: 'Connection Wizard',
  subtitle: 'Code-first connector builder, secret catalog discovery & help reference integration',
  category: 'Script Editors & IDE',
  fixtures: [
    { id: 'mssql-standard', label: 'SQL Server (Standard Auth & Secret Vault)' },
    { id: 'mssql-trusted',  label: 'SQL Server (Windows / Integrated Auth)' },
    { id: 'csv-standard',    label: 'CSV / FlatFile (Standard Path & Delimiter)' },
    { id: 'csv-advanced',    label: 'CSV / FlatFile (TSV, Skip Header Rows)' },
  ],
  async mount(stage, fixtureId, ctx) {
    const initial = FIXTURES[fixtureId] || FIXTURES['mssql-standard'];
    const state = JSON.parse(JSON.stringify(initial));
    state.isSharedRef = false;
    state.sharedRefAlias = '';
    state.customOptions = state.customOptions || {};

    let testing = false;
    let testResults = null;
    let showDocDrawer = false;

    function render() {
      const isMssql = state.type === 'MSSQL';
      const isCsv = state.type === 'FLATFILE';
      const sqlCode = generateSql(state);
      const doc = CONNECTOR_DOCS[state.type] || CONNECTOR_DOCS.MSSQL;

      stage.innerHTML = `
        <style>
          .cw-shell {
            --cw-bg: var(--portal-surface, #ffffff);
            --cw-panel: var(--portal-surface-subtle, #f8fafc);
            --cw-border: var(--portal-border, #d9e0ea);
            --cw-text: var(--portal-text, #172033);
            --cw-text-muted: var(--portal-muted, #5a6778);
            --cw-input-bg: var(--portal-surface, #ffffff);
            --cw-code-bg: #090d16;
            --cw-code-text: #e2e8f0;
            --cw-accent: var(--portal-accent, #2563eb);
          }
          body.theme-dark .cw-shell, body.theme-midnight .cw-shell {
            --cw-bg: #0f172a;
            --cw-panel: #1e293b;
            --cw-border: #334155;
            --cw-text: #f1f5f9;
            --cw-text-muted: #94a3b8;
            --cw-input-bg: #090d16;
            --cw-code-bg: #050811;
            --cw-code-text: #f8fafc;
            --cw-accent: #38bdf8;
          }
          .cw-input, .cw-select {
            background: var(--cw-input-bg);
            border: 1px solid var(--cw-border);
            color: var(--cw-text);
            padding: 7px 10px;
            border-radius: 6px;
            font-size: 13px;
            box-sizing: border-box;
          }
          .cw-input:focus, .cw-select:focus {
            outline: 2px solid var(--cw-accent);
            border-color: transparent;
          }
          .cw-btn-outline {
            background: var(--cw-panel);
            border: 1px solid var(--cw-border);
            color: var(--cw-text);
            padding: 6px 12px;
            border-radius: 6px;
            font-size: 12px;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 6px;
          }
          .cw-btn-outline:hover {
            border-color: var(--cw-accent);
          }
          .cw-btn-primary {
            background: var(--cw-accent);
            color: #fff;
            border: none;
            padding: 7px 16px;
            border-radius: 6px;
            font-size: 12.5px;
            font-weight: 600;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 6px;
          }
          .cw-opt-chip {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            background: rgba(56, 189, 248, 0.12);
            border: 1px solid rgba(56, 189, 248, 0.3);
            color: var(--cw-accent);
            padding: 3px 8px;
            border-radius: 4px;
            font-family: monospace;
            font-size: 11px;
            cursor: pointer;
          }
          .cw-opt-chip:hover {
            background: rgba(56, 189, 248, 0.22);
          }
        </style>

        <div class="cw-shell" style="display:flex;flex-direction:column;height:100%;padding:20px;gap:16px;max-width:1280px;margin:0 auto;box-sizing:border-box;font-family:var(--portal-font, -apple-system, sans-serif);color:var(--cw-text);">
          
          <!-- Top Bar: Header & Actions -->
          <div style="display:flex;justify-content:space-between;align-items:center;padding-bottom:14px;border-bottom:1px solid var(--cw-border);">
            <div>
              <div style="display:flex;align-items:center;gap:10px;">
                <span style="font-size:22px;">🔌</span>
                <h2 style="margin:0;font-size:18px;font-weight:600;">Data Connection Wizard</h2>
                <span style="font-size:11px;font-weight:600;padding:2px 8px;border-radius:4px;background:rgba(56,189,248,0.12);color:var(--cw-accent);border:1px solid rgba(56,189,248,0.3);">Code-First</span>
              </div>
              <p style="margin:4px 0 0 32px;font-size:12.5px;color:var(--cw-text-muted);">Configure connection credentials, test live connectivity, and emit canonical <code style="background:rgba(125,125,125,0.12);padding:2px 5px;border-radius:3px;font-size:11.5px;">CREATE CONNECTION</code> syntax.</p>
            </div>
            
            <div style="display:flex;gap:8px;">
              <button id="btnToggleDocs" class="cw-btn-outline" type="button">
                📖 ${showDocDrawer ? 'Hide Reference' : 'Connector Reference'}
              </button>
              <button id="btnInsert" class="cw-btn-primary" type="button">
                <span>↳ Insert into Script</span>
              </button>
            </div>
          </div>

          <!-- Catalog Quick-Load Banner -->
          <div style="background:var(--cw-panel);border:1px solid var(--cw-border);border-radius:8px;padding:10px 14px;display:flex;align-items:center;justify-content:space-between;gap:12px;">
            <div style="display:flex;align-items:center;gap:8px;font-size:12.5px;">
              <span style="font-size:16px;">🗂️</span>
              <strong>Shared Catalog Template:</strong>
              <select id="selSharedCatalog" class="cw-select" style="padding:4px 8px;font-size:12px;width:240px;">
                <option value="">-- Choose from Catalog --</option>
                ${SHARED_CONNECTIONS_CATALOG.map(c => `<option value="${c.alias}">SHARED:${c.alias} (${c.type})</option>`).join('')}
              </select>
            </div>
            <div style="display:flex;gap:8px;">
              <button id="btnUseSharedRef" class="cw-btn-outline" style="font-size:11.5px;padding:4px 10px;" ${!state.sharedRefAlias ? 'disabled' : ''}>
                Reference as SHARED:${state.sharedRefAlias || 'alias'}
              </button>
              <button id="btnCloneShared" class="cw-btn-outline" style="font-size:11.5px;padding:4px 10px;" ${!state.sharedRefAlias ? 'disabled' : ''}>
                Fork to Local Script Options
              </button>
            </div>
          </div>

          <!-- Main Workspace: Form Left, Code/Preview Right -->
          <div style="display:grid;grid-template-columns:${showDocDrawer ? '1.1fr 1fr 300px' : '1.2fr 1fr'};gap:18px;flex:1;min-height:0;overflow:hidden;">
            
            <!-- Left Pane: Configuration Form -->
            <div style="background:var(--cw-panel);border:1px solid var(--cw-border);border-radius:8px;padding:18px;overflow-y:auto;display:flex;flex-direction:column;gap:16px;">
              
              <!-- Connector Type & Alias -->
              <div style="display:grid;grid-template-columns:1fr 1fr;gap:14px;">
                <div>
                  <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Connector Type</label>
                  <select id="selConnType" class="cw-select" style="width:100%;">
                    <option value="MSSQL" ${isMssql ? 'selected' : ''}>Microsoft SQL Server (MSSQL)</option>
                    <option value="FLATFILE" ${isCsv ? 'selected' : ''}>Flat File / Delimited (CSV, TSV)</option>
                    <option value="POSTGRES">PostgreSQL (POSTGRES)</option>
                    <option value="SNOWFLAKE">Snowflake (SNOWFLAKE)</option>
                    <option value="BIGQUERY">Google BigQuery (BIGQUERY)</option>
                  </select>
                </div>
                <div>
                  <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Connection Name / Alias</label>
                  <input id="inputAlias" class="cw-input" type="text" value="${state.alias || ''}" placeholder="e.g. dw_sales" style="width:100%;" />
                </div>
              </div>

              <!-- Shared Reference State Alert -->
              ${state.isSharedRef ? `
                <div style="background:rgba(56,189,248,0.1);border:1px solid rgba(56,189,248,0.3);border-radius:6px;padding:12px;display:flex;justify-content:space-between;align-items:center;">
                  <div>
                    <div style="font-weight:600;font-size:12.5px;color:var(--cw-accent);">Referencing Catalog Connection: SHARED:${state.sharedRefAlias}</div>
                    <div style="font-size:11.5px;color:var(--cw-text-muted);margin-top:2px;">Centralized governance and credential rotation will apply automatically.</div>
                  </div>
                  <button id="btnSwitchToCustom" class="cw-btn-outline" style="font-size:11px;padding:3px 8px;">Customize Options</button>
                </div>
              ` : ''}

              <!-- Dynamic Connector Fields (hidden if pure shared ref) -->
              ${!state.isSharedRef ? `
                ${isMssql ? renderMssqlFields() : ''}
                ${isCsv ? renderCsvFields() : ''}
                ${renderCustomOptionsBlock()}
              ` : ''}

              <!-- Test Connection Trigger Area -->
              <div style="margin-top:auto;padding-top:14px;border-top:1px solid var(--cw-border);display:flex;align-items:center;justify-content:space-between;">
                <div style="font-size:11.5px;color:var(--cw-text-muted);display:flex;align-items:center;gap:6px;">
                  <span>🔒 Zero-Trust Guardrail:</span>
                  <span>Egress policy checked before TCP handshake</span>
                </div>
                <button id="btnTestConn" class="cw-btn-outline" style="font-weight:600;border-color:${testing ? 'var(--cw-accent)' : 'var(--cw-border)'};" type="button">
                  ${testing ? '<span style="animation:spin 1s linear infinite;display:inline-block;">🔄</span> Probing...' : '⚡ Test Connection'}
                </button>
              </div>
            </div>

            <!-- Right Pane: Live Code & Diagnostics -->
            <div style="display:flex;flex-direction:column;gap:14px;overflow-y:auto;">
              
              <!-- Code-First SQL Output Box -->
              <div style="background:var(--cw-panel);border:1px solid var(--cw-border);border-radius:8px;padding:14px;display:flex;flex-direction:column;gap:10px;">
                <div style="display:flex;justify-content:space-between;align-items:center;">
                  <span style="font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;color:var(--cw-accent);">Generated ETL-SQL Syntax</span>
                  <button id="btnCopySql" style="background:none;border:none;color:var(--cw-text-muted);cursor:pointer;font-size:11.5px;display:flex;align-items:center;gap:4px;">
                    📋 Copy
                  </button>
                </div>
                <pre style="margin:0;padding:12px;background:var(--cw-code-bg);border:1px solid var(--cw-border);border-radius:6px;font-family:Consolas, 'Cascadia Code', monospace;font-size:12px;line-height:1.5;color:var(--cw-code-text);overflow-x:auto;white-space:pre-wrap;">${escapeHtml(sqlCode)}</pre>
              </div>

              <!-- Diagnostic Probe Card (Live Test Results) -->
              <div style="background:var(--cw-panel);border:1px solid var(--cw-border);border-radius:8px;padding:14px;display:flex;flex-direction:column;gap:10px;">
                <div style="display:flex;justify-content:space-between;align-items:center;">
                  <span style="font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;color:var(--cw-text-muted);">Layered Diagnostics</span>
                  <span id="testStatusBadge" style="font-size:11px;font-weight:600;padding:2px 7px;border-radius:4px;${getBadgeStyle()}">
                    ${testResults ? (testResults.succeeded ? '✓ Connected' : '✕ Failed') : 'Ready to Test'}
                  </span>
                </div>

                <div id="diagnosticStepsList" style="display:flex;flex-direction:column;gap:8px;">
                  ${renderDiagnosticSteps()}
                </div>
              </div>

              <!-- Metadata Introspection Preview Card -->
              <div style="background:var(--cw-panel);border:1px solid var(--cw-border);border-radius:8px;padding:14px;display:flex;flex-direction:column;gap:10px;">
                <div style="display:flex;justify-content:space-between;align-items:center;">
                  <span style="font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;color:var(--cw-text-muted);">Introspected Schema Preview</span>
                  <span style="font-size:11px;color:var(--cw-text-muted);">${isMssql ? 'Tables & Views' : 'Columns Detected'}</span>
                </div>

                <div style="background:var(--cw-code-bg);border:1px solid var(--cw-border);border-radius:6px;padding:10px;font-size:12px;">
                  ${renderSchemaPreview()}
                </div>
              </div>

            </div>

            <!-- Optional 3rd Column: Driver Reference Drawer -->
            ${showDocDrawer ? `
              <div style="background:var(--cw-panel);border:1px solid var(--cw-border);border-radius:8px;padding:16px;overflow-y:auto;display:flex;flex-direction:column;gap:14px;">
                <div>
                  <h3 style="margin:0 0 4px;font-size:14px;font-weight:600;">${doc.title}</h3>
                  <p style="margin:0;font-size:11.5px;color:var(--cw-text-muted);line-height:1.4;">${doc.description}</p>
                </div>

                <div style="border-top:1px solid var(--cw-border);padding-top:10px;">
                  <div style="font-size:12px;font-weight:600;margin-bottom:8px;">All Supported Options:</div>
                  <div style="display:flex;flex-direction:column;gap:8px;">
                    ${doc.allOptions.map(opt => `
                      <div style="border-bottom:1px solid var(--cw-border);padding-bottom:6px;">
                        <div style="display:flex;justify-content:space-between;align-items:center;">
                          <span class="cw-opt-chip btn-add-option" data-opt="${opt.name}" title="Click to add to connection options">+ ${opt.name}</span>
                          <span style="font-size:10px;color:var(--cw-text-muted);">${opt.mandatory ? 'Mandatory' : 'Optional'}</span>
                        </div>
                        <div style="font-size:11px;color:var(--cw-text-muted);margin-top:4px;">${opt.desc}</div>
                      </div>
                    `).join('')}
                  </div>
                </div>
              </div>
            ` : ''}

          </div>

        </div>
      `;

      attachEventListeners();
    }

    function renderMssqlFields() {
      return `
        <!-- Server & Port -->
        <div style="display:grid;grid-template-columns:3fr 1fr;gap:12px;">
          <div>
            <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Server / Hostname</label>
            <input id="inputServer" class="cw-input" type="text" value="${state.server || ''}" placeholder="sql01.corp.internal or localhost" style="width:100%;" />
          </div>
          <div>
            <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Port</label>
            <input id="inputPort" class="cw-input" type="number" value="${state.port || 1433}" style="width:100%;" />
          </div>
        </div>

        <!-- Database Name -->
        <div>
          <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Database Name</label>
          <input id="inputDatabase" class="cw-input" type="text" value="${state.database || ''}" placeholder="e.g. SalesDW" style="width:100%;" />
        </div>

        <!-- Authentication Mode -->
        <div style="background:var(--cw-bg);border:1px solid var(--cw-border);border-radius:6px;padding:12px;display:flex;flex-direction:column;gap:10px;">
          <label style="display:block;font-size:12px;font-weight:600;">Authentication</label>
          <div style="display:flex;gap:18px;">
            <label style="font-size:12.5px;display:flex;align-items:center;gap:6px;cursor:pointer;">
              <input type="radio" name="authMode" value="SQL" ${state.authMode === 'SQL' ? 'checked' : ''} /> SQL Authentication
            </label>
            <label style="font-size:12.5px;display:flex;align-items:center;gap:6px;cursor:pointer;">
              <input type="radio" name="authMode" value="TRUSTED" ${state.authMode === 'TRUSTED' ? 'checked' : ''} /> Integrated / Windows (TRUSTED_CONNECTION)
            </label>
          </div>

          ${state.authMode === 'SQL' ? `
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:4px;">
              <div>
                <label style="display:block;font-size:11.5px;margin-bottom:4px;color:var(--cw-text-muted);">Username</label>
                <input id="inputUser" class="cw-input" type="text" value="${state.user || ''}" placeholder="app_user" style="width:100%;" />
              </div>
              <div>
                <label style="display:block;font-size:11.5px;margin-bottom:4px;color:var(--cw-text-muted);">Password Storage Method</label>
                <select id="selSecretMode" class="cw-select" style="width:100%;">
                  <option value="SECRET" ${state.secretMode === 'SECRET' ? 'selected' : ''}>Secret Vault (SECRET:name)</option>
                  <option value="ENV" ${state.secretMode === 'ENV' ? 'selected' : ''}>Environment ($ENV{KEY})</option>
                  <option value="RAW" ${state.secretMode === 'RAW' ? 'selected' : ''}>⚠️ Raw String (Unsafe)</option>
                </select>
              </div>
            </div>
            <div>
              <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px;">
                <label style="font-size:11.5px;color:var(--cw-text-muted);">
                  ${state.secretMode === 'SECRET' ? 'Shared Secret Key' : (state.secretMode === 'ENV' ? 'Environment Variable Name' : 'Raw Password')}
                </label>
                ${state.secretMode === 'SECRET' ? `<span style="font-size:10.5px;color:var(--cw-accent);">🔍 Pick from Vault</span>` : ''}
              </div>
              
              ${state.secretMode === 'SECRET' ? `
                <div style="display:flex;gap:8px;">
                  <select id="selSecretDropdown" class="cw-select" style="flex:1;">
                    <option value="">-- Select Existing Secret from Vault --</option>
                    ${SHARED_SECRETS_CATALOG.map(s => `<option value="${s.name}" ${state.secretKey === s.name ? 'selected' : ''}>${s.name} (${s.description})</option>`).join('')}
                  </select>
                  <input id="inputSecretKey" class="cw-input" type="text" value="${state.secretKey || ''}" placeholder="Or type key name" style="width:160px;" />
                </div>
              ` : `
                <input id="inputSecretKey" class="cw-input" type="${state.secretMode === 'RAW' ? 'password' : 'text'}" value="${state.secretKey || ''}" placeholder="${state.secretMode === 'ENV' ? 'e.g. DB_PASSWORD' : '••••••••'}" style="width:100%;" />
              `}
            </div>
          ` : ''}
        </div>

        <!-- Standard Options Accordion -->
        <details style="background:var(--cw-bg);border:1px solid var(--cw-border);border-radius:6px;padding:10px;">
          <summary style="font-size:12px;font-weight:600;cursor:pointer;color:var(--cw-text-muted);user-select:none;">Connection Tuning & Security Options</summary>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:10px;">
            <div>
              <label style="display:block;font-size:11px;margin-bottom:4px;color:var(--cw-text-muted);">Timeout (Seconds)</label>
              <input id="inputTimeout" class="cw-input" type="number" value="${state.timeoutSeconds || 30}" style="width:100%;" />
            </div>
            <div>
              <label style="display:block;font-size:11px;margin-bottom:4px;color:var(--cw-text-muted);">Application Name</label>
              <input id="inputAppName" class="cw-input" type="text" value="${state.applicationName || 'ReportBuilder'}" style="width:100%;" />
            </div>
            <div style="grid-column:span 2;display:flex;gap:18px;margin-top:4px;">
              <label style="font-size:12px;display:flex;align-items:center;gap:6px;cursor:pointer;">
                <input id="chkTrustCert" type="checkbox" ${state.trustServerCert ? 'checked' : ''} /> Trust Server Certificate
              </label>
              <label style="font-size:12px;display:flex;align-items:center;gap:6px;cursor:pointer;">
                <input id="chkEncrypt" type="checkbox" ${state.encrypt ? 'checked' : ''} /> Encrypt TLS
              </label>
            </div>
          </div>
        </details>
      `;
    }

    function renderCsvFields() {
      return `
        <!-- File Path -->
        <div>
          <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">File Path</label>
          <div style="display:flex;gap:8px;">
            <input id="inputPath" class="cw-input" type="text" value="${state.path || ''}" placeholder="C:/data/sales.csv or /data/sales.csv" style="flex:1;" />
            <button id="btnBrowseFile" class="cw-btn-outline" type="button">Browse...</button>
          </div>
        </div>

        <!-- Delimiter & Header Options -->
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;">
          <div>
            <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Delimiter</label>
            <select id="selDelimiter" class="cw-select" style="width:100%;">
              <option value="," ${state.delimiter === ',' ? 'selected' : ''}>Comma ( , )</option>
              <option value="\\t" ${state.delimiter === '\\t' ? 'selected' : ''}>Tab ( \\t )</option>
              <option value=";" ${state.delimiter === ';' ? 'selected' : ''}>Semicolon ( ; )</option>
              <option value="|" ${state.delimiter === '|' ? 'selected' : ''}>Pipe ( | )</option>
            </select>
          </div>
          <div>
            <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;">Skip Header Rows</label>
            <input id="inputSkipRows" class="cw-input" type="number" value="${state.skipRows || 0}" style="width:100%;" />
          </div>
        </div>

        <!-- Header Checkbox -->
        <div style="background:var(--cw-bg);border:1px solid var(--cw-border);border-radius:6px;padding:10px 14px;">
          <label style="font-size:12.5px;display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input id="chkHeader" type="checkbox" ${state.header ? 'checked' : ''} />
            <span><strong>First Row Contains Column Names (HEADER = ON)</strong></span>
          </label>
        </div>

        <!-- Advanced File Options Accordion -->
        <details style="background:var(--cw-bg);border:1px solid var(--cw-border);border-radius:6px;padding:10px;">
          <summary style="font-size:12px;font-weight:600;cursor:pointer;color:var(--cw-text-muted);user-select:none;">Encoding & Quote Options</summary>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:10px;">
            <div>
              <label style="display:block;font-size:11px;margin-bottom:4px;color:var(--cw-text-muted);">Encoding</label>
              <select id="selEncoding" class="cw-select" style="width:100%;">
                <option value="UTF-8" ${state.encoding === 'UTF-8' ? 'selected' : ''}>UTF-8</option>
                <option value="ASCII" ${state.encoding === 'ASCII' ? 'selected' : ''}>ASCII</option>
                <option value="Windows-1252" ${state.encoding === 'Windows-1252' ? 'selected' : ''}>Windows-1252</option>
              </select>
            </div>
            <div>
              <label style="display:block;font-size:11px;margin-bottom:4px;color:var(--cw-text-muted);">Quote Character</label>
              <input id="inputQuoteChar" class="cw-input" type="text" value="${state.quoteChar || '"'}" style="width:100%;" />
            </div>
          </div>
        </details>
      `;
    }

    function renderCustomOptionsBlock() {
      const keys = Object.keys(state.customOptions || {});
      if (keys.length === 0) return '';

      return `
        <div style="background:var(--cw-bg);border:1px solid var(--cw-border);border-radius:6px;padding:12px;display:flex;flex-direction:column;gap:8px;">
          <div style="font-size:12px;font-weight:600;">Custom Driver Options (from Reference)</div>
          ${keys.map(k => `
            <div style="display:flex;align-items:center;gap:8px;">
              <span style="font-family:monospace;font-size:12px;font-weight:600;width:160px;">${k}:</span>
              <input type="text" class="cw-input input-custom-opt" data-key="${k}" value="${state.customOptions[k]}" style="flex:1;" />
              <button type="button" class="cw-btn-outline btn-del-opt" data-key="${k}" style="padding:4px 8px;font-size:11px;color:#f87171;">✕</button>
            </div>
          `).join('')}
        </div>
      `;
    }

    function renderDiagnosticSteps() {
      if (testing) {
        return `
          <div style="padding:10px;font-size:12px;color:var(--cw-accent);display:flex;align-items:center;gap:8px;">
            <span style="animation:spin 1s linear infinite;display:inline-block;">🔄</span> Executing zero-trust connection probe sequence...
          </div>
        `;
      }

      if (!testResults) {
        return `
          <div style="padding:8px 0;font-size:12px;color:var(--cw-text-muted);font-style:italic;">
            Click "Test Connection" to probe DNS resolution, TCP handshake, security policy, and auth.
          </div>
        `;
      }

      return testResults.steps.map(s => `
        <div style="display:flex;align-items:flex-start;gap:8px;font-size:12px;padding:4px 0;">
          <span style="font-size:14px;color:${s.status === 'ok' ? '#4ade80' : (s.status === 'failed' ? '#f87171' : '#94a3b8')};">
            ${s.status === 'ok' ? '✓' : (s.status === 'failed' ? '✕' : '○')}
          </span>
          <div>
            <div style="font-weight:600;color:var(--cw-text);">${s.layer}: <span style="font-weight:400;color:var(--cw-text-muted);">${s.detail}</span></div>
            ${s.remedy ? `<div style="font-size:11px;color:#fca5a5;margin-top:2px;">💡 ${s.remedy}</div>` : ''}
          </div>
        </div>
      `).join('');
    }

    function renderSchemaPreview() {
      if (!testResults || !testResults.succeeded) {
        return `<span style="color:var(--cw-text-muted);font-style:italic;">Schema introspection will display available tables / columns once connection test succeeds.</span>`;
      }

      if (state.type === 'MSSQL') {
        const tables = [
          { name: 'dbo.FactSales', cols: 'Date, CustomerId, ProductId, Amount, Qty' },
          { name: 'dbo.DimCustomer', cols: 'CustomerId, Name, Region, Tier' },
          { name: 'dbo.DimProduct', cols: 'ProductId, SKU, Category, Cost' },
        ];
        return `
          <div style="display:flex;flex-direction:column;gap:6px;">
            ${tables.map(t => `
              <div style="display:flex;justify-content:space-between;align-items:center;padding:4px 6px;border-radius:4px;background:rgba(125,125,125,0.06);">
                <div>
                  <span style="color:var(--cw-accent);font-family:monospace;font-weight:600;">${t.name}</span>
                  <div style="font-size:11px;color:var(--cw-text-muted);">${t.cols}</div>
                </div>
                <button class="btn-sample-query cw-btn-outline" data-table="${t.name}" style="font-size:10.5px;padding:2px 6px;cursor:pointer;">+ Query</button>
              </div>
            `).join('')}
          </div>
        `;
      }

      if (state.type === 'FLATFILE') {
        const cols = [
          { name: 'Date', type: 'DATE' },
          { name: 'Vendor', type: 'VARCHAR' },
          { name: 'Region', type: 'VARCHAR' },
          { name: 'TotalAmount', type: 'DECIMAL' },
          { name: 'Units', type: 'INT' }
        ];
        return `
          <div style="display:flex;flex-wrap:wrap;gap:6px;">
            ${cols.map(c => `
              <span style="background:rgba(56,189,248,0.1);border:1px solid rgba(56,189,248,0.25);color:var(--cw-accent);padding:2px 7px;border-radius:4px;font-family:monospace;font-size:11.5px;">
                ${c.name} <span style="color:var(--cw-text-muted);font-size:10px;">(${c.type})</span>
              </span>
            `).join('')}
          </div>
        `;
      }

      return '';
    }

    function getBadgeStyle() {
      if (!testResults) return 'background:rgba(148,163,184,0.1);color:var(--cw-text-muted);border:1px solid rgba(148,163,184,0.2);';
      if (testResults.succeeded) return 'background:rgba(74,222,128,0.15);color:#4ade80;border:1px solid rgba(74,222,128,0.3);';
      return 'background:rgba(248,113,113,0.15);color:#f87171;border:1px solid rgba(248,113,113,0.3);';
    }

    function escapeHtml(str) {
      return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function attachEventListeners() {
      // Toggle Reference Drawer
      const btnToggleDocs = stage.querySelector('#btnToggleDocs');
      if (btnToggleDocs) {
        btnToggleDocs.onclick = () => {
          showDocDrawer = !showDocDrawer;
          render();
        };
      }

      // Shared Catalog Dropdown
      const selSharedCatalog = stage.querySelector('#selSharedCatalog');
      if (selSharedCatalog) {
        selSharedCatalog.onchange = (e) => {
          state.sharedRefAlias = e.target.value;
          render();
        };
      }

      const btnUseSharedRef = stage.querySelector('#btnUseSharedRef');
      if (btnUseSharedRef) {
        btnUseSharedRef.onclick = () => {
          if (!state.sharedRefAlias) return;
          state.isSharedRef = true;
          state.alias = state.sharedRefAlias;
          render();
        };
      }

      const btnCloneShared = stage.querySelector('#btnCloneShared');
      if (btnCloneShared) {
        btnCloneShared.onclick = () => {
          if (!state.sharedRefAlias) return;
          const template = SHARED_CONNECTIONS_CATALOG.find(c => c.alias === state.sharedRefAlias);
          if (template) {
            state.isSharedRef = false;
            state.type = template.type;
            state.alias = `${template.alias}_local`;
            if (template.type === 'MSSQL') {
              state.server = 'sql01.corp.internal';
              state.database = 'SalesDW';
              state.secretMode = 'SECRET';
              state.secretKey = 'SQL_READER_PW';
            }
          }
          render();
        };
      }

      const btnSwitchToCustom = stage.querySelector('#btnSwitchToCustom');
      if (btnSwitchToCustom) {
        btnSwitchToCustom.onclick = () => {
          state.isSharedRef = false;
          render();
        };
      }

      const selType = stage.querySelector('#selConnType');
      if (selType) {
        selType.onchange = (e) => {
          state.type = e.target.value;
          state.customOptions = {};
          testResults = null;
          render();
        };
      }

      const inputAlias = stage.querySelector('#inputAlias');
      if (inputAlias) inputAlias.oninput = (e) => { state.alias = e.target.value; updateSqlPreview(); };

      // Secret vault dropdown listener
      const selSecretDropdown = stage.querySelector('#selSecretDropdown');
      if (selSecretDropdown) {
        selSecretDropdown.onchange = (e) => {
          state.secretKey = e.target.value;
          const inputKey = stage.querySelector('#inputSecretKey');
          if (inputKey) inputKey.value = e.target.value;
          updateSqlPreview();
        };
      }

      // Add custom option from docs drawer
      const addOptionBtns = stage.querySelectorAll('.btn-add-option');
      addOptionBtns.forEach(btn => {
        btn.onclick = () => {
          const optName = btn.getAttribute('data-opt');
          if (!state.customOptions) state.customOptions = {};
          if (state.customOptions[optName] === undefined) {
            state.customOptions[optName] = 'ON';
            render();
          }
        };
      });

      // Custom option edits & deletion
      const customInputs = stage.querySelectorAll('.input-custom-opt');
      customInputs.forEach(input => {
        input.oninput = (e) => {
          const k = input.getAttribute('data-key');
          state.customOptions[k] = e.target.value;
          updateSqlPreview();
        };
      });

      const delOptionBtns = stage.querySelectorAll('.btn-del-opt');
      delOptionBtns.forEach(btn => {
        btn.onclick = () => {
          const k = btn.getAttribute('data-key');
          delete state.customOptions[k];
          render();
        };
      });

      // MSSQL listeners
      const inputServer = stage.querySelector('#inputServer');
      if (inputServer) inputServer.oninput = (e) => { state.server = e.target.value; updateSqlPreview(); };

      const inputPort = stage.querySelector('#inputPort');
      if (inputPort) inputPort.oninput = (e) => { state.port = e.target.value; updateSqlPreview(); };

      const inputDatabase = stage.querySelector('#inputDatabase');
      if (inputDatabase) inputDatabase.oninput = (e) => { state.database = e.target.value; updateSqlPreview(); };

      const authRadios = stage.querySelectorAll('input[name="authMode"]');
      authRadios.forEach(r => {
        r.onchange = (e) => {
          state.authMode = e.target.value;
          render();
        };
      });

      const inputUser = stage.querySelector('#inputUser');
      if (inputUser) inputUser.oninput = (e) => { state.user = e.target.value; updateSqlPreview(); };

      const selSecretMode = stage.querySelector('#selSecretMode');
      if (selSecretMode) selSecretMode.onchange = (e) => {
        state.secretMode = e.target.value;
        render();
      };

      const inputSecretKey = stage.querySelector('#inputSecretKey');
      if (inputSecretKey) inputSecretKey.oninput = (e) => { state.secretKey = e.target.value; updateSqlPreview(); };

      const inputTimeout = stage.querySelector('#inputTimeout');
      if (inputTimeout) inputTimeout.oninput = (e) => { state.timeoutSeconds = e.target.value; updateSqlPreview(); };

      const inputAppName = stage.querySelector('#inputAppName');
      if (inputAppName) inputAppName.oninput = (e) => { state.applicationName = e.target.value; updateSqlPreview(); };

      const chkTrustCert = stage.querySelector('#chkTrustCert');
      if (chkTrustCert) chkTrustCert.onchange = (e) => { state.trustServerCert = e.target.checked; updateSqlPreview(); };

      const chkEncrypt = stage.querySelector('#chkEncrypt');
      if (chkEncrypt) chkEncrypt.onchange = (e) => { state.encrypt = e.target.checked; updateSqlPreview(); };

      // CSV listeners
      const inputPath = stage.querySelector('#inputPath');
      if (inputPath) inputPath.oninput = (e) => { state.path = e.target.value; updateSqlPreview(); };

      const selDelimiter = stage.querySelector('#selDelimiter');
      if (selDelimiter) selDelimiter.onchange = (e) => { state.delimiter = e.target.value; updateSqlPreview(); };

      const inputSkipRows = stage.querySelector('#inputSkipRows');
      if (inputSkipRows) inputSkipRows.oninput = (e) => { state.skipRows = e.target.value; updateSqlPreview(); };

      const chkHeader = stage.querySelector('#chkHeader');
      if (chkHeader) chkHeader.onchange = (e) => { state.header = e.target.checked; updateSqlPreview(); };

      const selEncoding = stage.querySelector('#selEncoding');
      if (selEncoding) selEncoding.onchange = (e) => { state.encoding = e.target.value; updateSqlPreview(); };

      const inputQuoteChar = stage.querySelector('#inputQuoteChar');
      if (inputQuoteChar) inputQuoteChar.oninput = (e) => { state.quoteChar = e.target.value; updateSqlPreview(); };

      // Test connection button
      const btnTest = stage.querySelector('#btnTestConn');
      if (btnTest) {
        btnTest.onclick = async () => {
          testing = true;
          render();
          ctx.stat(`Probing connection to ${state.alias || 'unnamed'} (${state.type})...`);

          await new Promise(r => setTimeout(r, 600));
          testing = false;

          if (state.type === 'MSSQL') {
            testResults = {
              succeeded: true,
              steps: [
                { layer: 'POLICY', status: 'ok', detail: 'Host permitted by active egress policy.' },
                { layer: 'DNS', status: 'ok', detail: `'${state.server || 'sql01'}' resolved to 10.2.4.9.` },
                { layer: 'TCP', status: 'ok', detail: `Port ${state.port || 1433} reachable (RTT: 4ms).` },
                { layer: 'AUTH', status: 'ok', detail: state.authMode === 'TRUSTED' ? 'Windows integrated token accepted.' : `User '${state.user || 'reader'}' authenticated via secret vault.` }
              ]
            };
          } else {
            testResults = {
              succeeded: true,
              steps: [
                { layer: 'POLICY', status: 'ok', detail: 'File path within allowed tenant filesystem boundary.' },
                { layer: 'FS_RESOLVE', status: 'ok', detail: `Resolved file path: ${state.path || 'file.csv'} (Size: 4.8 MB).` },
                { layer: 'PARSER', status: 'ok', detail: `Delimited parser verified ${state.header ? 'header row + ' : ''}schema successfully.` }
              ]
            };
          }

          render();
          ctx.stat(`✓ Connection test passed for ${state.alias || 'connection'} (${state.type})`);
        };
      }

      // Copy SQL button
      const btnCopy = stage.querySelector('#btnCopySql');
      if (btnCopy) {
        btnCopy.onclick = async () => {
          try {
            await navigator.clipboard.writeText(generateSql(state));
            btnCopy.textContent = '✓ Copied!';
            setTimeout(() => { btnCopy.textContent = '📋 Copy'; }, 1500);
          } catch (e) {
            btnCopy.textContent = 'Copied (simulated)';
          }
        };
      }

      // Insert button
      const btnInsert = stage.querySelector('#btnInsert');
      if (btnInsert) {
        btnInsert.onclick = () => {
          ctx.stat(`Inserted CREATE CONNECTION ${state.alias || ''} into active script buffer.`);
          btnInsert.innerHTML = '<span>✓ Inserted!</span>';
          setTimeout(() => {
            btnInsert.innerHTML = '<span>↳ Insert into Script</span>';
          }, 1500);
        };
      }

      // Sample query buttons
      const sampleBtns = stage.querySelectorAll('.btn-sample-query');
      sampleBtns.forEach(btn => {
        btn.onclick = () => {
          const tbl = btn.getAttribute('data-table');
          ctx.stat(`Generated: CREATE DATASET &${state.alias}_data AS (SELECT * FROM ${state.alias}.${tbl});`);
        };
      });
    }

    function updateSqlPreview() {
      const pre = stage.querySelector('pre');
      if (pre) pre.textContent = generateSql(state);
    }

    render();
    ctx.stat(`Connection Wizard mounted · Fixture: ${fixtureId}`);

    return {
      dispose() {}
    };
  }
};
