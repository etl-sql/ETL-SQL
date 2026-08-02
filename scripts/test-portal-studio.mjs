import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [studio, designerHost, api, controller, designer, css, program, ...navPages] = await Promise.all([
  read('src/ETL-SQL.Portal/wwwroot/studio.html'),
  read('src/ETL-SQL.Portal/wwwroot/designer.html'),
  read('src/ETL-SQL.Portal/wwwroot/js/api.js'),
  read('src/ETL-SQL.Portal/Controllers/StudioController.cs'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js'),
  read('src/ETL-SQL.Portal/wwwroot/css/portal.css'),
  read('src/ETL-SQL.Portal/Program.cs'),
  ...['index.html', 'admin.html', 'docs.html', 'orchestrator.html'].map(name => read(`src/ETL-SQL.Portal/wwwroot/${name}`))
]);

const moduleSource = [...studio.matchAll(/<script type="module">([\s\S]*?)<\/script>/g)].at(-1)?.[1] || '';
assert.ok(moduleSource, 'Studio page module script was not found.');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
new AsyncFunction(moduleSource.replace(/^import .*;$/gm, ''));

assert.match(api, /export const studioApi/);
assert.match(controller, /RequireStudioCapability\(StudioCapabilities\.StudioAccess/);
assert.match(controller, /HttpPost\("reports"\)/);
assert.match(controller, /ArtifactArea\.Scripts/);
assert.match(controller, /FolderPermission\.Manage/);
assert.match(controller, /CREATE_STUDIO_REPORT/);
assert.doesNotMatch(studio, /scriptPath/);
assert.match(studio, /Catalog-only authoring/);
assert.match(studio, /class="studio-mode-rail"/);
assert.match(studio, />Design</);
assert.match(studio, />Code</);
assert.match(designer, /id="dsgn-design-mode"/);
assert.match(designer, /id="dsgn-code-mode"/);
assert.match(designer, /\/api\/studio\/reports/);
assert.doesNotMatch(designer, /\/api\/scripts\/upload/);
assert.match(designerHost, /await studioApi\.session\(\)/);
assert.match(designerHost, /studioSession\.capabilities\.includes\('SourceCommit'\)/);
assert.match(css, /\.studio-report-grid/);
assert.match(css, /\.studio-mode-rail/);
assert.match(program, /isStudioEntry/);
for (const page of navPages) {
  assert.match(page, /id="studioNav" style="display:none"/);
  assert.match(page, /studioApi\.session\(\)/);
}
assert.match(navPages[1], /studioSession\?\.mode === 'CatalogOnly'/);
assert.match(navPages[1], /Open in Studio/);
assert.match(navPages[1], /exposesExternalSource/);

console.log('Portal catalog-scoped Studio, equal Code/Design modes, and disabled-authoring fencing contract passed.');
