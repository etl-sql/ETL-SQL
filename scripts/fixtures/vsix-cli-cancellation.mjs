import fs from 'node:fs';
import path from 'node:path';

// A real npm lifecycle holds the first restore while the public coordinator receives a signal.
// No registry dependency, compiler, or product process is needed to reach that boundary.
export function createVsixCancellationFixture(repository, root) {
    fs.mkdirSync(root, { recursive: true });
    const write = (relative, bytes) => {
        const file = path.join(root, relative);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, bytes);
        return file;
    };
    for (const directory of ['src/etl-sql-vscode/src', 'src/etl-sql-vscode/resources', 'src/etl-sql-vscode/syntaxes',
        'src/etl-sql-vscode/snippets', 'src/etl-sql-vscode/ui', 'tools/package-inspector', 'scripts/vsix',
        'build/signing', 'deploy', 'capacity-results/workloads']) fs.mkdirSync(path.join(root, directory), { recursive: true });
    const identity = { name: 'etl-sql-vscode', publisher: 'etl-sql', version: '0.21.0' };
    write('Directory.Build.props', '<Project><PropertyGroup><VersionPrefix>0.21.0</VersionPrefix></PropertyGroup></Project>\n');
    write('src/etl-sql-vscode/package.json', JSON.stringify(identity));
    write('src/etl-sql-vscode/package-lock.json', JSON.stringify({ version: identity.version, packages: { '': { version: identity.version } } }));
    const manifest = { name: 'etlsql-cancellation-fixture', version: '1.0.0', private: true, scripts: { preinstall: 'node hold.mjs' } };
    write('scripts/typecheck/package.json', JSON.stringify(manifest));
    write('scripts/typecheck/package-lock.json', JSON.stringify({ name: manifest.name, version: manifest.version,
        lockfileVersion: 3, requires: true, packages: { '': { name: manifest.name, version: manifest.version, hasInstallScript: true } } }));
    write('scripts/typecheck/heartbeat.mjs', `import fs from 'node:fs';
setInterval(() => fs.writeFileSync('heartbeat.txt', String(Date.now())), 25);
`);
    write('scripts/typecheck/hold.mjs', `import fs from 'node:fs';
import { spawn } from 'node:child_process';
const child = spawn(process.execPath, ['heartbeat.mjs'], { stdio: 'ignore' });
fs.writeFileSync('pids.json', JSON.stringify([process.pid, child.pid]));
setInterval(() => {}, 1000);
`);
    for (const relative of ['scripts/package-vsix.mjs', 'scripts/build-vsix.ps1', 'scripts/build-vsix.sh',
        'scripts/lib/browser-asset-inputs.cjs', 'scripts/lib/vsix-toolchain.cjs', 'scripts/lib/vsix-package.mjs',
        'scripts/lib/vsix-executable-modes.mjs', 'scripts/lib/node-command-environment.mjs', 'scripts/lib/run-owned-command.mjs'])
        write(relative, fs.readFileSync(path.join(repository, relative)));
    const outputDirectory = path.join(root, 'accepted');
    const accepted = write(`accepted/${identity.name}-${identity.version}-win32-x64.vsix`, 'previous accepted package');
    return { root, identity, outputDirectory, accepted, pids: path.join(root, 'scripts/typecheck/pids.json'),
        heartbeat: path.join(root, 'scripts/typecheck/heartbeat.txt'), lock: path.join(root, 'artifacts/vsix-package.lock') };
}
