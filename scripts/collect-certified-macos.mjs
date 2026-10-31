import { spawnSync } from 'node:child_process';
import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { collectCertifiedNative, verifyNativeDraft } from './collect-certified-windows.mjs';

const json = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
function gh(args, timeout = 60000) {
  const result = spawnSync('gh', args, { encoding: 'utf8', windowsHide: true, timeout, maxBuffer: 32 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error('Cannot read certified macOS native evidence from GitHub.');
  return result.stdout;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2);
    if (args[0] === 'verify-draft' && args.length === 4) await verifyNativeDraft(args[1], json(args[2]), args[3]);
    else {
      if (![5, 6].includes(args.length)) throw new Error('Usage: collect-certified-macos.mjs <owner/repo> <sha> <version> <rid> <output-directory> [expected-selection.json] | verify-draft <directory> <selection.json> <rid>');
      const [repository, sha, version, platform, directory, expected] = args;
      if (!['osx-x64', 'osx-arm64'].includes(platform)) throw new Error('Expected a macOS native platform.');
      mkdirSync(directory, { recursive: true });
      const selection = await collectCertifiedNative(repository, sha, version, platform, {
        expectedSelection: expected ? json(expected) : undefined,
        query: endpoint => JSON.parse(gh(['api', '--paginate', '--slurp', endpoint])),
        readArtifact: (run, artifact) => {
          const target = path.join(directory, `${run.id}-${artifact.id}-${Date.now()}`);
          mkdirSync(target);
          gh(['run', 'download', String(run.id), '--repo', repository, '--name', artifact.name, '--dir', target], 600000);
          return target;
        },
      });
      writeFileSync(path.join(directory, `certified-${platform}-selection.json`), `${JSON.stringify(selection, null, 2)}\n`);
      if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT,
        `artifact_id=${selection.artifactId}\nrun_id=${selection.runId}\nmanifest_sha256=${selection.manifestSha256}\n`);
      console.log(`Certified ${platform} run ${selection.runId}, original job ${selection.jobId}, artifact ${selection.artifactId}; package bytes verified.`);
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
