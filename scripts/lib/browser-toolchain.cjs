const fs = require('node:fs');
const path = require('node:path');

// Preparation uses only this checkout's restored compiler. Never resolve a global package or
// invoke npm here: dependency restore is a separate, explicit step.
function loadBrowserCompiler(toolchain) {
    const directory = path.resolve(toolchain, 'scripts/typecheck');
    const restore = 'Use Node.js 22 and npm 10, then run npm ci --prefix scripts/typecheck.';
    const fail = message => { throw new Error(`Browser toolchain: ${message} ${restore}`); };
    const read = relative => {
        try { return JSON.parse(fs.readFileSync(path.join(directory, relative), 'utf8')); }
        catch { fail(`Missing or invalid ${relative}.`); }
    };
    const manifest = read('package.json');
    if (manifest.engines?.node !== '22.x' || manifest.engines?.npm !== '10.x')
        fail('The supported Node.js/npm versions are missing or inconsistent.');
    if (process.versions.node.split('.')[0] !== '22' && !process.env.ETLSQL_ALLOW_NODE_VERSION)
        fail(`Unsupported Node.js ${process.versions.node}; preparation requires Node.js 22.x.`);
    const version = manifest.devDependencies?.typescript;
    if (typeof version !== 'string' || !/^\d+\.\d+\.\d+$/.test(version))
        fail('TypeScript must have an exact version in package.json.');
    const lock = read('package-lock.json');
    const locked = lock.packages?.['node_modules/typescript'];
    if (lock.lockfileVersion !== 3 || lock.packages?.['']?.devDependencies?.typescript !== version ||
        locked?.version !== version || typeof locked.integrity !== 'string' ||
        lock.packages?.['']?.engines?.node !== manifest.engines.node ||
        lock.packages?.['']?.engines?.npm !== manifest.engines.npm)
        fail('package.json and package-lock.json disagree.');
    const installedLock = read('node_modules/.package-lock.json').packages?.['node_modules/typescript'];
    const installed = read('node_modules/typescript/package.json');
    if (installed.version !== version || installedLock?.version !== version ||
        installedLock.integrity !== locked.integrity || installedLock.resolved !== locked.resolved)
        fail(`The restored TypeScript compiler does not match the locked ${version} dependency.`);
    let compiler;
    try { compiler = require(path.join(directory, 'node_modules/typescript')); }
    catch { fail('The local TypeScript compiler cannot be loaded.'); }
    if (compiler.version !== version || typeof compiler.createProgram !== 'function' ||
        typeof compiler.readConfigFile !== 'function')
        fail(`The loaded TypeScript compiler does not match ${version}.`);
    return compiler;
}

module.exports = { loadBrowserCompiler };
