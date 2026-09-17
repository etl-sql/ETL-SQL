import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');

const [studioJs, contractsJs, policyCs] = await Promise.all([
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-contracts.js'),
  read('src/ETL-SQL.Portal/Services/PortalInteractiveRunPolicy.cs')
]);

// ── 1. Studio Home Quick Actions Markup ─────────────────────────────────────
assert.match(
  studioJs,
  /data-create-from-home="etl"/,
  'Studio Home quick action cards must include an ETL creation action.'
);
assert.match(
  studioJs,
  /data-create-from-home="etl"[^>]*data-seed-sample/,
  'Sample ETL pipeline card must specify data-seed-sample to seed the starter script.'
);
assert.match(
  studioJs,
  /Sample ETL pipeline/,
  'Studio Home quick action card title must identify the Sample ETL pipeline.'
);

// ── 2. STUDIO_STARTER_SCRIPTS.etl Contract ──────────────────────────────────
assert.match(
  contractsJs,
  /etl:\s*`(?<script>[\s\S]*?)`/,
  'STUDIO_STARTER_SCRIPTS must define an etl starter script.'
);

const match = contractsJs.match(/etl:\s*`(?<script>[\s\S]*?)`/);
const etlScript = match?.groups?.script ?? '';
assert.ok(etlScript.length > 100, 'ETL starter script must not be empty.');

// Extraction and staging
assert.match(
  etlScript,
  /CREATE CONNECTION\s+\w+\s+AS\s+MOCKDB\(\);/,
  'ETL starter must declare an in-memory MOCKDB connection.'
);
assert.match(
  etlScript,
  /INTO\s+#recent_sales/,
  'ETL starter must stage extracted rows into #recent_sales.'
);

// Intermediate row inspection
assert.match(
  etlScript,
  /SELECT\s+\*\s+FROM\s+#recent_sales;/,
  'ETL starter must include intermediate inspection of #recent_sales.'
);

// Transformation and aggregation
assert.match(
  etlScript,
  /INTO\s+#revenue_by_region/,
  'ETL starter must aggregate data into #revenue_by_region.'
);
assert.match(
  etlScript,
  /SELECT\s+\*\s+FROM\s+#revenue_by_region;/,
  'ETL starter must include inspection of summary table #revenue_by_region.'
);

// Deliberate validation failure and repair
assert.match(
  etlScript,
  /ASSERT\s+\(SELECT COUNT\(\*\) FROM #recent_sales\)\s*>=\s*500/,
  'ETL starter must include a deliberate validation failure expecting >= 500 rows.'
);
assert.match(
  etlScript,
  /500 to 50/,
  'ETL starter must provide clear repair instructions to change 500 to 50.'
);

// Table cleanup
assert.match(
  etlScript,
  /DROP TABLE #revenue_by_region;\s*DROP TABLE #recent_sales;/,
  'ETL starter must clean up temporary engine tables with DROP TABLE.'
);

// ── 3. Portal Interactive Run Policy Governance ─────────────────────────────
assert.match(
  policyCs,
  /CreateConnectionStatement\s+\w+\s+when\s+string\.Equals\(\w+\.ConnectionType,\s*"MOCKDB",\s*StringComparison\.OrdinalIgnoreCase\)\s*=>\s*null,/,
  'PortalInteractiveRunPolicy must permit in-memory MOCKDB connections for interactive practice.'
);
assert.match(
  policyCs,
  /DropTableStatement\s+\w+\s+when\s+IsTempTable\(\w+\.TargetTable\.TableName\)\s*=>\s*null,/,
  'PortalInteractiveRunPolicy must permit dropping temporary tables.'
);
assert.match(
  policyCs,
  /AssertStatement\s*=>\s*null,/,
  'PortalInteractiveRunPolicy must permit ASSERT statements.'
);
assert.match(
  policyCs,
  /AssertTableStatement\s*=>\s*null,/,
  'PortalInteractiveRunPolicy must permit ASSERT TABLE statements.'
);
assert.match(
  policyCs,
  /SectionLabelStatement\s*=>\s*null,/,
  'PortalInteractiveRunPolicy must permit SectionLabelStatement.'
);

console.log('Studio ETL starter script and practice execution contracts passed.');
