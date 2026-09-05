/**
 * Reads a Portal page as one string: its markup, plus the page module it loads.
 *
 * Each page's behaviour used to sit in an inline `<script type="module">` block inside the .html,
 * and a dozen checks in scripts/ read the .html and asserted over both halves at once. The blocks
 * now live in `wwwroot/js/pages/<page>.js` — a file the type gate, the linters and the parse check
 * can all see — so a check that still reads only the .html is asserting over the markup alone and
 * would pass no matter what happened to the code.
 *
 * Joining them here keeps those assertions meaning what they meant, and keeps them working whether
 * a given page's code is inline or extracted.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const wwwroot = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot');

/** Every `<script type="module" src="…">` the page loads from `/js/pages/`, in document order. */
function pageModulePaths(html) {
    return [...html.matchAll(/<script\b[^>]*\bsrc="(\/js\/pages\/[^"?]+)(?:\?[^"]*)?"[^>]*>/gi)]
        .map(match => path.join(wwwroot, match[1].replace(/^\//, '')));
}

/**
 * The page's markup followed by the source of every page module it loads.
 *
 * @param {string} page A page name (`index`) or a path ending in one (`.../index.html`).
 * @returns {string}
 */
export function readPortalPage(page) {
    const html = readPortalPageMarkup(page);
    const modules = pageModulePaths(html)
        .filter(file => fs.existsSync(file))
        .map(file => fs.readFileSync(file, 'utf8'));
    return [html, ...modules].join('\n');
}

/** Just the page module source, with no markup — for a check that is about the code alone. */
export function readPortalPageModule(page) {
    const html = readPortalPageMarkup(page);
    const files = pageModulePaths(html).filter(file => fs.existsSync(file));
    // A page whose code has not been extracted still carries it inline.
    if (files.length === 0) {
        return [...html.matchAll(/<script type="module">([\s\S]*?)<\/script>/g)].map(m => m[1]).join('\n');
    }
    return files.map(file => fs.readFileSync(file, 'utf8')).join('\n');
}

export function readPortalPageMarkup(page) {
    const name = path.basename(String(page)).replace(/\.html$/i, '');
    return fs.readFileSync(path.join(wwwroot, `${name}.html`), 'utf8');
}
