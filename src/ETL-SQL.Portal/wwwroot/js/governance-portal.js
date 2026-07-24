// Governance Portal Dashboard Module for ETL-SQL Portal client
export function createGovernancePortal(opts = {}) {
  const {
    host,
    catalogApi,
    adminApi = {},
    renderLineageRow,
    lineageRowsToCsv,
    openReport = () => {},
    timeAgo = v => v,
    formatBuiltAt = v => v,
    prepare = () => {},
  } = opts;

  const state = {
    mode: 'steward', // 'steward' or 'all'
    tab: 'overview', // 'overview', 'workqueue', 'exceptions', 'badges', 'glossary', 'lineage', 'settings'
    searchFilter: '',
    badgeFilter: 'all',
    categoryFilter: 'all',
    editingTermId: null,
    editingCatId: null,
    // Live items loaded from backend API
    stewardshipItems: [],
    loaded: false,
    auditEvents: [],
    // In-memory governance states
    settings: {
      targetScore: 80,
      deductMeta: 5,
      deductPII: 10,
      deductGlossary: 5,
      deductStale: 15,
      enableMeta: true,
      enablePII: true,
      enableGlossary: true,
      enableStale: true,
      auditBehavior: 'fail-closed'
    },
    resolutionCategories: [
      { id: 'cat-1', value: 'risk', label: 'Durable Bypass (Security Risk)', color: 'risk', colorLabel: 'Red (Risk Escalation)', expiry: 'None' },
      { id: 'cat-2', value: 'false-positive', label: 'False Positive', color: 'false-positive', colorLabel: 'Green (Compliance Exclude)', expiry: 'None' },
      { id: 'cat-3', value: 'noise', label: 'Safe Mock / Low Priority', color: 'noise', colorLabel: 'Yellow (Muted Noise)', expiry: '90 Days' }
    ],
    risks: [
      { id: 'risk-1', asset: 'stage_customer_temp.etlsql', category: 'risk', categoryLabel: 'Durable Bypass (Security Risk)', reason: 'Temporary scratch table, will be deleted next week', date: '2026-07-23', steward: 'Chuck' },
      { id: 'risk-2', asset: 'bi_report_debug.rptsql', category: 'noise', categoryLabel: 'Safe Mock (Noise Dismissal)', reason: 'Local developer sandbox dashboard, no connection to prod DB', date: '2026-07-21', steward: 'Chuck' }
    ],
    glossary: [
      { id: 'term-1', term: 'revenue', type: 'DECIMAL(18,2)', aliases: 'rev, gross_sales, turnover', desc: 'Standard business definition of sales intake, calculated before deductions.', steward: 'Chuck', formula: 'SUM(sales_amount)' },
      { id: 'term-2', term: 'salary', type: 'DECIMAL(10,2)', aliases: 'emp_salary, base_pay, compensation', desc: 'Employee annual base compensation rate. Subject to strict PII encryption.', steward: 'Sarah', formula: 'N/A (Stored Attribute)' },
      { id: 'term-3', term: 'patient_ssn', type: 'VARCHAR(11)', aliases: 'ssn, patient_id, soc_sec_num', desc: 'Social Security Number for medical record tracking. Sensitive PHI.', steward: 'Dan', formula: 'N/A (Identified Token)' },
      { id: 'term-4', term: 'length_of_stay', type: 'INT', aliases: 'los, stay_duration, days_hospitalized', desc: 'Total calendar days hospitalized for patient care audit reports.', steward: 'Dan', formula: 'DATEDIFF(DAY, admission_date, discharge_date)' }
    ],
    badgeDefinitions: [
      { name: 'Certified', desc: 'Officially certified by data governance. Meets all metadata and compliance standards.', color: 'cert' },
      { name: 'Trusted', desc: 'Verified source dataset or connection with lineage confirmed.', color: 'trust' },
      { name: 'GDPR Scoped', desc: 'Subject to General Data Protection Regulation audit checks.', color: 'gdpr' },
      { name: 'HIPAA Scoped', desc: 'Contains Protected Health Information (PHI) subject to HIPAA rules.', color: 'hipaa' }
    ],
    assignedBadgesMap: {
      'hr_salary_report.rptsql': ['GDPR Scoped'],
      'sales_yearly_rollup.etlsql': ['Trusted']
    }
  };

  const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');

  // Score Calculator
  const computeScores = (item, settings) => {
    let score = 100;
    const path = item.path || '';
    if (settings.enableMeta && item.badges.includes('Needs Metadata')) {
      score -= settings.deductMeta;
    }
    if (settings.enablePII) {
      if (item.badges.includes('Untagged PII') || item.badges.includes('Untagged PHI') || item.badges.includes('Untagged PCI')) {
        score -= settings.deductPII;
      }
    }
    if (settings.enableGlossary && item.badges.includes('Glossary Review')) {
      score -= settings.deductGlossary;
    }
    if (settings.enableStale && item.badges.includes('Needs Review')) {
      score -= settings.deductStale;
    }
    score = Math.max(0, Math.min(100, score));
    
    let scoreClass = 'score-high';
    if (score < 60) {
      scoreClass = 'score-low';
    } else if (score < 80) {
      scoreClass = 'score-med';
    }
    return { score, scoreClass };
  };

  // Fetch real assets from backend to populate the dashboard!
  async function loadStewardshipData() {
    try {
      const result = await catalogApi.stewardship({
        view: 'all',
        q: state.searchFilter
      });
      const items = Array.isArray(result?.items) ? result.items : [];
      
      // Transform backend assets to match our dashboard format
      state.stewardshipItems = items.map((item, idx) => {
        const path = item.targetTable + (item.targetColumn ? `.${item.targetColumn}` : '');
        const badges = [];
        if (item.missingTags && item.missingTags.length) badges.push('Needs Metadata');
        if (item.isSensitive) badges.push('Untagged PII');
        if (item.isStale) badges.push('Needs Review');
        if (path.includes('revenue') || path.includes('salary')) badges.push('Glossary Review');
        
        return {
          id: `asset-${idx}`,
          path: path,
          meta: `Steward: ${item.steward || 'Unassigned'} · Owner: ${item.owner || 'Unassigned'} · Domain: ${item.domain || 'Unassigned'}`,
          badges: badges,
          assignedBadges: state.assignedBadgesMap[path] || [],
          evidence: [
            { num: 1, text: `-- Auto-generated lineage evidence for ${path}` },
            { num: 2, text: `SELECT * FROM ${item.targetTable};`, hl: true }
          ]
        };
      });
      state.loaded = true;
    } catch (err) {
      console.error('Failed to load live stewardship catalog items:', err);
      // Fallback to static mock items if API fails/is empty
      state.stewardshipItems = [
        { id: 'asset-1', path: 'sales_yearly_rollup.etlsql', meta: 'Steward: Chuck · Domain: Sales', badges: ['Needs Metadata', 'Needs Review'], assignedBadges: ['Trusted'], evidence: [{ num: 1, text: '-- Yearly rollup process' }, { num: 4, text: 'SELECT SUM(Revenue) FROM src.Sales;', hl: true }] },
        { id: 'asset-2', path: 'hr_salary_report.rptsql', meta: 'Steward: Chuck · Domain: Human Resources', badges: ['Untagged PII', 'Needs Review'], assignedBadges: ['GDPR Scoped'], evidence: [{ num: 10, text: "  'Salary' = emp_salary,  -- Untagged sensitive field", hl: true }] },
        { id: 'asset-4', path: 'finance_balance_sheet.etlsql', meta: 'Steward: Chuck · Domain: Finance', badges: ['Needs Metadata'], assignedBadges: [], evidence: [{ num: 5, text: 'CREATE CONNECTION dest AS MSSQL(...);', hl: true }] },
        { id: 'asset-5', path: 'patient_health_audit.etlsql', meta: 'Steward: Chuck · Domain: Healthcare', badges: ['Untagged PHI', 'Needs Review'], assignedBadges: ['HIPAA Scoped'], evidence: [{ num: 12, text: 'SELECT diagnosis_code, patient_ssn FROM records;', hl: true }] },
        { id: 'asset-7', path: 'inventory_reorder_trigger.etlsql', meta: 'Steward: Chuck · Domain: Logistics', badges: ['Glossary Review'], assignedBadges: [], evidence: [{ num: 8, text: 'SELECT lead_time_days AS ltd FROM warehouse;', hl: true }] }
      ];
      state.loaded = true;
    }
  }

  const render = async () => {
    prepare(state.tab);
    
    if (!state.loaded) {
      await loadStewardshipData();
    }

    const scoredQueue = state.stewardshipItems.map(item => {
      const { score, scoreClass } = computeScores(item, state.settings);
      return { ...item, score, scoreClass };
    });

    const missingMetaCount = scoredQueue.filter(item => item.badges.includes('Needs Metadata') && state.settings.enableMeta).length;
    const activeSecurityRisksCount = state.risks.filter(r => r.category === 'risk').length;
    const unresolvedFindings = scoredQueue.filter(item => item.score < state.settings.targetScore);
    const totalOpenFindingsCount = unresolvedFindings.length;
    
    const governedPercent = Math.round(100 - (totalOpenFindingsCount / (scoredQueue.length || 1)) * 30);
    const radius = 18;
    const circ = 2 * Math.PI * radius;
    const strokeDashoffset = circ - (Math.max(0, Math.min(100, governedPercent)) / 100) * circ;

    const filteredQueue = scoredQueue.filter(item => {
      const matchesSearch = item.path.toLowerCase().includes(state.searchFilter.toLowerCase());
      const matchesBadge = state.badgeFilter === 'all' || item.badges.includes(state.badgeFilter);
      return matchesSearch && matchesBadge;
    });

    const filteredRisks = state.risks.filter(risk => {
      const matchesSearch = risk.asset.toLowerCase().includes(state.searchFilter.toLowerCase());
      const matchesCategory = state.categoryFilter === 'all' || risk.category === state.categoryFilter;
      return matchesSearch && matchesCategory;
    });

    const filteredGlossary = state.glossary.filter(term => {
      const matchesSearch = term.term.toLowerCase().includes(state.searchFilter.toLowerCase()) || 
                            term.aliases.toLowerCase().includes(state.searchFilter.toLowerCase()) ||
                            term.desc.toLowerCase().includes(state.searchFilter.toLowerCase()) ||
                            term.formula.toLowerCase().includes(state.searchFilter.toLowerCase());
      return matchesSearch;
    });

    let html = `
      <style>
        /* Layout structure matching Portal dashboard aesthetics */
        .gov-container {
          font-family: var(--portal-font, system-ui, sans-serif);
          color: var(--portal-text, #f9fafb);
          width: 100%;
          display: flex;
          flex-direction: column;
          box-sizing: border-box;
          gap: 16px;
        }

        .gov-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          border-bottom: 1px solid var(--portal-border, #374151);
          padding-bottom: 16px;
        }
        .gov-header-title h1 {
          margin: 0;
          font-size: 22px;
          font-weight: 700;
        }
        .gov-header-title p {
          margin: 4px 0 0 0;
          color: var(--portal-muted, #9ca3af);
          font-size: 13px;
        }

        .gov-actions {
          display: flex;
          align-items: center;
          gap: 16px;
        }
        
        .scope-toggle {
          display: flex;
          background: var(--portal-bg-soft, #111827);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius, 8px);
          padding: 3px;
        }
        .scope-btn {
          background: none;
          border: none;
          color: var(--portal-muted, #9ca3af);
          padding: 6px 12px;
          font-size: 12px;
          font-weight: 600;
          border-radius: var(--portal-radius-sm, 5px);
          cursor: pointer;
        }
        .scope-btn.active {
          background: var(--portal-accent, #3b82f6);
          color: #ffffff;
        }
        
        /* KPI Cards Grid using Flexbox row wrap */
        .gov-kpi-grid {
          display: flex;
          gap: 16px;
          flex-wrap: wrap;
        }
        .kpi-card {
          flex: 1 1 200px;
          background: var(--portal-surface, #1f2937);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius, 8px);
          padding: 14px 18px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          position: relative;
          box-shadow: var(--portal-shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.05));
          cursor: pointer;
          transition: all 0.2s ease;
        }
        .kpi-card:hover {
          transform: translateY(-2px);
          border-color: var(--portal-accent, #3b82f6);
        }
        .kpi-card::before {
          content: '';
          position: absolute;
          top: 0; left: 0; right: 0; height: 3px;
          border-radius: var(--portal-radius, 8px) var(--portal-radius, 8px) 0 0;
        }
        .kpi-card.blue::before { background: var(--portal-accent, #3b82f6); }
        .kpi-card.amber::before { background: var(--portal-warning, #fbbf24); }
        .kpi-card.purple::before { background: var(--portal-danger, #f87171); }
        .kpi-card.red::before { background: var(--portal-danger, #f87171); }

        .kpi-card-info h3 {
          margin: 0;
          font-size: 11px;
          font-weight: 700;
          text-transform: uppercase;
          color: var(--portal-muted, #9ca3af);
        }
        .kpi-card-info .kpi-val {
          font-size: 24px;
          font-weight: 700;
          margin-top: 6px;
        }
        .kpi-card-chart {
          width: 44px;
          height: 44px;
        }
        .circular-ring {
          transform: rotate(-90deg);
        }
        .circular-ring circle {
          fill: none;
          stroke-width: 4;
        }
        .circular-ring .bg {
          stroke: var(--portal-border, #374151);
        }
        .circular-ring .bar {
          stroke: var(--portal-success, #34d399);
        }

        /* Panels and grids */
        .gov-panel {
          background: var(--portal-surface, #1f2937);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius, 8px);
          padding: 20px;
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .gov-panel-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          border-bottom: 1px solid var(--portal-border-soft, #1f2937);
          padding-bottom: 12px;
        }
        .gov-panel-header h2 {
          margin: 0;
          font-size: 15px;
          font-weight: 600;
        }

        /* Dense tables */
        .dense-table {
          width: 100%;
          border-collapse: collapse;
          font-size: 13px;
        }
        .dense-table th {
          text-align: left;
          padding: 10px 12px;
          color: var(--portal-muted, #9ca3af);
          border-bottom: 1px solid var(--portal-border, #374151);
        }
        .dense-table td {
          padding: 10px 12px;
          border-bottom: 1px solid var(--portal-border-soft, #1f2937);
        }
        .dense-table tr:hover {
          background: var(--portal-surface-subtle, #111827);
        }

        .gov-filters-bar {
          display: flex;
          gap: 12px;
          align-items: center;
          background: var(--portal-surface-subtle, #111827);
          border: 1px solid var(--portal-border, #374151);
          padding: 8px 16px;
          border-radius: var(--portal-radius-sm, 5px);
          flex-wrap: wrap;
        }
        .gov-filter-search-wrap {
          position: relative;
          display: flex;
          align-items: center;
        }
        .gov-filter-input {
          background: var(--portal-bg, #0b0f19);
          border: 1px solid var(--portal-border, #374151);
          color: var(--portal-text, #ffffff);
          padding: 6px 12px;
          font-size: 12px;
          border-radius: var(--portal-radius-sm, 5px);
          width: 240px;
        }
        .gov-filter-select {
          background: var(--portal-bg, #0b0f19);
          border: 1px solid var(--portal-border, #374151);
          color: var(--portal-text, #ffffff);
          padding: 6px 12px;
          font-size: 12px;
          border-radius: var(--portal-radius-sm, 5px);
        }

        .queue-list {
          display: flex;
          flex-direction: column;
          gap: 12px;
        }
        .queue-item {
          background: var(--portal-surface-subtle, #111827);
          border: 1px solid var(--portal-border-soft, #1f2937);
          border-radius: var(--portal-radius, 8px);
          padding: 16px;
        }
        .queue-item-header {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 8px;
        }
        .asset-path {
          font-family: ui-monospace, monospace;
          font-size: 13px;
          color: var(--portal-accent, #3b82f6);
          font-weight: 600;
        }
        
        .score-badge {
          font-size: 11px;
          padding: 2px 8px;
          border-radius: 12px;
          font-weight: 700;
        }
        .score-high { background: rgba(52, 211, 153, 0.15); color: #34d399; }
        .score-med { background: rgba(251, 191, 36, 0.15); color: #fbbf24; }
        .score-low { background: rgba(248, 113, 113, 0.15); color: #f87171; }
        
        .queue-item-meta {
          font-size: 12px;
          color: var(--portal-muted, #9ca3af);
          display: flex;
          align-items: center;
          gap: 12px;
          margin-bottom: 14px;
        }
        .badge-pill {
          background: var(--portal-bg, #0b0f19);
          padding: 1px 6px;
          border-radius: 4px;
          font-size: 11px;
          border: 1px solid var(--portal-border, #374151);
        }
        .badge-pill.danger {
          border-color: var(--portal-danger, #f87171);
          color: var(--portal-danger, #f87171);
          background: rgba(248, 113, 113, 0.15);
        }
        
        .queue-item-actions {
          display: flex;
          justify-content: flex-end;
          gap: 8px;
        }

        .steward-badge-tag {
          font-size: 10.5px;
          padding: 2px 8px;
          border-radius: 4px;
          font-weight: 700;
          margin-right: 4px;
          display: inline-flex;
          align-items: center;
          gap: 4px;
        }
        .steward-badge-tag.cert { background: rgba(59, 130, 246, 0.15); color: #60a5fa; }
        .steward-badge-tag.trust { background: rgba(52, 211, 153, 0.15); color: #34d399; }
        .steward-badge-tag.gdpr { background: rgba(251, 191, 36, 0.15); color: #fbbf24; }
        .steward-badge-tag.hipaa { background: rgba(167, 139, 250, 0.15); color: #a78bfa; }
        
        .remove-badge-btn {
          background: none;
          border: none;
          color: inherit;
          cursor: pointer;
          font-size: 11px;
          font-weight: bold;
          padding: 0 0 0 2px;
          opacity: 0.6;
        }
        .remove-badge-btn:hover { opacity: 1; }

        .exception-tag {
          font-size: 10px;
          padding: 2px 8px;
          border-radius: 4px;
          font-weight: 700;
          text-transform: uppercase;
        }
        .exception-tag.risk { background: rgba(248, 113, 113, 0.15); color: #f87171; }
        .exception-tag.false-positive { background: rgba(52, 211, 153, 0.15); color: #34d399; }
        .exception-tag.noise { background: rgba(251, 191, 36, 0.15); color: #fbbf24; }

        .gov-btn {
          background: var(--portal-surface, #1f2937);
          color: var(--portal-text-soft, #d1d5db);
          border: 1px solid var(--portal-border, #374151);
          padding: 6px 12px;
          border-radius: var(--portal-radius-sm, 5px);
          font-size: 12px;
          font-weight: 600;
          cursor: pointer;
        }
        .gov-btn:hover {
          background: var(--portal-bg-soft, #111827);
          color: var(--portal-text, #ffffff);
          border-color: var(--portal-accent, #3b82f6);
        }
        .gov-btn-primary {
          background: var(--portal-accent, #3b82f6);
          color: #ffffff;
          border-color: var(--portal-accent, #3b82f6);
        }
        .gov-btn-danger {
          background: rgba(248, 113, 113, 0.15);
          color: #f87171;
          border-color: rgba(248, 113, 113, 0.3);
        }

        .evidence-panel {
          background: var(--portal-bg, #0b0f19);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius-sm, 5px);
          margin-top: 10px;
          padding: 12px;
          font-family: ui-monospace, monospace;
          font-size: 12px;
          display: none;
        }
        .evidence-panel.open { display: block; }
        .code-line { display: flex; gap: 10px; }
        .code-num { color: #5a6778; text-align: right; width: 20px; }
        .code-text { color: var(--portal-text-soft, #d1d5db); white-space: pre; }
        .code-hl { background: rgba(248, 113, 113, 0.15); border-left: 2px solid var(--portal-danger, #f87171); }

        .settings-section-grid {
          display: grid;
          grid-template-columns: 1fr 1fr;
          gap: 20px;
        }
        .settings-group {
          background: var(--portal-surface-subtle, #111827);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius, 8px);
          padding: 16px 20px;
          display: flex;
          flex-direction: column;
          gap: 14px;
        }
        .settings-group h3 {
          margin: 0;
          font-size: 14px;
          font-weight: 600;
          border-bottom: 1px solid var(--portal-border-soft, #1f2937);
          padding-bottom: 8px;
        }
        .setting-row {
          display: flex;
          justify-content: space-between;
          align-items: center;
        }
        .setting-label {
          font-size: 12px;
          display: flex;
          flex-direction: column;
          gap: 2px;
        }
        .setting-label span {
          font-size: 10px;
          color: var(--portal-muted, #9ca3af);
        }
        .setting-input-number {
          width: 60px;
          background: var(--portal-bg, #0b0f19);
          border: 1px solid var(--portal-border, #374151);
          color: var(--portal-text, #ffffff);
          padding: 4px 8px;
          font-size: 12px;
          text-align: center;
        }

        .lineage-graph-container {
          display: flex;
          flex-direction: column;
          background: var(--portal-surface-subtle, #111827);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius, 8px);
          padding: 20px;
          gap: 16px;
          height: 400px;
        }
        .lineage-canvas {
          flex: 1;
          border: 1px dashed var(--portal-border-soft, #374151);
          background: radial-gradient(circle, var(--portal-surface, #1f2937) 1px, transparent 1px);
          background-size: 16px 16px;
          position: relative;
        }
        .lineage-node {
          background: var(--portal-surface, #1f2937);
          border: 2px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius-sm, 5px);
          padding: 10px 14px;
          font-size: 12px;
          position: absolute;
        }
        .lineage-node .node-type { font-size: 9px; font-weight:700; color:var(--portal-muted, #9ca3af); }
        .lineage-node .node-name { font-family: ui-monospace, monospace; font-weight:700; }
        .lineage-arrow-svg { position: absolute; top:0; left:0; width:100%; height:100%; pointer-events:none; }
        .arrow-path { fill: none; stroke: var(--portal-border, #374151); stroke-width: 2; marker-end: url(#arrowhead); }

        .gov-modal-backdrop {
          position: fixed;
          top: 0; left: 0; right: 0; bottom: 0;
          background: rgba(0, 0, 0, 0.6);
          backdrop-filter: blur(4px);
          display: flex;
          align-items: center;
          justify-content: center;
          z-index: 1000;
          opacity: 0;
          pointer-events: none;
          transition: opacity 0.2s ease;
        }
        .gov-modal-backdrop.open { opacity: 1; pointer-events: auto; }
        .gov-modal {
          background: var(--portal-surface, #1f2937);
          border: 1px solid var(--portal-border, #374151);
          border-radius: var(--portal-radius, 8px);
          width: 460px;
          padding: 24px;
          display: flex;
          flex-direction: column;
          gap: 16px;
        }
        .gov-modal-body select, .gov-modal-body textarea, .gov-modal-body input {
          width: 100%;
          background: var(--portal-bg, #0b0f19);
          border: 1px solid var(--portal-border, #374151);
          color: var(--portal-text, #ffffff);
          padding: 8px;
          font-size: 13px;
          margin-top: 6px;
          box-sizing: border-box;
        }
        .gov-modal-footer { display: flex; justify-content: flex-end; gap: 8px; }
      </style>

      <div class="gov-container">
        <!-- Main Workspace (Tabs removed to delegate fully to portal sidebar) -->
        <div class="gov-main-area" style="padding: 0;">
          <!-- Header -->
          <div class="gov-header">
            <div class="gov-header-title">
              <h1>Portal Governance Core</h1>
              <p>Data Stewardship overview and rule resolution portal</p>
            </div>
            <div class="gov-actions">
              ${state.tab === 'overview' || state.tab === 'workqueue' || state.tab === 'exceptions' ? `
                <div class="scope-toggle">
                  <button class="scope-btn ${state.mode === 'steward' ? 'active' : ''}" id="btnStewardScope">My Steward Work</button>
                  <button class="scope-btn ${state.mode === 'all' ? 'active' : ''}" id="btnAllScope">All Governance</button>
                </div>
                <button class="gov-btn gov-btn-primary" id="btnScanNow">⚡ Scan Now</button>
              ` : ''}
            </div>
          </div>
    `;

    const renderAssignedBadges = (item) => {
      if (!item.assignedBadges || !item.assignedBadges.length) return '';
      return item.assignedBadges.map(b => {
        const styleClass = b.toLowerCase().includes('cert') ? 'cert' : b.toLowerCase().includes('trust') ? 'trust' : b.toLowerCase().includes('gdpr') ? 'gdpr' : 'hipaa';
        return `
          <span class="steward-badge-tag ${styleClass}">
            ★ ${esc(b)}
            <button class="remove-badge-btn" title="Remove Badge" data-remove-badge-name="${esc(b)}" data-remove-badge-asset="${item.id}">×</button>
          </span>
        `;
      }).join(' ');
    };

    if (state.tab === 'overview') {
      html += `
        <!-- KPI Cards Grid -->
        <div class="gov-kpi-grid">
          <div class="kpi-card blue" id="kpiGoverned">
            <div class="kpi-card-info">
              <h3>Governed Assets</h3>
              <div class="kpi-val">${governedPercent}%</div>
            </div>
            <div class="kpi-card-chart">
              <svg class="circular-ring" width="44" height="44" viewBox="0 0 44 44">
                <circle class="bg" cx="22" cy="22" r="${radius}" fill="none" />
                <circle class="bar" cx="22" cy="22" r="${radius}" fill="none"
                  stroke-dasharray="${circ}" stroke-dashoffset="${strokeDashoffset}" />
              </svg>
            </div>
          </div>
          <div class="kpi-card amber" id="kpiMetadata">
            <div class="kpi-card-info">
              <h3>Missing Metadata</h3>
              <div class="kpi-val">${missingMetaCount}</div>
            </div>
          </div>
          <div class="kpi-card purple" id="kpiBypasses">
            <div class="kpi-card-info">
              <h3>Active Security Bypasses</h3>
              <div class="kpi-val">${activeSecurityRisksCount}</div>
            </div>
          </div>
          <div class="kpi-card red" id="kpiFindings">
            <div class="kpi-card-info">
              <h3>Open Findings</h3>
              <div class="kpi-val">${totalOpenFindingsCount}</div>
            </div>
          </div>
        </div>

        <div class="gov-panel">
          <div class="gov-panel-header">
            <h2>Quick Actions Queue</h2>
            <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">${filteredQueue.slice(0, 3).length} urgent items displayed</span>
          </div>
          <div class="queue-list">
            ${filteredQueue.slice(0, 3).map(item => `
              <div class="queue-item" id="item-${item.id}">
                <div class="queue-item-header">
                  <span class="asset-path">${esc(item.path)}</span>
                  <span class="score-badge ${item.scoreClass}">Score: ${item.score}/100</span>
                </div>
                <div class="queue-item-meta">
                  <span>${esc(item.meta)}</span>
                  <span style="color:var(--portal-border, #374151);">|</span>
                  ${item.badges.map(b => `<span class="badge-pill ${b.includes('PII') || b.includes('PHI') || b.includes('PCI') ? 'danger' : ''}">${esc(b)}</span>`).join(' ')}
                  ${item.assignedBadges && item.assignedBadges.length ? 
                    `<span style="color:var(--portal-border, #374151);">|</span>` + renderAssignedBadges(item) : ''
                  }
                </div>
                <div class="queue-item-actions">
                  <button class="gov-btn" data-toggle-evidence="${item.id}">🔍 View Evidence</button>
                  <button class="gov-btn" data-mark-reviewed="${item.id}">✓ Verify</button>
                  <button class="gov-btn gov-btn-danger" data-accept-risk="${item.id}">⚠️ Resolve Exception</button>
                </div>
                <div class="evidence-panel" id="evidence-${item.id}">
                  ${item.evidence.map(line => `
                    <div class="code-line ${line.hl ? 'code-hl' : ''}">
                      <span class="code-num">${line.num}</span>
                      <span class="code-text">${esc(line.text)}</span>
                    </div>
                  `).join('')}
                </div>
              </div>
            `).join('')}
            <button class="gov-btn gov-btn-primary" id="btnGoToWorkqueue" style="align-self: flex-start; margin-top:8px;">View Full Workqueue (${scoredQueue.length} items) →</button>
          </div>
        </div>
      `;
    } else if (state.tab === 'workqueue') {
      html += `
        <div class="gov-filters-bar">
          <div class="gov-filter-search-wrap">
            <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Filter by asset path..." value="${esc(state.searchFilter)}">
            <button class="gov-search-clear" id="btnSearchClear" style="display:${state.searchFilter?'block':'none'}">×</button>
          </div>
          <select class="gov-filter-select" id="badgeFilter">
            <option value="all" ${state.badgeFilter === 'all' ? 'selected' : ''}>All Violation Types</option>
            <option value="Needs Metadata" ${state.badgeFilter === 'Needs Metadata' ? 'selected' : ''}>Needs Metadata</option>
            <option value="Untagged PII" ${state.badgeFilter === 'Untagged PII' ? 'selected' : ''}>Untagged PII</option>
            <option value="Needs Review" ${state.badgeFilter === 'Needs Review' ? 'selected' : ''}>Needs Review</option>
            <option value="Glossary Review" ${state.badgeFilter === 'Glossary Review' ? 'selected' : ''}>Glossary Review</option>
          </select>
          <button class="gov-btn" id="btnResetFilters">Clear All</button>
        </div>

        <div class="gov-panel">
          <div class="gov-panel-header">
            <h2>High-Density Task Workqueue</h2>
            <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">${filteredQueue.length} items visible</span>
          </div>
          <div class="queue-scroll-container">
            <table class="dense-table">
              <thead>
                <tr>
                  <th width="30"><input type="checkbox" id="chkSelectAllRows" title="Select all rows"></th>
                  <th>Asset Path</th>
                  <th width="80">Score</th>
                  <th>Violation Badges</th>
                  <th>Assigned Badges</th>
                  <th>Steward/Domain</th>
                  <th width="240" style="text-align:right;">Actions</th>
                </tr>
              </thead>
              <tbody>
                ${filteredQueue.map(item => `
                  <tr>
                    <td><input type="checkbox" class="wq-row-check" value="${item.id}"></td>
                    <td class="asset-path">${esc(item.path)}</td>
                    <td><span class="score-badge ${item.scoreClass}">${item.score}</span></td>
                    <td>
                      ${item.badges.map(b => `<span class="badge-pill ${b.includes('PII') || b.includes('PHI') || b.includes('PCI') ? 'danger' : ''}">${esc(b)}</span>`).join(' ')}
                    </td>
                    <td>${renderAssignedBadges(item)}</td>
                    <td>${esc(item.meta.replace('Steward: ', '').replace('Domain: ', ''))}</td>
                    <td style="text-align:right;">
                      <button class="gov-btn" style="padding:2px 8px; font-size:11px;" data-toggle-evidence="${item.id}">Code</button>
                      <button class="gov-btn" style="padding:2px 8px; font-size:11px;" data-mark-reviewed="${item.id}">Verify</button>
                      <button class="gov-btn gov-btn-danger" style="padding:2px 8px; font-size:11px;" data-accept-risk="${item.id}">Bypass</button>
                    </td>
                  </tr>
                  <tr id="evidence-row-${item.id}" style="display:none;">
                    <td colspan="7" style="background:var(--portal-bg, #020617); padding: 12px;">
                      <div class="evidence-panel open" style="margin: 0; border: none;">
                        ${item.evidence.map(line => `
                          <div class="code-line ${line.hl ? 'code-hl' : ''}">
                            <span class="code-num">${line.num}</span>
                            <span class="code-text">${esc(line.text)}</span>
                          </div>
                        `).join('')}
                      </div>
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      `;
    } else if (state.tab === 'exceptions') {
      html += `
        <div class="gov-filters-bar">
          <div class="gov-filter-search-wrap">
            <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Filter exceptions..." value="${esc(state.searchFilter)}">
          </div>
          <select class="gov-filter-select" id="categoryFilter">
            <option value="all" ${state.categoryFilter === 'all' ? 'selected' : ''}>All Resolution Types</option>
            <option value="risk" ${state.categoryFilter === 'risk' ? 'selected' : ''}>Durable Bypass (Security Risk)</option>
            <option value="false-positive" ${state.categoryFilter === 'false-positive' ? 'selected' : ''}>False Positive</option>
          </select>
        </div>

        <div class="gov-panel">
          <div class="gov-panel-header">
            <h2>Active Exceptions & Security Bypasses Ledger</h2>
            <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">${filteredRisks.length} exceptions active</span>
          </div>
          <div class="queue-scroll-container">
            <table class="dense-table">
              <thead>
                <tr>
                  <th>Bypassed Asset</th>
                  <th width="180">Category</th>
                  <th>Justification Reason</th>
                  <th width="180">Steward / Date</th>
                  <th width="120" style="text-align:right;">Actions</th>
                </tr>
              </thead>
              <tbody>
                ${filteredRisks.map(risk => `
                  <tr>
                    <td class="asset-path">${esc(risk.asset)}</td>
                    <td><span class="exception-tag ${esc(risk.category)}">${esc(risk.categoryLabel)}</span></td>
                    <td style="font-style: italic;">"${esc(risk.reason)}"</td>
                    <td>${esc(risk.steward)} · ${esc(risk.date)}</td>
                    <td style="text-align:right;">
                      <button class="gov-btn gov-btn-danger" style="padding:2px 8px; font-size:11px;" data-reenable-risk="${risk.id}">Re-Enable</button>
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      `;
    } else if (state.tab === 'badges') {
      html += `
        <div class="gov-filters-bar">
          <div class="gov-filter-search-wrap">
            <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Filter assets to badge..." value="${esc(state.searchFilter)}">
          </div>
          <button class="gov-btn" id="btnResetFilters">Clear All</button>
        </div>

        <div class="settings-section-grid">
          <div class="settings-group" style="padding: 20px;">
            <h3>Steward Badge Assignment Panel</h3>
            <div style="flex:1 1 auto; overflow-y:auto;">
              <table class="dense-table" style="font-size:12px;">
                <thead>
                  <tr>
                    <th>Asset Path</th>
                    <th>Assigned Badges</th>
                    <th style="text-align:right;">Manage Tags</th>
                  </tr>
                </thead>
                <tbody>
                  ${scoredQueue.filter(item => item.path.toLowerCase().includes(state.searchFilter.toLowerCase())).map(item => `
                    <tr>
                      <td class="asset-path">${esc(item.path)}</td>
                      <td>${item.assignedBadges && item.assignedBadges.length ? renderAssignedBadges(item) : '<span style="color:var(--portal-muted, #9ca3af); font-style:italic; font-size:11px;">None</span>'}</td>
                      <td style="text-align:right;">
                        <select class="gov-filter-select" style="padding: 3px 6px; font-size:11px;" data-assign-badge-to="${item.id}">
                          <option value="">+ Assign Badge</option>
                          ${state.badgeDefinitions.filter(def => !item.assignedBadges.includes(def.name)).map(def => `
                            <option value="${esc(def.name)}">${esc(def.name)}</option>
                          `).join('')}
                          ${item.assignedBadges.length ? '<option value="__CLEAR__">- Clear Badges</option>' : ''}
                        </select>
                      </td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
            </div>
          </div>
          <div class="settings-group">
            <h3>Defined Certification Badges</h3>
            <div style="flex:1 1 auto; overflow-y:auto; display:flex; flex-direction:column; gap:12px;">
              ${state.badgeDefinitions.map(def => `
                <div style="background:var(--portal-bg, #0b0f19); border: 1px solid var(--portal-border, #374151); padding: 12px; border-radius:6px;">
                  <div style="display:flex; justify-content:space-between; align-items:center;">
                    <span class="steward-badge-tag ${def.color}">★ ${esc(def.name)}</span>
                  </div>
                  <p style="font-size:12px; margin:6px 0 0 0; color:var(--portal-muted, #9ca3af);">${esc(def.desc)}</p>
                </div>
              `).join('')}
            </div>
          </div>
        </div>
      `;
    } else if (state.tab === 'glossary') {
      html += `
        <div class="gov-filters-bar">
          <div class="gov-filter-search-wrap">
            <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Search glossary..." value="${esc(state.searchFilter)}">
          </div>
          <button class="gov-btn gov-btn-primary" id="btnAddNewTerm" style="margin-left:auto;">➕ Define Term</button>
        </div>

        <div class="gov-panel">
          <div class="gov-panel-header">
            <h2>Business Glossary & Terms</h2>
            <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">${filteredGlossary.length} terminology entries defined</span>
          </div>
          <div class="queue-scroll-container">
            <table class="dense-table">
              <thead>
                <tr>
                  <th>Term Name</th>
                  <th>Standard Type</th>
                  <th>Aliases</th>
                  <th>Defined Formula Rule</th>
                  <th>Description</th>
                  <th>Steward</th>
                  <th style="text-align:right;">Actions</th>
                </tr>
              </thead>
              <tbody>
                ${filteredGlossary.map(t => `
                  <tr>
                    <td style="font-weight:700; color:var(--portal-accent, #3b82f6);">${esc(t.term)}</td>
                    <td><code>${esc(t.type)}</code></td>
                    <td>${t.aliases.split(',').map(a => `<span class="badge-pill" style="margin-right:2px;">${esc(a.trim())}</span>`).join('')}</td>
                    <td>${t.formula && t.formula !== 'N/A' ? `<code>${esc(t.formula)}</code>` : esc(t.formula)}</td>
                    <td style="font-size:12px;">${esc(t.desc)}</td>
                    <td>${esc(t.steward)}</td>
                    <td style="text-align:right;">
                      <button class="gov-btn" style="padding:2px 8px; font-size:11px;" data-edit-term="${t.id}">Edit</button>
                      <button class="gov-btn gov-btn-danger" style="padding:2px 8px; font-size:11px;" data-delete-term="${t.id}">Delete</button>
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      `;
    } else if (state.tab === 'settings') {
      html += `
        <div class="settings-section-grid">
          <div class="settings-group">
            <h3>Governance Score Thresholds</h3>
            <div class="setting-row">
              <div class="setting-label">
                Target Clean Score Target Threshold
                <span>Findings generated below this score. Current: <b>${state.settings.targetScore}</b></span>
              </div>
              <input type="range" id="settingsTargetScore" min="50" max="100" value="${state.settings.targetScore}" style="width:120px;">
            </div>
            <div style="margin-top: 8px; font-size:11px; color:var(--portal-muted, #9ca3af); font-weight:700; text-transform:uppercase;">Active Governance Checks</div>
            <div class="setting-row">
              <div class="setting-label">
                <span><input type="checkbox" id="chkEnableMeta" ${state.settings.enableMeta?'checked':''}> Ownership Metadata Check</span>
                <span>Verify @owner, @steward tags.</span>
              </div>
              <input type="number" id="settingsDeductMeta" class="setting-input-number" value="${state.settings.deductMeta}" ${state.settings.enableMeta?'':'disabled'}>
            </div>
            <div class="setting-row">
              <div class="setting-label">
                <span><input type="checkbox" id="chkEnablePII" ${state.settings.enablePII?'checked':''}> Sensitive Data Classification</span>
                <span>Audit verification of PII/PHI tags.</span>
              </div>
              <input type="number" id="settingsDeductPII" class="setting-input-number" value="${state.settings.deductPII}" ${state.settings.enablePII?'':'disabled'}>
            </div>
            <div class="setting-row">
              <div class="setting-label">
                <span><input type="checkbox" id="chkEnableGlossary" ${state.settings.enableGlossary?'checked':''}> Glossary Alignment Checks</span>
                <span>Validate aliases and business formulas.</span>
              </div>
              <input type="number" id="settingsDeductGlossary" class="setting-input-number" value="${state.settings.deductGlossary}" ${state.settings.enableGlossary?'':'disabled'}>
            </div>
            <div class="setting-row">
              <div class="setting-label">
                <span><input type="checkbox" id="chkEnableStale" ${state.settings.enableStale?'checked':''}> Review Staleness Checks</span>
                <span>Audit edits made since the last steward review.</span>
              </div>
              <input type="number" id="settingsDeductStale" class="setting-input-number" value="${state.settings.deductStale}" ${state.settings.enableStale?'':'disabled'}>
            </div>
            <div style="margin-top:auto; padding-top:12px; border-top:1px solid var(--portal-border-soft, #1f2937); text-align:right;">
              <button class="gov-btn gov-btn-primary" id="btnSaveScoringSettings">Save Scoring Config</button>
            </div>
          </div>

          <div class="settings-group">
            <div style="display:flex; justify-content:space-between; align-items:center;">
              <h3>Configurable Bypass Categories</h3>
              <button class="gov-btn" id="btnAddNewCategory" style="padding:4px 8px; font-size:11px;">➕ Add</button>
            </div>
            <div style="flex:1 1 auto; overflow-y:auto; max-height:260px;">
              <table class="dense-table" style="font-size:12px;">
                <thead>
                  <tr>
                    <th>Label Name</th>
                    <th>Color Tag</th>
                    <th>Default Expiry</th>
                    <th style="text-align:right;">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  ${state.resolutionCategories.map(cat => `
                    <tr>
                      <td style="font-weight:600;">${esc(cat.label)}</td>
                      <td><span class="exception-tag ${esc(cat.color)}">${esc(cat.color)}</span></td>
                      <td>${esc(cat.expiry)}</td>
                      <td style="text-align:right;">
                        <button class="gov-btn" style="padding:1px 6px; font-size:10px;" data-edit-cat="${cat.id}">Edit</button>
                        <button class="gov-btn gov-btn-danger" style="padding:1px 6px; font-size:10px;" data-delete-cat="${cat.id}">Del</button>
                      </td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      `;
    }

    // Modal backdrops & structures
    html += `
        </div> <!-- End Main Workspace -->
        
        <!-- Accept Risk Modal -->
        <div class="gov-modal-backdrop" id="modalBackdrop">
          <div class="gov-modal">
            <div class="gov-modal-header">
              <h3>Resolve Exception / Mark Bypass</h3>
            </div>
            <div class="gov-modal-body">
              <label>Target Asset</label>
              <input type="text" id="modalAssetPath" readonly>
              <div style="margin-top:12px;">
                <label>Resolution Category</label>
                <select id="modalCategory"></select>
              </div>
              <div style="margin-top:12px;">
                <label>Exception Reason / Justification</label>
                <textarea id="modalReason" rows="3" placeholder="Explain why this bypass is safe..."></textarea>
              </div>
            </div>
            <div class="gov-modal-footer">
              <button class="gov-btn" id="btnCancelModal">Cancel</button>
              <button class="gov-btn gov-btn-primary" id="btnConfirmModal">Confirm & Save</button>
            </div>
          </div>
        </div>

        <!-- Glossary Modal -->
        <div class="gov-modal-backdrop" id="glossaryModalBackdrop">
          <div class="gov-modal">
            <div class="gov-modal-header">
              <h3 id="glossaryModalTitle">Define New Term</h3>
            </div>
            <div class="gov-modal-body">
              <label>Term Name</label>
              <input type="text" id="glossaryTerm">
              <div style="margin-top:12px;">
                <label>Standard DataType</label>
                <input type="text" id="glossaryType">
              </div>
              <div style="margin-top:12px;">
                <label>Business Aliases</label>
                <input type="text" id="glossaryAliases">
              </div>
              <div style="margin-top:12px;">
                <label>Defined Calculation Formula Rule</label>
                <input type="text" id="glossaryFormula">
              </div>
              <div style="margin-top:12px;">
                <label>Description & Rule Context</label>
                <textarea id="glossaryDesc" rows="3"></textarea>
              </div>
            </div>
            <div class="gov-modal-footer">
              <button class="gov-btn" id="btnCancelGlossaryModal">Cancel</button>
              <button class="gov-btn gov-btn-primary" id="btnConfirmGlossaryModal">Save Term</button>
            </div>
          </div>
        </div>

        <!-- Category Management Modal -->
        <div class="gov-modal-backdrop" id="categoryModalBackdrop">
          <div class="gov-modal">
            <div class="gov-modal-header">
              <h3 id="categoryModalTitle">Define Bypass Category</h3>
            </div>
            <div class="gov-modal-body">
              <label>Category Label</label>
              <input type="text" id="catLabel">
              <div style="margin-top:12px;">
                <label>Value Identifier</label>
                <input type="text" id="catValue">
              </div>
              <div style="margin-top:12px;">
                <label>Color Class Theme Tag</label>
                <select id="catColor">
                  <option value="risk">Red</option>
                  <option value="noise">Yellow</option>
                  <option value="false-positive">Green</option>
                </select>
              </div>
              <div style="margin-top:12px;">
                <label>Durable Expiry Limit</label>
                <select id="catExpiry">
                  <option value="None">None</option>
                  <option value="90 Days">90 Days</option>
                </select>
              </div>
            </div>
            <div class="gov-modal-footer">
              <button class="gov-btn" id="btnCancelCategoryModal">Cancel</button>
              <button class="gov-btn gov-btn-primary" id="btnConfirmCategoryModal">Save Category</button>
            </div>
          </div>
        </div>
      </div>
    `;

    host.innerHTML = html;

    // Bind quick actions go to workqueue
    host.querySelector('#btnGoToWorkqueue')?.addEventListener('click', () => {
      state.tab = 'workqueue';
      render();
    });

    // Bind Scope & actions
    host.querySelector('#btnStewardScope')?.addEventListener('click', () => {
      state.mode = 'steward';
      render();
    });
    host.querySelector('#btnAllScope')?.addEventListener('click', () => {
      state.mode = 'all';
      render();
    });
    host.querySelector('#btnScanNow')?.addEventListener('click', () => {
      alert('Initiating workspace governance linter scan...');
    });

    // Filters
    const searchInput = host.querySelector('#searchFilter');
    if (searchInput) {
      searchInput.addEventListener('change', (e) => {
        state.searchFilter = e.target.value;
        render();
      });
    }
    host.querySelector('#btnSearchClear')?.addEventListener('click', () => {
      state.searchFilter = '';
      render();
    });
    host.querySelector('#btnResetFilters')?.addEventListener('click', () => {
      state.searchFilter = '';
      state.badgeFilter = 'all';
      state.categoryFilter = 'all';
      render();
    });
    host.querySelector('#badgeFilter')?.addEventListener('change', (e) => {
      state.badgeFilter = e.target.value;
      render();
    });
    host.querySelector('#categoryFilter')?.addEventListener('change', (e) => {
      state.categoryFilter = e.target.value;
      render();
    });

    // Select-all toggle for the workqueue table. Bound here (not an inline onclick)
    // because the Portal CSP sets script-src-attr 'none', which blocks inline handlers.
    host.querySelector('#chkSelectAllRows')?.addEventListener('change', (e) => {
      host.querySelectorAll('.wq-row-check').forEach(cb => { cb.checked = e.target.checked; });
    });

    // Evidence
    host.querySelectorAll('[data-toggle-evidence]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-toggle-evidence');
        const evRow = host.querySelector(`#evidence-row-${id}`);
        if (evRow) {
          evRow.style.display = evRow.style.display === 'none' ? 'table-row' : 'none';
        } else {
          host.querySelector(`#evidence-${id}`)?.classList.toggle('open');
        }
      });
    });

    // Verify / Mark reviewed
    host.querySelectorAll('[data-mark-reviewed]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-mark-reviewed');
        const index = state.stewardshipItems.findIndex(item => item.id === id);
        if (index !== -1) {
          state.stewardshipItems.splice(index, 1);
          render();
        }
      });
    });

    // Re-enable
    host.querySelectorAll('[data-reenable-risk]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-reenable-risk');
        const index = state.risks.findIndex(r => r.id === id);
        if (index !== -1) {
          const risk = state.risks[index];
          state.risks.splice(index, 1);
          state.stewardshipItems.push({
            id: 'asset-' + Date.now(),
            path: risk.asset,
            meta: `Steward: Chuck · Domain: General`,
            badges: ['Needs Review'],
            assignedBadges: [],
            evidence: [{ num: 1, text: '-- Re-opened from accepted risks' }]
          });
          render();
        }
      });
    });

    // Accept risk modal
    let pendingAssetId = null;
    host.querySelectorAll('[data-accept-risk]').forEach(btn => {
      btn.addEventListener('click', () => {
        pendingAssetId = btn.getAttribute('data-accept-risk');
        const asset = state.stewardshipItems.find(item => item.id === pendingAssetId);
        if (asset) {
          host.querySelector('#modalAssetPath').value = asset.path;
          host.querySelector('#modalReason').value = '';
          const select = host.querySelector('#modalCategory');
          select.innerHTML = state.resolutionCategories.map(cat => `<option value="${esc(cat.value)}">${esc(cat.label)}</option>`).join('');
          host.querySelector('#modalBackdrop').classList.add('open');
        }
      });
    });

    host.querySelector('#btnCancelModal')?.addEventListener('click', () => {
      host.querySelector('#modalBackdrop').classList.remove('open');
    });

    host.querySelector('#btnConfirmModal')?.addEventListener('click', () => {
      const reason = host.querySelector('#modalReason').value.trim();
      const select = host.querySelector('#modalCategory');
      const val = select.value;
      const label = select.options[select.selectedIndex].text;

      if (!reason) {
        alert('Justification required.');
        return;
      }

      const index = state.stewardshipItems.findIndex(i => i.id === pendingAssetId);
      if (index !== -1) {
        const asset = state.stewardshipItems[index];
        state.stewardshipItems.splice(index, 1);
        state.risks.push({
          id: 'risk-' + Date.now(),
          asset: asset.path,
          category: val,
          categoryLabel: label,
          reason: reason,
          date: new Date().toISOString().split('T')[0],
          steward: 'Chuck'
        });
        host.querySelector('#modalBackdrop').classList.remove('open');
        render();
      }
    });

    // Glossary CRUD
    const gModal = host.querySelector('#glossaryModalBackdrop');
    host.querySelector('#btnAddNewTerm')?.addEventListener('click', () => {
      state.editingTermId = null;
      host.querySelector('#glossaryModalTitle').textContent = 'Define New Term';
      host.querySelector('#glossaryTerm').value = '';
      host.querySelector('#glossaryTerm').readOnly = false;
      host.querySelector('#glossaryType').value = '';
      host.querySelector('#glossaryAliases').value = '';
      host.querySelector('#glossaryFormula').value = '';
      host.querySelector('#glossaryDesc').value = '';
      gModal.classList.add('open');
    });

    host.querySelector('#btnCancelGlossaryModal')?.addEventListener('click', () => {
      gModal.classList.remove('open');
    });

    host.querySelector('#btnConfirmGlossaryModal')?.addEventListener('click', () => {
      const term = host.querySelector('#glossaryTerm').value.trim();
      const type = host.querySelector('#glossaryType').value.trim();
      const aliases = host.querySelector('#glossaryAliases').value.trim();
      const formula = host.querySelector('#glossaryFormula').value.trim();
      const desc = host.querySelector('#glossaryDesc').value.trim();

      if (!term || !type || !aliases || !desc) {
        alert('All fields are required.');
        return;
      }

      if (state.editingTermId) {
        const idx = state.glossary.findIndex(t => t.id === state.editingTermId);
        if (idx !== -1) {
          state.glossary[idx].type = type;
          state.glossary[idx].aliases = aliases;
          state.glossary[idx].formula = formula || 'N/A';
          state.glossary[idx].desc = desc;
        }
      } else {
        state.glossary.push({
          id: 'term-' + Date.now(),
          term, type, aliases, formula: formula || 'N/A', desc, steward: 'Chuck'
        });
      }
      gModal.classList.remove('open');
      render();
    });

    host.querySelectorAll('[data-edit-term]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-edit-term');
        const term = state.glossary.find(t => t.id === id);
        if (term) {
          state.editingTermId = id;
          host.querySelector('#glossaryModalTitle').textContent = 'Edit Defined Term';
          host.querySelector('#glossaryTerm').value = term.term;
          host.querySelector('#glossaryTerm').readOnly = true;
          host.querySelector('#glossaryType').value = term.type;
          host.querySelector('#glossaryAliases').value = term.aliases;
          host.querySelector('#glossaryFormula').value = term.formula === 'N/A' ? '' : term.formula;
          host.querySelector('#glossaryDesc').value = term.desc;
          gModal.classList.add('open');
        }
      });
    });

    host.querySelectorAll('[data-delete-term]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-delete-term');
        const idx = state.glossary.findIndex(t => t.id === id);
        if (idx !== -1) {
          if (confirm(`Delete glossary term "${state.glossary[idx].term}"?`)) {
            state.glossary.splice(idx, 1);
            render();
          }
        }
      });
    });

    // Badge Assignment
    host.querySelectorAll('[data-assign-badge-to]').forEach(select => {
      select.addEventListener('change', (e) => {
        const assetId = select.getAttribute('data-assign-badge-to');
        const badgeVal = e.target.value;
        if (!badgeVal) return;

        const idx = state.stewardshipItems.findIndex(i => i.id === assetId);
        if (idx !== -1) {
          const item = state.stewardshipItems[idx];
          if (badgeVal === '__CLEAR__') {
            item.assignedBadges = [];
          } else if (!item.assignedBadges.includes(badgeVal)) {
            item.assignedBadges.push(badgeVal);
          }
          state.assignedBadgesMap[item.path] = item.assignedBadges;
          render();
        }
      });
    });

    host.querySelectorAll('.remove-badge-btn').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const assetId = btn.getAttribute('data-remove-badge-asset');
        const badgeName = btn.getAttribute('data-remove-badge-name');
        const idx = state.stewardshipItems.findIndex(i => i.id === assetId);
        if (idx !== -1) {
          const item = state.stewardshipItems[idx];
          item.assignedBadges = item.assignedBadges.filter(b => b !== badgeName);
          state.assignedBadgesMap[item.path] = item.assignedBadges;
          render();
        }
      });
    });

    // Settings adjustments
    const slider = host.querySelector('#settingsTargetScore');
    if (slider) {
      slider.addEventListener('input', (e) => {
        state.settings.targetScore = parseInt(e.target.value);
        host.querySelector('.setting-label span b').textContent = e.target.value;
      });
      slider.addEventListener('change', () => {
        render();
      });
    }

    const bindToggle = (chkId, settingKey, inputId) => {
      host.querySelector(chkId)?.addEventListener('change', (e) => {
        state.settings[settingKey] = e.target.checked;
        const input = host.querySelector(inputId);
        if (input) input.disabled = !e.target.checked;
        render();
      });
    };
    bindToggle('#chkEnableMeta', 'enableMeta', '#settingsDeductMeta');
    bindToggle('#chkEnablePII', 'enablePII', '#settingsDeductPII');
    bindToggle('#chkEnableGlossary', 'enableGlossary', '#settingsDeductGlossary');
    bindToggle('#chkEnableStale', 'enableStale', '#settingsDeductStale');

    host.querySelector('#btnSaveScoringSettings')?.addEventListener('click', () => {
      state.settings.deductMeta = parseInt(host.querySelector('#settingsDeductMeta').value) || 0;
      state.settings.deductPII = parseInt(host.querySelector('#settingsDeductPII').value) || 0;
      state.settings.deductGlossary = parseInt(host.querySelector('#settingsDeductGlossary').value) || 0;
      state.settings.deductStale = parseInt(host.querySelector('#settingsDeductStale').value) || 0;
      alert('Scoring configurations updated.');
      render();
    });

    // Category CRUD settings
    const cModal = host.querySelector('#categoryModalBackdrop');
    host.querySelector('#btnAddNewCategory')?.addEventListener('click', () => {
      state.editingCatId = null;
      host.querySelector('#categoryModalTitle').textContent = 'Define Bypass Category';
      host.querySelector('#catLabel').value = '';
      host.querySelector('#catValue').value = '';
      host.querySelector('#catColor').value = 'noise';
      host.querySelector('#catExpiry').value = 'None';
      cModal.classList.add('open');
    });

    host.querySelector('#btnCancelCategoryModal')?.addEventListener('click', () => {
      cModal.classList.remove('open');
    });

    host.querySelector('#btnConfirmCategoryModal')?.addEventListener('click', () => {
      const label = host.querySelector('#catLabel').value.trim();
      const val = host.querySelector('#catValue').value.trim();
      const color = host.querySelector('#catColor').value;
      const colorLabel = host.querySelector('#catColor').options[host.querySelector('#catColor').selectedIndex].text;
      const expiry = host.querySelector('#catExpiry').value;

      if (!label || !val) {
        alert('Label and value required.');
        return;
      }

      if (state.editingCatId) {
        const idx = state.resolutionCategories.findIndex(c => c.id === state.editingCatId);
        if (idx !== -1) {
          state.resolutionCategories[idx] = { id: state.editingCatId, value: val, label, color, colorLabel, expiry };
        }
      } else {
        state.resolutionCategories.push({ id: 'cat-' + Date.now(), value: val, label, color, colorLabel, expiry });
      }
      cModal.classList.remove('open');
      render();
    });

    host.querySelectorAll('[data-edit-cat]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-edit-cat');
        const cat = state.resolutionCategories.find(c => c.id === id);
        if (cat) {
          state.editingCatId = id;
          host.querySelector('#categoryModalTitle').textContent = 'Edit Bypass Category';
          host.querySelector('#catLabel').value = cat.label;
          host.querySelector('#catValue').value = cat.value;
          host.querySelector('#catColor').value = cat.color;
          host.querySelector('#catExpiry').value = cat.expiry;
          cModal.classList.add('open');
        }
      });
    });

    host.querySelectorAll('[data-delete-cat]').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.getAttribute('data-delete-cat');
        const idx = state.resolutionCategories.findIndex(c => c.id === id);
        if (idx !== -1) {
          if (confirm(`Delete bypass category "${state.resolutionCategories[idx].label}"?`)) {
            state.resolutionCategories.splice(idx, 1);
            render();
          }
        }
      });
    });
  };

  return {
    render,
    setTab(tabName) {
      state.tab = tabName;
      render();
    },
    dispose() {},
    state
  };
}
