const { spawn } = require('child_process');
const path = require('path');

/**
 * Cross-platform wrapper to run the sync-assets.ps1 script.
 * Detects whether to use 'powershell' (Windows) or 'pwsh' (Linux/macOS).
 */

const isWin = process.platform === 'win32';
const shell = isWin ? 'powershell' : 'pwsh';
const scriptPath = path.join(__dirname, 'sync-assets.ps1');

console.log(`Running sync-assets via ${shell}...`);

const args = [
    '-ExecutionPolicy', 'Bypass',
    '-NoProfile',
    '-File', scriptPath
];

// Pass through any arguments (like -Check)
args.push(...process.argv.slice(2));

const child = spawn(shell, args, { stdio: 'inherit' });

child.on('exit', (code) => {
    if (code !== 0) {
        console.error(`sync-assets.ps1 exited with code ${code}`);
    }
    process.exit(code || 0);
});

child.on('error', (err) => {
    if (err.code === 'ENOENT') {
        console.error(`Error: '${shell}' not found. Please ensure PowerShell is installed.`);
        if (!isWin) {
            console.error("On Linux/macOS, you may need to install 'pwsh' (PowerShell Core).");
        }
    } else {
        console.error(`Failed to start child process: ${err.message}`);
    }
    process.exit(1);
});
