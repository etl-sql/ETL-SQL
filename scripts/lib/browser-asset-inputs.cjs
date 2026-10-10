const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');

const surfaces = [
    ['src/ETL-SQL.ReportRuntime/Resources/TypeScript', 'src/ETL-SQL.ReportRuntime/Resources/Shared'],
    ['src/ETL-SQL.Portal/BrowserSources', 'src/ETL-SQL.Portal/wwwroot/js'],
];
const shared = 'src/ETL-SQL.ReportRuntime/Resources/Shared/';
const bundle = shared + 'report-runtime.bundle.js';
const hostRoots = ['src/ETL-SQL.ReportPlayer/wwwroot/',
    'src/ETL-SQL.WorkstationEditor/wwwroot/', 'src/etl-sql-vscode/media/'];
const inventoryName = 'artifacts/browser-assets.json';
const hash = content => createHash('sha256').update(content).digest('hex');

function relativePath(value) {
    if (typeof value !== 'string' || !value || value.includes('\\') || value.includes(':') ||
        value.includes('\0') || value.startsWith('/') || value.split('/').some(p => !p || p === '.' || p === '..'))
        throw new Error(`Invalid browser ownership path: ${String(value)}`);
    return value;
}

// Validate each existing ancestor, including roots supplied by isolated-checkout tests. A lexical
// containment check alone would allow a junction to redirect a generated write outside the tree.
function safeAssetPath(root, relative) {
    relativePath(relative);
    root = path.resolve(root);
    let current = root;
    for (const segment of ['', ...relative.split('/')]) {
        if (segment) {
            if (fs.existsSync(current) && fs.statSync(current).isDirectory()) {
                const actual = fs.readdirSync(current).find(name => name.toLowerCase() === segment.toLowerCase());
                if (actual && actual !== segment) throw new Error(`Browser path casing collision: ${relative} (${actual})`);
            }
            current = path.join(current, segment);
        }
        let stat;
        try { stat = fs.lstatSync(current); } catch (error) { if (error.code === 'ENOENT') continue; throw error; }
        if (stat.isSymbolicLink()) throw new Error(`Browser asset symlink or junction is unsupported: ${current}`);
    }
    return current;
}

function portalTarget(relative) {
    if (relative.startsWith('maps/') || relative.startsWith('designer/'))
        return 'src/ETL-SQL.Portal/wwwroot/' + relative;
    return 'src/ETL-SQL.Portal/wwwroot/' + (path.extname(relative).toLowerCase() === '.css' ? 'css/' : 'js/') + relative;
}

function validateOwner(owner) {
    const output = relativePath(owner?.path), source = relativePath(owner?.source);
    if (owner.kind === 'typescript') {
        const surface = surfaces.find(([input]) => source.startsWith(input + '/') &&
            source.endsWith('.ts') && !source.endsWith('.d.ts'));
        if (!surface || output !== surface[1] + source.slice(surface[0].length).replace(/\.ts$/, '.js'))
            throw new Error(`Invalid TypeScript output owner: ${source} -> ${output}`);
    } else if (owner.kind === 'offline-bundle') {
        if (output !== bundle || source !== 'scripts/sync-assets.js' || !Array.isArray(owner.inputs) ||
            owner.inputs.some(input => !relativePath(input).startsWith(shared) || !input.endsWith('.js')))
            throw new Error(`Invalid offline bundle owner: ${output}`);
    } else if (owner.kind === 'host-copy') {
        if (!source.startsWith(shared)) throw new Error(`Invalid host-copy owner: ${source}`);
        const relative = source.slice(shared.length);
        if (![...hostRoots.map(host => host + relative), portalTarget(relative)].includes(output))
            throw new Error(`Invalid host-copy output: ${source} -> ${output}`);
    } else throw new Error(`Unknown browser output owner kind: ${owner.kind}`);
}

function readOwnership(root) {
    const file = safeAssetPath(root, inventoryName);
    if (!fs.existsSync(file)) return { version: 2, outputs: [] };
    let inventory;
    try { inventory = JSON.parse(fs.readFileSync(file, 'utf8')); }
    catch { throw new Error('Malformed browser ownership inventory: artifacts/browser-assets.json'); }
    if (![1, 2].includes(inventory?.version) || !Array.isArray(inventory.outputs))
        throw new Error('Unsupported browser ownership inventory: artifacts/browser-assets.json');
    const seen = new Set();
    for (const owner of inventory.outputs) {
        validateOwner(owner);
        if (seen.has(owner.path.toLowerCase())) throw new Error(`Duplicate browser output owner: ${owner.path}`);
        seen.add(owner.path.toLowerCase());
        if (inventory.version === 2 && !/^[a-f0-9]{64}$/.test(owner.sha256))
            throw new Error(`Missing or invalid browser output hash: ${owner.path}`);
    }
    return inventory;
}

