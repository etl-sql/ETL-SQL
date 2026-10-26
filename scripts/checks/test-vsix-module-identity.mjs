import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createRequire } from 'node:module';
import { getLoadedExtensionModule } from '../lib/loaded-extension-module.cjs';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'activated-extension-'));
const fixtureRequire = createRequire(path.join(root, 'module-proof.cjs'));
const modules = [];
function write(relative) {
    const filename = path.join(root, relative);
    fs.mkdirSync(path.dirname(filename), { recursive: true });
    fs.writeFileSync(filename, 'module.exports = { identity: Symbol("activated") };\n');
    return filename;
}
try {
    const filename = write('extension/out/reportDesignerPanel.js');
    const extensionRoot = path.dirname(path.dirname(filename));
    const aliasRoot = path.join(root, 'extension-alias');
    fs.symlinkSync(extensionRoot, aliasRoot, process.platform === 'win32' ? 'junction' : 'dir');
    const activated = fixtureRequire(filename);
    modules.push(fixtureRequire.resolve(filename));
    const before = Object.keys(fixtureRequire.cache).length;
    assert.equal(getLoadedExtensionModule(fixtureRequire.cache, aliasRoot, 'reportDesignerPanel.js'), activated);
    assert.equal(getLoadedExtensionModule(fixtureRequire.cache, extensionRoot, 'reportDesignerPanel.js'), activated);
    const aliasFile = path.join(aliasRoot, 'out/reportDesignerPanel.js');
    assert.equal(getLoadedExtensionModule({ [aliasFile]: { exports: activated } }, extensionRoot, 'reportDesignerPanel.js'), activated);
    assert.equal(Object.keys(fixtureRequire.cache).length, before, 'Lookup must not load another module instance.');
    const otherFile = write('other-extension/out/reportDesignerPanel.js');
    assert.throws(() => getLoadedExtensionModule({ [otherFile]: { exports: {} } }, aliasRoot, 'reportDesignerPanel.js'), /did not load/);
    const absentFile = path.join(root, 'absent/out/reportDesignerPanel.js');
    assert.equal(getLoadedExtensionModule({ [absentFile]: { exports: {} }, [filename]: { exports: activated } }, aliasRoot, 'reportDesignerPanel.js'), activated);
    console.log('Activated module identity: filesystem aliases retain the original instance; missing/stale entries and other copies cannot substitute for it.');
} finally {
    for (const filename of modules) delete fixtureRequire.cache[filename];
    // Remove only the private mkdtemp fixture and its internal alias.
    fs.rmSync(root, { recursive: true, force: true });
}
