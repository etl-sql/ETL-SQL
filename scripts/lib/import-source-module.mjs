/**
 * Imports a canonical browser source file as an ES module, for the scripts/test-*.mjs checks.
 *
 * These sources are served to the browser as plain `.js`, so Node will not import them directly —
 * the checks have always worked around that by copying the file to a temp path with an `.mjs`
 * extension and importing that. Copying it to `os.tmpdir()` quietly assumed the file had no
 * relative imports, and the moment `designer.js` gained `import … from './visual-preview.js'` the
 * copy resolved that specifier against the temp directory and the check died with
 * ERR_MODULE_NOT_FOUND — reported as a Portal lane failure with nothing wrong in the Portal.
 *
 * Writing the temp module beside its source instead keeps every relative specifier resolving
 * against the directory the author wrote it for, so a source may gain, move, or drop imports
 * without breaking the checks that read it.
 */

import { randomBytes } from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

/**
 * Copies `sourcePath` to a uniquely named `.mjs` sibling and imports it.
 *
 * @param {string} sourcePath Absolute path to the `.js` source to import.
 * @returns {Promise<{ module: any, cleanup: () => Promise<void> }>} The imported module namespace
 *   and a cleanup function that removes the temporary sibling. Call cleanup in a `finally`.
 */
export async function importSourceModule(sourcePath) {
    const resolved = path.resolve(sourcePath);
    const directory = path.dirname(resolved);
    const base = path.basename(resolved, path.extname(resolved));
    // Random rather than timestamped: two checks can run in the same millisecond, and a collision
    // would have one of them importing the other's copy.
    const temporary = path.join(directory, `${base}.__test-${randomBytes(6).toString('hex')}.mjs`);

    await fs.writeFile(temporary, await fs.readFile(resolved, 'utf8'), 'utf8');

    const cleanup = () => fs.rm(temporary, { force: true });
    try {
        return { module: await import(pathToFileURL(temporary).href), cleanup };
    } catch (error) {
        await cleanup();
        throw error;
    }
}
