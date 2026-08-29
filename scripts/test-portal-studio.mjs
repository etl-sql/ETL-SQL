import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [studio, designerHost, api, controller, designer, sharedStudio, designerCss, css, program, portalHeader, indexPage, adminPage] = await Promise.all([
  read('src/ETL-SQL.Portal/wwwroot/studio.html'),
  read('src/ETL-SQL.Portal/wwwroot/designer.html'),
  read('src/ETL-SQL.Portal/wwwroot/js/api.js'),
  read('src/ETL-SQL.Portal/Controllers/StudioController.cs'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css'),
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
assert.match(studio, /createStudioWorkbench/);
assert.match(studio, /\/api\/designer\/save/);
assert.match(designer, /id="dsgn-design-mode"/);
assert.match(designer, /id="dsgn-code-mode"/);
assert.match(designer, /\/api\/studio\/reports/);
assert.doesNotMatch(designer, /\/api\/scripts\/upload/);
assert.match(designer, /opts\.hideTopbar/);
assert.match(sharedStudio, /hideTopbar: true/);
assert.match(sharedStudio, /hideSidebar: true/);
assert.match(sharedStudio, /propertiesHost/);
assert.match(sharedStudio, /requireDataFirst: true/);
assert.match(sharedStudio, /snapshotCache: new Map/);
assert.match(sharedStudio, /['"]\/api\/designer\/parse['"]/);
assert.match(sharedStudio, /['"]\/api\/designer\/patch['"]/);
assert.match(sharedStudio, /canonicalDesignerMutation/);
assert.doesNotMatch(sharedStudio, /script\.replace\s*\(/);
assert.match(sharedStudio, /data-property-field/);
assert.match(sharedStudio, /data-action="run-selected"/);
assert.match(sharedStudio, /No filters yet/);
assert.match(designer, /data-edit-title/);
assert.match(designer, /refreshSnapshot: renderCanvas/);
assert.match(sharedStudio, /data-studio-tabbar/);
assert.match(sharedStudio, /data-studio-overflow-btn/);
assert.match(sharedStudio, /data-studio-tab-dropdown/);
assert.match(designer, /dataset\.vid/);
assert.match(designerCss, /\.etlsql-studio-tabbar/);
assert.match(designerCss, /\.etlsql-studio-tab-dropdown/);
assert.match(designerHost, /await studioApi\.session\(\)/);
assert.match(designerHost, /studioSession\.capabilities\.includes\('SourceCommit'\)/);
assert.match(css, /\.studio-report-grid/);
assert.match(program, /isStudioEntry/);
assert.match(portalHeader, /studioNav/);
assert.match(portalHeader, /display:none/);
assert.match(indexPage, /studioApi\.session\(\)/);
assert.match(adminPage, /studioApi\.session\(\)/);
assert.match(adminPage, /studioSession\?\.mode === 'CatalogOnly'/);
assert.match(adminPage, /Open in Studio/);
assert.match(adminPage, /exposesExternalSource/);

console.log('Portal catalog-scoped Studio, equal Code/Design modes, and disabled-authoring fencing contract passed.');
