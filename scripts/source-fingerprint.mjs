import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..'));
const git = args => {
    const result = spawnSync('git', args, { cwd: root, maxBuffer: 64 * 1024 * 1024 });
    if (result.error || result.status !== 0) throw new Error('Cannot fingerprint the candidate Git sources.');
    return result.stdout;
};

try {
    const digest = createHash('sha256');
    const add = bytes => digest.update(`${bytes.length}:`).update(bytes);
    add(git(['rev-parse', 'HEAD']));
    add(git(['diff', '--binary', 'HEAD', '--']));
    const untracked = git(['ls-files', '--others', '--exclude-standard', '-z']).toString('utf8').split('\0').filter(Boolean);
    for (const relative of untracked) {
        const file = path.resolve(root, relative);
        if (!file.startsWith(root + path.sep)) throw new Error('Candidate source path escapes the repository.');
        add(Buffer.from(relative));
        const stat = fs.lstatSync(file);
        // Hash the link itself; fingerprinting must not read its external target.
        add(stat.isSymbolicLink() ? Buffer.from(fs.readlinkSync(file)) : fs.readFileSync(file));
    }
    console.log(digest.digest('hex'));
} catch (error) {
    console.error(error.message);
    process.exitCode = 1;
}
