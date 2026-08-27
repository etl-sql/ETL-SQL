import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [studio, designerHost, api, controller, designer, css, program, portalHeader, indexPage, adminPage] = await Promise.all([
  read('src/ETL-SQL.Portal/wwwroot/studio.html'),
  read('src/ETL-SQL.Portal/wwwroot/designer.html'),
  read('src/ETL-SQL.Portal/wwwroot/js/api.js'),
  read('src/ETL-SQL.Portal/Controllers/StudioController.cs'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js'),
  read('src/ETL-SQL.Portal/wwwroot/css/portal.css'),
  read('src/ETL-SQL.Portal/Program.cs'),
  read('src/ETL-SQL.Portal/wwwroot/js/portal-header.js'),
  read('src/ETL-SQL.Portal/wwwroot/index.html'),
  read('src/ETL-SQL.Portal/wwwroot/admin.html')
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
assert.match(portalHeader, /studioNav/);
assert.match(portalHeader, /display:none/);
assert.match(indexPage, /studioApi\.session\(\)/);
assert.match(adminPage, /studioApi\.session\(\)/);
assert.match(adminPage, /studioSession\?\.mode === 'CatalogOnly'/);
assert.match(adminPage, /Open in Studio/);
assert.match(adminPage, /exposesExternalSource/);

console.log('Portal catalog-scoped Studio, equal Code/Design modes, and disabled-authoring fencing contract passed.');
