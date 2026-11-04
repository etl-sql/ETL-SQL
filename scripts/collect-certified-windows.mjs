import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { appendFileSync, lstatSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectHostedEvidence } from './collect-hosted-release-evidence.mjs';
import { windowsPackages } from './windows-release-packages.mjs';

export const certifiedWindowsPolicy = { schemaVersion: 1, maximumAgeHours: 168, workflows: [
  { file: 'native-packages.yml', events: ['workflow_dispatch'], releaseBranchesOnly: true,
    jobs: ['Native package impact', 'Native package (win-x64)', 'Native Package Certification'] },
] };
const hash = value => createHash('sha256').update(value).digest('hex');
const json = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const upper = value => Date.parse(value) + (/:\d{2}(?:Z|[+-]\d{2}:\d{2})$/.test(value ?? '') ? 999 : 0);
const pinned = ['schemaVersion', 'scope', 'repository', 'sourceSha', 'version', 'runId', 'jobId',
  'executionAttempt', 'artifactId', 'artifactName', 'artifactDigest', 'manifestSha256', 'evidenceSha256', 'platform', 'windowsPayloadSha256'];
function sameSelection(actual, expected) {
  if (pinned.some(field => !isDeepStrictEqual(actual[field], expected[field]))) throw new Error('Certified native selection changed after validation.');
}
function artifacts(pages) {
  if (!Array.isArray(pages) || !pages.length || pages.some(page => !Array.isArray(page.artifacts))) throw new Error('Invalid native artifact inventory.');
  const inventory = pages.flatMap(page => page.artifacts);
  if (pages.some(page => page.total_count !== inventory.length) || inventory.some(record => !Number.isSafeInteger(record.id) || record.id < 1) ||
      new Set(inventory.map(record => record.id)).size !== inventory.length) throw new Error('Incomplete or duplicate native artifact inventory.');
  return inventory;
}
function file(root, relative) {
  if (lstatSync(root).isSymbolicLink() || !lstatSync(root).isDirectory()) throw new Error('Certified native root must be a real directory.');
  if (relative.split('/').some(component => !component || component === '.' || component === '..' || component.includes('\\'))) throw new Error('Invalid certified native evidence path.');
  let current = root;
  for (const component of relative.split('/')) {
    current = path.join(current, component);
    if (lstatSync(current).isSymbolicLink()) throw new Error('Certified native inputs cannot traverse links.');
  }
  if (!lstatSync(current).isFile() || lstatSync(current).size === 0) throw new Error('Certified native evidence is missing or empty.');
  return current;
}

function nativePlatform(platform) {
  if (!['win-x64', 'osx-x64', 'osx-arm64'].includes(platform)) throw new Error('Unsupported certified native platform.');
  return { target: platform === 'win-x64' ? 'win32-x64' : platform.replace('osx-', 'darwin-'),
    manifest: platform === 'win-x64' ? 'windows-package-inputs.json' : `${platform}-package-inputs.json`,
    architecture: platform === 'osx-arm64' ? 'arm64' : 'x64' };
}

async function verifyPackages(directory, sha, version, platform, digest) {
  if (platform === 'win-x64') return windowsPackages('verify', directory, sha, version, digest);
  const { macosPackages } = await import('./macos-release-packages.mjs');
  return macosPackages('verify', directory, sha, version, platform, digest);
}

