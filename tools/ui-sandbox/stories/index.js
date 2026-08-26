// Story registry. Add a surface by writing a *.story.js module and listing it here.
import scriptEditor from './script-editor.story.js';
import scriptEditorUnified from './script-editor-unified.story.js';
import designer from './designer.story.js';
import constrainedHtmlRuntime from './constrained-html-runtime.story.js';
import lineageUi from './lineage-ui.story.js';
import lineageCatalog from './lineage-catalog.story.js';
import datasetsAdmin from './datasets-admin.story.js';
import subscriptionHistory from './subscription-history.story.js';
import adminCatalog from './admin-catalog.story.js';
import vscodeWebviews from './vscode-webviews.story.js';
import secretsAdmin from './secrets-admin.story.js';
import connectionsAdmin from './connections-admin.story.js';
import gatewaysAdmin from './gateways-admin.story.js';
import policyAuthorityAdmin from './policy-authority-admin.story.js';
import snapshotDesigner from './snapshot-designer.story.js';
import lineageDag from './lineage-dag.story.js';
import portalGovernance from './governance.story.js';
import dataQualityQueue from './data-quality-queue.story.js';
import portalResponsiveShell from './portal-responsive-shell.story.js';
import portalStudio from './portal-studio.story.js';
import portalOperations from './portal-operations.story.js';
import feedback from './feedback.story.js';
import triageBoard from './triage-board.story.js';
import orchestratorRunOverrides from './orchestrator-run-overrides.story.js';
import orchestratorCheckpointResume from './orchestrator-checkpoint-resume.story.js';
import orchestratorAcl from './orchestrator-acl.story.js';
import orchestratorAdmin from './orchestrator-admin.story.js';
import controlPlaneDashboard from './control-plane-dashboard.story.js';

export const categoryOrder = [
  'Admin & Fleet',
  'Control Plane & SaaS',
  'Orchestrator & Jobs',
  'Governance & Security',
  'Lineage & Graphs',
  'Designers & Visuals',
  'Script Editors & IDE',
  'Portal Shell & Views'
];

const categoryDefaults = {
  // Admin & Fleet
  'gateways-admin': 'Admin & Fleet',
  'connections-admin': 'Admin & Fleet',
  'secrets-admin': 'Admin & Fleet',
  'datasets-admin': 'Admin & Fleet',
  'policy-authority-admin': 'Admin & Fleet',
  'portal-operations': 'Admin & Fleet',
  'admin-catalog': 'Admin & Fleet',
  'subscription-history': 'Admin & Fleet',

  // Control Plane & SaaS
  'control-plane-dashboard': 'Control Plane & SaaS',
  'triage-board': 'Control Plane & SaaS',

  // Orchestrator & Jobs
  'orchestrator-admin-ui': 'Orchestrator & Jobs',
  'orchestrator-admin': 'Orchestrator & Jobs',
  'orchestrator-run-overrides': 'Orchestrator & Jobs',
  'orchestrator-checkpoint-resume': 'Orchestrator & Jobs',
  'orchestrator-acl': 'Orchestrator & Jobs',

  // Governance & Security
  'portal-governance': 'Governance & Security',
  'data-quality-queue': 'Governance & Security',

  // Lineage & Graphs
  'lineage-dag': 'Lineage & Graphs',
  'lineage-catalog': 'Lineage & Graphs',
  'lineage-ui': 'Lineage & Graphs',

  // Designers & Visuals
  'designer': 'Designers & Visuals',
  'constrained-html-runtime': 'Designers & Visuals',
  'snapshot-designer': 'Designers & Visuals',

  // Script Editors & IDE
  'script-editor': 'Script Editors & IDE',
  'script-editor-unified': 'Script Editors & IDE',
  'vscode-webviews': 'Script Editors & IDE',

  // Portal Shell & Views
  'portal-responsive-shell': 'Portal Shell & Views',
  'portal-studio': 'Portal Shell & Views',
  'feedback': 'Portal Shell & Views'
};

export const rawStories = [
  // Admin & Fleet
  gatewaysAdmin,
  connectionsAdmin,
  secretsAdmin,
  datasetsAdmin,
  policyAuthorityAdmin,
  portalOperations,
  adminCatalog,
  subscriptionHistory,

  // Control Plane & SaaS
  controlPlaneDashboard,
  triageBoard,

  // Orchestrator & Jobs
  orchestratorAdmin,
  orchestratorRunOverrides,
  orchestratorCheckpointResume,
  orchestratorAcl,

  // Governance & Security
  portalGovernance,
  dataQualityQueue,

  // Lineage & Graphs
  lineageDag,
  lineageCatalog,
  lineageUi,

  // Designers & Visuals
  designer,
  constrainedHtmlRuntime,
  snapshotDesigner,

  // Script Editors & IDE
  scriptEditor,
  scriptEditorUnified,
  vscodeWebviews,

  // Portal Shell & Views
  portalResponsiveShell,
  portalStudio,
  feedback
];

export const stories = rawStories.map((story) => {
  story.category = categoryDefaults[story.id] || story.category || 'Other Surfaces';
  return story;
});
