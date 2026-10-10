import { createHash, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { browserInputSignature, readOwnership, safeAssetPath } from './lib/browser-asset-inputs.cjs';
import { loadVsixToolchain } from './lib/vsix-toolchain.cjs';
import { createUnsignedVsix, inspectVsix, resolveVsixPlatform } from './lib/vsix-package.mjs';
import { runOwnedCommand as runPackagingCommand } from './lib/run-owned-command.mjs';
export { runPackagingCommand };

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const tools = Object.freeze([
    ['ETL-SQL', 'ETL-SQL.App'], ['ETL-SQL-LSP', 'ETL-SQL.LanguageServer'],
    ['ETL-SQL-Report', 'ETL-SQL.ReportBuilder.CLI'], ['ETL-SQL-Player', 'ETL-SQL.ReportPlayer'],
]);
const excluded = new Set(['node_modules', 'obj', 'bin', 'out', 'dist', '.git', '.vscode-test', 'coverage', 'logs']);
const posix = value => value.replaceAll('\\', '/');

function files(directory, skip = new Set()) {
    if (!fs.existsSync(directory)) throw new Error(`Required packaging input is missing: ${directory}.`);
    if (fs.lstatSync(directory).isSymbolicLink()) throw new Error(`Packaging inputs must not be links: ${directory}.`);
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        if (skip.has(entry.name)) return [];
        const file = path.join(directory, entry.name);
        if (entry.isSymbolicLink()) throw new Error(`Packaging inputs must not be links: ${file}.`);
        if (entry.isDirectory()) return files(file, skip);
        if (!entry.isFile()) throw new Error(`Unsupported packaging input: ${file}.`);
        return [file];
    });
}

export function packagingNpmCli(executable = process.execPath) {
    const directory = path.dirname(executable);
    const candidates = [path.join(directory, 'node_modules/npm/bin/npm-cli.js'),
        path.resolve(directory, '../lib/node_modules/npm/bin/npm-cli.js')];
    const cli = candidates.find(candidate => fs.existsSync(candidate));
    if (!cli) throw new Error('Use the Node.js 22 distribution with its pinned npm 10 installation.');
    return cli;
}

export function packagingIdentity(root, requested) {
    const extension = path.join(root, 'src/etl-sql-vscode');
    const manifest = JSON.parse(fs.readFileSync(path.join(extension, 'package.json'), 'utf8'));
    const lock = JSON.parse(fs.readFileSync(path.join(extension, 'package-lock.json'), 'utf8'));
    const properties = fs.readFileSync(path.join(root, 'Directory.Build.props'), 'utf8');
    const versions = [...properties.matchAll(/<VersionPrefix>([^<]+)<\/VersionPrefix>/g)];
    const version = versions[0]?.[1];
    if (versions.length !== 1 || !/^\d+\.\d+\.\d+(?:-[\da-zA-Z.-]+)?$/.test(version ?? '') ||
        /<VersionSuffix>[^<]+<\/VersionSuffix>/.test(properties)) throw new Error('A single explicit release version is required in Directory.Build.props.');
    if (manifest.version !== version || lock.version !== version || lock.packages?.['']?.version !== version ||
        (requested !== undefined && requested !== version)) throw new Error(`Extension, lockfile, requested version, and .NET version must agree (${version}).`);
    if (!/^[a-z0-9][a-z0-9-]*$/.test(manifest.name ?? '') || !/^[a-z0-9][a-z0-9-]*$/.test(manifest.publisher ?? ''))
        throw new Error('Extension name and publisher must be explicit package identities.');
    return { name: manifest.name, publisher: manifest.publisher, version };
}

