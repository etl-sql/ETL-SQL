// Source-only checks shared by local hooks, release validation and GitHub.
import { spawn } from 'node:child_process';
import { mkdirSync, openSync, closeSync, readSync, appendFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const sourceChecks = [
  ['workflows', 'check-workflows.mjs'],
  ['syntax-index', 'generate-syntax-index.js', '--check'],
  ['syntax-links', 'audit-syntax-index.js', '--strict'],
  ['documentation', 'audit-docs.js', '--strict'],
  ['flaky-delays', 'check-flaky-test-delays.mjs'],
  ['shell-line-endings', 'check-shell-line-endings.js'],
  ['comment-policy', 'check-ai-slop-comments.mjs'],
].map(([id, script, ...args]) => ({ id, command: process.execPath, args: [`scripts/${script}`, ...args] }));
sourceChecks.push({ id: 'test-inventory', command: 'pwsh', args: ['-NoProfile', '-File', 'scripts/Get-TestLaneInventory.ps1', '-FailOnIssues'] });
sourceChecks.push({ id: 'release-versions', command: 'pwsh', args: ['-NoProfile', '-File', 'scripts/Test-ReleaseVersions.ps1'] });
sourceChecks.push({ id: 'installer-sources', command: 'pwsh', args: ['-NoProfile', '-File', 'scripts/Test-InstallerSources.ps1'] });

// These fixtures require no restore, build, network or installed product. Each runs in its own
// process so a failing policy test cannot hide the other source or recovery findings.
export const releaseAutomationChecks = [
  'nightly-release', 'workflows', 'windows-release-packages', 'macos-release-packages', 'package-upload-recovery', 'msi-candidate', 'deb-candidate', 'deb-reuse', 'certification-process', 'release-preflight', 'release-versions',
  'installer-sources', 'installer-actions', 'master-release', 'publish-release', 'sbom-output', 'docs-audit', 'release-evidence', 'production-canary-evidence', 'certification-source', 'deployment-claims', 'local-release-bundle', 'release-driver-gates', 'release-run-wait', 'release-tag', 'release-timeline', 'ci-impact', 'browser-shards', 'built-browser-assets', 'hosted-release-evidence', 'hosted-local-evidence', 'release-certification',
].map(name => ({ id: `test-${name}`, command: process.execPath, args: [`scripts/checks/test-${name}.mjs`] }));
sourceChecks.push(...releaseAutomationChecks);
// Ordinary branch work may keep draft notes. Tag publication must use finalized metadata.
if (process.env.ETLSQL_RELEASE_TAG) sourceChecks.push({ id: 'final-release-metadata', command: process.execPath,
  args: ['scripts/check-release-metadata.mjs', process.env.ETLSQL_RELEASE_TAG] });

export async function runChecks(checks, { cwd = root, outputDirectory, concurrency = 3, timeoutMs = 60000, heartbeatMs = 15000, streamLogs = false, report = console.log } = {}) {
  if (!outputDirectory || !Number.isInteger(concurrency) || concurrency < 1 || timeoutMs < 1 || heartbeatMs < 1) throw new Error('Invalid preflight options.');
  if (checks.length === 0 || new Set(checks.map(c => c.id)).size !== checks.length || checks.some(c => !/^[a-z0-9-]+$/.test(c.id))) throw new Error('Checks need unique, safe IDs.');
  mkdirSync(outputDirectory, { recursive: true });
  const started = Date.now();
  const results = new Array(checks.length);
  let next = 0;
  async function worker() {
    while (next < checks.length) {
      const index = next++;
      const check = checks[index];
      const logPath = path.join(outputDirectory, `${check.id}.log`);
      const fd = openSync(logPath, 'w');
      const checkStart = Date.now();
      report(`Starting ${check.id}`);
      const result = await new Promise(resolve => {
        let done = false;
        let timer;
        let heartbeat;
        let logOffset = 0;
        function streamOutput() {
          if (!streamLogs) return;
          const reader = openSync(logPath, 'r');
          try {
            const buffer = Buffer.alloc(32768);
            const length = readSync(reader, buffer, 0, buffer.length, logOffset);
            logOffset += length;
            if (length) report(`[${check.id}] ${buffer.subarray(0, length).toString('utf8').trimEnd()}`);
          } finally { closeSync(reader); }
        }
        let timedOut = false;
        function finish(code, error) {
          if (done) return;
          done = true;
          clearTimeout(timer);
          clearInterval(heartbeat);
          closeSync(fd);
          if (error) appendFileSync(logPath, `${error}\n`);
          streamOutput();
          resolve({ id: check.id, command: check.command, args: check.args, exitCode: code, timedOut, passed: code === 0 && !timedOut && !error, durationMs: Date.now() - checkStart, logPath });
        }
        let child;
        try {
          // Files, not pipes: large output cannot block collection of another failure.
          child = spawn(check.command, check.args, { cwd, shell: false, windowsHide: true, stdio: ['ignore', fd, fd] });
        } catch (error) { finish(null, error.message); return; }
        child.once('error', error => finish(null, error.message));
        child.once('exit', code => finish(code));
        heartbeat = setInterval(() => {
          streamOutput();
          report(`Heartbeat: ${check.id}, ${((Date.now() - checkStart) / 1000).toFixed(1)}s elapsed; log: ${logPath}`);
        }, heartbeatMs);
        timer = setTimeout(() => {
          timedOut = true;
          child.kill('SIGKILL');
          finish(null, `Check exceeded ${timeoutMs}ms; stopped its child process.`);
        }, timeoutMs);
      });
      results[index] = result;
      report(`${result.passed ? 'PASS' : 'FAIL'} ${check.id} (${(result.durationMs / 1000).toFixed(1)}s): ${logPath}`);
    }
  }
  await Promise.all(Array.from({ length: Math.min(concurrency, checks.length) }, worker));
  const summary = { schemaVersion: 1, startedUtc: new Date(started).toISOString(), durationMs: Date.now() - started, passed: results.every(r => r.passed), results };
  writeFileSync(path.join(outputDirectory, 'summary.json'), JSON.stringify(summary, null, 2));
  report(`Preflight: ${results.filter(r => !r.passed).length}/${results.length} failed in ${(summary.durationMs / 1000).toFixed(1)}s. Report: ${path.join(outputDirectory, 'summary.json')}`);
  return summary;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.length > 2) throw new Error('No check-selection switches: this command always runs the full source preflight.');
  const outputDirectory = path.join(root, 'release-validation', 'preflight', `${Date.now()}-${process.pid}`);
  const summary = await runChecks(sourceChecks, { outputDirectory });
  process.exitCode = summary.passed ? 0 : 1;
}
