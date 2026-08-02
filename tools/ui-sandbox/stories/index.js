// Story registry. Add a surface by writing a *.story.js module and listing it here.
import scriptEditor from './script-editor.story.js';
import scriptEditorUnified from './script-editor-unified.story.js';
import designer from './designer.story.js';
import lineageUi from './lineage-ui.story.js';
import lineageCatalog from './lineage-catalog.story.js';
import datasetsAdmin from './datasets-admin.story.js';
import subscriptionHistory from './subscription-history.story.js';
import adminCatalog from './admin-catalog.story.js';
import vscodeWebviews from './vscode-webviews.story.js';
import secretsAdmin from './secrets-admin.story.js';
import connectionsAdmin from './connections-admin.story.js';
import policyAuthorityAdmin from './policy-authority-admin.story.js';
import snapshotDesigner from './snapshot-designer.story.js';
import lineageDag from './lineage-dag.story.js';
import portalGovernance from './governance.story.js';
import dataQualityQueue from './data-quality-queue.story.js';
import portalResponsiveShell from './portal-responsive-shell.story.js';
import portalStudio from './portal-studio.story.js';

export const stories = [
  portalGovernance,
  dataQualityQueue,
  portalResponsiveShell,
  portalStudio,
  scriptEditor,
  scriptEditorUnified,
  designer,
  snapshotDesigner,
  lineageUi,
  lineageCatalog,
  datasetsAdmin,
  subscriptionHistory,
  adminCatalog,
  vscodeWebviews,
  secretsAdmin,
  connectionsAdmin,
  policyAuthorityAdmin,
  lineageDag
];

