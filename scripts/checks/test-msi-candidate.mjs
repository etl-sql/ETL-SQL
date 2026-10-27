// Run installer artifact-boundary checks in the shared local/CI contract gate.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

for (const name of ['Test-MsiCandidate.ps1', 'Test-MsiReuse.ps1', 'Test-FindCertifiedMsi.ps1', 'Test-MsiImpact.ps1', 'Test-MsiCheckpoint.ps1']) {
  const script = fileURLToPath(new URL(name, import.meta.url));
  const result = spawnSync('pwsh', ['-NoProfile', '-File', script], { stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