export async function verifyCertifiedNative(directory, sha, version, platform) {
  const descriptor = nativePlatform(platform);
  if (!/^[a-f0-9]{40}$/.test(sha) || !/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) throw new Error('Invalid native acceptance identity.');
  const evidence = `artifacts/native-packages/${platform}`;
  const summaryFile = file(directory, `${evidence}/certification.json`), summary = json(summaryFile);
  if (summary.publishReadyToRun !== true) throw new Error('Native publication requires ReadyToRun release certification.');
  if (platform === 'win-x64' && (summary.windowsPayload?.sourceSha !== sha || summary.windowsPayload?.version !== version ||
      summary.windowsPayload?.publishReadyToRun !== true ||
      !/^[a-f0-9]{64}$/.test(summary.windowsPayload?.manifestSha256 ?? ''))) throw new Error('Shared Windows payload acceptance is missing or mismatched.');
  const manifestFile = file(directory, `release/${descriptor.manifest}`), manifestSha256 = hash(readFileSync(manifestFile));
  const manifest = json(manifestFile);
  for (const name of [`ETL-SQL-v${version}-${platform}.zip`, `etl-sql-vscode-${version}-${descriptor.target}.vsix`]) file(directory, `release/${name}`);
  await verifyPackages(path.join(directory, 'release'), sha, version, platform, manifestSha256);
  const webviewFile = file(directory, `${evidence}/packaged-webview.json`), webviews = json(webviewFile);
  // The CLI prints its start-up and security-policy lines before the version, as the native
  // workflow's own launch check allows; require the exact candidate version as one whole line.
  const cliFile = file(directory, `${evidence}/cli-version.log`);
  const cliLines = readFileSync(cliFile, 'utf8').replace(/^\uFEFF/, '').split(/\r?\n/).map(line => line.trim());
  const logs = ['publish.log', 'packaged-assets.log', 'vsix-acceptance.log', 'dotnet-info.log'].map(name => file(directory, `${evidence}/${name}`));
  const viewsValid = ['designer', 'preview'].every(kind => {
    const view = webviews.views?.[kind];
    return view?.kind === kind && view.missingRejected === true && Array.isArray(view.errors) && view.errors.length === 0 &&
      Array.isArray(view.resources) && view.resources.some(resource => resource.name?.includes('deliberately-missing-module.js') && resource.status === 404) &&
      typeof view.text === 'string' && view.text.includes(kind === 'designer' ? 'AssetProof' : '21');
  });
  if (summary.schemaVersion !== 1 || summary.commit !== sha || summary.version !== version || summary.dirty !== false ||
      summary.rid !== platform || summary.architecture !== descriptor.architecture || summary.status !== 'Passed' ||
      summary.packageManifestSha256 !== manifestSha256 || summary.zipSha256 !== manifest.packages[`ETL-SQL-v${version}-${platform}.zip`] ||
      summary.vsixWebviewsVerified !== true || !Number.isSafeInteger(summary.vsixInstalledAssets) || summary.vsixInstalledAssets < 1 ||
      webviews.passed !== true || !Array.isArray(webviews.errors) || webviews.errors.length !== 0 ||
      webviews.version !== version || !viewsValid ||
      webviews.views?.preview?.runtime !== true || webviews.installedAssets !== summary.vsixInstalledAssets ||
      !cliLines.includes(`${version}+${sha}`) || !/^v22\.\d+\.\d+$/.test(summary.toolchain?.nodeVersion ?? '') ||
      !/^10\.\d+\.\d+$/.test(summary.toolchain?.dotnetVersion ?? '')) throw new Error('Native acceptance evidence is incomplete or mismatched.');
  if (platform === 'osx-x64') {
    const dmg = `ETL-SQL_v${version}.dmg`;
    if (summary.dmgMountedAndLaunched !== true || summary.dmgSha256 !== manifest.packages[dmg]) throw new Error('Certified Intel DMG acceptance is missing or mismatched.');
    file(directory, `release/${dmg}`);
    logs.push(file(directory, `${evidence}/dmg-acceptance.log`));
  }
  return { summary, webviews, manifestSha256, evidenceSha256: hash(JSON.stringify([summaryFile, webviewFile, cliFile, ...logs]
    .map(name => hash(readFileSync(name))))) };
}

export const verifyCertifiedWindows = (directory, sha, version) => verifyCertifiedNative(directory, sha, version, 'win-x64');

export async function collectCertifiedNative(repository, sha, version, platform, { query, readArtifact, now = Date.now(), expectedSelection } = {}) {
  nativePlatform(platform);
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version) || typeof query !== 'function' || typeof readArtifact !== 'function') throw new Error('Invalid certified native collection inputs.');
  const policy = structuredClone(certifiedWindowsPolicy);
  policy.workflows[0].jobs[1] = `Native package (${platform})`;
  const collect = () => collectHostedEvidence(repository, sha, { query, now, policy });
  const run = (await collect()).workflows[0], job = run.jobs.find(record => record.name === `Native package (${platform})`);
  const origins = run.history.find(record => record.attempt === job.attempt)?.jobs.filter(record => record.name === job.name && record.conclusion === 'success') ?? [];
  if (origins.length !== 1) throw new Error('Native package execution origin is ambiguous.');
  const endpoint = `repos/${repository}/actions/runs/${run.id}/artifacts?per_page=100`, inventory = artifacts(await query(endpoint));
  const name = `native-package-${platform}-${job.attempt}`, matches = inventory.filter(record => record.name === name);
  if (matches.length !== 1) throw new Error('The original native execution must own one native package artifact.');
  const artifact = matches[0], executions = await query(`repos/${repository}/actions/jobs/${origins[0].id}`), execution = executions?.[0];
  if (!Array.isArray(executions) || executions.length !== 1 || execution.id !== origins[0].id || execution.run_id !== run.id ||
      execution.head_sha !== sha || execution.name !== job.name || execution.status !== 'completed' || execution.conclusion !== 'success' ||
      execution.completed_at !== job.completedAt || !Number.isFinite(Date.parse(execution.started_at)) ||
      Date.parse(execution.started_at) < Date.parse(run.createdAt) || Date.parse(execution.started_at) > Date.parse(execution.completed_at) ||
      artifact.expired !== false || artifact.workflow_run?.id !== run.id || artifact.workflow_run?.head_sha !== sha ||
      !/^sha256:[a-f0-9]{64}$/.test(artifact.digest ?? '') || !Number.isFinite(Date.parse(artifact.created_at)) ||
      Date.parse(artifact.created_at) < Date.parse(execution.started_at) || Date.parse(artifact.created_at) > upper(execution.completed_at)) throw new Error('Native artifact does not belong to its successful execution.');
  const verified = await verifyCertifiedNative(await readArtifact(run, artifact), sha, version, platform);
  const timestamps = [verified.summary.startedAt, verified.summary.finishedAt, verified.webviews.startedAt,
    verified.webviews.finishedAt, verified.summary.releaseCandidateChecksPassedAt].map(Date.parse);
  if (timestamps.some(value => !Number.isFinite(value) || value < Date.parse(execution.started_at) || value > upper(execution.completed_at)) ||
      timestamps.some((value, index) => index > 0 && value < timestamps[index - 1]) || timestamps.at(-1) > upper(artifact.created_at) ||
      verified.summary.runUrl !== `https://github.com/${repository}/actions/runs/${run.id}`) throw new Error('Native Native acceptance lies outside its producing job.');
  const selection = { schemaVersion: 1, scope: platform === 'win-x64' ? 'certified-windows-native-packages' : 'certified-macos-native-packages', repository, sourceSha: sha, version,
    runId: run.id, runAttempt: run.attempt, jobId: origins[0].id, executionAttempt: job.attempt,
    artifactId: artifact.id, artifactName: artifact.name, artifactDigest: artifact.digest,
    manifestSha256: verified.manifestSha256, evidenceSha256: verified.evidenceSha256, collectedAt: new Date(now).toISOString() };
  if (platform !== 'win-x64') selection.platform = platform;
  else selection.windowsPayloadSha256 = verified.summary.windowsPayload.manifestSha256;
  if (expectedSelection) {
    sameSelection(selection, expectedSelection);
    if (!Number.isSafeInteger(expectedSelection.runAttempt) || expectedSelection.runAttempt < job.attempt || expectedSelection.runAttempt > run.attempt ||
        !Number.isFinite(Date.parse(expectedSelection.collectedAt)) || Date.parse(expectedSelection.collectedAt) < Date.parse(run.createdAt) ||
        Date.parse(expectedSelection.collectedAt) > now) throw new Error('Pinned Windows selection has an invalid attempt or collection time.');
  }
  const refreshedArtifacts = artifacts(await query(endpoint)).filter(record => record.id === artifact.id);
  if (refreshedArtifacts.length !== 1 || !isDeepStrictEqual(refreshedArtifacts[0], artifact)) throw new Error('Certified native artifact changed during selection.');
  const refreshed = (await collect()).workflows[0];
  if (refreshed.id !== run.id || refreshed.attempt !== run.attempt || !isDeepStrictEqual(refreshed.jobs, run.jobs) ||
      !isDeepStrictEqual(refreshed.history, run.history)) throw new Error('Native certification changed during selection.');
  return selection;
}

