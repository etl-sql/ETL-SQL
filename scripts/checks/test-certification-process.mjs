import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./Test-CertificationProcess.ps1', import.meta.url));
const result = spawnSync('pwsh', ['-NoProfile', '-File', script], { stdio: 'inherit' });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
