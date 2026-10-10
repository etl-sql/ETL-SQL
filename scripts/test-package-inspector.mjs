import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolveVsixPlatform, vsixTargets } from './lib/vsix-package.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
fs.mkdirSync(path.join(root, 'artifacts'), { recursive: true });
const temporary = fs.mkdtempSync(path.join(root, 'artifacts', 'package-inspector espaces-é-$-'));
const host = resolveVsixPlatform().platform;
const platforms = process.argv.includes('--all-platforms') ? Object.keys(vsixTargets) : [host];
const name = 'ETL-SQL-InspectFixture';
const version = '0.21.0';
const log = [];
const run = (args, options = {}) => {
    const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', timeout: 180000, ...options });
    log.push({ args, status: result.status, stdout: result.stdout, stderr: result.stderr });
    assert.equal(result.error, undefined, `dotnet could not complete: ${result.error?.message}`);
    return result;
};
const succeed = (args, options) => {
    const result = run(args, options);
    assert.equal(result.status, 0, `${args.join(' ')}\n${result.stdout}\n${result.stderr}`);
    return result;
};
try {
    const inspectorArtifacts = path.join(temporary, 'inspector-dotnet');
    succeed(['build', path.join(root, 'tools/package-inspector/ETL-SQL.PackageInspector.csproj'), '-c', 'Release',
        '--artifacts-path', inspectorArtifacts, '--nologo']);
    const inspector = path.join(inspectorArtifacts, 'bin/ETL-SQL.PackageInspector/release/ETL-SQL-PackageInspector.dll');
    assert.equal(fs.existsSync(inspector), true);
    const fixture = path.join(temporary, 'fixture');
    fs.mkdirSync(fixture);
    fs.writeFileSync(path.join(fixture, 'Program.cs'), 'return 0;\n');
    fs.writeFileSync(path.join(fixture, 'Fixture.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>${name}</AssemblyName>
    <Version>${version}</Version>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <PublishReadyToRun>false</PublishReadyToRun>
  </PropertyGroup>
</Project>\n`);
    const publish = (platform, variant, extra = []) => {
        const output = path.join(temporary, `${variant}-${platform}`);
        succeed(['publish', path.join(fixture, 'Fixture.csproj'), '-c', 'Release', '-r', platform,
            '--artifacts-path', path.join(temporary, `fixture-dotnet-${variant}`), '-o', output, '--nologo', ...extra]);
        return path.join(output, name + (platform === 'win-x64' ? '.exe' : ''));
    };
    const inspect = (binary, requestedName, requestedVersion, platform, error) => {
        const result = run([inspector, binary, requestedName, requestedVersion, platform]);
        if (error) {
            assert.equal(result.status, 1, `Rejected binary returned ${result.status}: ${result.stdout} ${result.stderr}`);
            assert.equal(result.stdout, '');
            assert.match(JSON.parse(result.stderr).error, error);
            return;
        }
        assert.equal(result.status, 0, result.stderr);
        const metadata = JSON.parse(result.stdout);
        assert.equal(metadata.assemblyName, requestedName);
        assert.equal(metadata.version, requestedVersion);
        assert.equal(metadata.platform, platform);
        assert.equal(metadata.sha256, createHash('sha256').update(fs.readFileSync(binary)).digest('hex'));
        assert.equal(metadata.entries.includes('System.Private.CoreLib.dll'), true);
        return metadata;
    };
    for (const platform of platforms) {
        const binary = publish(platform, 'self-contained');
        inspect(binary, name, version, platform);
        inspect(binary, name, '0.20.0', platform, /Bundled assembly/);
        const wrongPlatform = platform === 'win-x64' ? 'linux-x64' : 'win-x64';
        inspect(binary, name, version, wrongPlatform, /architecture\/platform/);
        const renamed = path.join(temporary, `${platform}-ETL-SQL-Player${platform === 'win-x64' ? '.exe' : ''}`);
        fs.copyFileSync(binary, renamed);
        inspect(renamed, 'ETL-SQL-Player', version, platform, /missing the main assembly/);
        const bytes = fs.readFileSync(binary);
        const truncated = path.join(temporary, `${platform}-truncated`);
        fs.writeFileSync(truncated, bytes.subarray(0, Math.min(bytes.length - 1024, 1024 * 1024)));
        inspect(truncated, name, version, platform, /single-file bundle|bundle marker/);
        const runtimeTarget = `.NETCoreApp,Version=v10.0/${platform}`;
        const targetPosition = bytes.indexOf(Buffer.from(runtimeTarget));
        assert.notEqual(targetPosition, -1, 'SDK dependency metadata must contain its runtime target.');
        const inconsistent = Buffer.from(bytes);
        // Change only the target name; keep the SDK's manifest offsets and string length intact.
        inconsistent[targetPosition + runtimeTarget.length - 1] = 'z'.charCodeAt(0);
        const badTarget = path.join(temporary, `${platform}-bad-runtime-target`);
        fs.writeFileSync(badTarget, inconsistent);
        inspect(badTarget, name, version, platform, /dependency target disagrees/);
        if (platform === host) {
            const executable = spawnSync(binary, [], { encoding: 'utf8', timeout: 30000,
                env: { ...process.env, DOTNET_ROOT: path.join(temporary, 'absent-runtime'),
                    DOTNET_ROOT_X64: path.join(temporary, 'absent-runtime'), DOTNET_ROOT_ARM64: path.join(temporary, 'absent-runtime') } });
            assert.equal(executable.error, undefined);
            assert.equal(executable.status, 0, executable.stderr);
        }
    }
    const compressed = publish(host, 'compressed', ['-p:EnableCompressionInSingleFile=true']);
    inspect(compressed, name, version, host);
    const frameworkDependent = publish(host, 'framework-dependent', ['--self-contained', 'false']);
    inspect(frameworkDependent, name, version, host, /not self-contained/);
    const missing = path.join(temporary, 'missing-binary');
    inspect(missing, name, version, host, /Could not find|cannot find/);
    console.log(`Package inspector: ${platforms.length} real SDK target(s), native host execution, compressed metadata, and version/name/architecture/runtime/truncation/missing-file rejection passed.`);
} finally {
    // Keep command evidence even when a fixture fails; remove only this test's assembly directory.
    fs.writeFileSync(path.join(root, 'artifacts', 'package-inspector-tests.json'), JSON.stringify(log, null, 2));
    if (path.dirname(path.resolve(temporary)) !== path.resolve(root, 'artifacts') || fs.lstatSync(temporary).isSymbolicLink())
        throw new Error('Refusing unexpected package-inspector fixture cleanup.');
    fs.rmSync(temporary, { recursive: true, force: true });
}
