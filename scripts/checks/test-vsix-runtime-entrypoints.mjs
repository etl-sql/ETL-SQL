import assert from 'node:assert/strict';
import { dependencyEntryPointProblems } from '../lib/vsix-package.mjs';

const metadata = new Map([
    ['extension/node_modules/vscode-languageserver-types/package.json', JSON.stringify({ main: './lib/umd/main.js' })],
    ['extension/node_modules/@scope/tool/package.json', JSON.stringify({ main: 'lib' })],
    ['extension/node_modules/parent/node_modules/child/package.json', JSON.stringify({ main: './index' })],
    ['extension/node_modules/data/package.json', JSON.stringify({ name: 'data' })],
    ['extension/node_modules/root/package.json', JSON.stringify({ main: '.' })],
]);
const files = new Set([
    'extension/node_modules/vscode-languageserver-types/lib/umd/main.js',
    'extension/node_modules/@scope/tool/lib/index.js',
    'extension/node_modules/parent/node_modules/child/index.js',
    'extension/node_modules/root/index.js',
]);
assert.deepEqual(dependencyEntryPointProblems(metadata, files), []);
files.delete('extension/node_modules/vscode-languageserver-types/lib/umd/main.js');
assert.deepEqual(dependencyEntryPointProblems(metadata, files), [
    'Missing VSIX dependency entry point: extension/node_modules/vscode-languageserver-types/package.json -> ./lib/umd/main.js.',
]);
files.add('extension/node_modules/vscode-languageserver-types/lib/umd/Main.js');
assert.equal(dependencyEntryPointProblems(metadata, files).length, 1, 'Entry paths must match case on every platform.');
metadata.set('extension/node_modules/escape/package.json', JSON.stringify({ main: '../@scope/tool/lib/index.js' }));
assert.equal(dependencyEntryPointProblems(metadata, files).length, 2, 'An escaped entry must not certify another dependency.');
console.log('VSIX dependency entry points: missing UMD, scoped, nested, directory and case checks passed.');
