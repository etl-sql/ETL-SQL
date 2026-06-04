// Story registry. Add a surface by writing a *.story.js module and listing it here.
import dag from './dag.story.js';
import scriptEditor from './script-editor.story.js';
import designer from './designer.story.js';
import lineageUi from './lineage-ui.story.js';
import lineageCatalog from './lineage-catalog.story.js';
import datasetsAdmin from './datasets-admin.story.js';
import subscriptionHistory from './subscription-history.story.js';
import adminCatalog from './admin-catalog.story.js';
import vscodeWebviews from './vscode-webviews.story.js';

export const stories = [dag, scriptEditor, designer, lineageUi, lineageCatalog, datasetsAdmin, subscriptionHistory, adminCatalog, vscodeWebviews];
