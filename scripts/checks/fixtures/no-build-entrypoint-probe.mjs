import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function verifyNoBuildEntrypoints() {
    const base = fs.realpathSync.native(os.tmpdir());
    const temporary = fs.mkdtempSync(path.join(base, 'no-build-lanes-'));
    const root = path.join(temporary, "repo espaces-é-$-quote'");
    const scripts = path.join(root, 'scripts');
    const originals = fileURLToPath(new URL('../../', import.meta.url));
    fs.mkdirSync(scripts, { recursive: true });
    try {
        for (const name of ['test-lane.ps1', 'test-lane.sh', 'test-smoke.ps1', 'test-smoke.sh', 'Release.Helpers.ps1'])
            fs.copyFileSync(path.join(originals, name), path.join(scripts, name));
        const log = path.join(temporary, 'calls.jsonl');
        const driver = path.join(temporary, 'driver.ps1');
        fs.writeFileSync(driver, String.raw`$ErrorActionPreference = 'Stop'
$global:EtlsqlProbeGuardCount = 0
function global:node {
  @{ command = 'node'; args = @($args) } | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ETLSQL_PROBE_LOG
  $guard = $args[0] -like '*check-built-browser-assets.mjs'
  if ($guard) { $global:EtlsqlProbeGuardCount++ }
  $reject = $env:ETLSQL_PROBE_GUARD -eq 'reject' -or ($env:ETLSQL_PROBE_GUARD -eq 'reject-second' -and $global:EtlsqlProbeGuardCount -eq 2)
  $global:LASTEXITCODE = if ($guard -and $reject) { 13 } else { 0 }
}
function global:dotnet {
  @{ command = 'dotnet'; args = @($args) } | ConvertTo-Json -Compress | Add-Content -LiteralPath $env:ETLSQL_PROBE_LOG
  Write-Output 'Passed: 1'
  $global:LASTEXITCODE = 0
}
$parameters = $env:ETLSQL_PROBE_PARAMETERS | ConvertFrom-Json -AsHashtable
& $env:ETLSQL_PROBE_SCRIPT @parameters
exit $LASTEXITCODE
`);
        const logger = path.join(temporary, 'logger.mjs');
        fs.writeFileSync(logger, String.raw`import fs from 'node:fs';
const [command, ...args] = process.argv.slice(2);
fs.appendFileSync(process.env.ETLSQL_PROBE_LOG, JSON.stringify({ command, args }) + '\n');
if (command === 'node' && args[0]?.endsWith('check-built-browser-assets.mjs')) {
  const count = fs.readFileSync(process.env.ETLSQL_PROBE_LOG, 'utf8').trim().split(/\r?\n/).map(line => JSON.parse(line))
    .filter(call => call.command === 'node' && call.args[0]?.endsWith('check-built-browser-assets.mjs')).length;
  if (process.env.ETLSQL_PROBE_GUARD === 'reject' || (process.env.ETLSQL_PROBE_GUARD === 'reject-second' && count === 2)) process.exit(13);
}
if (command === 'dotnet') console.log('Passed: 1');
`);
        const shim = path.join(temporary, 'bin');
        fs.mkdirSync(shim);
        for (const name of ['node', 'dotnet']) {
            const file = path.join(shim, name);
            fs.writeFileSync(file, `#!/bin/sh\n"$ETLSQL_PROBE_NODE" "$ETLSQL_PROBE_LOGGER" ${name} "$@"\n`);
            fs.chmodSync(file, 0o755);
        }
        let bash = '/bin/bash';
        if (process.platform === 'win32') {
            const git = spawnSync('git', ['--exec-path'], { encoding: 'utf8', windowsHide: true });
            assert.equal(git.status, 0, git.stderr);
            bash = path.resolve(git.stdout.trim(), '../../..', 'bin/bash.exe');
        }
        const quote = value => `'${value.replaceAll("'", "'\\''")}'`;
        const posix = value => {
            if (process.platform !== 'win32') return value;
            const converted = spawnSync(bash, ['--noprofile', '--norc', '-c', 'cygpath -u "$1"', 'convert', value],
                { encoding: 'utf8', windowsHide: true, timeout: 10000 });
            assert.equal(converted.status, 0, converted.stderr);
            return converted.stdout.trim();
        };
        const shells = ['powershell'];
        if (fs.existsSync(bash)) shells.push('bash');
        else if (process.platform !== 'win32') throw new Error('Bash is required to validate the POSIX entry points.');
        const shared = { ...process.env, ETLSQL_PROBE_LOG: log };
        for (const shell of shells) {
            for (const { kind, lane, expected } of [
                { kind: 'lane', lane: 'engine', expected: 1 },
                { kind: 'smoke', lane: 'security', expected: 2 },
                { kind: 'smoke', lane: 'all', expected: 5 },
            ]) {
                for (const noBuild of [false, true]) {
                    for (const outcome of noBuild ? (expected > 1 ? ['accept', 'reject', 'reject-second'] : ['accept', 'reject']) : ['accept']) {
                        const reject = outcome !== 'accept';
                        fs.writeFileSync(log, '');
                        const script = path.join(scripts, `test-${kind}.${shell === 'powershell' ? 'ps1' : 'sh'}`);
                        const env = { ...shared, ETLSQL_PROBE_GUARD: outcome };
                        let result;
                        if (shell === 'powershell') {
                            env.ETLSQL_PROBE_SCRIPT = script;
                            env.ETLSQL_PROBE_PARAMETERS = JSON.stringify({ Lane: lane, Configuration: 'Release', NoRestore: true,
                                NoBuild: noBuild, ...(kind === 'lane' ? { EngineShardCount: reject ? 2 : 1 } : {}) });
                            result = spawnSync('pwsh', ['-NoProfile', '-File', driver],
                                { env, encoding: 'utf8', windowsHide: true, cwd: temporary, timeout: 15000 });
                        } else {
                            const command = `export PATH=${quote(posix(shim))}:$PATH; export ETLSQL_PROBE_NODE=${quote(posix(process.execPath))}; ` +
                                `export ETLSQL_PROBE_LOGGER=${quote(posix(logger))}; /bin/bash ${quote(posix(script))} ` +
                                `--lane ${lane} --configuration Release --no-restore${noBuild ? ' --no-build' : ''}`;
                            result = spawnSync(bash, ['--noprofile', '--norc', '-c', command],
                                { env, encoding: 'utf8', windowsHide: true, cwd: temporary, timeout: 15000 });
                        }
                        assert.equal(result.error, undefined);
                        const calls = fs.readFileSync(log, 'utf8').replace(/^\uFEFF/, '').trim().split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
                        const guards = calls.filter(call => call.command === 'node' && call.args[0].endsWith('check-built-browser-assets.mjs'));
                        const tests = calls.filter(call => call.command === 'dotnet');
                        assert.ok(calls[0].args[0].endsWith('sync-assets.js'));
                        assert.equal(calls[0].args.includes('-Check'), noBuild);
                        if (reject) {
                            assert.notEqual(result.status, 0, `${shell}/${kind} ignored a failed guard.`);
                            const stoppedAt = outcome === 'reject-second' ? 2 : 1;
                            assert.equal(guards.length, stoppedAt);
                            assert.equal(tests.length, stoppedAt - 1, 'Guard failure must stop the unverified project before tests or shard discovery.');
                        } else {
                            assert.equal(result.status, 0, result.stdout + result.stderr);
                            assert.equal(tests.length, expected);
                            assert.equal(guards.length, noBuild ? tests.length : 0);
                            for (const [i, test] of tests.entries()) {
                                assert.equal(test.args.includes('--no-build'), noBuild);
                                if (!noBuild) continue;
                                const guard = guards[i];
                                const project = guard.args[guard.args.indexOf('--project') + 1];
                                assert.equal(path.resolve(project), path.resolve(test.args[1]));
                                assert.equal(guard.args[guard.args.indexOf('--configuration') + 1],
                                    test.args[test.args.indexOf('--configuration') + 1]);
                                assert.ok(calls.indexOf(guard) < calls.indexOf(test), 'Each project must be checked before it runs.');
                            }
                        }
                    }
                }
            }
        }
        console.log(`No-build entry points: ${shells.join(', ')} lane/smoke scopes, aggregate projects and rejection before test/discovery passed.`);
    } finally {
        assert.equal(path.dirname(temporary), base);
        assert.ok(path.basename(temporary).startsWith('no-build-lanes-'));
        fs.rmSync(temporary, { recursive: true, force: true });
    }
}
