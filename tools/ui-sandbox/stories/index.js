// Story registry. Add a surface by writing a *.story.js module and listing it here.
import dag from './dag.story.js';
import scriptEditor from './script-editor.story.js';
import designer from './designer.story.js';
import lineageUi from './lineage-ui.story.js';

export const stories = [dag, scriptEditor, designer, lineageUi];
