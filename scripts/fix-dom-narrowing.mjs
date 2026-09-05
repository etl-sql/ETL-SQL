/**
 * Inserts the JSDoc casts that let `checkJs` narrow a DOM lookup to the element the code is
 * actually using.
 *
 * `document.getElementById(id)` is typed `HTMLElement`, `querySelector` is typed `Element`, and
 * `event.target` is typed `EventTarget` — none of which carry `.value`, `.dataset`, `.style` or
 * `.closest`. The code is right and the type is merely wide, so the fix is to say which element it
 * is, in a comment. Nothing here changes what runs: every edit is a comment plus a pair of
 * parentheses.
 *
 * The cast written is the narrowest one that is still *true*. Where a property is shared by
 * several element types — `.value` is on input, select and textarea alike — the cast is the union
 * of them, not a guess at one. A cast that names the wrong element would be worse than the wide
 * type it replaced: it would typecheck the following lines against the wrong shape.
 *
 * Usage:
 *   node scripts/fix-dom-narrowing.mjs <file> [<file> ...]   apply
 *   node scripts/fix-dom-narrowing.mjs --dry <file>          report what it would do
 */
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

// Resolved from the type gate's own pinned toolchain rather than from a root install, because
// there is no root package.json: node would then treat every .js file in the tree as belonging
// to a package with no "type" and warn on each one it has to detect as ESM.
const require = createRequire(import.meta.url);
const ts = require('./typecheck/node_modules/typescript/lib/typescript.js');

const repoRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, '$1'), '..');

/**
 * Property → the type to cast the receiver to. Only the narrowest true answer belongs here; when
 * in doubt the entry is left out and the finding stays visible rather than being papered over.
 */
const CAST_FOR_PROPERTY = {
    // Form value carriers. `.value` and `.disabled` are on all of these; `.checked` only on input.
    value: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement',
    disabled: 'HTMLButtonElement | HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement',
    checked: 'HTMLInputElement',
    indeterminate: 'HTMLInputElement',
    multiple: 'HTMLInputElement | HTMLSelectElement',
    options: 'HTMLSelectElement',
    rows: 'HTMLTextAreaElement',
    oninput: 'HTMLElement',

    // On HTMLElement but not on the wider Element / EventTarget.
    style: 'HTMLElement',
    dataset: 'HTMLElement',
    focus: 'HTMLElement',
    click: 'HTMLElement',
    title: 'HTMLElement',
    hidden: 'HTMLElement',
    inert: 'HTMLElement',
    offsetWidth: 'HTMLElement',
    offsetHeight: 'HTMLElement',
    offsetParent: 'HTMLElement',
    draggable: 'HTMLElement',
    tabIndex: 'HTMLElement',
    blur: 'HTMLElement',
    select: 'HTMLInputElement | HTMLTextAreaElement',
    setSelectionRange: 'HTMLInputElement | HTMLTextAreaElement',
    selectionStart: 'HTMLInputElement | HTMLTextAreaElement',
    selectionEnd: 'HTMLInputElement | HTMLTextAreaElement',
    placeholder: 'HTMLInputElement | HTMLTextAreaElement',
    readOnly: 'HTMLInputElement | HTMLTextAreaElement',
    name: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement | HTMLFormElement',
    form: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement',
    reset: 'HTMLFormElement',
    submit: 'HTMLFormElement',
    play: 'HTMLMediaElement',
    pause: 'HTMLMediaElement',
    naturalWidth: 'HTMLImageElement',
    naturalHeight: 'HTMLImageElement',
    complete: 'HTMLImageElement',
    download: 'HTMLAnchorElement',
    rel: 'HTMLAnchorElement | HTMLLinkElement',
    target: 'HTMLAnchorElement | HTMLFormElement',
    labels: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement',
    validity: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement',
    setCustomValidity: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement',
    reportValidity: 'HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLFormElement',
    innerText: 'HTMLElement',
    offsetTop: 'HTMLElement',
    offsetLeft: 'HTMLElement',
    accessKey: 'HTMLElement',
    lang: 'HTMLElement',
    dir: 'HTMLElement',

    // On Element but not on EventTarget.
    closest: 'Element',
    classList: 'Element',
    innerHTML: 'Element',
    getAttribute: 'Element',
    setAttribute: 'Element',
    querySelector: 'Element',
    querySelectorAll: 'Element',

    // Specific elements.
    contentWindow: 'HTMLIFrameElement',
    contentDocument: 'HTMLIFrameElement',
    src: 'HTMLImageElement | HTMLIFrameElement | HTMLScriptElement | HTMLMediaElement',
    href: 'HTMLAnchorElement | HTMLLinkElement',
    open: 'HTMLDetailsElement',
    files: 'HTMLInputElement',
    showPicker: 'HTMLInputElement | HTMLSelectElement',
    selectedOptions: 'HTMLSelectElement',
    selectedIndex: 'HTMLSelectElement',
    onchange: 'HTMLElement',
    // `type` is on input, button, select, textarea, link, script and more. The four that carry a
    // settable form `type` are the ones every call site here means.
    type: 'HTMLInputElement | HTMLButtonElement | HTMLSelectElement | HTMLTextAreaElement',

    // On Element / Node but not on EventTarget.
    tagName: 'Element',
    matches: 'Element',
    isContentEditable: 'HTMLElement',

    // Event subtypes, continued.
    relatedTarget: 'FocusEvent | MouseEvent',
    dataTransfer: 'DragEvent',

    // Event subtypes. `Event` is what an untyped handler parameter infers to.
    key: 'KeyboardEvent',
    ctrlKey: 'KeyboardEvent | MouseEvent',
    metaKey: 'KeyboardEvent | MouseEvent',
    shiftKey: 'KeyboardEvent | MouseEvent',
    altKey: 'KeyboardEvent | MouseEvent',
    clientX: 'MouseEvent',
    clientY: 'MouseEvent',
    pointerId: 'PointerEvent',
    pointerType: 'PointerEvent',

    // Properties report-runtime.js hangs off a visual's host element. See types/browser-globals.d.ts.
    _visualData: 'EtlSqlVisualHost',
    _updateCrosshair: 'EtlSqlVisualHost',
    _hideCrosshair: 'EtlSqlVisualHost',
    _detailSurface: 'EtlSqlVisualHost',
};

