#!/usr/bin/env node

import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';

const explicitFiles = process.argv.slice(2);
const files = explicitFiles.length > 0
  ? explicitFiles
  : [
      'capacity-results/workload.example.json',
      'capacity-results/reference-local/workload.sanitized.json',
      ...await findJsonFiles('capacity-results/workloads')
    ];

for (const file of files) {
  await runNode(['scripts/test-service-capacity.mjs', '--config', file, '--validate-only']);
}

console.log(`Validated ${files.length} capacity workload configuration(s).`);

async function findJsonFiles(directory) {
  const entries = await fs.readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await findJsonFiles(fullPath));
    else if (entry.isFile() && entry.name.endsWith('.json')) files.push(fullPath);
  }
  return files.sort();
}

function runNode(argumentsList) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, argumentsList, { cwd: path.resolve('.'), stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve() : reject(new Error(`Child process exited with code ${code}.`)));
  });
}
