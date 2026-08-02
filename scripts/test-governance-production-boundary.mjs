import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
const indexPath = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'index.html');
const source = fs.readFileSync(indexPath, 'utf8');

assert.doesNotMatch(source, /createGovernancePortal|governance-portal\.js/);
for (const unfinished of ['govNavOverview', 'govNavStewardship', 'govNavAudit', 'govNavBadges', 'govNavGlossary', 'govNavSettings']) {
  assert.doesNotMatch(source, new RegExp(`id=["']${unfinished}["']`));
}
assert.match(source, /id="govNavQuarantine"/);
assert.match(source, /id="govNavLineage"/);
assert.match(source, /setReportHash\('governance\/quarantine'\)/);
assert.match(source, /let mode = 'quarantine'/);
console.log('Production Governance exposes only durable quarantine and lineage routes.');
