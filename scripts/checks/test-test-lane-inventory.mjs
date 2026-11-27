import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';

// The inventory decides which lane a test belongs to from its Category traits. A public type nested
// in a test class used to reset the class's traits, and a partial class's second file never saw them,
// so RoleJourneyTests (Browser) and the *.Owners.cs / *.Zoom.cs partials read as uncategorized.
const script = fileURLToPath(new URL('../Get-TestLaneInventory.ps1', import.meta.url));
const tmp = mkdtempSync(path.join(os.tmpdir(), 'test-lane-inventory-'));
// A long directory name has an 8.3 alias on Windows; CI's temp directory is one (RUNNER~1).
const root = path.join(tmp, 'repository with a long name');
const project = path.join(root, 'tests', 'Sample');
const files = {
  'Sample.csproj': '<Project Sdk="Microsoft.NET.Sdk" />',
  'NestedTests.cs': `namespace Sample;
[Trait("Category", "Browser")]
public sealed class NestedTests(object fixture)
{
    public sealed record Row(int A);

    [Fact]
    public void AfterNestedType() { }
}
`,
  'SplitTests.cs': `namespace Sample;
[Trait("Category", "Integration")]
public sealed partial class SplitTests
{
    [Fact]
    public void InMainFile() { }
}
`,
  'SplitTests.Extra.cs': `namespace Sample;
public sealed partial class SplitTests
{
    [Fact]
    public void InPartialFile() { }
}
`,
  'PlainTests.cs': `namespace Sample
{
    public class PlainTests
    {
        [Fact]
        public void Uncategorized() { }
    }

    [Trait("Category", "SLT")]
    public class SecondTopLevelTests
    {
        [Fact]
        public void SecondType() { }
    }
}
`,
};
try {
  mkdirSync(project, { recursive: true });
  for (const [name, text] of Object.entries(files)) writeFileSync(path.join(project, name), text);
  const inventory = (repoRoot) => {
    const out = path.join(tmp, 'inventory.json');
    const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', script, '-RepoRoot', repoRoot, '-Format', 'Json', '-OutFile', out],
      { encoding: 'utf8', windowsHide: true, timeout: 60000 });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stdout + result.stderr);
    return JSON.parse(readFileSync(out, 'utf8').replace(/^﻿/, '')).tests;
  };
  const tests = inventory(root);
  assert.deepEqual([...new Set(tests.map(t => t.project.replaceAll('\\', '/')))], ['tests/Sample/Sample.csproj']);
  assert.deepEqual([...new Set(tests.map(t => t.file.replaceAll('\\', '/').split('/').slice(0, 2).join('/')))], ['tests/Sample']);
  const categories = Object.fromEntries(tests.map(t => [t.method, t.categories]));
  assert.deepEqual(categories, {
    AfterNestedType: ['Browser'],
    InMainFile: ['Integration'],
    InPartialFile: ['Integration'],
    Uncategorized: ['(none)'],
    SecondType: ['SLT'],
  });
  if (process.platform === 'win32') {
    const short = spawnSync('cmd', ['/d', '/c', `for %I in ("${root}") do @echo %~sI`], { encoding: 'utf8', windowsHide: true, windowsVerbatimArguments: true }).stdout.trim();
    const viaShort = inventory(short);
    assert.deepEqual(viaShort.map(t => [t.project, t.file]), tests.map(t => [t.project, t.file]), `8.3 root ${short} changed the paths`);
  }
  console.log('Test lane inventory: class traits survive nested types, reach partial-class files, and stay per top-level type.');
} finally { rmSync(tmp, { recursive: true, force: true }); }
