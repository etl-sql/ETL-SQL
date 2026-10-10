const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const { isDeepStrictEqual } = require('node:util');

// Packaging never downloads an executable. Restore this lock separately with npm ci.
function loadVsixToolchain(root) {
    const directory = path.resolve(root, 'scripts/vsix');
    const restore = 'Use Node.js 22 and npm 10, then run npm ci --prefix scripts/vsix.';
    const fail = message => { throw new Error(`VSIX toolchain: ${message} ${restore}`); };
    const read = relative => {
        try { return JSON.parse(fs.readFileSync(path.join(directory, relative), 'utf8')); }
        catch { fail(`Missing or invalid ${relative}.`); }
    };
    if (process.versions.node.split('.')[0] !== '22') fail(`Unsupported Node.js ${process.versions.node}.`);
    const manifest = read('package.json');
    const lock = read('package-lock.json');
    if (manifest.engines?.node !== '22.x' || manifest.engines?.npm !== '10.x' ||
        lock.lockfileVersion !== 3 || !isDeepStrictEqual(lock.packages?.['']?.engines, manifest.engines) ||
        !isDeepStrictEqual(lock.packages?.['']?.devDependencies, manifest.devDependencies))
        fail('package.json and package-lock.json disagree.');
    const replacement = 'file:./unsigned-signing';
    if (manifest.devDependencies?.['@vscode/vsce-sign'] !== replacement ||
        manifest.overrides?.['@vscode/vsce-sign'] !== '$@vscode/vsce-sign' ||
        lock.packages?.['node_modules/@vscode/vsce-sign']?.resolved !== 'unsigned-signing' ||
        lock.packages?.['node_modules/@vscode/vsce-sign']?.link !== true)
        fail('The repository-owned unsigned signing replacement is missing.');
    const signer = read('unsigned-signing/package.json');
    const lockedSigner = lock.packages?.['unsigned-signing'];
    if (signer.name !== '@vscode/vsce-sign' || signer.private !== true || signer.license !== 'Apache-2.0' ||
        lockedSigner?.name !== signer.name || lockedSigner.version !== signer.version ||
        lockedSigner.license !== signer.license)
        fail('The unsigned signing replacement disagrees with its lock.');
    const installedLock = read('node_modules/.package-lock.json');
    for (const relative of Object.keys(installedLock.packages ?? {}))
        if (!Object.hasOwn(lock.packages, relative)) fail(`Unexpected restored dependency ${relative}.`);
    const licenses = new Set(['MIT', 'Apache-2.0', 'Artistic-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', 'BlueOak-1.0.0', '0BSD']);
    for (const [relative, dependency] of Object.entries(lock.packages)) {
        if (!relative.startsWith('node_modules/')) continue;
        if (relative === 'node_modules/@vscode/vsce-sign') continue;
        if (dependency.link || !licenses.has(dependency.license) ||
            typeof dependency.integrity !== 'string' || !dependency.resolved?.startsWith('https://registry.npmjs.org/'))
            fail(`Unapproved or unpinned dependency ${relative}.`);
        const installed = installedLock.packages?.[relative];
        if (!installed && dependency.optional) continue; // Native keyring packages are platform-specific.
        if (installed?.version !== dependency.version || installed.integrity !== dependency.integrity ||
            installed.resolved !== dependency.resolved || read(`${relative}/package.json`).version !== dependency.version)
            fail(`The restored ${relative} does not match its lock.`);
    }
    for (const name of ['@vscode/vsce', 'yauzl', 'xml2js']) {
        const version = manifest.devDependencies?.[name];
        if (!/^\d+\.\d+\.\d+$/.test(version ?? '') || lock.packages?.[`node_modules/${name}`]?.version !== version)
            fail(`${name} needs an exact, consistent version.`);
    }
    const requireLocal = createRequire(path.join(directory, 'package.json'));
    const requireVsce = createRequire(path.join(directory, 'node_modules/@vscode/vsce/package.json'));
    if (fs.realpathSync(requireVsce.resolve('@vscode/vsce-sign')) !==
        fs.realpathSync(path.join(directory, 'unsigned-signing/index.cjs')))
        fail('The packager resolved a different signing module.');
    return {
        cli: path.join(directory, 'node_modules/@vscode/vsce/vsce'),
        yauzl: requireLocal('yauzl'),
        parseXml: requireLocal('xml2js').parseStringPromise,
    };
}

module.exports = { loadVsixToolchain };
