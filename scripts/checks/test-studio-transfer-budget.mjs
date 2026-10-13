import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes } from 'node:crypto';
import { measureStudioTransfer, resolveStudioAsset } from '../measure-studio-transfer.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const budgets = JSON.parse(fs.readFileSync(path.join(root, 'docs/benchmarks/studio-transfer-budgets.json'), 'utf8'));
function enforce(measurement) {
    for (const [host, ceiling] of Object.entries(budgets.hosts)) {
        const actual = measurement.hosts[host];
        for (const metric of ['rawBytes', 'gzipBytes']) {
            assert.ok(actual[metric] <= ceiling[metric],
                `Studio ${host} ${metric}: ${actual[metric]} exceeds accepted ${ceiling[metric]}`);
        }
    }
}
const baseline = measureStudioTransfer();
enforce(baseline);
assert.ok(baseline.hosts.portal.assets.some(asset => asset.url.endsWith('/codemirror-bundle.min.js')));

// Exercise the actual walker on an isolated oversized module, without modifying prepared assets.
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-studio-budget-'));
try {
    const files = new Set([
        'src/ETL-SQL.Portal/wwwroot/studio.html', 'src/ETL-SQL.WorkstationEditor/StudioShell.cs',
        ...Object.values(baseline.hosts).flatMap(host => host.assets.map(asset => path.relative(root, resolveStudioAsset(asset.url))))
    ]);
    for (const relative of files) {
        const target = path.join(temporary, relative);
        fs.mkdirSync(path.dirname(target), { recursive: true });
        fs.copyFileSync(path.join(root, relative), target);
    }
    fs.appendFileSync(resolveStudioAsset('/designer/studio.js', temporary),
        `\n/* oversized fixture ${randomBytes(1_000_000).toString('hex')} */\n`);
    const oversized = measureStudioTransfer(temporary);
    assert.throws(() => enforce(oversized), /exceeds accepted/);
    const gzipOnly = structuredClone(oversized);
    for (const [host, result] of Object.entries(gzipOnly.hosts)) result.rawBytes = baseline.hosts[host].rawBytes;
    assert.throws(() => enforce(gzipOnly), /gzipBytes.*exceeds accepted/);
    assert.ok(oversized.hosts.portal.gzipBytes > budgets.hosts.portal.gzipBytes);
    assert.ok(oversized.hosts.desktop.gzipBytes > budgets.hosts.desktop.gzipBytes);
} finally {
    fs.rmSync(temporary, { recursive: true, force: true });
}
console.log('Studio transfer budgets passed; oversized production-closure fixture rejected.');