// Generated files are excluded using the canonical browser ownership rules. New/deleted authored
// files still change this signature, including the .NET sources consumed by every bundled tool.
export function packagingInputSignature(root) {
    const selected = new Set();
    for (const directory of ['src', 'tools/package-inspector']) {
        for (const file of files(path.join(root, directory), new Set([...excluded, 'wwwroot', 'Shared', 'BrowserSources', 'TypeScript', 'media'])))
            if (/\.(?:cs|csproj|props|targets|resx|json)$/.test(file)) selected.add(file);
    }
    const extension = path.join(root, 'src/etl-sql-vscode');
    for (const directory of ['src', 'ui', 'resources', 'syntaxes', 'snippets'])
        for (const file of files(path.join(extension, directory), excluded)) selected.add(file);
    for (const directory of ['scripts/vsix', 'build/signing', 'deploy', 'capacity-results/workloads'])
        for (const file of files(path.join(root, directory), excluded)) selected.add(file);
    for (const relative of ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
        'global.json', 'NuGet.Config', 'LICENSE.md', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-INVENTORY.md',
        'src/etl-sql-vscode/package.json', 'src/etl-sql-vscode/package-lock.json', 'src/etl-sql-vscode/.vscodeignore',
        'src/etl-sql-vscode/README.md', 'src/etl-sql-vscode/language-configuration.json', 'src/etl-sql-vscode/tsconfig.json',
        'scripts/package-vsix.mjs', 'certification-results/ha-large-job-soak-scenarios.json']) {
        const file = safeAssetPath(root, relative);
        if (fs.existsSync(file)) selected.add(file);
    }
    const hash = createHash('sha256').update(browserInputSignature(root)).update('\0');
    for (const file of [...selected].sort()) hash.update(posix(path.relative(root, file))).update('\0').update(fs.readFileSync(file)).update('\0');
    return hash.digest('hex');
}

function workspacePath(root, candidate) {
    const absolute = path.resolve(candidate);
    const relative = posix(path.relative(root, absolute));
    if (!relative || relative === '..' || relative.startsWith('../') || path.isAbsolute(relative))
        throw new Error('VSIX output and temporary directories must stay inside the repository workspace.');
    return safeAssetPath(root, relative);
}

function assertNoLinks(candidate) {
    const absolute = path.resolve(candidate);
    let current = path.parse(absolute).root;
    for (const segment of path.relative(current, absolute).split(path.sep)) {
        current = path.join(current, segment);
        if (fs.lstatSync(current).isSymbolicLink()) throw new Error(`Packaging paths must not contain links: ${current}.`);
    }
}

function copyTree(source, destination, accept = () => true) {
    for (const file of files(source)) {
        const relative = posix(path.relative(source, file));
        if (!accept(relative)) continue;
        const output = path.join(destination, relative);
        fs.mkdirSync(path.dirname(output), { recursive: true });
        fs.copyFileSync(file, output);
    }
}

async function hashFile(file, signal) {
    const hash = createHash('sha256');
    for await (const bytes of fs.createReadStream(file, { signal })) hash.update(bytes);
    return hash.digest('hex');
}

