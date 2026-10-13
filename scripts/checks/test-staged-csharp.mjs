import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { checkStagedCSharp } from '../check-staged-csharp.mjs';

const name = 'src/example with spaces.cs';
const staged = Buffer.from('namespace Example;\n\npublic sealed class Staged;\n');
const config = Buffer.from('root = true\n\n[*.cs]\nindent_size = 4\n');
const entries = `100644 ${'a'.repeat(40)} 0\t${name}\0` + `100644 ${'b'.repeat(40)} 0\t.editorconfig\0`;
function git(args) {
  if (args[0] === 'diff') return Buffer.from(name + '\0');
  if (args[0] === 'ls-files') return Buffer.from(entries);
  if (args[0] === 'show') return args[1] === ':.editorconfig' ? config : staged;
  throw new Error('Unexpected Git command');
}
let snapshot;
const result = checkStagedCSharp('.', { git, format(directory, files) {
  snapshot = directory;
  assert.deepEqual(files, [name]);
  assert.deepEqual(fs.readFileSync(path.join(directory, name)), staged);
  assert.deepEqual(fs.readFileSync(path.join(directory, '.editorconfig')), config);
  return true;
} });
assert.deepEqual(result, { files: 1, clean: true });
assert.equal(fs.existsSync(snapshot), false);
assert.deepEqual(checkStagedCSharp('.', { git, format: () => false }), { files: 1, clean: false });
assert.throws(() => checkStagedCSharp('.', { git, format(directory) {
  snapshot = directory;
  throw new Error('Formatter unavailable');
} }), /Formatter unavailable/);
assert.equal(fs.existsSync(snapshot), false);
assert.deepEqual(checkStagedCSharp('.', { git: () => Buffer.from(''), format: () => assert.fail('Empty index must not start formatter') }), { files: 0, clean: true });
assert.throws(() => checkStagedCSharp('.', { git: args => args[0] === 'ls-files'
  ? Buffer.from(entries.replace('100644', '120000')) : git(args), format: () => assert.fail('Symlink must be rejected') }), /regular/);
assert.throws(() => checkStagedCSharp('.', { git: args => args[0] === 'show' ? staged
  : args[0] === 'ls-files' ? Buffer.from(`100644 ${'a'.repeat(40)} 0\t../escape.cs\0`) : Buffer.from('../escape.cs\0'),
format: () => assert.fail('Traversal must be rejected') }), /escapes/);
let indexReads = 0;
assert.throws(() => checkStagedCSharp('.', { git: args => args[0] === 'ls-files' && ++indexReads > 1
  ? Buffer.from(entries.replace('a'.repeat(40), 'c'.repeat(40))) : git(args), format: () => true }), /changed during/);
console.log('staged C# formatting: index-only sources/configuration, spaced paths, failure cleanup, unsafe modes/paths, and concurrent index changes passed');
