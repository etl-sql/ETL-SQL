import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const fixture = mkdtempSync(path.join(os.tmpdir(), 'docs-audit-'));
try {
  mkdirSync(path.join(fixture, 'scripts'));
  mkdirSync(path.join(fixture, 'docs/guides'), { recursive: true });
  copyFileSync(new URL('../audit-docs.js', import.meta.url), path.join(fixture, 'scripts/audit-docs.js'));
  writeFileSync(path.join(fixture, 'docs/guides/topic.md'), '# Topic\n\n## Details\n');
  const audit = href => {
    writeFileSync(path.join(fixture, 'docs/README.md'), `# Documentation\n\n[Topic](${href})\n`);
    const result = spawnSync(process.execPath, [path.join(fixture, 'scripts/audit-docs.js'), '--strict'],
      { encoding: 'utf8', windowsHide: true, timeout: 10000 });
    assert.equal(result.error, undefined);
    return result;
  };
  const valid = audit('guides/topic.md#details');
  assert.equal(valid.status, 0, valid.stdout + valid.stderr);
  for (const href of ['guides/Topic.md', 'Guides/topic.md', 'guides/missing.md', 'guides/topic.md#missing']) {
    const invalid = audit(href);
    assert.equal(invalid.status, 1, `${href}\n${invalid.stdout}${invalid.stderr}`);
    assert.match(invalid.stdout, /broken (link|anchor)/);
  }
  assert.equal(audit('guides/../guides/topic.md#details').status, 0);
  console.log('Documentation audit: file/directory casing, missing targets and anchors fail on every platform; exact and normalized links pass.');
} finally {
  rmSync(fixture, { recursive: true, force: true });
}
