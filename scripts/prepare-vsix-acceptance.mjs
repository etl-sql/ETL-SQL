import fs from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { prepareVsixTestHost } from './lib/vsix-test-host.mjs';

if (process.argv.length !== 4 || process.argv[2] !== '--out') throw new Error('Usage: node scripts/prepare-vsix-acceptance.mjs --out <receipt>');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const { downloadAndUnzipVSCode } = require(path.join(root, 'src/etl-sql-vscode/node_modules/@vscode/test-electron'));
const receipt = await prepareVsixTestHost(root, downloadAndUnzipVSCode);
const output = path.resolve(process.argv[3]);
await fs.mkdir(path.dirname(output), { recursive: true });
await fs.writeFile(output, JSON.stringify(receipt, null, 2) + '\n');
console.log('VS Code acceptance host prepared before native publishing.');
