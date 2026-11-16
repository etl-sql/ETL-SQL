// The acceptance profile skips the report checks when it cannot seed a report. That is right when
// the Portal's script root is out of reach, and wrong when the caller supplied the root: the
// profile wrote the script there itself, so a refused publish is a Portal defect. Smoke parity
// supplies the root on both targets, so a publish broken on both used to pass as six skips.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const script = path.join(repoRoot, 'scripts', 'Invoke-AcceptanceProfile.ps1');
const root = mkdtempSync(path.join(os.tmpdir(), 'acceptance-profile-'));
const users = ['viewer', 'publisher', 'datasteward', 'orchestratormanager'].map(role => ({ username: `acceptance-${role}` }));

// A Portal whose catalog works and whose report publish answers 500.
const server = createServer((request, response) => {
  const url = new URL(request.url, 'http://stub');
  const reply = (status, body) => { response.writeHead(status, { 'Content-Type': 'application/json' }); response.end(JSON.stringify(body)); };
  request.resume();
  request.on('end', () => {
    if (url.pathname === '/healthz') return reply(200, { status: 'ok' });
    if (url.pathname === '/api/auth/login') return reply(200, { token: 't'.repeat(40) });
    if (url.pathname === '/api/folders') return reply(200, [{ id: 7, name: 'acceptance-folder' }]);
    if (url.pathname === '/api/folders/7/reports') return reply(200, []);
    if (url.pathname === '/api/reports') return reply(500, { error: 'publish is broken' });
    if (url.pathname === '/api/admin/users/catalog') return reply(200, { items: users });
    return reply(404, {});
  });
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}`;

function run(name, extra) {
  const results = path.join(root, `${name}.json`);
  return new Promise((resolve, reject) => {
    const child = spawn('pwsh', ['-NoProfile', '-File', script, '-BaseUrl', baseUrl, '-ResultsPath', results, ...extra],
      { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    child.stdout.on('data', chunk => { output += chunk; });
    child.stderr.on('data', chunk => { output += chunk; });
    const timer = setTimeout(() => child.kill('SIGKILL'), 120000);
    child.once('error', reject);
    child.once('exit', status => {
      clearTimeout(timer);
      let checks = [];
      try { checks = JSON.parse(readFileSync(results, 'utf8')).checks; } catch { /* reported through the status assertion */ }
      resolve({ status, output, outcome: Object.fromEntries(checks.map(check => [check.check, check.outcome])) });
    });
  });
}

try {
  // 1. The caller supplied the script root, so a refused publish fails the run.
  {
    const { status, output, outcome } = await run('root-supplied', ['-ScriptRootPath', path.join(root, 'scripts')]);
    assert.equal(status, 1, output);
    assert.equal(outcome['seeded report is listed'], 'fail', output);
    assert.equal(outcome['seeded folder is listed'], 'pass', output);
  }
  // 2. No script root: the profile could not have seeded a report, so the report checks skip.
  {
    const { status, output, outcome } = await run('no-root', []);
    assert.equal(status, 0, output);
    for (const check of ['seeded report is listed', 'seeded report starts a job', 'seeded report completes'])
      assert.equal(outcome[check], 'skip', output);
  }
  console.log('Acceptance profile: a refused report publish fails when the script root was supplied, and skips when it was not.');
} finally {
  server.close();
  rmSync(root, { recursive: true, force: true });
}