// Every expected byte is computed before this preflight. Existing files may be adopted only when
// byte-identical to that fresh result, or replaced/removed when the last validated inventory hash
// still matches them. Banners never grant ownership. Version 1 can bootstrap exact bytes only.
function prepareOutputs(root, outputs, { check = false, partial = false } = {}) {
    const previous = readOwnership(root);
    if (!check && partial && previous.version === 1 && previous.outputs.some(owner => owner.kind !== 'typescript'))
        throw new Error('Bootstrap the complete browser ownership inventory first: node scripts/sync-assets.js');
    const old = new Map(previous.outputs.map(owner => [owner.path.toLowerCase(), owner]));
    const expected = new Map();
    const changes = [];
    const stale = [];
    for (const output of outputs) {
        validateOwner(output);
        const key = output.path.toLowerCase();
        if (expected.has(key)) throw new Error(`Browser output collision: ${output.path} (${expected.get(key).source}, ${output.source})`);
        const file = safeAssetPath(root, output.path);
        safeAssetPath(root, output.source);
        const content = Buffer.from(output.content);
        const owner = { path: output.path, kind: output.kind, source: output.source,
            ...(output.inputs ? { inputs: output.inputs } : {}), sha256: hash(content) };
        expected.set(key, owner);
        const actual = fs.existsSync(file) ? fs.readFileSync(file) : null;
        const before = actual ? hash(actual) : null;
        const prior = old.get(key);
        if (prior && (prior.kind !== owner.kind || prior.source !== owner.source))
            throw new Error(`Browser output ownership conflict: ${owner.path} (${prior.source}, ${owner.source})`);
        if (before === owner.sha256) continue;
        const reproducibleEarlierFormat = output.bootstrapContents?.some(content => hash(Buffer.from(content)) === before);
        if (actual && !reproducibleEarlierFormat && !(previous.version === 2 && prior?.sha256 === before))
            throw new Error(`Browser output conflicts with a retained or locally edited file: ${owner.path} (owner: ${owner.source}). Preserve the change in its owner before preparing assets.`);
        stale.push(output.path);
        changes.push({ file, content, before });
    }
    const obsolete = previous.version === 2 ? previous.outputs.filter(owner =>
        (!partial || owner.kind === 'typescript') && !expected.has(owner.path.toLowerCase())) : [];
    if (previous.version === 1) {
        const unverified = previous.outputs.filter(owner => (!partial || owner.kind === 'typescript') &&
            !expected.has(owner.path.toLowerCase()) && fs.existsSync(safeAssetPath(root, owner.path)));
        if (unverified.length) throw new Error(`Unverified obsolete browser outputs need review before bootstrap: ${unverified.map(owner => owner.path).join(', ')}. A version-1 inventory cannot authorize cleanup.`);
    }
    for (const owner of obsolete) {
        const file = safeAssetPath(root, owner.path);
        if (!fs.existsSync(file)) continue;
        const before = hash(fs.readFileSync(file));
        if (before !== owner.sha256)
            throw new Error(`Obsolete browser output contains local edits: ${owner.path}. Preserve it before cleanup.`);
        changes.push({ file, content: null, before });
    }
    if (check && (stale.length || obsolete.length))
        throw new Error(`${obsolete.length ? 'Orphaned browser output: ' + obsolete.map(o => o.path).join(', ') + '\n' : ''}TypeScript or host output missing or stale: ${stale.join(', ')}\nRun node scripts/sync-assets.js`);
    const owners = [...expected.values(), ...(partial && previous.version === 2
        ? previous.outputs.filter(owner => owner.kind !== 'typescript') : [])].sort((a, b) => a.path < b.path ? -1 : 1);
    const inventory = JSON.stringify({ version: 2, outputs: owners }, null, 2) + '\n';
    const file = safeAssetPath(root, inventoryName);
    if (!partial && check && (!fs.existsSync(file) || fs.readFileSync(file, 'utf8') !== inventory))
        throw new Error('Browser ownership inventory missing or stale: artifacts/browser-assets.json');
    if (!check) {
        // Recheck the complete set before mutation, then guard each individual operation as well.
        const unchanged = operation => {
            safeAssetPath(root, path.relative(root, operation.file).replaceAll('\\', '/'));
            const current = fs.existsSync(operation.file) ? hash(fs.readFileSync(operation.file)) : null;
            if (current !== operation.before) throw new Error(`Browser output changed during preparation: ${operation.file}`);
        };
        for (const operation of changes) unchanged(operation);
        for (const operation of changes) {
            unchanged(operation);
            if (operation.content === null) fs.unlinkSync(operation.file);
            else { fs.mkdirSync(path.dirname(operation.file), { recursive: true }); fs.writeFileSync(operation.file, operation.content); }
        }
        safeAssetPath(root, inventoryName);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        if (!fs.existsSync(file) || fs.readFileSync(file, 'utf8') !== inventory) fs.writeFileSync(file, inventory);
    }
    return { obsolete: obsolete.map(owner => owner.path), owners };
}

function isTemporaryBrowserModule(file) {
    return /\.__test-[^/\\]+\.mjs$/.test(file);
}

