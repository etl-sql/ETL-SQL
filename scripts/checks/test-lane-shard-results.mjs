import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';

const root = mkdtempSync(path.join(os.tmpdir(), 'lane-shard-results-'));
const source = fileURLToPath(new URL('../TestLane.Helpers.ps1', import.meta.url));
const expected = [{ method: 'ETL_SQL.Fixture.A.Theory', count: 2 }, { method: 'ETL_SQL.Fixture.B.Test', count: 1 }];
function verify(rows, definitions = rows) {
  writeFileSync(path.join(root, 'results.trx'), `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>${rows.map(([id]) => `<UnitTestResult testId="${id}"/>`).join('')}</Results><TestDefinitions>${definitions.map(([id, method]) => `<UnitTest id="${id}"><TestMethod className="${method.slice(0, method.lastIndexOf('.'))}" name="${method.slice(method.lastIndexOf('.') + 1)}"/></UnitTest>`).join('')}</TestDefinitions></TestRun>`);
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($env:FIXTURE_SOURCE,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Lane script parse errors'}
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Read-ShardTestResults'},$true)
if(-not $function){throw 'Missing production TRX verifier'}
Invoke-Expression $function.Extent.Text
$expected=Get-Content -LiteralPath expected.json -Raw | ConvertFrom-Json
$result=Read-ShardTestResults -TrxPath results.trx -ExpectedMethodCounts $expected
@{actual=$result.results.Count;missing=@($result.missingMethods | ForEach-Object method);unexpected=@($result.unexpectedMethods)} | ConvertTo-Json -Depth 5
`], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 15000, env: { ...process.env, FIXTURE_SOURCE: source } });
  assert.ifError(result.error); assert.equal(result.status, 0, result.stdout + result.stderr);
  return JSON.parse(result.stdout);
}
try {
  writeFileSync(path.join(root, 'expected.json'), JSON.stringify(expected));
  const rows = [['1', expected[0].method], ['2', expected[0].method], ['3', expected[1].method]];
  assert.deepEqual(verify(rows), { actual: 3, missing: [], unexpected: [] });
  assert.deepEqual(verify([...rows, ['4', expected[0].method]]), { actual: 4, missing: [], unexpected: [] });
  assert.deepEqual(verify(rows.slice(0, 2)).missing, [expected[1].method]);
  assert.deepEqual(verify([rows[0], rows[2]]).missing, [expected[0].method]);
  assert.equal(verify([]).missing.length, 2);
  assert.equal(verify([...rows, ['foreign', 'ETL_SQL.Fixture.C.Extra']]).unexpected.length, 1);
  assert.equal(verify(rows, rows.slice(0, 2)).unexpected.length, 1);
  assert.equal(verify([['1', expected[0].method.toLowerCase()], ...rows.slice(1)]).unexpected.length, 1);
  console.log('Lane TRX verification: complete and expanded theories pass; missing method, missing theory row, empty run, foreign/missing identity and case mismatch fail inventory verification.');
} finally { rmSync(root, { recursive: true, force: true }); }
