export default {
  id: 'portal-governance',
  title: 'Portal Governance Module',
  subtitle: 'Steward-first workspace shell and workflows',
  fixtures: [
    { id: 'steward-view', label: 'My Steward Work (Assigned to Me)' },
    { id: 'all-governance', label: 'All Governance Work (Full Estate)' }
  ],
  async mount(stage, fixtureId, ctx) {
    // Large mock data
    const data = {
      steward: {
        queue: [
          { id: 'asset-1', path: 'sales_yearly_rollup.etlsql', meta: 'Steward: Chuck · Domain: Sales', badges: ['Needs Metadata', 'Needs Review'], assignedBadges: ['Trusted'], evidence: [{ num: 1, text: '-- Yearly rollup process' }, { num: 4, text: 'SELECT SUM(Revenue) FROM src.Sales;', hl: true }] },
          { id: 'asset-2', path: 'hr_salary_report.rptsql', meta: 'Steward: Chuck · Domain: Human Resources', badges: ['Untagged PII', 'Needs Review'], assignedBadges: ['GDPR Scoped'], evidence: [{ num: 10, text: "  'Salary' = emp_salary,  -- Untagged sensitive field", hl: true }] },
          { id: 'asset-4', path: 'finance_balance_sheet.etlsql', meta: 'Steward: Chuck · Domain: Finance', badges: ['Needs Metadata'], assignedBadges: [], evidence: [{ num: 5, text: 'CREATE CONNECTION dest AS MSSQL(...);', hl: true }] },
          { id: 'asset-5', path: 'patient_health_audit.etlsql', meta: 'Steward: Chuck · Domain: Healthcare', badges: ['Untagged PHI', 'Needs Review'], assignedBadges: ['HIPAA Scoped'], evidence: [{ num: 12, text: 'SELECT diagnosis_code, patient_ssn FROM records;', hl: true }] },
          { id: 'asset-6', path: 'customer_checkout_flow.json', meta: 'Steward: Chuck · Domain: ECommerce', badges: ['Needs Review'], assignedBadges: ['Trusted'], evidence: [{ num: 2, text: '"connectionType": "STRIPE_API"', hl: true }] },
          { id: 'asset-7', path: 'inventory_reorder_trigger.etlsql', meta: 'Steward: Chuck · Domain: Logistics', badges: ['Glossary Review'], assignedBadges: [], evidence: [{ num: 8, text: 'SELECT lead_time_days AS ltd FROM warehouse;', hl: true }] },
          { id: 'asset-8', path: 'executive_revenue_summary.rptsql', meta: 'Steward: Chuck · Domain: Sales', badges: ['Untagged PII', 'Needs Metadata'], assignedBadges: [], evidence: [{ num: 14, text: "MAP ('KPI' = revenue_card)", hl: true }] }
        ],
        risks: [
          { id: 'risk-1', asset: 'stage_customer_temp.etlsql', category: 'risk', categoryLabel: 'Durable Bypass (Security Risk)', reason: 'Temporary scratch table, will be deleted next week', date: '2026-07-23', steward: 'Chuck' },
          { id: 'risk-3', asset: 'bi_report_debug.rptsql', category: 'noise', categoryLabel: 'Safe Mock (Noise Dismissal)', reason: 'Local developer sandbox dashboard, no connection to prod DB', date: '2026-07-21', steward: 'Chuck' },
          { id: 'risk-4', asset: 'temp_log_purge.etlsql', category: 'false-positive', categoryLabel: 'False Positive', reason: 'Purge script flags drop tables on SQLite, not prod', date: '2026-07-20', steward: 'Chuck' }
        ],
        glossary: [
          { id: 'term-1', term: 'revenue', type: 'DECIMAL(18,2)', aliases: 'rev, gross_sales, turnover', desc: 'Standard business definition of sales intake, calculated before deductions.', steward: 'Chuck', formula: 'SUM(sales_amount)' },
          { id: 'term-2', term: 'salary', type: 'DECIMAL(10,2)', aliases: 'emp_salary, base_pay, compensation', desc: 'Employee annual base compensation rate. Subject to strict PII encryption.', steward: 'Sarah', formula: 'N/A (Stored Attribute)' },
          { id: 'term-3', term: 'patient_ssn', type: 'VARCHAR(11)', aliases: 'ssn, patient_id, soc_sec_num', desc: 'Social Security Number for medical record tracking. Sensitive PHI.', steward: 'Dan', formula: 'N/A (Identified Token)' },
          { id: 'term-4', term: 'lead_time_days', type: 'INT', aliases: 'ltd, warehouse_lead_time, delivery_days', desc: 'Logistics processing days between order placement and fulfillment.', steward: 'Chuck', formula: 'DATEDIFF(DAY, order_date, ship_date)' },
          { id: 'term-5', term: 'customer_id', type: 'INT', aliases: 'cust_id, client_number, purchaser_key', desc: 'Unique numeric sequence primary key identifying a registered store account.', steward: 'Sarah', formula: 'N/A (Key Column)' },
          { id: 'term-6', term: 'transaction_amount', type: 'DECIMAL(12,2)', aliases: 'tx_amt, payment_value, charge', desc: 'Financial transaction volume for credit card audit logs. PCI scoped.', steward: 'Dan', formula: 'N/A (Value Column)' },
          { id: 'term-7', term: 'length_of_stay', type: 'INT', aliases: 'los, stay_duration, days_hospitalized', desc: 'Total calendar days hospitalized for patient care audit reports.', steward: 'Dan', formula: 'DATEDIFF(DAY, admission_date, discharge_date)' }
        ],
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
        badgeDefinitions: [
          { name: 'Certified', desc: 'Officially certified by data governance. Meets all metadata and compliance standards.', color: 'cert' },
          { name: 'Trusted', desc: 'Verified source dataset or connection with lineage confirmed.', color: 'trust' },
          { name: 'GDPR Scoped', desc: 'Subject to General Data Protection Regulation audit checks.', color: 'gdpr' },
          { name: 'HIPAA Scoped', desc: 'Contains Protected Health Information (PHI) subject to HIPAA rules.', color: 'hipaa' }
        ]
      },
      all: {
        queue: [
          { id: 'asset-1', path: 'sales_yearly_rollup.etlsql', meta: 'Steward: Chuck · Domain: Sales', badges: ['Needs Metadata', 'Needs Review'], assignedBadges: ['Trusted'], evidence: [{ num: 1, text: '-- Yearly rollup process' }, { num: 4, text: 'SELECT SUM(Revenue) FROM src.Sales;', hl: true }] },
          { id: 'asset-2', path: 'hr_salary_report.rptsql', meta: 'Steward: Chuck · Domain: Human Resources', badges: ['Untagged PII', 'Needs Review'], assignedBadges: ['GDPR Scoped'], evidence: [{ num: 10, text: "  'Salary' = emp_salary,  -- Untagged sensitive field", hl: true }] },
          { id: 'asset-3', path: 'marketing_leads.etlsql', meta: 'Steward: Sarah · Domain: Marketing', badges: ['Needs Metadata', 'Glossary Review'], assignedBadges: [], evidence: [{ num: 4, text: 'SELECT lead_email AS email FROM leads;', hl: true }] },
          { id: 'asset-4', path: 'finance_balance_sheet.etlsql', meta: 'Steward: Chuck · Domain: Finance', badges: ['Needs Metadata'], assignedBadges: [], evidence: [{ num: 5, text: 'CREATE CONNECTION dest AS MSSQL(...);', hl: true }] },
          { id: 'asset-5', path: 'patient_health_audit.etlsql', meta: 'Steward: Chuck · Domain: Healthcare', badges: ['Untagged PHI', 'Needs Review'], assignedBadges: ['HIPAA Scoped'], evidence: [{ num: 12, text: 'SELECT diagnosis_code, patient_ssn FROM records;', hl: true }] },
          { id: 'asset-6', path: 'customer_checkout_flow.json', meta: 'Steward: Chuck · Domain: ECommerce', badges: ['Needs Review'], assignedBadges: ['Trusted'], evidence: [{ num: 2, text: '"connectionType": "STRIPE_API"', hl: true }] },
          { id: 'asset-7', path: 'inventory_reorder_trigger.etlsql', meta: 'Steward: Chuck · Domain: Logistics', badges: ['Glossary Review'], assignedBadges: [], evidence: [{ num: 8, text: 'SELECT lead_time_days AS ltd FROM warehouse;', hl: true }] },
          { id: 'asset-8', path: 'executive_revenue_summary.rptsql', meta: 'Steward: Chuck · Domain: Sales', badges: ['Untagged PII', 'Needs Metadata'], assignedBadges: [], evidence: [{ num: 14, text: "MAP ('KPI' = revenue_card)", hl: true }] },
          { id: 'asset-9', path: 'audit_log_exporter.etlsql', meta: 'Steward: Dan · Domain: Compliance', badges: ['Needs Review'], assignedBadges: ['Certified'], evidence: [{ num: 1, text: 'SET AUDIT_OUTBOX = FAIL_CLOSED;', hl: true }] },
          { id: 'asset-10', path: 'sftp_vendor_upload.etlsql', meta: 'Steward: Sarah · Domain: Logistics', badges: ['Needs Metadata', 'Untagged PCI'], assignedBadges: [], evidence: [{ num: 6, text: 'SEND FILE credit_cards.csv AT vendor_sftp;', hl: true }] },
          { id: 'asset-11', path: 'azure_blob_sync.etlsql', meta: 'Steward: Sarah · Domain: Infrastructure', badges: ['Needs Review'], assignedBadges: [], evidence: [{ num: 3, text: 'CREATE CONNECTION az AS AZURE_BLOB(...);', hl: true }] },
          { id: 'asset-12', path: 'active_directory_sync.etlsql', meta: 'Steward: Dan · Domain: Compliance', badges: ['Needs Metadata', 'Untagged PII'], assignedBadges: [], evidence: [{ num: 5, text: 'SELECT ssn, password_hash FROM active_directory;', hl: true }] },
          { id: 'asset-13', path: 'sales_forecast.rptsql', meta: 'Steward: Chuck · Domain: Sales', badges: ['Glossary Review'], assignedBadges: [], evidence: [{ num: 7, text: 'CREATE VISUAL FC AS COMBO', hl: true }] },
          { id: 'asset-14', path: 'snowflake_loading_zone.etlsql', meta: 'Steward: Sarah · Domain: Marketing', badges: ['Needs Review'], assignedBadges: ['Trusted'], evidence: [{ num: 8, text: 'MERGE INTO snowflake_db.Leads', hl: true }] },
          { id: 'asset-15', path: 'postgresql_audit_trigger.etlsql', meta: 'Steward: Dan · Domain: Compliance', badges: ['Untagged PII'], assignedBadges: [], evidence: [{ num: 4, text: 'SELECT * FROM secrets.keys;', hl: true }] }
        ],
        risks: [
          { id: 'risk-1', asset: 'stage_customer_temp.etlsql', category: 'risk', categoryLabel: 'Durable Bypass (Security Risk)', reason: 'Temporary scratch table, will be deleted next week', date: '2026-07-23', steward: 'Chuck' },
          { id: 'risk-2', asset: 'test_mock_data.etlsql', category: 'false-positive', categoryLabel: 'False Positive', reason: 'The field name is local_time but tagged by linter as secret PII token', date: '2026-07-22', steward: 'Sarah' },
          { id: 'risk-3', asset: 'bi_report_debug.rptsql', category: 'noise', categoryLabel: 'Safe Mock (Noise Dismissal)', reason: 'Local developer sandbox dashboard, no connection to prod DB', date: '2026-07-21', steward: 'Chuck' },
          { id: 'risk-4', asset: 'temp_log_purge.etlsql', category: 'false-positive', categoryLabel: 'False Positive', reason: 'Purge script flags drop tables on SQLite, not prod', date: '2026-07-20', steward: 'Chuck' },
          { id: 'risk-5', asset: 'sandbox_test_1.etlsql', category: 'noise', categoryLabel: 'Safe Mock (Noise Dismissal)', reason: 'Testing connection variables only', date: '2026-07-18', steward: 'Sarah' },
          { id: 'risk-6', asset: 'pci_bypass_test.etlsql', category: 'risk', categoryLabel: 'Durable Bypass (Security Risk)', reason: 'Mock sandbox connection for payment gateway validation', date: '2026-07-17', steward: 'Dan' },
          { id: 'risk-7', asset: 'auth_helper_mock.etlsql', category: 'noise', categoryLabel: 'Safe Mock (Noise Dismissal)', reason: 'Developer helper script, excluded in production build', date: '2026-07-15', steward: 'Sarah' },
          { id: 'risk-8', asset: 'ad_hoc_export.etlsql', category: 'risk', categoryLabel: 'Durable Bypass (Security Risk)', reason: 'One-off report export requested by finance lead, deleted in 2 days', date: '2026-07-14', steward: 'Dan' }
        ],
        glossary: [
          { id: 'term-1', term: 'revenue', type: 'DECIMAL(18,2)', aliases: 'rev, gross_sales, turnover', desc: 'Standard business definition of sales intake, calculated before deductions.', steward: 'Chuck', formula: 'SUM(sales_amount)' },
          { id: 'term-2', term: 'salary', type: 'DECIMAL(10,2)', aliases: 'emp_salary, base_pay, compensation', desc: 'Employee annual base compensation rate. Subject to strict PII encryption.', steward: 'Sarah', formula: 'N/A (Stored Attribute)' },
          { id: 'term-3', term: 'patient_ssn', type: 'VARCHAR(11)', aliases: 'ssn, patient_id, soc_sec_num', desc: 'Social Security Number for medical record tracking. Sensitive PHI.', steward: 'Dan', formula: 'N/A (Identified Token)' },
          { id: 'term-4', term: 'lead_time_days', type: 'INT', aliases: 'ltd, warehouse_lead_time, delivery_days', desc: 'Logistics processing days between order placement and fulfillment.', steward: 'Chuck', formula: 'DATEDIFF(DAY, order_date, ship_date)' },
          { id: 'term-5', term: 'customer_id', type: 'INT', aliases: 'cust_id, client_number, purchaser_key', desc: 'Unique numeric sequence primary key identifying a registered store account.', steward: 'Sarah', formula: 'N/A (Key Column)' },
          { id: 'term-6', term: 'transaction_amount', type: 'DECIMAL(12,2)', aliases: 'tx_amt, payment_value, charge', desc: 'Financial transaction volume for credit card audit logs. PCI scoped.', steward: 'Dan', formula: 'N/A (Value Column)' },
          { id: 'term-7', term: 'length_of_stay', type: 'INT', aliases: 'los, stay_duration, days_hospitalized', desc: 'Total calendar days hospitalized for patient care audit reports.', steward: 'Dan', formula: 'DATEDIFF(DAY, admission_date, discharge_date)' }
        ],
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
        badgeDefinitions: [
          { name: 'Certified', desc: 'Officially certified by data governance. Meets all metadata and compliance standards.', color: 'cert' },
          { name: 'Trusted', desc: 'Verified source dataset or connection with lineage confirmed.', color: 'trust' },
          { name: 'GDPR Scoped', desc: 'Subject to General Data Protection Regulation audit checks.', color: 'gdpr' },
          { name: 'HIPAA Scoped', desc: 'Contains Protected Health Information (PHI) subject to HIPAA rules.', color: 'hipaa' }
        ]
      }
    };
    
    let activeMode = fixtureId === 'steward-view' ? 'steward' : 'all';
    let currentModuleTab = 'overview'; // 'overview', 'workqueue', 'exceptions', 'glossary', 'settings', 'lineage', 'badges'
    
    // Filters state
    let searchFilter = '';
    let categoryFilter = 'all';
    let badgeFilter = 'all';
    
    // Edit glossary state
    let editingTermId = null;
    let editingCatId = null;

    const wrapper = document.createElement('div');
    wrapper.className = 'gov-container';

    // Dynamic scoring calculator
    const computeScores = (item, settings) => {
      let score = 100;
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
    
    const render = () => {
      const modeData = data[activeMode];
      const radius = 18;
      const circ = 2 * Math.PI * radius;

      // Compute scored items
      const scoredQueue = modeData.queue.map(item => {
        const { score, scoreClass } = computeScores(item, modeData.settings);
        return { ...item, score, scoreClass };
      });
      
      // Calculate dynamic values
      const missingMetaCount = scoredQueue.filter(item => item.badges.includes('Needs Metadata') && modeData.settings.enableMeta).length;
      const activeSecurityRisksCount = modeData.risks.filter(r => r.category === 'risk').length;
      
      // Findings are items that score below the clean target score
      const unresolvedFindings = scoredQueue.filter(item => item.score < modeData.settings.targetScore);
      const totalOpenFindingsCount = unresolvedFindings.length;
      
      const governedPercent = Math.round(100 - (totalOpenFindingsCount / (scoredQueue.length || 1)) * 30);
      const strokeDashoffset = circ - (Math.max(0, Math.min(100, governedPercent)) / 100) * circ;

      // Filter Lists
      const filteredQueue = scoredQueue.filter(item => {
        const matchesSearch = item.path.toLowerCase().includes(searchFilter.toLowerCase());
        const matchesBadge = badgeFilter === 'all' || item.badges.includes(badgeFilter);
        return matchesSearch && matchesBadge;
      });

      const filteredRisks = modeData.risks.filter(risk => {
        const matchesSearch = risk.asset.toLowerCase().includes(searchFilter.toLowerCase());
        const matchesCategory = categoryFilter === 'all' || risk.category === categoryFilter;
        return matchesSearch && matchesCategory;
      });

      const filteredGlossary = modeData.glossary.filter(term => {
        const matchesSearch = term.term.toLowerCase().includes(searchFilter.toLowerCase()) || 
                              term.aliases.toLowerCase().includes(searchFilter.toLowerCase()) ||
                              term.desc.toLowerCase().includes(searchFilter.toLowerCase()) ||
                              term.formula.toLowerCase().includes(searchFilter.toLowerCase());
        return matchesSearch;
      });

      // Construct html string in a single variable to avoid browser auto-closing tags on partial templates
      let html = `
        <style>
          /* Layout structure */
          .gov-container {
            font-family: var(--portal-font, system-ui, sans-serif);
            color: var(--portal-text, #f9fafb);
            background: var(--portal-bg, #0b0f19);
            height: 100%;
            width: 100%;
            display: flex;
            flex-direction: row; /* Sidebar on left, workspace on right */
            box-sizing: border-box;
            overflow: hidden;
          }
          
          /* Align to Portal sidebar style specifications */
          .gov-sidebar {
            width: 230px;
            background: var(--portal-bg-soft, #111827);
            border-right: 1px solid var(--portal-border, #374151);
            display: flex;
            flex-direction: column;
            padding: 16px 8px;
            box-sizing: border-box;
            gap: 12px;
            flex-shrink: 0;
            height: 100%;
          }
          
          .gov-sidebar .sidebar-hdr {
            padding: 8px 12px;
            display: flex;
            flex-direction: column;
            gap: 2px;
          }
          .gov-sidebar .sidebar-hdr span {
            font-size: 12px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: var(--portal-text, #ffffff);
          }
          .gov-sidebar .sidebar-hdr .sidebar-hint {
            font-size: 10px;
            font-weight: 500;
            color: var(--portal-muted, #9ca3af);
            text-transform: none;
            letter-spacing: normal;
          }
          
          .gov-sidebar .sidebar-nav {
            display: flex;
            flex-direction: column;
            gap: 3px;
          }
          
          .gov-sidebar .sidebar-nav-item {
            background: none;
            border: none;
            color: var(--portal-text-soft, #d1d5db);
            padding: 9px 12px;
            border-radius: var(--portal-radius, 8px);
            text-align: left;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.15s ease;
            display: flex;
            align-items: center;
            gap: 10px;
            width: 100%;
            box-sizing: border-box;
          }
          .gov-sidebar .sidebar-nav-item:hover {
            background: var(--portal-surface-subtle, #1f2937);
            color: var(--portal-text, #ffffff);
          }
          .gov-sidebar .sidebar-nav-item.active {
            background: var(--portal-accent-soft, rgba(59, 130, 246, 0.15));
            color: var(--portal-accent, #3b82f6);
          }

          .gov-main-area {
            flex: 1 1 auto;
            min-width: 0;
            padding: 24px;
            display: flex;
            flex-direction: column;
            gap: 20px;
            box-sizing: border-box;
            overflow: hidden;
            height: 100%;
          }
          
          /* Header */
          .gov-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--portal-border, #374151);
            padding-bottom: 16px;
            flex: 0 0 auto;
          }
          .gov-header-title h1 {
            margin: 0;
            font-size: 22px;
            font-weight: 700;
            color: var(--portal-text, #ffffff);
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
          
          /* Scope Toggles */
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
            transition: all 0.15s ease;
          }
          .scope-btn:hover {
            color: var(--portal-text-soft, #d1d5db);
          }
          .scope-btn.active {
            background: var(--portal-accent, #3b82f6);
            color: #ffffff;
          }
          
          /* KPI Grid */
          .gov-kpi-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 16px;
            flex: 0 0 auto;
          }
          .kpi-card {
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
            transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
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
            letter-spacing: 0.05em;
            color: var(--portal-muted, #9ca3af);
          }
          .kpi-card-info .kpi-val {
            font-size: 24px;
            font-weight: 700;
            margin-top: 6px;
            color: var(--portal-text, #ffffff);
          }
          .kpi-card-chart {
            width: 44px;
            height: 44px;
            display: flex;
            align-items: center;
            justify-content: center;
          }
          .circular-ring {
            transform: rotate(-90deg);
            overflow: visible;
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
            stroke-linecap: round;
          }
          
          /* Filters Bar */
          .gov-filters-bar {
            display: flex;
            gap: 12px;
            align-items: center;
            background: var(--portal-surface-subtle, #111827);
            border: 1px solid var(--portal-border, #374151);
            padding: 8px 16px;
            border-radius: var(--portal-radius-sm, 5px);
            flex: 0 0 auto;
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
            padding: 6px 30px 6px 12px;
            font-size: 12px;
            border-radius: var(--portal-radius-sm, 5px);
            width: 240px;
          }
          .gov-filter-input::-webkit-search-cancel-button {
            display: none;
          }
          .gov-search-clear {
            position: absolute;
            right: 8px;
            background: none;
            border: none;
            color: var(--portal-muted, #9ca3af);
            cursor: pointer;
            font-size: 14px;
            padding: 0;
            display: ${searchFilter ? 'block' : 'none'};
          }
          .gov-filter-select {
            background: var(--portal-bg, #0b0f19);
            border: 1px solid var(--portal-border, #374151);
            color: var(--portal-text, #ffffff);
            padding: 6px 12px;
            font-size: 12px;
            border-radius: var(--portal-radius-sm, 5px);
          }

          /* Panel styling matching Portal report library panels */
          .gov-panel {
            background: var(--portal-surface, #1f2937);
            border: 1px solid var(--portal-border, #374151);
            border-radius: var(--portal-radius, 8px);
            padding: 20px;
            display: flex;
            flex-direction: column;
            gap: 16px;
            flex: 1 1 min-content;
            min-height: 0;
          }
          
          .gov-panel-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--portal-border-soft, #1f2937);
            padding-bottom: 12px;
            flex: 0 0 auto;
          }
          .gov-panel-header h2 {
            margin: 0;
            font-size: 15px;
            font-weight: 600;
            color: var(--portal-text, #ffffff);
          }

          /* Dense Grid Table styles */
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
            font-weight: 700;
          }
          .dense-table td {
            padding: 10px 12px;
            border-bottom: 1px solid var(--portal-border-soft, #1f2937);
            color: var(--portal-text-soft, #d1d5db);
            vertical-align: middle;
          }
          .dense-table tr:hover {
            background: var(--portal-surface-subtle, #111827);
          }

          /* Action queue panel max-height scroll limit container */
          .queue-scroll-container {
            flex: 1 1 auto;
            overflow-y: auto;
            display: flex;
            flex-direction: column;
            gap: 12px;
            padding-right: 6px;
            min-height: 0;
          }
          
          .queue-scroll-container::-webkit-scrollbar {
            width: 6px;
          }
          .queue-scroll-container::-webkit-scrollbar-track {
            background: var(--portal-bg, #0b0f19);
            border-radius: 4px;
          }
          .queue-scroll-container::-webkit-scrollbar-thumb {
            background: var(--portal-border, #374151);
            border-radius: 4px;
          }
          
          /* Action Queue Items */
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
            transition: all 0.15s ease;
          }
          .queue-item:hover {
            border-color: var(--portal-border, #374151);
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
          .score-high { background: var(--portal-success-soft, rgba(52, 211, 153, 0.15)); color: var(--portal-success, #34d399); }
          .score-med { background: var(--portal-warning-soft, rgba(251, 191, 36, 0.15)); color: var(--portal-warning, #fbbf24); }
          .score-low { background: var(--portal-danger-soft, rgba(248, 113, 113, 0.15)); color: var(--portal-danger, #f87171); }
          
          .queue-item-meta {
            font-size: 12px;
            color: var(--portal-muted, #9ca3af);
            display: flex;
            align-items: center;
            gap: 12px;
            margin-bottom: 14px;
            flex-wrap: wrap;
          }
          .badge-pill {
            background: var(--portal-bg, #0b0f19);
            color: var(--portal-text-soft, #d1d5db);
            padding: 1px 6px;
            border-radius: 4px;
            font-size: 11px;
            border: 1px solid var(--portal-border, #374151);
          }
          .badge-pill.danger {
            border-color: var(--portal-danger, #f87171);
            color: var(--portal-danger, #f87171);
            background: var(--portal-danger-soft, rgba(248, 113, 113, 0.15));
          }
          
          .queue-item-actions {
            display: flex;
            justify-content: flex-end;
            gap: 8px;
          }
          
          /* Buttons */
          .gov-btn {
            background: var(--portal-surface, #1f2937);
            color: var(--portal-text-soft, #d1d5db);
            border: 1px solid var(--portal-border, #374151);
            padding: 6px 12px;
            border-radius: var(--portal-radius-sm, 5px);
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.15s ease;
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
          .gov-btn-primary:hover {
            background: var(--portal-accent-hover, #60a5fa);
            border-color: var(--portal-accent-hover, #60a5fa);
          }
          .gov-btn-danger {
            background: var(--portal-danger-soft, rgba(248, 113, 113, 0.15));
            color: var(--portal-danger, #f87171);
            border-color: rgba(248, 113, 113, 0.3);
          }
          
          /* Exception badge categories styling */
          .exception-tag {
            font-size: 10px;
            padding: 2px 8px;
            border-radius: 4px;
            font-weight: 700;
            text-transform: uppercase;
            display: inline-block;
          }
          .exception-tag.risk { background: var(--portal-danger-soft, rgba(248, 113, 113, 0.15)); color: var(--portal-danger, #f87171); }
          .exception-tag.false-positive { background: var(--portal-success-soft, rgba(52, 211, 153, 0.15)); color: var(--portal-success, #34d399); }
          .exception-tag.noise { background: var(--portal-warning-soft, rgba(251, 191, 36, 0.15)); color: var(--portal-warning, #fbbf24); }

          /* Steward assigned business badges styling */
          .steward-badge-tag {
            font-size: 10.5px;
            padding: 2px 8px;
            border-radius: 4px;
            font-weight: 700;
            margin-right: 4px;
            display: inline-flex;
            align-items: center;
            gap: 4px;
            border: 1px solid transparent;
          }
          .steward-badge-tag.cert { background: rgba(59, 130, 246, 0.15); color: #60a5fa; border-color: rgba(59, 130, 246, 0.3); }
          .steward-badge-tag.trust { background: rgba(52, 211, 153, 0.15); color: #34d399; border-color: rgba(52, 211, 153, 0.3); }
          .steward-badge-tag.gdpr { background: rgba(251, 191, 36, 0.15); color: #fbbf24; border-color: rgba(251, 191, 36, 0.3); }
          .steward-badge-tag.hipaa { background: rgba(167, 139, 250, 0.15); color: #a78bfa; border-color: rgba(167, 139, 250, 0.3); }
          
          .remove-badge-btn {
            background: none;
            border: none;
            color: inherit;
            cursor: pointer;
            font-size: 11px;
            font-weight: bold;
            padding: 0 0 0 2px;
            line-height: 1;
            opacity: 0.6;
            transition: opacity 0.15s ease;
          }
          .remove-badge-btn:hover {
            opacity: 1;
          }

          /* Expandable Evidence Code panel */
          .evidence-panel {
            background: var(--portal-bg, #0b0f19);
            border: 1px solid var(--portal-border, #374151);
            border-radius: var(--portal-radius-sm, 5px);
            margin-top: 10px;
            padding: 12px;
            font-family: ui-monospace, monospace;
            font-size: 12px;
            color: var(--portal-muted, #9ca3af);
            display: none;
            overflow-x: auto;
          }
          .evidence-panel.open {
            display: block;
          }
          .code-line {
            display: flex;
            gap: 10px;
          }
          .code-num {
            color: var(--portal-muted, #5a6778);
            text-align: right;
            width: 20px;
            user-select: none;
          }
          .code-text {
            color: var(--portal-text-soft, #d1d5db);
            white-space: pre;
          }
          .code-hl {
            background: var(--portal-danger-soft, rgba(248, 113, 113, 0.15));
            border-left: 2px solid var(--portal-danger, #f87171);
            width: 100%;
          }

          /* Settings Specifi Layout styling */
          .settings-section-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 20px;
            overflow-y: auto;
            flex: 1 1 auto;
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
            color: var(--portal-text, #ffffff);
            border-bottom: 1px solid var(--portal-border-soft, #1f2937);
            padding-bottom: 8px;
          }
          .setting-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 12px;
          }
          .setting-label {
            font-size: 12px;
            color: var(--portal-text-soft, #cbd5e1);
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
            border-radius: var(--portal-radius-sm, 5px);
            text-align: center;
          }
          
          /* Modal Styles */
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
          .gov-modal-backdrop.open {
            opacity: 1;
            pointer-events: auto;
          }
          .gov-modal {
            background: var(--portal-surface, #1f2937);
            border: 1px solid var(--portal-border, #374151);
            border-radius: var(--portal-radius, 8px);
            width: 460px;
            max-width: 90%;
            padding: 24px;
            display: flex;
            flex-direction: column;
            gap: 16px;
            transform: translateY(20px);
            transition: transform 0.2s ease;
            box-shadow: var(--portal-shadow-md, 0 14px 30px rgba(0, 0, 0, 0.2));
          }
          .gov-modal-backdrop.open .gov-modal {
            transform: translateY(0);
          }
          .gov-modal-header h3 {
            margin: 0;
            color: var(--portal-text, #ffffff);
            font-size: 16px;
            font-weight: 600;
          }
          .gov-modal-body select, .gov-modal-body textarea, .gov-modal-body input {
            width: 100%;
            background: var(--portal-bg, #0b0f19);
            border: 1px solid var(--portal-border, #374151);
            border-radius: var(--portal-radius-sm, 5px);
            color: var(--portal-text, #ffffff);
            padding: 8px;
            font-size: 13px;
            margin-top: 6px;
            box-sizing: border-box;
          }
          .gov-modal-footer {
            display: flex;
            justify-content: flex-end;
            gap: 8px;
          }

          /* Lineage Graph Mock Styling */
          .lineage-graph-container {
            flex: 1 1 auto;
            display: flex;
            flex-direction: column;
            background: var(--portal-surface-subtle, #111827);
            border: 1px solid var(--portal-border, #374151);
            border-radius: var(--portal-radius, 8px);
            padding: 20px;
            gap: 16px;
            overflow: hidden;
            min-height: 0;
          }
          .lineage-canvas {
            flex: 1 1 auto;
            border: 1px dashed var(--portal-border-soft, #374151);
            background: radial-gradient(circle, var(--portal-surface, #1f2937) 1px, transparent 1px);
            background-size: 16px 16px;
            border-radius: var(--portal-radius, 8px);
            display: flex;
            align-items: center;
            justify-content: center;
            position: relative;
            overflow: auto;
          }
          .lineage-node {
            background: var(--portal-surface, #1f2937);
            border: 2px solid var(--portal-border, #374151);
            border-radius: var(--portal-radius-sm, 5px);
            padding: 10px 14px;
            font-size: 12px;
            min-width: 140px;
            box-shadow: var(--portal-shadow-md, 0 10px 20px rgba(0,0,0,0.15));
            display: flex;
            flex-direction: column;
            gap: 4px;
            position: absolute;
            transition: all 0.15s ease;
          }
          .lineage-node:hover {
            border-color: var(--portal-accent, #3b82f6);
            transform: scale(1.03);
          }
          .lineage-node .node-type {
            font-size: 9px;
            font-weight: 700;
            text-transform: uppercase;
            color: var(--portal-muted, #9ca3af);
          }
          .lineage-node .node-name {
            font-family: ui-monospace, monospace;
            font-weight: 700;
            color: var(--portal-text, #ffffff);
          }
          .lineage-node .node-meta {
            font-size: 10px;
            color: var(--portal-success, #34d399);
          }
          .lineage-arrow-svg {
            position: absolute;
            top: 0; left: 0;
            width: 100%; height: 100%;
            pointer-events: none;
            z-index: 0;
          }
          .arrow-path {
            fill: none;
            stroke: var(--portal-border, #374151);
            stroke-width: 2;
            marker-end: url(#arrowhead);
          }
        </style>

        <!-- Sidebar Navigation (Matching exact Portal layout styles) -->
        <div class="gov-sidebar">
          <div class="sidebar-hdr">
            <span>Governance</span>
            <span class="sidebar-hint">Data & Catalog</span>
          </div>
          <div class="sidebar-nav" aria-label="Governance views">
            <button class="sidebar-nav-item ${currentModuleTab === 'overview' ? 'active' : ''}" id="sideOverview">
              <span>📊 Overview</span>
            </button>
            <button class="sidebar-nav-item ${currentModuleTab === 'workqueue' ? 'active' : ''}" id="sideWorkqueue">
              <span>📋 Task Workqueue</span>
            </button>
            <button class="sidebar-nav-item ${currentModuleTab === 'exceptions' ? 'active' : ''}" id="sideExceptions">
              <span>🛡️ Exceptions Ledger</span>
            </button>
            <button class="sidebar-nav-item ${currentModuleTab === 'badges' ? 'active' : ''}" id="sideBadges">
              <span>🏷️ Badge Manager</span>
            </button>
            <button class="sidebar-nav-item ${currentModuleTab === 'glossary' ? 'active' : ''}" id="sideGlossary">
              <span>📖 Glossary & Terms</span>
            </button>
            <button class="sidebar-nav-item ${currentModuleTab === 'lineage' ? 'active' : ''}" id="sideLineage">
              <span>🔗 Lineage Explorer</span>
            </button>
            <button class="sidebar-nav-item ${currentModuleTab === 'settings' ? 'active' : ''}" id="sideSettings">
              <span>⚙️ Settings & Policies</span>
            </button>
          </div>
        </div>

        <!-- Main Workspace -->
        <div class="gov-main-area">
          
          <!-- Header -->
          <div class="gov-header">
            <div class="gov-header-title">
              <h1>Portal Governance Core</h1>
              <p>Data Stewardship overview and rule resolution portal</p>
            </div>
            <div class="gov-actions">
              <!-- CONDITIONAL ACTIONS: Hide scope togglers and Scan button on non-operational views -->
              ${currentModuleTab === 'overview' || currentModuleTab === 'workqueue' || currentModuleTab === 'exceptions' ? `
                <div class="scope-toggle">
                  <button class="scope-btn ${activeMode === 'steward' ? 'active' : ''}" id="btnStewardScope">My Steward Work</button>
                  <button class="scope-btn ${activeMode === 'all' ? 'active' : ''}" id="btnAllScope">All Governance</button>
                </div>
                <button class="gov-btn gov-btn-primary" id="btnScanNow">⚡ Scan Now</button>
              ` : ''}
            </div>
          </div>
      `;

      // Helper function to render active badges with dynamic removal buttons
      const renderAssignedBadges = (item) => {
        if (!item.assignedBadges || !item.assignedBadges.length) return '';
        return item.assignedBadges.map(b => {
          const styleClass = b.toLowerCase().includes('cert') ? 'cert' : b.toLowerCase().includes('trust') ? 'trust' : b.toLowerCase().includes('gdpr') ? 'gdpr' : 'hipaa';
          return `
            <span class="steward-badge-tag ${styleClass}">
              ★ ${b}
              <button class="remove-badge-btn" title="Remove Badge" data-remove-badge-name="${b}" data-remove-badge-asset="${item.id}">×</button>
            </span>
          `;
        }).join(' ');
      };

      if (currentModuleTab === 'overview') {
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
                  <circle class="bg" cx="22" cy="22" r="${radius}" />
                  <circle class="bar" cx="22" cy="22" r="${radius}" 
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
                    <span class="asset-path">${item.path}</span>
                    <span class="score-badge ${item.scoreClass}">Score: ${item.score}/100</span>
                  </div>
                  <div class="queue-item-meta">
                    <span>${item.meta}</span>
                    <span style="color:var(--portal-border, #374151);">|</span>
                    ${item.badges.map(b => `<span class="badge-pill ${b.includes('PII') || b.includes('PHI') || b.includes('PCI') ? 'danger' : ''}">${b}</span>`).join(' ')}
                    ${item.assignedBadges && item.assignedBadges.length ? 
                      `<span style="color:var(--portal-border, #374151);">|</span>` + renderAssignedBadges(item) : ''
                    }
                  </div>
                  <div class="queue-item-actions">
                    <button class="gov-btn" data-toggle-evidence="${item.id}">🔍 View Evidence</button>
                    <button class="gov-btn" data-mark-reviewed="${item.id}">✓ Mark Reviewed</button>
                    <button class="gov-btn gov-btn-danger" data-accept-risk="${item.id}">⚠️ Resolve Exception</button>
                  </div>
                  <div class="evidence-panel" id="evidence-${item.id}">
                    ${item.evidence.map(line => `
                      <div class="code-line ${line.hl ? 'code-hl' : ''}">
                        <span class="code-num">${line.num}</span>
                        <span class="code-text">${line.text}</span>
                      </div>
                    `).join('')}
                  </div>
                </div>
              `).join('')}
              <button class="gov-btn gov-btn-primary" id="btnGoToWorkqueue" style="align-self: flex-start; margin-top:8px;">View Full Workqueue (${modeData.queue.length} items) →</button>
            </div>
          </div>
        `;
      } else if (currentModuleTab === 'workqueue') {
        html += `
          <!-- Filters Bar -->
          <div class="gov-filters-bar">
            <div class="gov-filter-search-wrap">
              <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Filter by asset path..." value="${searchFilter}">
              <button class="gov-search-clear" id="btnSearchClear" title="Clear text">×</button>
            </div>
            
            <select class="gov-filter-select" id="badgeFilter">
              <option value="all" ${badgeFilter === 'all' ? 'selected' : ''}>All Violation Types</option>
              <option value="Needs Metadata" ${badgeFilter === 'Needs Metadata' ? 'selected' : ''}>Needs Metadata</option>
              <option value="Untagged PII" ${badgeFilter === 'Untagged PII' ? 'selected' : ''}>Untagged PII</option>
              <option value="Untagged PHI" ${badgeFilter === 'Untagged PHI' ? 'selected' : ''}>Untagged PHI</option>
              <option value="Untagged PCI" ${badgeFilter === 'Untagged PCI' ? 'selected' : ''}>Untagged PCI</option>
              <option value="Needs Review" ${badgeFilter === 'Needs Review' ? 'selected' : ''}>Needs Review</option>
              <option value="Glossary Review" ${badgeFilter === 'Glossary Review' ? 'selected' : ''}>Glossary Review</option>
            </select>
            
            <button class="gov-btn" id="btnResetFilters" style="padding: 4px 8px; font-size: 11px;">Clear All</button>
            
            <div style="margin-left: auto; display: flex; gap: 8px;">
              <button class="gov-btn gov-btn-primary" style="padding: 4px 8px; font-size: 11px;" onclick="alert('Bulk marked selection as Reviewed.')">Batch Mark Reviewed</button>
              <button class="gov-btn gov-btn-danger" style="padding: 4px 8px; font-size: 11px;" onclick="alert('Please select and bulk apply exceptions.')">Batch Accept Exception</button>
            </div>
          </div>

          <!-- Dense Tabular Workspace -->
          <div class="gov-panel">
            <div class="gov-panel-header">
              <h2>High-Density Task Workqueue</h2>
              <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">${filteredQueue.length} items visible</span>
            </div>
            
            <div class="queue-scroll-container">
              <table class="dense-table">
                <thead>
                  <tr>
                    <th width="30"><input type="checkbox" onclick="alert('Toggled selection on all rows.')"></th>
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
                      <td><input type="checkbox" value="${item.id}"></td>
                      <td class="asset-path">${item.path}</td>
                      <td><span class="score-badge ${item.scoreClass}">${item.score}</span></td>
                      <td>
                        ${item.badges.map(b => `<span class="badge-pill ${b.includes('PII') || b.includes('PHI') || b.includes('PCI') ? 'danger' : ''}">${b}</span>`).join(' ')}
                      </td>
                      <td>
                        ${renderAssignedBadges(item)}
                      </td>
                      <td>${item.meta.replace('Steward: ', '').replace('Domain: ', '')}</td>
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
                              <span class="code-text">${line.text}</span>
                            </div>
                          `).join('')}
                        </div>
                      </td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
              ${filteredQueue.length === 0 ? '<p style="text-align:center;color:var(--portal-muted, #9ca3af);padding:20px;">No findings matching search criteria.</p>' : ''}
            </div>
          </div>
        `;
      } else if (currentModuleTab === 'exceptions') {
        html += `
          <!-- Filters Bar -->
          <div class="gov-filters-bar">
            <div class="gov-filters-search-wrap">
              <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Filter exceptions..." value="${searchFilter}">
              <button class="gov-search-clear" id="btnSearchClear" title="Clear text">×</button>
            </div>
            
            <select class="gov-filter-select" id="categoryFilter">
              <option value="all" ${categoryFilter === 'all' ? 'selected' : ''}>All Resolution Types</option>
              <option value="risk" ${categoryFilter === 'risk' ? 'selected' : ''}>Durable Bypass (Security Risk)</option>
              <option value="false-positive" ${categoryFilter === 'false-positive' ? 'selected' : ''}>False Positive</option>
              <option value="noise" ${categoryFilter === 'noise' ? 'selected' : ''}>Safe Mock / Low Priority</option>
            </select>
            
            <button class="gov-btn" id="btnResetFilters" style="padding: 4px 8px; font-size: 11px;">Clear All</button>
          </div>

          <!-- DENSE Tabular Exceptions Ledger Workspace -->
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
                      <td class="asset-path">${risk.asset}</td>
                      <td><span class="exception-tag ${risk.category}">${risk.categoryLabel}</span></td>
                      <td style="font-style: italic;">"${risk.reason}"</td>
                      <td>${risk.steward} · ${risk.date}</td>
                      <td style="text-align:right;">
                        <button class="gov-btn gov-btn-danger" style="padding:2px 8px; font-size:11px;" data-reenable-risk="${risk.id}">Re-Enable</button>
                      </td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
              ${filteredRisks.length === 0 ? '<p style="color:var(--portal-muted, #9ca3af);font-size:12px;text-align:center;padding:20px;">No matching exceptions found.</p>' : ''}
            </div>
          </div>
        `;
      } else if (currentModuleTab === 'badges') {
        html += `
          <!-- Filters Bar -->
          <div class="gov-filters-bar">
            <div class="gov-filter-search-wrap">
              <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Filter assets to badge..." value="${searchFilter}">
              <button class="gov-search-clear" id="btnSearchClear" title="Clear text">×</button>
            </div>
            <button class="gov-btn" id="btnResetFilters" style="padding: 4px 8px; font-size: 11px;">Clear All</button>
            <span style="font-size:12px; color:var(--portal-muted, #9ca3af); margin-left: 10px;">Select badges to dynamically assign to compliance and connection assets.</span>
          </div>

          <div class="settings-section-grid">
            <!-- Left Side: Table of Assets and Active Badges -->
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
                    ${scoredQueue.filter(item => item.path.toLowerCase().includes(searchFilter.toLowerCase())).map(item => `
                      <tr>
                        <td class="asset-path">${item.path}</td>
                        <td>
                          ${item.assignedBadges && item.assignedBadges.length ? renderAssignedBadges(item) : '<span style="color:var(--portal-muted, #9ca3af); font-style:italic; font-size:11px;">None</span>'}
                        </td>
                        <td style="text-align:right;">
                          <select class="gov-filter-select" style="padding: 3px 6px; font-size:11px;" data-assign-badge-to="${item.id}">
                            <option value="">+ Assign Badge</option>
                            ${modeData.badgeDefinitions.filter(def => !item.assignedBadges.includes(def.name)).map(def => `
                              <option value="${def.name}">${def.name}</option>
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

            <!-- Right Side: Badge definitions dictionary -->
            <div class="settings-group">
              <h3>Defined Certification Badges</h3>
              <div style="flex:1 1 auto; overflow-y:auto; display:flex; flex-direction:column; gap:12px;">
                ${modeData.badgeDefinitions.map(def => `
                  <div style="background:var(--portal-bg, #0b0f19); border: 1px solid var(--portal-border, #374151); padding: 12px; border-radius:6px; display:flex; flex-direction:column; gap:6px;">
                    <div style="display:flex; justify-content:space-between; align-items:center;">
                      <span class="steward-badge-tag ${def.color}">★ ${def.name}</span>
                      <button class="gov-btn gov-btn-danger" style="padding:1px 6px; font-size:10px;" onclick="alert('Badge definition removal must be approved by GovernanceManager role.')">Delete</button>
                    </div>
                    <p style="font-size:12px; margin:0; color:var(--portal-muted, #9ca3af);">${def.desc}</p>
                  </div>
                `).join('')}
                <button class="gov-btn gov-btn-primary" style="align-self:flex-start;" onclick="alert('Create Badge definition form will launch a schema migration helper in portal settings.')">+ Create Custom Badge Definition</button>
              </div>
            </div>
          </div>
        `;
      } else if (currentModuleTab === 'glossary') {
        html += `
          <!-- Action header for glossary -->
          <div class="gov-filters-bar">
            <div class="gov-filter-search-wrap">
              <input type="search" class="gov-filter-input" id="searchFilter" placeholder="Search terms, formulas, or aliases..." value="${searchFilter}">
              <button class="gov-search-clear" id="btnSearchClear" title="Clear text">×</button>
            </div>
            
            <button class="gov-btn" id="btnResetFilters" style="padding: 4px 8px; font-size: 11px;">Clear All</button>
            
            <button class="gov-btn gov-btn-primary" id="btnAddNewTerm" style="margin-left: auto; padding: 6px 12px; font-size: 12px;">➕ Define Term</button>
          </div>

          <!-- Business Glossary Dense Grid -->
          <div class="gov-panel">
            <div class="gov-panel-header">
              <h2>Business Glossary & Terms</h2>
              <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">${filteredGlossary.length} terminology entries defined</span>
            </div>
            
            <div class="queue-scroll-container">
              <table class="dense-table">
                <thead>
                  <tr>
                    <th width="150">Term Name</th>
                    <th width="120">Standard Type</th>
                    <th width="180">Business Aliases</th>
                    <th width="240">Defined Formula Rule</th>
                    <th>Description</th>
                    <th width="80">Steward</th>
                    <th width="120" style="text-align:right;">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  ${filteredGlossary.map(t => `
                    <tr>
                      <td style="font-weight: 700; color: var(--portal-accent, #3b82f6);">${t.term}</td>
                      <td><code style="font-family: monospace; font-size: 11px; background:var(--portal-bg, #0b0f19); padding:2px 4px; border-radius:3px; border:1px solid var(--portal-border, #374151);">${t.type}</code></td>
                      <td>
                        ${t.aliases.split(',').map(a => `<span class="badge-pill" style="margin-right:2px;">${a.trim()}</span>`).join('')}
                      </td>
                      <td>
                        ${t.formula && t.formula !== 'N/A' && !t.formula.includes('Stored') && !t.formula.includes('Identified') && !t.formula.includes('Key') && !t.formula.includes('Value') ? 
                          `<code style="font-family: monospace; font-size: 11px; color:#f87171; background:rgba(248,113,113,0.1); padding:3px 6px; border-radius:4px; border:1px solid rgba(248,113,113,0.2);">${t.formula}</code>` :
                          `<span style="color:var(--portal-muted, #9ca3af); font-size:11px; font-style:italic;">${t.formula || 'None'}</span>`
                        }
                      </td>
                      <td style="font-size:12px; color:var(--portal-text-soft, #d1d5db);">${t.desc}</td>
                      <td>${t.steward}</td>
                      <td style="text-align:right;">
                        <button class="gov-btn" style="padding:2px 8px; font-size:11px;" data-edit-term="${t.id}">Edit</button>
                        <button class="gov-btn gov-btn-danger" style="padding:2px 8px; font-size:11px;" data-delete-term="${t.id}">Delete</button>
                      </td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
              ${filteredGlossary.length === 0 ? '<p style="text-align:center;color:var(--portal-muted, #9ca3af);padding:20px;">No terminology definitions found.</p>' : ''}
            </div>
          </div>
        `;
      } else if (currentModuleTab === 'lineage') {
        // Lineage Explorer UI mock!
        html += `
          <div class="gov-filters-bar">
            <div class="gov-filters-search-wrap">
              <input type="search" class="gov-filter-input" id="lineageSearch" placeholder="Search lineage index (e.g. length_of_stay)..." style="width:300px;">
            </div>
            <button class="gov-btn gov-btn-primary" onclick="alert('Searching lineage graph index...')">🔍 Search Index</button>
            <span style="font-size:12.5px; color:var(--portal-muted, #9ca3af); margin-left: 10px;">Select nodes to investigate deep column dependencies.</span>
          </div>

          <div class="lineage-graph-container">
            <div class="gov-panel-header" style="border:none; padding:0;">
              <h2>Dependency Lineage Graph Explorer</h2>
              <span style="font-size:12px; color:var(--portal-muted, #9ca3af);">Fuzzy Match Lineage Mappings: <b>Active</b></span>
            </div>
            
            <div class="lineage-canvas">
              <svg class="lineage-arrow-svg">
                <defs>
                  <marker id="arrowhead" markerWidth="10" markerHeight="7" refX="8" refY="3.5" orient="auto">
                    <polygon points="0 0, 10 3.5, 0 7" fill="var(--portal-border, #374151)" />
                  </marker>
                </defs>
                <!-- Render SVG connector paths -->
                <path d="M 190 120 L 300 120" class="arrow-path" />
                <path d="M 450 120 L 560 120" class="arrow-path" />
              </svg>
              
              <!-- Source node -->
              <div class="lineage-node" style="left: 40px; top: 90px; border-left: 4px solid var(--portal-success, #34d399);">
                <span class="node-type">Source Connection</span>
                <span class="node-name">mssql_prod.patients</span>
                <span class="node-meta">14 Column Attributes</span>
              </div>
              
              <!-- ETL-SQL transformation node -->
              <div class="lineage-node" style="left: 300px; top: 90px; border-left: 4px solid var(--portal-accent, #3b82f6);">
                <span class="node-type">ETL-SQL Pipeline</span>
                <span class="node-name">patient_health_audit.etlsql</span>
                <span class="node-meta" style="color:var(--portal-warning, #fbbf24);">Clean Score: 78</span>
              </div>

              <!-- Output visual report node -->
              <div class="lineage-node" style="left: 560px; top: 90px; border-left: 4px solid var(--portal-danger, #f87171);">
                <span class="node-type">Report Visual</span>
                <span class="node-name">patient_stay_duration.rptsql</span>
                <span class="node-meta" style="color:var(--portal-danger, #f87171);">Untagged PHI alert</span>
              </div>
            </div>
          </div>
        `;
      } else if (currentModuleTab === 'settings') {
        html += `
          <div class="settings-section-grid">
            
            <!-- Left Grid: Thresholds & scoring rules -->
            <div class="settings-group">
              <h3>Governance Score Thresholds</h3>
              
              <div class="setting-row">
                <div class="setting-label">
                  Target Clean Score Target Threshold
                  <span>Findings generated below this score. Current: <b>${modeData.settings.targetScore}</b></span>
                </div>
                <input type="range" id="settingsTargetScore" min="50" max="100" value="${modeData.settings.targetScore}" style="width:120px;">
              </div>

              <div style="margin-top: 8px; font-size:11px; color:var(--portal-muted, #9ca3af); font-weight:700; text-transform:uppercase; letter-spacing:0.05em;">Active Governance Checks & Deduction Weights</div>

              <div class="setting-row">
                <div class="setting-label">
                  <span style="font-size:12.5px; color:var(--portal-text, #ffffff); font-weight:600; display:flex; align-items:center; gap:6px;">
                    <input type="checkbox" id="chkEnableMeta" ${modeData.settings.enableMeta ? 'checked' : ''}> Ownership Metadata Check
                  </span>
                  <span>Verify presence of @owner, @steward, @domain tags.</span>
                </div>
                <input type="number" id="settingsDeductMeta" class="setting-input-number" value="${modeData.settings.deductMeta}" ${modeData.settings.enableMeta ? '' : 'disabled'}>
              </div>

              <div class="setting-row">
                <div class="setting-label">
                  <span style="font-size:12.5px; color:var(--portal-text, #ffffff); font-weight:600; display:flex; align-items:center; gap:6px;">
                    <input type="checkbox" id="chkEnablePII" ${modeData.settings.enablePII ? 'checked' : ''}> Sensitive Data Classification (PII/PHI/PCI)
                  </span>
                  <span>Audit verification of classified elements.</span>
                </div>
                <input type="number" id="settingsDeductPII" class="setting-input-number" value="${modeData.settings.deductPII}" ${modeData.settings.enablePII ? '' : 'disabled'}>
              </div>

              <div class="setting-row">
                <div class="setting-label">
                  <span style="font-size:12.5px; color:var(--portal-text, #ffffff); font-weight:600; display:flex; align-items:center; gap:6px;">
                    <input type="checkbox" id="chkEnableGlossary" ${modeData.settings.enableGlossary ? 'checked' : ''}> Glossary Alignment & Calculations
                  </span>
                  <span>Validate drift against business alias rules and formulas.</span>
                </div>
                <input type="number" id="settingsDeductGlossary" class="setting-input-number" value="${modeData.settings.deductGlossary}" ${modeData.settings.enableGlossary ? '' : 'disabled'}>
              </div>

              <div class="setting-row">
                <div class="setting-label">
                  <span style="font-size:12.5px; color:var(--portal-text, #ffffff); font-weight:600; display:flex; align-items:center; gap:6px;">
                    <input type="checkbox" id="chkEnableStale" ${modeData.settings.enableStale ? 'checked' : ''}> Asset Review Staleness Check
                  </span>
                  <span>Audit edits made since the last steward review boundary.</span>
                </div>
                <input type="number" id="settingsDeductStale" class="setting-input-number" value="${modeData.settings.deductStale}" ${modeData.settings.enableStale ? '' : 'disabled'}>
              </div>

              <div style="margin-top:auto; padding-top:12px; border-top: 1px solid var(--portal-border-soft, #1f2937); text-align:right;">
                <button class="gov-btn gov-btn-primary" id="btnSaveScoringSettings">Save Scoring Config</button>
              </div>
            </div>

            <!-- Right Grid: Resolution Exceptions settings -->
            <div class="settings-group">
              <div style="display:flex; justify-content:space-between; align-items:center;">
                <h3>Configurable Bypass Categories</h3>
                <button class="gov-btn" id="btnAddNewCategory" style="padding:4px 8px; font-size:11px;">➕ Add</button>
              </div>

              <div style="flex:1 1 auto; overflow-y:auto; max-height: 260px;">
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
                    ${modeData.resolutionCategories.map(cat => `
                      <tr>
                        <td style="font-weight:600;">${cat.label}</td>
                        <td><span class="exception-tag ${cat.color}">${cat.color}</span></td>
                        <td>${cat.expiry}</td>
                        <td style="text-align:right;">
                          <button class="gov-btn" style="padding:1px 6px; font-size:10px;" data-edit-cat="${cat.id}">Edit</button>
                          <button class="gov-btn gov-btn-danger" style="padding:1px 6px; font-size:10px;" data-delete-cat="${cat.id}">Del</button>
                        </td>
                      </tr>
                    `).join('')}
                  </tbody>
                </table>
              </div>

              <!-- Zero-trust system audit outbox options -->
              <div style="border-top: 1px solid var(--portal-border-soft, #1f2937); padding-top:12px; display:flex; flex-direction:column; gap:8px;">
                <div class="setting-label" style="font-weight:600; color:var(--portal-text, #ffffff);">
                  Audit Outbox Fail-Safe Mode
                  <span style="font-weight:normal;">Policy for outbox failures in zero-trust governance pipelines.</span>
                </div>
                <div style="display:flex; gap:16px; margin-top:4px;">
                  <label style="font-size:12px; display:flex; align-items:center; gap:6px; cursor:pointer;">
                    <input type="radio" name="auditBehavior" value="fail-closed" ${modeData.settings.auditBehavior === 'fail-closed' ? 'checked' : ''} id="optFailClosed"> Fail-Closed (Strict Enforcement)
                  </label>
                  <label style="font-size:12px; display:flex; align-items:center; gap:6px; cursor:pointer;">
                    <input type="radio" name="auditBehavior" value="fail-open" ${modeData.settings.auditBehavior === 'fail-open' ? 'checked' : ''} id="optFailOpen"> Fail-Open (Log Warnings Only)
                  </label>
                </div>
              </div>
            </div>

          </div>
        `;
      }

      // Close Main Workspace div and add modal elements
      html += `
          </div> <!-- End Main Workspace -->
          
          <!-- Accept Risk Modal -->
          <div class="gov-modal-backdrop" id="modalBackdrop">
            <div class="gov-modal">
              <div class="gov-modal-header">
                <h3>Resolve Exception / Mark Bypass</h3>
              </div>
              <div class="gov-modal-body">
                <p style="font-size:12px; color:var(--portal-muted, #9ca3af); margin:0 0 12px 0; line-height: 1.4;">Classify this bypass. This is tracked inside the Governance Security Audit log for zero-trust compliance.</p>
                
                <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Target Asset</label>
                <input type="text" id="modalAssetPath" readonly style="width:100%; background:var(--portal-bg, #0b0f19); border:1px solid var(--portal-border, #374151); color:var(--portal-muted, #9ca3af); padding:8px; border-radius:var(--portal-radius-sm, 5px); font-size:13px; font-family:monospace; margin-top:4px;">
                
                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Resolution Category</label>
                  <select id="modalCategory">
                    ${modeData.resolutionCategories.map(cat => `
                      <option value="${cat.value}">${cat.label}</option>
                    `).join('')}
                  </select>
                </div>

                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Exception Reason / Justification</label>
                  <textarea id="modalReason" rows="3" placeholder="Explain why this security exception is safe to bypass..."></textarea>
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
                <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Term Name</label>
                <input type="text" id="glossaryTerm" placeholder="e.g. length_of_stay" style="width:100%;">
                
                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Standard DataType</label>
                  <input type="text" id="glossaryType" placeholder="e.g. INT, VARCHAR(100), DECIMAL(18,2)" style="width:100%;">
                </div>

                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Business Aliases (Comma Separated)</label>
                  <input type="text" id="glossaryAliases" placeholder="e.g. los, stay_duration, days_hospitalized" style="width:100%;">
                </div>

                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Defined Calculation Formula Rule (Optional)</label>
                  <input type="text" id="glossaryFormula" placeholder="e.g. DATEDIFF(DAY, admission_date, discharge_date)" style="width:100%;">
                </div>

                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Description & Rule Context</label>
                  <textarea id="glossaryDesc" rows="3" placeholder="Explain the business calculations and security rules for this term..."></textarea>
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
                <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Category Label</label>
                <input type="text" id="catLabel" placeholder="e.g. Safe Test Script Override" style="width:100%;">
                
                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Value Identifier (URL Friendly)</label>
                  <input type="text" id="catValue" placeholder="e.g. safe-test" style="width:100%;">
                </div>

                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Color Class Theme Tag</label>
                  <select id="catColor">
                    <option value="risk">Red (Critical Threat Alert)</option>
                    <option value="noise">Yellow (Minor Alert Notification)</option>
                    <option value="false-positive">Green (Fully Compliance Excluded)</option>
                  </select>
                </div>

                <div style="margin-top:12px;">
                  <label style="font-size:12px; color:var(--portal-text-soft, #cbd5e1); font-weight:600;">Durable Expiry Limit</label>
                  <select id="catExpiry">
                    <option value="None">None (Indefinite Bypass)</option>
                    <option value="30 Days">30 Days</option>
                    <option value="90 Days">90 Days</option>
                    <option value="180 Days">180 Days</option>
                  </select>
                </div>
              </div>
              <div class="gov-modal-footer">
                <button class="gov-btn" id="btnCancelCategoryModal">Cancel</button>
                <button class="gov-btn gov-btn-primary" id="btnConfirmCategoryModal">Save Category</button>
              </div>
            </div>
          </div>
      `;

      // Single assignment to wrapper.innerHTML to prevent auto-closing tags and ensure correct layout nesting
      wrapper.innerHTML = html;
      
      // Bind Sidebar Links
      wrapper.querySelector('#sideOverview').addEventListener('click', () => {
        currentModuleTab = 'overview';
        render();
      });
      wrapper.querySelector('#sideWorkqueue').addEventListener('click', () => {
        currentModuleTab = 'workqueue';
        render();
      });
      wrapper.querySelector('#sideExceptions').addEventListener('click', () => {
        currentModuleTab = 'exceptions';
        render();
      });
      wrapper.querySelector('#sideBadges').addEventListener('click', () => {
        currentModuleTab = 'badges';
        render();
      });
      wrapper.querySelector('#sideGlossary').addEventListener('click', () => {
        currentModuleTab = 'glossary';
        render();
      });
      wrapper.querySelector('#sideLineage').addEventListener('click', () => {
        currentModuleTab = 'lineage';
        render();
      });
      wrapper.querySelector('#sideSettings').addEventListener('click', () => {
        currentModuleTab = 'settings';
        render();
      });

      const btnGoTo = wrapper.querySelector('#btnGoToWorkqueue');
      if (btnGoTo) {
        btnGoTo.addEventListener('click', () => {
          currentModuleTab = 'workqueue';
          render();
        });
      }

      // Bind Scope buttons (Only attach listeners if they exist in DOM)
      const btnSteward = wrapper.querySelector('#btnStewardScope');
      if (btnSteward) {
        btnSteward.addEventListener('click', () => {
          activeMode = 'steward';
          ctx.stat('Switched to My Steward Work');
          render();
        });
      }

      const btnAll = wrapper.querySelector('#btnAllScope');
      if (btnAll) {
        btnAll.addEventListener('click', () => {
          activeMode = 'all';
          ctx.stat('Switched to All Governance');
          render();
        });
      }

      // Bind KPI click filters
      const kpiGov = wrapper.querySelector('#kpiGoverned');
      if (kpiGov) {
        kpiGov.addEventListener('click', () => {
          currentModuleTab = 'workqueue';
          searchFilter = '';
          badgeFilter = 'all';
          ctx.stat('KPI Click: Opened workqueue.');
          render();
        });
      }

      const kpiMeta = wrapper.querySelector('#kpiMetadata');
      if (kpiMeta) {
        kpiMeta.addEventListener('click', () => {
          currentModuleTab = 'workqueue';
          searchFilter = '';
          badgeFilter = 'Needs Metadata';
          ctx.stat('KPI Click: Opened workqueue, filtered by Needs Metadata.');
          render();
        });
      }

      const kpiByp = wrapper.querySelector('#kpiBypasses');
      if (kpiByp) {
        kpiByp.addEventListener('click', () => {
          currentModuleTab = 'exceptions';
          searchFilter = '';
          categoryFilter = 'risk';
          ctx.stat('KPI Click: Opened Exceptions ledger, filtered by Durable Bypasses.');
          render();
        });
      }

      const kpiFind = wrapper.querySelector('#kpiFindings');
      if (kpiFind) {
        kpiFind.addEventListener('click', () => {
          currentModuleTab = 'workqueue';
          searchFilter = '';
          badgeFilter = 'all';
          ctx.stat('KPI Click: Opened workqueue, showing all unresolved findings.');
          render();
        });
      }
      
      // Bind Filters input
      const searchInput = wrapper.querySelector('#searchFilter');
      if (searchInput) {
        searchInput.addEventListener('input', (e) => {
          searchFilter = e.target.value;
        });
        searchInput.addEventListener('change', (e) => {
          searchFilter = e.target.value;
          render();
        });
        searchInput.addEventListener('keyup', (e) => {
          if (e.key === 'Enter') {
            searchFilter = e.target.value;
            render();
          }
        });
      }

      // Clear Search Text X Button
      const clearBtn = wrapper.querySelector('#btnSearchClear');
      if (clearBtn) {
        clearBtn.addEventListener('click', () => {
          searchFilter = '';
          render();
        });
      }

      // Reset All Filters Button
      const resetFiltersBtn = wrapper.querySelector('#btnResetFilters');
      if (resetFiltersBtn) {
        resetFiltersBtn.addEventListener('click', () => {
          searchFilter = '';
          badgeFilter = 'all';
          categoryFilter = 'all';
          render();
        });
      }
      
      const badgeSel = wrapper.querySelector('#badgeFilter');
      if (badgeSel) {
        badgeSel.addEventListener('change', (e) => {
          badgeFilter = e.target.value;
          render();
        });
      }

      const catSel = wrapper.querySelector('#categoryFilter');
      if (catSel) {
        catSel.addEventListener('change', (e) => {
          categoryFilter = e.target.value;
          render();
        });
      }

      // Bind Scan Button (Only attach listener if it exists in DOM)
      const scanBtn = wrapper.querySelector('#btnScanNow');
      if (scanBtn) {
        scanBtn.addEventListener('click', () => {
          ctx.stat('Triggered full background workspace linter scan...');
          alert('Initiating workspace governance linter scan...');
        });
      }
      
      // Bind toggle evidence buttons (supports both card and grid table lists)
      wrapper.querySelectorAll('[data-toggle-evidence]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-toggle-evidence');
          // check if grid table evidence row exists
          const evRow = wrapper.querySelector(`#evidence-row-${id}`);
          if (evRow) {
            evRow.style.display = evRow.style.display === 'none' ? 'table-row' : 'none';
          } else {
            const panel = wrapper.querySelector(`#evidence-${id}`);
            if (panel) panel.classList.toggle('open');
          }
        });
      });
      
      // Bind Mark Reviewed
      wrapper.querySelectorAll('[data-mark-reviewed]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-mark-reviewed');
          const modeData = data[activeMode];
          const index = modeData.queue.findIndex(item => item.id === id);
          if (index !== -1) {
            const assetName = modeData.queue[index].path;
            modeData.queue.splice(index, 1);
            ctx.stat(`Marked ${assetName} as Reviewed`);
            render();
          }
        });
      });
      
      // Bind Re-Enable Risk
      wrapper.querySelectorAll('[data-reenable-risk]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-reenable-risk');
          const modeData = data[activeMode];
          const index = modeData.risks.findIndex(r => r.id === id);
          if (index !== -1) {
            const risk = modeData.risks[index];
            modeData.risks.splice(index, 1);
            modeData.queue.push({
              id: 'asset-' + Date.now(),
              path: risk.asset,
              meta: `Steward: Chuck · Domain: General`,
              badges: ['Needs Review'],
              assignedBadges: [],
              evidence: [
                { num: 1, text: '-- Re-opened from accepted risk list' },
                { num: 2, text: '-- Awaiting full review' }
              ]
            });
            ctx.stat(`Re-enabled rule checks for ${risk.asset}`);
            render();
          }
        });
      });
      
      // Modal Bindings (Accept Risk)
      let pendingAssetId = null;
      wrapper.querySelectorAll('[data-accept-risk]').forEach(btn => {
        btn.addEventListener('click', () => {
          pendingAssetId = btn.getAttribute('data-accept-risk');
          const modeData = data[activeMode];
          const asset = modeData.queue.find(item => item.id === pendingAssetId);
          if (asset) {
            wrapper.querySelector('#modalAssetPath').value = asset.path;
            wrapper.querySelector('#modalReason').value = '';
            // set options from active resolution categories configuration!
            const selectEl = wrapper.querySelector('#modalCategory');
            selectEl.innerHTML = modeData.resolutionCategories.map(cat => `
              <option value="${cat.value}">${cat.label}</option>
            `).join('');
            
            wrapper.querySelector('#modalBackdrop').classList.add('open');
          }
        });
      });
      
      wrapper.querySelector('#btnCancelModal').addEventListener('click', () => {
        wrapper.querySelector('#modalBackdrop').classList.remove('open');
        pendingAssetId = null;
      });
      
      wrapper.querySelector('#btnConfirmModal').addEventListener('click', () => {
        const reason = wrapper.querySelector('#modalReason').value.trim();
        const category = wrapper.querySelector('#modalCategory').value;
        const categorySelect = wrapper.querySelector('#modalCategory');
        const categoryLabel = categorySelect.options[categorySelect.selectedIndex].text;

        if (!reason) {
          alert('Please provide a justification reason.');
          return;
        }
        
        const modeData = data[activeMode];
        const index = modeData.queue.findIndex(item => item.id === pendingAssetId);
        if (index !== -1) {
          const asset = modeData.queue[index];
          modeData.queue.splice(index, 1);
          modeData.risks.push({
            id: 'risk-' + Date.now(),
            asset: asset.path,
            category: category,
            categoryLabel: categoryLabel,
            reason: reason,
            date: new Date().toISOString().split('T')[0],
            steward: 'Chuck'
          });
          wrapper.querySelector('#modalBackdrop').classList.remove('open');
          ctx.stat(`Resolved exception for ${asset.path} [${categoryLabel}]: "${reason}"`);
          render();
        }
      });

      // Glossary Modal Bindings
      const gModal = wrapper.querySelector('#glossaryModalBackdrop');
      const btnAddTerm = wrapper.querySelector('#btnAddNewTerm');
      if (btnAddTerm) {
        btnAddTerm.addEventListener('click', () => {
          editingTermId = null;
          wrapper.querySelector('#glossaryModalTitle').textContent = 'Define New Term';
          wrapper.querySelector('#glossaryTerm').value = '';
          wrapper.querySelector('#glossaryTerm').readOnly = false;
          wrapper.querySelector('#glossaryType').value = '';
          wrapper.querySelector('#glossaryAliases').value = '';
          wrapper.querySelector('#glossaryFormula').value = '';
          wrapper.querySelector('#glossaryDesc').value = '';
          gModal.classList.add('open');
        });
      }

      const btnCancelGModal = wrapper.querySelector('#btnCancelGlossaryModal');
      if (btnCancelGModal) {
        btnCancelGModal.addEventListener('click', () => {
          gModal.classList.remove('open');
          editingTermId = null;
        });
      }

      const btnConfirmGModal = wrapper.querySelector('#btnConfirmGlossaryModal');
      if (btnConfirmGModal) {
        btnConfirmGModal.addEventListener('click', () => {
          const term = wrapper.querySelector('#glossaryTerm').value.trim();
          const type = wrapper.querySelector('#glossaryType').value.trim();
          const aliases = wrapper.querySelector('#glossaryAliases').value.trim();
          const formula = wrapper.querySelector('#glossaryFormula').value.trim();
          const desc = wrapper.querySelector('#glossaryDesc').value.trim();

          if (!term || !type || !aliases || !desc) {
            alert('Please fill out all fields.');
            return;
          }

          if (editingTermId) {
            // Edit existing
            const termIndex = modeData.glossary.findIndex(t => t.id === editingTermId);
            if (termIndex !== -1) {
              modeData.glossary[termIndex].type = type;
              modeData.glossary[termIndex].aliases = aliases;
              modeData.glossary[termIndex].formula = formula || 'N/A';
              modeData.glossary[termIndex].desc = desc;
              ctx.stat(`Updated Glossary Term: ${term}`);
            }
          } else {
            // Check for duplicate term name
            if (modeData.glossary.some(t => t.term.toLowerCase() === term.toLowerCase())) {
              alert(`Term "${term}" is already defined in the glossary.`);
              return;
            }
            // Add new
            modeData.glossary.push({
              id: 'term-' + Date.now(),
              term: term,
              type: type,
              aliases: aliases,
              formula: formula || 'N/A',
              desc: desc,
              steward: 'Chuck'
            });
            ctx.stat(`Defined New Glossary Term: ${term}`);
          }
          gModal.classList.remove('open');
          editingTermId = null;
          render();
        });
      }

      // Bind Edit Term buttons
      wrapper.querySelectorAll('[data-edit-term]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-edit-term');
          const termObj = modeData.glossary.find(t => t.id === id);
          if (termObj) {
            editingTermId = id;
            wrapper.querySelector('#glossaryModalTitle').textContent = 'Edit Defined Term';
            wrapper.querySelector('#glossaryTerm').value = termObj.term;
            wrapper.querySelector('#glossaryTerm').readOnly = true;
            wrapper.querySelector('#glossaryType').value = termObj.type;
            wrapper.querySelector('#glossaryAliases').value = termObj.aliases;
            wrapper.querySelector('#glossaryFormula').value = termObj.formula === 'N/A' ? '' : termObj.formula;
            wrapper.querySelector('#glossaryDesc').value = termObj.desc;
            gModal.classList.add('open');
          }
        });
      });

      // Bind Delete Term buttons
      wrapper.querySelectorAll('[data-delete-term]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-delete-term');
          const index = modeData.glossary.findIndex(t => t.id === id);
          if (index !== -1) {
            const name = modeData.glossary[index].term;
            if (confirm(`Are you sure you want to delete term "${name}"? This will invalidate metadata linters checking for this alias.`)) {
              modeData.glossary.splice(index, 1);
              ctx.stat(`Deleted Glossary Term: ${name}`);
              render();
            }
          }
        });
      });

      // Bind Badge Selection changes
      wrapper.querySelectorAll('[data-assign-badge-to]').forEach(select => {
        select.addEventListener('change', (e) => {
          const assetId = select.getAttribute('data-assign-badge-to');
          const badgeVal = e.target.value;
          if (!badgeVal) return;

          const itemIndex = modeData.queue.findIndex(item => item.id === assetId);
          if (itemIndex !== -1) {
            const item = modeData.queue[itemIndex];
            if (badgeVal === '__CLEAR__') {
              item.assignedBadges = [];
              ctx.stat(`Cleared all assigned badges for ${item.path}`);
            } else {
              if (!item.assignedBadges.includes(badgeVal)) {
                item.assignedBadges.push(badgeVal);
                ctx.stat(`Assigned badge "${badgeVal}" to ${item.path}`);
              }
            }
            render();
          }
        });
      });

      // Bind Individual Badge Tag Removal button
      wrapper.querySelectorAll('.remove-badge-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          const assetId = btn.getAttribute('data-remove-badge-asset');
          const badgeName = btn.getAttribute('data-remove-badge-name');
          const itemIndex = modeData.queue.findIndex(item => item.id === assetId);
          if (itemIndex !== -1) {
            const item = modeData.queue[itemIndex];
            item.assignedBadges = item.assignedBadges.filter(b => b !== badgeName);
            ctx.stat(`Revoked badge "${badgeName}" from ${item.path}`);
            render();
          }
        });
      });

      // Settings Save Buttons & Interactive bindings
      const sliderTarget = wrapper.querySelector('#settingsTargetScore');
      if (sliderTarget) {
        sliderTarget.addEventListener('input', (e) => {
          modeData.settings.targetScore = parseInt(e.target.value);
          wrapper.querySelector('.setting-label span b').textContent = e.target.value;
        });
        sliderTarget.addEventListener('change', () => {
          ctx.stat(`Updated Target Score Threshold to ${modeData.settings.targetScore}`);
          render();
        });
      }

      // Active checks toggle bindings
      const chkMeta = wrapper.querySelector('#chkEnableMeta');
      if (chkMeta) {
        chkMeta.addEventListener('change', (e) => {
          modeData.settings.enableMeta = e.target.checked;
          wrapper.querySelector('#settingsDeductMeta').disabled = !e.target.checked;
          ctx.stat(`Toggled Metadata Check to ${e.target.checked}`);
          render();
        });
      }
      
      const chkPII = wrapper.querySelector('#chkEnablePII');
      if (chkPII) {
        chkPII.addEventListener('change', (e) => {
          modeData.settings.enablePII = e.target.checked;
          wrapper.querySelector('#settingsDeductPII').disabled = !e.target.checked;
          ctx.stat(`Toggled PII Check to ${e.target.checked}`);
          render();
        });
      }

      const chkGlossary = wrapper.querySelector('#chkEnableGlossary');
      if (chkGlossary) {
        chkGlossary.addEventListener('change', (e) => {
          modeData.settings.enableGlossary = e.target.checked;
          wrapper.querySelector('#settingsDeductGlossary').disabled = !e.target.checked;
          ctx.stat(`Toggled Glossary Mismatch Check to ${e.target.checked}`);
          render();
        });
      }

      const chkStale = wrapper.querySelector('#chkEnableStale');
      if (chkStale) {
        chkStale.addEventListener('change', (e) => {
          modeData.settings.enableStale = e.target.checked;
          wrapper.querySelector('#settingsDeductStale').disabled = !e.target.checked;
          ctx.stat(`Toggled Review Staleness Check to ${e.target.checked}`);
          render();
        });
      }

      const saveScoringBtn = wrapper.querySelector('#btnSaveScoringSettings');
      if (saveScoringBtn) {
        saveScoringBtn.addEventListener('click', () => {
          modeData.settings.deductMeta = parseInt(wrapper.querySelector('#settingsDeductMeta').value) || 0;
          modeData.settings.deductPII = parseInt(wrapper.querySelector('#settingsDeductPII').value) || 0;
          modeData.settings.deductGlossary = parseInt(wrapper.querySelector('#settingsDeductGlossary').value) || 0;
          modeData.settings.deductStale = parseInt(wrapper.querySelector('#settingsDeductStale').value) || 0;
          ctx.stat('Saved linter deduction configuration weights successfully.');
          alert('Scoring policy thresholds updated in Governance settings memory.');
          render();
        });
      }

      // Audit behavior bindings
      const optClosed = wrapper.querySelector('#optFailClosed');
      if (optClosed) {
        optClosed.addEventListener('change', () => {
          modeData.settings.auditBehavior = 'fail-closed';
          ctx.stat('Zero-Trust audit set to strict fail-closed enforcement.');
        });
      }
      const optOpen = wrapper.querySelector('#optFailOpen');
      if (optOpen) {
        optOpen.addEventListener('change', () => {
          modeData.settings.auditBehavior = 'fail-open';
          ctx.stat('Zero-Trust audit set to fail-open (log only).');
        });
      }

      // Bypass categories buttons
      const cModal = wrapper.querySelector('#categoryModalBackdrop');
      const btnAddCat = wrapper.querySelector('#btnAddNewCategory');
      if (btnAddCat) {
        btnAddCat.addEventListener('click', () => {
          editingCatId = null;
          wrapper.querySelector('#categoryModalTitle').textContent = 'Define Bypass Category';
          wrapper.querySelector('#catLabel').value = '';
          wrapper.querySelector('#catValue').value = '';
          wrapper.querySelector('#catColor').value = 'noise';
          wrapper.querySelector('#catExpiry').value = 'None';
          cModal.classList.add('open');
        });
      }

      const btnCancelCatModal = wrapper.querySelector('#btnCancelCategoryModal');
      if (btnCancelCatModal) {
        btnCancelCatModal.addEventListener('click', () => {
          cModal.classList.remove('open');
          editingCatId = null;
        });
      }

      const btnConfirmCatModal = wrapper.querySelector('#btnConfirmCategoryModal');
      if (btnConfirmCatModal) {
        btnConfirmCatModal.addEventListener('click', () => {
          const label = wrapper.querySelector('#catLabel').value.trim();
          const value = wrapper.querySelector('#catValue').value.trim();
          const color = wrapper.querySelector('#catColor').value;
          const colorLabel = wrapper.querySelector('#catColor').options[wrapper.querySelector('#catColor').selectedIndex].text;
          const expiry = wrapper.querySelector('#catExpiry').value;

          if (!label || !value) {
            alert('Please fill in Category Label and Value Identifier.');
            return;
          }

          if (editingCatId) {
            const index = modeData.resolutionCategories.findIndex(c => c.id === editingCatId);
            if (index !== -1) {
              modeData.resolutionCategories[index].label = label;
              modeData.resolutionCategories[index].color = color;
              modeData.resolutionCategories[index].colorLabel = colorLabel;
              modeData.resolutionCategories[index].expiry = expiry;
              ctx.stat(`Updated Bypass Category: ${label}`);
            }
          } else {
            modeData.resolutionCategories.push({
              id: 'cat-' + Date.now(),
              value: value,
              label: label,
              color: color,
              colorLabel: colorLabel,
              expiry: expiry
            });
            ctx.stat(`Defined New Bypass Category: ${label}`);
          }
          cModal.classList.remove('open');
          editingCatId = null;
          render();
        });
      }

      // Bind category edit & delete buttons
      wrapper.querySelectorAll('[data-edit-cat]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-edit-cat');
          const cat = modeData.resolutionCategories.find(c => c.id === id);
          if (cat) {
            editingCatId = id;
            wrapper.querySelector('#categoryModalTitle').textContent = 'Edit Bypass Category';
            wrapper.querySelector('#catLabel').value = cat.label;
            wrapper.querySelector('#catValue').value = cat.value;
            wrapper.querySelector('#catColor').value = cat.color;
            wrapper.querySelector('#catExpiry').value = cat.expiry;
            cModal.classList.add('open');
          }
        });
      });

      wrapper.querySelectorAll('[data-delete-cat]').forEach(btn => {
        btn.addEventListener('click', () => {
          const id = btn.getAttribute('data-delete-cat');
          const realIndex = modeData.resolutionCategories.findIndex(c => c.id === id);
          if (realIndex !== -1) {
            const label = modeData.resolutionCategories[realIndex].label;
            if (confirm(`Are you sure you want to delete resolution category "${label}"? Active exceptions using this bypass type will default back to standard alerts.`)) {
              modeData.resolutionCategories.splice(realIndex, 1);
              ctx.stat(`Deleted Bypass Category: ${label}`);
              render();
            }
          }
        });
      });
    };
    
    // Initial Render
    render();
    stage.replaceChildren(wrapper);
    ctx.stat('Module navigation shell successfully rendered.');
    
    return {
      dispose() {},
      resize() {}
    };
  }
};
