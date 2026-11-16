import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { packageManifest } from './release-package-manifest.mjs';

// This records build inputs and bytes, not installed-package certification.
export async function windowsPackages(mode, directory, sourceSha, version, manifestSha256) {
  await packageManifest(mode, directory, sourceSha, version, {
    platform: 'win-x64', manifestName: 'windows-package-inputs.json',
    required: [`ETL-SQL-v${version}-win-x64.zip`, `etl-sql-vscode-${version}-win32-x64.vsix`],
  }, manifestSha256);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if ((process.argv[2] === 'create' && process.argv.length !== 6) ||
        (process.argv[2] === 'verify' && process.argv.length !== 7) || !['create', 'verify'].includes(process.argv[2])) {
      throw new Error('Usage: windows-release-packages.mjs create <directory> <source-sha> <version> | verify <directory> <source-sha> <version> <manifest-sha256>');
    }
    await windowsPackages(...process.argv.slice(2));
    console.log('Windows package identity and hashes verified.');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