// Injectable command/assembly functions let regression fixtures fail real workflow boundaries.
// Public wrappers expose no switch that skips restoration, inspection, or archive validation.
export async function packageVsix(options, { run: invoke = runPackagingCommand, assemble = createUnsignedVsix } = {}) {
    const signal = options.signal;
    signal?.throwIfAborted();
    const run = async (command, args, detail) => {
        signal?.throwIfAborted();
        const result = await invoke(command, args, { ...detail, signal });
        signal?.throwIfAborted();
        return result;
    };
    const hash = file => hashFile(file, signal);
    const root = path.resolve(options.root ?? repository);
    assertNoLinks(root);
    const { platform, target } = resolveVsixPlatform(options.platform);
    const identity = packagingIdentity(root, options.version ?? process.env.ETL_SQL_VERSION);
    if (process.versions.node.split('.')[0] !== '22') throw new Error('VSIX packaging requires Node.js 22.');
    if (!['build', 'publish'].includes(options.mode)) throw new Error('Choose build or publish mode.');
    if (options.mode === 'publish' && !options.binDirectory) throw new Error('Publish mode requires --bin-dir.');
    if (options.mode === 'publish') {
        assertNoLinks(options.binDirectory);
        if (!fs.statSync(options.binDirectory).isDirectory()) throw new Error('Published tool source must be a directory.');
    }
    const npmCli = packagingNpmCli();
    const npm = (args, cwd, label, capture = false) => run(process.execPath, [npmCli, ...args], { cwd, label, capture });
    const npmVersion = (await npm(['--version'], root, 'npm version', true)).stdout.trim();
    if (!/^10\./.test(npmVersion)) throw new Error(`VSIX packaging requires npm 10; found ${npmVersion}.`);
    const outputDirectory = workspacePath(root, options.outputDirectory ?? path.join(root, 'release/vsix'));
    const lockFile = workspacePath(root, path.join(root, 'artifacts/vsix-package.lock'));
    fs.mkdirSync(path.dirname(lockFile), { recursive: true });
    const token = `${process.pid}:${randomUUID()}`;
    let lock;
    try { lock = fs.openSync(lockFile, 'wx'); } catch (error) {
        if (error.code === 'EEXIST') throw new Error(`VSIX packaging is already locked: ${lockFile}. Resolve an abandoned lock before retrying.`);
        throw error;
    }
    let temporary;
    try {
        fs.writeFileSync(lock, token);
        fs.mkdirSync(outputDirectory, { recursive: true });
        temporary = fs.mkdtempSync(path.join(outputDirectory, '.vsix-stage-'));
        const inputSignature = packagingInputSignature(root);
        const assertInputs = () => {
            if (packagingInputSignature(root) !== inputSignature) throw new Error('Packaging inputs changed during the build. Retry from a stable candidate.');
        };
        for (const relative of ['scripts/typecheck', 'scripts/vsix', 'src/etl-sql-vscode', 'src/etl-sql-vscode/ui'])
            await npm(['ci', '--no-audit', '--no-fund'], path.join(root, relative), `restore ${relative}`);
        loadVsixToolchain(root);
        await run(process.execPath, [path.join(root, 'scripts/sync-assets.js')], { cwd: root, label: 'prepare browser assets' });
        await run(process.execPath, [path.join(root, 'scripts/sync-assets.js'), '-Check'], { cwd: root, label: 'verify browser assets' });
        const extension = path.join(root, 'src/etl-sql-vscode');
        const compiled = path.join(temporary, 'compiled');
        const ui = path.join(temporary, 'ui');
        await npm(['run', 'build', '--', '--outDir', ui], path.join(extension, 'ui'), 'build UI');
        await npm(['run', 'compile', '--', '--outDir', compiled], extension, 'compile extension');
        assertInputs();
        const preparedOutputs = new Map(readOwnership(root).outputs.map(owner => [owner.path, owner.sha256]));
        const deliveredInputs = new Map();
        const inspectorArtifacts = path.join(temporary, 'inspector-dotnet');
        await run('dotnet', ['build', path.join(root, 'tools/package-inspector/ETL-SQL.PackageInspector.csproj'), '-c', 'Release',
            '--artifacts-path', inspectorArtifacts, '--disable-build-servers', '--nologo'], { cwd: root, label: 'build package inspector' });
        const inspector = path.join(inspectorArtifacts, 'bin/ETL-SQL.PackageInspector/release/ETL-SQL-PackageInspector.dll');
        const published = new Map();
        for (const [name, project] of tools) {
            const directory = options.mode === 'publish' ? path.resolve(options.binDirectory) : path.join(temporary, `publish-${name}`);
            if (options.mode === 'build') await run('dotnet', ['publish', path.join(root, `src/${project}/${project}.csproj`),
                '-c', 'Release', '-r', platform, '--self-contained', 'true', '-p:PublishSingleFile=true',
                '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishReadyToRun=true', '--artifacts-path',
                path.join(temporary, 'publish-dotnet'), '-o', directory, '--disable-build-servers', '--nologo'], { cwd: root, label: `publish ${name}` });
            published.set(name, directory);
        }
        const assembly = path.join(temporary, 'extension');
        fs.mkdirSync(assembly);
        for (const relative of ['.vscodeignore', 'README.md', 'language-configuration.json'])
            fs.copyFileSync(path.join(extension, relative), path.join(assembly, relative));
        for (const relative of ['LICENSE.md', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-INVENTORY.md'])
            fs.copyFileSync(path.join(root, relative), path.join(assembly, relative));
        const manifest = JSON.parse(fs.readFileSync(path.join(extension, 'package.json'), 'utf8'));
        delete manifest.scripts['vscode:prepublish'];
        fs.writeFileSync(path.join(assembly, 'package.json'), JSON.stringify(manifest, null, 2) + '\n');
        for (const relative of ['resources', 'syntaxes', 'snippets', 'media']) copyTree(path.join(extension, relative), path.join(assembly, relative));
        for (const [relative, expected] of preparedOutputs) if (relative.startsWith('src/etl-sql-vscode/media/')) {
            const packaged = path.join(assembly, relative.slice('src/etl-sql-vscode/'.length));
            if (await hash(packaged) !== expected) throw new Error(`Prepared extension asset changed before assembly: ${relative}.`);
        }
        copyTree(compiled, path.join(assembly, 'out'), name => !name.startsWith('test/') && !name.endsWith('.map'));
        copyTree(ui, path.join(assembly, 'ui/dist'));
        const closure = (await npm(['ls', '--omit=dev', '--all', '--parseable'], extension, 'runtime dependency closure', true)).stdout.trim().split(/\r?\n/);
        for (const dependency of closure) {
            if (path.resolve(dependency) === extension) continue;
            const relative = posix(path.relative(extension, dependency));
            if (!relative.startsWith('node_modules/') || relative.includes('../')) throw new Error('Runtime dependency resolved outside the extension.');
            copyTree(safeAssetPath(root, `src/etl-sql-vscode/${relative}`), path.join(assembly, relative));
        }
        const binaries = [];
        const inspections = [];
        fs.mkdirSync(path.join(assembly, 'bin'));
        for (const [name] of tools) {
            const basename = name + (platform === 'win-x64' ? '.exe' : '');
            const source = path.join(published.get(name), basename);
            if (fs.lstatSync(source).isSymbolicLink()) throw new Error(`Bundled binaries must not be links: ${source}.`);
            const sourceHash = await hash(source);
            const binary = path.join(assembly, 'bin', basename);
            fs.copyFileSync(source, binary);
            const result = await run('dotnet', [inspector, binary, name, identity.version, platform], { cwd: root, label: `inspect ${name}`, capture: true });
            const inspection = JSON.parse(result.stdout);
            const sqlite = ({ 'win-x64': 'e_sqlite3.dll', 'linux-x64': 'libe_sqlite3.so', 'osx-x64': 'libe_sqlite3.dylib', 'osx-arm64': 'libe_sqlite3.dylib' })[platform];
            if (!inspection.nativeLibraries.includes(sqlite) || inspection.sha256 !== sourceHash || inspection.sha256 !== await hash(binary))
                throw new Error(`Required SQLite binding or inspected binary hash is missing: ${name}.`);
            deliveredInputs.set(source, sourceHash);
            binaries.push(`bin/${basename}`);
            inspections.push(inspection);
        }
        // Player opens browser assets beside its executable. Verify the published bytes against
        // prepared source before copying; a four-executable-only package cannot host reports.
        const playerAssets = path.join(root, 'src/ETL-SQL.ReportPlayer/wwwroot');
        const publishedAssets = path.join(published.get('ETL-SQL-Player'), 'wwwroot');
        for (const source of files(playerAssets)) {
            const relative = posix(path.relative(playerAssets, source));
            const delivered = path.join(publishedAssets, relative);
            const expected = await hash(source);
            if (fs.lstatSync(delivered).isSymbolicLink() || expected !== await hash(delivered))
                throw new Error(`Missing or stale published Player asset: ${relative}.`);
            const destination = path.join(assembly, 'bin/wwwroot', relative);
            fs.mkdirSync(path.dirname(destination), { recursive: true });
            fs.copyFileSync(delivered, destination);
            if (await hash(destination) !== expected) throw new Error(`Player asset changed while copying: ${relative}.`);
            deliveredInputs.set(delivered, expected);
        }
        fs.copyFileSync(path.join(root, 'src/appsettings.json'), path.join(assembly, 'bin/appsettings.json'));
        fs.copyFileSync(path.join(root, 'THIRD-PARTY-NOTICES.md'), path.join(assembly, 'bin/THIRD-PARTY-NOTICES.md'));
        fs.writeFileSync(path.join(assembly, 'packaging-receipt.json'), JSON.stringify({ schemaVersion: 1, identity, platform, target,
            inputSignature, node: process.versions.node, npm: npmVersion, binaries: inspections }, null, 2) + '\n');
        const selected = (await run(process.execPath, [loadVsixToolchain(root).cli, 'ls'],
            { cwd: assembly, label: 'VSIX file selection', capture: true })).stdout.trim().split(/\r?\n/);
        const selectedSet = new Set(selected.map(posix));
        const expectedFiles = {};
        for (const file of files(assembly)) {
            const relative = posix(path.relative(assembly, file));
            const required = !relative.startsWith('node_modules/') && !['README.md', '.vscodeignore'].includes(relative);
            if (required && !selectedSet.has(relative)) throw new Error(`Required payload excluded from VSIX: ${relative}.`);
            if (selectedSet.has(relative) && relative !== 'README.md') expectedFiles[relative] = await hash(file);
        }
        assertInputs();
        const filename = `${identity.name}-${identity.version}-${target}.vsix`;
        const temporaryVsix = path.join(temporary, filename);
        await assemble(root, assembly, temporaryVsix, target, { executables: binaries, signal });
        await inspectVsix(root, temporaryVsix, { identity, target, expectedFiles, executables: binaries, signal });
        await run(process.execPath, [path.join(root, 'scripts/sync-assets.js'), '-Check'],
            { cwd: root, label: 'verify browser assets before promotion' });
        assertInputs();
        for (const [file, expected] of deliveredInputs) if (await hash(file) !== expected)
            throw new Error(`Published input changed during packaging: ${path.basename(file)}.`);
        const destination = workspacePath(root, path.join(outputDirectory, filename));
        if (fs.existsSync(destination) && !fs.lstatSync(destination).isFile()) throw new Error('Accepted VSIX path must be a regular file.');
        // Both files are on the same filesystem. A failed stage never deletes/replaces the accepted package.
        signal?.throwIfAborted();
        fs.renameSync(temporaryVsix, destination);
        return destination;
    } finally {
        try {
            if (temporary) fs.rmSync(workspacePath(root, temporary), { recursive: true, force: true });
        } finally {
            fs.closeSync(lock);
            if (fs.existsSync(lockFile) && fs.lstatSync(lockFile).isFile() && fs.readFileSync(lockFile, 'utf8') === token)
                fs.unlinkSync(lockFile);
        }
    }
}

export function parsePackagingArguments(args) {
    const [mode, ...rest] = args;
    const options = { mode };
    const names = { '--platform': 'platform', '-p': 'platform', '--bin-dir': 'binDirectory', '-b': 'binDirectory',
        '--version': 'version', '--out-dir': 'outputDirectory' };
    for (let index = 0; index < rest.length; index += 2) {
        const key = names[rest[index]];
        if (!key || !rest[index + 1] || rest[index + 1].startsWith('--') || options[key] !== undefined)
            throw new Error(`Unknown, duplicate, or incomplete VSIX option: ${rest[index]}.`);
        options[key] = rest[index + 1];
    }
    return options;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const controller = new AbortController();
    let cancellationCode = 0;
    const interrupt = signal => {
        cancellationCode = signal === 'SIGTERM' ? 143 : 130;
        controller.abort(new Error(`VSIX packaging cancelled (${signal}).`));
    };
    const onInterrupt = () => interrupt('SIGINT');
    const onTerminate = () => interrupt('SIGTERM');
    process.on('SIGINT', onInterrupt);
    process.on('SIGTERM', onTerminate);
    try { process.stdout.write(await packageVsix({ ...parsePackagingArguments(process.argv.slice(2)), signal: controller.signal }) + '\n'); }
    catch (error) { process.stderr.write(`[VSIX] ${error.message}\n`); process.exitCode = cancellationCode || 1; }
    finally { process.removeListener('SIGINT', onInterrupt); process.removeListener('SIGTERM', onTerminate); }
}
