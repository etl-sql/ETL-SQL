import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Check the index in isolation. Never format or restage somebody's working-tree edits.
// The full solution's style/analyzer formatting gate remains in Test-PrePush.ps1.
export function checkStagedCSharp(root, options = {}) {
  const git = options.git ?? (args => {
    const result = spawnSync('git', args, { cwd: root });
    if (result.status !== 0) throw new Error(`Cannot read staged C# sources: ${result.stderr?.toString().trim() || result.error?.message || 'Git failed.'}`);
    return result.stdout;
  });
  const changed = () => git(['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z', '--', '*.cs']).toString();
  const names = changed(), files = names.split('\0').filter(Boolean);
  if (!files.length) return { files: 0, clean: true };
  const configs = new Set(['.editorconfig']);
  for (const file of files) {
    if (path.posix.isAbsolute(file) || path.win32.isAbsolute(file) || file.split('/').includes('..'))
      throw new Error('Staged source path escapes the formatting snapshot.');
    let directory = path.posix.dirname(file);
    while (directory !== '.') { configs.add(directory + '/.editorconfig'); directory = path.posix.dirname(directory); }
  }
  const indexArgs = ['ls-files', '--stage', '-z', '--', ...files, ...configs];
  const entries = git(indexArgs).toString();
  const temporaryBase = path.resolve(os.tmpdir());
  const temporaryRoot = fs.mkdtempSync(path.join(temporaryBase, 'etlsql-staged-csharp-'));
  try {
    for (const entry of entries.split('\0').filter(Boolean)) {
      const match = /^(\d+) ([0-9a-f]+) ([0-3])\t(.+)$/s.exec(entry);
      if (!match || match[3] !== '0' || !['100644', '100755'].includes(match[1]))
        throw new Error('Staged C# or EditorConfig input is not a regular, resolved file.');
      const relative = match[4], target = path.resolve(temporaryRoot, relative);
      if (!target.startsWith(temporaryRoot + path.sep)) throw new Error('Staged source path escapes the formatting snapshot.');
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.writeFileSync(target, git(['show', ':' + relative]));
    }
    for (const file of files) {
      if (!fs.existsSync(path.join(temporaryRoot, file))) throw new Error('A staged C# input is missing from the snapshot.');
    }
    const format = options.format ?? ((directory, includes) => {
      const normalize = text => text.replace(/\r\n/g, '\n');
      const before = new Map(includes.map(file => [file, normalize(fs.readFileSync(path.join(directory, file), 'utf8'))]));
      const result = spawnSync('dotnet', ['format', 'whitespace', directory, '--folder',
        '--include', ...includes, '--verbosity', 'quiet'], { encoding: 'utf8' });
      if (result.stdout) process.stdout.write(result.stdout);
      if (result.stderr) process.stderr.write(result.stderr);
      if (result.error) throw result.error;
      if (result.status !== 0) return false;
      // Git stores normalized text; checkout CRLF must not look like a formatting defect.
      const changed = includes.filter(file => before.get(file) !== normalize(fs.readFileSync(path.join(directory, file), 'utf8')));
      for (const file of changed) console.error(`Staged C# whitespace needs formatting: ${file}`);
      return changed.length === 0;
    });
    const clean = format(temporaryRoot, files);
    if (names !== changed() || entries !== git(indexArgs).toString())
      throw new Error('Staged sources changed during formatting verification. Retry the commit.');
    return { files: files.length, clean };
  } finally {
    if (path.dirname(path.resolve(temporaryRoot)) !== temporaryBase
      || !path.basename(temporaryRoot).startsWith('etlsql-staged-csharp-'))
      throw new Error('Refusing to remove an unexpected formatting snapshot path.');
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
    const result = checkStagedCSharp(root);
    if (!result.clean) {
      console.error('Staged C# whitespace needs formatting. Format the intended source, review its diff, and stage the corrections. Working files and the index were preserved.');
      process.exitCode = 1;
    } else if (result.files) console.log(`Staged C# whitespace: ${result.files} files verified; working-tree edits preserved.`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : 'Staged formatting verification failed.');
    process.exitCode = 1;
  }
}