export const collectCertifiedWindows = (repository, sha, version, options) => collectCertifiedNative(repository, sha, version, 'win-x64', options);

export async function verifyNativeDraft(directory, selection, platform) {
  nativePlatform(platform);
  if (platform !== 'win-x64' && selection.platform !== platform) throw new Error('Draft native platform mismatch.');
  const attached = json(file(directory, platform === 'win-x64' ? 'certified-windows-selection.json' : `certified-${platform}-selection.json`));
  sameSelection(attached, selection);
  if (!Number.isSafeInteger(attached.runAttempt) || attached.runAttempt < selection.executionAttempt || attached.runAttempt > selection.runAttempt ||
      !Number.isFinite(Date.parse(attached.collectedAt)) || Date.parse(attached.collectedAt) > Date.parse(selection.collectedAt)) throw new Error('Draft Windows selection has an invalid attempt or collection time.');
  await verifyPackages(directory, selection.sourceSha, selection.version, platform, selection.manifestSha256);
}

export const verifyWindowsDraft = (directory, selection) => verifyNativeDraft(directory, selection, 'win-x64');

function gh(args, timeout = 60000) {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout, maxBuffer: 32 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error('Cannot read certified native evidence from GitHub.');
  return result.stdout;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2);
    if (args[0] === 'verify-draft' && args.length === 3) await verifyWindowsDraft(args[1], json(args[2]));
    else {
      if (![4, 5].includes(args.length)) throw new Error('Usage: collect-certified-windows.mjs <owner/repo> <sha> <version> <output-directory> [expected-selection.json] | verify-draft <directory> <selection.json>');
      const [repository, sha, version, directory, expected] = args;
      mkdirSync(directory, { recursive: true });
      const selection = await collectCertifiedWindows(repository, sha, version, {
        expectedSelection: expected ? json(expected) : undefined,
        query: endpoint => JSON.parse(gh(['api', '--paginate', '--slurp', endpoint])),
        readArtifact: (run, artifact) => {
          const target = path.join(directory, `${run.id}-${artifact.id}-${Date.now()}`);
          mkdirSync(target);
          gh(['run', 'download', String(run.id), '--repo', repository, '--name', artifact.name, '--dir', target], 600000);
          return target;
        },
      });
      writeFileSync(path.join(directory, 'certified-windows-selection.json'), `${JSON.stringify(selection, null, 2)}\n`);
      if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT,
        `artifact_id=${selection.artifactId}\nrun_id=${selection.runId}\nmanifest_sha256=${selection.manifestSha256}\n`);
      console.log(`Certified native run ${selection.runId}, original job ${selection.jobId}, artifact ${selection.artifactId}; package bytes verified.`);
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
