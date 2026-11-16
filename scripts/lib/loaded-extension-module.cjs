const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const canonicalPath = filename => {
    const resolved = fs.realpathSync.native(filename);
    return process.platform === 'win32' ? resolved.toLowerCase() : resolved;
};

// Find the activated instance through filesystem aliases without loading a second module.
function getLoadedExtensionModule(cache, extensionPath, name) {
    const expected = canonicalPath(path.join(extensionPath, 'out', name));
    for (const [filename, module] of Object.entries(cache)) {
        if (!module || path.basename(filename).toLowerCase() !== name.toLowerCase()) continue;
        try {
            if (canonicalPath(filename) === expected) return module.exports;
        } catch (error) {
            if (error.code !== 'ENOENT' && error.code !== 'ENOTDIR') throw error;
        }
    }
    assert.fail(`Activated extension did not load ${name} at ${expected}`);
}

module.exports = { getLoadedExtensionModule };
