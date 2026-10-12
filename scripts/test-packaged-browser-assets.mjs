// scripts/verify-packaged-browser-assets.mjs against a hand-built checkout and published folder.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { packagedAssetProblems } from './verify-packaged-browser-assets.mjs';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-packaged-'));
const published = path.join(root, 'published');
const shared = 'src/ETL-SQL.ReportRuntime/Resources/Shared/rt-util.js';
const portal = 'src/ETL-SQL.Portal/wwwroot/js/rt-util.js';
const player = 'src/ETL-SQL.ReportPlayer/wwwroot/rt-util.js';
const media = 'src/etl-sql-vscode/media/rt-util.js';
const bytes = { [shared]: 'export const a = 1;\n', [portal]: '// portal\nexport const a = 1;\n',
    [player]: '// player\nexport const a = 1;\n', [media]: '// media\nexport const a = 1;\n' };
const sha = content => createHash('sha256').update(content).digest('hex');
const write = (file, content) => {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, content);
};
const check = () => packagedAssetProblems(published, { root });

try {
    write(path.join(root, 'artifacts/browser-assets.json'), JSON.stringify({ version: 2, outputs: [
        { path: media, kind: 'host-copy', source: shared, sha256: sha(bytes[media]) },
        { path: portal, kind: 'host-copy', source: shared, sha256: sha(bytes[portal]) },
        { path: player, kind: 'host-copy', source: shared, sha256: sha(bytes[player]) },
        { path: shared, kind: 'typescript', source: 'src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-util.ts', sha256: sha(bytes[shared]) },
    ] }));

    assert.match(check().problems[0], /has no wwwroot/, 'A folder with no web root is not a published host.');

    write(path.join(published, 'wwwroot/js/rt-util.js'), bytes[portal]);
    write(path.join(published, 'wwwroot/rt-util.js'), bytes[player]);
    write(path.join(published, 'wwwroot/css/portal.css'), 'body{}\n');
    let result = check();
    assert.deepEqual(result.problems, []);
    assert.equal(result.checked, 2, 'Only the hosts served from wwwroot are expected: not the canonical file, not the extension media.');

    fs.rmSync(path.join(published, 'wwwroot/rt-util.js'));
    assert.match(check().problems.join('\n'), /Missing from the package: wwwroot\/rt-util\.js/);
    write(path.join(published, 'wwwroot/rt-util.js'), bytes[player]);

    write(path.join(published, 'wwwroot/js/rt-util.js'), bytes[portal] + '// older build\n');
    assert.match(check().problems.join('\n'), /Differs from the prepared file: wwwroot\/js\/rt-util\.js/, 'A complete but older asset set must not pass.');
    write(path.join(published, 'wwwroot/js/rt-util.js'), bytes[portal]);

    for (const stray of ['js/rt-util.ts', 'js/rt-util.js.map', 'js/rt-util.__test-1.mjs', 'browser-assets.json']) {
        write(path.join(published, 'wwwroot', stray), 'x');
        assert.match(check().problems.join('\n'), /Development file in the package/, `${stray} must not ship.`);
        fs.rmSync(path.join(published, 'wwwroot', stray));
    }
    assert.deepEqual(check().problems, []);

    fs.rmSync(path.join(root, 'artifacts/browser-assets.json'));
    assert.throws(check, /inventory is missing/);
    console.log('packaged browser asset checks passed');
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}
