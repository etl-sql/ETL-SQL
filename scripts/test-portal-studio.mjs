import { readFileSync as readSplitSource } from 'node:fs';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import { readPortalPage, readPortalPageModule } from './lib/portal-page.mjs';

const studioVisualStageSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-visual-stage.js', 'utf8');
const studioFilterPanelSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-filter-panel.js', 'utf8');

const designerPersistenceSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-persistence.js', 'utf8');
const designerCanvasRenderSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-canvas-render.js', 'utf8');


const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');
const [studio, designerHost, api, controller, designer, sharedStudio, studioState, designerCss, css, program, portalHeader, indexPage, adminPage] = await Promise.all([
  Promise.resolve(readPortalPage('studio')),
  Promise.resolve(readPortalPage('designer')),
  read('src/ETL-SQL.Portal/wwwroot/js/api.js'),
  read('src/ETL-SQL.Portal/Controllers/StudioController.cs'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-state.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css'),
  read('src/ETL-SQL.Portal/wwwroot/css/portal.css'),
  read('src/ETL-SQL.Portal/Program.cs'),
  read('src/ETL-SQL.Portal/wwwroot/js/portal-header.js'),
  Promise.resolve(readPortalPage('index')),
  Promise.resolve(readPortalPage('admin'))
]);

// Both of the checks below are "this must exist nowhere" checks, and a negative assertion pinned to
// one file gets quietly weaker every time that file is split — the designer became eleven modules
// and studio.js a family of studio-*.js, so a pin to either would now pass while the thing it
// forbids lived one module over. These read the whole shared browser source instead, and name the
// file when they fail.
const sharedRoot = new URL('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/', root);
const sharedSources = await Promise.all(
  (await readdir(sharedRoot, { withFileTypes: true }))
    .filter(entry => entry.isFile() && entry.name.endsWith('.js'))
    .map(async entry => [entry.name, await readFile(new URL(entry.name, sharedRoot), 'utf8')]));
assert.ok(sharedSources.length >= 20, `Expected the split designer and studio modules, found ${sharedSources.length} files.`);

function assertAbsentEverywhere(pattern, what) {
  const offenders = sharedSources.filter(([, source]) => pattern.test(source)).map(([name]) => name);
  assert.deepEqual(offenders, [], `${what} (${pattern}) must appear in no shared browser module, but is in: ${offenders.join(', ')}`);
}

const moduleSource = readPortalPageModule('studio');
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
assert.match(designerPersistenceSource, /\/api\/studio\/reports/);
assertAbsentEverywhere(/\/api\/scripts\/upload/, 'the legacy script-upload endpoint');
assert.match(designer, /opts\.hideTopbar/);
assert.match(studioVisualStageSource, /hideTopbar: true/);
assert.match(studioVisualStageSource, /hideSidebar: true/);
assert.match(sharedStudio, /propertiesHost/);
assert.match(studioVisualStageSource, /requireDataFirst: true/);
assert.match(studioState, /snapshotCache: new Map/);
assert.match(sharedStudio, /canonicalDesignerMutation/);
assertAbsentEverywhere(/script\.replace\s*\(/, 'raw text mutation of the script');
assert.match(sharedStudio, /data-property-field/);
assert.match(sharedStudio, /data-action="run-selected"/);
assert.match(studioFilterPanelSource, /No filters yet/);
assert.match(designer, /data-edit-title/);
assert.match(designer, /refreshSnapshot: renderCanvas/);
assert.match(sharedStudio, /data-studio-tabbar/);
assert.match(sharedStudio, /data-studio-overflow-btn/);
assert.match(sharedStudio, /data-studio-tab-dropdown/);
assert.match(designerCanvasRenderSource, /dataset\.vid/);
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
