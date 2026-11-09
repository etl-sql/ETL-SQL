// Source-only checks shared by local hooks, release validation and GitHub.
import { spawn } from 'node:child_process';
import { mkdirSync, openSync, closeSync, readSync, appendFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const sourceChecks = [
  ['workflows', 'check-workflows.mjs'],
  ['workflow-timeouts', 'check-workflow-timeouts.mjs'],
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
  'nightly-release', 'workflows', 'workflow-timeouts', 'dmg-staging', 'windows-release-packages', 'certified-windows', 'shared-windows-payload', 'macos-release-packages', 'certified-macos', 'package-upload-recovery', 'msi-candidate', 'msi-build-watchdog', 'deb-candidate', 'deb-reuse', 'certification-process', 'release-preflight', 'release-versions', 'changelog-compiler', 'vsix-module-identity', 'vsix-test-host',
  'installer-sources', 'installer-actions', 'master-release', 'publish-release', 'sbom-output', 'docs-audit', 'release-evidence', 'production-canary-evidence', 'certification-source', 'deployment-claims', 'local-release-bundle', 'release-driver-gates', 'release-run-wait', 'release-tag', 'release-dry-run', 'release-timeline', 'ci-impact', 'browser-shards', 'built-browser-assets', 'hosted-release-evidence', 'hosted-local-evidence', 'workstation-release-evidence', 'collected-workstation-evidence', 'operator-release-evidence', 'collected-operator-evidence', 'operator-report-inputs', 'workstation-certification-driver', 'lane-shard-results', 'engine-coverage', 'scale-comparison-assets', 'release-certification',
].map(name => ({ id: `test-${name}`, command: process.execPath, args: [`scripts/checks/test-${name}.mjs`] }));
releaseAutomationChecks.push({ id: 'test-release-draft-manifest', command: process.execPath, args: ['scripts/checks/test-release-draft-manifest.mjs'] });
releaseAutomationChecks.push({ id: 'test-msi-progress', command: process.execPath, args: ['scripts/checks/test-msi-progress.mjs'] });
releaseAutomationChecks.push({ id: 'test-candidate-supervisor', command: process.execPath, args: ['scripts/checks/test-candidate-supervisor.mjs'] });
releaseAutomationChecks.push({ id: 'test-test-retry', command: process.execPath, args: ['scripts/checks/test-test-retry.mjs'] });
releaseAutomationChecks.push({ id: 'test-actionlint-download', command: process.execPath, args: ['scripts/checks/test-actionlint-download.mjs'] });
sourceChecks.push(...releaseAutomationChecks);
// Ordinary branch work may keep draft notes. Tag publication must use finalized metadata.
if (process.env.ETLSQL_RELEASE_TAG) sourceChecks.push({ id: 'final-release-metadata', command: process.execPath,
  args: ['scripts/check-release-metadata.mjs', process.env.ETLSQL_RELEASE_TAG] });

// Every pwsh shares one multicore-JIT startup profile (StartupProfileData-NonInteractive in its cache
// directory). Once that file is corrupt, every later pwsh aborts at startup: on 2026-11-08 and -09 the
// MSI preflight on ubuntu-26.04 lost every check after the first one hit, with "Stack overflow." and
// "FileLoadException: The given assembly name was invalid ('System.DiagnostiSystem.Text.Json')".
// Corrupting the file by hand reproduces that abort. Checks start pwsh in parallel and some kill
// it on purpose, so their pwsh neither reads nor writes the profile; it costs ~20 ms per start.
export function checkEnvironment(base = process.env) {
  return { ...base, DOTNET_MultiCoreJitMinNumCpus: '100000' };
}

// The per-check deadline only catches hangs. Across 39 CI preflights (2026-11) test-workflows took a
// median 47.5 s and three checks reached 52-55 s, so a 60 s deadline failed a healthy check on a slow
// runner (CI 41676267442). The enclosing jobs allow 18 minutes for the whole preflight.
export const checkTimeoutMs = 180000;

export async function runChecks(checks, { cwd = root, outputDirectory, concurrency = 3, timeoutMs = checkTimeoutMs, heartbeatMs = 15000, streamLogs = false, report = console.log } = {}) {
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
          child = spawn(check.command, check.args, { cwd, shell: false, windowsHide: true, stdio: ['ignore', fd, fd], env: checkEnvironment() });
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

export function checksForArguments(args) {
  if (args.length === 0) return sourceChecks;
  if (args.length !== 2 || args[0] !== '--release-branch' || !args[1].trim() || args[1].startsWith('-')) {
    throw new Error('Expected no arguments or --release-branch <branch>. This command always runs the full source preflight.');
  }
  return sourceChecks.map(check => check.id === 'release-versions'
    ? { ...check, args: [...check.args, '-BranchName', args[1]] } : check);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const checks = checksForArguments(process.argv.slice(2));
  const outputDirectory = path.join(root, 'release-validation', 'preflight', `${Date.now()}-${process.pid}`);
  const summary = await runChecks(checks, { outputDirectory });
  process.exitCode = summary.passed ? 0 : 1;
}
