import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { builtAssetProblems } from '../check-built-browser-assets.mjs';

const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'built-browser-scope-'));
const root = path.join(temporary, 'repo espaces-é-$');
fs.mkdirSync(root);
const write = (name, text) => {
    const destination = path.join(root, name);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, text);
};
const current = createHash('sha256').update('prepared assets').digest('hex');
const stamp = (name, configuration = 'Debug', value = current) =>
    write(`artifacts/browser-assets-built/${name}.${configuration}.any.sha256`, value);
const scope = (project = 'Fixture.Tests.proj', configuration = 'Debug', more = {}) =>
    builtAssetProblems({ root, project, configuration, ...more });
try {
    write('artifacts/browser-assets.props', 'prepared assets');
    write('Fixture.Tests.proj', `<Project><ItemGroup><ProjectReference Include="Bridge.proj" />
      <ProjectReference Include="DebugHost.proj" Condition="'$(Configuration)' == 'Debug'" /></ItemGroup></Project>`);
    write('Bridge.proj', '<Project><ItemGroup><ProjectReference Include="Runtime.proj" /></ItemGroup></Project>');
    write('Runtime.proj', '<Project><PropertyGroup><RequiresBrowserAssets>true</RequiresBrowserAssets></PropertyGroup></Project>');
    write('DebugHost.proj', '<Project><PropertyGroup><RequiresBrowserAssets>true</RequiresBrowserAssets></PropertyGroup></Project>');
    stamp('Runtime'); stamp('Runtime', 'Release'); stamp('DebugHost');
    stamp('Unrelated', 'Debug', 'older assets'); stamp('Unrelated', 'Release', 'older assets');
    assert.match(builtAssetProblems({ root }).join('\n'), /Unrelated/);
    assert.deepEqual(scope(), [], 'Unrelated outputs must not block a fully built selected graph.');

    stamp('DebugHost', 'Debug', 'older assets');
    assert.match(scope().join('\n'), /DebugHost.*older browser asset preparation/);
    assert.deepEqual(scope('Fixture.Tests.proj', 'Release'), [], 'MSBuild conditions must select the requested configuration.');
    stamp('DebugHost');
    stamp('Runtime', 'Debug', 'older assets');
    assert.match(scope().join('\n'), /Runtime.*older browser asset preparation/);
    fs.rmSync(path.join(root, 'artifacts/browser-assets-built/Runtime.Debug.any.sha256'));
    assert.match(scope().join('\n'), /Runtime.*no unique build recorded/);
    stamp('Runtime');

    write('NoAssets.proj', '<Project />');
    assert.deepEqual(scope('NoAssets.proj'), []);
    write('Rapports-été.proj', '<Project><PropertyGroup><RequiresBrowserAssets>true</RequiresBrowserAssets></PropertyGroup></Project>');
    stamp('Rapports-été');
    assert.deepEqual(scope('Rapports-été.proj'), []);
    write('Multi.proj', '<Project><PropertyGroup><TargetFrameworks>one;two</TargetFrameworks></PropertyGroup></Project>');
    assert.match(scope('Multi.proj').join('\n'), /target framework/);
    assert.ok(scope('missing.proj').length);
    write('Broken.proj', '<Project>');
    assert.match(scope('Broken.proj').join('\n'), /could not evaluate/);
    write('Overrides.proj', `<Project><ItemGroup><ProjectReference Include="Runtime.proj">
      <AdditionalProperties>Configuration=Other</AdditionalProperties></ProjectReference></ItemGroup></Project>`);
    assert.match(scope('Overrides.proj').join('\n'), /overrides/);
    write('WebGlobals.proj', `<Project><ItemGroup><ProjectReference Include="Runtime.proj">
      <GlobalPropertiesToRemove>;WebPublishProfileFile</GlobalPropertiesToRemove></ProjectReference></ItemGroup></Project>`);
    assert.deepEqual(scope('WebGlobals.proj'), []);
    write('ConfigRemoval.proj', `<Project><ItemGroup><ProjectReference Include="Runtime.proj">
      <GlobalPropertiesToRemove>Configuration</GlobalPropertiesToRemove></ProjectReference></ItemGroup></Project>`);
    assert.match(scope('ConfigRemoval.proj').join('\n'), /overrides/);
    write('CycleA.proj', '<Project><ItemGroup><ProjectReference Include="CycleB.proj" /></ItemGroup></Project>');
    write('CycleB.proj', '<Project><ItemGroup><ProjectReference Include="CycleA.proj" /></ItemGroup></Project>');
    assert.match(scope('CycleA.proj').join('\n'), /cycle/);
    write('other/Runtime.proj', '<Project><PropertyGroup><RequiresBrowserAssets>true</RequiresBrowserAssets></PropertyGroup></Project>');
    write('Collision.proj', `<Project><ItemGroup><ProjectReference Include="Runtime.proj" />
      <ProjectReference Include="other/Runtime.proj" /></ItemGroup></Project>`);
    assert.match(scope('Collision.proj').join('\n'), /ambiguous project names/);
    const outside = path.join(temporary, 'Outside.proj');
    fs.writeFileSync(outside, '<Project />');
    assert.match(scope(outside).join('\n'), /outside the repository/);
    write('OutsideReference.proj', '<Project><ItemGroup><ProjectReference Include="../Outside.proj" /></ItemGroup></Project>');
    assert.match(scope('OutsideReference.proj').join('\n'), /outside the repository/);
    assert.ok(scope('Fixture.Tests.proj', 'Debug', { evaluate: () => undefined }).length);
    assert.ok(scope('Fixture.Tests.proj', 'Debug', { evaluate: file => ({
        Properties: { MSBuildProjectName: 'Fixture.Tests', MSBuildProjectFullPath: file, RequiresBrowserAssets: '' },
        Items: { ProjectReference: [] },
    }) }).length, 'Omitted metadata fields must not turn an unknown graph into a no-consumer success.');
    const timedOut = scope('Fixture.Tests.proj', 'Debug', { evaluate: () => {
        throw Object.assign(new Error('provider-private-output'), { code: 'ETIMEDOUT' });
    } });
    assert.match(timedOut.join('\n'), /ETIMEDOUT/);
    assert.doesNotMatch(timedOut.join('\n'), /provider-private-output/);

    const original = fileURLToPath(new URL('../check-built-browser-assets.mjs', import.meta.url));
    const cli = path.join(root, 'scripts/check-built-browser-assets.mjs');
    fs.mkdirSync(path.dirname(cli));
    fs.copyFileSync(original, cli);
    const run = args => spawnSync(process.execPath, [cli, ...args],
        { encoding: 'utf8', windowsHide: true, cwd: temporary, timeout: 60000 });
    const accepted = run(['--configuration', 'Debug', '--project', 'Fixture.Tests.proj']);
    assert.equal(accepted.status, 0, accepted.stderr);
    assert.equal(run(['--configuration', 'Debug']).status, 1, 'Unscoped validation must still reject stale outputs.');
    for (const args of [['--project'], ['--unknown', 'value'], ['toString', 'value'],
        ['--configuration', 'Debug;RequiresBrowserAssets=false'],
        ['--project', 'Fixture.Tests.proj', '--project', 'NoAssets.proj']]) {
        assert.equal(run(args).status, 1, `Invalid CLI scope was accepted: ${args.join(' ')}`);
    }
    console.log('Built browser assets: evaluated transitive/conditional scopes, required stamps, unrelated outputs, unknown graphs and CLI rejection passed.');
} finally {
    assert.equal(path.dirname(temporary), path.resolve(os.tmpdir()));
    fs.rmSync(temporary, { recursive: true, force: true });
}