function browserFiles(dir) {
    if (!fs.existsSync(dir)) return [];
    if (fs.lstatSync(dir).isSymbolicLink()) throw new Error(`Browser source symlink is unsupported: ${dir}`);
    return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
        const file = path.join(dir, entry.name);
        if (isTemporaryBrowserModule(file)) return [];
        if (entry.isSymbolicLink()) throw new Error(`Browser source symlink is unsupported: ${file}`);
        return entry.isDirectory() ? browserFiles(file) : [file];
    });
}

function browserSourceFiles(root) {
    // Portal markup, styles, and images are retained build inputs alongside its module sources.
    return [surfaces[0][0], surfaces[0][1], surfaces[1][0],
        'src/ETL-SQL.Portal/wwwroot', 'types'].flatMap(relative => browserFiles(safeAssetPath(root, relative)));
}

function browserPreparationFiles(root) {
    const fixed = ['scripts/compile-browser.mjs', 'scripts/sync-assets.js',
        'scripts/generate-portal-browser-contract.mjs', 'scripts/generate-browser-contracts.mjs', 'tsconfig.browser-build.json',
        'scripts/typecheck/package.json', 'scripts/typecheck/package-lock.json', 'scripts/typecheck/.npmrc',
        'Directory.Build.props', 'Directory.Build.targets',
        ...['Reporting', 'ReportRuntime', 'ReportPlayer', 'WorkstationEditor', 'Portal']
            .map(project => `src/ETL-SQL.${project}/ETL-SQL.${project}.csproj`),
        'src/etl-sql-vscode/.vscodeignore', 'src/etl-sql-vscode/package.json',
        'src/etl-sql-vscode/ui/package.json', 'src/etl-sql-vscode/ui/vite.config.ts'];
    const inputs = fixed.map(relative => safeAssetPath(root, relative)).filter(file => fs.existsSync(file));
    // Generator helpers belong here so adding/removing a transitive helper cannot escape freshness
    // checks. Do not infer input ownership from a comment or generated-looking banner.
    inputs.push(...browserFiles(safeAssetPath(root, 'scripts/lib')).filter(file => /\.(?:cjs|mjs|js|json)$/.test(file)));
    inputs.push(...browserFiles(safeAssetPath(root, 'src/ETL-SQL.Portal/BrowserContracts')).filter(file => file.endsWith('.json')));
    const tests = safeAssetPath(root, 'tests/ETL-SQL.Portal.Tests');
    if (fs.existsSync(tests)) inputs.push(...fs.readdirSync(tests)
        .filter(name => /BrowserContractsGenerator(?:Tests)?\.cs$/.test(name))
        .map(name => safeAssetPath(root, `tests/ETL-SQL.Portal.Tests/${name}`)));
    return inputs;
}

// Outputs change during preparation. Only authored inputs belong in its consistency check.
function browserInputSignature(root, plannedOutputs = []) {
    for (const [source, output] of surfaces) {
        safeAssetPath(root, source);
        safeAssetPath(root, output);
    }
    const outputs = new Set(surfaces.flatMap(([source, output]) => browserFiles(path.join(root, source))
        .filter(file => file.endsWith('.ts') && !file.endsWith('.d.ts'))
        .map(file => path.resolve(root, output, path.relative(path.join(root, source), file).replace(/\.ts$/, '.js')))));
    outputs.add(path.join(root, bundle));
    // Portal receives every Shared asset at a deterministic URL. Include future compiler emits
    // before they exist so first preparation has the same input set as subsequent preparation.
    const sharedFiles = browserFiles(path.join(root, shared));
    const sharedEmits = browserFiles(path.join(root, surfaces[0][0]))
        .filter(file => file.endsWith('.ts') && !file.endsWith('.d.ts'))
        .map(file => path.resolve(root, surfaces[0][1], path.relative(path.join(root, surfaces[0][0]), file).replace(/\.ts$/, '.js')));
    for (const file of [...sharedFiles, ...sharedEmits, path.join(root, bundle)])
        outputs.add(path.join(root, portalTarget(path.relative(path.join(root, shared), file).replaceAll('\\', '/'))));
    for (const file of plannedOutputs) outputs.add(path.resolve(root, file));
    const inventory = readOwnership(root);
    if (inventory.version === 2) for (const owner of inventory.outputs) outputs.add(path.resolve(root, owner.path));
    else for (const owner of inventory.outputs) {
        // During the version-1 bootstrap, retain paths whose owner still exists. A deleted owner
        // without a stored output hash cannot authorize removal or hide an authored input.
        if (fs.existsSync(path.join(root, owner.source))) outputs.add(path.resolve(root, owner.path));
    }
    const inputs = [...browserSourceFiles(root), ...browserPreparationFiles(root)];
    const signature = createHash('sha256');
    for (const file of [...new Set(inputs)].sort()) {
        if (outputs.has(path.resolve(file))) continue;
        const content = fs.readFileSync(file);
        signature.update(path.relative(root, file).replaceAll('\\', '/')).update('\0').update(content).update('\0');
    }
    return signature.digest('hex');
}

module.exports = { browserFiles, browserInputSignature, isTemporaryBrowserModule,
    browserSourceFiles, browserPreparationFiles, readOwnership, prepareOutputs, safeAssetPath, portalTarget };
