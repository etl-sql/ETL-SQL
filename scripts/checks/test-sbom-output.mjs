import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const parent = fs.realpathSync(os.tmpdir());
const root = fs.realpathSync(fs.mkdtempSync(path.join(parent, 'sbom-output-')));
function write(name, content) {
  const file = path.join(root, name);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content);
}
function execute(args, accepted) {
  const result = spawnSync(process.execPath, [path.join(root, 'scripts/generate-sbom.js'), ...args], { encoding: 'utf8', timeout: 10000 });
  if (result.error) throw result.error;
  assert.equal(result.status === 0, accepted, result.stdout + result.stderr);
}
try {
  write('scripts/generate-sbom.js', fs.readFileSync(new URL('../generate-sbom.js', import.meta.url)));
  write('Directory.Build.props', '<Project><PropertyGroup><VersionPrefix>0.22.0</VersionPrefix></PropertyGroup></Project>');
  write('scripts/codemirror/package-lock.json', JSON.stringify({ packages: { '': { dependencies: {} } } }));
  write('release/sentinel', 'prior output');
  execute([], true);
  assert.equal(JSON.parse(fs.readFileSync(path.join(root, 'release/sbom.json'), 'utf8')).metadata.component.version, '0.22.0');
  const accepted = fs.readFileSync(path.join(root, 'release/sbom.json'));
  execute(['--output-dir', 'artifacts/Unicode é [literal]'], true);
  assert.equal(JSON.parse(fs.readFileSync(path.join(root, 'artifacts/Unicode é [literal]/sbom.json'), 'utf8')).metadata.component.version, '0.22.0');
  assert.deepEqual(fs.readFileSync(path.join(root, 'release/sbom.json')), accepted);
  for (const args of [['--wrong'], ['--output-dir'], ['--output-dir', '.'], ['--output-dir', '..'], ['--output-dir', 'x', '--extra']]) execute(args, false);
  fs.mkdirSync(path.join(root, 'held'));
  fs.symlinkSync(path.join(root, 'held'), path.join(root, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
  execute(['--output-dir', 'linked/subdirectory'], false);
  assert.deepEqual(fs.readdirSync(path.join(root, 'held')), []);
  fs.mkdirSync(path.join(root, 'artifacts/not-file/sbom.json'), { recursive: true });
  execute(['--output-dir', 'artifacts/not-file'], false);
  assert.equal(fs.readFileSync(path.join(root, 'release/sentinel'), 'utf8'), 'prior output');
  console.log('SBOM output: default and staged generation, accepted-byte retention, malformed options, path escape, linked ancestor and non-file destination passed.');
} finally {
  assert.equal(path.dirname(fs.realpathSync(root)), parent);
  assert.match(path.basename(root), /^sbom-output-/);
  fs.rmSync(root, { recursive: true, force: true });
}