/** Receivers wide enough that narrowing them is the right fix. Anything else is a real finding. */
const NARROWABLE_RECEIVER = /^'(Element|HTMLElement|EventTarget|Event|Node|HTMLDivElement|HTMLInputElement \| HTMLTextAreaElement)'$/;

function loadProgram() {
    const configPath = path.join(repoRoot, 'tsconfig.json');
    const { config } = ts.readConfigFile(configPath, ts.sys.readFile);
    const parsed = ts.parseJsonConfigFileContent(config, ts.sys, repoRoot);
    return ts.createProgram(parsed.fileNames, parsed.options);
}

function normalize(p) {
    return path.resolve(p).replace(/\\/g, '/').toLowerCase();
}

const args = process.argv.slice(2);
const dryRun = args.includes('--dry');
const targets = new Set(args.filter(a => a !== '--dry').map(normalize));
if (targets.size === 0) {
    console.error('Name at least one file to fix.');
    process.exit(2);
}

let totalApplied = 0;

// One pass exposes the next: casting the receiver of `a.b.c` can reveal a finding on `.c`. Repeat
// until a pass changes nothing, with a ceiling so a property this script has no entry for cannot
// spin here forever.
for (let pass = 1; pass <= 8; pass++) {
    const program = loadProgram();
    /** @type {Map<string, Array<{start: number, end: number, cast: string, property: string}>>} */
    const editsByFile = new Map();

    for (const sourceFile of program.getSourceFiles()) {
        if (!targets.has(normalize(sourceFile.fileName))) continue;

        for (const diagnostic of program.getSemanticDiagnostics(sourceFile)) {
            // 2339 is "does not exist"; 2551 is the same finding when TypeScript also has a
            // near-miss to suggest (`contentDocument` vs `ownerDocument`). Same fix either way.
            if ((diagnostic.code !== 2339 && diagnostic.code !== 2551) || diagnostic.start === undefined) continue;
            const message = ts.flattenDiagnosticMessageText(diagnostic.messageText, ' ');
            const match = /^Property '([^']+)' does not exist on type ('[^']+')\.(?: Did you mean .*)?$/.exec(message);
            if (!match) continue;

            const [, property, receiverType] = match;
            const cast = CAST_FOR_PROPERTY[property];
            if (!cast || !NARROWABLE_RECEIVER.test(receiverType)) continue;

            const node = findNodeAt(sourceFile, diagnostic.start);
            const access = node?.parent;
            if (!access || !ts.isPropertyAccessExpression(access) || access.name !== node) continue;

            const receiver = access.expression;
            // Already cast (a previous pass, or by hand): leave it alone.
            if (ts.isParenthesizedExpression(receiver)) continue;

            editsByFile.set(sourceFile.fileName, editsByFile.get(sourceFile.fileName) ?? []);
            editsByFile.get(sourceFile.fileName).push({
                start: receiver.getStart(sourceFile),
                end: receiver.getEnd(),
                cast,
                property,
            });
        }
    }

    if (editsByFile.size === 0) {
        console.log(`pass ${pass}: nothing left to narrow`);
        break;
    }

    let appliedThisPass = 0;
    for (const [fileName, edits] of editsByFile) {
        const text = fs.readFileSync(fileName, 'utf8');
        // Apply back to front so earlier offsets stay valid, and drop overlaps — a receiver that is
        // itself inside another receiver is handled by the next pass.
        const ordered = edits.sort((a, b) => b.start - a.start);
        let out = text;
        let lastStart = Infinity;
        for (const edit of ordered) {
            if (edit.end > lastStart) continue;
            out = out.slice(0, edit.start)
                + `/** @type {${edit.cast}} */ (`
                + out.slice(edit.start, edit.end)
                + ')'
                + out.slice(edit.end);
            lastStart = edit.start;
            appliedThisPass++;
        }
        if (dryRun) {
            console.log(`pass ${pass}: would apply ${edits.length} cast(s) to ${path.relative(repoRoot, fileName)}`);
        } else {
            fs.writeFileSync(fileName, out);
            console.log(`pass ${pass}: ${appliedThisPass} cast(s) -> ${path.relative(repoRoot, fileName)}`);
        }
    }
    totalApplied += appliedThisPass;
    if (dryRun || appliedThisPass === 0) break;
}

console.log(`${dryRun ? 'would apply' : 'applied'} ${totalApplied} cast(s)`);

/** @param {import('typescript').Node} node */
function findNodeAt(node, position) {
    let found = null;
    const visit = current => {
        if (position < current.getStart() || position >= current.getEnd()) return;
        found = current;
        ts.forEachChild(current, visit);
    };
    ts.forEachChild(node, visit);
    return found;
}
