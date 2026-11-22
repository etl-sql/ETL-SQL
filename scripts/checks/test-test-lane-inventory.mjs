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
const root = mkdtempSync(path.join(os.tmpdir(), 'test-lane-inventory-'));
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
  const out = path.join(root, 'inventory.json');
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', script, '-RepoRoot', root, '-Format', 'Json', '-OutFile', out],
    { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const tests = JSON.parse(readFileSync(out, 'utf8').replace(/^﻿/, '')).tests;
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
  console.log('Test lane inventory: class traits survive nested types, reach partial-class files, and stay per top-level type.');
} finally { rmSync(root, { recursive: true, force: true }); }
