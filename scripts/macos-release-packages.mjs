import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { packageManifest } from './release-package-manifest.mjs';

export async function macosPackages(mode, directory, sourceSha, version, platform, digestOrOption) {
  if (!['osx-x64', 'osx-arm64'].includes(platform)) throw new Error('Invalid macOS package platform.');
  if (mode === 'create' && digestOrOption !== undefined &&
      (digestOrOption !== '--include-dmg' || platform !== 'osx-x64')) throw new Error('Only Intel packages can include the optional DMG.');
  const target = platform === 'osx-x64' ? 'darwin-x64' : 'darwin-arm64';
  await packageManifest(mode, directory, sourceSha, version, {
    platform, manifestName: `${platform}-package-inputs.json`,
    required: [`ETL-SQL-v${version}-${platform}.zip`, `etl-sql-vscode-${version}-${target}.vsix`],
    optional: platform === 'osx-x64' ? [`ETL-SQL_v${version}.dmg`] : [],
    includeOptional: digestOrOption === '--include-dmg',
  }, mode === 'verify' ? digestOrOption : undefined);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (!['create', 'verify'].includes(process.argv[2]) ||
        (process.argv[2] === 'create' && ![7, 8].includes(process.argv.length)) ||
        (process.argv[2] === 'verify' && process.argv.length !== 8)) {
      throw new Error('Usage: macos-release-packages.mjs create <directory> <sha> <version> <rid> [--include-dmg] | verify <directory> <sha> <version> <rid> <manifest-sha256>');
    }
    await macosPackages(...process.argv.slice(2));
    console.log('macOS package identity and hashes verified.');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
