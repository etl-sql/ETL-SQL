import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { packageVsix, packagingIdentity, packagingInputSignature, packagingNpmCli, parsePackagingArguments } from './package-vsix.mjs';
import { createUnsignedVsix, inspectVsix, resolveVsixPlatform } from './lib/vsix-package.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = fs.mkdtempSync(path.join(repository, 'artifacts', 'vsix-workflow espaces-é-$-'));
const root = path.join(temporary, 'candidate');
const host = resolveVsixPlatform();
const identity = { name: 'etlsql-workflow-check', publisher: 'etl-sql', version: '0.21.0' };
const log = [];
const write = (relative, bytes) => {
    const file = path.join(root, relative);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, bytes);
    return file;
};
const clone = (source, destination, accept = () => true) => {
    if (!accept(source)) return;
    if (fs.statSync(source).isDirectory()) {
        fs.mkdirSync(destination, { recursive: true });
        for (const name of fs.readdirSync(source)) clone(path.join(source, name), path.join(destination, name), accept);
    } else {
        fs.mkdirSync(path.dirname(destination), { recursive: true });
        fs.copyFileSync(source, destination);
    }
};
const execute = (command, args, cwd = root) => {
    process.stderr.write(`[workflow fixture] ${path.basename(command)} ${args.slice(0, 2).join(' ')}\n`);
    const result = spawnSync(command, args, { cwd, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
    log.push({ command, args, status: result.status, stdout: result.stdout, stderr: result.stderr });
    assert.equal(result.error, undefined, result.error?.message);
    assert.equal(result.status, 0, `${args.join(' ')}\n${result.stdout}\n${result.stderr}`);
    return { stdout: result.stdout };
};
try {
    assert.deepEqual(parsePackagingArguments(['publish', '-p', host.platform, '-b', 'binaries with spaces']),
        { mode: 'publish', platform: host.platform, binDirectory: 'binaries with spaces' });
    for (const args of [['build', '--platform'], ['build', '--sign', 'key'], ['build', '-p', host.platform, '-p', host.platform]])
        assert.throws(() => parsePackagingArguments(args), /Unknown, duplicate, or incomplete/);
    for (const directory of ['src/etl-sql-vscode/src', 'src/etl-sql-vscode/resources', 'src/etl-sql-vscode/syntaxes',
        'src/etl-sql-vscode/snippets', 'src/etl-sql-vscode/ui', 'build/signing', 'deploy', 'capacity-results/workloads']) fs.mkdirSync(path.join(root, directory), { recursive: true });
    write('Directory.Build.props', `<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><VersionPrefix>${identity.version}</VersionPrefix><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>\n`);
    write('Directory.Build.targets', '<Project />\n');
    write('Directory.Packages.props', fs.readFileSync(path.join(repository, 'Directory.Packages.props')));
    const manifest = { ...identity, displayName: 'Workflow regression', description: 'ETL-SQL packaging workflow fixture',
        license: 'Apache-2.0', repository: { type: 'git', url: 'https://github.com/etl-sql/ETL-SQL' },
        engines: { vscode: '^1.91.0' }, activationEvents: ['onStartupFinished'], main: './out/extension.js',
        scripts: { 'vscode:prepublish': 'npm run compile' }, dependencies: {} };
    const manifestFile = write('src/etl-sql-vscode/package.json', JSON.stringify(manifest));
    write('src/etl-sql-vscode/package-lock.json', JSON.stringify({ version: identity.version, packages: { '': { version: identity.version } } }));
    write('src/etl-sql-vscode/.vscodeignore', 'out/test/**\n**/*.map\nnode_modules/**/test/**\n');
    write('src/etl-sql-vscode/README.md', '# Packaging workflow regression\n');
    write('src/etl-sql-vscode/language-configuration.json', '{}\n');
    const uiSource = write('src/etl-sql-vscode/ui/source.txt', 'new UI source\n');
    write('src/etl-sql-vscode/ui/dist/index.html', 'stale UI output\n');
    write('src/etl-sql-vscode/out/removed-module.js', 'stale extension output\n');
    write('src/etl-sql-vscode/out/test/dev.js', 'development test\n');
    write('src/etl-sql-vscode/bin/developer-data.txt', 'preserve developer data\n');
    write('src/etl-sql-vscode/media/report-runtime.js', 'export const prepared = true;\n');
    write('src/ETL-SQL.ReportPlayer/wwwroot/runtime.js', 'export const prepared = true;\n');
    write('src/appsettings.json', '{}\n');
    for (const file of ['LICENSE.md', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-INVENTORY.md'])
        write(file, fs.readFileSync(path.join(repository, file)));
    clone(path.join(repository, 'scripts/vsix'), path.join(root, 'scripts/vsix'), file => !file.split(path.sep).includes('node_modules'));
    execute(process.execPath, [packagingNpmCli(), 'ci', '--offline', '--no-audit', '--no-fund'], path.join(root, 'scripts/vsix'));
    clone(path.join(repository, 'tools/package-inspector'), path.join(root, 'tools/package-inspector'), file => !['bin', 'obj'].some(segment => file.split(path.sep).includes(segment)));
    const inspectorArtifacts = path.join(temporary, 'inspector-artifacts');
    execute('dotnet', ['build', path.join(root, 'tools/package-inspector/ETL-SQL.PackageInspector.csproj'), '-c', 'Release', '--artifacts-path', inspectorArtifacts, '--nologo']);
    const names = ['ETL-SQL', 'ETL-SQL-LSP', 'ETL-SQL-Report', 'ETL-SQL-Player'];
    const binaries = path.join(temporary, 'published');
    fs.mkdirSync(binaries);
    const fixture = write('fixture/Program.cs', 'return 0;\n');
    const project = write('fixture/Fixture.csproj', `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>
<PublishSingleFile>true</PublishSingleFile><SelfContained>true</SelfContained><IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<PublishReadyToRun>false</PublishReadyToRun><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
</PropertyGroup><ItemGroup><PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" /></ItemGroup></Project>\n`);
    for (const name of names) {
        const output = path.join(temporary, `sdk-${name}`);
        execute('dotnet', ['publish', project, '-c', 'Release', '-r', host.platform,
            `-p:AssemblyName=${name}`, '--artifacts-path', path.join(temporary, 'fixture-artifacts'), '-o', output, '--nologo']);
        const basename = name + (host.platform === 'win-x64' ? '.exe' : '');
        fs.copyFileSync(path.join(output, basename), path.join(binaries, basename));
    }
    fs.mkdirSync(path.join(binaries, 'wwwroot'));
    fs.copyFileSync(path.join(root, 'src/ETL-SQL.ReportPlayer/wwwroot/runtime.js'), path.join(binaries, 'wwwroot/runtime.js'));
    const extensionBefore = fs.readFileSync(manifestFile);
    const signature = packagingInputSignature(root);
    write('src/NewAuthoredSource.cs', 'namespace Regression;\n');
    assert.notEqual(packagingInputSignature(root), signature, 'Adding a .NET source must invalidate the candidate.');
    fs.unlinkSync(path.join(root, 'src/NewAuthoredSource.cs'));
    assert.equal(packagingInputSignature(root), signature);
    assert.throws(() => packagingIdentity(root, '0.20.0'), /must agree/);
    const outputDirectory = path.join(root, 'accepted');
    fs.mkdirSync(outputDirectory);
    const destination = path.join(outputDirectory, `${identity.name}-${identity.version}-${host.target}.vsix`);
    const labels = [];
    let failAt;
    let changeInputs = false;
    // Controlled build boundaries keep the failure matrix fast. Actual SDK binaries, the BCL
    // inspector, locked VSCE, streamed archive hashes, and atomic promotion run without substitution.
    const run = async (command, args, options) => {
        process.stderr.write(`[workflow boundary] ${options.label}\n`);
        labels.push(options.label);
        if (options.label === failAt) throw new Error(`Injected failure: ${failAt}.`);
        if (options.label === 'npm version') return { stdout: '10.9.9\n' };
        if (options.label.startsWith('restore ') || options.label.includes('browser assets')) return { stdout: '' };
        if (options.label === 'build UI' || options.label === 'compile extension') {
            const output = args[args.indexOf('--outDir') + 1];
            fs.mkdirSync(output, { recursive: true });
            if (options.label === 'build UI') fs.writeFileSync(path.join(output, 'index.html'), fs.readFileSync(uiSource));
            else fs.writeFileSync(path.join(output, 'extension.js'), 'exports.activate = () => {};\n');
            if (changeInputs) fs.writeFileSync(uiSource, 'changed while building\n');
            return { stdout: '' };
        }
        if (options.label === 'build package inspector') {
            const output = args[args.indexOf('--artifacts-path') + 1];
            clone(inspectorArtifacts, output);
            return { stdout: '' };
        }
        if (options.label.startsWith('publish ')) {
            const output = args[args.indexOf('-o') + 1];
            clone(binaries, output);
            return { stdout: '' };
        }
        if (options.label === 'runtime dependency closure') return { stdout: path.join(root, 'src/etl-sql-vscode') + '\n' };
        return execute(command, args, options.cwd);
    };
    const options = { root, mode: 'build', platform: host.platform, version: identity.version, outputDirectory };
    const callerDirectory = process.cwd();
    const assertPreserved = accepted => {
        assert.deepEqual(fs.readFileSync(destination), accepted);
        assert.equal(fs.existsSync(path.join(root, 'artifacts/vsix-package.lock')), false);
        assert.deepEqual(fs.readdirSync(outputDirectory), [path.basename(destination)]);
        assert.deepEqual(fs.readFileSync(manifestFile), extensionBefore);
        assert.equal(fs.readFileSync(path.join(root, 'src/etl-sql-vscode/bin/developer-data.txt'), 'utf8'), 'preserve developer data\n');
        assert.equal(process.cwd(), callerDirectory);
    };
    fs.writeFileSync(destination, 'previous accepted artifact');
    for (const label of ['restore scripts/typecheck', 'restore scripts/vsix', 'restore src/etl-sql-vscode',
        'restore src/etl-sql-vscode/ui', 'prepare browser assets', 'verify browser assets', 'build UI',
        'compile extension', 'build package inspector', ...names.map(name => `publish ${name}`)]) {
        failAt = label;
        const accepted = fs.readFileSync(destination);
        await assert.rejects(packageVsix(options, { run }), /Injected failure/);
        assertPreserved(accepted);
    }
    failAt = undefined;
    const accepted = fs.readFileSync(destination);
    await assert.rejects(packageVsix(options, { run, assemble: async () => { throw new Error('Injected assembly failure.'); } }), /Injected assembly failure/);
    assertPreserved(accepted);
    changeInputs = true;
    await assert.rejects(packageVsix(options, { run }), /inputs changed/);
    assertPreserved(accepted);
    changeInputs = false;
    fs.writeFileSync(uiSource, 'new UI source\n');
    await assert.rejects(packageVsix({ ...options, mode: 'publish', binDirectory: path.join(temporary, 'missing') }, { run }), /ENOENT/);
    assertPreserved(accepted);
    const lock = write('artifacts/vsix-package.lock', 'owned by another process');
    await assert.rejects(packageVsix(options, { run }), /already locked/);
    assert.equal(fs.readFileSync(lock, 'utf8'), 'owned by another process');
    fs.unlinkSync(lock);
    for (const mode of ['build', 'publish']) {
        assert.equal(await packageVsix({ ...options, mode, binDirectory: binaries }, { run }), destination);
        const inspected = await inspectVsix(root, destination, { identity, target: host.target, expectedFiles: {} });
        assert.equal(inspected.hashes['extension/out/removed-module.js'], undefined);
        assert.equal(inspected.hashes['extension/out/test/dev.js'], undefined);
        assert.equal(inspected.hashes['extension/bin/developer-data.txt'], undefined);
        assert.ok(inspected.hashes['extension/bin/wwwroot/runtime.js']);
        assert.ok(inspected.hashes['extension/ui/dist/index.html']);
    }
    const acceptedBeforeFinalChecks = fs.readFileSync(destination);
    failAt = 'verify browser assets before promotion';
    await assert.rejects(packageVsix({ ...options, mode: 'publish', binDirectory: binaries }, { run }), /Injected failure/);
    assertPreserved(acceptedBeforeFinalChecks);
    failAt = undefined;
    const suppliedBinary = path.join(binaries, names[0] + (host.platform === 'win-x64' ? '.exe' : ''));
    const suppliedLength = fs.statSync(suppliedBinary).size;
    try {
        await assert.rejects(packageVsix({ ...options, mode: 'publish', binDirectory: binaries }, { run,
            assemble: async (...args) => { await createUnsignedVsix(...args); fs.appendFileSync(suppliedBinary, 'changed'); } }), /Published input changed/);
        assertPreserved(acceptedBeforeFinalChecks);
    } finally { fs.truncateSync(suppliedBinary, suppliedLength); }
    assert.ok(labels.includes('build UI'));
    fs.writeFileSync(path.join(binaries, 'wwwroot/runtime.js'), 'old published asset\n');
    const validAccepted = fs.readFileSync(destination);
    await assert.rejects(packageVsix({ ...options, mode: 'publish', binDirectory: binaries }, { run }), /stale published Player asset/);
    assertPreserved(validAccepted);
    assert.equal(fs.readFileSync(path.join(root, 'src/etl-sql-vscode/ui/dist/index.html'), 'utf8'), 'stale UI output\n');
    assert.equal(fs.existsSync(fixture), true);
    const shells = [{ executable: 'bash', suffix: 'sh', prefix: [] }];
    if (process.platform === 'win32') {
        shells[0].executable = path.join(process.env.ProgramFiles, 'Git/bin/bash.exe');
        assert.equal(fs.existsSync(shells[0].executable), true, 'Windows wrapper checks require the installed Git Bash.');
        shells.push({ executable: 'pwsh', suffix: 'ps1', prefix: ['-NoProfile', '-File'] });
    }
    for (const shell of shells) for (const mode of ['build', 'publish']) {
        const script = path.join(repository, `scripts/${mode}-vsix.${shell.suffix}`);
        const args = [...shell.prefix, script];
        if (shell.suffix === 'ps1') args.push('-Version', '0.0.0', '-Platform', host.platform);
        else args.push('--version', '0.0.0', '--platform', host.platform);
        if (mode === 'publish') args.push(shell.suffix === 'ps1' ? '-BinSourceDir' : '--bin-dir', binaries);
        const result = spawnSync(shell.executable, args, { cwd: temporary, encoding: 'utf8' });
        assert.equal(result.error, undefined, result.error?.message);
        assert.notEqual(result.status, 0, 'A public wrapper must propagate coordinator failures.');
        assert.match(result.stdout + result.stderr, /must agree/);
        assert.equal(result.stdout.trim(), '', 'Failure must not print a completed package path.');
        assert.equal(process.cwd(), callerDirectory);
    }
    console.log('VSIX workflow: build/publish promote actual SDK/SQLite bundles; failures preserve accepted packages and developer files.');
} finally {
    fs.writeFileSync(path.join(repository, 'artifacts/vsix-coordinator-tests.json'), JSON.stringify(log, null, 2));
    assert.equal(path.dirname(temporary), path.join(repository, 'artifacts'));
    assert.equal(fs.lstatSync(temporary).isSymbolicLink(), false);
    fs.rmSync(temporary, { recursive: true, force: true });
}
