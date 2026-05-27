import { describe, it } from 'vitest';
import * as cp from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

function getEnginePath(): string | null {
    const isWindows = process.platform === 'win32';
    const exeName = isWindows ? 'ETL-SQL.exe' : 'ETL-SQL';
    const relativeBase = '../../../../src/ETL-SQL.App/bin';
    
    const configs = ['Debug', 'Release'];
    const frameworks = ['net10.0'];

    for (const config of configs) {
        for (const framework of frameworks) {
            const fullPath = path.resolve(__dirname, relativeBase, config, framework, exeName);
            if (fs.existsSync(fullPath)) {
                return fullPath;
            }
        }
    }

    return null;
}

describe('Engine Integration (Real Pipe)', () => {
    const exePath = getEnginePath();

    it('successfully pings the real engine via stdin/stdout', async () => {
        if (!exePath) {
            console.warn('Skipping integration test: ETL-SQL engine not found. Build the ETL-SQL.App project first.');
            return;
        }

        return new Promise<void>((resolve, reject) => {
            const child = cp.spawn(exePath, ['ui', 'repl', '--json', '--verbose'], {
                env: { ...process.env, "FORCE_COLOR": "0" },
                stdio: ['pipe', 'pipe', 'pipe']
            });

            let buffer = '';
            let ready = false;
            let pongReceived = false;

            const timeout = setTimeout(() => {
                child.kill();
                reject(new Error('Integration test timed out after 10s'));
            }, 10000);

            child.stdout.on('data', (data) => {
                const text = data.toString().trim();
                if (text) { console.log(`[INTEGRATION STDOUT] ${text}`); }
                buffer += data.toString();
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    if (!line.trim()) { continue; }
                    try {
                        const msg = JSON.parse(line);
                        if (msg.type === 'status' && msg.status === 'ready') {
                            ready = true;
                            // Send Ping
                            console.log(`[INTEGRATION] Sending PING...`);
                            child.stdin.write(JSON.stringify({ Action: 'ping' }) + '\r\n');
                        }
                        if (msg.type === 'pong') {
                            console.log(`[INTEGRATION] PONG received!`);
                            pongReceived = true;
                            child.stdin.write(JSON.stringify({ Action: 'exit' }) + '\r\n');
                        }
                    } catch {
                        // Startup noise
                    }
                }
            });

            child.stderr.on('data', (data) => {
                const text = data.toString().trim();
                if (text) { console.log(`[INTEGRATION STDERR] ${text}`); }
            });

            child.on('close', (code) => {
                clearTimeout(timeout);
                if (ready && pongReceived) {
                    resolve();
                } else {
                    reject(new Error(`Engine exited with code ${code}. Ready: ${ready}, Pong: ${pongReceived}`));
                }
            });
        });
    });

    it('executes a simple MOCKDB query on the real engine', async () => {
        if (!exePath) {
            console.warn('Skipping query integration test: ETL-SQL engine not found.');
            return;
        }

        return new Promise<void>((resolve, reject) => {
            const child = cp.spawn(exePath, ['ui', 'repl', '--json'], {
                env: { ...process.env, "FORCE_COLOR": "0" },
                stdio: ['pipe', 'pipe', 'pipe']
            });

            let buffer = '';
            let doneReceived = false;

            const timeout = setTimeout(() => {
                child.kill();
                reject(new Error('Query integration test timed out after 15s'));
            }, 15000);

            child.stdout.on('data', (data) => {
                buffer += data.toString();
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    try {
                        const msg = JSON.parse(line);
                        if (msg.type === 'status' && msg.status === 'ready') {
                            const cmd = {
                                Action: 'run',
                                Script: 'CREATE CONNECTION m AS MOCKDB(); SELECT 1 as val;'
                            };
                            child.stdin.write(JSON.stringify(cmd) + '\r\n');
                        }
                        if (msg.type === 'done') {
                            doneReceived = true;
                            child.stdin.write(JSON.stringify({ Action: 'exit' }) + '\r\n');
                        }
                    } catch {
                        // ignore startup noise
                    }
                }
            });

            child.on('close', () => {
                clearTimeout(timeout);
                if (doneReceived) {
                    resolve();
                } else {
                    reject(new Error(`Query failed. Done received: ${doneReceived}`));
                }
            });
        });
    });
});
